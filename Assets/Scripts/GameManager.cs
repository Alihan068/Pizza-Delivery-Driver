using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class GameManager : MonoBehaviour {
    public static GameManager Instance;

    const string LastSlotKey = "save.lastSlot";
    const string LegacySaveFileName = "save.json";

    [Header("Configuration")]
    [SerializeField] GameConfig config;
    [SerializeField] CareerData careerData;

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

    /// <summary>Shift modifiers that ship with the build; selection is temporary and is not saved.</summary>
    [Tooltip("Shift modifiers that ship with the build. Selection is temporary and is not saved to a career.")]
    public ShiftModifierData[] allModifiers;

    [Header("Career State")]
    public VehicleData currentVehicle;
    public MapData currentMap;
    ShiftModifierData currentModifier;
    public List<VehicleSaveData> vehicleSaveList = new List<VehicleSaveData>();
    public DriverSaveData driverStats = new DriverSaveData();

    [Header("Career Progress")]
    public int totalReputation;
    public int highestRankAchieved = 1;
    public int highestUnlockedRegionTier = 1;
    public int currentDay = 1;
    public int shiftsCompletedToday;
    public bool everPaidRent;
    public int lastRentChargeRank = 1;
    public bool reachedEnding;

    /// <summary>Every vehicle and map the game knows about, from all content sources.</summary>
    public ContentRegistry Content { get; private set; }

    /// <summary>Profile slot currently being played and written to.</summary>
    public int ActiveSlot { get; private set; }

    /// <summary>Reads and writes profile slots on disk.</summary>
    public SaveSlotService Saves { get; private set; }

    /// <summary>
    /// Reputation/rank/rent calculations for the career layer. Null when no <see cref="CareerData"/>
    /// is assigned, in which case the career layer is inert: reputation never changes and rent is
    /// never charged, rather than throwing.
    /// </summary>
    public CareerManager Career { get; private set; }

    /// <summary>Authored career tuning used by the active profile.</summary>
    public CareerData CareerData => careerData;

    /// <summary>Modifier selected for the next shift, or null for an unmodified shift.</summary>
    public ShiftModifierData CurrentModifier => currentModifier;

    /// <summary>Rank the active career has reached, from <see cref="totalReputation"/>.</summary>
    public int CurrentRank => Career != null ? Career.ComputeRank(totalReputation, out _) : 1;

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
        if (careerData != null) Career = new CareerManager(careerData);

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

        // Runs after InitializeVehicles because the repair bill needs the vehicle's stats.
        SettleInterruptedShiftIfNeeded();
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
        driverStats = new DriverSaveData();
        currentVehicle = Content.ResolveStartingVehicle(config != null ? config.startingVehicle : null);
        currentMap = Content.ResolveStartingMap(config != null ? config.startingMap : null);
        HasInterruptedShift = false;

        totalReputation = 0;
        highestRankAchieved = 1;
        highestUnlockedRegionTier = 1;
        currentDay = 1;
        shiftsCompletedToday = 0;
        everPaidRent = false;
        lastRentChargeRank = 1;
        reachedEnding = false;
    }

    void ApplySaveData(GameSaveData data) {
        totalMoney = data.totalMoney;
        vehicleSaveList = data.vehicleSaveList ?? new List<VehicleSaveData>();
        driverStats = data.driverStats ?? new DriverSaveData();
        HasInterruptedShift = data.shiftInProgress;

        currentVehicle = Content.GetVehicle(data.currentVehicleId);
        if (currentVehicle == null && !string.IsNullOrEmpty(data.currentVehicleId)) {
            Debug.LogWarning("[Save] This profile uses vehicle '" + data.currentVehicleId +
                             "', which is not installed. Falling back to the starting vehicle.");
        }
        if (currentVehicle == null) currentVehicle = Content.ResolveStartingVehicle(config != null ? config.startingVehicle : null);

        currentMap = Content.GetMap(data.currentMapId);
        if (currentMap == null) currentMap = Content.ResolveStartingMap(config != null ? config.startingMap : null);

        totalReputation = data.totalReputation;
        highestRankAchieved = Mathf.Max(1, data.highestRankAchieved);
        highestUnlockedRegionTier = Mathf.Max(1, data.highestUnlockedRegionTier);
        currentDay = Mathf.Max(1, data.currentDay);
        shiftsCompletedToday = data.shiftsCompletedToday;
        everPaidRent = data.everPaidRent;
        lastRentChargeRank = Mathf.Max(1, data.lastRentChargeRank);
        reachedEnding = data.reachedEnding;
    }

    GameSaveData BuildSaveData() {
        return new GameSaveData {
            saveVersion = SaveMigration.CurrentVersion,
            totalMoney = totalMoney,
            currentVehicleId = currentVehicle != null ? currentVehicle.vehicleId : string.Empty,
            currentMapId = currentMap != null ? currentMap.mapId : string.Empty,
            vehicleSaveList = vehicleSaveList,
            driverStats = driverStats,
            shiftInProgress = HasInterruptedShift,
            totalReputation = totalReputation,
            highestRankAchieved = highestRankAchieved,
            highestUnlockedRegionTier = highestUnlockedRegionTier,
            currentDay = currentDay,
            shiftsCompletedToday = shiftsCompletedToday,
            everPaidRent = everPaidRent,
            lastRentChargeRank = lastRentChargeRank,
            reachedEnding = reachedEnding
        };
    }

    /// <summary>Raised after every write attempt to the active slot, successful or not.</summary>
    public static event System.Action<bool> GameSaved;

    /// <summary>Writes the current career to its slot.</summary>
    /// <returns>True when the write completed.</returns>
    public bool SaveGame() {
        bool success = Saves.Save(ActiveSlot, BuildSaveData());
        GameSaved?.Invoke(success);
        return success;
    }

    /// <summary>
    /// Marks that a session has begun, so a profile loaded with this flag still set can be
    /// recognised as a shift that was killed rather than finished.
    /// </summary>
    public void MarkShiftStarted() {
        HasInterruptedShift = true;
        SaveGame();
    }

    /// <summary>
    /// Settlement of a shift that was never finished, produced on load when the profile still had
    /// a session marked as running. Null once it has been shown to the player.
    /// </summary>
    /// <remarks>
    /// Nullable because <see cref="SessionResult"/> is a value type, and "no interrupted shift" has
    /// to be distinguishable from "an interrupted shift that settled to all zeroes".
    /// </remarks>
    public SessionResult? PendingInterruptedResult { get; private set; }

    /// <summary>Clears the pending interrupted settlement after the UI has shown it.</summary>
    public void ClearPendingInterruptedResult() {
        PendingInterruptedResult = null;
    }

    // Settles immediately on load rather than waiting for the UI, so the charge lands even if the
    // player never sees the notice. Otherwise force-quitting again before the notice appeared would
    // keep dodging the bill.
    void SettleInterruptedShiftIfNeeded() {
        if (!HasInterruptedShift) return;
        if (currentVehicle == null) {
            HasInterruptedShift = false;
            return;
        }
        PendingInterruptedResult = SettleSession(0, 0f, GetHealth(), EndReason.Interrupted, 0, 0);
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

    /// <summary>
    /// Purchased level of a stat, read from wherever it lives: the driver's own record for
    /// Storage/Stabilizer, or the active vehicle's record for everything else.
    /// </summary>
    /// <param name="stat">Which stat to read.</param>
    /// <returns>The number of levels bought, zero when none or when no vehicle is selected.</returns>
    public int GetLevel(VehicleStatId stat) {
        if (VehicleStatOwnership.IsDriverBound(stat)) return driverStats.GetLevel(stat);
        var save = GetCurrentVehicleSave();
        return save != null ? save.GetLevel(stat) : 0;
    }

    /// <summary>Current value of a stat on the active vehicle, including purchased levels.</summary>
    /// <param name="stat">Which stat to read.</param>
    /// <returns>The effective value, unclamped except where the stat is a fraction.</returns>
    public float GetStatValue(VehicleStatId stat) {
        if (currentVehicle == null) return 0f;
        return GetStatValueAtLevel(stat, GetLevel(stat));
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

    /// <summary>Current stat value including the temporary modifier selected for the next shift.</summary>
    /// <param name="stat">Stat to read.</param>
    /// <returns>The effective shift value with valid fraction bounds applied.</returns>
    public float GetShiftStatValue(VehicleStatId stat) {
        float value = GetStatValue(stat);
        if (currentModifier != null) value += currentModifier.GetStatDelta(stat);
        bool isFraction = stat == VehicleStatId.Armor || stat == VehicleStatId.Protection;
        return isFraction ? Mathf.Clamp01(value) : Mathf.Max(0f, value);
    }

    /// <summary>Current pizza capacity including the temporary shift modifier.</summary>
    /// <returns>The effective whole-pizza capacity, never below one.</returns>
    public int GetShiftCapacity() {
        return Mathf.Max(1, Mathf.RoundToInt(GetShiftStatValue(VehicleStatId.Capacity)));
    }

    /// <summary>Selects a registered modifier for the next shift without changing career progress.</summary>
    /// <param name="modifier">Modifier to select, or null to play without one.</param>
    public void SelectModifier(ShiftModifierData modifier) {
        if (modifier == null) {
            currentModifier = null;
            return;
        }

        if (allModifiers == null) return;
        foreach (var candidate in allModifiers) {
            if (candidate == modifier) {
                currentModifier = modifier;
                return;
            }
        }
    }

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
    /// <remarks>
    /// A driver-bound stat (Storage/Stabilizer) is capped by the <i>active</i> vehicle's authored
    /// max level, same as any other stat — since the level itself is shared, switching to a vehicle
    /// with a higher cap for that stat is how the player pushes it further.
    /// </remarks>
    /// <param name="stat">Which stat to upgrade.</param>
    /// <returns>True when a level was bought and money was spent.</returns>
    public bool TryUpgradeStat(VehicleStatId stat) {
        if (currentVehicle == null) return false;
        bool driverBound = VehicleStatOwnership.IsDriverBound(stat);
        var vehicleSave = GetCurrentVehicleSave();
        if (!driverBound && vehicleSave == null) return false;

        int currentLevel = GetLevel(stat);
        if (currentLevel >= currentVehicle.GetMaxLevel(stat)) return false;

        int cost = GetUpgradeCost(stat, currentLevel);
        if (totalMoney < cost) return false;

        totalMoney -= cost;
        if (driverBound) {
            driverStats.AddLevel(stat);
        }
        else {
            vehicleSave.moneySpent += cost;
            vehicleSave.AddLevel(stat);
        }
        SaveGame();
        return true;
    }

    /// <summary>Buys the active vehicle when it is still locked, rank-eligible, and affordable.</summary>
    /// <returns>True when the vehicle was unlocked and money was spent.</returns>
    public bool TryPurchaseVehicle() {
        var save = GetCurrentVehicleSave();
        if (save == null || currentVehicle == null) return false;
        if (save.isUnlocked) return false;
        if (CurrentRank < currentVehicle.requiredRank) return false;
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
        if (map == null || CurrentRank < map.requiredRank) return;
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
    /// Closes a session: banks the kept earnings, charges repairs, earns reputation, settles the
    /// day's rent if this was its last shift, and writes the result to disk.
    /// </summary>
    /// <param name="sessionEarnings">Money earned during the session, not yet banked.</param>
    /// <param name="currentHealth">Health remaining at the end.</param>
    /// <param name="maxHealth">The vehicle's maximum health.</param>
    /// <param name="reason">How the session ended, which decides how much of the earnings survive.</param>
    /// <param name="ordersCompleted">Orders fully delivered this shift, for the reputation quality signal.</param>
    /// <param name="ordersOffered">Customers that appeared this shift, for the reputation quality signal.</param>
    /// <returns>A breakdown of the settlement for the result screen.</returns>
    public SessionResult SettleSession(int sessionEarnings, float currentHealth, float maxHealth, EndReason reason, int ordersCompleted, int ordersOffered) {
        int bankBefore = totalMoney;

        int kept;
        switch (reason) {
            case EndReason.Wrecked: kept = Mathf.FloorToInt(sessionEarnings * deathEarningsKeep); break;
            case EndReason.Abandoned: kept = 0; break;
            case EndReason.Interrupted: kept = 0; break;
            default: kept = sessionEarnings; break;
        }
        kept = Mathf.Max(0, kept);

        // An interrupted shift is billed like a wreck: the vehicle was left in the street, so the
        // full health bar is charged. The bank safety clamp below still caps the damage, which is
        // what stops a genuine crash from ever being ruinous.
        bool died = (reason == EndReason.Wrecked || reason == EndReason.Interrupted);
        int repairRaw = CalculateRepairCost(currentHealth, maxHealth, died);

        int maxCharge = kept + Mathf.FloorToInt(bankBefore * repairBankSafetyRate);
        int repair = Mathf.Clamp(repairRaw, 0, maxCharge);

        totalMoney = Mathf.Max(0, bankBefore + kept - repair);

        var result = new SessionResult {
            reason = reason,
            grossEarnings = sessionEarnings,
            keptEarnings = kept,
            repairCost = repair,
            repairBeforeClamp = repairRaw,
            bankBefore = bankBefore,
            bankAfter = totalMoney,
            rankAfter = CurrentRank
        };

        // Rent is charged inside this same call, atomically with the day's last shift, rather than
        // as a separate check the garage could run later - otherwise a player could spend the bank
        // to zero the instant a day ends and dodge rent on demand every single day, no matter how
        // gentle the shortfall penalty is (BF-021-adjacent finding from the Economy Designer pass).
        if (Career != null) {
            int regionTier = currentMap != null ? currentMap.regionTier : 1;
            float onTimeRate = ordersOffered > 0 ? (float)ordersCompleted / ordersOffered : (ordersCompleted > 0 ? 1f : 0f);
            float healthRetainedRatio = maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
            int reputationEarned = Career.ComputeShiftReputation(regionTier, onTimeRate, healthRetainedRatio, reason);

            int cap = Career.GetRegionCap(highestUnlockedRegionTier);
            totalReputation = Mathf.Clamp(totalReputation + reputationEarned, 0, cap);

            int rankAfter = Career.ComputeRank(totalReputation, out bool reachedEndingNow);
            if (rankAfter > highestRankAchieved) highestRankAchieved = rankAfter;
            int unlockedTier = Career.ComputeUnlockedRegionTier(rankAfter);
            if (unlockedTier > highestUnlockedRegionTier) highestUnlockedRegionTier = unlockedTier;
            if (reachedEndingNow) reachedEnding = true;

            result.reputationEarned = reputationEarned;
            result.rankAfter = rankAfter;

            shiftsCompletedToday++;
            if (shiftsCompletedToday >= careerData.shiftsPerDay) {
                var rent = Career.ComputeRentSettlement(rankAfter, lastRentChargeRank, everPaidRent, totalMoney);
                totalMoney = rent.bankAfter;
                if (rent.reputationPenalty > 0) totalReputation = Mathf.Max(0, totalReputation - rent.reputationPenalty);
                everPaidRent = true;
                lastRentChargeRank = rankAfter;

                result.dayEnded = true;
                result.dayNumber = currentDay;
                result.rent = rent;
                result.bankAfter = totalMoney;

                currentDay++;
                shiftsCompletedToday = 0;
            }
        }

        // The shift is over however it ended, so the interrupted-shift flag is cleared here rather
        // than on any one ending path.
        HasInterruptedShift = false;
        SaveGame();

        return result;
    }
}
