using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GarageManager : MonoBehaviour {
    [Header("UI References")]
    public TextMeshProUGUI totalMoneyText;
    public TextMeshProUGUI currentVehicleNameText;
    public Image chosenVehicleImage;
    public Button startButton;

    [Header("Stat Panels")]
    // Assign the objects with StatDisplay script here in the Inspector
    public StatDisplay speedPanel;
    public StatDisplay turnPanel;
    public StatDisplay healthPanel;
    public StatDisplay armorPanel;
    public StatDisplay capacityPanel;
    public StatDisplay protectionPanel;

    [Header("Vehicle Lock UI")]
    public GameObject statsPanel;
    public GameObject lockedPanel;
    public TextMeshProUGUI purchasePriceText;
    public Button purchaseButton;

    [Header("Navigation")]
    public Button mainMenuButton;

    void Start() {
        Time.timeScale = 1f;

        if (speedPanel != null) speedPanel.upgradeButton.onClick.AddListener(() => OnClickUpgrade("Speed"));
        if (turnPanel != null) turnPanel.upgradeButton.onClick.AddListener(() => OnClickUpgrade("Turn"));
        if (healthPanel != null) healthPanel.upgradeButton.onClick.AddListener(() => OnClickUpgrade("Health"));
        if (armorPanel != null) armorPanel.upgradeButton.onClick.AddListener(() => OnClickUpgrade("Armor"));
        if (capacityPanel != null) capacityPanel.upgradeButton.onClick.AddListener(() => OnClickUpgrade("Capacity"));
        if (protectionPanel != null) protectionPanel.upgradeButton.onClick.AddListener(() => OnClickUpgrade("Protection"));

        if (purchaseButton != null) purchaseButton.onClick.AddListener(OnClickPurchaseVehicle);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnClickMainMenu);

        UpdateUI();
    }

    void UpdateUI() {
        //Money
        if (totalMoneyText != null)
            totalMoneyText.text = "$ " + GameManager.Instance.totalMoney;

        // Get Current Vehicle Data
        var currentVehicle = GameManager.Instance.currentVehicle;
        var saveData = GameManager.Instance.GetCurrentVehicleSave();

        if (saveData == null || currentVehicle == null) return;

        // Update Vehicle Name
        currentVehicleNameText.text = currentVehicle.vehicleName;

        // Update Vehicle Image Logic
        Sprite displaySprite = null;

        if (currentVehicle.vehicleIcon != null) {
            displaySprite = currentVehicle.vehicleIcon;
        }
        else if (currentVehicle.vehiclePrefab != null) {
            SpriteRenderer sr = currentVehicle.vehiclePrefab.GetComponent<SpriteRenderer>();
            if (sr != null) {
                displaySprite = sr.sprite;
            }
        }

        // Apply Image of the vehhicle to UI
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
            if (purchasePriceText != null) purchasePriceText.text = "Satın Al: $ " + currentVehicle.price;
            return;
        }

        // --- Update Stat Panels ---
        SetupStatPanel(speedPanel, "Speed", "Speed", currentVehicle.speedDesc, saveData.speedLevel, currentVehicle.maxSpeedLevel);
        SetupStatPanel(turnPanel, "Turn", "Handling", currentVehicle.turnDesc, saveData.turnLevel, currentVehicle.maxTurnLevel);
        SetupStatPanel(healthPanel, "Health", "Chassis", currentVehicle.healthDesc, saveData.healthLevel, currentVehicle.maxHealthLevel);
        SetupStatPanel(armorPanel, "Armor", "Armor", currentVehicle.armorDesc, saveData.armorLevel, currentVehicle.maxArmorLevel);
        SetupStatPanel(capacityPanel, "Capacity", "Storage", currentVehicle.capacityDesc, saveData.capacityLevel, currentVehicle.maxCapacityLevel);
        SetupStatPanel(protectionPanel, "Protection", "Stabilizer", currentVehicle.protectionDesc, saveData.protectionLevel, currentVehicle.maxProtectionLevel);
    }

    void SetupStatPanel(StatDisplay panel, string statKey, string displayName, string desc, int level, int maxLevel) {
        bool isMaxed = level >= maxLevel;

        string valueDisplay = FormatStatValue(statKey, GameManager.Instance.GetStatValueAtLevel(statKey, level));
        if (!isMaxed) {
            valueDisplay += " -> " + FormatStatValue(statKey, GameManager.Instance.GetStatValueAtLevel(statKey, level + 1));
        }

        int cost = GameManager.Instance.GetUpgradeCost(statKey, level);
        panel.Setup(displayName, desc, level, maxLevel, cost, isMaxed, valueDisplay);
    }

    string FormatStatValue(string statKey, float value) {
        switch (statKey) {
            case "Armor":
            case "Protection":
                return Mathf.RoundToInt(value * 100) + "%";
            case "Capacity":
                return Mathf.RoundToInt(value) + " pizza";
            default:
                return value.ToString("0.#");
        }
    }

    public void OnClickUpgrade(string statName) {
        bool success = GameManager.Instance.TryUpgradeStat(statName);
        if (success) UpdateUI();
        else Debug.Log("Insufficient funds or max level reached.");
    }

    public void OnClickPurchaseVehicle() {
        bool success = GameManager.Instance.TryPurchaseVehicle();
        if (success) UpdateUI();
        else Debug.Log("Not enough money to purchase this vehicle.");
    }

    public void OnClickNextVehicle() {
        GameManager.Instance.ChangeVehicle(1);
        UpdateUI();
    }

    public void OnClickPrevVehicle() {
        GameManager.Instance.ChangeVehicle(-1);
        UpdateUI();
    }

    public void OnClickStartJob() {
        var save = GameManager.Instance.GetCurrentVehicleSave();
        if (save == null || !save.isUnlocked) return;
        SceneManager.LoadScene("GameScene");
    }

    public void OnClickMainMenu() {
        SceneManager.LoadScene("MainMenu");
    }
}
