using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameUIManager : MonoBehaviour {
    [Header("UI Elements")]
    [SerializeField] TextMeshProUGUI scoreText;
    [SerializeField] TextMeshProUGUI moneyText;
    [SerializeField] TextMeshProUGUI pizzaCountText;
    [SerializeField] TextMeshProUGUI carryText;
    [SerializeField] TextMeshProUGUI timerText;

    [SerializeField] Slider healthbar;
    [SerializeField] TextMeshProUGUI speedText;
    [SerializeField] TextMeshProUGUI steeringText;

    [Header("Damage Feedback")]
    [SerializeField] Color healthBarFlashColor = Color.red;
    [SerializeField] float healthBarFlashDuration = 0.15f;
    Image healthbarFillImage;
    Color healthbarBaseColor;
    Coroutine healthBarFlashCoroutine;

    [Header("Audio")]
    [SerializeField] AudioClip gameOverClip;
    AudioSource audioSource;

    ScoreHandler scoreHandler;

    private void Start() {
        audioSource = GetComponent<AudioSource>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();

        scoreText.text = "= 0";
        pizzaCountText.text = "= 0";
        if (carryText != null) carryText.text = "0/0";
        if (timerText != null) timerText.text = "00:00";

        healthbar.value = healthbar.maxValue;

        if (healthbar.fillRect != null) {
            healthbarFillImage = healthbar.fillRect.GetComponent<Image>();
            if (healthbarFillImage != null) healthbarBaseColor = healthbarFillImage.color;
        }
    }

    public void UpdateScoreDisplays() {
        //Take score from ScoreHandler
        if (scoreHandler != null) {
            scoreText.text = "= " + scoreHandler.currentScore;
            moneyText.text = "= " + scoreHandler.sessionEarnings;
        }
    }

    public void UpdatePizzaText(int deliveredPizza) {
        pizzaCountText.text = "= " + deliveredPizza;
    }

    public void UpdateCarryText(int carried, int capacity) {
        if (carryText != null) carryText.text = carried + "/" + capacity;
    }

    int lastDisplayedSeconds = -1;

    // Display timer in MM:SS format
    public void UpdateTimerText(float timeInSeconds) {
        if (timerText == null) return;

        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(timeInSeconds));
        if (totalSeconds == lastDisplayedSeconds) return;
        lastDisplayedSeconds = totalSeconds;

        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        timerText.text = minutes.ToString("00") + ":" + seconds.ToString("00");
    }

    public void UpdateStatPanel(float hp, float maxHp, float speed, float steering) {
        healthbar.maxValue = maxHp;
        healthbar.value = hp;
        // Divide steering by 10 for display
        steeringText.text = "Steering: \n" + (steering / 10).ToString("F1");
        speedText.text = "Speed: \n" + speed.ToString("F1");
    }

    public void FlashHealthBar() {
        if (healthbarFillImage == null) return;
        if (healthBarFlashCoroutine != null) StopCoroutine(healthBarFlashCoroutine);
        healthBarFlashCoroutine = StartCoroutine(HealthBarFlashRoutine());
    }

    IEnumerator HealthBarFlashRoutine() {
        healthbarFillImage.color = healthBarFlashColor;
        yield return new WaitForSecondsRealtime(healthBarFlashDuration);
        healthbarFillImage.color = healthbarBaseColor;
        healthBarFlashCoroutine = null;
    }

    public void PlayGameOverSound() {
        if (audioSource != null && gameOverClip != null) {
            audioSource.Stop();
            audioSource.PlayOneShot(gameOverClip);
        }
    }
}
