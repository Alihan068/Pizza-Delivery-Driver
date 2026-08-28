using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Customer : MonoBehaviour {

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
        // First activation happens before CustomerManager ever calls Setup() (the
        // objects start active in the scene and get switched off once at startup) -
        // ignore that spurious enable, the real one comes right after Setup().
        if (currentOrder == null) return;

        ResetState();
        leaveCoroutine = StartCoroutine(LeaveAfterTime());
    }

    private void OnDisable() {
        StopAllCoroutines();
        leaveCoroutine = null;
    }

    // Called by CustomerManager right before SetActive(true).
    public void Setup(CustomerOrder order, LevelData data) {
        currentOrder = order;
        levelData = data;
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
            scoreHandler.AddMoney(accepted * levelData.pizzaBaseReward);
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
        float cap = currentOrder.waitTime * levelData.partialExtensionCapMult;
        timeLeft = Mathf.Min(timeLeft + levelData.partialExtension, cap);
        UpdateTimeDisplay();
    }

    void CompleteOrder() {
        if (leaveCoroutine != null) StopCoroutine(leaveCoroutine);
        bodyCollider.enabled = false;
        if (timeText != null) timeText.text = "Thank You!";

        float timeRatio = Mathf.Clamp01(timeLeft / currentOrder.waitTime);
        int tip = Mathf.RoundToInt(currentOrder.totalPizzas * levelData.tipPerPizza * timeRatio);
        int bonus = Mathf.RoundToInt(levelData.completionBonusBase * Mathf.Pow(currentOrder.totalPizzas, levelData.bonusExponent));

        if (scoreHandler != null) {
            scoreHandler.AddMoney(tip + bonus);
            scoreHandler.AddScore(Mathf.RoundToInt(timeLeft * 10));
        }

        if (customerManager != null) customerManager.CustomerRoutine(gameObject, 0);
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
            if (remaining > 0) scoreHandler.AddMoney(-remaining * levelData.failPenaltyPerPizza);
        }

        if (customerManager != null) customerManager.CustomerRoutine(gameObject, remaining);
    }
}
