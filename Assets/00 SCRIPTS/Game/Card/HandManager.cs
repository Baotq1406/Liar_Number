using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using DG.Tweening;

public class HandManager : MonoBehaviour
{
    [Header("Card Setup")]
    [SerializeField] private int maxHandSize = 6;
    [SerializeField] private int initialHandSize = 6;
    [SerializeField] private bool dealOnStart = true;
    [SerializeField] private float initialDealInterval = 0.12f;
    [SerializeField] private bool useAuthoritativeHandOnStart = true;
    [SerializeField] private bool useDealAnimation = true;
    [SerializeField] private float dealAnimationDuration = 0.2f;
    [SerializeField] private Vector3 dealSpawnOffset = new Vector3(2.8f, 0f, 0f);
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private SplineContainer splineContainer;
    [SerializeField] private Transform handAnchor;
    [SerializeField] private Vector3 handLocalOffset = new Vector3(0f, 0.8f, 0f);

    [Header("Debug Input")]
    [SerializeField] private bool drawByKeyboard = false;
    [SerializeField] private KeyCode drawKey = KeyCode.Space;

    [Header("Fallback (No Spline)")]
    [SerializeField] private float fallbackCardSpacing = 0.6f;

    [Header("Selection UI")]
    [SerializeField] private GameObject selectionCardButtom;
    [SerializeField] private int maxSelectedCards = 3;

    private readonly List<GameObject> handCards = new List<GameObject>();
    private readonly HashSet<CardInteraction> selectedCards = new HashSet<CardInteraction>();
    private readonly HashSet<GameObject> pendingDealCards = new HashSet<GameObject>();
    private readonly Dictionary<GameObject, Tween> dealTweens = new Dictionary<GameObject, Tween>();
    private bool _isAdjustingSelection;
    private int _lastAppliedRoundResetPlayId;

    private void Awake()
    {
        UpdateSelectionCardButtom();
    }

    private void Start()
    {
        if (useAuthoritativeHandOnStart && TryApplyAuthoritativeLocalHand())
        {
            return;
        }

        if (dealOnStart)
        {
            StartCoroutine(DealInitialHandRoutine());
            return;
        }

        DrawInitialHand();
    }

    private void OnEnable()
    {
        CardInteraction.SelectionChanged += OnCardSelectionChanged;
        GameManager.GameInfoChanged += OnGameInfoChanged;
    }

    private void OnDisable()
    {
        CardInteraction.SelectionChanged -= OnCardSelectionChanged;
        GameManager.GameInfoChanged -= OnGameInfoChanged;
    }

    private void Update()
    {
        if (drawByKeyboard && Input.GetKeyDown(drawKey))
        {
            DrawCard();
        }
    }

    private IEnumerator DealInitialHandRoutine()
    {
        int cardCount = Mathf.Clamp(initialHandSize, 0, maxHandSize);

        for (int i = 0; i < cardCount; i++)
        {
            DrawCard();

            if (i < cardCount - 1 && initialDealInterval > 0f)
            {
                yield return new WaitForSeconds(initialDealInterval);
            }
        }
    }

    public void DrawCard()
    {
        DrawCard(-1);
    }

    public void DrawCard(int cardValue)
    {
        if (cardPrefab == null)
        {
            Debug.LogWarning("[HandManager] Card Prefab chua duoc gan.");
            return;
        }

        if (handCards.Count >= maxHandSize)
        {
            return;
        }

        var point = GetLayoutAnchor();
        var spawnOffset = handLocalOffset;
        if (useDealAnimation)
        {
            spawnOffset += dealSpawnOffset;
        }

        var spawnWorldPos = point.TransformPoint(spawnOffset);
        var card = Instantiate(cardPrefab, spawnWorldPos, point.rotation, point);
        if (cardValue >= 0)
        {
            var normalizedCardValue = GameManager.NormalizeCardValue(cardValue);
            card.name = cardPrefab.name + "_" + normalizedCardValue;

            var cardVisual = card.GetComponent<CardVisual>();
            if (cardVisual == null)
            {
                cardVisual = card.GetComponentInChildren<CardVisual>(true);
            }

            if (cardVisual != null)
            {
                cardVisual.SetCardValue(normalizedCardValue);
            }
            else
            {
                Debug.LogWarning("[HandManager] Card prefab khong co CardVisual de hien thi cardValue=" + normalizedCardValue);
            }
        }

        if (useDealAnimation)
        {
            pendingDealCards.Add(card);
        }

        handCards.Add(card);

        UpdateCardPositions();
    }

    private void DrawInitialHand()
    {
        int cardCount = Mathf.Clamp(initialHandSize, 0, maxHandSize);

        for (int i = 0; i < cardCount; i++)
        {
            DrawCard();
        }
    }

