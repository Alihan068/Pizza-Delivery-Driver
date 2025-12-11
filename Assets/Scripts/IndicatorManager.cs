using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IndicatorManager : MonoBehaviour {
    [Header("Setup")]
    public GameObject indicatorPrefab; 
    public Transform uiCanvasParent;   

    [Header("Settings")]
    [SerializeField] float scanFrequency = 1.0f; 

    private HashSet<GameObject> trackedCustomers = new HashSet<GameObject>();

    void Start() {
        StartCoroutine(ScanForCustomers());
    }

    IEnumerator ScanForCustomers() {
        while (true) {
            CheckAndAddIndicators();
            yield return new WaitForSeconds(scanFrequency);
        }
    }

    void CheckAndAddIndicators() {
        GameObject[] activeCustomers = GameObject.FindGameObjectsWithTag("Customer");

        foreach (GameObject customerObj in activeCustomers) {
            if (trackedCustomers.Contains(customerObj)) continue;

            Customer customerScript = customerObj.GetComponent<Customer>();
            if (customerScript != null && customerScript.timeLeft > 0) {
                CreateIndicator(customerScript);
            }
        }

        trackedCustomers.RemoveWhere(item => item == null || !item.activeInHierarchy);
    }

    void CreateIndicator(Customer customer) {
        GameObject newUI = Instantiate(indicatorPrefab, uiCanvasParent);

        SmartIndicator indicatorScript = newUI.GetComponent<SmartIndicator>();
        if (indicatorScript != null) {
            indicatorScript.Initialize(customer);

        }

        trackedCustomers.Add(customer.gameObject);
    }
}