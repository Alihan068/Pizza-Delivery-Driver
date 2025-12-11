using UnityEngine;

public class SpritePool : MonoBehaviour
{
   [SerializeField] Sprite[] sprites;
    SpriteRenderer spriteRenderer;

    private void OnEnable() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (sprites.Length > 0) {
            spriteRenderer.sprite = sprites[Random.Range(0, sprites.Length)];
        }
    }
        
}