    private bool TryApplyAuthoritativeLocalHand()
    {
        if (GameManager.Instant == null)
        {
            return false;
        }

        var authoritativeCards = GameManager.Instant.LocalHandCards;
        if (authoritativeCards == null || authoritativeCards.Count == 0)
        {
            return false;
        }

        ClearHand();

        for (int i = 0; i < authoritativeCards.Count; i++)
        {
            DrawCard(GameManager.NormalizeCardValue(authoritativeCards[i]));
        }

        Debug.Log("[HandManager] Da ap dung local hand authoritative. count=" + authoritativeCards.Count);
        return true;
    }

    private void OnGameInfoChanged()
    {
        var gameManager = GameManager.Instant;
        if (gameManager == null)
        {
            return;
        }

        var latestResetPlayId = gameManager.LatestRoundResetPlayId;
        if (latestResetPlayId <= 0 || latestResetPlayId == _lastAppliedRoundResetPlayId)
        {
            return;
        }

        if (TryApplyAuthoritativeLocalHand())
        {
            _lastAppliedRoundResetPlayId = latestResetPlayId;
            return;
        }

        if (gameManager.IsLocalPlayerDead)
        {
            ClearHand();
            _lastAppliedRoundResetPlayId = latestResetPlayId;
        }
    }

    public void RemoveLastCard()
    {
        if (handCards.Count == 0)
        {
            return;
        }

        var last = handCards[handCards.Count - 1];
        handCards.RemoveAt(handCards.Count - 1);
        KillDealTweenForCard(last);

        if (last != null)
        {
            var interaction = last.GetComponent<CardInteraction>();
            if (interaction != null)
            {
                selectedCards.Remove(interaction);
            }
        }

        if (last != null)
        {
            Destroy(last);
        }

        UpdateSelectionCardButtom();
        UpdateCardPositions();
    }

    public void ClearHand()
    {
        KillAllDealTweens();

        for (int i = 0; i < handCards.Count; i++)
        {
            if (handCards[i] != null)
            {
                Destroy(handCards[i]);
            }
        }

        handCards.Clear();
        selectedCards.Clear();
        UpdateSelectionCardButtom();
    }

    public void UpdateCardPositions()
    {
        handCards.RemoveAll(card => card == null);

        if (handCards.Count == 0)
        {
            return;
        }

        if (splineContainer == null || splineContainer.Spline == null)
        {
            UpdateCardPositionsFallback();
            return;
        }

        float cardSpacing = 1f / Mathf.Max(1, maxHandSize);
        float firstCardPosition = 0.5f - (handCards.Count - 1) * cardSpacing * 0.5f;
        var spline = splineContainer.Spline;
        var anchor = GetLayoutAnchor();

        for (int i = 0; i < handCards.Count; i++)
        {
            float t = Mathf.Clamp01(firstCardPosition + i * cardSpacing);

            Vector3 localPosition = spline.EvaluatePosition(t);
            Vector3 localForward = spline.EvaluateTangent(t);
            Vector3 localUp = spline.EvaluateUpVector(t);

            Vector3 position = anchor.TransformPoint(localPosition + handLocalOffset);
            Vector3 forward = anchor.TransformDirection(localForward);
            Vector3 up = anchor.TransformDirection(localUp);

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            if (up.sqrMagnitude < 0.0001f)
            {
                up = Vector3.up;
            }

            Quaternion rotation = Quaternion.LookRotation(up, Vector3.Cross(up, forward).normalized);
            ApplyCardPose(handCards[i], position, rotation);
        }
    }

    private void UpdateCardPositionsFallback()
    {
        var point = GetLayoutAnchor();
        float startX = -(handCards.Count - 1) * fallbackCardSpacing * 0.5f;

        for (int i = 0; i < handCards.Count; i++)
        {
            var localPos = new Vector3(startX + i * fallbackCardSpacing, 0f, 0f) + handLocalOffset;
            ApplyCardPose(handCards[i], point.TransformPoint(localPos), point.rotation);
        }
    }

