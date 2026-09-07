using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
    [SerializeField] Button closeButton;
    [SerializeField] Button resolutionPreviousButton;
    [SerializeField] Button resolutionButton;
    [SerializeField] TextMeshProUGUI resolutionValueText;
    [SerializeField] Button fullscreenPreviousButton;
    [SerializeField] Button fullscreenButton;
    [SerializeField] TextMeshProUGUI fullscreenValueText;
    [SerializeField] FullScreenMode[] fullscreenModes;
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

    [Header("Developer Tools")]
    [Tooltip("Temporary testing controls. Disable this flag before shipping a player build.")]
    [SerializeField] bool developerToolsEnabled = true;
    [Min(1)] [SerializeField] int developerMoneyAmount = 1000;
    [Min(1)] [SerializeField] int developerRatingAmount = 100;
    [SerializeField] Vector2 developerToolsPosition = new Vector2(0f, 8f);
    [SerializeField] Vector2 developerToolsRootSize = new Vector2(340f, 380f);
    [SerializeField] Vector2 developerToolsButtonSize = new Vector2(300f, 52f);
    [SerializeField] Vector2 developerToolsDropdownSize = new Vector2(320f, 310f);
    [SerializeField] Color developerToolsDropdownColor = new Color(0.08f, 0.08f, 0.11f, 0.98f);

    [Header("Developer Tools Localization Keys")]
    [SerializeField] string developerToolsKey = "settings.developerTools";
    [SerializeField] string developerAddMoneyKey = "settings.developer.addMoney";
    [SerializeField] string developerAddRatingKey = "settings.developer.addRating";
    [SerializeField] string developerRemoveRatingKey = "settings.developer.removeRating";
    [SerializeField] string developerUnlockVehiclesKey = "settings.developer.unlockVehicles";
    [SerializeField] string developerUnlockMapsKey = "settings.developer.unlockMaps";
    [SerializeField] string developerReadyKey = "settings.developer.ready";
    [SerializeField] string developerUnavailableKey = "settings.developer.unavailable";
    [SerializeField] string developerMoneyAddedKey = "settings.developer.moneyAdded";
    [SerializeField] string developerRatingAddedKey = "settings.developer.ratingAdded";
    [SerializeField] string developerRatingRemovedKey = "settings.developer.ratingRemoved";
    [SerializeField] string developerVehiclesUnlockedKey = "settings.developer.vehiclesUnlocked";
    [SerializeField] string developerMapsUnlockedKey = "settings.developer.mapsUnlocked";

    GameObject developerToolsRuntimeRoot;
    GameObject developerToolsDropdown;
    TextMeshProUGUI developerToolsToggleText;
    TextMeshProUGUI developerAddMoneyText;
    TextMeshProUGUI developerAddRatingText;
    TextMeshProUGUI developerRemoveRatingText;
    TextMeshProUGUI developerUnlockVehiclesText;
    TextMeshProUGUI developerUnlockMapsText;
    TextMeshProUGUI developerStatusText;


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
        BuildDeveloperToolsUI();
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
        if (closeButton != null) closeButton.onClick.AddListener(OnClickBack);
        if (resolutionPreviousButton != null) resolutionPreviousButton.onClick.AddListener(OnResolutionPreviousButtonClicked);
        if (resolutionButton != null) resolutionButton.onClick.AddListener(OnResolutionButtonClicked);
        if (fullscreenPreviousButton != null) fullscreenPreviousButton.onClick.AddListener(OnFullscreenPreviousButtonClicked);
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
        if (developerToolsRuntimeRoot == null) BuildDeveloperToolsUI();
        LocalizationManager.LanguageChanged += RefreshDeveloperToolsLabels;
        if (musicSlider != null) musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        UpdateMusicLabel(GameSettings.MusicVolume);
        if (masterSlider != null) masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(GameSettings.SfxVolume);
        UpdateVolumeLabel(masterValueText, GameSettings.MasterVolume);
        UpdateVolumeLabel(sfxValueText, GameSettings.SfxVolume);
        RefreshDisplayLabels();
        if (displayOptionsOverlay != null) displayOptionsOverlay.SetActive(false);
        SetDeveloperToolsDropdownVisible(false);
        SetConfirmVisible(false);
        ApplyContextVisibility();
        RefreshDeveloperToolsLabels();
    }

    void Update() {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;

        if (developerToolsDropdown != null && developerToolsDropdown.activeSelf) {
            SetDeveloperToolsDropdownVisible(false);
            return;
        }

        if (displayOptionsOverlay != null && displayOptionsOverlay.activeSelf) {
            OnDisplayOptionsCloseButtonClicked();
            return;
        }

        if (confirmGroup != null && confirmGroup.activeSelf) {
            OnClickCancelReset();
            return;
        }

        OnClickBack();
    }

    void OnDisable() {
        LocalizationManager.LanguageChanged -= RefreshDeveloperToolsLabels;
        SetConfirmVisible(false);
    }

    void BuildDeveloperToolsUI() {
        if (!developerToolsEnabled || developerToolsRuntimeRoot != null || displayOptionsButton == null) return;

        developerToolsRuntimeRoot = new GameObject("DeveloperToolsRuntime", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        developerToolsRuntimeRoot.transform.SetParent(transform, false);
        developerToolsRuntimeRoot.transform.SetAsLastSibling();

        RectTransform rootRect = developerToolsRuntimeRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = developerToolsPosition;
        rootRect.sizeDelta = developerToolsRootSize;

        LayoutElement rootLayout = developerToolsRuntimeRoot.GetComponent<LayoutElement>();
        rootLayout.ignoreLayout = true;

        Image dropdownBackground = developerToolsRuntimeRoot.GetComponent<Image>();
        dropdownBackground.color = Color.clear;
        dropdownBackground.raycastTarget = false;

        Button toggleButton = CreateDeveloperButton(developerToolsRuntimeRoot.transform, "DeveloperToolsToggle");
        RectTransform toggleRect = toggleButton.transform as RectTransform;
        toggleRect.anchorMin = new Vector2(0.5f, 0f);
        toggleRect.anchorMax = new Vector2(0.5f, 0f);
        toggleRect.pivot = new Vector2(0.5f, 0f);
        toggleRect.anchoredPosition = Vector2.zero;
        toggleRect.sizeDelta = developerToolsButtonSize;
        developerToolsToggleText = toggleButton.GetComponentInChildren<TextMeshProUGUI>(true);
        toggleButton.onClick.AddListener(OnDeveloperToolsToggleClicked);

        developerToolsDropdown = new GameObject("DeveloperToolsDropdown", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        developerToolsDropdown.transform.SetParent(developerToolsRuntimeRoot.transform, false);
        RectTransform dropdownRect = developerToolsDropdown.GetComponent<RectTransform>();
        dropdownRect.anchorMin = new Vector2(0.5f, 0f);
        dropdownRect.anchorMax = new Vector2(0.5f, 0f);
        dropdownRect.pivot = new Vector2(0.5f, 0f);
        dropdownRect.anchoredPosition = new Vector2(0f, developerToolsButtonSize.y + 8f);
        dropdownRect.sizeDelta = developerToolsDropdownSize;
        Image dropdownImage = developerToolsDropdown.GetComponent<Image>();
        dropdownImage.color = developerToolsDropdownColor;

        VerticalLayoutGroup dropdownLayout = developerToolsDropdown.GetComponent<VerticalLayoutGroup>();
        dropdownLayout.padding = new RectOffset(8, 8, 8, 8);
        dropdownLayout.spacing = 4f;
        dropdownLayout.childAlignment = TextAnchor.UpperCenter;
        dropdownLayout.childControlWidth = true;
        dropdownLayout.childControlHeight = true;
        dropdownLayout.childForceExpandWidth = true;
        dropdownLayout.childForceExpandHeight = false;

        developerStatusText = CreateDeveloperText(developerToolsDropdown.transform, "DeveloperToolsStatus");
        SetDeveloperTextLayout(developerStatusText, 30f, 11f);

        Button addMoneyButton = CreateDeveloperButton(developerToolsDropdown.transform, "DeveloperAddMoney");
        developerAddMoneyText = addMoneyButton.GetComponentInChildren<TextMeshProUGUI>(true);
        SetDeveloperButtonLayout(addMoneyButton, 44f);
        addMoneyButton.onClick.AddListener(OnDeveloperAddMoneyClicked);

        Button addRatingButton = CreateDeveloperButton(developerToolsDropdown.transform, "DeveloperAddRating");
        developerAddRatingText = addRatingButton.GetComponentInChildren<TextMeshProUGUI>(true);
        SetDeveloperButtonLayout(addRatingButton, 44f);
        addRatingButton.onClick.AddListener(OnDeveloperAddRatingClicked);

        Button removeRatingButton = CreateDeveloperButton(developerToolsDropdown.transform, "DeveloperRemoveRating");
        developerRemoveRatingText = removeRatingButton.GetComponentInChildren<TextMeshProUGUI>(true);
        SetDeveloperButtonLayout(removeRatingButton, 44f);
        removeRatingButton.onClick.AddListener(OnDeveloperRemoveRatingClicked);

        Button unlockVehiclesButton = CreateDeveloperButton(developerToolsDropdown.transform, "DeveloperUnlockVehicles");
        developerUnlockVehiclesText = unlockVehiclesButton.GetComponentInChildren<TextMeshProUGUI>(true);
        SetDeveloperButtonLayout(unlockVehiclesButton, 44f);
        unlockVehiclesButton.onClick.AddListener(OnDeveloperUnlockVehiclesClicked);

        Button unlockMapsButton = CreateDeveloperButton(developerToolsDropdown.transform, "DeveloperUnlockMaps");
        developerUnlockMapsText = unlockMapsButton.GetComponentInChildren<TextMeshProUGUI>(true);
        SetDeveloperButtonLayout(unlockMapsButton, 44f);
        unlockMapsButton.onClick.AddListener(OnDeveloperUnlockMapsClicked);

        developerToolsDropdown.SetActive(false);
    }

    Button CreateDeveloperButton(Transform parent, string objectName) {
        GameObject buttonObject = Instantiate(displayOptionsButton.gameObject, parent, false);
        buttonObject.name = objectName;
        buttonObject.SetActive(true);

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        foreach (LocalizedText localizedText in buttonObject.GetComponentsInChildren<LocalizedText>(true))
            localizedText.enabled = false;

        TextMeshProUGUI label = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) {
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = 16f;
        }

        return button;
    }

    TextMeshProUGUI CreateDeveloperText(Transform parent, string objectName) {
        TextMeshProUGUI template = displayOptionsButton.GetComponentInChildren<TextMeshProUGUI>(true);
        GameObject textObject = template != null
            ? Instantiate(template.gameObject, parent, false)
            : new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.name = objectName;
        textObject.SetActive(true);

        foreach (LocalizedText localizedText in textObject.GetComponentsInChildren<LocalizedText>(true))
            localizedText.enabled = false;

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        if (label != null) {
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = 14f;
        }
        return label;
    }

    void SetDeveloperButtonLayout(Button button, float height) {
        if (button == null) return;
        RectTransform rect = button.transform as RectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, height);

        LayoutElement layout = button.GetComponent<LayoutElement>();
        if (layout == null) layout = button.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        layout.flexibleHeight = 0f;
        layout.minWidth = 0f;
        layout.preferredWidth = -1f;
        layout.flexibleWidth = 1f;
    }

    void SetDeveloperTextLayout(TextMeshProUGUI text, float height, float fontSize) {
        if (text == null) return;
        RectTransform rect = text.transform as RectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, height);
        text.fontSize = fontSize;
        text.fontSizeMin = 10f;
        text.fontSizeMax = fontSize;
        text.alignment = TextAlignmentOptions.Center;

        LayoutElement layout = text.GetComponent<LayoutElement>();
        if (layout == null) layout = text.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        layout.flexibleHeight = 0f;
        layout.minWidth = 0f;
        layout.preferredWidth = -1f;
        layout.flexibleWidth = 1f;
    }

    void RefreshDeveloperToolsLabels() {
        if (developerToolsToggleText != null) developerToolsToggleText.text = LocalizationManager.Get(developerToolsKey);
        if (developerAddMoneyText != null) developerAddMoneyText.text = LocalizationManager.Get(developerAddMoneyKey, developerMoneyAmount);
        if (developerAddRatingText != null) developerAddRatingText.text = LocalizationManager.Get(developerAddRatingKey, developerRatingAmount);
        if (developerRemoveRatingText != null) developerRemoveRatingText.text = LocalizationManager.Get(developerRemoveRatingKey, developerRatingAmount);
        if (developerUnlockVehiclesText != null) developerUnlockVehiclesText.text = LocalizationManager.Get(developerUnlockVehiclesKey);
        if (developerUnlockMapsText != null) developerUnlockMapsText.text = LocalizationManager.Get(developerUnlockMapsKey);
        if (developerStatusText != null && string.IsNullOrEmpty(developerStatusText.text))
            developerStatusText.text = LocalizationManager.Get(developerReadyKey);
    }

    void SetDeveloperToolsDropdownVisible(bool visible) {
        if (developerToolsDropdown != null) developerToolsDropdown.SetActive(visible);
    }

    void OnDeveloperToolsToggleClicked() {
        if (developerToolsDropdown == null) return;
        SetDeveloperToolsDropdownVisible(!developerToolsDropdown.activeSelf);
    }

    void OnDeveloperAddMoneyClicked() {
        GameManager manager = GameManager.Instance;
        if (manager == null) {
            SetDeveloperStatus(developerUnavailableKey);
            return;
        }

        manager.DeveloperAddMoney(developerMoneyAmount);
        SetDeveloperStatus(developerMoneyAddedKey, developerMoneyAmount);
    }

    void OnDeveloperAddRatingClicked() {
        GameManager manager = GameManager.Instance;
        if (manager == null) {
            SetDeveloperStatus(developerUnavailableKey);
            return;
        }

        manager.DeveloperAdjustCourierRating(developerRatingAmount);
        SetDeveloperStatus(developerRatingAddedKey, developerRatingAmount);
    }

    void OnDeveloperRemoveRatingClicked() {
        GameManager manager = GameManager.Instance;
        if (manager == null) {
            SetDeveloperStatus(developerUnavailableKey);
            return;
        }

        manager.DeveloperAdjustCourierRating(-developerRatingAmount);
        SetDeveloperStatus(developerRatingRemovedKey, developerRatingAmount);
    }

    void OnDeveloperUnlockVehiclesClicked() {
        GameManager manager = GameManager.Instance;
        if (manager == null) {
            SetDeveloperStatus(developerUnavailableKey);
            return;
        }

        int changed = manager.DeveloperUnlockAllVehicles();
        SetDeveloperStatus(developerVehiclesUnlockedKey, changed);
    }

    void OnDeveloperUnlockMapsClicked() {
        GameManager manager = GameManager.Instance;
        if (manager == null) {
            SetDeveloperStatus(developerUnavailableKey);
            return;
        }

        int changed = manager.DeveloperUnlockAllMapsAndDifficulties();
        SetDeveloperStatus(developerMapsUnlockedKey, changed);
    }

    void SetDeveloperStatus(string key, params object[] args) {
        if (developerStatusText != null) developerStatusText.text = LocalizationManager.Get(key, args);
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

    void OnResolutionPreviousButtonClicked() {
        CycleResolution(-1);
    }

    void OnResolutionButtonClicked() {
        CycleResolution(1);
    }

    void CycleResolution(int direction) {
        Resolution[] resolutions = Screen.resolutions;
        if (resolutions == null || resolutions.Length == 0) return;

        int currentIndex = -1;
        int uniqueIndex = 0;
        for (int i = 0; i < resolutions.Length; i++) {
            bool isDuplicate = false;
            for (int j = 0; j < i; j++) {
                if (resolutions[j].width == resolutions[i].width && resolutions[j].height == resolutions[i].height) {
                    isDuplicate = true;
                    break;
                }
            }

            if (isDuplicate) continue;
            if (resolutions[i].width == Screen.width && resolutions[i].height == Screen.height) currentIndex = uniqueIndex;
            uniqueIndex++;
        }

        if (uniqueIndex == 0) return;
        if (currentIndex < 0) currentIndex = 0;
        int nextIndex = (currentIndex + direction) % uniqueIndex;
        if (nextIndex < 0) nextIndex += uniqueIndex;

        int uniqueResolutionIndex = 0;
        Resolution next = resolutions[0];
        for (int i = 0; i < resolutions.Length; i++) {
            bool isDuplicate = false;
            for (int j = 0; j < i; j++) {
                if (resolutions[j].width == resolutions[i].width && resolutions[j].height == resolutions[i].height) {
                    isDuplicate = true;
                    break;
                }
            }

            if (isDuplicate) continue;
            if (uniqueResolutionIndex == nextIndex) {
                next = resolutions[i];
                break;
            }
            uniqueResolutionIndex++;
        }

        DisplaySettings.SetResolution(next.width, next.height, DisplaySettings.GetFullscreenMode());
        RefreshDisplayLabels();
    }

    void OnFullscreenPreviousButtonClicked() {
        CycleFullscreen(-1);
    }

    void OnFullscreenButtonClicked() {
        CycleFullscreen(1);
    }

    void CycleFullscreen(int direction) {
        if (fullscreenModes == null || fullscreenModes.Length == 0) return;

        int currentIndex = 0;
        for (int i = 0; i < fullscreenModes.Length; i++) {
            if (fullscreenModes[i] == Screen.fullScreenMode) {
                currentIndex = i;
                break;
            }
        }

        int nextIndex = (currentIndex + direction) % fullscreenModes.Length;
        if (nextIndex < 0) nextIndex += fullscreenModes.Length;
        DisplaySettings.SetFullscreenMode(fullscreenModes[nextIndex]);
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
        if (developerToolsRuntimeRoot != null) developerToolsRuntimeRoot.SetActive(developerToolsEnabled);
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
