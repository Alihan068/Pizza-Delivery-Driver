using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class GarageManager : MonoBehaviour {
    [Header("UI References")]
    public TextMeshProUGUI totalMoneyText;
    public TextMeshProUGUI currentVehicleNameText;

    [Header("Stat Texts")]
    public TextMeshProUGUI speedLvlText;
    public TextMeshProUGUI turnLvlText;
    public TextMeshProUGUI healthLvlText;
    public TextMeshProUGUI armorLvlText;
    public TextMeshProUGUI capacityLvlText;
    public TextMeshProUGUI protectionLvlText;

    [Header("Cost Texts")]
    public TextMeshProUGUI speedCostText;
    public TextMeshProUGUI turnCostText;
    public TextMeshProUGUI healthCostText;
    public TextMeshProUGUI armorCostText;
    public TextMeshProUGUI capacityCostText;
    public TextMeshProUGUI protectionCostText;

    void Start() {
        UpdateUI();
    }

    void UpdateUI() {
        totalMoneyText.text = "$ " + GameManager.Instance.totalMoney;

        if (GameManager.Instance.currentVehicle != null)
            currentVehicleNameText.text = GameManager.Instance.currentVehicle.vehicleName;

        var saveData = GameManager.Instance.GetCurrentVehicleSave();
        var data = GameManager.Instance.currentVehicle;

        if (saveData == null || data == null) return;

        UpdateSingleStatUI(saveData.speedLevel, data.maxSpeedLevel, speedLvlText, speedCostText);
        UpdateSingleStatUI(saveData.turnLevel, data.maxTurnLevel, turnLvlText, turnCostText);
        UpdateSingleStatUI(saveData.healthLevel, data.maxHealthLevel, healthLvlText, healthCostText);
        UpdateSingleStatUI(saveData.armorLevel, data.maxArmorLevel, armorLvlText, armorCostText);
        UpdateSingleStatUI(saveData.capacityLevel, data.maxCapacityLevel, capacityLvlText, capacityCostText);
        UpdateSingleStatUI(saveData.protectionLevel, data.maxProtectionLevel, protectionLvlText, protectionCostText);
    }

    void UpdateSingleStatUI(int currentLevel, int maxLevel, TextMeshProUGUI lvlText, TextMeshProUGUI costText) {
        lvlText.text = "Lvl " + currentLevel + "/" + maxLevel;

        if (currentLevel >= maxLevel) {
            costText.text = "MAX";
            costText.color = Color.red;
        }
        else {
            int cost = GameManager.Instance.GetUpgradeCost(currentLevel);
            costText.text = "$ " + cost;
            costText.color = Color.white;
        }
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