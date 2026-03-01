using UnityEngine;
using TMPro;

public class ExtractionZone : MonoBehaviour {
    [Header("Settings")]
    [SerializeField] KeyCode interactKey = KeyCode.E;
    [SerializeField] GameObject visualIndicator;

    ScoreHandler scoreHandler;

    private void Start() {
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        if (visualIndicator != null) visualIndicator.SetActive(false);
    }

    private void OnTriggerStay2D(Collider2D other) {
        if (other.CompareTag("Player")) {

            if (visualIndicator != null) visualIndicator.SetActive(true);

            if (Input.GetKey(interactKey)) {
                scoreHandler.EndLevel(false);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other) {
        if (other.CompareTag("Player")) {
            if (visualIndicator != null) visualIndicator.SetActive(false);
        }
    }
}