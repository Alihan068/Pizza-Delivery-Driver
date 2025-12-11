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
    public bool hasPizza;

    private void Start() {
        customerManager = FindFirstObjectByType<CustomerManager>();
        driverTarget = GetComponentInChildren<DriverTarget>();
        audioSource = GetComponent<AudioSource>();
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        pizzaObject.SetActive(false);

        driverTarget.SearchSetNavigation("Pizza");           
    }

    private void OnTriggerEnter2D(Collider2D collision) {
        if (collision.gameObject.CompareTag("Customer") && hasPizza) {
            havePizzaStatus(false);
            pizzaDelivered++;
            gameUIManager.UpdatePizzaText(pizzaDelivered);
            TryPlayAudioClip(pizzaDeliverClip);
            collision.gameObject.GetComponent<Customer>().ReceivePizza();
            //Debug.Log("Delivery Complete!");
        }

        if (collision.gameObject.CompareTag("Pizza") && !hasPizza) {
            havePizzaStatus(true);
            Destroy(collision.gameObject, destroyDelay);
            TryPlayAudioClip(pizzaCollectClip);
            //Debug.Log("Pizza Picked Up");
        }
        else if (hasPizza && collision.gameObject.CompareTag("Pizza")) {
            TryPlayAudioClip(pizzaFailClip);
            //Debug.Log("Already carrying Pizza");
        }
        else { 
        return;
        }

        void TryPlayAudioClip(AudioClip clip) {
            if (clip != null && audioSource != null)
                audioSource.PlayOneShot(clip);
            else
                Debug.LogWarning("AudioClip is not assigned.");
        }
    }

    public void havePizzaStatus(bool havePizza) {
        pizzaObject.SetActive(havePizza);
        hasPizza = havePizza;
        if (havePizza) {
            driverTarget.SearchSetNavigation("Customer");
            customerManager.GetCustomer();
        }
        else {
            driverTarget.SearchSetNavigation("Pizza");
        }
    }
}
