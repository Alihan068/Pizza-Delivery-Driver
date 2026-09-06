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

        // Records that a shift is underway. Without this, killing the process mid-shift skips
        // settlement entirely: the player loses the unbanked earnings but escapes the repair bill,
        // which makes force-quitting cheaper than any legitimate way out of a bad run.
        if (GameManager.Instance != null) GameManager.Instance.MarkShiftStarted();

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

    
    /// <summary>
    /// Ends the running session: settles the economy, shows the result panel and freezes time.
    /// </summary>
    /// <param name="reason">How the session ended, which decides how much of the earnings survive.</param>
    /// <param name="destinationScene">
    /// Scene the result panel returns to. Leave empty to use the garage from <see cref="GameConfig"/>.
    /// </param>
    public void EndLevel(EndReason reason, string destinationScene = null) {
        if (!isGameActive) return;

        if (string.IsNullOrEmpty(destinationScene) && GameManager.Instance != null) {
            destinationScene = GameManager.Instance.Config.garageScene;
        }

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