    private void ApplyCardPose(GameObject card, Vector3 position, Quaternion rotation)
    {
        if (card == null)
        {
            return;
        }

        if (useDealAnimation && pendingDealCards.Contains(card))
        {
            KillDealTweenForCard(card, removePending: false);

            var duration = Mathf.Max(0f, dealAnimationDuration);
            if (duration <= 0f)
            {
                pendingDealCards.Remove(card);
            }
            else
            {
                var seq = DOTween.Sequence();
                seq.Join(card.transform.DOMove(position, duration).SetEase(Ease.OutCubic));
                seq.Join(card.transform.DORotateQuaternion(rotation, duration).SetEase(Ease.OutCubic));
                seq.OnComplete(() =>
                {
                    pendingDealCards.Remove(card);
                    dealTweens.Remove(card);

                    if (card == null)
                    {
                        return;
                    }

                    var cardInteractionAfterTween = card.GetComponent<CardInteraction>();
                    if (cardInteractionAfterTween != null)
                    {
                        cardInteractionAfterTween.UpdateLayoutPose(position, rotation);
                    }
                    else
                    {
                        card.transform.SetPositionAndRotation(position, rotation);
                    }
                });

                dealTweens[card] = seq;
                return;
            }
        }

        var interaction = card.GetComponent<CardInteraction>();

        if (interaction != null)
        {
            interaction.UpdateLayoutPose(position, rotation);
            return;
        }

        card.transform.SetPositionAndRotation(position, rotation);
    }

    private Transform GetLayoutAnchor()
    {
        if (handAnchor != null)
        {
            return handAnchor;
        }

        return transform;
    }

    public void CancelSelectedCards()
    {
        for (int i = 0; i < handCards.Count; i++)
        {
            var card = handCards[i];
            if (card == null)
            {
                continue;
            }

            var interaction = card.GetComponent<CardInteraction>();
            if (interaction != null && interaction.IsSelected)
            {
                interaction.SetSelected(false);
            }
        }

        selectedCards.Clear();
        UpdateSelectionCardButtom();
    }

    private void OnCardSelectionChanged(CardInteraction cardInteraction, bool isSelected)
    {
        if (_isAdjustingSelection)
        {
            return;
        }

        if (cardInteraction == null)
        {
            return;
        }

        if (!handCards.Contains(cardInteraction.gameObject))
        {
            return;
        }

        if (isSelected)
        {
            var maxSelectable = Mathf.Max(1, maxSelectedCards);
            if (selectedCards.Count >= maxSelectable)
            {
                _isAdjustingSelection = true;
                cardInteraction.SetSelected(false);
                _isAdjustingSelection = false;
                Debug.LogWarning("[HandManager] Chi duoc chon toi da " + maxSelectable + " la bai.");
                return;
            }

            selectedCards.Add(cardInteraction);
        }
        else
        {
            selectedCards.Remove(cardInteraction);
        }

        UpdateSelectionCardButtom();
    }

    private void UpdateSelectionCardButtom()
    {
        if (selectionCardButtom != null)
        {
            selectionCardButtom.SetActive(selectedCards.Count > 0);
        }
    }

    public List<int> GetSelectedCardValues()
    {
        var result = new List<int>();

        foreach (var selected in selectedCards)
        {
            if (selected == null)
            {
                continue;
            }

            var cardObject = selected.gameObject;
            if (cardObject == null)
            {
                continue;
            }

            var cardVisual = cardObject.GetComponent<CardVisual>();
            if (cardVisual == null)
            {
                cardVisual = cardObject.GetComponentInChildren<CardVisual>(true);
            }

            var cardValue = 0;
            if (cardVisual != null)
            {
                cardValue = GameManager.NormalizeCardValue(cardVisual.CardValue);
            }

            result.Add(cardValue);
        }

        return result;
    }

    public int RemoveSelectedCards()
    {
        if (selectedCards.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        var toRemove = new List<CardInteraction>(selectedCards);
        for (int i = 0; i < toRemove.Count; i++)
        {
            var interaction = toRemove[i];
            if (interaction == null)
            {
                continue;
            }

            var cardObject = interaction.gameObject;
            if (cardObject == null)
            {
                continue;
            }

            if (handCards.Remove(cardObject))
            {
                KillDealTweenForCard(cardObject);
                Destroy(cardObject);
                removed++;
            }
        }

        selectedCards.Clear();
        UpdateSelectionCardButtom();
        UpdateCardPositions();
        return removed;
    }

    public int GetHandCardCount()
    {
        handCards.RemoveAll(card => card == null);
        return handCards.Count;
    }

    private void KillDealTweenForCard(GameObject card, bool removePending = true)
    {
        if (card == null)
        {
            return;
        }

        if (dealTweens.TryGetValue(card, out var tween) && tween != null && tween.IsActive())
        {
            tween.Kill();
        }

        dealTweens.Remove(card);
        if (removePending)
        {
            pendingDealCards.Remove(card);
        }
    }

    private void KillAllDealTweens()
    {
        foreach (var pair in dealTweens)
        {
            var tween = pair.Value;
            if (tween != null && tween.IsActive())
            {
                tween.Kill();
            }
        }

        dealTweens.Clear();
        pendingDealCards.Clear();
    }

    private void OnDestroy()
    {
        KillAllDealTweens();
    }
}
