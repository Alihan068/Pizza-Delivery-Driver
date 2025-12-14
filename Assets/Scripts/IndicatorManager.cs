using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IndicatorManager : MonoBehaviour {
    [Header("Setup")]
    public GameObject indicatorPrefab;
    public Transform uiCanvasParent;

    [Header("Settings")]
    [SerializeField] float scanFrequency = 0.5f; // Tarama hızını biraz artırdım (daha seri olsun diye)

    // HashSet yerine Dictionary kullanıyoruz: <Müşteri, Onun Oku>
    private Dictionary<GameObject, SmartIndicator> trackedCustomers = new Dictionary<GameObject, SmartIndicator>();

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
        // Önce temizlik: Oku (Indicator) silinmiş olan müşterileri listeden çıkar
        List<GameObject> keysToRemove = new List<GameObject>();
        foreach (var pair in trackedCustomers) {
            // Eğer müşteri yoksa, pasifse VEYA oluşturduğumuz ok (SmartIndicator) yok olmuşsa
            if (pair.Key == null || !pair.Key.activeInHierarchy || pair.Value == null) {
                keysToRemove.Add(pair.Key);
            }
        }

        // Listeden temizle
        foreach (GameObject key in keysToRemove) {
            trackedCustomers.Remove(key);
        }

        // Şimdi yeni müşterileri tara
        GameObject[] activeCustomers = GameObject.FindGameObjectsWithTag("Customer");

        foreach (GameObject customerObj in activeCustomers) {
            // Eğer zaten takip ediyorsak ve oku da hala duruyorsa atla
            if (trackedCustomers.ContainsKey(customerObj) && trackedCustomers[customerObj] != null) continue;

            Customer customerScript = customerObj.GetComponent<Customer>();
            if (customerScript != null && customerScript.timeLeft > 0) {
                CreateIndicator(customerScript);
            }
        }
    }

    void CreateIndicator(Customer customer) {
        GameObject newUI = Instantiate(indicatorPrefab, uiCanvasParent);

        SmartIndicator indicatorScript = newUI.GetComponent<SmartIndicator>();
        if (indicatorScript != null) {
            indicatorScript.Initialize(customer);

            // Eğer listede zaten anahtar varsa (ama değeri null ise) güncelle, yoksa ekle
            if (trackedCustomers.ContainsKey(customer.gameObject)) {
                trackedCustomers[customer.gameObject] = indicatorScript;
            }
            else {
                trackedCustomers.Add(customer.gameObject, indicatorScript);
            }
        }
    }
}