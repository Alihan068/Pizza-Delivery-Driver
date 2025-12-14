using UnityEngine;
using UnityEngine.UI;

public class Delivery : MonoBehaviour {
    [SerializeField] float destroyDelay = 0.2f;
    [SerializeField] Color32 noPizzaColor = new Color32(1, 1, 1, 255);
    [SerializeField] Color32 hasPizzaColor = new Color32(1, 1, 1, 255);

    GameUIManager gameUIManager;
    DriverTarget driverTarget;
    CustomerManager customerManager;
    AudioSource audioSource;

    [SerializeField] AudioClip pizzaCollectClip;
    [SerializeField] AudioClip pizzaDeliverClip;
    [SerializeField] AudioClip pizzaFailClip;

    [SerializeField] GameObject pizzaObject;
    int pizzaDelivered = 0;
    public float carryPizzaAmount;
    public float maxCarryPizzaAmount = 2;

    private void Start() {
        customerManager = FindFirstObjectByType<CustomerManager>();
        driverTarget = GetComponentInChildren<DriverTarget>();
        audioSource = GetComponent<AudioSource>();
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        pizzaObject.SetActive(false);

        driverTarget.SearchSetNavigation("Pizza");
    }

    private void OnTriggerEnter2D(Collider2D collision) {

        if (collision.gameObject.CompareTag("Customer") && carryPizzaAmount > 0) {
            DeliverPizza();
            pizzaDelivered++;
            gameUIManager.UpdatePizzaText(pizzaDelivered);
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
            carryPizzaAmount = 0;
            driverTarget.SearchSetNavigation("Pizza");
        }
    }

    public void DeliverPizza() {
        carryPizzaAmount -= 1;

        if (carryPizzaAmount <= 0) {
            carryPizzaAmount = 0;
            havePizzaStatus(false);
        }
    }

    public void LosePizza() {
        if (carryPizzaAmount > 0) {
            carryPizzaAmount -= 1;
            Debug.Log("Lost Pizza because customer is left!");

            if (carryPizzaAmount <= 0) {
                carryPizzaAmount = 0;
                havePizzaStatus(false);
            }
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