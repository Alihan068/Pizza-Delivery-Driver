using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the Garage entry point for Advanced Tuning and reveals the detailed tuning overlay on demand.
/// </summary>
/// <remarks>
/// Authored Toggle, Button and panel references are preferred. If a scene was saved before this
/// feature existed, <see cref="InitializeFallback"/> creates equivalent screen-space controls
/// beneath the existing Garage UI using its button sprite and TMP font. The feature remains optional:
/// the toggle is global, while the values opened by the panel are saved against the active vehicle.
/// </remarks>
public class GarageTuningUI : MonoBehaviour {

    [Header("Optional authored references")]
    [SerializeField] Toggle advancedTuningToggle;
    [SerializeField] Button driftTuningButton;
    [SerializeField] TMP_Text advancedTuningLabel;
    [SerializeField] AdvancedTuningPanel tuningPanel;

    [Header("Localization Keys")]
    [SerializeField] string advancedTuningKey = "garage.advancedTuning";
    [SerializeField] string advancedTuningOnKey = "garage.advancedTuning.on";
    [SerializeField] string advancedTuningOffKey = "garage.advancedTuning.off";
    [SerializeField] string driftTuningKey = "garage.driftTuning";

    [Header("Runtime fallback layout")]
    [SerializeField] Vector2 toggleAnchorMin = new Vector2(0.68f, 0.69f);
    [SerializeField] Vector2 toggleAnchorMax = new Vector2(0.93f, 0.76f);
    [SerializeField] Vector2 driftButtonAnchorMin = new Vector2(0.68f, 0.47f);
    [SerializeField] Vector2 driftButtonAnchorMax = new Vector2(0.93f, 0.55f);
    [SerializeField] Color controlColor = new Color(0.16f, 0.29f, 0.45f, 1f);
    [SerializeField] Color checkmarkColor = new Color(0.88f, 0.72f, 0.15f, 1f);
    [SerializeField] float labelFontSize = 17f;

    bool initialized;
    bool ignoreToggleEvents;
    bool vehicleAvailable;

    /// <summary>Creates the fallback entry and binds the optional panel to the Garage UI.</summary>
    /// <param name="parent">Screen-space Garage UI transform that owns the controls.</param>
    /// <param name="sprite">Existing Garage button sprite used by generated controls.</param>
    /// <param name="font">Existing Garage TMP font used by generated labels.</param>
    public void InitializeFallback(Transform parent, Sprite sprite, TMP_FontAsset font) {
        if (initialized) return;
        if (transform.parent != parent) transform.SetParent(parent, false);
        BuildMissingEntry(parent, sprite, font);
        BindListeners();
        initialized = true;
        Refresh();
    }

    /// <summary>Refreshes ownership, mode state and localized labels after a Garage change.</summary>
    public void Refresh() {
        if (!initialized) return;
        GameManager manager = GameManager.Instance;
        VehicleSaveData save = manager != null ? manager.GetCurrentVehicleSave() : null;
        vehicleAvailable = save != null && save.isUnlocked;
        bool enabled = GameSettings.AdvancedTuningEnabled;

        ignoreToggleEvents = true;
        if (advancedTuningToggle != null) advancedTuningToggle.isOn = enabled;
        ignoreToggleEvents = false;
        if (advancedTuningToggle != null) advancedTuningToggle.interactable = vehicleAvailable;
        if (driftTuningButton != null) {
            driftTuningButton.gameObject.SetActive(vehicleAvailable && enabled);
            driftTuningButton.interactable = vehicleAvailable && enabled;
        }
        if (advancedTuningLabel != null) {
            string stateKey = enabled ? advancedTuningOnKey : advancedTuningOffKey;
            advancedTuningLabel.text = LocalizationManager.Get(advancedTuningKey, LocalizationManager.Get(stateKey));
        }
        if (!vehicleAvailable && tuningPanel != null) tuningPanel.Close();
        if (vehicleAvailable && tuningPanel != null && tuningPanel.IsOpen)
            tuningPanel.RefreshFromVehicle();
    }

    /// <summary>Closes the detailed tuning panel without changing its saved values.</summary>
    public void ClosePanel() {
        if (tuningPanel != null) tuningPanel.Close();
    }

