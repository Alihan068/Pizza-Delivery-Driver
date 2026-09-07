using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

[RequireComponent(typeof(CanvasGroup))]
public class SmartIndicator : MonoBehaviour {
    [SerializeField] string infoKey = "hud.indicator";
    int lastLanguageRevision = -1;
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
    RectTransform canvasRect;
    Canvas indicatorCanvas;
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
        indicatorCanvas = GetComponentInParent<Canvas>();
        canvasRect = indicatorCanvas != null ? indicatorCanvas.transform as RectTransform : null;
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
        if (mainCamera == null || canvasRect == null) return;

        Vector3 screenPoint = mainCamera.WorldToScreenPoint(targetCustomer.transform.position);
        bool isBehindCamera = screenPoint.z < 0f;
        if (isBehindCamera) screenPoint *= -1f;

        Camera eventCamera = indicatorCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : indicatorCanvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, eventCamera, out Vector2 canvasPoint)) return;

        Rect canvasBounds = canvasRect.rect;
        bool isOffScreen = isBehindCamera ||
                           canvasPoint.x < canvasBounds.xMin + edgePadding ||
                           canvasPoint.x > canvasBounds.xMax - edgePadding ||
                           canvasPoint.y < canvasBounds.yMin + edgePadding ||
                           canvasPoint.y > canvasBounds.yMax - edgePadding;

        // Only write alpha when visibility actually flips. CanvasGroup.alpha can
        // propagate a change notification to every child graphic, so setting it
        // every frame kept dirtying the canvas for no reason.
        int offNow = isOffScreen ? 1 : 0;
        if (offNow != lastOffScreen) {
            lastOffScreen = offNow;
            canvasGroup.alpha = isOffScreen ? 1f : 0f;
        }

        if (isOffScreen) {
            UpdatePosition(canvasPoint);
            UpdateVisuals();
        }
    }

    void UpdatePosition(Vector2 canvasPoint) {
        Vector2 canvasCenter = canvasRect.rect.center;
        Vector2 direction = (canvasPoint - canvasCenter).normalized;

        Vector2 canvasBounds = canvasRect.rect.size * 0.5f;
        canvasBounds -= new Vector2(edgePadding, edgePadding);

        float divX = (direction.x != 0) ? canvasBounds.x / Mathf.Abs(direction.x) : canvasBounds.x;
        float divY = (direction.y != 0) ? canvasBounds.y / Mathf.Abs(direction.y) : canvasBounds.y;

        Vector2 clampedPos = canvasCenter + (direction * Mathf.Min(divX, divY));

        rectTransform.localPosition = Vector2.Lerp(rectTransform.localPosition, clampedPos, Time.deltaTime * smoothSpeed);
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

            int revision = LocalizationManager.Instance != null ? LocalizationManager.Instance.Revision : 0;
            if (d != lastDist || t != lastTime || r != lastRemaining || revision != lastLanguageRevision) {
                lastLanguageRevision = revision;
                lastDist = d;
                lastTime = t;
                lastRemaining = r;

                // SetText with args writes straight into TMP's own char buffer, so
                // this costs zero allocations. The old string concat here was the
                // scene's single largest source of GC pressure: the distance readout
                // changes almost every frame while driving, so the dirty check above
                // can't prevent the rebuild - only making the rebuild free helps.
                LocalizationManager.SetText(infoText, infoKey, d, t, r);
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
