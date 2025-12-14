using UnityEngine;

public class ScoreHandler : MonoBehaviour {
    [Header("Game Settings")]
    [SerializeField] float levelDurationInMinutes = 5f; 
    private float currentTimer;
    private bool isGameActive = true;

    [Header("Session Data")]
    public int currentScore = 0;
    public int currentMoney = 0;

    GameUIManager gameUIManager;

    void Start() {
        gameUIManager = FindFirstObjectByType<GameUIManager>();

        // Convert minutes to seconds
        currentTimer = levelDurationInMinutes * 60;

        UpdateUI();
    }

    void Update() {
        if (!isGameActive) return;

        // Timer Logic
        if (currentTimer > 0) {
            currentTimer -= Time.deltaTime;

            // UI Update format
            if (gameUIManager != null) {
                gameUIManager.UpdateTimerText(currentTimer);
            }
        }
        else {
            // Süre Bitti!
            currentTimer = 0;
            EndLevel(true); // true = Başarıyla süre bitti
        }
    }

    public void AddScore(int amount) {
        if (!isGameActive) return;

        currentScore += amount;
        UpdateUI();
    }

    public void AddMoney(int amount) {
        if (!isGameActive) return;

        currentMoney += amount;
        // Add UI update in future if needed
    }

    void UpdateUI() {
        if (gameUIManager != null) {
            gameUIManager.UpdateScoreText();
        }
    }

    // End Level
    public void EndLevel(bool timeRanOut) {
        if (!isGameActive) return;

        isGameActive = false;
        Debug.Log(timeRanOut ? "Süre Bitti - Level Tamamlandı!" : "Oyun Bitti - Can Kalmadı!");

        // Transfer money to GameManager
        if (GameManager.Instance != null) {
            GameManager.Instance.AddMoneyToBank(currentMoney);
        }

        // gameUIManager.ShowGameOverPanel(); 
    }
}