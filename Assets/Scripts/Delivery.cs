using UnityEngine;

public class Delivery : MonoBehaviour {
    public int carryPizzaAmount;
    public int maxCarryPizzaAmount;

    float protectionChance = 0f;

    [Header("References")]
    [SerializeField] GameObject pizzaObject;
    [SerializeField] GameObject wastedPizzaPrefab;
    [Tooltip("Legacy score penalty used only when the selected map has no difficulty tier.")]
    [SerializeField] int legacyScorePenaltyPerPizzaLost = 50;

    GameUIManager gameUIManager;
    DriverTarget driverTarget;
    CustomerManager customerManager;
    LevelData levelData;
    MapDifficultyData difficultyData;
    AudioSource audioSource;
    PizzaDeliveryEffect deliveryEffect;
    ScoreHandler scoreHandler;
    Driver driver;

    [Header("Audio")]
    [SerializeField] AudioClip pizzaDeliverClip;

    public int pizzaDelivered = 0;

    public bool IsFull => carryPizzaAmount >= maxCarryPizzaAmount;

    private void Start() {
        customerManager = FindFirstObjectByType<CustomerManager>();
        levelData = customerManager != null ? customerManager.LevelData : null;
        difficultyData = customerManager != null ? customerManager.DifficultyData : null;
        if (difficultyData == null && GameManager.Instance != null)
            difficultyData = GameManager.Instance.CurrentDifficulty;
        driverTarget = GetComponentInChildren<DriverTarget>();
        audioSource = GetComponent<AudioSource>();
        deliveryEffect = GetComponent<PizzaDeliveryEffect>();
        if (deliveryEffect == null) deliveryEffect = gameObject.AddComponent<PizzaDeliveryEffect>();
        if (pizzaObject != null) deliveryEffect.Configure(pizzaObject.GetComponent<SpriteRenderer>());
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        driver = GetComponent<Driver>();

        pizzaObject.SetActive(false);

        if (GameManager.Instance != null) {
            maxCarryPizzaAmount = GameManager.Instance.GetShiftCapacity();
            protectionChance = GameManager.Instance.GetShiftStatValue(VehicleStatId.Protection);
        }
        else {
            maxCarryPizzaAmount = 2;
            protectionChance = 0f;
        }

        driverTarget.SearchSetNavigation("CollectPoint");
        UpdateCarryUI();
    }

    /// <summary>
    /// Attempts to add one pizza to the carried inventory when the driver has room.
    /// </summary>
    /// <param name="point">The collect point that supplied the pizza.</param>
    /// <remarks>
    /// When the bank can cover the authored pizza cost, that cost is removed. A zero or insufficient
    /// bank balance grants the pizza for free so the shift cannot become stuck at a pickup point.
    /// </remarks>
    /// <returns><see langword="true"/> when one pizza was added; otherwise <see langword="false"/>.</returns>
    public bool CollectPizza(PizzaCollectPoint point) {
        if (IsFull) return false;

        int cost = levelData != null ? levelData.pizzaCost : 0;
        GameManager gameManager = GameManager.Instance;
        if (cost > 0 && gameManager != null && gameManager.totalMoney >= cost) {
            gameManager.TrySpendMoney(cost);
        }

        carryPizzaAmount += 1;
        UpdateCarryUI();
        havePizzaStatus(true);
        return true;
    }

    /// <summary>
    /// Shows one pizza travelling from a collection point into this vehicle.
    /// </summary>
    /// <param name="sourcePosition">World position where the collection point supplied the pizza.</param>
    /// <remarks>
    /// The carried-pizza transform is used as a moving target so the visual remains attached to
    /// the vehicle while it drives away. Inventory state is already updated by <see cref="CollectPizza"/>
    /// before this method is called.
    /// </remarks>
    public void PlayPizzaCollectionEffect(Vector3 sourcePosition) {
        if (deliveryEffect == null) return;

        Transform target = pizzaObject != null ? pizzaObject.transform : transform;
        deliveryEffect.Play(sourcePosition, target, 1, null);
    }

