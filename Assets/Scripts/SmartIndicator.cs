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

    // -1 until the first frame decides, so the very first visibility state is always
    // written even if it matches the prefab's stored alpha.
    int lastOffScreen = -1;

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

        // Only write alpha when visibility actually flips. CanvasGroup.alpha can
        // propagate a change notification to every child graphic, so setting it
        // every frame kept dirtying the canvas for no reason.
        int offNow = isOffScreen ? 1 : 0;
        if (offNow != lastOffScreen) {
            lastOffScreen = offNow;
            canvasGroup.alpha = isOffScreen ? 1f : 0f;
        }

        if (isOffScreen) {
            UpdatePosition(screenPoint);
            UpdateVisuals();
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

                // SetText with args writes straight into TMP's own char buffer, so
                // this costs zero allocations. The old string concat here was the
                // scene's single largest source of GC pressure: the distance readout
                // changes almost every frame while driving, so the dirty check above
                // can't prevent the rebuild - only making the rebuild free helps.
                infoText.SetText("{0}m\n{1}s\n{2}x", d, t, r);
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
