using System.Collections;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Customer : MonoBehaviour {

    [SerializeField] float waitTime = 40f;
    [SerializeField] float waitTimeVariance = 10f;

    [SerializeField] int moneyReward = 10;
    [SerializeField] int moneyRewardVariance = 5;

    public float timeLeft;

    [SerializeField] GameObject pizzaInHand;
    [SerializeField] Sprite[] CustomerBodyVariety;

    [SerializeField] Image fillImage;
    TextMeshProUGUI timeText;

    GameManager gameManager;
    SpriteRenderer spriteRenderer;
    ScoreHandler scoreHandler;
    CustomerManager customerManager;
    GameUIManager gameUIManager;

    Collider2D bodyCollider;
    Coroutine leaveCoroutine;

    private void OnEnable() {
        bodyCollider = GetComponent<Collider2D>();
        bodyCollider.enabled = true;
        waitTime = waitTime + Random.Range(-waitTimeVariance, waitTimeVariance);
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (pizzaInHand != null) {
            pizzaInHand.SetActive(false);
        }

        if (CustomerBodyVariety.Length > 0)
            spriteRenderer.sprite = CustomerBodyVariety[Random.Range(0, CustomerBodyVariety.Length)];
        
        gameManager = FindFirstObjectByType<GameManager>();
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();

        customerManager = FindFirstObjectByType<CustomerManager>();
        timeText = GetComponentInChildren<TextMeshProUGUI>();

        leaveCoroutine = StartCoroutine(LeaveAfterTime());


    }
    void FixedUpdate() {

    }

    public void ReceivePizza() {
        bodyCollider.enabled = false;
        if (pizzaInHand != null) {
            pizzaInHand.SetActive(true);
        }
        StopCoroutine(leaveCoroutine);
        timeText.text = "Thank You!";
        customerManager.CustomerRoutine(this.gameObject);
        scoreHandler.AddScore(Mathf.RoundToInt(timeLeft * 10));

        int reward = moneyReward + Random.Range(-moneyRewardVariance, moneyRewardVariance);

        scoreHandler.AddMoney(moneyReward + Mathf.RoundToInt(timeLeft / 2));
        Debug.Log("Customer rewarded player with $" + reward + "\n +Tipped" + timeLeft/2);

        
    }

    IEnumerator LeaveAfterTime() {


        for (int i = 0; i <= waitTime; i++) {
            timeLeft = waitTime - i;
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

        int penalty = Mathf.RoundToInt((moneyReward + Random.Range(-moneyRewardVariance, moneyRewardVariance)/2));

        scoreHandler.AddMoney(-moneyReward);
    }


    private void OnDisable() {
        spriteRenderer = null;
        StopAllCoroutines();
    }
}
