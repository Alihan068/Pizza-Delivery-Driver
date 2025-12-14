using UnityEngine;

public class Delivery : MonoBehaviour {
    [Header("Settings")]
    [SerializeField] float destroyDelay = 0.2f;
    public float carryPizzaAmount;
    public float maxCarryPizzaAmount;

    float protectionChance = 0f;

    [Header("References")]
    [SerializeField] GameObject pizzaObject;
    [SerializeField] GameObject wastedPizzaPrefab; 

    GameUIManager gameUIManager;
    DriverTarget driverTarget;
    CustomerManager customerManager;
    AudioSource audioSource;
    ScoreHandler scoreHandler; 

    [Header("Audio")]
    [SerializeField] AudioClip pizzaCollectClip;
    [SerializeField] AudioClip pizzaDeliverClip;
    [SerializeField] AudioClip pizzaFailClip;

    int pizzaDelivered = 0;

    private void Start() {
        customerManager = FindFirstObjectByType<CustomerManager>();
        driverTarget = GetComponentInChildren<DriverTarget>();
        audioSource = GetComponent<AudioSource>();
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();

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
    }

    public void AttemptDropPizza(Vector3 dropPosition) {
        if (carryPizzaAmount <= 0) return;


        if (Random.value < protectionChance) {
            Debug.Log("Pizza is saved");
            return;
        }

        Instantiate(wastedPizzaPrefab, dropPosition, Quaternion.identity);
        if (scoreHandler != null) scoreHandler.AddScore(-50);

        LosePizza(true);
    }

    public void LosePizza(bool updateVisuals) {
        if (carryPizzaAmount > 0) {
            carryPizzaAmount -= 1;

            if (carryPizzaAmount <= 0) {
                carryPizzaAmount = 0;
                if (updateVisuals) havePizzaStatus(false);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision) {
        if (collision.gameObject.CompareTag("Customer") && carryPizzaAmount > 0) {
            DeliverPizza();
            pizzaDelivered++;
            if (gameUIManager != null) gameUIManager.UpdatePizzaText(pizzaDelivered);
            TryPlayAudioClip(pizzaDeliverClip);
            collision.gameObject.GetComponent<Customer>().ReceivePizza();
        }

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

    public void DeliverPizza() {
        carryPizzaAmount -= 1;
        if (carryPizzaAmount <= 0) {
            carryPizzaAmount = 0;
            havePizzaStatus(false);
        }
    }

    public void PickupPizza() {
        carryPizzaAmount += 1;
        if (carryPizzaAmount > customerManager.activeCustomers) {
            customerManager.GetCustomer();
        }
        havePizzaStatus(true);
    }
}