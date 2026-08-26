using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ExtractionZone : MonoBehaviour {
    [Header("Settings")]
    [SerializeField] Key interactKey = Key.E;
    [SerializeField] float holdDuration = 1.5f;
    [SerializeField] GameObject visualIndicator;
    [SerializeField] Image holdFillImage;

    ScoreHandler scoreHandler;
    bool playerInRange;
    float holdTimer;

    private void Start() {
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        if (visualIndicator != null) visualIndicator.SetActive(false);
        SetFillAmount(0f);
    }

    private void Update() {
        if (!playerInRange) return;

        bool isHeld = Keyboard.current != null && Keyboard.current[interactKey].isPressed;

        if (isHeld) {
            holdTimer += Time.deltaTime;
            SetFillAmount(holdTimer / holdDuration);

            if (holdTimer >= holdDuration) {
                holdTimer = 0f;
                SetFillAmount(0f);
                if (scoreHandler != null) scoreHandler.EndLevel(EndReason.Extracted);
            }
        }
        else if (holdTimer > 0f) {
            holdTimer = 0f;
            SetFillAmount(0f);
        }
    }

    void SetFillAmount(float ratio) {
        if (holdFillImage != null) holdFillImage.fillAmount = Mathf.Clamp01(ratio);
    }

    private void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Player")) {
            playerInRange = true;
            if (visualIndicator != null) visualIndicator.SetActive(true);
        }
    }

    private void OnTriggerExit2D(Collider2D other) {
        if (other.CompareTag("Player")) {
            playerInRange = false;
            holdTimer = 0f;
            SetFillAmount(0f);
            if (visualIndicator != null) visualIndicator.SetActive(false);
        }
    }
}
