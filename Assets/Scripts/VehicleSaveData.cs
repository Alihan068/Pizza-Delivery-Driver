using UnityEngine;

[System.Serializable]
public class VehicleSaveData {
    public string vehicleName;
    public bool isUnlocked;

    public int speedLevel;
    public int turnLevel;
    public int healthLevel;
    public int armorLevel;
    public int capacityLevel;
    public int protectionLevel;

    public VehicleSaveData(string name, bool unlocked) {
        vehicleName = name;
        isUnlocked = unlocked;
        speedLevel = 0;
        turnLevel = 0;
        healthLevel = 0;
        armorLevel = 0;
        capacityLevel = 0;
        protectionLevel = 0;
    }
}