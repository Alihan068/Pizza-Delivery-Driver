using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameUIManager : MonoBehaviour {
    [Header("Localization Keys")]
    [SerializeField] string steeringKey = "hud.steering";
    [SerializeField] string speedKey = "hud.speed";
    float shownSpeed, shownSteering;

    void OnEnable() { LocalizationManager.LanguageChanged += RefreshStatLabels; }
    void OnDisable() { LocalizationManager.LanguageChanged -= RefreshStatLabels; }
    void RefreshStatLabels() {
        LocalizationManager.SetText(steeringText, steeringKey, shownSteering / 10);
        LocalizationManager.SetText(speedText, speedKey, shownSpeed);
        if (repairCostText != null) LocalizationManager.SetText(repairCostText, repairCostKey, shownRepairCost);
        RefreshObjectiveDisplays();
    }
    [Header("UI Elements")]
    [SerializeField] TextMeshProUGUI scoreText;
    [SerializeField] TextMeshProUGUI moneyText;
    [SerializeField] TextMeshProUGUI pizzaCountText;
    [SerializeField] TextMeshProUGUI carryText;
    [SerializeField] TextMeshProUGUI timerText;
    [SerializeField] string endlessKey = "hud.endless";

    [SerializeField] Slider healthbar;
    [SerializeField] TextMeshProUGUI repairCostText;
    [SerializeField] string repairCostKey = "hud.repairCost";
    [SerializeField] TextMeshProUGUI speedText;
    [SerializeField] TextMeshProUGUI steeringText;

    [Header("Shift Objectives")]
    [SerializeField] TextMeshProUGUI[] objectiveTexts;
    [SerializeField] string objectiveProgressKey = "hud.objective.progress";
    [SerializeField] string objectiveZeroKey = "hud.objective.zero";
    [SerializeField] string objectiveCompleteKey = "hud.objective.complete";
    [SerializeField] string objectiveFailedKey = "hud.objective.failed";
    IList<ShiftObjectiveState> shownObjectives;

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
    int shownRepairCost;

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
            scoreText.text = "= " + scoreHandler.FinalScore;
            moneyText.text = "= " + scoreHandler.sessionEarnings;
        }
    }

    public void UpdatePizzaText(int deliveredPizza) {
        pizzaCountText.text = "= " + deliveredPizza;
    }

    public void UpdateCarryText(int carried, int capacity) {
        if (carryText != null) carryText.text = carried + "/" + capacity;
    }

    /// <summary>Updates the visible objective cards for the active shift.</summary>
    /// <param name="objectives">Current objective states.</param>
    public void UpdateObjectiveDisplays(IList<ShiftObjectiveState> objectives) {
        shownObjectives = objectives;
        RefreshObjectiveDisplays();
    }

    void RefreshObjectiveDisplays() {
        if (objectiveTexts == null) return;
        for (int i = 0; i < objectiveTexts.Length; i++) {
            var text = objectiveTexts[i];
            if (text == null) continue;
            bool visible = shownObjectives != null && i < shownObjectives.Count && shownObjectives[i] != null;
            text.gameObject.SetActive(visible);
            if (!visible) continue;

            var objective = shownObjectives[i];
            string title = LocalizationManager.Get(objective.displayNameKey);
            if (objective.IsComplete) {
                text.text = LocalizationManager.Get(objectiveCompleteKey, title, objective.reward);
            }
            else if (objective.IsFailed) {
                text.text = LocalizationManager.Get(objectiveFailedKey, title);
            }
            else if (objective.target == 0) {
                text.text = LocalizationManager.Get(objectiveZeroKey, title, LocalizationManager.Get(objective.descriptionKey));
            }
            else {
                text.text = LocalizationManager.Get(objectiveProgressKey, title, LocalizationManager.Get(objective.descriptionKey), objective.progress, objective.target, objective.reward);
            }
        }
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

    /// <summary>Replaces the countdown with the localized label used by endless sessions.</summary>
    public void ShowEndlessTimer() {
        if (timerText == null) return;
        lastDisplayedSeconds = -1;
        timerText.text = LocalizationManager.Get(endlessKey);
    }

    /// <summary>Updates health and localized driving values, retaining those values for a subsequent language change.</summary>
    /// <param name="hp">Remaining health.</param>
    /// <param name="maxHp">Health bar maximum for the selected vehicle.</param>
    /// <param name="speed">Current driving speed.</param>
    /// <param name="steering">Turning stat, displayed on the existing one-tenth scale.</param>
    public void UpdateStatPanel(float hp, float maxHp, float speed, float steering) {
        healthbar.maxValue = maxHp;
        healthbar.value = hp;
        shownRepairCost = GameManager.Instance != null && maxHp > 0f
            ? GameManager.Instance.CalculateRepairCost(hp, maxHp, false)
            : 0;
        // Divide steering by 10 for display
        shownSpeed = speed;
        shownSteering = steering;
        RefreshStatLabels();
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
