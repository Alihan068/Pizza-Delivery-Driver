using UnityEngine;
using UnityEngine.UI;

/// <summary>Displays a map preview sprite inside a dynamically generated selection card.</summary>
public class MapCardPreview : MonoBehaviour {
    static Sprite fallbackSprite;
    [SerializeField] Image previewImage;

    /// <summary>Returns a shared one-pixel sprite used for authored preview fallbacks.</summary>
    /// <returns>A safe sprite that can be tinted by a UI Image.</returns>
    public static Sprite GetFallbackSprite() {
        if (fallbackSprite == null) {
            fallbackSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f), 1f);
            fallbackSprite.name = "MapPreviewFallback";
        }
        return fallbackSprite;
    }

    /// <summary>Creates the preview image as a child of the supplied map card.</summary>
    /// <param name="cardRect">RectTransform used as the card parent.</param>
    public void Initialize(RectTransform cardRect) {
        if (cardRect == null) return;
        if (previewImage == null) {
            Image cardBackground = GetComponent<Image>();
            Image[] childImages = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < childImages.Length; i++) {
                if (childImages[i] != cardBackground) {
                    previewImage = childImages[i];
                    break;
                }
            }
        }
        if (previewImage != null) {
            ConfigurePreviewRect(previewImage.transform as RectTransform);
            previewImage.preserveAspect = true;
            previewImage.raycastTarget = false;
            return;
        }

        GameObject previewObject = new GameObject("MapCardPreviewImage", typeof(RectTransform), typeof(Image));
        previewObject.transform.SetParent(cardRect, false);
        previewImage = previewObject.GetComponent<Image>();
        ConfigurePreviewRect(previewObject.GetComponent<RectTransform>());
        previewImage.preserveAspect = true;
        previewImage.raycastTarget = false;
    }

    void ConfigurePreviewRect(RectTransform rect) {
        if (rect == null) return;
        rect.anchorMin = new Vector2(0.08f, 0.34f);
        rect.anchorMax = new Vector2(0.92f, 0.94f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>Assigns the preview sprite and keeps a visible tinted fallback when it is missing.</summary>
    /// <param name="sprite">Map preview sprite, or null.</param>
    public void SetSprite(Sprite sprite) {
        SetSprite(sprite, Color.white);
    }

    /// <summary>Assigns a preview sprite or a visible fallback tint for an unillustrated map.</summary>
    /// <param name="sprite">Map preview sprite, or null.</param>
    /// <param name="fallbackColor">Tint used when the preview sprite is missing.</param>
    public void SetSprite(Sprite sprite, Color fallbackColor) {
        if (previewImage == null) return;
        bool hasSprite = sprite != null;
        previewImage.sprite = hasSprite ? sprite : GetFallbackSprite();
        previewImage.color = hasSprite ? Color.white : fallbackColor;
        previewImage.enabled = true;
    }
}
