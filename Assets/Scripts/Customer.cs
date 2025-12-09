using System.Collections;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Customer : MonoBehaviour {

    [SerializeField] float waitTime = 40f;
    [SerializeField] float waitTimeVariance = 10f;
    [SerializeField] GameObject pizzaInHand;
    [SerializeField] Sprite[] CustomerBodyVariety;

    [SerializeField] Image fillImage;
    TextMeshProUGUI timeText;

    SpriteRenderer spriteRenderer;
    Coroutine leaveCoroutine;
    CustomerManager customerManager;
    GameUIManager gameUIManager;

    private void OnEnable() {
        waitTime = waitTime + Random.Range(-waitTimeVariance, waitTimeVariance);
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (pizzaInHand != null) {
            pizzaInHand.SetActive(false);
        }

        if (CustomerBodyVariety.Length > 0)
            spriteRenderer.sprite = CustomerBodyVariety[Random.Range(0, CustomerBodyVariety.Length)];

        customerManager = FindFirstObjectByType<CustomerManager>();
        timeText = GetComponentInChildren<TextMeshProUGUI>();

        leaveCoroutine = StartCoroutine(LeaveAfterTime());


    }
    void FixedUpdate() {

    }

    public void ReceivePizza() {
        if (pizzaInHand != null) {
            pizzaInHand.SetActive(true);
        }
        StopCoroutine(leaveCoroutine);
        timeText.text = "Thank You!";
        customerManager.CustomerRoutine(this.gameObject);
    }

    IEnumerator LeaveAfterTime() {


        for (int i = 0; i <= waitTime; i++) {
            float timeLeft = waitTime - i;
            if (fillImage != null) {
                fillImage.fillAmount = timeLeft / waitTime;
            }
            if (timeText != null) {
                timeText.text = Mathf.Ceil(timeLeft).ToString();
            }
            yield return new WaitForSeconds(1f);
        }
        customerManager.CustomerRoutine(this.gameObject);
        leaveCoroutine = null;
    }

    private void OnDisable() {
        spriteRenderer = null;
    }
}