    /// <summary>
    /// Resolves the selected map tier's pizza-loss budget for one damaging impact.
    /// </summary>
    /// <param name="dropPosition">World position where lost pizza visuals are spawned.</param>
    /// <remarks>
    /// The authored value is an unbounded percentage budget. Stabilizer reduces that budget and
    /// the remaining fractional part is rolled once, allowing values above 100 to guarantee more
    /// than one lost pizza without adding special-case rules to each difficulty tier.
    /// </remarks>
    public void AttemptDropPizza(Vector3 dropPosition) {
        if (carryPizzaAmount <= 0) return;

        float authoredLossPercent = difficultyData != null ? difficultyData.pizzaLossChancePercent : 100f;
        float randomPercent = Random.value * 100f;
        int lossCount = MapDifficultyRules.CalculatePizzaLossCount(authoredLossPercent, protectionChance,
            randomPercent, carryPizzaAmount);
        if (lossCount <= 0) {
            if (driver != null) driver.PlayProtectionFlash();
            return;
        }

        int scorePenalty = difficultyData != null ? difficultyData.scorePenaltyPerPizzaLost : legacyScorePenaltyPerPizzaLost;
        if (wastedPizzaPrefab != null) {
            for (int i = 0; i < lossCount; i++)
                Instantiate(wastedPizzaPrefab, dropPosition, Quaternion.identity);
        }
        if (scoreHandler != null) scoreHandler.AddScore(-Mathf.Max(0, scorePenalty) * lossCount);

        LosePizza(lossCount);
        if (scoreHandler != null) {
            for (int i = 0; i < lossCount; i++) scoreHandler.RegisterPizzaLost();
        }
    }

    /// <summary>Removes a requested number of pizzas from the carried inventory.</summary>
    /// <param name="amount">Number of pizzas to remove, clamped to the current inventory.</param>
    public void LosePizza(int amount) {
        if (carryPizzaAmount <= 0) return;

        carryPizzaAmount -= Mathf.Clamp(amount, 0, carryPizzaAmount);
        UpdateCarryUI();
        if (carryPizzaAmount <= 0) {
            carryPizzaAmount = 0;
            havePizzaStatus(false);
        }
    }

    void UpdateCarryUI() {
        if (gameUIManager != null) gameUIManager.UpdateCarryText(carryPizzaAmount, maxCarryPizzaAmount);
    }

    // An order can need more pizzas than the driver is carrying, so the customer's
    // collider stays open across multiple visits until its order is complete. Each
    // tick here just hands over whatever's left to give.
    private void OnTriggerStay2D(Collider2D collision) {
        if (carryPizzaAmount <= 0) return;

        CustomerDeliveryZone deliveryZone = collision.GetComponent<CustomerDeliveryZone>();
        if (deliveryZone == null) return;

        Customer customer = deliveryZone.Owner;
        if (customer == null) return;

        int accepted = customer.ReceivePizza(carryPizzaAmount);
        if (accepted <= 0) return;

        carryPizzaAmount -= accepted;
        pizzaDelivered += accepted;
        if (scoreHandler != null) scoreHandler.RegisterDeliveredPizzas(accepted);
        UpdateCarryUI();

        if (gameUIManager != null) gameUIManager.UpdatePizzaText(pizzaDelivered);
        TryPlayAudioClip(pizzaDeliverClip);
        if (deliveryEffect != null)
            deliveryEffect.Play(pizzaObject != null ? pizzaObject.transform.position : transform.position,
                customer.PizzaDeliveryTarget.position, accepted,
                customer.ShowDeliveredPizza);
        else
            customer.ShowDeliveredPizza();

        if (carryPizzaAmount <= 0) {
            carryPizzaAmount = 0;
            havePizzaStatus(false);
        }
    }

    void TryPlayAudioClip(AudioClip clip) {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    public void havePizzaStatus(bool havePizza) {
        pizzaObject.SetActive(havePizza);
        if (havePizza) {
            driverTarget.SearchSetNavigation("Customer");
        }
        else {
            if (carryPizzaAmount <= 0) driverTarget.SearchSetNavigation("CollectPoint");
            else driverTarget.SearchSetNavigation("Customer");
        }
    }
}
