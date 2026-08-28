using System.Collections.Generic;

[System.Serializable]
public class GameSaveData {
    public int totalMoney;
    public string currentVehicleName;
    public List<VehicleSaveData> vehicleSaveList;
}
