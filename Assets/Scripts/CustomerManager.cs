using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class CustomerManager : MonoBehaviour
{
    Coroutine customerRespawnCoroutine;
    [SerializeField] float minRespawnTime = 10f;
    [SerializeField] float maxRespawnTime = 15f;

    public void CustomerRoutine(GameObject customer) {
        StartCoroutine(CustomerRespawnRoutine(customer, Random.Range((int)minRespawnTime, (int)maxRespawnTime)));
    }

    IEnumerator CustomerRespawnRoutine(GameObject customer, int respawnTime) {
        yield return new WaitForSeconds(2f);
        customer.SetActive(false);
        yield return new WaitForSeconds(respawnTime);
        customer.SetActive(true);      
    }
}
