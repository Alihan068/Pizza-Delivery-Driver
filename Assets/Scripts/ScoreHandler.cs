using UnityEngine;

public class ScoreHandler : MonoBehaviour {
    [Header("Game Settings")]
    [SerializeField] float levelDurationInMinutes = 5f; 
    private float currentTimer;
    private bool isGameActive = true;

    [Header("Session Data")]
    public int currentScore = 0;

    GameUIManager gameUIManager;
    GameManager gameManager;

    void Start() {
        gameManager = FindFirstObjectByType<GameManager>();
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        
        // Convert minutes to seconds
        currentTimer = levelDurationInMinutes * 60;

        UpdateUI();
    }

    void Update() {
        if (!isGameActive) return;

        
        if (currentTimer > 0) {
            currentTimer -= Time.deltaTime;

            // UI Update format
            if (gameUIManager != null) {
                gameUIManager.UpdateTimerText(currentTimer);
            }
        }
        else {
            
            currentTimer = 0;
            EndLevel(true); 
        }
    }

    public void AddScore(int amount) {
        if (!isGameActive) return;

        currentScore += amount;
        UpdateUI();
    }

    public void AddMoney(int amount) {
        if (!isGameActive) return;
        if (gameManager != null) {
            gameManager.AddMoneyToBank(amount);
            UpdateUI();
        }
    }


    void UpdateUI() {
        if (gameUIManager != null) {
            gameUIManager.UpdateScoreDisplays();
        }
    }

    
    public void EndLevel(bool timeRanOut) {
        if (!isGameActive) return;

        isGameActive = false;
        Debug.Log(timeRanOut ? "Süre Bitti - Level Tamamlandı!" : "Oyun Bitti - Can Kalmadı!");
        
    }
}