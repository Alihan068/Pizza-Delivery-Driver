using UnityEngine;

public class GameManager : MonoBehaviour {
    public static GameManager Instance;

    [Header("Economy")]
    public int totalMoney = 0;

    [Header("Upgrade Levels (0 - 10)")]
    public int speedLevel = 0;
    public int turnLevel = 0;
    public int healthLevel = 0;
    public int armorLevel = 0;      
    public int capacityLevel = 0;
    public int protectionLevel = 0; 

    [Header("Current Vehicle")]
    public VehicleData currentVehicle;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else {
            Destroy(gameObject);
        }
    }

    public void AddMoneyToBank(int amount) {
        totalMoney += amount;
    }

    private float CalculateStat(float min, float max, int currentLevel) {
        if (currentVehicle == null) return min;
        float step = (max - min) / 10f;
        return min + (step * currentLevel);
    }

    public float GetSpeed() {
        return CalculateStat(currentVehicle.minSpeed, currentVehicle.maxSpeed, speedLevel);
    }

    public float GetTurn() {
        return CalculateStat(currentVehicle.minTurn, currentVehicle.maxTurn, turnLevel);
    }

    public float GetHealth() {
        return CalculateStat(currentVehicle.minHealth, currentVehicle.maxHealth, healthLevel);
    }

    public float GetArmor() {
        return CalculateStat(currentVehicle.minArmor, currentVehicle.maxArmor, armorLevel);
    }

    public int GetCapacity() {
        if (currentVehicle == null) return 2;
        float val = CalculateStat(currentVehicle.minCapacity, currentVehicle.maxCapacity, capacityLevel);
        return Mathf.RoundToInt(val);
    }

    public float GetProtectionChance() {
        return CalculateStat(currentVehicle.minProtection, currentVehicle.maxProtection, protectionLevel);
    }
}