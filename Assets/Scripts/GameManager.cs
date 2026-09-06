using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class GameManager : MonoBehaviour {
    public static GameManager Instance;

    const string LastSlotKey = "save.lastSlot";
    const string LegacySaveFileName = "save.json";

    [Header("Configuration")]
    [SerializeField] GameConfig config;

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

    [Header("Built-in Content")]
    [Tooltip("Vehicles that ship with the build. Externally supplied vehicles are added through the content registry, not here.")]
    public VehicleData[] allVehicles;

    [Tooltip("Maps that ship with the build. Externally supplied maps are added through the content registry, not here.")]
    public MapData[] allMaps;

    [Header("Career State")]
    public VehicleData currentVehicle;
    public MapData currentMap;
    public List<VehicleSaveData> vehicleSaveList = new List<VehicleSaveData>();

    /// <summary>Every vehicle and map the game knows about, from all content sources.</summary>
    public ContentRegistry Content { get; private set; }

    /// <summary>Profile slot currently being played and written to.</summary>
    public int ActiveSlot { get; private set; }

    /// <summary>Reads and writes profile slots on disk.</summary>
    public SaveSlotService Saves { get; private set; }

    /// <summary>
    /// True when the loaded profile was written while a session was still running, meaning the game
    /// was killed mid-shift. The session must be settled before play resumes.
    /// </summary>
    public bool HasInterruptedShift { get; private set; }

    /// <summary>Settings shared by every profile: slot layout, scene names, new career defaults.</summary>
    public GameConfig Config => config;

    private void Awake() {
        if (Instance != null) {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildContentRegistry();
        Saves = new SaveSlotService(config, ResolveVehicleIdFromName);
        Saves.AdoptLegacySave(LegacySaveFileName, 0);

        LoadCareer(PlayerPrefs.GetInt(LastSlotKey, 0));
    }

    // ---------------------------------------------------------------- content

    void BuildContentRegistry() {
        Content = new ContentRegistry();
        Content.AddProvider(new BuiltInContentProvider(allVehicles, allMaps));
        Content.Rebuild();
        Content.LogConflicts();
    }

    // Version 0 saves keyed vehicles by display name. Migration needs that mapping once, and it
    // only works while the old names still match installed content.
    string ResolveVehicleIdFromName(string displayName) {
        if (string.IsNullOrEmpty(displayName) || Content == null) return string.Empty;
        foreach (var vehicle in Content.Vehicles) {
            if (vehicle.vehicleName == displayName) return vehicle.vehicleId;
        }
        return string.Empty;
    }

    // ----------------------------------------------------------------- career

    /// <summary>
    /// Loads a profile into the running game. An empty or damaged slot leaves a fresh career in
    /// memory rather than failing, so the player always lands somewhere playable.
    /// </summary>
    /// <param name="slotIndex">Slot to load.</param>
    /// <returns>How reading the slot turned out.</returns>
    public SaveLoadStatus LoadCareer(int slotIndex) {
        ActiveSlot = Mathf.Clamp(slotIndex, 0, Saves.SlotCount - 1);
        PlayerPrefs.SetInt(LastSlotKey, ActiveSlot);
        PlayerPrefs.Save();

        var data = Saves.Load(ActiveSlot, out var status);
        if (data == null) StartFreshCareer();
        else ApplySaveData(data);

        InitializeVehicles();
        return status;
    }

    /// <summary>Wipes a slot and begins a brand new career in it.</summary>
    /// <param name="slotIndex">Slot the new career occupies.</param>
    public void StartNewCareer(int slotIndex) {
        ActiveSlot = Mathf.Clamp(slotIndex, 0, Saves.SlotCount - 1);
        PlayerPrefs.SetInt(LastSlotKey, ActiveSlot);
        PlayerPrefs.Save();

        Saves.Delete(ActiveSlot);
        StartFreshCareer();
        InitializeVehicles();
        SaveGame();
    }

    void StartFreshCareer() {
        totalMoney = config != null ? config.startingMoney : totalMoney;
        vehicleSaveList = new List<VehicleSaveData>();
        currentVehicle = Content.ResolveStartingVehicle(config != null ? config.startingVehicle : null);
        currentMap = Content.ResolveStartingMap(config != null ? config.startingMap : null);
        HasInterruptedShift = false;
    }

    void ApplySaveData(GameSaveData data) {
        totalMoney = data.totalMoney;
        vehicleSaveList = data.vehicleSaveList ?? new List<VehicleSaveData>();
        HasInterruptedShift = data.shiftInProgress;

        currentVehicle = Content.GetVehicle(data.currentVehicleId);
        if (currentVehicle == null && !string.IsNullOrEmpty(data.currentVehicleId)) {
            Debug.LogWarning("[Save] This profile uses vehicle '" + data.currentVehicleId +
                             "', which is not installed. Falling back to the starting vehicle.");
        }
        if (currentVehicle == null) currentVehicle = Content.ResolveStartingVehicle(config != null ? config.startingVehicle : null);

        currentMap = Content.GetMap(data.currentMapId);
        if (currentMap == null) currentMap = Content.ResolveStartingMap(config != null ? config.startingMap : null);
    }

    GameSaveData BuildSaveData() {
        return new GameSaveData {
            saveVersion = SaveMigration.CurrentVersion,
            totalMoney = totalMoney,
            currentVehicleId = currentVehicle != null ? currentVehicle.vehicleId : string.Empty,
            currentMapId = currentMap != null ? currentMap.mapId : string.Empty,
            vehicleSaveList = vehicleSaveList,
            shiftInProgress = HasInterruptedShift
        };
    }

    /// <summary>Writes the current career to its slot.</summary>
    /// <returns>True when the write completed.</returns>
    public bool SaveGame() {
        return Saves.Save(ActiveSlot, BuildSaveData());
    }

    /// <summary>
    /// Marks that a session has begun, so a profile loaded with this flag still set can be
    /// recognised as a shift that was killed rather than finished.
    /// </summary>
    public void MarkShiftStarted() {
        HasInterruptedShift = true;
        SaveGame();
    }

    void InitializeVehicles() {
        if (Content.Vehicles.Count == 0) return;
        if (currentVehicle == null) currentVehicle = Content.Vehicles[0];

        var starter = Content.Vehicles[0];
        foreach (var vehicle in Content.Vehicles) {
            if (vehicleSaveList.Exists(x => x.vehicleId == vehicle.vehicleId)) continue;
            vehicleSaveList.Add(new VehicleSaveData(vehicle.vehicleId, vehicle == starter));
        }
    }

    /// <summary>
    /// Wipes all saved progress in the active slot and returns the player to a brand new career.
    /// </summary>
    /// <remarks>
    /// Destructive and irreversible, so the caller confirms with the player first (see
    /// <see cref="SettingsPanel"/>). Player settings such as music volume live in
    /// <see cref="GameSettings"/> and are deliberately left untouched.
    /// </remarks>
    public void ResetProgress() {
        StartNewCareer(ActiveSlot);
    }

    /// <summary>Upgrade and unlock state of the vehicle currently being driven.</summary>
    /// <returns>The record, or null when no vehicle is selected.</returns>
    public VehicleSaveData GetCurrentVehicleSave() {
        if (currentVehicle == null) return null;
        return vehicleSaveList.FirstOrDefault(x => x.vehicleId == currentVehicle.vehicleId);
    }

    /// <summary>Adds money to the bank, never letting the balance go negative.</summary>
    /// <param name="amount">Amount to add; may be negative.</param>
    public void AddMoneyToBank(int amount) {
        totalMoney = Mathf.Max(0, totalMoney + amount);
    }

    // ------------------------------------------------------------------ stats

    float CalculateStat(float baseVal, float step, int currentLevel) {
        return baseVal + (step * currentLevel);
    }

    /// <summary>Current value of a stat on the active vehicle, including purchased levels.</summary>
    /// <param name="stat">Which stat to read.</param>
    /// <returns>The effective value, unclamped except where the stat is a fraction.</returns>
    public float GetStatValue(VehicleStatId stat) {
        if (currentVehicle == null) return 0f;
        var save = GetCurrentVehicleSave();
        int level = save != null ? save.GetLevel(stat) : 0;
        return GetStatValueAtLevel(stat, level);
    }

    /// <summary>Value a stat would have at a given level, used to preview an upgrade.</summary>
    /// <param name="stat">Which stat to read.</param>
    /// <param name="level">Hypothetical purchased level.</param>
    /// <returns>The value at that level; fractional stats are clamped to one.</returns>
    public float GetStatValueAtLevel(VehicleStatId stat, int level) {
        if (currentVehicle == null) return 0f;
        float value = CalculateStat(currentVehicle.GetBaseValue(stat), currentVehicle.GetStep(stat), level);
        bool isFraction = stat == VehicleStatId.Armor || stat == VehicleStatId.Protection;
        return isFraction ? Mathf.Clamp01(value) : value;
    }

    /// <summary>Top speed of the active vehicle.</summary>
    /// <returns>Effective speed.</returns>
    public float GetSpeed() => GetStatValue(VehicleStatId.Speed);

    /// <summary>Turn rate of the active vehicle.</summary>
    /// <returns>Effective turn rate in degrees per second.</returns>
    public float GetTurn() => GetStatValue(VehicleStatId.Turn);

    /// <summary>Maximum health of the active vehicle.</summary>
    /// <returns>Effective maximum health.</returns>
    public float GetHealth() => GetStatValue(VehicleStatId.Health);

    /// <summary>Damage reduction of the active vehicle.</summary>
    /// <returns>A fraction from zero to one.</returns>
    public float GetArmor() => GetStatValue(VehicleStatId.Armor);

    /// <summary>Pizza carrying capacity of the active vehicle.</summary>
    /// <returns>Whole pizzas the player can hold.</returns>
    public int GetCapacity() => Mathf.RoundToInt(GetStatValue(VehicleStatId.Capacity));

    /// <summary>Chance the active vehicle keeps a pizza on impact.</summary>
    /// <returns>A fraction from zero to one.</returns>
    public float GetProtectionChance() => GetStatValue(VehicleStatId.Protection);

    // --------------------------------------------------------------- upgrades

    /// <summary>Price of buying the next level of a stat.</summary>
    /// <param name="stat">Which stat to price.</param>
    /// <param name="currentLevel">Levels already purchased.</param>
    /// <returns>The cost in currency.</returns>
    public int GetUpgradeCost(VehicleStatId stat, int currentLevel) {
        float mult = currentVehicle != null ? currentVehicle.GetCostMultiplier(stat) : 1f;
        return Mathf.RoundToInt((upgradeCostBase + upgradeCostStep * currentLevel) * mult);
    }

    /// <summary>
    /// Buys one level of a stat when the player can afford it and the ceiling has not been reached.
    /// </summary>
    /// <param name="stat">Which stat to upgrade.</param>
    /// <returns>True when a level was bought and money was spent.</returns>
    public bool TryUpgradeStat(VehicleStatId stat) {
        var save = GetCurrentVehicleSave();
        if (save == null || currentVehicle == null) return false;

        int currentLevel = save.GetLevel(stat);
        if (currentLevel >= currentVehicle.GetMaxLevel(stat)) return false;

        int cost = GetUpgradeCost(stat, currentLevel);
        if (totalMoney < cost) return false;

        totalMoney -= cost;
        save.moneySpent += cost;
        save.AddLevel(stat);
        SaveGame();
        return true;
    }

    /// <summary>Buys the active vehicle when it is still locked and affordable.</summary>
    /// <returns>True when the vehicle was unlocked and money was spent.</returns>
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

    /// <summary>Steps to the next or previous vehicle in the registry, wrapping at both ends.</summary>
    /// <param name="direction">Positive to advance, negative to go back.</param>
    public void ChangeVehicle(int direction) {
        var vehicles = Content.Vehicles;
        if (vehicles.Count == 0) return;

        int currentIndex = 0;
        for (int i = 0; i < vehicles.Count; i++) {
            if (vehicles[i] == currentVehicle) { currentIndex = i; break; }
        }

        int newIndex = currentIndex + direction;
        if (newIndex < 0) newIndex = vehicles.Count - 1;
        if (newIndex >= vehicles.Count) newIndex = 0;

        currentVehicle = vehicles[newIndex];
        SaveGame();
    }

    /// <summary>Selects the map the next session will be played on.</summary>
    /// <param name="map">Map to select. Ignored when null.</param>
    public void SelectMap(MapData map) {
        if (map == null) return;
        currentMap = map;
        SaveGame();
    }

    // --------------------------------------------------------------- economy

    /// <summary>What the active vehicle is worth: its price plus everything spent upgrading it.</summary>
    /// <returns>The value used to scale repair cost.</returns>
    public int GetVehicleValue() {
        var save = GetCurrentVehicleSave();
        int spent = save != null ? save.moneySpent : 0;
        int price = currentVehicle != null ? currentVehicle.price : 0;
        return spent + price;
    }

    /// <summary>
    /// Repair bill for the damage taken in a session. Linear in health lost, plus a share of the
    /// vehicle's value, so an expensive vehicle costs more to run.
    /// </summary>
    /// <param name="currentHealth">Health remaining at the end of the session.</param>
    /// <param name="maxHealth">The vehicle's maximum health.</param>
    /// <param name="died">True when the vehicle was wrecked, which bills the full health bar.</param>
    /// <returns>The repair cost before the bank safety clamp.</returns>
    public int CalculateRepairCost(float currentHealth, float maxHealth, bool died) {
        if (maxHealth <= 0f) return 0;
        float hpLost = died ? maxHealth : Mathf.Clamp(maxHealth - currentHealth, 0f, maxHealth);
        float damageRatio = hpLost / maxHealth;
        return Mathf.RoundToInt(hpLost * repairCostPerHP + GetVehicleValue() * repairValueRate * damageRatio);
    }

    /// <summary>
    /// Closes a session: banks the kept earnings, charges repairs, and writes the result to disk.
    /// </summary>
    /// <param name="sessionEarnings">Money earned during the session, not yet banked.</param>
    /// <param name="currentHealth">Health remaining at the end.</param>
    /// <param name="maxHealth">The vehicle's maximum health.</param>
    /// <param name="reason">How the session ended, which decides how much of the earnings survive.</param>
    /// <returns>A breakdown of the settlement for the result screen.</returns>
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

        // The shift is over however it ended, so the interrupted-shift flag is cleared here rather
        // than on any one ending path.
        HasInterruptedShift = false;
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
