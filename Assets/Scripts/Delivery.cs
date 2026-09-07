using UnityEngine;

public class Delivery : MonoBehaviour {
    public int carryPizzaAmount;
    public int maxCarryPizzaAmount;

    float protectionChance = 0f;

    [Header("References")]
    [SerializeField] GameObject pizzaObject;
    [SerializeField] GameObject wastedPizzaPrefab;

    GameUIManager gameUIManager;
    DriverTarget driverTarget;
    CustomerManager customerManager;
    LevelData levelData;
    AudioSource audioSource;
    ScoreHandler scoreHandler;
    Driver driver;

    [Header("Audio")]
    [SerializeField] AudioClip pizzaDeliverClip;

    public int pizzaDelivered = 0;

    public bool IsFull => carryPizzaAmount >= maxCarryPizzaAmount;

    private void Start() {
        customerManager = FindFirstObjectByType<CustomerManager>();
        levelData = customerManager != null ? customerManager.LevelData : null;
        driverTarget = GetComponentInChildren<DriverTarget>();
        audioSource = GetComponent<AudioSource>();
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

    public void CollectPizza(PizzaCollectPoint point) {
        if (IsFull) return;

        int cost = levelData != null ? levelData.pizzaCost : 0;
        int spendable = 0;
        if (GameManager.Instance != null) spendable += GameManager.Instance.totalMoney;
        if (scoreHandler != null) spendable += scoreHandler.sessionEarnings;

        int charge = Mathf.Clamp(cost, 0, Mathf.Max(0, spendable));
        if (charge > 0 && scoreHandler != null) scoreHandler.AddMoney(-charge);

        carryPizzaAmount += 1;
        UpdateCarryUI();
        havePizzaStatus(true);
    }

    public void AttemptDropPizza(Vector3 dropPosition) {
        if (carryPizzaAmount <= 0) return;

        if (Random.value < protectionChance) {
            if (driver != null) driver.PlayProtectionFlash();
            return;
        }

        Instantiate(wastedPizzaPrefab, dropPosition, Quaternion.identity);
        if (scoreHandler != null) scoreHandler.AddScore(-50);

        LosePizza();
        if (scoreHandler != null) scoreHandler.RegisterPizzaLost();
    }

    public void LosePizza() {
        if (carryPizzaAmount <= 0) return;

        carryPizzaAmount -= 1;
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
        if (!collision.gameObject.CompareTag("Customer")) return;

        Customer customer = collision.gameObject.GetComponent<Customer>();
        if (customer == null) return;

        int accepted = customer.ReceivePizza(carryPizzaAmount);
        if (accepted <= 0) return;

        carryPizzaAmount -= accepted;
        pizzaDelivered += accepted;
        if (scoreHandler != null) scoreHandler.RegisterDeliveredPizzas(accepted);
        UpdateCarryUI();

        if (gameUIManager != null) gameUIManager.UpdatePizzaText(pizzaDelivered);
        TryPlayAudioClip(pizzaDeliverClip);

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
