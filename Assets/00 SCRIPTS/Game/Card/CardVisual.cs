using UnityEngine;
using UnityEngine.UI;

public class CardVisual : MonoBehaviour
{
    [Header("Card Faces (0,1,2,3)")]
    [SerializeField] private Sprite[] valueSprites = new Sprite[4];

    [Header("Optional Targets")]
    [SerializeField] private SpriteRenderer targetSpriteRenderer;
    [SerializeField] private Image targetImage;

    public int CardValue { get; private set; } = -1;

    private Sprite _defaultSpriteRendererSprite;
    private Sprite _defaultImageSprite;

    private void Awake()
    {
        if (targetSpriteRenderer == null)
        {
            targetSpriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        if (targetImage == null)
        {
            targetImage = GetComponentInChildren<Image>(true);
        }

        _defaultSpriteRendererSprite = targetSpriteRenderer != null ? targetSpriteRenderer.sprite : null;
        _defaultImageSprite = targetImage != null ? targetImage.sprite : null;
    }

    public void SetCardValue(int cardValue)
    {
        var normalized = GameManager.NormalizeCardValue(cardValue);
        CardValue = normalized;
        if (valueSprites == null || normalized < 0 || normalized >= valueSprites.Length)
        {
            Debug.LogWarning("[CardVisual] Chua gan du sprite cho cardValue=" + normalized + ".");
            return;
        }

        var sprite = valueSprites[normalized];
        if (sprite == null)
        {
            Debug.LogWarning("[CardVisual] Sprite cardValue=" + normalized + " dang null.");
            return;
        }

        if (targetSpriteRenderer != null)
        {
            targetSpriteRenderer.sprite = sprite;
        }

        if (targetImage != null)
        {
            targetImage.sprite = sprite;
        }

        if (targetSpriteRenderer == null && targetImage == null)
        {
            Debug.LogWarning("[CardVisual] Khong tim thay SpriteRenderer/Image target de gan sprite.");
        }
    }

    public void ResetToDefaultVisual()
    {
        CardValue = -1;

        if (targetSpriteRenderer != null)
        {
            targetSpriteRenderer.sprite = _defaultSpriteRendererSprite;
        }

        if (targetImage != null)
        {
            targetImage.sprite = _defaultImageSprite;
        }
    }
}
