using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class PizzaCollectPoint : MonoBehaviour {
    [Header("Collect")]
    [SerializeField] float collectInterval = 0.3f;

    [Header("Audio")]
    [SerializeField] AudioClip collectClip;
    [SerializeField] AudioClip fullClip;

    AudioSource audioSource;
    float timer;
    bool fullSoundPlayed;

    void Awake() {
        audioSource = GetComponent<AudioSource>();
    }

    void OnTriggerEnter2D(Collider2D other) {
        if (!other.CompareTag("Player")) return;
        timer = 0f;
        fullSoundPlayed = false;
    }

    void OnTriggerStay2D(Collider2D other) {
        if (!other.CompareTag("Player")) return;

        Delivery delivery = other.GetComponent<Delivery>();
        if (delivery == null) return;

        if (delivery.IsFull) {
            if (!fullSoundPlayed) {
                TryPlayAudioClip(fullClip);
                fullSoundPlayed = true;
            }
            return;
        }

        fullSoundPlayed = false;
        timer += Time.deltaTime;
        if (timer < collectInterval) return;
        timer -= collectInterval;

        delivery.CollectPizza(this);
        TryPlayAudioClip(collectClip);
    }

    void OnTriggerExit2D(Collider2D other) {
        if (!other.CompareTag("Player")) return;
        timer = 0f;
        fullSoundPlayed = false;
    }

    void TryPlayAudioClip(AudioClip clip) {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
    }
}
