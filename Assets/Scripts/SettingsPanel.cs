using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Shared settings screen used by the main menu, garage and pause menu.
/// </summary>
/// <remarks>
/// The reset is deliberately two-step. The first press only reveals a confirmation row; nothing is
/// destroyed until the player presses the confirm button. Opening or closing the panel always
/// returns it to the unconfirmed state, so a stray click can never land on an already armed
/// confirmation. Context-specific navigation controls are activated from serialized references,
/// which lets one panel prefab serve all three screens without duplicating navigation rules.
/// </remarks>
public class SettingsPanel : MonoBehaviour {

    [Header("Context")]
    [Tooltip("Determines which optional navigation and save actions are visible on this instance.")]
    [SerializeField] SettingsContext context = SettingsContext.Pause;

    [Header("Music")]
    [SerializeField] Slider musicSlider;
    [SerializeField] TextMeshProUGUI musicValueText;

    [Header("Audio")]
    [SerializeField] Slider masterSlider;
    [SerializeField] TextMeshProUGUI masterValueText;
    [SerializeField] Slider sfxSlider;
    [SerializeField] TextMeshProUGUI sfxValueText;

    [Header("Display")]
    [SerializeField] Button displayOptionsButton;
    [SerializeField] GameObject displayOptionsOverlay;
    [SerializeField] Button displayOptionsCloseButton;
    [SerializeField] Button resolutionButton;
    [SerializeField] TextMeshProUGUI resolutionValueText;
    [SerializeField] Button fullscreenButton;
    [SerializeField] TextMeshProUGUI fullscreenValueText;
    [SerializeField] Button frameRateButton;
    [SerializeField] TextMeshProUGUI frameRateValueText;
    [SerializeField] Button vSyncButton;
    [SerializeField] TextMeshProUGUI vSyncValueText;

    [Header("Display Localization Keys")]
    [SerializeField] string resolutionValueKey = "settings.resolutionValue";
    [SerializeField] string fullscreenOnKey = "settings.fullscreenOn";
    [SerializeField] string fullscreenOffKey = "settings.fullscreenOff";
    [SerializeField] string frameRateValueKey = "settings.frameRateValue";
    [SerializeField] string uncappedKey = "settings.uncapped";
    [SerializeField] string vSyncOnKey = "settings.vSyncOn";
    [SerializeField] string vSyncOffKey = "settings.vSyncOff";

    [Header("Progress Reset")]
    [SerializeField] Button resetProgressButton;
    [SerializeField] GameObject confirmGroup;
    [SerializeField] Button confirmYesButton;
    [SerializeField] Button confirmNoButton;

    [Header("Navigation")]
    [SerializeField] Button backButton;

    [Header("Shared Actions")]
    [Tooltip("Language control shown in every settings context.")]
    [SerializeField] LanguageSelector languageSelector;
    [Tooltip("Manual save control shown in the garage and pause contexts.")]
    [SerializeField] SaveStatusChip saveChip;
    [Tooltip("Career copy action shown only in the garage context.")]
    [SerializeField] Button copyToSlotButton;
    [Tooltip("Destination picker opened by the copy action.")]
    [SerializeField] SaveSlotSelectPanel copySlotPanel;
    [Tooltip("In-session route to the garage, shown only from pause.")]
    [SerializeField] Button garageButton;
    [Tooltip("Route to the main menu, shown from pause and garage.")]
    [SerializeField] Button mainMenuButton;


    /// <summary>Raised when the player closes this panel with the back button.</summary>
    public event System.Action Closed;

    /// <summary>
    /// Raised by the pause instance when the player chooses to abandon the current session for the
    /// garage. The pause controller owns settlement so this action cannot bypass abandonment rules.
    /// </summary>
    public event System.Action ReturnToGarageRequested;

    /// <summary>
    /// Raised by the pause instance when the player chooses to abandon the current session for the
    /// main menu. The pause controller owns settlement and the resulting session loss.
    /// </summary>
    public event System.Action ReturnToMainMenuRequested;

