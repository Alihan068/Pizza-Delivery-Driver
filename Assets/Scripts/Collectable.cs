using UnityEngine;

public class Collectable : MonoBehaviour {
    Driver driver;

    [SerializeField] AudioClip effectClip;

    // No driver lookup here on purpose. Obstacles respawn every couple of seconds,
    // and a scene-wide search per spawn cost far more than it was worth - especially
    // since it only ever fed an audio call. Resolved lazily on first contact instead.
    void OnEnable() {
        transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
    }

    private void OnTriggerEnter2D(Collider2D collision) {
        if (effectClip == null) return;
        if (!collision.gameObject.CompareTag("Player")) return;

        if (driver == null) driver = FindFirstObjectByType<Driver>();
        if (driver == null) return;

        driver.TryPlayAudioClip(effectClip);
    }
}
