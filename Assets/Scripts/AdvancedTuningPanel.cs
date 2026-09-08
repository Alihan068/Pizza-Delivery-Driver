using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Presents the optional per-vehicle speed and drift-feel controls in a dismissible Garage overlay.
/// </summary>
/// <remarks>
/// The panel writes only through GameManager's clamped tuning API. It never changes VehicleData or
/// its embedded VehicleDrivingSettings asset, and the resulting values are consumed on the next
/// vehicle spawn. When authored references are absent, GarageTuningUI creates this panel with the
/// same controls at runtime so a partially wired scene remains testable after import.
/// </remarks>
public class AdvancedTuningPanel : MonoBehaviour {

    [Header("Optional authored references")]
    [SerializeField] Button closeButton;
    [SerializeField] Slider speedSlider;
    [SerializeField] Slider driftGripSlider;
    [SerializeField] Slider driftSteeringSlider;
    [SerializeField] Slider gripEntrySlider;
    [SerializeField] TMP_Text speedValueText;
    [SerializeField] TMP_Text driftGripValueText;
    [SerializeField] TMP_Text driftSteeringValueText;
    [SerializeField] TMP_Text gripEntryValueText;
    [SerializeField] TMP_Text titleText;
    [SerializeField] Button resetButton;
    [SerializeField] Button gripPresetButton;
    [SerializeField] Button balancedPresetButton;
    [SerializeField] Button slidePresetButton;

    [Header("Localization Keys")]
    [SerializeField] string titleKey = "garage.advancedTuning.title";
    [SerializeField] string speedKey = "garage.advancedTuning.speed";
    [SerializeField] string speedValueKey = "garage.advancedTuning.speedValue";
    [SerializeField] string driftGripKey = "garage.advancedTuning.driftGrip";
    [SerializeField] string driftGripValueKey = "garage.advancedTuning.driftGripValue";
    [SerializeField] string driftSteeringKey = "garage.advancedTuning.driftSteering";
    [SerializeField] string driftSteeringValueKey = "garage.advancedTuning.driftSteeringValue";
    [SerializeField] string gripEntryKey = "garage.advancedTuning.gripEntry";
    [SerializeField] string gripEntryValueKey = "garage.advancedTuning.gripEntryValue";
    [SerializeField] string resetKey = "garage.advancedTuning.reset";
    [SerializeField] string gripPresetKey = "garage.advancedTuning.presetGrip";
    [SerializeField] string balancedPresetKey = "garage.advancedTuning.presetBalanced";
    [SerializeField] string slidePresetKey = "garage.advancedTuning.presetSlide";
    [SerializeField] string closeKey = "common.cross";

    [Header("Runtime fallback layout")]
    [SerializeField] Vector2 panelAnchorMin = new Vector2(0.18f, 0.08f);
    [SerializeField] Vector2 panelAnchorMax = new Vector2(0.82f, 0.92f);
    [SerializeField] Vector2 controlLeftAnchor = new Vector2(0.10f, 0.69f);
    [SerializeField] Vector2 controlRightAnchor = new Vector2(0.90f, 0.77f);
    [SerializeField] Vector2 valueLeftAnchor = new Vector2(0.55f, 0.69f);
    [SerializeField] Vector2 valueRightAnchor = new Vector2(0.90f, 0.77f);
    [SerializeField] Vector2 sliderLeftAnchor = new Vector2(0.10f, 0.59f);
    [SerializeField] Vector2 sliderRightAnchor = new Vector2(0.90f, 0.65f);
    [SerializeField] Vector2 presetLeftAnchor = new Vector2(0.08f, 0.08f);
    [SerializeField] Vector2 presetRightAnchor = new Vector2(0.92f, 0.16f);
    [SerializeField] Color overlayColor = new Color(0f, 0f, 0f, 0.62f);
    [SerializeField] Color panelColor = new Color(0.06f, 0.08f, 0.12f, 0.98f);
    [SerializeField] Color controlColor = new Color(0.16f, 0.29f, 0.45f, 1f);
    [SerializeField] Color handleColor = new Color(0.88f, 0.72f, 0.15f, 1f);
    [SerializeField] float titleFontSize = 28f;
    [SerializeField] float labelFontSize = 18f;
    [SerializeField] float valueFontSize = 16f;
    [SerializeField] float buttonFontSize = 16f;
    [SerializeField] float gripPresetGripPosition = 0.80f;
    [SerializeField] float gripPresetSteeringPosition = 0.30f;
    [SerializeField] float gripPresetEntryPosition = 0.70f;
    [SerializeField] float slidePresetGripPosition = 0.20f;
    [SerializeField] float slidePresetSteeringPosition = 0.85f;
    [SerializeField] float slidePresetEntryPosition = 0.20f;

