using UnityEngine;

public class Collectable : MonoBehaviour {
    Driver driver;

    [SerializeField] AudioClip effectClip;

    void OnEnable() {
        driver = FindFirstObjectByType<Driver>();

        transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));

    
}
    private void OnTriggerEnter2D(Collider2D collision) {
        if(collision.gameObject.CompareTag("Player")) {
            driver.TryPlayAudioClip(effectClip);
        }
    }
}
