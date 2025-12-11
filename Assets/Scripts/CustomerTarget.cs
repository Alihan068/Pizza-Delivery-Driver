using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct CriticalCustomerInfo {
    public GameObject customerObject; 
    public float remainingTime;       
    public float distanceToPlayer;    
}

public class CustomerTarget : MonoBehaviour
{
    [SerializeField] int trackCount = 3;

    [SerializeField] float scanFrequency = 0.5f;

    [SerializeField] Transform playerTransform;


    public List<CriticalCustomerInfo> criticalCustomers = new List<CriticalCustomerInfo>();


    Coroutine scanCoroutine;

    private void Start() {
        
        if (playerTransform == null) {
            playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (playerTransform == null) playerTransform = this.transform; 
        }

        StartScan();
    }
    public void StartScan() {
        if (scanCoroutine != null) StopCoroutine(scanCoroutine);
        scanCoroutine = StartCoroutine(ScanRoutine());
    }

    IEnumerator ScanRoutine() {
        while (true) {
            FindCriticalCustomers();
            yield return new WaitForSeconds(scanFrequency);
        }
    }

    void FindCriticalCustomers() {
       
        GameObject[] allCustomers = GameObject.FindGameObjectsWithTag("Customer");

        List<CriticalCustomerInfo> tempList = new List<CriticalCustomerInfo>();
        Vector3 playerPos = playerTransform.position;

        foreach (GameObject customerObj in allCustomers) {
            if (customerObj == null || !customerObj.activeInHierarchy) continue;

            Customer customerScript = customerObj.GetComponent<Customer>();

        
            if (customerScript == null || customerScript.timeLeft <= 0) continue;

            CriticalCustomerInfo info = new CriticalCustomerInfo();
            info.customerObject = customerObj;
            info.remainingTime = customerScript.timeLeft;
            info.distanceToPlayer = Vector2.Distance(playerPos, customerObj.transform.position);

            tempList.Add(info);
        }

        tempList.Sort((a, b) => a.remainingTime.CompareTo(b.remainingTime));

        criticalCustomers.Clear();

        int count = Mathf.Min(trackCount, tempList.Count);
        for (int i = 0; i < count; i++) {
            criticalCustomers.Add(tempList[i]);
        }
    }

    private void OnDrawGizmosSelected() {
        if (criticalCustomers.Count > 0 && playerTransform != null) {
            Gizmos.color = Color.red;
            foreach (var info in criticalCustomers) {
                if (info.customerObject != null) {
                    
                    Gizmos.DrawLine(playerTransform.position, info.customerObject.transform.position);
                    
                    Gizmos.DrawWireSphere(info.customerObject.transform.position, 1f);
                }
            }
        }
    }
}