    bool listenersBound;
    bool ignoreSliderEvents;
    Sprite uiSprite;
    TMP_FontAsset uiFont;

    /// <summary>Raised after the panel is closed by its X button or the Escape key.</summary>
    public event Action Closed;

    /// <summary>Whether the panel is currently visible to the player.</summary>
    public bool IsOpen => gameObject.activeSelf;

    /// <summary>
    /// Builds missing controls under the supplied Garage UI parent and prepares the panel for use.
    /// </summary>
    /// <param name="parent">Screen-space Garage UI transform that owns the overlay.</param>
    /// <param name="sprite">Existing UI sprite used to keep fallback controls visually consistent.</param>
    /// <param name="font">Existing TMP font used by the Garage labels.</param>
    public void InitializeFallback(Transform parent, Sprite sprite, TMP_FontAsset font) {
        uiSprite = sprite;
        uiFont = font;
        if (transform.parent != parent) transform.SetParent(parent, false);
        ConfigureRect(transform as RectTransform, Vector2.zero, Vector2.one);
        BuildMissingControls();
        BindListeners();
        gameObject.SetActive(false);
    }

    /// <summary>Opens the panel and loads the active vehicle's current tuning values.</summary>
    public void Open() {
        if (GameManager.Instance == null || GameManager.Instance.currentVehicle == null) return;
        gameObject.SetActive(true);
        RefreshFromVehicle();
    }

    /// <summary>Closes the panel and returns control to the Garage screen.</summary>
    public void Close() {
        if (!gameObject.activeSelf) return;
        gameObject.SetActive(false);
        Closed?.Invoke();
    }

    /// <summary>Refreshes the controls after the selected vehicle changes in the Garage.</summary>
    public void RefreshFromVehicle() {
        GameManager manager = GameManager.Instance;
        VehicleData vehicle = manager != null ? manager.currentVehicle : null;
        if (vehicle == null) return;

        VehicleDrivingSettings settings = vehicle.drivingSettings != null
            ? vehicle.drivingSettings
            : new VehicleDrivingSettings();
        float speedMax = manager.GetUnlockedMaxSpeed();
        float speedMin = VehicleTuningRules.GetSafeMinimumSpeed(vehicle.minimumTuningSpeed, speedMax);
        float selectedSpeed = manager.GetSelectedBaseSpeed();
        VehicleSaveData save = manager.GetCurrentVehicleSave();
        bool hasCustom = save != null && save.hasCustomTuning;
        float grip = VehicleTuningRules.ResolvePlayerValue(GameSettings.AdvancedTuningEnabled, hasCustom,
            save != null ? save.tunedDriftGrip : 0f, settings.driftGrip,
            settings.playerDriftGripMin, settings.playerDriftGripMax);
        float steering = VehicleTuningRules.ResolvePlayerValue(GameSettings.AdvancedTuningEnabled, hasCustom,
            save != null ? save.tunedDriftSteeringMultiplier : 0f, settings.driftSteeringMultiplier,
            settings.playerDriftSteeringMultiplierMin, settings.playerDriftSteeringMultiplierMax);
        float entry = VehicleTuningRules.ResolvePlayerValue(GameSettings.AdvancedTuningEnabled, hasCustom,
            save != null ? save.tunedGripEnterTime : 0f, settings.gripEnterTime,
            settings.playerGripEnterTimeMin, settings.playerGripEnterTimeMax);

        ignoreSliderEvents = true;
        ConfigureSlider(speedSlider, speedMin, speedMax, selectedSpeed);
        ConfigureSlider(driftGripSlider, settings.playerDriftGripMin, settings.playerDriftGripMax, grip);
        ConfigureSlider(driftSteeringSlider, settings.playerDriftSteeringMultiplierMin,
            settings.playerDriftSteeringMultiplierMax, steering);
        ConfigureSlider(gripEntrySlider, settings.playerGripEnterTimeMin, settings.playerGripEnterTimeMax, entry);
        ignoreSliderEvents = false;

        if (GameSettings.AdvancedTuningEnabled && hasCustom &&
            (!Mathf.Approximately(save.tunedSpeed, selectedSpeed) ||
             !Mathf.Approximately(save.tunedDriftGrip, grip) ||
             !Mathf.Approximately(save.tunedDriftSteeringMultiplier, steering) ||
             !Mathf.Approximately(save.tunedGripEnterTime, entry))) {
            manager.SaveCurrentVehicleTuning(selectedSpeed, grip, steering, entry);
        }

        RefreshText(speedMax);
    }

