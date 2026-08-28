using UnityEngine;

public class Delivery : MonoBehaviour {
    [Header("Settings")]
    [SerializeField] float destroyDelay = 0.2f;
    public int carryPizzaAmount;
    public int maxCarryPizzaAmount;

    float protectionChance = 0f;

    [Header("References")]
    [SerializeField] GameObject pizzaObject;
    [SerializeField] GameObject wastedPizzaPrefab;

    GameUIManager gameUIManager;
    DriverTarget driverTarget;
    CustomerManager customerManager;
    AudioSource audioSource;
    ScoreHandler scoreHandler;
    Driver driver;

    [Header("Audio")]
    [SerializeField] AudioClip pizzaCollectClip;
    [SerializeField] AudioClip pizzaDeliverClip;
    [SerializeField] AudioClip pizzaFailClip;

    public int pizzaDelivered = 0;

    private void Start() {
        customerManager = FindFirstObjectByType<CustomerManager>();
        driverTarget = GetComponentInChildren<DriverTarget>();
        audioSource = GetComponent<AudioSource>();
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        driver = GetComponent<Driver>();

        pizzaObject.SetActive(false);

        if (GameManager.Instance != null) {
            maxCarryPizzaAmount = GameManager.Instance.GetCapacity();
            protectionChance = GameManager.Instance.GetProtectionChance();
        }
        else {
            maxCarryPizzaAmount = 2;
            protectionChance = 0f;
        }

        driverTarget.SearchSetNavigation("Pizza");
        UpdateCarryUI();
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

    private void OnTriggerEnter2D(Collider2D collision) {
        if (collision.gameObject.CompareTag("Pizza")) {
            if (carryPizzaAmount < maxCarryPizzaAmount) {
                PickupPizza();
                Destroy(collision.gameObject, destroyDelay);
                TryPlayAudioClip(pizzaCollectClip);
            }
            else {
                TryPlayAudioClip(pizzaFailClip);
            }
        }
    }

    // Stay (not Enter) - an order can need more pizzas than the driver is carrying,
    // so the customer's collider stays open across multiple visits until its order
    // is complete. Each tick here just hands over whatever's left to give.
    private void OnTriggerStay2D(Collider2D collision) {
        if (carryPizzaAmount <= 0) return;
        if (!collision.gameObject.CompareTag("Customer")) return;

        Customer customer = collision.gameObject.GetComponent<Customer>();
        if (customer == null) return;

        int accepted = customer.ReceivePizza(carryPizzaAmount);
        if (accepted <= 0) return;

        carryPizzaAmount -= accepted;
        pizzaDelivered += accepted;
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
            if (carryPizzaAmount <= 0) driverTarget.SearchSetNavigation("Pizza");
            else driverTarget.SearchSetNavigation("Customer");
        }
    }

    public void PickupPizza() {
        carryPizzaAmount += 1;
        UpdateCarryUI();
        if (customerManager != null && carryPizzaAmount >= customerManager.outstandingDemand) {
            customerManager.GetCustomer();
        }
        havePizzaStatus(true);
    }
}
