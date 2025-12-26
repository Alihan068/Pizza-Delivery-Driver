using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GarageManager : MonoBehaviour {
    [Header("UI References")]
    public TextMeshProUGUI totalMoneyText;
    public TextMeshProUGUI currentVehicleNameText;
    public Image chosenVehicleImage;

    [Header("Stat Panels")]
    // Assign the objects with StatDisplay script here in the Inspector
    public StatDisplay speedPanel;
    public StatDisplay turnPanel;
    public StatDisplay healthPanel;
    public StatDisplay armorPanel;
    public StatDisplay capacityPanel;
    public StatDisplay protectionPanel;

    void Start() {
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

        // --- Update Stat Panels ---

        // Speed
        speedPanel.Setup("Speed", currentVehicle.speedDesc, saveData.speedLevel, currentVehicle.maxSpeedLevel,
            GameManager.Instance.GetUpgradeCost(saveData.speedLevel), saveData.speedLevel >= currentVehicle.maxSpeedLevel);

        // Turn (Handling)
        turnPanel.Setup("Handling", currentVehicle.turnDesc, saveData.turnLevel, currentVehicle.maxTurnLevel,
            GameManager.Instance.GetUpgradeCost(saveData.turnLevel), saveData.turnLevel >= currentVehicle.maxTurnLevel);

        // Health (Chassis)
        healthPanel.Setup("Chassis", currentVehicle.healthDesc, saveData.healthLevel, currentVehicle.maxHealthLevel,
            GameManager.Instance.GetUpgradeCost(saveData.healthLevel), saveData.healthLevel >= currentVehicle.maxHealthLevel);

        // Armor
        armorPanel.Setup("Armor", currentVehicle.armorDesc, saveData.armorLevel, currentVehicle.maxArmorLevel,
            GameManager.Instance.GetUpgradeCost(saveData.armorLevel), saveData.armorLevel >= currentVehicle.maxArmorLevel);

        // Capacity
        capacityPanel.Setup("Storage", currentVehicle.capacityDesc, saveData.capacityLevel, currentVehicle.maxCapacityLevel,
            GameManager.Instance.GetUpgradeCost(saveData.capacityLevel), saveData.capacityLevel >= currentVehicle.maxCapacityLevel);

        // Protection (Stabilizer)
        protectionPanel.Setup("Stabilizer", currentVehicle.protectionDesc, saveData.protectionLevel, currentVehicle.maxProtectionLevel,
            GameManager.Instance.GetUpgradeCost(saveData.protectionLevel), saveData.protectionLevel >= currentVehicle.maxProtectionLevel);
    }

    public void OnClickUpgrade(string statName) {
        bool success = GameManager.Instance.TryUpgradeStat(statName);
        if (success) UpdateUI();
        else Debug.Log("Insufficient funds or max level reached.");
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
        SceneManager.LoadScene("GameScene");
    }

    public void OnClickMainMenu() {
        SceneManager.LoadScene("MainMenu");
    }
}