using System.Collections.Generic;

/// <summary>
/// Everything one career profile persists.
/// </summary>
/// <remarks>
/// Serialized with <c>JsonUtility</c>, which fills any field missing from the file with its C#
/// default and reports nothing. That silence is why <see cref="saveVersion"/> exists: without it
/// an old save and a save whose meaning has changed look identical.
/// <para>
/// The legacy name fields are only read, never written. They exist so a version 0 file — which
/// keyed everything by display name — can still be migrated.
/// </para>
/// </remarks>
[System.Serializable]
public class GameSaveData {

    /// <summary>Schema version this file was written with. Version 0 means "written before versioning existed".</summary>
    public int saveVersion;

    /// <summary>Banked money.</summary>
    public int totalMoney;

    /// <summary>Permanent id of the vehicle the player is currently driving.</summary>
    public string currentVehicleId;

    /// <summary>Permanent id of the map the player last selected.</summary>
    public string currentMapId;

    /// <summary>Per-vehicle upgrade levels and unlock state.</summary>
    public List<VehicleSaveData> vehicleSaveList;

    /// <summary>
    /// True while a session is running. Set when a shift starts and cleared at settlement, so a
    /// profile loaded with this still set can be recognised as an interrupted shift.
    /// </summary>
    public bool shiftInProgress;

    /// <summary>Legacy version 0 field: the display name of the current vehicle. Migration input only.</summary>
    public string currentVehicleName;
}
