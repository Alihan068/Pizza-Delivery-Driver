using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameUIManager : MonoBehaviour {
    [Header("UI Elements")]
    [SerializeField] TextMeshProUGUI scoreText;
    [SerializeField] TextMeshProUGUI moneyText;
    [SerializeField] TextMeshProUGUI pizzaCountText;
    [SerializeField] TextMeshProUGUI timerText;

    [SerializeField] Slider healthbar;
    [SerializeField] TextMeshProUGUI speedText;
    [SerializeField] TextMeshProUGUI steeringText;

    [Header("Audio")]
    [SerializeField] AudioClip gameOverClip;
    AudioSource audioSource;

    GameManager gameManager;
    ScoreHandler scoreHandler; 

    private void Start() {
        audioSource = GetComponent<AudioSource>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        gameManager = FindFirstObjectByType<GameManager>();

        scoreText.text = "= 0";
        pizzaCountText.text = "= 0";
        if (timerText != null) timerText.text = "00:00";

        healthbar.value = healthbar.maxValue;
    }

    public void UpdateScoreDisplays() {
        //Take score from ScoreHandler
        if (scoreHandler != null) {
            scoreText.text = "= " + scoreHandler.currentScore;
            moneyText.text = "= " + gameManager.totalMoney;
        }
    }

    public void UpdatePizzaText(int deliveredPizza) {
        pizzaCountText.text = "= " + deliveredPizza;
    }

    // Display timer in MM:SS format
    public void UpdateTimerText(float timeInSeconds) {
        if (timerText == null) return;

        float minutes = Mathf.FloorToInt(timeInSeconds / 60);
        float seconds = Mathf.FloorToInt(timeInSeconds % 60);

        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    public void UpdateStatPanel(float hp, float speed, float steering) {
        healthbar.value = hp;
        // Divide steering by 10 for display
        steeringText.text = "Steering: \n" + (steering / 10).ToString("F1");
        speedText.text = "Speed: \n" + speed.ToString("F1");
    }

    public void PlayGameOverSound() {
        if (audioSource != null && gameOverClip != null) {
            audioSource.Stop();
            audioSource.PlayOneShot(gameOverClip);
        }
    }
}