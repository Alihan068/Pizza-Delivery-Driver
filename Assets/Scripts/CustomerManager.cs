using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CustomerManager : MonoBehaviour {
    [SerializeField] LevelData levelData;
    [SerializeField] float demandCheckInterval = 0.5f;

    /// <summary>Number of logically active customer orders in this shift.</summary>
    public int activeCustomers = 0;

    /// <summary>Total number of pizzas still owed across active customer orders.</summary>
    public int outstandingDemand = 0;

    /// <summary>How many customers have spawned this shift. Feeds the reputation on-time rate.</summary>
    public int ordersOffered = 0;

    /// <summary>Map-wide tuning used by this shift, or null when the legacy fallback is active.</summary>
    public LevelData LevelData => levelData;

    /// <summary>Difficulty rules active for the current map shift.</summary>
    public MapDifficultyData DifficultyData { get; private set; }

    GameObject[] allCustomers;
    readonly List<GameObject> inactiveCustomers = new List<GameObject>();

    IndicatorManager indicatorManager;
    Delivery delivery;
    ScoreHandler scoreHandler;
    bool firstShiftTutorialOrderUsed;

    void Start() {
        ResolveActiveTuning();
        indicatorManager = FindFirstObjectByType<IndicatorManager>();
        delivery = FindFirstObjectByType<Delivery>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();

        allCustomers = GameObject.FindGameObjectsWithTag("Customer");
        foreach (GameObject customer in allCustomers) {
            customer.SetActive(false);
        }

        StartCoroutine(DemandCheckRoutine());
    }

    void ResolveActiveTuning() {
        GameManager manager = GameManager.Instance;
        if (manager != null && manager.currentMap != null && manager.currentMap.levelData != null) {
            levelData = manager.currentMap.levelData;
            DifficultyData = manager.GetCurrentDifficulty();
        }

        if (levelData == null) {
            Debug.LogWarning("CustomerManager has no active LevelData. Legacy scene fallback will be used.");
        }
    }

    // The invariant that keeps the pizza-loop from deadlocking: as long as the
    // driver is carrying more pizzas than outstanding demand covers, spawn more
    // customers. Runs continuously (not just on pickup) so a customer timing out
    // while the driver's inventory is full still gets replaced.
    IEnumerator DemandCheckRoutine() {
        var wait = new WaitForSeconds(demandCheckInterval);
        while (true) {
            yield return wait;
            EnsureDemandCoversCarry();
        }
    }

    void EnsureDemandCoversCarry() {
        if (delivery == null) delivery = FindFirstObjectByType<Delivery>();
        if (delivery == null || levelData == null) return;

        while (outstandingDemand < delivery.carryPizzaAmount
               && activeCustomers < levelData.maxActiveCustomers) {
            if (!GetCustomer()) break;
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
    public void CustomerRoutine(GameObject customer, int remainingDemand, float despawnDelay) {
        activeCustomers--;
        if (activeCustomers < 0) activeCustomers = 0;

        outstandingDemand = Mathf.Max(0, outstandingDemand - remainingDemand);

        StartCoroutine(CustomerRespawnRoutine(customer, despawnDelay));
    }

    IEnumerator CustomerRespawnRoutine(GameObject customer, float respawnTime) {
        yield return new WaitForSeconds(respawnTime);
        customer.SetActive(false);
    }

    // Returns false if the customer pool is exhausted (no inactive customer to spawn).
    public bool GetCustomer() {
        inactiveCustomers.Clear();

        foreach (GameObject customer in allCustomers) {
            if (!customer.activeInHierarchy) {
                inactiveCustomers.Add(customer);
            }
        }

        if (inactiveCustomers.Count == 0) return false;

        // Pick Random inavtive Customer
        GameObject selectedCustomerObj = inactiveCustomers[Random.Range(0, inactiveCustomers.Count)];

        int capacity = GameManager.Instance != null ? GameManager.Instance.GetShiftCapacity() : 2;
        bool shouldDemonstratePartialDelivery = levelData != null && levelData.demonstratePartialDeliveryOnFirstShift &&
            !firstShiftTutorialOrderUsed && GameManager.Instance != null &&
            !GameManager.Instance.IsFreeplayMode && GameManager.Instance.totalShiftsSettled == 0;
        int totalPizzas;
        MapDifficultyData activeDifficulty = DifficultyData;
        int legacyOrderMin = levelData != null ? levelData.orderMin : 1;
        float legacyOrderScale = levelData != null ? levelData.orderScale : 0.6f;
        int orderMin = activeDifficulty != null ? activeDifficulty.GetSafeOrderMin() : Mathf.Max(1, legacyOrderMin);
        int orderMax;
        if (activeDifficulty != null) {
            orderMax = activeDifficulty.GetSafeOrderMax();
        }
        else {
            orderMax = Mathf.Max(orderMin + 1, Mathf.CeilToInt(capacity * legacyOrderScale));
        }

        if (shouldDemonstratePartialDelivery) {
            int tutorialOrder = Mathf.Clamp(capacity + 1, orderMin, orderMax);
            if (tutorialOrder > capacity) {
                totalPizzas = tutorialOrder;
                firstShiftTutorialOrderUsed = true;
            }
            else {
                totalPizzas = activeDifficulty != null ? activeDifficulty.RollOrderAmount() : Random.Range(orderMin, orderMax + 1);
            }
        }
        else {
            totalPizzas = activeDifficulty != null ? activeDifficulty.RollOrderAmount() : Random.Range(orderMin, orderMax + 1);
        }
        float waitBase = levelData != null ? levelData.waitBase : 40f;
        float waitPerPizza = levelData != null ? levelData.waitPerOrderPizza : 0f;
        float baseWaitTime = waitBase + waitPerPizza * totalPizzas;
        float shiftProgress = scoreHandler != null ? scoreHandler.ShiftProgress01 : 0f;
        float waitTime = levelData != null ? baseWaitTime * levelData.GetCustomerWaitMultiplier(shiftProgress) : baseWaitTime;

        Customer customerScript = selectedCustomerObj.GetComponent<Customer>();
        customerScript.Setup(new CustomerOrder(totalPizzas, waitTime), levelData, DifficultyData);

        selectedCustomerObj.SetActive(true);
        activeCustomers++;
        ordersOffered++;
        outstandingDemand += totalPizzas;

        if (indicatorManager != null) {
            indicatorManager.CreateIndicator(customerScript);
        }

        return true;
    }
}
