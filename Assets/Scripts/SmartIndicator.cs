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
    private float maxTimeCache;

    void Awake() {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        mainCamera = Camera.main;
    }

    public void Initialize(Customer customer) {
        targetCustomer = customer;
        maxTimeCache = customer.timeLeft > 0 ? customer.timeLeft : 30f;
    }

    void LateUpdate() {
        //Destroy if customer is invalid or inactive
        if (targetCustomer == null || !targetCustomer.gameObject.activeInHierarchy || targetCustomer.timeLeft <= 0) {
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

        if (isOffScreen) {
            canvasGroup.alpha = 1f; //Show indicator
            UpdatePosition(screenPoint);
            UpdateVisuals();
        }
        else {
            canvasGroup.alpha = 0f; //Hide indicator
        }
    }

    void UpdatePosition(Vector3 screenPoint) {
        //Flip if behind camera
        if (screenPoint.z < 0) screenPoint *= -1;

        Vector3 screenCenter = new Vector3(Screen.width, Screen.height, 0) * 0.5f;
        Vector3 direction = (screenPoint - screenCenter).normalized;

        // Calculate boundaries
        Vector2 screenBounds = new Vector2(Screen.width, Screen.height) * 0.5f;
        screenBounds -= new Vector2(edgePadding, edgePadding);

        //Calculate clamping intersection
        float divX = (direction.x != 0) ? screenBounds.x / Mathf.Abs(direction.x) : screenBounds.x;
        float divY = (direction.y != 0) ? screenBounds.y / Mathf.Abs(direction.y) : screenBounds.y;

        Vector3 clampedPos = screenCenter + (direction * Mathf.Min(divX, divY));

        rectTransform.position = Vector3.Lerp(rectTransform.position, clampedPos, Time.deltaTime * smoothSpeed);
    }

    void UpdateVisuals() {
        float currentTime = targetCustomer.timeLeft;

        //Update Text
        if (infoText != null) {
            float distance = Vector2.Distance(mainCamera.transform.position, targetCustomer.transform.position);
            infoText.text = $"{distance * 10:F0}m\n{currentTime:F0}s";
        }

        //Update Color
        if (targetImage != null) {
            float ratio = Mathf.Clamp01(currentTime / maxTimeCache);
            targetImage.color = timeColorGradient.Evaluate(ratio);
        }

        //Trigger Event
        if (currentTime <= criticalTimeThreshold && !isCriticalTriggered) {
            isCriticalTriggered = true;
            onCriticalTimeEnter?.Invoke();
        }
    }
}