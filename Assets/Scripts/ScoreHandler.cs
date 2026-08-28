using UnityEngine;

public class ScoreHandler : MonoBehaviour {
    [Header("Game Settings")]
    [SerializeField] float levelDurationInMinutes = 3f; 
    private float currentTimer;
    private bool isGameActive = true;

    [Header("Session Data")]
    public int currentScore = 0;
    public int sessionEarnings { get; private set; }
    public int missedCustomers { get; private set; }
    public bool IsGameActive => isGameActive;

    GameUIManager gameUIManager;
    SessionResultPanel resultPanel;

    void Start() {
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        resultPanel = FindFirstObjectByType<SessionResultPanel>(FindObjectsInactive.Include);
        
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
            EndLevel(EndReason.TimeUp); 
        }
    }

    public void AddScore(int amount) {
        if (!isGameActive) return;

        currentScore += amount;
        UpdateUI();
    }

    public void AddMoney(int amount) {
        if (!isGameActive) return;
        sessionEarnings += amount;
        UpdateUI();
    }

    public void RegisterMissedCustomer() {
        missedCustomers++;
    }


    void UpdateUI() {
        if (gameUIManager != null) {
            gameUIManager.UpdateScoreDisplays();
        }
    }

    
    public void EndLevel(EndReason reason, string destinationScene = "GarageScene") {
        if (!isGameActive) return;

        isGameActive = false;

        Driver driver = FindFirstObjectByType<Driver>();
        float hp = driver != null ? driver.currentHealth : 0f;
        float maxHp = driver != null ? driver.maxHealth : 0f;

        Delivery delivery = FindFirstObjectByType<Delivery>();
        int delivered = delivery != null ? delivery.pizzaDelivered : 0;

        SessionResult result = GameManager.Instance.SettleSession(sessionEarnings, hp, maxHp, reason);

        if (resultPanel != null) {
            resultPanel.Show(result, delivered, missedCustomers, currentScore, destinationScene);
        }

        Time.timeScale = 0f;
    }
}
