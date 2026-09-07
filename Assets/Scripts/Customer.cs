using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Customer : MonoBehaviour {
    [SerializeField] string thanksKey = "customer.thanks";

    void RefreshLanguage() {
        if (timeText != null && currentOrder != null && currentOrder.IsComplete)
            timeText.text = LocalizationManager.Get(thanksKey);
    }

    [SerializeField] GameObject pizzaInHand;
    [SerializeField] Sprite[] CustomerBodyVariety;

    [SerializeField] Image fillImage;
    TextMeshProUGUI timeText;

    [Header("Order Indicator")]
    [SerializeField] Image orderIndicatorBackground;
    [SerializeField] Image[] pizzaIcons;
    [SerializeField] Color32 canFulfillColor = new Color32(80, 220, 100, 255);
    [SerializeField] Color32 cannotFulfillColor = new Color32(220, 90, 90, 255);

    public CustomerOrder currentOrder { get; private set; }
    LevelData levelData;
    MapDifficultyData difficultyData;

    public float timeLeft;

    GameManager gameManager;
    SpriteRenderer spriteRenderer;
    ScoreHandler scoreHandler;
    CustomerManager customerManager;
    GameUIManager gameUIManager;
    Delivery delivery;

    Collider2D bodyCollider;
    Coroutine leaveCoroutine;

    static readonly WaitForSeconds oneSecond = new WaitForSeconds(1f);

    int lastRemainingShown = -1;
    bool lastCanFulfill;

    private void Awake() {
        bodyCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        timeText = GetComponentInChildren<TextMeshProUGUI>();

        gameManager = FindFirstObjectByType<GameManager>();
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        customerManager = FindFirstObjectByType<CustomerManager>();
        delivery = FindFirstObjectByType<Delivery>();
    }

    void Update() {
        if (currentOrder == null) return;

        int remaining = currentOrder.RemainingPizzas;
        int carried = delivery != null ? delivery.carryPizzaAmount : 0;
        bool canFulfill = carried >= remaining;

        if (remaining == lastRemainingShown && canFulfill == lastCanFulfill) return;

        lastRemainingShown = remaining;
        lastCanFulfill = canFulfill;
        UpdateOrderIndicator();
    }

    void UpdateOrderIndicator() {
        int remaining = currentOrder.RemainingPizzas;

        for (int i = 0; i < pizzaIcons.Length; i++) {
            if (pizzaIcons[i] != null) pizzaIcons[i].gameObject.SetActive(i < remaining);
        }

        if (orderIndicatorBackground != null) {
            int carried = delivery != null ? delivery.carryPizzaAmount : 0;
            orderIndicatorBackground.color = carried >= remaining ? canFulfillColor : cannotFulfillColor;
        }
    }

    private void OnEnable() {
        LocalizationManager.LanguageChanged += RefreshLanguage;
        // First activation happens before CustomerManager ever calls Setup() (the
        // objects start active in the scene and get switched off once at startup) -
        // ignore that spurious enable, the real one comes right after Setup().
        if (currentOrder == null) return;

        ResetState();
        leaveCoroutine = StartCoroutine(LeaveAfterTime());
    }

    private void OnDisable() {
        LocalizationManager.LanguageChanged -= RefreshLanguage;
        StopAllCoroutines();
        leaveCoroutine = null;
    }

    /// <summary>
    /// Assigns a new order and the tuning data that governs its wait time and rewards.
    /// </summary>
    /// <param name="order">Fresh runtime order state for this pooled customer.</param>
    /// <param name="data">Map-wide timing and reward data.</param>
    /// <remarks>The customer must receive this data before it is activated by the manager.</remarks>
    public void Setup(CustomerOrder order, LevelData data) {
        Setup(order, data, null);
    }

    /// <summary>
    /// Assigns a new order together with its selected map difficulty tier.
    /// </summary>
    /// <param name="order">Fresh runtime order state for this pooled customer.</param>
    /// <param name="data">Map-wide timing and reward data.</param>
    /// <param name="difficulty">Selected tier, or null for legacy fallback behavior.</param>
    public void Setup(CustomerOrder order, LevelData data, MapDifficultyData difficulty) {
        currentOrder = order;
        levelData = data;
        difficultyData = difficulty;
    }

    void ResetState() {
        bodyCollider.enabled = true;
        lastRemainingShown = -1;

        if (pizzaInHand != null) pizzaInHand.SetActive(false);

        if (CustomerBodyVariety.Length > 0)
            spriteRenderer.sprite = CustomerBodyVariety[Random.Range(0, CustomerBodyVariety.Length)];

        timeLeft = currentOrder.waitTime;
        UpdateTimeDisplay();
        UpdateOrderIndicator();
    }

    void UpdateTimeDisplay() {
        if (fillImage != null) {
            fillImage.fillAmount = currentOrder.waitTime > 0 ? timeLeft / currentOrder.waitTime : 0f;
        }
        if (timeText != null) {
            timeText.text = Mathf.Ceil(timeLeft).ToString();
        }
    }

    // Called by Delivery while the player overlaps this customer. Returns how many
    // of the offered pizzas were actually accepted, so Delivery knows what's left.
    public int ReceivePizza(int offeredCount) {
        if (currentOrder == null || currentOrder.IsComplete) return 0;

        int accepted = Mathf.Min(offeredCount, currentOrder.RemainingPizzas);
        if (accepted <= 0) return 0;

        currentOrder.RegisterDelivery(accepted);
        if (customerManager != null) customerManager.RegisterDelivery(accepted);

        if (pizzaInHand != null) pizzaInHand.SetActive(true);

        if (scoreHandler != null) {
            int pizzaBaseReward = levelData != null ? Mathf.Max(0, levelData.pizzaBaseReward) : 0;
            int baseReward = difficultyData != null
                ? difficultyData.ApplyRewardMultiplier(accepted * pizzaBaseReward)
                : accepted * pizzaBaseReward;
            scoreHandler.AddMoney(baseReward);
            scoreHandler.AddScore(accepted * 10);
        }

        if (currentOrder.IsComplete) {
            CompleteOrder();
        }
        else {
            ExtendWaitForPartialDelivery();
        }

        return accepted;
    }

    void ExtendWaitForPartialDelivery() {
        float extension = levelData != null ? Mathf.Max(0f, levelData.partialExtension) : 0f;
        float capMultiplier = levelData != null ? Mathf.Max(0f, levelData.partialExtensionCapMult) : 1f;
        float cap = currentOrder.waitTime * capMultiplier;
        timeLeft = Mathf.Min(timeLeft + extension, cap);
        UpdateTimeDisplay();
    }

    void CompleteOrder() {
        if (leaveCoroutine != null) StopCoroutine(leaveCoroutine);
        bodyCollider.enabled = false;
        RefreshLanguage();

        float timeRatio = Mathf.Clamp01(timeLeft / currentOrder.waitTime);
        int tipPerPizza = levelData != null ? levelData.tipPerPizza : 0;
        float completionBonusBase = levelData != null ? Mathf.Max(0f, levelData.completionBonusBase) : 0f;
        float bonusExponent = levelData != null ? Mathf.Max(0f, levelData.bonusExponent) : 1f;
        float tipBeforeMultiplier = currentOrder.totalPizzas * tipPerPizza * timeRatio;
        float bonusBeforeMultiplier = completionBonusBase * Mathf.Pow(currentOrder.totalPizzas, bonusExponent);
        int tip = difficultyData != null ? difficultyData.ApplyRewardMultiplier(tipBeforeMultiplier) : Mathf.RoundToInt(tipBeforeMultiplier);
        int bonus = difficultyData != null ? difficultyData.ApplyRewardMultiplier(bonusBeforeMultiplier) : Mathf.RoundToInt(bonusBeforeMultiplier);

        if (scoreHandler != null) {
            scoreHandler.AddMoney(tip + bonus);
            scoreHandler.AddScore(Mathf.RoundToInt(timeLeft * 10));
            scoreHandler.RegisterCompletedOrder(currentOrder.totalPizzas);
        }

        float despawnDelay = levelData != null ? Mathf.Max(0f, levelData.completedDespawnDelay) : 0f;
        if (customerManager != null) customerManager.CustomerRoutine(gameObject, 0, despawnDelay);
    }

    IEnumerator LeaveAfterTime() {
        while (timeLeft > 0) {
            UpdateTimeDisplay();
            yield return oneSecond;
            timeLeft -= 1f;
        }

        leaveCoroutine = null;

        int remaining = currentOrder.RemainingPizzas;

        if (scoreHandler != null) {
            scoreHandler.RegisterMissedCustomer();
            int penaltyPerPizza = levelData != null ? Mathf.Max(0, levelData.failPenaltyPerPizza) : 0;
            if (remaining > 0) scoreHandler.AddMoney(-remaining * penaltyPerPizza);
        }

        float despawnDelay = levelData != null ? Mathf.Max(0f, levelData.timedOutDespawnDelay) : 0f;
        if (customerManager != null) customerManager.CustomerRoutine(gameObject, remaining, despawnDelay);
    }
}