    void Awake() {
        LocalizationManager.LanguageChanged += RefreshLocalizedText;
        BindListeners();
    }

    void OnDestroy() {
        LocalizationManager.LanguageChanged -= RefreshLocalizedText;
        UnbindListeners();
    }

    void Update() {
        if (!gameObject.activeSelf || Keyboard.current == null) return;
        if (Keyboard.current.escapeKey.wasPressedThisFrame) Close();
    }

    void BuildMissingControls() {
        RectTransform root = transform as RectTransform;
        if (root == null) return;

        Image backdrop = GetComponent<Image>();
        if (backdrop == null) backdrop = gameObject.AddComponent<Image>();
        backdrop.color = overlayColor;
        backdrop.raycastTarget = true;

        RectTransform panel = CreateRect("TuningPanel", root, panelAnchorMin, panelAnchorMax);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = uiSprite;
        panelImage.type = uiSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        panelImage.color = panelColor;

        titleText = CreateLabel("Title", panel, new Vector2(0.10f, 0.88f), new Vector2(0.90f, 0.98f), titleFontSize);
        closeButton = CreateButton("Close", panel, new Vector2(0.86f, 0.89f), new Vector2(0.97f, 0.98f), closeKey, buttonFontSize);

        speedSlider = CreateTuningRow(panel, "SpeedRow", speedKey, 0.70f,
            out speedValueText);
        driftGripSlider = CreateTuningRow(panel, "DriftGripRow", driftGripKey, 0.52f,
            out driftGripValueText);
        driftSteeringSlider = CreateTuningRow(panel, "DriftSteeringRow", driftSteeringKey,
            0.34f, out driftSteeringValueText);
        gripEntrySlider = CreateTuningRow(panel, "GripEntryRow", gripEntryKey, 0.16f,
            out gripEntryValueText);

        resetButton = CreateButton("Reset", panel, new Vector2(0.08f, 0.04f), new Vector2(0.27f, 0.12f),
            resetKey, buttonFontSize);
        gripPresetButton = CreateButton("GripPreset", panel, new Vector2(0.30f, 0.04f), new Vector2(0.51f, 0.12f),
            gripPresetKey, buttonFontSize);
        balancedPresetButton = CreateButton("BalancedPreset", panel, new Vector2(0.54f, 0.04f), new Vector2(0.73f, 0.12f),
            balancedPresetKey, buttonFontSize);
        slidePresetButton = CreateButton("SlidePreset", panel, new Vector2(0.76f, 0.04f), new Vector2(0.93f, 0.12f),
            slidePresetKey, buttonFontSize);
        RefreshLocalizedText();
    }

    Slider CreateTuningRow(RectTransform parent, string name, string labelKey, float centerY,
        out TMP_Text valueText) {
        TMP_Text label = CreateLabel(name + "Label", parent, new Vector2(0.10f, centerY + 0.04f),
            new Vector2(0.48f, centerY + 0.12f), labelFontSize);
        LocalizedTextBinding(label, labelKey);
        valueText = CreateLabel(name + "Value", parent, new Vector2(0.52f, centerY + 0.04f),
            new Vector2(0.90f, centerY + 0.12f), valueFontSize);
        Slider slider = CreateSlider(name, parent, new Vector2(0.10f, centerY - 0.02f),
            new Vector2(0.90f, centerY + 0.03f));
        return slider;
    }

