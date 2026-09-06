using UnityEngine;

/// <summary>
/// Project-wide settings that are not per-vehicle or per-map: save file layout, scene names and
/// the state a brand new career starts from.
/// </summary>
/// <remarks>
/// This exists so that none of these values are written into code. Scene names in particular used
/// to be string literals scattered across the managers, which meant renaming a scene broke the game
/// silently at runtime rather than at compile time.
/// <para>
/// Nothing here assumes a fixed count. <see cref="saveSlotCount"/> is data, so adding a fourth
/// profile is an Inspector change, not a code change.
/// </para>
/// </remarks>
[CreateAssetMenu(fileName = "GameConfig", menuName = "PizzaGame/Game Config")]
public class GameConfig : ScriptableObject {

    [Header("Save Files")]
    [Tooltip("How many career profiles the player can keep. Changing this is safe: existing profiles keep their index.")]
    [Min(1)] public int saveSlotCount = 3;

    [Tooltip("File name for a profile. {0} is the slot index.")]
    public string saveFilePattern = "save_{0}.json";

    [Tooltip("Suffix appended to a profile file to make its backup copy.")]
    public string backupSuffix = ".bak";

    [Tooltip("Suffix used when a profile fails to load and is set aside instead of being destroyed. {0} is a timestamp.")]
    public string corruptSuffix = ".corrupt_{0}.json";

    [Header("Scenes")]
    [Tooltip("Scene name of the main menu. Must be present in Build Settings.")]
    public string mainMenuScene = "MainMenu";

    [Tooltip("Scene name of the garage hub. Must be present in Build Settings.")]
    public string garageScene = "GarageScene";

    [Header("New Career")]
    [Tooltip("Money a brand new career starts with.")]
    public int startingMoney = 100;

    [Tooltip("Vehicle a brand new career starts with, already unlocked. Leave empty to use the first registered vehicle.")]
    public VehicleData startingVehicle;

    [Tooltip("Map selected by default before the player has unlocked anything else.")]
    public MapData startingMap;

    /// <summary>
    /// Builds the file name for a profile slot.
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <returns>The file name, without a directory.</returns>
    public string GetSaveFileName(int slotIndex) {
        return string.Format(saveFilePattern, slotIndex);
    }
}