    void Awake() {
        GameSettings.Changed += Refresh;
        LocalizationManager.LanguageChanged += Refresh;
    }

    void OnDestroy() {
        GameSettings.Changed -= Refresh;
        LocalizationManager.LanguageChanged -= Refresh;
        UnbindListeners();
    }

    void BuildMissingEntry(Transform parent, Sprite sprite, TMP_FontAsset font) {
        if (advancedTuningToggle == null) {
            advancedTuningToggle = CreateToggle("AdvancedTuningToggle", parent as RectTransform,
                toggleAnchorMin, toggleAnchorMax, sprite, font, out advancedTuningLabel);
        }
        else if (advancedTuningLabel == null) {
            advancedTuningLabel = advancedTuningToggle.GetComponentInChildren<TMP_Text>(true);
        }

        if (driftTuningButton == null) {
            driftTuningButton = CreateButton("DriftTuningButton", parent as RectTransform,
                driftButtonAnchorMin, driftButtonAnchorMax, sprite, font, driftTuningKey);
        }

        if (tuningPanel == null) {
            GameObject panelObject = new GameObject("AdvancedTuningPanel", typeof(RectTransform));
            panelObject.transform.SetParent(parent, false);
            tuningPanel = panelObject.AddComponent<AdvancedTuningPanel>();
            tuningPanel.InitializeFallback(parent, sprite, font);
        }

        if (tuningPanel != null) tuningPanel.Close();
    }

    void BindListeners() {
        if (advancedTuningToggle != null) advancedTuningToggle.onValueChanged.AddListener(OnToggleChanged);
        if (driftTuningButton != null) driftTuningButton.onClick.AddListener(OnDriftTuningClicked);
    }

    void UnbindListeners() {
        if (advancedTuningToggle != null) advancedTuningToggle.onValueChanged.RemoveListener(OnToggleChanged);
        if (driftTuningButton != null) driftTuningButton.onClick.RemoveListener(OnDriftTuningClicked);
    }

    void OnToggleChanged(bool enabled) {
        if (ignoreToggleEvents || !vehicleAvailable) return;
        GameSettings.AdvancedTuningEnabled = enabled;
        if (!enabled && tuningPanel != null) tuningPanel.Close();
        Refresh();
    }

    void OnDriftTuningClicked() {
        if (!vehicleAvailable || !GameSettings.AdvancedTuningEnabled || tuningPanel == null) return;
        tuningPanel.Open();
    }

    static void ConfigureRect(RectTransform rect, Vector2 min, Vector2 max) {
        if (rect == null) return;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    static RectTransform CreateRect(string name, RectTransform parent, Vector2 min, Vector2 max) {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        ConfigureRect(rect, min, max);
        return rect;
    }

    Button CreateButton(string name, RectTransform parent, Vector2 min, Vector2 max, Sprite sprite,
        TMP_FontAsset font, string key) {
        RectTransform rect = CreateRect(name, parent, min, max);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = controlColor;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        TMP_Text label = CreateLabel(name + "Label", rect, font);
        label.text = LocalizationManager.Get(key);
        return button;
    }

    Toggle CreateToggle(string name, RectTransform parent, Vector2 min, Vector2 max, Sprite sprite,
        TMP_FontAsset font, out TMP_Text label) {
        RectTransform rect = CreateRect(name, parent, min, max);
        Image background = rect.gameObject.AddComponent<Image>();
        background.sprite = sprite;
        background.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        background.color = controlColor;
        Toggle toggle = rect.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = background;

        RectTransform checkRect = CreateRect("Checkmark", rect, new Vector2(0.03f, 0.17f), new Vector2(0.18f, 0.83f));
        Image checkmark = checkRect.gameObject.AddComponent<Image>();
        checkmark.sprite = sprite;
        checkmark.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        checkmark.color = checkmarkColor;
        toggle.graphic = checkmark;

        label = CreateLabel(name + "Label", rect, font);
        RectTransform labelRect = label.transform as RectTransform;
        labelRect.anchorMin = new Vector2(0.20f, 0f);
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        return toggle;
    }

    TMP_Text CreateLabel(string name, RectTransform parent, TMP_FontAsset font) {
        RectTransform rect = CreateRect(name, parent, Vector2.zero, Vector2.one);
        TMP_Text label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = labelFontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        return label;
    }
}
