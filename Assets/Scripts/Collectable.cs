using UnityEngine;

public class Collectable : MonoBehaviour {
    Driver driver;

    [SerializeField] AudioClip effectClip;

    void OnEnable() {
        driver = FindFirstObjectByType<Driver>();
    }
    private void OnTriggerEnter2D(Collider2D collision) {
        if(collision.gameObject.CompareTag("Player")) {
            driver.TryPlayAudioClip(effectClip);
        }
    }
}