    void Awake() {
        if (musicSlider != null) {
            musicSlider.minValue = 0f;
            musicSlider.maxValue = 100f;
            musicSlider.wholeNumbers = true;
            musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);
        }
        ConfigureVolumeSlider(masterSlider, OnMasterSliderChanged);
        ConfigureVolumeSlider(sfxSlider, OnSfxSliderChanged);
        if (displayOptionsButton != null) displayOptionsButton.onClick.AddListener(OnDisplayOptionsButtonClicked);
        if (displayOptionsCloseButton != null) displayOptionsCloseButton.onClick.AddListener(OnDisplayOptionsCloseButtonClicked);
        if (resolutionButton != null) resolutionButton.onClick.AddListener(OnResolutionButtonClicked);
        if (fullscreenButton != null) fullscreenButton.onClick.AddListener(OnFullscreenButtonClicked);
        if (frameRateButton != null) frameRateButton.onClick.AddListener(OnFrameRateButtonClicked);
        if (vSyncButton != null) vSyncButton.onClick.AddListener(OnVSyncButtonClicked);
        if (resetProgressButton != null) resetProgressButton.onClick.AddListener(OnClickResetProgress);
        if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnClickConfirmReset);
        if (confirmNoButton != null) confirmNoButton.onClick.AddListener(OnClickCancelReset);
        if (backButton != null) backButton.onClick.AddListener(OnClickBack);
        if (copyToSlotButton != null) copyToSlotButton.onClick.AddListener(OnClickCopyToSlot);
        if (garageButton != null) garageButton.onClick.AddListener(OnClickGarage);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnClickMainMenu);
    }

    void OnEnable() {
        if (musicSlider != null) musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        UpdateMusicLabel(GameSettings.MusicVolume);
        if (masterSlider != null) masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(GameSettings.SfxVolume);
        UpdateVolumeLabel(masterValueText, GameSettings.MasterVolume);
        UpdateVolumeLabel(sfxValueText, GameSettings.SfxVolume);
        RefreshDisplayLabels();
        if (displayOptionsOverlay != null) displayOptionsOverlay.SetActive(false);
        SetConfirmVisible(false);
        ApplyContextVisibility();
    }

    void OnDisable() {
        SetConfirmVisible(false);
    }

    void OnMusicSliderChanged(float value) {
        int volume = Mathf.RoundToInt(value);
        GameSettings.MusicVolume = volume;
        UpdateMusicLabel(volume);
    }

    void ConfigureVolumeSlider(Slider slider, UnityEngine.Events.UnityAction<float> callback) {
        if (slider == null) return;
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.wholeNumbers = true;
        slider.onValueChanged.AddListener(callback);
    }

    void OnMasterSliderChanged(float value) {
        int volume = Mathf.RoundToInt(value);
        GameSettings.MasterVolume = volume;
        UpdateVolumeLabel(masterValueText, volume);
    }

    void OnSfxSliderChanged(float value) {
        int volume = Mathf.RoundToInt(value);
        GameSettings.SfxVolume = volume;
        UpdateVolumeLabel(sfxValueText, volume);
    }

    void UpdateMusicLabel(int volume) {
        if (musicValueText == null) return;
        // SetText writes straight into TMP's char buffer, so dragging the slider allocates nothing.
        musicValueText.SetText("{0}", volume);
    }

    void UpdateVolumeLabel(TextMeshProUGUI target, int volume) {
        if (target != null) target.SetText("{0}", volume);
    }

    void OnResolutionButtonClicked() {
        Resolution[] resolutions = Screen.resolutions;
        if (resolutions == null || resolutions.Length == 0) return;

        int currentIndex = 0;
        for (int i = 0; i < resolutions.Length; i++) {
            if (resolutions[i].width == Screen.width && resolutions[i].height == Screen.height) {
                currentIndex = i;
                break;
            }
        }

        Resolution next = resolutions[(currentIndex + 1) % resolutions.Length];
        DisplaySettings.SetResolution(next.width, next.height, DisplaySettings.GetFullscreenMode());
        RefreshDisplayLabels();
    }

    void OnFullscreenButtonClicked() {
        FullScreenMode next = Screen.fullScreenMode == FullScreenMode.Windowed
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;
        DisplaySettings.SetFullscreenMode(next);
        RefreshDisplayLabels();
    }

    void OnFrameRateButtonClicked() {
        GameConfig config = GameManager.Instance != null ? GameManager.Instance.Config : null;
        int[] options = config != null ? config.frameRateOptions : null;
        if (options == null || options.Length == 0) return;

        int current = DisplaySettings.GetTargetFrameRate(config);
        int currentIndex = -1;
        for (int i = 0; i < options.Length; i++) {
            if (options[i] == current) { currentIndex = i; break; }
        }

        int nextIndex = (currentIndex + 1) % options.Length;
        DisplaySettings.SetFrameRate(0, Mathf.Max(0, options[nextIndex]));
        RefreshDisplayLabels();
    }

    void OnVSyncButtonClicked() {
        GameConfig config = GameManager.Instance != null ? GameManager.Instance.Config : null;
        int next = DisplaySettings.GetVSyncCount(config) > 0 ? 0 : 1;
        DisplaySettings.SetFrameRate(next, DisplaySettings.GetTargetFrameRate(config));
        RefreshDisplayLabels();
    }

    void OnDisplayOptionsButtonClicked() {
        if (displayOptionsOverlay != null) displayOptionsOverlay.SetActive(true);
    }

    void OnDisplayOptionsCloseButtonClicked() {
        if (displayOptionsOverlay != null) displayOptionsOverlay.SetActive(false);
    }

    void RefreshDisplayLabels() {
        if (resolutionValueText != null)
            resolutionValueText.text = LocalizationManager.Get(resolutionValueKey, Screen.width, Screen.height);

        if (fullscreenValueText != null) {
            string key = Screen.fullScreenMode == FullScreenMode.Windowed ? fullscreenOffKey : fullscreenOnKey;
            fullscreenValueText.text = LocalizationManager.Get(key);
        }

        if (frameRateValueText != null) {
            GameConfig config = GameManager.Instance != null ? GameManager.Instance.Config : null;
            int target = DisplaySettings.GetTargetFrameRate(config);
            frameRateValueText.text = target > 0
                ? LocalizationManager.Get(frameRateValueKey, target)
                : LocalizationManager.Get(uncappedKey);
        }

        if (vSyncValueText != null) {
            GameConfig config = GameManager.Instance != null ? GameManager.Instance.Config : null;
            vSyncValueText.text = LocalizationManager.Get(DisplaySettings.GetVSyncCount(config) > 0 ? vSyncOnKey : vSyncOffKey);
        }
    }

    void SetConfirmVisible(bool visible) {
        if (confirmGroup != null) confirmGroup.SetActive(visible);
        if (resetProgressButton != null) resetProgressButton.gameObject.SetActive(!visible);
    }

    void ApplyContextVisibility() {
        bool isGarage = context == SettingsContext.Garage;
        bool isPause = context == SettingsContext.Pause;

        SetOptionalActive(languageSelector, true);
        SetOptionalActive(saveChip, isGarage || isPause);
        SetOptionalActive(copyToSlotButton, isGarage);
        SetOptionalActive(garageButton, isPause);
        SetOptionalActive(mainMenuButton, isGarage || isPause);
        SetOptionalActive(displayOptionsButton, true);
    }

    static void SetOptionalActive(Component component, bool active) {
        if (component != null) component.gameObject.SetActive(active);
    }

    void OnClickResetProgress() {
        SetConfirmVisible(true);
    }

    void OnClickCancelReset() {
        SetConfirmVisible(false);
    }

    // The running session is built on progress that no longer exists, so it cannot continue: the
    // player is returned to the main menu rather than dropped back into a stale garage.
    void OnClickConfirmReset() {
        if (GameManager.Instance == null) return;

        GameManager.Instance.ResetProgress();
        SetConfirmVisible(false);

        // The pause menu froze time; the destination scene would otherwise open frozen.
        Time.timeScale = 1f;
        SceneManager.LoadScene(GameManager.Instance.Config.mainMenuScene);
    }

    void OnClickBack() {
        Closed?.Invoke();
    }

    void OnClickCopyToSlot() {
        if (copySlotPanel == null || GameManager.Instance == null) return;

        // Refresh the copy cards from the latest state even if the player has not triggered an
        // automatic save since the last upgrade or vehicle change.
        GameManager.Instance.SaveGame();
        copySlotPanel.Open(SaveSlotSelectMode.CopyTarget);
    }

    void OnClickGarage() {
        if (context == SettingsContext.Pause) ReturnToGarageRequested?.Invoke();
    }

    void OnClickMainMenu() {
        if (context == SettingsContext.Pause) {
            ReturnToMainMenuRequested?.Invoke();
            return;
        }

        if (context != SettingsContext.Garage || GameManager.Instance == null) return;
        var config = GameManager.Instance.Config;
        if (config == null || string.IsNullOrEmpty(config.mainMenuScene)) {
            Debug.LogError("No main menu scene is configured in GameConfig.");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(config.mainMenuScene);
    }
}
