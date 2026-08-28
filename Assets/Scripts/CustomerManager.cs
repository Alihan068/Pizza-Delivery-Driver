using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CustomerManager : MonoBehaviour {
    [SerializeField] LevelData levelData;

    [SerializeField] float minRespawnTime = 10f;
    [SerializeField] float maxRespawnTime = 15f;

    public int activeCustomers = 0;
    public int outstandingDemand = 0;

    GameObject[] allCustomers;
    readonly List<GameObject> inactiveCustomers = new List<GameObject>();

    IndicatorManager indicatorManager;

    void Start() {
        indicatorManager = FindFirstObjectByType<IndicatorManager>();

        allCustomers = GameObject.FindGameObjectsWithTag("Customer");
        foreach (GameObject customer in allCustomers) {
            customer.SetActive(false);
        }
    }

    // Called by a customer every time it accepts a delivery (partial or final),
    // so demand drains as pizzas actually arrive, not just when the order closes out.
    public void RegisterDelivery(int accepted) {
        outstandingDemand = Mathf.Max(0, outstandingDemand - accepted);
    }

    // Called by a customer when it finishes (served in full or timed out).
    // remainingDemand is whatever that order STILL needed and will never get now -
    // 0 for a completed order (RegisterDelivery already drained all of it), or the
    // leftover pizza count for one that timed out with an order still open.
    public void CustomerRoutine(GameObject customer, int remainingDemand) {
        activeCustomers--;
        if (activeCustomers < 0) activeCustomers = 0;

        outstandingDemand = Mathf.Max(0, outstandingDemand - remainingDemand);

        StartCoroutine(CustomerRespawnRoutine(customer, Random.Range(minRespawnTime, maxRespawnTime)));
    }

    IEnumerator CustomerRespawnRoutine(GameObject customer, float respawnTime) {
        yield return new WaitForSeconds(respawnTime);
        customer.SetActive(false);
    }

    public void GetCustomer() {
        inactiveCustomers.Clear();

        foreach (GameObject customer in allCustomers) {
            if (!customer.activeInHierarchy) {
                inactiveCustomers.Add(customer);
            }
        }

        if (inactiveCustomers.Count == 0) return;

        // Pick Random inavtive Customer
        GameObject selectedCustomerObj = inactiveCustomers[Random.Range(0, inactiveCustomers.Count)];

        int capacity = GameManager.Instance != null ? GameManager.Instance.GetCapacity() : 2;
        int orderMax = Mathf.Max(levelData.orderMin + 1, Mathf.RoundToInt(capacity * levelData.orderScale));
        int totalPizzas = Random.Range(levelData.orderMin, orderMax + 1);
        float waitTime = levelData.waitBase + levelData.waitPerOrderPizza * totalPizzas;

        Customer customerScript = selectedCustomerObj.GetComponent<Customer>();
        customerScript.Setup(new CustomerOrder(totalPizzas, waitTime), levelData);

        selectedCustomerObj.SetActive(true);
        activeCustomers++;
        outstandingDemand += totalPizzas;

        if (indicatorManager != null) {
            indicatorManager.CreateIndicator(customerScript);
        }
    }
}
