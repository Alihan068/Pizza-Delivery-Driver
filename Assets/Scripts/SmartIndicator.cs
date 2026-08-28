using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

[RequireComponent(typeof(CanvasGroup))]
public class SmartIndicator : MonoBehaviour {
    [Header("Target & UI")]
    public Customer targetCustomer;
    public Image targetImage;
    public TextMeshProUGUI infoText;

    [Header("Settings")]
    public float edgePadding = 50f;
    public float smoothSpeed = 20f;
    public Gradient timeColorGradient;

    [Header("Events")]
    public float criticalTimeThreshold = 5f;
    public UnityEvent onCriticalTimeEnter;

    private Camera mainCamera;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private bool isCriticalTriggered = false;

    int lastDist = -1, lastTime = -1, lastRemaining = -1;

    void Awake() {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        mainCamera = Camera.main;
    }

    public void Initialize(Customer customer) {
        targetCustomer = customer;
    }

    void LateUpdate() {
        // Destroy once the order is done (fulfilled or abandoned) or the customer
        // is gone. timeLeft alone isn't enough - a completed order can still have
        // time left on the clock, and a stale cached "max time" would drift once
        // partial deliveries extend the wait, so read currentOrder live instead.
        if (targetCustomer == null || !targetCustomer.gameObject.activeInHierarchy ||
            targetCustomer.currentOrder == null || targetCustomer.currentOrder.IsComplete) {
            Destroy(gameObject);
            return;
        }

        HandleVisibilityAndPosition();
    }

    void HandleVisibilityAndPosition() {
        Vector3 screenPoint = mainCamera.WorldToScreenPoint(targetCustomer.transform.position);

        //Check if target is off-screen
        bool isOffScreen = screenPoint.z < 0 ||
                           screenPoint.x < edgePadding ||
                           screenPoint.x > Screen.width - edgePadding ||
                           screenPoint.y < edgePadding ||
                           screenPoint.y > Screen.height - edgePadding;

        if (isOffScreen) { //1 Show/0 Hide indicator
            canvasGroup.alpha = 1f;
            UpdatePosition(screenPoint);
            UpdateVisuals();
        }
        else {
            canvasGroup.alpha = 0f;
        }
    }

    void UpdatePosition(Vector3 screenPoint) {

        if (screenPoint.z < 0) screenPoint *= -1;

        Vector3 screenCenter = new Vector3(Screen.width, Screen.height, 0) * 0.5f;
        Vector3 direction = (screenPoint - screenCenter).normalized;


        Vector2 screenBounds = new Vector2(Screen.width, Screen.height) * 0.5f;
        screenBounds -= new Vector2(edgePadding, edgePadding);


        float divX = (direction.x != 0) ? screenBounds.x / Mathf.Abs(direction.x) : screenBounds.x;
        float divY = (direction.y != 0) ? screenBounds.y / Mathf.Abs(direction.y) : screenBounds.y;

        Vector3 clampedPos = screenCenter + (direction * Mathf.Min(divX, divY));

        rectTransform.position = Vector3.Lerp(rectTransform.position, clampedPos, Time.deltaTime * smoothSpeed);
    }

    void UpdateVisuals() {
        float currentTime = targetCustomer.timeLeft;
        float maxTime = targetCustomer.currentOrder.waitTime;

        //Update Text - distance, time left, and how many pizzas are still owed
        if (infoText != null) {
            float distance = Vector2.Distance(mainCamera.transform.position, targetCustomer.transform.position);
            int d = Mathf.RoundToInt(distance * 10);
            int t = Mathf.RoundToInt(currentTime);
            int r = targetCustomer.currentOrder.RemainingPizzas;

            if (d != lastDist || t != lastTime || r != lastRemaining) {
                lastDist = d;
                lastTime = t;
                lastRemaining = r;
                infoText.text = d + "m\n" + t + "s\n" + r + "x";
            }
        }

        //Update Color
        if (targetImage != null) {
            float ratio = maxTime > 0 ? Mathf.Clamp01(currentTime / maxTime) : 0f;
            targetImage.color = timeColorGradient.Evaluate(ratio);
        }

        //Trigger Event
        if (currentTime <= criticalTimeThreshold && !isCriticalTriggered) {
            isCriticalTriggered = true;
            onCriticalTimeEnter?.Invoke();
        }
    }
}
