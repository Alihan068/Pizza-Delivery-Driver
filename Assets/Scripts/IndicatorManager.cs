using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IndicatorManager : MonoBehaviour {
    [Header("Setup")]
    public GameObject indicatorPrefab;
    public Transform uiCanvasParent;


    public void CreateIndicator(Customer customer) {

        if (customer == null || !customer.gameObject.activeInHierarchy) return;

        // İndikatörü yarat
        GameObject newUI = Instantiate(indicatorPrefab, uiCanvasParent);

        SmartIndicator indicatorScript = newUI.GetComponent<SmartIndicator>();
        if (indicatorScript != null) {
            indicatorScript.Initialize(customer);
        }
    }
}