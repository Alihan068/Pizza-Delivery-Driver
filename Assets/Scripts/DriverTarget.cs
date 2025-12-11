using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DriverTarget : MonoBehaviour {
    [SerializeField] float scanFrequency = 0.5f;
    

    string targetTag;
    GameObject currentTarget;
    SpriteRenderer arrow;
    Coroutine searchCoroutine;

    void Start() {
        arrow = GetComponentInChildren<SpriteRenderer>();
        arrow.enabled = false;
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

        GameObject closestTarget = null;
        float minDistance = Mathf.Infinity;
        Vector2 myPos = transform.position;

        foreach (GameObject target in allTargets) {

            if (target == null) continue;

            float distance = Vector2.Distance(target.transform.position, myPos);
            if (distance < minDistance) {
                closestTarget = target;
                minDistance = distance;
            }

            
        }
        currentTarget = closestTarget;
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
