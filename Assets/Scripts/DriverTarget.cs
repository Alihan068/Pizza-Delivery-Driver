using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DriverTarget : MonoBehaviour {
    [SerializeField] float scanFrequency = 0.5f;


    string targetTag;
    GameObject currentTarget;
    SpriteRenderer arrow;
    Delivery delivery;
    Coroutine searchCoroutine;

    void Start() {
        arrow = GetComponentInChildren<SpriteRenderer>();
        arrow.enabled = false;
        delivery = GetComponentInParent<Delivery>();
    }
    public void SearchSetNavigation(string targetTag) {

        if (searchCoroutine != null) {
            StopCoroutine(searchCoroutine);
        }

        currentTarget = null;

        searchCoroutine = StartCoroutine(FindClosestTargetRoutine(targetTag));
    }
    IEnumerator FindClosestTargetRoutine(string targetTag) {
        while (true) {
            FindClosestTarget(targetTag);
            yield return new WaitForSeconds(scanFrequency);
        }
    }

    void FindClosestTarget(string targetTag) {
        GameObject[] allTargets = GameObject.FindGameObjectsWithTag(targetTag);
        Vector2 myPos = transform.position;

        currentTarget = targetTag == "Customer"
            ? FindBestCustomerTarget(allTargets, myPos)
            : FindClosestOf(allTargets, myPos);
    }

    GameObject FindClosestOf(GameObject[] targets, Vector2 myPos) {
        GameObject closestTarget = null;
        float minDistance = Mathf.Infinity;

        foreach (GameObject target in targets) {
            if (target == null) continue;

            float distance = Vector2.Distance(target.transform.position, myPos);
            if (distance < minDistance) {
                closestTarget = target;
                minDistance = distance;
            }
        }
        return closestTarget;
    }

    // Prefer the closest customer whose remaining order the driver can fully
    // satisfy right now, so the arrow doesn't send an almost-empty driver across
    // the map to a customer who needs more pizzas than they're carrying.
    GameObject FindBestCustomerTarget(GameObject[] customers, Vector2 myPos) {
        int carried = delivery != null ? delivery.carryPizzaAmount : 0;

        GameObject bestFulfillable = null;
        float bestFulfillableDist = Mathf.Infinity;
        GameObject bestAny = null;
        float bestAnyDist = Mathf.Infinity;

        foreach (GameObject target in customers) {
            if (target == null || !target.activeInHierarchy) continue;

            Customer customer = target.GetComponent<Customer>();
            if (customer == null || customer.currentOrder == null) continue;

            float distance = Vector2.Distance(target.transform.position, myPos);

            if (distance < bestAnyDist) {
                bestAnyDist = distance;
                bestAny = target;
            }

            int remaining = customer.currentOrder.RemainingPizzas;
            if (remaining > 0 && remaining <= carried && distance < bestFulfillableDist) {
                bestFulfillableDist = distance;
                bestFulfillable = target;
            }
        }

        return bestFulfillable != null ? bestFulfillable : bestAny;
    }

    private void Update() {
        if (currentTarget != null) {

            if (arrow.enabled == false) {
                arrow.enabled = true;
            }

            transform.right = currentTarget.transform.position - transform.position;
        }
        else {
            if (arrow.enabled == true) {
                arrow.enabled = false;
            }
                //Debug.Log("No target yet");
        }
    }
}
