using UnityEngine;

public class Delivery : MonoBehaviour {
    [SerializeField] float destroyDelay = 1f;
    [SerializeField] Color32 noPizzaColor = new Color32(1, 1, 1, 255);
    [SerializeField] Color32 hasPizzaColor = new Color32(1, 1, 1, 255);

    SpriteRenderer spriteRenderer;
    int pizzaDelivered = 0;
    bool hasPizza;

    private void Start() {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }
    private void OnTriggerEnter2D(Collider2D collision) {
        if (collision.gameObject.CompareTag("Customer") && hasPizza) {
            hasPizza = false;
            spriteRenderer.color = noPizzaColor;
            pizzaDelivered++;
            Debug.Log("Delivery Complete!");
        }

        if (collision.gameObject.CompareTag("Pizza") && !hasPizza) {
            hasPizza= true;
            spriteRenderer.color = hasPizzaColor;
            Destroy(collision.gameObject, destroyDelay);
            Debug.Log("Pizza Picked Up");
        }
        else if (hasPizza) {
            Debug.Log("Already carrying Pizza");
        }
    }
}
