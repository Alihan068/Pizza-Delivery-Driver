using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class GameManager : MonoBehaviour {
    public static GameManager Instance;

    const string SaveFileName = "save.json";

    [Header("Economy")]
    public int totalMoney = 100;

    [Header("Session Settlement")]
    [SerializeField] float repairCostPerHP = 0.7f;
    [SerializeField] float repairValueRate = 0.03f;
    [SerializeField] float deathEarningsKeep = 0.5f;
    [SerializeField] float repairBankSafetyRate = 0.5f;

    [Header("Upgrade Cost")]
    [SerializeField] float upgradeCostBase = 460f;
    [SerializeField] float upgradeCostStep = 380f;

    [Header("Vehicle Database")]
    public VehicleData[] allVehicles;
    public VehicleData currentVehicle;

    [Header("Save Data")]
    public List<VehicleSaveData> vehicleSaveList = new List<VehicleSaveData>();

    // The Inspector value of totalMoney is the starting balance. It is captured before LoadGame
    // overwrites it so ResetProgress can restore it without duplicating the number in a second field.
    int defaultMoney;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            defaultMoney = totalMoney;
            LoadGame();
            InitializeVehicles();
        }
        else {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Wipes all saved progress and returns the player to a brand new game: starting money, no
    /// upgrades, only the first vehicle unlocked.
    /// </summary>
    /// <remarks>
    /// Destructive and irreversible, so the caller is responsible for confirming with the player
    /// first (see <see cref="SettingsPanel"/>). Player settings such as music volume live in
    /// <see cref="GameSettings"/> and are deliberately left untouched.
    /// <para>
    /// The save file is deleted and then written again from the fresh state, so what is on disk
    /// always matches what is in memory even if the game is killed right afterwards.
    /// </para>
    /// </remarks>
    public void ResetProgress() {
        try {
            string path = GetSavePath();
            if (File.Exists(path)) File.Delete(path);
        }
        catch (System.Exception e) {
            Debug.LogWarning("Failed to delete the save file during reset: " + e.Message);
        }

        totalMoney = defaultMoney;
        vehicleSaveList = new List<VehicleSaveData>();
        currentVehicle = null;

        InitializeVehicles();
        SaveGame();
    }

    string GetSavePath() {
        return Path.Combine(Application.persistentDataPath, SaveFileName);
    }

    void LoadGame() {
        string path = GetSavePath();
        if (!File.Exists(path)) return;

        try {
            string json = File.ReadAllText(path);
            GameSaveData data = JsonUtility.FromJson<GameSaveData>(json);
            if (data == null) return;

            totalMoney = data.totalMoney;
            vehicleSaveList = data.vehicleSaveList ?? new List<VehicleSaveData>();

            if (!string.IsNullOrEmpty(data.currentVehicleName)) {
                currentVehicle = System.Array.Find(allVehicles, v => v.vehicleName == data.currentVehicleName);
            }
        }
        catch (System.Exception e) {
            Debug.LogWarning("Save file could not be loaded, starting fresh: " + e.Message);
        }
    }

    void SaveGame() {
        try {
            GameSaveData data = new GameSaveData {
                totalMoney = totalMoney,
                currentVehicleName = currentVehicle != null ? currentVehicle.vehicleName : "",
                vehicleSaveList = vehicleSaveList
            };

            File.WriteAllText(GetSavePath(), JsonUtility.ToJson(data, true));
        }
        catch (System.Exception e) {
            Debug.LogWarning("Failed to save game: " + e.Message);
        }
    }

    void InitializeVehicles() {
        if (allVehicles.Length == 0) return;
        if (currentVehicle == null) currentVehicle = allVehicles[0];

        foreach (var vehicle in allVehicles) {
            if (!vehicleSaveList.Exists(x => x.vehicleName == vehicle.vehicleName)) {
                bool isDefaultUnlocked = (vehicle == allVehicles[0]);
                vehicleSaveList.Add(new VehicleSaveData(vehicle.vehicleName, isDefaultUnlocked));
            }
        }
    }

    public VehicleSaveData GetCurrentVehicleSave() {
        if (currentVehicle == null) return null;
        return vehicleSaveList.FirstOrDefault(x => x.vehicleName == currentVehicle.vehicleName);
    }

    public void AddMoneyToBank(int amount) {
        totalMoney = Mathf.Max(0, totalMoney + amount);
        Debug.Log("Added $" + amount + " to bank. Total Money: $" + totalMoney);
    }

    private float CalculateStat(float baseVal, float step, int currentLevel) {
        return baseVal + (step * currentLevel);
    }

    public float GetSpeed() {
        var save = GetCurrentVehicleSave();
        if (save == null) return currentVehicle.baseSpeed;
        return CalculateStat(currentVehicle.baseSpeed, currentVehicle.speedStep, save.speedLevel);
    }

    public float GetTurn() {
        var save = GetCurrentVehicleSave();
        if (save == null) return currentVehicle.baseTurn;
        return CalculateStat(currentVehicle.baseTurn, currentVehicle.turnStep, save.turnLevel);
    }

    public float GetHealth() {
        var save = GetCurrentVehicleSave();
        if (save == null) return currentVehicle.baseHealth;
        return CalculateStat(currentVehicle.baseHealth, currentVehicle.healthStep, save.healthLevel);
    }

    public float GetArmor() {
        var save = GetCurrentVehicleSave();
        if (save == null) return currentVehicle.baseArmor;
        return Mathf.Clamp01(CalculateStat(currentVehicle.baseArmor, currentVehicle.armorStep, save.armorLevel));
    }

    public int GetCapacity() {
        var save = GetCurrentVehicleSave();
        if (save == null) return currentVehicle.baseCapacity;
        float val = CalculateStat(currentVehicle.baseCapacity, currentVehicle.capacityStep, save.capacityLevel);
        return Mathf.RoundToInt(val);
    }

    public float GetProtectionChance() {
        var save = GetCurrentVehicleSave();
        if (save == null) return currentVehicle.baseProtection;
        return Mathf.Clamp01(CalculateStat(currentVehicle.baseProtection, currentVehicle.protectionStep, save.protectionLevel));
    }

    public float GetStatValueAtLevel(string statName, int level) {
        if (currentVehicle == null) return 0f;

        switch (statName) {
            case "Speed": return CalculateStat(currentVehicle.baseSpeed, currentVehicle.speedStep, level);
            case "Turn": return CalculateStat(currentVehicle.baseTurn, currentVehicle.turnStep, level);
            case "Health": return CalculateStat(currentVehicle.baseHealth, currentVehicle.healthStep, level);
            case "Armor": return Mathf.Clamp01(CalculateStat(currentVehicle.baseArmor, currentVehicle.armorStep, level));
            case "Capacity": return CalculateStat(currentVehicle.baseCapacity, currentVehicle.capacityStep, level);
            case "Protection": return Mathf.Clamp01(CalculateStat(currentVehicle.baseProtection, currentVehicle.protectionStep, level));
            default: return 0f;
        }
    }

    float GetCostMultiplier(string statName) {
        if (currentVehicle == null) return 1f;

        switch (statName) {
            case "Speed": return currentVehicle.speedCostMult;
            case "Turn": return currentVehicle.turnCostMult;
            case "Health": return currentVehicle.healthCostMult;
            case "Armor": return currentVehicle.armorCostMult;
            case "Capacity": return currentVehicle.capacityCostMult;
            case "Protection": return currentVehicle.protectionCostMult;
            default: return 1f;
        }
    }

    public int GetUpgradeCost(string statName, int currentLevel) {
        float mult = GetCostMultiplier(statName);
        return Mathf.RoundToInt((upgradeCostBase + upgradeCostStep * currentLevel) * mult);
    }

    public bool TryUpgradeStat(string statName) {
        var save = GetCurrentVehicleSave();
        var data = currentVehicle;
        if (save == null || data == null) return false;

        int currentLevel = 0;
        int maxLevelAllowed = 10;

        switch (statName) {
            case "Speed":
                currentLevel = save.speedLevel;
                maxLevelAllowed = data.maxSpeedLevel;
                break;
            case "Turn":
                currentLevel = save.turnLevel;
                maxLevelAllowed = data.maxTurnLevel;
                break;
            case "Health":
                currentLevel = save.healthLevel;
                maxLevelAllowed = data.maxHealthLevel;
                break;
            case "Armor":
                currentLevel = save.armorLevel;
                maxLevelAllowed = data.maxArmorLevel;
                break;
            case "Capacity":
                currentLevel = save.capacityLevel;
                maxLevelAllowed = data.maxCapacityLevel;
                break;
            case "Protection":
                currentLevel = save.protectionLevel;
                maxLevelAllowed = data.maxProtectionLevel;
                break;
            default:
                return false;
        }

        if (currentLevel >= maxLevelAllowed) return false;

        int cost = GetUpgradeCost(statName, currentLevel);

        if (totalMoney >= cost) {
            totalMoney -= cost;
            save.moneySpent += cost;

            switch (statName) {
                case "Speed": save.speedLevel++; break;
                case "Turn": save.turnLevel++; break;
                case "Health": save.healthLevel++; break;
                case "Armor": save.armorLevel++; break;
                case "Capacity": save.capacityLevel++; break;
                case "Protection": save.protectionLevel++; break;
            }
            SaveGame();
            return true;
        }
        return false;
    }

    public bool TryPurchaseVehicle() {
        var save = GetCurrentVehicleSave();
        if (save == null || currentVehicle == null) return false;
        if (save.isUnlocked) return false;
        if (totalMoney < currentVehicle.price) return false;

        totalMoney -= currentVehicle.price;
        save.isUnlocked = true;
        SaveGame();
        return true;
    }

    public void ChangeVehicle(int direction) {
        int currentIndex = System.Array.IndexOf(allVehicles, currentVehicle);
        int newIndex = (currentIndex + direction);

        if (newIndex < 0) newIndex = allVehicles.Length - 1;
        if (newIndex >= allVehicles.Length) newIndex = 0;

        currentVehicle = allVehicles[newIndex];
        SaveGame();
    }

    public int GetVehicleValue() {
        var save = GetCurrentVehicleSave();
        int spent = save != null ? save.moneySpent : 0;
        int price = currentVehicle != null ? currentVehicle.price : 0;
        return spent + price;
    }

    public int CalculateRepairCost(float currentHealth, float maxHealth, bool died) {
        if (maxHealth <= 0f) return 0;
        float hpLost = died ? maxHealth : Mathf.Clamp(maxHealth - currentHealth, 0f, maxHealth);
        float damageRatio = hpLost / maxHealth;
        return Mathf.RoundToInt(hpLost * repairCostPerHP + GetVehicleValue() * repairValueRate * damageRatio);
    }

    public SessionResult SettleSession(int sessionEarnings, float currentHealth, float maxHealth, EndReason reason) {
        int bankBefore = totalMoney;

        int kept;
        switch (reason) {
            case EndReason.Wrecked: kept = Mathf.FloorToInt(sessionEarnings * deathEarningsKeep); break;
            case EndReason.Abandoned: kept = 0; break;
            default: kept = sessionEarnings; break;
        }
        kept = Mathf.Max(0, kept);

        bool died = (reason == EndReason.Wrecked);
        int repairRaw = CalculateRepairCost(currentHealth, maxHealth, died);

        int maxCharge = kept + Mathf.FloorToInt(bankBefore * repairBankSafetyRate);
        int repair = Mathf.Clamp(repairRaw, 0, maxCharge);

        totalMoney = Mathf.Max(0, bankBefore + kept - repair);
        SaveGame();

        return new SessionResult {
            reason = reason,
            grossEarnings = sessionEarnings,
            keptEarnings = kept,
            repairCost = repair,
            repairBeforeClamp = repairRaw,
            bankBefore = bankBefore,
            bankAfter = totalMoney
        };
    }
}
