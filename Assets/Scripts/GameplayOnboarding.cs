using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a one-time control guide at the start of a player's first career shift.
/// </summary>
/// <remarks>
/// The guide is intentionally a small overlay rather than a separate tutorial scene. The first
/// shift continues to use the real delivery loop, and the overlay can be dismissed immediately.
/// Its completion is stored as a machine preference because it is a presentation hint, not career
/// progress and should not be duplicated across save slots.
/// </remarks>
public class GameplayOnboarding : MonoBehaviour {

    const string SeenKey = "onboarding.controls.seen";

    [Tooltip("Overlay shown while the first-shift control guide is visible.")]
    [SerializeField] GameObject panel;
    [Tooltip("Localized text shown inside the guide.")]
    [SerializeField] TextMeshProUGUI messageText;
    [Tooltip("Button that dismisses the guide and records it as seen.")]
    [SerializeField] Button closeButton;
    [Tooltip("Localization key for the control guide message.")]
    [SerializeField] string messageKey = "onboarding.controls";

    void Awake() {
        if (closeButton != null) closeButton.onClick.AddListener(Dismiss);
    }

    void OnEnable() {
        LocalizationManager.LanguageChanged += RefreshText;
        RefreshText();
    }

    void OnDisable() {
        LocalizationManager.LanguageChanged -= RefreshText;
    }

    void Start() {
        bool isFirstCareerShift = GameManager.Instance != null &&
            !GameManager.Instance.IsFreeplayMode && GameManager.Instance.totalShiftsSettled == 0;
        bool shouldShow = isFirstCareerShift && PlayerPrefs.GetInt(SeenKey, 0) == 0;
        if (panel != null) panel.SetActive(shouldShow);
    }

    void RefreshText() {
        if (messageText != null) messageText.text = LocalizationManager.Get(messageKey);
    }

    void Dismiss() {
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();
        if (panel != null) panel.SetActive(false);
    }
}
