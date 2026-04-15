using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

public class HandManager : MonoBehaviour
{
    [Header("Card Setup")]
    [SerializeField] private int maxHandSize = 6;
    [SerializeField] private int initialHandSize = 6;
    [SerializeField] private bool dealOnStart = true;
    [SerializeField] private float initialDealInterval = 0.12f;
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

    private readonly List<GameObject> handCards = new List<GameObject>();
    private readonly HashSet<CardInteraction> selectedCards = new HashSet<CardInteraction>();

    private void Awake()
    {
        UpdateSelectionCardButtom();
    }

    private void Start()
    {
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
    }

    private void OnDisable()
    {
        CardInteraction.SelectionChanged -= OnCardSelectionChanged;
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
        var spawnWorldPos = point.TransformPoint(handLocalOffset);
        var card = Instantiate(cardPrefab, spawnWorldPos, point.rotation, point);
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

    public void RemoveLastCard()
    {
        if (handCards.Count == 0)
        {
            return;
        }

        var last = handCards[handCards.Count - 1];
        handCards.RemoveAt(handCards.Count - 1);

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
}
