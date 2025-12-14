using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class CustomerManager : MonoBehaviour {
    Coroutine customerRespawnCoroutine;
    [SerializeField] float minRespawnTime = 10f;
    [SerializeField] float maxRespawnTime = 15f;

    public int activeCustomers = 0;

    GameObject[] allCustomers;
    Delivery deliveryScript;

    IndicatorManager indicatorManager;

    void Start() {
        deliveryScript = FindFirstObjectByType<Delivery>();

        indicatorManager = FindFirstObjectByType<IndicatorManager>();

        allCustomers = GameObject.FindGameObjectsWithTag("Customer");
        foreach (GameObject customer in allCustomers) {
            customer.SetActive(false);
        }
    }

    public void CustomerRoutine(GameObject customer) {
        activeCustomers--;
        if (activeCustomers < 0) activeCustomers = 0;

        if (deliveryScript != null && activeCustomers < deliveryScript.carryPizzaAmount) {
            deliveryScript.LosePizza();
        }

        StartCoroutine(CustomerRespawnRoutine(customer, Random.Range((int)minRespawnTime, (int)maxRespawnTime)));
    }

    IEnumerator CustomerRespawnRoutine(GameObject customer, int respawnTime) {
        yield return new WaitForSeconds(1f);
        customer.SetActive(false);
    }

    public void GetCustomer() {
        List<GameObject> inactiveCustomers = new List<GameObject>();

        foreach (GameObject customer in allCustomers) {
            if (!customer.activeInHierarchy) {
                inactiveCustomers.Add(customer);
            }
        }

        if (inactiveCustomers.Count == 0) return;

        // Pick Random inavtive Customer
        GameObject selectedCustomerObj = inactiveCustomers[Random.Range(0, inactiveCustomers.Count)];

        selectedCustomerObj.SetActive(true);
        activeCustomers++;

        if (indicatorManager != null) {
            indicatorManager.CreateIndicator(selectedCustomerObj.GetComponent<Customer>());
        }
    }
}