    void LocalizedTextBinding(TMP_Text label, string key) {
        LocalizedText binding = label.gameObject.AddComponent<LocalizedText>();
        binding.SetKey(key);
    }

    void BindListeners() {
        if (listenersBound) return;
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (resetButton != null) resetButton.onClick.AddListener(ResetToClassic);
        if (gripPresetButton != null) gripPresetButton.onClick.AddListener(ApplyGripPreset);
        if (balancedPresetButton != null) balancedPresetButton.onClick.AddListener(ApplyBalancedPreset);
        if (slidePresetButton != null) slidePresetButton.onClick.AddListener(ApplySlidePreset);
        if (speedSlider != null) speedSlider.onValueChanged.AddListener(OnSliderChanged);
        if (driftGripSlider != null) driftGripSlider.onValueChanged.AddListener(OnSliderChanged);
        if (driftSteeringSlider != null) driftSteeringSlider.onValueChanged.AddListener(OnSliderChanged);
        if (gripEntrySlider != null) gripEntrySlider.onValueChanged.AddListener(OnSliderChanged);
        listenersBound = true;
    }

    void UnbindListeners() {
        if (!listenersBound) return;
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (resetButton != null) resetButton.onClick.RemoveListener(ResetToClassic);
        if (gripPresetButton != null) gripPresetButton.onClick.RemoveListener(ApplyGripPreset);
        if (balancedPresetButton != null) balancedPresetButton.onClick.RemoveListener(ApplyBalancedPreset);
        if (slidePresetButton != null) slidePresetButton.onClick.RemoveListener(ApplySlidePreset);
        if (speedSlider != null) speedSlider.onValueChanged.RemoveListener(OnSliderChanged);
        if (driftGripSlider != null) driftGripSlider.onValueChanged.RemoveListener(OnSliderChanged);
        if (driftSteeringSlider != null) driftSteeringSlider.onValueChanged.RemoveListener(OnSliderChanged);
        if (gripEntrySlider != null) gripEntrySlider.onValueChanged.RemoveListener(OnSliderChanged);
        listenersBound = false;
    }

    void OnSliderChanged(float value) {
        if (ignoreSliderEvents || GameManager.Instance == null) return;
        GameManager.Instance.SaveCurrentVehicleTuning(speedSlider.value, driftGripSlider.value,
            driftSteeringSlider.value, gripEntrySlider.value);
        RefreshText(speedSlider.maxValue);
    }

    void ResetToClassic() {
        if (GameManager.Instance == null) return;
        GameManager.Instance.ResetCurrentVehicleTuning();
        RefreshFromVehicle();
    }

    void ApplyBalancedPreset() {
        VehicleDrivingSettings settings = GetAuthoredSettings();
        if (settings == null) return;
        SetTuningValues(settings.driftGrip, settings.driftSteeringMultiplier, settings.gripEnterTime);
    }

    void ApplyGripPreset() {
        VehicleDrivingSettings settings = GetAuthoredSettings();
        if (settings == null) return;
        SetTuningValues(
            Mathf.Lerp(settings.playerDriftGripMin, settings.playerDriftGripMax, Mathf.Clamp01(gripPresetGripPosition)),
            Mathf.Lerp(settings.playerDriftSteeringMultiplierMin, settings.playerDriftSteeringMultiplierMax,
                Mathf.Clamp01(gripPresetSteeringPosition)),
            Mathf.Lerp(settings.playerGripEnterTimeMin, settings.playerGripEnterTimeMax,
                Mathf.Clamp01(gripPresetEntryPosition)));
    }

    void ApplySlidePreset() {
        VehicleDrivingSettings settings = GetAuthoredSettings();
        if (settings == null) return;
        SetTuningValues(
            Mathf.Lerp(settings.playerDriftGripMin, settings.playerDriftGripMax, Mathf.Clamp01(slidePresetGripPosition)),
            Mathf.Lerp(settings.playerDriftSteeringMultiplierMin, settings.playerDriftSteeringMultiplierMax,
                Mathf.Clamp01(slidePresetSteeringPosition)),
            Mathf.Lerp(settings.playerGripEnterTimeMin, settings.playerGripEnterTimeMax,
                Mathf.Clamp01(slidePresetEntryPosition)));
    }

