using UnityEngine;

public class Delivery : MonoBehaviour {
    [SerializeField] float destroyDelay = 1f;
    [SerializeField] Color32 noPizzaColor = new Color32(1, 1, 1, 255);
    [SerializeField] Color32 hasPizzaColor = new Color32(1, 1, 1, 255);
    GameUIManager gameUIManager;

    AudioSource audioSource;
    [SerializeField] AudioClip pizzaCollectClip;
    [SerializeField] AudioClip pizzaDeliverClip;
    [SerializeField] AudioClip pizzaFailClip;

    SpriteRenderer spriteRenderer;
    int pizzaDelivered = 0;
    bool hasPizza;

    private void Start() {
        audioSource = GetComponent<AudioSource>();
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }
    private void OnTriggerEnter2D(Collider2D collision) {
        if (collision.gameObject.CompareTag("Customer") && hasPizza) {
            hasPizza = false;
            spriteRenderer.color = noPizzaColor;
            pizzaDelivered++;
            gameUIManager.UpdatePizzaText(pizzaDelivered);
            TryPlayAudioClip(pizzaDeliverClip);
            Debug.Log("Delivery Complete!");
        }

        if (collision.gameObject.CompareTag("Pizza") && !hasPizza) {
            hasPizza= true;
            spriteRenderer.color = hasPizzaColor;
            Destroy(collision.gameObject, destroyDelay);
            TryPlayAudioClip(pizzaCollectClip);
            Debug.Log("Pizza Picked Up");
        }
        else if (hasPizza) {
            TryPlayAudioClip(pizzaFailClip);
            Debug.Log("Already carrying Pizza");
        }

        void TryPlayAudioClip(AudioClip clip) {
            if (clip != null && audioSource!= null)
                audioSource.PlayOneShot(clip);
            else
                Debug.LogWarning("AudioClip is not assigned.");
        }
    }
}
