using System.Collections.Generic;
using UnityEngine;

public class ScoreHandler : MonoBehaviour {
    [Header("Game Settings")]
    [SerializeField] float levelDurationInMinutes = 3f;
    float currentTimer;
    float totalDurationSeconds;
    float activeSessionSeconds;
    bool isGameActive = true;

    [Header("Session Data")]
    public int currentScore;
    public int sessionEarnings { get; private set; }
    public int missedCustomers { get; private set; }
    public int ordersCompleted { get; private set; }
    /// <summary>Number of collision damage events recorded during this shift.</summary>
    public int collisionDamageEvents { get; private set; }
    /// <summary>Number of pizzas lost during this shift.</summary>
    public int pizzasLost { get; private set; }
    public bool IsGameActive => isGameActive;

    /// <summary>True when this score handler is running an endless session.</summary>
    public bool IsFreeplay => GameManager.Instance != null && GameManager.Instance.IsFreeplayMode;

    /// <summary>Seconds remaining in the active shift.</summary>
    public float RemainingTimeSeconds => Mathf.Max(0f, currentTimer);

    /// <summary>Normalized progress through the active shift.</summary>
    public float ShiftProgress01 => totalDurationSeconds > 0f ? Mathf.Clamp01(1f - currentTimer / totalDurationSeconds) : 0f;

    /// <summary>Objectives selected for this shift and their live progress.</summary>
    public List<ShiftObjectiveState> ActiveObjectives { get; private set; } = new List<ShiftObjectiveState>();

    GameUIManager gameUIManager;
    SessionResultPanel resultPanel;

    void Start() {
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        resultPanel = FindFirstObjectByType<SessionResultPanel>(FindObjectsInactive.Include);
        currentTimer = IsFreeplay ? 0f : levelDurationInMinutes * 60f;
        totalDurationSeconds = currentTimer;

        if (!IsFreeplay && GameManager.Instance != null) GameManager.Instance.MarkShiftStarted();
        CreateShiftObjectives();
        if (IsFreeplay && gameUIManager != null) gameUIManager.ShowEndlessTimer();
        UpdateUI();
    }

    void CreateShiftObjectives() {
        ActiveObjectives.Clear();
        if (IsFreeplay) return;
        if (GameManager.Instance == null || GameManager.Instance.Career == null || GameManager.Instance.CareerData == null) return;

        var data = GameManager.Instance.CareerData;
        if (data.objectiveTypes == null || data.objectiveTypes.Length == 0) return;

        int capacity = GameManager.Instance.GetShiftCapacity();
        int rank = GameManager.Instance.CurrentRank;
        int objectiveCount = Mathf.Clamp(data.objectivesPerShift, 0, data.objectiveTypes.Length);
        var available = new List<ShiftObjectiveTuning>(data.objectiveTypes);

        for (int i = 0; i < objectiveCount && available.Count > 0; i++) {
            int index = Random.Range(0, available.Count);
            var tuning = available[index];
            available.RemoveAt(index);
            var state = GameManager.Instance.Career.CreateObjective(tuning.type, capacity, rank);
            if (state != null) ActiveObjectives.Add(state);
        }
    }