    void SetTuningValues(float grip, float steering, float entry) {
        ignoreSliderEvents = true;
        driftGripSlider.value = grip;
        driftSteeringSlider.value = steering;
        gripEntrySlider.value = entry;
        ignoreSliderEvents = false;
        OnSliderChanged(0f);
    }

    VehicleDrivingSettings GetAuthoredSettings() {
        VehicleData vehicle = GameManager.Instance != null ? GameManager.Instance.currentVehicle : null;
        return vehicle != null ? (vehicle.drivingSettings != null ? vehicle.drivingSettings : new VehicleDrivingSettings()) : null;
    }

    void RefreshText(float speedMax) {
        if (titleText != null) titleText.text = LocalizationManager.Get(titleKey);
        if (speedValueText != null) speedValueText.text = LocalizationManager.Get(speedValueKey,
            speedSlider.minValue, speedSlider.value, speedMax);
        if (driftGripValueText != null) driftGripValueText.text = LocalizationManager.Get(driftGripValueKey, driftGripSlider.value);
        if (driftSteeringValueText != null) driftSteeringValueText.text = LocalizationManager.Get(driftSteeringValueKey, driftSteeringSlider.value);
        if (gripEntryValueText != null) gripEntryValueText.text = LocalizationManager.Get(gripEntryValueKey, gripEntrySlider.value);
    }

    void RefreshLocalizedText() {
        if (!gameObject.activeSelf && speedSlider == null) return;
        RefreshText(speedSlider != null ? speedSlider.maxValue : 0f);
        SetButtonText(closeButton, closeKey);
        SetButtonText(resetButton, resetKey);
        SetButtonText(gripPresetButton, gripPresetKey);
        SetButtonText(balancedPresetButton, balancedPresetKey);
        SetButtonText(slidePresetButton, slidePresetKey);
    }

    static void SetButtonText(Button button, string key) {
        if (button == null) return;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = LocalizationManager.Get(key);
    }

    static void ConfigureSlider(Slider slider, float minimum, float maximum, float value) {
        if (slider == null) return;
        slider.minValue = Mathf.Min(minimum, maximum);
        slider.maxValue = Mathf.Max(minimum, maximum);
        slider.wholeNumbers = false;
        slider.value = VehicleTuningRules.ClampPlayerValue(value, slider.minValue, slider.maxValue);
    }

    static void ConfigureRect(RectTransform rect, Vector2 min, Vector2 max) {
        if (rect == null) return;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    TMP_Text CreateLabel(string name, RectTransform parent, Vector2 min, Vector2 max, float size) {
        RectTransform rect = CreateRect(name, parent, min, max);
        TMP_Text label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = uiFont;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        return label;
    }

    Button CreateButton(string name, RectTransform parent, Vector2 min, Vector2 max, string key, float size) {
        RectTransform rect = CreateRect(name, parent, min, max);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = uiSprite;
        image.type = uiSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = controlColor;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        TMP_Text label = CreateLabel(name + "Label", rect, Vector2.zero, Vector2.one, size);
        label.text = LocalizationManager.Get(key);
        return button;
    }

    Slider CreateSlider(string name, RectTransform parent, Vector2 min, Vector2 max) {
        RectTransform rect = CreateRect(name, parent, min, max);
        Slider slider = rect.gameObject.AddComponent<Slider>();
        Image background = rect.gameObject.AddComponent<Image>();
        background.sprite = uiSprite;
        background.type = uiSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        background.color = new Color(controlColor.r, controlColor.g, controlColor.b, 0.55f);
        RectTransform handleRect = CreateRect("Handle", rect, new Vector2(0f, -0.45f), new Vector2(0f, 1.45f));
        Image handle = handleRect.gameObject.AddComponent<Image>();
        handle.sprite = uiSprite;
        handle.type = uiSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        handle.color = handleColor;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    static RectTransform CreateRect(string name, Transform parent, Vector2 min, Vector2 max) {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        ConfigureRect(rect, min, max);
        return rect;
    }

}
