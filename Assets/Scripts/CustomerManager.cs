using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class CustomerManager : MonoBehaviour {
    Coroutine customerRespawnCoroutine;
    [SerializeField] float minRespawnTime = 10f;
    [SerializeField] float maxRespawnTime = 15f;

    GameObject[] allCustomers;

    void Start() {
        allCustomers = GameObject.FindGameObjectsWithTag("Customer");
        foreach (GameObject customer in allCustomers) {
            customer.SetActive(false);
        }
    }

    public void CustomerRoutine(GameObject customer) {
        StartCoroutine(CustomerRespawnRoutine(customer, Random.Range((int)minRespawnTime, (int)maxRespawnTime)));
    }

    IEnumerator CustomerRespawnRoutine(GameObject customer, int respawnTime) {
        yield return new WaitForSeconds(1f);
        customer.SetActive(false);
        //yield return new WaitForSeconds(respawnTime);
        //customer.SetActive(true);
    }

    public void GetCustomer() {

        List<GameObject> inactiveCustomers = new List<GameObject>();

        foreach (GameObject customer in allCustomers) {
            if (!customer.activeInHierarchy) {
                inactiveCustomers.Add(customer);               
            } else return;
        }
        if (inactiveCustomers.Count ==0) return;

        inactiveCustomers[Random.Range(0, inactiveCustomers.Count)].SetActive(true);

    }
}
