using UnityEngine;

/// <summary>
/// Provides an invisible trigger that supplies pizzas to a nearby delivery vehicle.
/// </summary>
/// <remarks>
/// The point owns only the interaction cadence and audio feedback. Inventory, payment and
/// navigation remain the responsibility of <see cref="Delivery"/>, so the same prefab can be
/// placed at a shop, roadside depot or any future map-authored pickup location.
/// </remarks>
[RequireComponent(typeof(AudioSource))]
public class PizzaCollectPoint : MonoBehaviour {
    /// <summary>
    /// Authored service role used by editor map builders. Custom points keep their
    /// manually authored placement and are not moved by built-in service placement.
    /// </summary>
    [Header("Map Authoring")]
    public PizzaCollectionPointRole role = PizzaCollectionPointRole.Custom;

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

        if (!delivery.CollectPizza(this)) return;

        delivery.PlayPizzaCollectionEffect(GetCollectionVisualSourcePosition());
        TryPlayAudioClip(collectClip);
    }

    Vector3 GetCollectionVisualSourcePosition() {
        SpriteRenderer serviceRenderer = GetComponentInParent<SpriteRenderer>();
        return serviceRenderer != null
            ? serviceRenderer.bounds.ClosestPoint(transform.position)
            : transform.position;
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
