using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class GameManager : MonoBehaviour {
    public static GameManager Instance;

    [Header("Economy")]
    public int totalMoney = 0;

    [Header("Vehicle Database")]
    public VehicleData[] allVehicles;
    public VehicleData currentVehicle;

    [Header("Save Data")]
    public List<VehicleSaveData> vehicleSaveList = new List<VehicleSaveData>();

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeVehicles();
        }
        else {
            Destroy(gameObject);
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
        totalMoney += amount;
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

    public int GetUpgradeCost(int currentLevel) {
        return 100 + (currentLevel * 50);
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
        }

        if (currentLevel >= maxLevelAllowed) return false;

        int cost = GetUpgradeCost(currentLevel);

        if (totalMoney >= cost) {
            totalMoney -= cost;

            switch (statName) {
                case "Speed": save.speedLevel++; break;
                case "Turn": save.turnLevel++; break;
                case "Health": save.healthLevel++; break;
                case "Armor": save.armorLevel++; break;
                case "Capacity": save.capacityLevel++; break;
                case "Protection": save.protectionLevel++; break;
            }
            return true;
        }
        return false;
    }

    public void ChangeVehicle(int direction) {
        int currentIndex = System.Array.IndexOf(allVehicles, currentVehicle);
        int newIndex = (currentIndex + direction);

        if (newIndex < 0) newIndex = allVehicles.Length - 1;
        if (newIndex >= allVehicles.Length) newIndex = 0;

        currentVehicle = allVehicles[newIndex];
    }
}