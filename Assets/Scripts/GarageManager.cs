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

    [Header("Slot Management")]
    [Tooltip("Shared slot picker, reused here in copy-target mode to pick a destination for the active career.")]
    public SaveSlotSelectPanel copySlotPanel;
    public Button copyToSlotButton;

    void Start() {
        Time.timeScale = 1f;

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
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnClickMainMenu);
        if (copyToSlotButton != null) copyToSlotButton.onClick.AddListener(OnClickCopyToSlot);

        UpdateUI();
    }

    void UpdateUI() {
        if (GameManager.Instance == null) return;

        LocalizationManager.SetText(totalMoneyText, moneyKey, GameManager.Instance.totalMoney);

        if (rankText != null) {
            bool hasCareerLayer = GameManager.Instance.Career != null;
            rankText.gameObject.SetActive(hasCareerLayer);
            if (hasCareerLayer) LocalizationManager.SetText(rankText, rankKey, GameManager.Instance.CurrentRank, GameManager.Instance.totalReputation);
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

    /// <summary>Starts a session on the selected map, when the vehicle is owned.</summary>
    public void OnClickStartJob() {
        var save = GameManager.Instance.GetCurrentVehicleSave();
        if (save == null || !save.isUnlocked) return;

        var map = GameManager.Instance.currentMap;
        if (map == null || string.IsNullOrEmpty(map.sceneName)) {
            Debug.LogError("No map is selected, or the selected map has no scene assigned.");
            return;
        }
        SceneManager.LoadScene(map.sceneName);
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

    /// <summary>Opens the slot picker to copy the active career onto another slot.</summary>
    public void OnClickCopyToSlot() {
        if (copySlotPanel != null) copySlotPanel.Open(SaveSlotSelectMode.CopyTarget);
    }
}
