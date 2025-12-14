using UnityEngine;

[CreateAssetMenu(fileName = "NewVehicleData", menuName = "PizzaGame/Vehicle Data")]
public class VehicleData : ScriptableObject {
    [Header("Identity")]
    public string vehicleName;
    public int price;

    [Header("Engine (Speed)")]
    public float minSpeed = 10f;
    public float maxSpeed = 25f;

    [Header("Handling (Turn)")]
    public float minTurn = 150f;
    public float maxTurn = 300f;

    [Header("Chassis (Health)")]
    public float minHealth = 100f;
    public float maxHealth = 300f;

    [Header("Armor (Damage Reduction %)")]
    [Range(0, 1)] public float minArmor = 0f;   
    [Range(0, 1)] public float maxArmor = 0.5f; 

    [Header("Storage (Capacity)")]
    public int minCapacity = 2;
    public int maxCapacity = 10;

    [Header("Stabilizer (Pizza Protection %)")]
    // Çarpışma anında pizzanın düşmeme şansı
    [Range(0, 1)] public float minProtection = 0f;   
    [Range(0, 1)] public float maxProtection = 0.8f; 
}