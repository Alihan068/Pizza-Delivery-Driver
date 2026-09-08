using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GarageManager : MonoBehaviour {
    [Header("Localization Keys")]
    [SerializeField] string moneyKey = "common.money";
    [SerializeField] string buyKey = "garage.buyPrice";
    [SerializeField] string capacityKey = "stat.pizzaValue";
    [SerializeField] string percentKey = "stat.percentValue";
    [SerializeField] string numberKey = "stat.numberValue";
    [SerializeField] string comparisonKey = "stat.comparison";
    [SerializeField] string persistentSuffixKey = "stat.persistentSuffix";
    [SerializeField] string rankKey = "garage.rank";

    void OnEnable() { LocalizationManager.LanguageChanged += UpdateUI; }
    void OnDisable() { LocalizationManager.LanguageChanged -= UpdateUI; }

    /// <summary>
    /// Binds one upgrade card in the garage to the stat it sells.
    /// </summary>
    /// <remarks>
    /// Authored as a list rather than six named fields so the garage does not assume how many stats
    /// exist. Adding or removing a card is an Inspector change.
    /// </remarks>
    [System.Serializable]
    public class StatPanelBinding {
        [Tooltip("Which stat this card sells.")]
        public VehicleStatId stat;

        [Tooltip("The card that shows it.")]
        public StatDisplay panel;

        /// <summary>Localization key for this card's title. Serialized name retained for existing bindings.</summary>
        [Tooltip("Localization key for the stat card title.")]
        public string displayName;
    }

    [Header("UI References")]
    public TextMeshProUGUI totalMoneyText;
    public TextMeshProUGUI currentVehicleNameText;
    public Image chosenVehicleImage;
    public Button startButton;

    [Tooltip("Shows the active career's rank and reputation. Left blank when no CareerData is configured.")]
    public TextMeshProUGUI rankText;

    [Header("Stat Cards")]
    [SerializeField] StatPanelBinding[] statPanels;

    [Header("Vehicle Lock UI")]
    public GameObject statsPanel;
    public GameObject lockedPanel;
    public TextMeshProUGUI purchasePriceText;
    public Button purchaseButton;

    [Header("Navigation")]
    public Button mainMenuButton;
    [SerializeField] Button nextVehicleButton;
    [SerializeField] Button previousVehicleButton;
    /// <summary>Opens the shared settings panel from the garage corner control.</summary>
    public Button settingsButton;
    /// <summary>Shared settings panel instance owned by this garage scene.</summary>
    public SettingsPanel settingsPanel;

    [Header("Responsive Settings Entry Point")]
    [Tooltip("Percentage anchors used by the Garage Settings button so it stays inside the screen at every aspect ratio.")]
    [SerializeField] Vector2 settingsButtonAnchorMin = new Vector2(0.82f, 0.03f);
    [SerializeField] Vector2 settingsButtonAnchorMax = new Vector2(0.98f, 0.11f);

    [Header("Map Selection")]
    /// <summary>Legacy inline map button. The active garage uses <see cref="startButton"/> to open the separate map selection scene.</summary>
    public Button mapSelectionButton;
    /// <summary>Legacy inline picker reference. It should remain empty after the map selection scene migration.</summary>
    public MapSelectionPanel mapSelectionPanel;

    [Header("Slot Management")]
    [Tooltip("Shared slot picker, reused here in copy-target mode to pick a destination for the active career.")]
    public SaveSlotSelectPanel copySlotPanel;
    public Button copyToSlotButton;

    [Header("Advanced Tuning")]
    [Tooltip("Optional Garage entry and panel. A fallback is created under the existing Garage UI when this is empty.")]
    [SerializeField] GarageTuningUI advancedTuningUI;

    void Start() {
        Time.timeScale = 1f;
        ConfigureSettingsEntryPoint();
        ConfigureAdvancedTuningEntryPoint();

        // Bound in code rather than through Inspector events: a persistent UnityEvent left behind
        // on one of these buttons once made a single click buy two levels (BF-016).
        if (statPanels != null) {
            foreach (var binding in statPanels) {
                if (binding == null || binding.panel == null || binding.panel.upgradeButton == null) continue;
                VehicleStatId stat = binding.stat;
                binding.panel.upgradeButton.onClick.AddListener(() => OnClickUpgrade(stat));
            }
        }

        if (purchaseButton != null) purchaseButton.onClick.AddListener(OnClickPurchaseVehicle);
        if (settingsPanel != null) settingsPanel.Closed += CloseSettings;
        if (settingsButton != null) settingsButton.onClick.AddListener(OnClickSettings);
        if (startButton != null) startButton.onClick.AddListener(OnClickMapSelection);
        if (nextVehicleButton != null) nextVehicleButton.onClick.AddListener(OnClickNextVehicle);
        if (previousVehicleButton != null) previousVehicleButton.onClick.AddListener(OnClickPrevVehicle);

        UpdateUI();
    }

    void ConfigureSettingsEntryPoint() {
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
        if (settingsButton == null) {
            Debug.LogWarning("Garage settings button is not assigned.");
            return;
        }

        settingsButton.gameObject.SetActive(true);
        RectTransform buttonRect = settingsButton.transform as RectTransform;
        if (buttonRect == null) return;

        buttonRect.anchorMin = settingsButtonAnchorMin;
        buttonRect.anchorMax = settingsButtonAnchorMax;
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;
        TMP_Text buttonLabel = settingsButton.GetComponentInChildren<TMP_Text>(true);
        if (buttonLabel != null) {
            buttonLabel.enableAutoSizing = true;
            buttonLabel.fontSizeMin = 12f;
            buttonLabel.fontSizeMax = 24f;
            buttonLabel.alignment = TextAlignmentOptions.Center;
            buttonLabel.raycastTarget = false;
        }
        settingsButton.transform.SetAsLastSibling();
    }

    void ConfigureAdvancedTuningEntryPoint() {
        Transform parent = transform.parent;
        if (parent == null) {
            Debug.LogWarning("Garage Advanced Tuning has no UI parent for its fallback controls.");
            return;
        }

        Sprite sprite = null;
        if (settingsButton != null) {
            Image image = settingsButton.GetComponent<Image>();
            if (image != null) sprite = image.sprite;
        }
        TMP_FontAsset font = null;
        TMP_Text sourceText = startButton != null ? startButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (sourceText == null && currentVehicleNameText != null) sourceText = currentVehicleNameText;
        if (sourceText != null) font = sourceText.font;

        if (advancedTuningUI != null) {
            advancedTuningUI.InitializeFallback(parent, sprite, font);
            return;
        }

        GameObject host = new GameObject("GarageTuningUI", typeof(RectTransform));
        host.transform.SetParent(parent, false);
        advancedTuningUI = host.AddComponent<GarageTuningUI>();
        advancedTuningUI.InitializeFallback(parent, sprite, font);
    }

    void OnDestroy() {
        if (settingsPanel != null) settingsPanel.Closed -= CloseSettings;
    }

    void UpdateUI() {
        if (GameManager.Instance == null) return;

        LocalizationManager.SetText(totalMoneyText, moneyKey, GameManager.Instance.totalMoney);

        if (rankText != null) {
            bool hasCareerLayer = GameManager.Instance.Career != null;
            rankText.gameObject.SetActive(hasCareerLayer);
            if (hasCareerLayer) LocalizationManager.SetText(rankText, rankKey, GameManager.Instance.CurrentRank, GameManager.Instance.CourierRating);
        }

        var currentVehicle = GameManager.Instance.currentVehicle;
        var saveData = GameManager.Instance.GetCurrentVehicleSave();
        if (saveData == null || currentVehicle == null) return;

        currentVehicleNameText.text = currentVehicle.GetDisplayName();

        Sprite displaySprite = null;
        if (currentVehicle.vehicleIcon != null) {
            displaySprite = currentVehicle.vehicleIcon;
        }
        else if (currentVehicle.vehiclePrefab != null) {
            SpriteRenderer sr = currentVehicle.vehiclePrefab.GetComponent<SpriteRenderer>();
            if (sr != null) displaySprite = sr.sprite;
        }

        if (displaySprite != null) {
            chosenVehicleImage.sprite = displaySprite;
            chosenVehicleImage.enabled = true;
            chosenVehicleImage.preserveAspect = true;
        }
        else {
            chosenVehicleImage.enabled = false;
        }

        // --- Locked vehicle: show purchase UI instead of stat panels ---
        bool unlocked = saveData.isUnlocked;
        if (statsPanel != null) statsPanel.SetActive(unlocked);
        if (lockedPanel != null) lockedPanel.SetActive(!unlocked);
        if (startButton != null) startButton.interactable = unlocked;
        if (advancedTuningUI != null) advancedTuningUI.Refresh();

        if (!unlocked) {
            LocalizationManager.SetText(purchasePriceText, buyKey, currentVehicle.price);
            return;
        }

        if (statPanels == null) return;
        foreach (var binding in statPanels) {
            if (binding == null || binding.panel == null) continue;
            SetupStatPanel(binding, currentVehicle);
        }
    }

    void SetupStatPanel(StatPanelBinding binding, VehicleData vehicle) {
        int level = GameManager.Instance.GetLevel(binding.stat);
        int maxLevel = vehicle.GetMaxLevel(binding.stat);
        bool isMaxed = level >= maxLevel;

        string valueDisplay = FormatStatValue(binding.stat, GameManager.Instance.GetStatValueAtLevel(binding.stat, level));
        if (!isMaxed) {
            valueDisplay = LocalizationManager.Get(comparisonKey, valueDisplay,
                FormatStatValue(binding.stat, GameManager.Instance.GetStatValueAtLevel(binding.stat, level + 1)));
        }

        int cost = GameManager.Instance.GetUpgradeCost(binding.stat, level);
        string title = LocalizationManager.Get(binding.displayName);
        // Marks the two stats that survive a vehicle switch, so the garage does not silently imply
        // every card resets the way the other four do.
        if (VehicleStatOwnership.IsDriverBound(binding.stat)) title += LocalizationManager.Get(persistentSuffixKey);
        binding.panel.Setup(title, LocalizationManager.Get(vehicle.GetDescription(binding.stat)), level, maxLevel, cost, isMaxed, valueDisplay);
    }

    string FormatStatValue(VehicleStatId stat, float value) {
        switch (stat) {
            case VehicleStatId.Armor:
            case VehicleStatId.Protection:
                return LocalizationManager.Get(percentKey, Mathf.RoundToInt(value * 100));
            case VehicleStatId.Capacity:
                return LocalizationManager.Get(capacityKey, Mathf.RoundToInt(value));
            default:
                return LocalizationManager.Get(numberKey, value);
        }
    }

    /// <summary>Buys one level of a stat and refreshes the garage.</summary>
    /// <param name="stat">Which stat the pressed card sells.</param>
    public void OnClickUpgrade(VehicleStatId stat) {
        if (GameManager.Instance.TryUpgradeStat(stat)) UpdateUI();
    }

    /// <summary>Buys the vehicle currently on display.</summary>
    public void OnClickPurchaseVehicle() {
        if (GameManager.Instance.TryPurchaseVehicle()) UpdateUI();
    }

    /// <summary>Shows the next vehicle in the registry.</summary>
    public void OnClickNextVehicle() {
        GameManager.Instance.ChangeVehicle(1);
        UpdateUI();
    }

    /// <summary>Shows the previous vehicle in the registry.</summary>
    public void OnClickPrevVehicle() {
        GameManager.Instance.ChangeVehicle(-1);
        UpdateUI();
    }

    /// <summary>Opens the separate map selection screen when the current vehicle is owned.</summary>
    public void OnClickMapSelection() {
        var save = GameManager.Instance != null ? GameManager.Instance.GetCurrentVehicleSave() : null;
        if (save == null || !save.isUnlocked) return;

        var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
        if (config == null || string.IsNullOrEmpty(config.mapSelectionScene)) {
            Debug.LogError("No map selection scene is configured in GameConfig.");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(config.mapSelectionScene);
    }

    /// <summary>Legacy entry point retained for scene and external compatibility.</summary>
    public void OnClickStartJob() {
        OnClickMapSelection();
    }

    /// <summary>Returns to the main menu.</summary>
    public void OnClickMainMenu() {
        var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
        if (config == null) {
            Debug.LogError("No GameConfig is assigned, so the main menu scene name is unknown.");
            return;
        }
        SceneManager.LoadScene(config.mainMenuScene);
    }

    /// <summary>Opens the shared garage settings panel.</summary>
    public void OnClickSettings() {
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(true);
    }

    void CloseSettings() {
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
    }

    /// <summary>Opens the slot picker to copy the active career onto another slot.</summary>
    public void OnClickCopyToSlot() {
        if (copySlotPanel != null) copySlotPanel.Open(SaveSlotSelectMode.CopyTarget);
    }
}