    void Update() {
        if (!isGameActive) return;

        if (IsFreeplay) {
            activeSessionSeconds += Time.deltaTime;
            return;
        }

        if (currentTimer > 0f) {
            float elapsed = Mathf.Min(Time.deltaTime, currentTimer);
            activeSessionSeconds += elapsed;
            currentTimer -= elapsed;
            if (gameUIManager != null) gameUIManager.UpdateTimerText(currentTimer);
        }
        else {
            currentTimer = 0f;
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
        RegisterObjectiveViolation(ShiftObjectiveType.PerfectService);
    }

    public void RegisterCompletedOrder() {
        ordersCompleted++;
    }

    /// <summary>Advances the quota objective by pizzas delivered on time.</summary>
    /// <param name="amount">Number of pizzas accepted by customers.</param>
    public void RegisterDeliveredPizzas(int amount) {
        if (!isGameActive || amount <= 0) return;
        foreach (var objective in ActiveObjectives) {
            if (objective.type != ShiftObjectiveType.Quota || objective.IsComplete || objective.IsFailed) continue;
            if (objective.AddProgress(amount)) CompleteObjective(objective);
        }
    }

    /// <summary>Registers a completed order and advances any large-order objective.</summary>
    /// <param name="orderSize">Total pizzas in the completed order.</param>
    public void RegisterCompletedOrder(int orderSize) {
        ordersCompleted++;
        foreach (var objective in ActiveObjectives) {
            if (objective.type != ShiftObjectiveType.BigOrder || objective.IsComplete || objective.IsFailed) continue;
            if (orderSize < objective.parameter) continue;
            if (objective.AddProgress(1)) CompleteObjective(objective);
        }
    }

    /// <summary>Registers one collision that caused damage during this shift.</summary>
    public void RegisterCollisionDamageEvent() {
        collisionDamageEvents++;
        RegisterObjectiveViolation(ShiftObjectiveType.CleanRun);
    }

    /// <summary>Registers one pizza lost because of a damaging impact.</summary>
    public void RegisterPizzaLost() {
        pizzasLost++;
        RegisterObjectiveViolation(ShiftObjectiveType.CargoGuard);
    }

    /// <summary>Checks and completes the fast-extraction objective at the extraction zone.</summary>
    public void RegisterFastExtraction() {
        if (!isGameActive || GameManager.Instance == null || GameManager.Instance.CareerData == null) return;

        var data = GameManager.Instance.CareerData;
        int quotaTarget = data.GetObjectiveTarget(ShiftObjectiveType.Quota, GameManager.Instance.GetShiftCapacity(), GameManager.Instance.CurrentRank);
        int requiredDeliveries = Mathf.FloorToInt(quotaTarget * data.fastExtractionQuotaFraction);
        Delivery delivery = FindFirstObjectByType<Delivery>();
        bool hasTime = RemainingTimeSeconds >= data.fastExtractionMinSecondsRemaining;
        bool hasDeliveries = delivery != null && delivery.pizzaDelivered >= requiredDeliveries;
        if (!hasTime || !hasDeliveries) return;

        foreach (var objective in ActiveObjectives) {
            if (objective.type != ShiftObjectiveType.FastExtraction || objective.IsComplete || objective.IsFailed) continue;
            if (objective.AddProgress(1)) CompleteObjective(objective);
        }
    }

    void RegisterObjectiveViolation(ShiftObjectiveType type) {
        foreach (var objective in ActiveObjectives) {
            if (objective.type == type) objective.RegisterViolation();
        }
        UpdateUI();
    }

    void CompleteObjective(ShiftObjectiveState objective) {
        if (objective == null || objective.IsComplete || objective.IsFailed) return;
        objective.MarkCompleted();
        if (GameManager.Instance != null && objective.reward > 0) {
            GameManager.Instance.AddMoneyToBank(objective.reward);
            GameManager.Instance.SaveGame();
        }
        UpdateUI();
    }

    void FinalizeObjectives() {
        foreach (var objective in ActiveObjectives) {
            if (objective.IsComplete || objective.IsFailed) continue;
            if (objective.target > 0) {
                if (objective.progress >= objective.target) CompleteObjective(objective);
            }
            else {
                CompleteObjective(objective);
            }
        }
    }

    void UpdateUI() {
        if (gameUIManager != null) {
            gameUIManager.UpdateScoreDisplays();
            gameUIManager.UpdateObjectiveDisplays(ActiveObjectives);
        }
    }

    /// <summary>Ends the running session, settles the economy and freezes time.</summary>
    /// <param name="reason">How the session ended.</param>
    /// <param name="destinationScene">Optional scene for the result button.</param>
    public void EndLevel(EndReason reason, string destinationScene = null) {
        if (!isGameActive) return;

        if (string.IsNullOrEmpty(destinationScene) && GameManager.Instance != null) {
            destinationScene = GameManager.Instance.Config.garageScene;
        }

        isGameActive = false;
        FinalizeObjectives();

        Driver driver = FindFirstObjectByType<Driver>();
        float hp = driver != null ? driver.currentHealth : 0f;
        float maxHp = driver != null ? driver.maxHealth : 0f;

        Delivery delivery = FindFirstObjectByType<Delivery>();
        int delivered = delivery != null ? delivery.pizzaDelivered : 0;

        CustomerManager customerManager = FindFirstObjectByType<CustomerManager>();
        int ordersOffered = customerManager != null ? customerManager.ordersOffered : 0;

        bool freeplay = IsFreeplay;
        SessionResult result = GameManager.Instance.SettleSession(sessionEarnings, hp, maxHp, reason,
            ordersCompleted, ordersOffered, missedCustomers, activeSessionSeconds, delivered, currentScore, freeplay);
        result.noMissedOrders = missedCustomers == 0;
        result.noCollisionDamage = collisionDamageEvents == 0;
        result.noPizzasLost = pizzasLost == 0;
        result.perfectShift = !freeplay && result.noMissedOrders && result.noCollisionDamage && result.noPizzasLost &&
                              (reason == EndReason.TimeUp || reason == EndReason.Extracted);

        if (resultPanel != null) resultPanel.Show(result, delivered, missedCustomers, currentScore, destinationScene);
        Time.timeScale = 0f;
    }
}
