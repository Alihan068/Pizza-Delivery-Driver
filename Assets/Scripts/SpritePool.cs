using UnityEngine;

/// <summary>Assigns a random configured sprite whenever the object is enabled.</summary>
public class SpritePool : MonoBehaviour {
   [SerializeField] Sprite[] sprites;
    SpriteRenderer spriteRenderer;

    private void OnEnable() {
        if (!TryGetComponent(out spriteRenderer) || sprites == null || sprites.Length == 0) {
            return;
        }

        spriteRenderer.sprite = sprites[Random.Range(0, sprites.Length)];
    }
        
}
