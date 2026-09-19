using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

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
    [SerializeField] float upgradeCostBase = 100f;
    [SerializeField] float upgradeCostStep = 40f;

    [Header("Audio")]
    [Tooltip("Sound played when a career shift raises the courier's visual rank.")]
    [SerializeField] AudioClip rankUpClip;

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
    [Header("Shift Selection")]
    [Tooltip("Fallback duration used when the map selection screen has not supplied a duration.")]
    [SerializeField] int defaultShiftDurationMinutes = 5;
    [Tooltip("Fixed duration accepted by the first standard leaderboard and achievement ruleset.")]
    [SerializeField] int competitiveDurationMinutes = 5;
    int selectedShiftDurationMinutes;
    /// <summary>Permanent id of the selected difficulty inside currentMap.</summary>
    public string currentDifficultyId;
    ShiftModifierData currentModifier;
    readonly List<string> selectedModifierIds = new List<string>();
    public List<VehicleSaveData> vehicleSaveList = new List<VehicleSaveData>();
    /// <summary>Permanent map ids the active profile has purchased or received as a starter.</summary>
    public List<string> ownedMapIds = new List<string>();
    /// <summary>Best-score progress for each map and difficulty pair.</summary>
    public List<MapDifficultyProgress> mapDifficultyProgress = new List<MapDifficultyProgress>();
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
    /// <summary>True after the authored final career challenge has been completed successfully.</summary>
    public bool careerCompleted;

    [Header("Career Records")]
    /// <summary>Total settled career shifts, excluding endless sessions.</summary>
    public int totalShiftsSettled;
    /// <summary>Total customer orders completed across settled career shifts.</summary>
    public int totalOrdersCompleted;
    /// <summary>Total pizzas delivered across settled career shifts.</summary>
    public int totalPizzasDelivered;
    /// <summary>Highest score reached in one settled career shift.</summary>
    public int bestShiftScore;
    /// <summary>Highest pizza delivery count reached in one settled career shift.</summary>
    public int bestShiftDeliveries;
    /// <summary>Highest pizza delivery count reached in one endless session.</summary>
    public int bestFreeplayDeliveries;

    bool isFreeplayMode;
    int sessionSceneHandle;
    int initialSceneHandle;
    readonly Dictionary<VehicleStatId, float> frozenShiftStats = new Dictionary<VehicleStatId, float>();
    float frozenShiftSpeed;
    bool frozenBuiltInMap;
    bool frozenBuiltInVehicle;
    bool frozenCustomTuning;
    int frozenCompetitiveDuration;

    /// <summary>Scene-owned rules retained through settlement; cleared when that scene unloads.</summary>
    public TrafficSessionContext ActiveSession { get; private set; }

    /// <summary>Captures modifier and effective stat values once per scene, before player instantiation.</summary>
    /// <param name="scene">Scene owning this run; different scene handles create independent runs.</param>
    /// <param name="map">Explicit authored binding when a traffic host is present.</param>
    /// <param name="explicitBinding">True forbids any fallback to currentMap, including for null bindings.</param>
    public TrafficSessionContext PrepareSession(Scene scene, MapData map = null, bool explicitBinding = false) {
        if (ActiveSession != null && sessionSceneHandle == scene.handle) return ActiveSession;
        if (ActiveSession != null) ReleaseSession(ActiveSession);
        var context = SessionSceneRules.Create(this, map, explicitBinding, scene.name);
        foreach (VehicleStatId stat in System.Enum.GetValues(typeof(VehicleStatId))) {
            float value = GetStatValue(stat) + context.Modifiers.GetStatDelta(stat);
            frozenShiftStats[stat] = stat == VehicleStatId.Armor || stat == VehicleStatId.Protection
                ? Mathf.Clamp01(value) : Mathf.Max(0f, value);
        }
        frozenShiftSpeed = Mathf.Max(0f, GetSelectedBaseSpeed() + context.Modifiers.GetStatDelta(VehicleStatId.Speed));
        MapData boundMap = explicitBinding ? map : currentMap;
        frozenBuiltInMap = Content != null && Content.GetMapProviderId(boundMap) == BuiltInContentProvider.SourceId;
        frozenBuiltInVehicle = Content != null && Content.GetVehicleProviderId(currentVehicle) == BuiltInContentProvider.SourceId;
        var vehicleSave = GetCurrentVehicleSave();
        frozenCustomTuning = vehicleSave != null && vehicleSave.hasCustomTuning;
        frozenCompetitiveDuration = CompetitiveDurationMinutes;
        sessionSceneHandle = scene.handle;
        ActiveSession = context;
        if (scene.handle == initialSceneHandle || !Application.isPlaying)
            context.Integrity.MarkInvalid("Direct editor scene test is not a competitive career run.");
        if (!explicitBinding) SessionSceneRules.Freeze(context, false);
        return context;
    }

    /// <summary>Releases only the matching scene's context; stale teardown cannot clear a newer run.</summary>
    public void ReleaseSession(TrafficSessionContext context) {
        if (context == null || !ReferenceEquals(ActiveSession, context)) return;
        context.SetPhase(TrafficSessionPhase.Ended);
        ActiveSession = null;
        frozenShiftStats.Clear();
    }

    void HandleSessionSceneUnloaded(Scene scene) {
        if (ActiveSession != null && scene.handle == sessionSceneHandle) ReleaseSession(ActiveSession);
    }

    void OnDestroy() {
        SceneManager.sceneUnloaded -= HandleSessionSceneUnloaded;
        if (Instance == this) {
            ReleaseSession(ActiveSession);
            Instance = null;
        }
    }

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

    /// <summary>Currently selected and unlocked difficulty inside the active map.</summary>
    public MapDifficultyData CurrentDifficulty => GetCurrentDifficulty();

    /// <summary>Persistent courier rating shown in career UI and available to future leaderboards.</summary>
    public int CourierRating => Mathf.Max(0, totalReputation);

    /// <summary>Visual rank represented by the active courier rating.</summary>
    public int CurrentRank => Career != null ? Career.ComputeRank(CourierRating, out _) : 1;

    /// <summary>True while the current gameplay scene is running an endless session.</summary>
    public bool IsFreeplayMode => isFreeplayMode;

    /// <summary>Duration selected for the next finite shift, in whole minutes.</summary>
    /// <remarks>This is temporary session setup and is deliberately excluded from career saves.</remarks>
    public int SelectedShiftDurationMinutes => Mathf.Max(1, selectedShiftDurationMinutes);

    /// <summary>Duration accepted by the first standard competitive ruleset, in minutes.</summary>
    public int CompetitiveDurationMinutes => Mathf.Max(1, competitiveDurationMinutes);

    /// <summary>True after the authored final career challenge has been completed.</summary>
    public bool IsCareerCompleted => careerCompleted;

    /// <summary>
    /// True when the next career shift is the authored final challenge. This is driven by career
    /// day data rather than courier rating, so the rating remains a display-only performance score.
    /// </summary>
    public bool IsFinalShift {
        get {
            return !isFreeplayMode && !careerCompleted && CareerData != null &&
                   currentDay >= Mathf.Max(1, CareerData.finalShiftDay);
        }
    }

    /// <summary>Plays a one-shot sound through the persistent SFX audio source.</summary>
    /// <param name="clip">Clip to play; ignored when null or when the shared source is unavailable.</param>
    /// <param name="volume">Relative one-shot volume in the range 0 to 1.</param>
    public void PlaySfx(AudioClip clip, float volume = 1f) {
        if (clip == null) return;
        var source = GetComponent<AudioSource>();
        if (source != null) source.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    /// <summary>Number of orders required by the authored final career challenge.</summary>
    public int FinalShiftOrderTarget => CareerData != null ? Mathf.Max(1, CareerData.finalShiftOrderTarget) : 1;

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
        initialSceneHandle = gameObject.scene.handle;
        SceneManager.sceneUnloaded += HandleSessionSceneUnloaded;
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
        isFreeplayMode = false;
        selectedShiftDurationMinutes = Mathf.Max(1, defaultShiftDurationMinutes);
        ActiveSlot = Mathf.Clamp(slotIndex, 0, Saves.SlotCount - 1);
        PlayerPrefs.SetInt(LastSlotKey, ActiveSlot);
        PlayerPrefs.Save();

        var data = Saves.Load(ActiveSlot, out var status);
        if (data == null) StartFreshCareer();
        else ApplySaveData(data);

        InitializeVehicles();
        InitializeMaps();

        // Runs after InitializeVehicles because the repair bill needs the vehicle's stats.
        SettleInterruptedShiftIfNeeded();
        return status;
    }

    /// <summary>Wipes a slot and begins a brand new career in it.</summary>
    /// <param name="slotIndex">Slot the new career occupies.</param>
    public void StartNewCareer(int slotIndex) {
        isFreeplayMode = false;
        selectedShiftDurationMinutes = Mathf.Max(1, defaultShiftDurationMinutes);
        ActiveSlot = Mathf.Clamp(slotIndex, 0, Saves.SlotCount - 1);
        PlayerPrefs.SetInt(LastSlotKey, ActiveSlot);
        PlayerPrefs.Save();

        Saves.Delete(ActiveSlot);
        StartFreshCareer();
        InitializeVehicles();
        InitializeMaps();
        SaveGame();
    }

    /// <summary>
    /// Starts an endless session on the currently selected owned map without changing career day,
    /// courier rating, rent state, or career objectives. The session still settles money and repair
    /// costs when it ends, and its best delivery count is stored as a personal record.
    /// </summary>
    public void StartFreeplay() {
        if (currentMap == null || string.IsNullOrEmpty(currentMap.sceneName)) {
            Debug.LogError("Cannot start freeplay because the selected map has no gameplay scene.");
            return;
        }
        if (!IsMapOwned(currentMap)) {
            Debug.LogError("Cannot start freeplay because the selected map is not owned.");
            return;
        }

        isFreeplayMode = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(currentMap.sceneName);
    }

    void StartFreshCareer() {
        totalMoney = config != null ? config.startingMoney : totalMoney;
        vehicleSaveList = new List<VehicleSaveData>();
        ownedMapIds = new List<string>();
        mapDifficultyProgress = new List<MapDifficultyProgress>();
        driverStats = new DriverSaveData();
        currentVehicle = Content.ResolveStartingVehicle(config != null ? config.startingVehicle : null);
        currentMap = Content.ResolveStartingMap(config != null ? config.startingMap : null);
        currentDifficultyId = string.Empty;
        HasInterruptedShift = false;

        totalReputation = 0;
        highestRankAchieved = 1;
        highestUnlockedRegionTier = 1;
        currentDay = 1;
        shiftsCompletedToday = 0;
        everPaidRent = false;
        lastRentChargeRank = 1;
        reachedEnding = false;
        careerCompleted = false;
        totalShiftsSettled = 0;
        totalOrdersCompleted = 0;
        totalPizzasDelivered = 0;
        bestShiftScore = 0;
        bestShiftDeliveries = 0;
        bestFreeplayDeliveries = 0;
    }

    void ApplySaveData(GameSaveData data) {
        totalMoney = Mathf.Max(0, data.totalMoney);
        vehicleSaveList = data.vehicleSaveList ?? new List<VehicleSaveData>();
        ownedMapIds = data.ownedMapIds ?? new List<string>();
        mapDifficultyProgress = data.mapDifficultyProgress ?? new List<MapDifficultyProgress>();
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
        currentDifficultyId = data.currentDifficultyId;

        totalReputation = Mathf.Max(0, data.totalReputation);
        highestRankAchieved = Mathf.Max(1, data.highestRankAchieved);
        highestUnlockedRegionTier = Mathf.Max(1, data.highestUnlockedRegionTier);
        currentDay = Mathf.Max(1, data.currentDay);
        shiftsCompletedToday = data.shiftsCompletedToday;
        everPaidRent = data.everPaidRent;
        lastRentChargeRank = Mathf.Max(1, data.lastRentChargeRank);
        reachedEnding = data.reachedEnding;
        careerCompleted = data.careerCompleted;
        totalShiftsSettled = Mathf.Max(0, data.totalShiftsSettled);
        totalOrdersCompleted = Mathf.Max(0, data.totalOrdersCompleted);
        totalPizzasDelivered = Mathf.Max(0, data.totalPizzasDelivered);
        bestShiftScore = Mathf.Max(0, data.bestShiftScore);
        bestShiftDeliveries = Mathf.Max(0, data.bestShiftDeliveries);
        bestFreeplayDeliveries = Mathf.Max(0, data.bestFreeplayDeliveries);
    }

    GameSaveData BuildSaveData() {
        return new GameSaveData {
            saveVersion = SaveMigration.CurrentVersion,
            totalMoney = totalMoney,
            currentVehicleId = currentVehicle != null ? currentVehicle.vehicleId : string.Empty,
            currentMapId = currentMap != null ? currentMap.mapId : string.Empty,
            currentDifficultyId = currentDifficultyId ?? string.Empty,
            vehicleSaveList = vehicleSaveList,
            ownedMapIds = ownedMapIds,
            mapDifficultyProgress = mapDifficultyProgress,
            driverStats = driverStats,
            shiftInProgress = HasInterruptedShift,
            totalReputation = totalReputation,
            highestRankAchieved = highestRankAchieved,
            highestUnlockedRegionTier = highestUnlockedRegionTier,
            currentDay = currentDay,
            shiftsCompletedToday = shiftsCompletedToday,
            everPaidRent = everPaidRent,
            lastRentChargeRank = lastRentChargeRank,
            reachedEnding = reachedEnding,
            careerCompleted = careerCompleted,
            totalShiftsSettled = totalShiftsSettled,
            totalOrdersCompleted = totalOrdersCompleted,
            totalPizzasDelivered = totalPizzasDelivered,
            bestShiftScore = bestShiftScore,
            bestShiftDeliveries = bestShiftDeliveries,
            bestFreeplayDeliveries = bestFreeplayDeliveries
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
        PendingInterruptedResult = SettleSession(0, 0f, GetHealth(), EndReason.Interrupted, 0, 0, 0, 0f);
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

    void InitializeMaps() {
        if (ownedMapIds == null) ownedMapIds = new List<string>();
        if (mapDifficultyProgress == null) mapDifficultyProgress = new List<MapDifficultyProgress>();

        var starter = Content.ResolveStartingMap(config != null ? config.startingMap : null);
        if (starter != null && !string.IsNullOrEmpty(starter.mapId) && !ownedMapIds.Contains(starter.mapId)) {
            ownedMapIds.Add(starter.mapId);
        }

        // Preserve a map that a legacy profile had already selected under the former rank gate.
        if (currentMap != null && !string.IsNullOrEmpty(currentMap.mapId) && !ownedMapIds.Contains(currentMap.mapId)) {
            ownedMapIds.Add(currentMap.mapId);
        }

        EnsureCurrentDifficulty();
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

    /// <summary>Spends as much banked money as possible without allowing a negative balance.</summary>
    /// <param name="requestedAmount">Amount requested by a small gameplay purchase.</param>
    /// <returns>The amount actually removed from the bank.</returns>
    public int TrySpendMoney(int requestedAmount) {
        int requested = Mathf.Max(0, requestedAmount);
        int spent = Mathf.Min(requested, Mathf.Max(0, totalMoney));
        totalMoney -= spent;
        return spent;
    }

    /// <summary>Adds a positive currency amount for temporary developer testing.</summary>
    /// <param name="amount">Currency to add. Non-positive values are ignored.</param>
    /// <returns>True when the balance was changed and saved.</returns>
    public bool DeveloperAddMoney(int amount) {
        int safeAmount = Mathf.Max(0, amount);
        if (safeAmount <= 0) return false;

        long nextBalance = (long)Mathf.Max(0, totalMoney) + safeAmount;
        totalMoney = nextBalance > int.MaxValue ? int.MaxValue : (int)nextBalance;
        SaveGame();
        return true;
    }

    /// <summary>Adjusts the persistent courier rating for temporary developer testing.</summary>
    /// <param name="delta">Signed rating change. The total remains floored at zero.</param>
    /// <returns>True when a non-zero adjustment was applied and saved.</returns>
    public bool DeveloperAdjustCourierRating(int delta) {
        if (delta == 0) return false;

        long nextRating = (long)Mathf.Max(0, totalReputation) + delta;
        totalReputation = nextRating <= 0 ? 0 : nextRating > int.MaxValue ? int.MaxValue : (int)nextRating;
        int rank = CurrentRank;
        if (rank > highestRankAchieved) highestRankAchieved = rank;
        SaveGame();
        return true;
    }

    /// <summary>Unlocks every registered vehicle for temporary developer testing.</summary>
    /// <returns>The number of vehicle records that changed.</returns>
    public int DeveloperUnlockAllVehicles() {
        if (Content == null || Content.Vehicles == null) return 0;
        if (vehicleSaveList == null) vehicleSaveList = new List<VehicleSaveData>();

        int changed = 0;
        foreach (var vehicle in Content.Vehicles) {
            if (vehicle == null || string.IsNullOrEmpty(vehicle.vehicleId)) continue;
            VehicleSaveData save = vehicleSaveList.FirstOrDefault(x => x != null && x.vehicleId == vehicle.vehicleId);
            if (save == null) {
                vehicleSaveList.Add(new VehicleSaveData(vehicle.vehicleId, true));
                changed++;
                continue;
            }

            if (save.isUnlocked) continue;
            save.isUnlocked = true;
            changed++;
        }

        if (changed > 0) SaveGame();
        return changed;
    }

    /// <summary>Unlocks every registered map and its authored difficulty tiers for testing.</summary>
    /// <returns>The number of map ownership records that changed.</returns>
    public int DeveloperUnlockAllMapsAndDifficulties() {
        if (Content == null || Content.Maps == null) return 0;
        if (ownedMapIds == null) ownedMapIds = new List<string>();
        if (mapDifficultyProgress == null) mapDifficultyProgress = new List<MapDifficultyProgress>();

        int changedMaps = 0;
        bool changed = false;
        foreach (var map in Content.Maps) {
            if (map == null || string.IsNullOrEmpty(map.mapId)) continue;
            if (!ownedMapIds.Contains(map.mapId)) {
                ownedMapIds.Add(map.mapId);
                changedMaps++;
                changed = true;
            }

            if (map.levelData == null || map.levelData.difficultyLevels == null) continue;
            for (int i = 0; i < map.levelData.difficultyLevels.Count; i++) {
                MapDifficultyData difficulty = map.levelData.GetDifficultyAt(i);
                if (difficulty == null || string.IsNullOrEmpty(difficulty.difficultyId)) continue;

                MapDifficultyProgress progress = mapDifficultyProgress.FirstOrDefault(x => x != null &&
                    x.mapId == map.mapId && x.difficultyId == difficulty.difficultyId);
                int guaranteedBest = Mathf.Max(0, difficulty.unlockTargetScoreForNext);
                if (progress == null) {
                    mapDifficultyProgress.Add(new MapDifficultyProgress(map.mapId, difficulty.difficultyId, guaranteedBest));
                    changed = true;
                }
                else {
                    int previousBest = progress.bestScore;
                    progress.bestScore = Mathf.Max(progress.bestScore, guaranteedBest);
                    changed |= progress.bestScore != previousBest;
                }
            }
        }

        if (changed) SaveGame();
        return changedMaps;
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

    /// <summary>Maximum base speed unlocked by the active vehicle's purchased Speed levels.</summary>
    /// <returns>The purchased speed ceiling before optional tuning and shift modifiers.</returns>
    public float GetUnlockedMaxSpeed() {
        if (currentVehicle == null) return 0f;
        return VehicleTuningRules.GetUnlockedMaxSpeed(currentVehicle.baseSpeed, currentVehicle.speedStep,
            GetLevel(VehicleStatId.Speed), currentVehicle.maxSpeedLevel);
    }

    /// <summary>Speed selected for the active vehicle before a temporary shift modifier is applied.</summary>
    /// <returns>The authored maximum in Classic mode, or the saved clamped selection in Advanced mode.</returns>
    public float GetSelectedBaseSpeed() {
        if (currentVehicle == null) return 0f;
        VehicleSaveData save = GetCurrentVehicleSave();
        return VehicleTuningRules.ResolveSpeed(GameSettings.AdvancedTuningEnabled,
            save != null && save.hasCustomTuning, save != null ? save.tunedSpeed : 0f,
            currentVehicle.minimumTuningSpeed, GetUnlockedMaxSpeed());
    }

    /// <summary>Effective speed passed to the next spawned vehicle after every selected shift modifier.</summary>
    /// <returns>The non-negative speed limit used by the movement motor.</returns>
    public float GetEffectiveShiftSpeed() {
        if (ActiveSession != null) return frozenShiftSpeed;
        float speed = GetSelectedBaseSpeed();
        speed += ModifierEffectResolver.GetCombinedStatDelta(GetSelectedModifiers(), VehicleStatId.Speed);
        return Mathf.Max(0f, speed);
    }

    /// <summary>
    /// Creates the detached driving settings snapshot for the next spawned vehicle.
    /// </summary>
    /// <returns>A copy of the authored settings with valid custom drift values when Advanced Tuning is active.</returns>
    public VehicleDrivingSettings GetEffectiveShiftDrivingSettings() {
        if (currentVehicle == null) return null;

        VehicleDrivingSettings authored = currentVehicle.drivingSettings;
        VehicleDrivingSettings snapshot = authored != null ? authored.Clone() : new VehicleDrivingSettings();
        VehicleSaveData save = GetCurrentVehicleSave();
        if (!GameSettings.AdvancedTuningEnabled || save == null || !save.hasCustomTuning) return snapshot;

        snapshot.driftGrip = VehicleTuningRules.ResolvePlayerValue(true, true, save.tunedDriftGrip,
            snapshot.driftGrip, snapshot.playerDriftGripMin, snapshot.playerDriftGripMax);
        snapshot.driftSteeringMultiplier = VehicleTuningRules.ResolvePlayerValue(true, true,
            save.tunedDriftSteeringMultiplier, snapshot.driftSteeringMultiplier,
            snapshot.playerDriftSteeringMultiplierMin, snapshot.playerDriftSteeringMultiplierMax);
        snapshot.gripEnterTime = VehicleTuningRules.ResolvePlayerValue(true, true, save.tunedGripEnterTime,
            snapshot.gripEnterTime, snapshot.playerGripEnterTimeMin, snapshot.playerGripEnterTimeMax);
        return snapshot;
    }

    /// <summary>
    /// Saves the optional tuning snapshot for the currently selected vehicle after clamping every
    /// value to that vehicle's authored envelope.
    /// </summary>
    /// <param name="selectedSpeed">Base speed selected for future shifts.</param>
    /// <param name="driftGrip">Lateral grip used while handbraking.</param>
    /// <param name="driftSteeringMultiplier">Steering multiplier used while handbraking.</param>
    /// <param name="gripEnterTime">Time used to transition into drift grip.</param>
    /// <returns>True when a current vehicle record received and saved the snapshot.</returns>
    public bool SaveCurrentVehicleTuning(float selectedSpeed, float driftGrip,
        float driftSteeringMultiplier, float gripEnterTime) {
        if (currentVehicle == null) return false;
        VehicleSaveData save = GetCurrentVehicleSave();
        if (save == null) return false;

        VehicleDrivingSettings settings = currentVehicle.drivingSettings != null
            ? currentVehicle.drivingSettings
            : new VehicleDrivingSettings();
        save.tunedSpeed = VehicleTuningRules.ClampSpeed(currentVehicle.minimumTuningSpeed,
            GetUnlockedMaxSpeed(), selectedSpeed);
        save.tunedDriftGrip = VehicleTuningRules.ClampPlayerValue(driftGrip,
            settings.playerDriftGripMin, settings.playerDriftGripMax);
        save.tunedDriftSteeringMultiplier = VehicleTuningRules.ClampPlayerValue(
            driftSteeringMultiplier, settings.playerDriftSteeringMultiplierMin,
            settings.playerDriftSteeringMultiplierMax);
        save.tunedGripEnterTime = VehicleTuningRules.ClampPlayerValue(gripEnterTime,
            settings.playerGripEnterTimeMin, settings.playerGripEnterTimeMax);
        save.hasCustomTuning = true;
        SaveGame();
        return true;
    }

    /// <summary>Clears the current vehicle's custom tuning so future shifts use authored defaults.</summary>
    /// <returns>True when a current vehicle record was reset and saved.</returns>
    public bool ResetCurrentVehicleTuning() {
        VehicleSaveData save = GetCurrentVehicleSave();
        if (save == null) return false;
        save.hasCustomTuning = false;
        save.tunedSpeed = 0f;
        save.tunedDriftGrip = 0f;
        save.tunedDriftSteeringMultiplier = 0f;
        save.tunedGripEnterTime = 0f;
        SaveGame();
        return true;
    }

    /// <summary>Current stat value including the temporary modifier selected for the next shift.</summary>
    /// <param name="stat">Stat to read.</param>
    /// <returns>The effective shift value with valid fraction bounds applied.</returns>
    public float GetShiftStatValue(VehicleStatId stat) {
        if (ActiveSession != null && frozenShiftStats.TryGetValue(stat, out float frozen)) return frozen;
        float value = GetStatValue(stat);
        value += ModifierEffectResolver.GetCombinedStatDelta(GetSelectedModifiers(), stat);
        bool isFraction = stat == VehicleStatId.Armor || stat == VehicleStatId.Protection;
        return isFraction ? Mathf.Clamp01(value) : Mathf.Max(0f, value);
    }

    /// <summary>Current pizza capacity including the temporary shift modifier.</summary>
    /// <returns>The effective whole-pizza capacity, never below one.</returns>
    public int GetShiftCapacity() {
        return Mathf.Max(1, Mathf.RoundToInt(GetShiftStatValue(VehicleStatId.Capacity)));
    }

    /// <summary>
    /// Selects a registered modifier for the next shift without changing career progress.
    /// Compatibility wrapper over <see cref="SelectModifiers"/> for the single-modifier screen:
    /// it always builds a single-element (or empty) id request, so both selection states stay in sync.
    /// </summary>
    /// <param name="modifier">Modifier to select, or null to play without one.</param>
    public void SelectModifier(ShiftModifierData modifier) {
        if (modifier == null) {
            currentModifier = null;
            SelectModifiers(null);
            return;
        }

        if (allModifiers == null) return;
        foreach (var candidate in allModifiers) {
            if (candidate == modifier) {
                currentModifier = modifier;
                SelectModifiers(new[] { modifier.modifierId });
                return;
            }
        }
    }

    /// <summary>
    /// Selects a duplicate-free, order-independent set of modifiers by stable id for the next shift.
    /// Not saved to career progress. Unknown ids, duplicates, and exclusive-group conflicts are rejected.
    /// </summary>
    /// <param name="modifierIds">Requested modifier ids in any order.</param>
    /// <returns>The resolved selection together with any rejected ids.</returns>
    public ModifierSelectionResult SelectModifiers(IReadOnlyList<string> modifierIds) {
        var result = ModifierSelectionResolver.Resolve(allModifiers, modifierIds);
        selectedModifierIds.Clear();
        foreach (var modifier in result.resolvedModifiers) selectedModifierIds.Add(modifier.modifierId);
        return result;
    }

    /// <summary>Modifiers selected for the next shift, in canonical order. Not saved to career progress.</summary>
    public IReadOnlyList<string> SelectedModifierIds => selectedModifierIds;

    /// <summary>Resolves the selected modifier ids back to their assets, in the same canonical order.</summary>
    /// <returns>Resolved modifiers; an id with no matching asset in <see cref="allModifiers"/> is skipped.</returns>
    public IReadOnlyList<ShiftModifierData> GetSelectedModifiers() {
        var resolved = new List<ShiftModifierData>(selectedModifierIds.Count);
        if (allModifiers == null) return resolved;
        foreach (var id in selectedModifierIds) {
            foreach (var candidate in allModifiers) {
                if (candidate != null && candidate.modifierId == id) {
                    resolved.Add(candidate);
                    break;
                }
            }
        }
        return resolved;
    }

    /// <summary>
    /// Takes a detached copy of the current map/vehicle/difficulty/duration/modifier selection for a
    /// new traffic/police session. This is the raw pre-validation draft; S02/S07 map validation turns
    /// it into the final frozen <see cref="SessionRulesSnapshot"/>.
    /// </summary>
    /// <param name="sessionId">Caller-assigned identifier for the new session.</param>
    public SessionSetupDraft CreateSessionSetupDraft(string sessionId) {
        string mapId = currentMap != null ? currentMap.mapId : string.Empty;
        string vehicleId = currentVehicle != null ? currentVehicle.vehicleId : string.Empty;
        return new SessionSetupDraft(sessionId, mapId, vehicleId, currentDifficultyId,
            SelectedShiftDurationMinutes, IsFreeplayMode, SelectedModifierIds);
    }

    /// <summary>Selects the finite shift duration used when the next gameplay scene starts.</summary>
    /// <param name="minutes">Positive whole minutes supplied by the map selection data.</param>
    /// <returns>True when the duration was accepted.</returns>
    public bool SelectShiftDuration(int minutes) {
        if (minutes <= 0) return false;
        selectedShiftDurationMinutes = minutes;
        return true;
    }

    /// <summary>Evaluates the current session against the standard competitive content rules.</summary>
    /// <param name="durationMinutes">Duration used by the session.</param>
    /// <param name="reason">Reason that ended the session.</param>
    /// <param name="freeplay">Whether the session was endless.</param>
    /// <returns>A status explaining eligibility for future leaderboard and achievement services.</returns>
    public CompetitiveEligibilityStatus EvaluateCompetitiveEligibility(int durationMinutes,
        EndReason reason, bool freeplay) {
        if (ActiveSession != null && !ActiveSession.Integrity.IsValid) return CompetitiveEligibilityStatus.Unfinished;
        if (ActiveSession != null) return CompetitiveRunRules.Evaluate(ActiveSession.Draft.isFreeplay,
            frozenBuiltInMap, frozenBuiltInVehicle, frozenCustomTuning, ActiveSession.Draft.shiftDurationMinutes,
            frozenCompetitiveDuration, reason == EndReason.TimeUp || reason == EndReason.Extracted);
        bool builtInMap = Content != null && Content.GetMapProviderId(currentMap) == BuiltInContentProvider.SourceId;
        bool builtInVehicle = Content != null && Content.GetVehicleProviderId(currentVehicle) == BuiltInContentProvider.SourceId;
        VehicleSaveData vehicleSave = GetCurrentVehicleSave();
        bool customTuning = vehicleSave != null && vehicleSave.hasCustomTuning;
        bool completed = reason == EndReason.TimeUp || reason == EndReason.Extracted;
        return CompetitiveRunRules.Evaluate(freeplay, builtInMap, builtInVehicle, customTuning,
            durationMinutes, CompetitiveDurationMinutes, completed);
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

    /// <summary>Buys the active vehicle when it is still locked and affordable.</summary>
    /// <returns>True when the vehicle was unlocked and money was spent.</returns>
    public bool TryPurchaseVehicle() {
        var save = GetCurrentVehicleSave();
        if (save == null || currentVehicle == null) return false;
        if (save.isUnlocked) return false;
        int price = Mathf.Max(0, currentVehicle.price);
        if (totalMoney < price) return false;

        totalMoney -= price;
        save.isUnlocked = true;
        SaveGame();
        return true;
    }

    /// <summary>Steps to the next or previous vehicle in the registry, wrapping at both ends.</summary>
    /// <param name="direction">Positive to advance, negative to go back.</param>
    public void ChangeVehicle(int direction) {
        if (Content == null || Content.Vehicles == null || Content.Vehicles.Count == 0) return;

        var vehicles = Content.Vehicles;
        int currentIndex = -1;
        for (int i = 0; i < vehicles.Count; i++) {
            var vehicle = vehicles[i];
            if (vehicle == currentVehicle ||
                (vehicle != null && currentVehicle != null &&
                 !string.IsNullOrEmpty(vehicle.vehicleId) &&
                 string.Equals(vehicle.vehicleId, currentVehicle.vehicleId, System.StringComparison.Ordinal))) {
                currentIndex = i;
                break;
            }
        }

        // A profile can contain an asset reference from before the content registry was rebuilt.
        // Use a deterministic fallback so navigation never silently remains on the same vehicle.
        if (currentIndex < 0) currentIndex = direction < 0 ? 0 : vehicles.Count - 1;

        int step = direction > 0 ? 1 : direction < 0 ? -1 : 0;
        if (step == 0) return;

        int newIndex = (currentIndex + step) % vehicles.Count;
        if (newIndex < 0) newIndex += vehicles.Count;

        currentVehicle = vehicles[newIndex];
        SaveGame();
    }

    /// <summary>Returns whether a map has been purchased by the active profile.</summary>
    /// <param name="map">Map to inspect.</param>
    /// <returns>True when the map id is present in the persistent ownership list.</returns>
    public bool IsMapOwned(MapData map) {
        return map != null && ownedMapIds != null && !string.IsNullOrEmpty(map.mapId) && ownedMapIds.Contains(map.mapId);
    }

    /// <summary>Returns whether the active profile can purchase a map right now.</summary>
    /// <param name="map">Map to inspect.</param>
    /// <returns>True when the map is registered, unowned, and affordable.</returns>
    public bool CanPurchaseMap(MapData map) {
        if (map == null || Content == null || Content.GetMap(map.mapId) != map || IsMapOwned(map)) return false;
        return totalMoney >= Mathf.Max(0, map.unlockPrice);
    }

    /// <summary>Purchases a map once and stores ownership by its permanent id.</summary>
    /// <param name="map">Map to purchase.</param>
    /// <returns>True when ownership was newly granted and currency was spent.</returns>
    public bool TryPurchaseMap(MapData map) {
        if (!CanPurchaseMap(map)) return false;

        int price = Mathf.Max(0, map.unlockPrice);
        totalMoney -= price;
        ownedMapIds.Add(map.mapId);
        SaveGame();
        return true;
    }

    /// <summary>Selects an owned map for the next session.</summary>
    /// <param name="map">Map to select. Ignored when null or unowned.</param>
    public void SelectMap(MapData map) {
        if (map == null || !IsMapOwned(map)) return;
        currentMap = map;
        EnsureCurrentDifficulty();
        SaveGame();
    }

    /// <summary>Returns the selected difficulty when it is authored and currently unlocked.</summary>
    /// <returns>The active map difficulty, or null when the map has no difficulty data.</returns>
    public MapDifficultyData GetCurrentDifficulty() {
        if (currentMap == null || currentMap.levelData == null) return null;

        MapDifficultyData selected = currentMap.levelData.GetDifficulty(currentDifficultyId);
        if (selected != null) {
            int selectedIndex = FindDifficultyIndex(currentMap, selected.difficultyId);
            if (IsDifficultyUnlocked(currentMap, selectedIndex)) return selected;
        }

        return GetFirstUnlockedDifficulty(currentMap);
    }

    /// <summary>Returns whether a map difficulty is available to the active profile.</summary>
    /// <param name="map">Map containing the difficulty.</param>
    /// <param name="difficultyIndex">Zero-based index in the map's difficulty list.</param>
    /// <returns>True when the map is owned and the previous tier target has been reached.</returns>
    public bool IsDifficultyUnlocked(MapData map, int difficultyIndex) {
        if (map == null || map.levelData == null || !IsMapOwned(map)) return false;
        MapDifficultyData difficulty = map.levelData.GetDifficultyAt(difficultyIndex);
        if (difficulty == null) return false;
        if (difficultyIndex <= 0) return true;

        MapDifficultyData previous = map.levelData.GetDifficultyAt(difficultyIndex - 1);
        return MapDifficultyRules.IsTierUnlocked(IsMapOwned(map), difficultyIndex, previous,
            previous != null ? GetBestDifficultyScore(map, previous.difficultyId) : 0);
    }

    /// <summary>Returns the best saved score for a map difficulty.</summary>
    /// <param name="map">Map containing the difficulty.</param>
    /// <param name="difficultyId">Permanent difficulty identifier.</param>
    /// <returns>The saved best score, or zero when no record exists.</returns>
    public int GetBestDifficultyScore(MapData map, string difficultyId) {
        if (map == null || string.IsNullOrEmpty(difficultyId) || mapDifficultyProgress == null) return 0;
        int bestScore = 0;
        foreach (var progress in mapDifficultyProgress) {
            if (progress == null || progress.mapId != map.mapId || progress.difficultyId != difficultyId) continue;
            bestScore = Mathf.Max(bestScore, progress.bestScore);
        }
        return bestScore;
    }

    /// <summary>Applies an authored unlocked difficulty to the selected map.</summary>
    /// <param name="map">Owned map to select.</param>
    /// <param name="difficultyId">Permanent difficulty identifier.</param>
    /// <returns>True when the selection was applied and saved.</returns>
    public bool SelectDifficulty(MapData map, string difficultyId) {
        if (map == null || map != currentMap || !IsMapOwned(map) || map.levelData == null) return false;
        MapDifficultyData difficulty = map.levelData.GetDifficulty(difficultyId);
        int index = difficulty != null ? FindDifficultyIndex(map, difficulty.difficultyId) : -1;
        if (difficulty == null || !IsDifficultyUnlocked(map, index)) return false;

        currentDifficultyId = difficulty.difficultyId;
        SaveGame();
        return true;
    }

    /// <summary>Records a settled career score for the active map difficulty.</summary>
    /// <param name="score">Score reached during the settled session.</param>
    /// <param name="reason">Ending reason used to reject abandoned sessions.</param>
    /// <returns>True when a best-score record was created or improved.</returns>
    public bool RecordDifficultyScore(int score, EndReason reason) {
        if ((ActiveSession != null ? ActiveSession.Draft.isFreeplay : IsFreeplayMode) ||
            reason == EndReason.Abandoned || reason == EndReason.Interrupted) return false;
        if (ActiveSession != null && !ActiveSession.Integrity.IsValid) return false;
        if (mapDifficultyProgress == null) return false;
        string mapId = ActiveSession != null ? ActiveSession.Draft.mapId : currentMap != null ? currentMap.mapId : null;
        string difficultyId = ActiveSession != null ? ActiveSession.Draft.difficultyId : GetCurrentDifficulty()?.difficultyId;
        if (string.IsNullOrEmpty(mapId) || string.IsNullOrEmpty(difficultyId)) return false;

        int safeScore = Mathf.Max(0, score);
        foreach (var progress in mapDifficultyProgress) {
            if (progress == null || progress.mapId != mapId || progress.difficultyId != difficultyId) continue;
            if (safeScore <= progress.bestScore) return false;
            progress.bestScore = safeScore;
            return true;
        }

        mapDifficultyProgress.Add(new MapDifficultyProgress(mapId, difficultyId, safeScore));
        return true;
    }

    MapDifficultyData GetFirstUnlockedDifficulty(MapData map) {
        if (map == null || map.levelData == null || map.levelData.difficultyLevels == null) return null;
        for (int i = 0; i < map.levelData.difficultyLevels.Count; i++) {
            if (map.levelData.GetDifficultyAt(i) != null && IsDifficultyUnlocked(map, i))
                return map.levelData.GetDifficultyAt(i);
        }
        return null;
    }

    void EnsureCurrentDifficulty() {
        MapDifficultyData selected = GetCurrentDifficulty();
        if (selected != null) {
            currentDifficultyId = selected.difficultyId;
            return;
        }
        currentDifficultyId = string.Empty;
    }

    int FindDifficultyIndex(MapData map, string difficultyId) {
        if (map == null || map.levelData == null || string.IsNullOrEmpty(difficultyId) || map.levelData.difficultyLevels == null) return -1;
        for (int i = 0; i < map.levelData.difficultyLevels.Count; i++) {
            MapDifficultyData difficulty = map.levelData.GetDifficultyAt(i);
            if (difficulty != null && difficulty.difficultyId == difficultyId) return i;
        }
        return -1;
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
        return SessionSettlementMath.CalculateRepairCost(currentHealth, maxHealth, died, GetVehicleValue(),
            repairCostPerHP, repairValueRate);
    }

    /// <summary>
    /// Closes a session: banks the kept earnings, charges repairs, applies signed courier rating,
    /// settles the day's rent if this was its last shift, and writes the result to disk.
    /// </summary>
    /// <param name="sessionEarnings">Money earned during the session, not yet banked.</param>
    /// <param name="currentHealth">Health remaining at the end.</param>
    /// <param name="maxHealth">The vehicle's maximum health.</param>
    /// <param name="reason">How the session ended, which decides how much of the earnings survive.</param>
    /// <param name="ordersCompleted">Orders fully delivered this shift.</param>
    /// <param name="ordersOffered">Customers that appeared this shift.</param>
    /// <param name="missedCustomers">Customers that timed out this shift.</param>
    /// <param name="activeSessionSeconds">Unpaused seconds spent in the shift.</param>
    /// <param name="deliveredPizzas">Pizzas delivered during the shift.</param>
    /// <param name="score">Score produced during the shift.</param>
    /// <param name="freeplay">True when this is an endless session and career day state must not change.</param>
    /// <returns>A breakdown of the settlement for the result screen.</returns>
    public SessionResult SettleSession(int sessionEarnings, float currentHealth, float maxHealth, EndReason reason, int ordersCompleted, int ordersOffered, int missedCustomers, float activeSessionSeconds, int deliveredPizzas = 0, int score = 0, bool freeplay = false) {
        if (ActiveSession != null) freeplay = ActiveSession.Draft.isFreeplay;
        bool wasFinalShift = !freeplay && IsFinalShift;
        int bankBefore = totalMoney;

        int kept = SessionSettlementMath.CalculateKeptEarnings(sessionEarnings, reason, deathEarningsKeep);

        // An interrupted shift is billed like a wreck: the vehicle was left in the street, so the
        // full health bar is charged. The bank safety clamp below still caps the damage, which is
        // what stops a genuine crash from ever being ruinous.
        bool died = (reason == EndReason.Wrecked || reason == EndReason.Interrupted);
        int repairRaw = CalculateRepairCost(currentHealth, maxHealth, died);

        int repair = SessionSettlementMath.ClampRepairCost(repairRaw, kept, bankBefore, repairBankSafetyRate);

        totalMoney = SessionSettlementMath.CalculateBankAfter(bankBefore, kept, repair);

        // RawScore is ScoreHandler's plain event accumulation (the "score" parameter); this is the
        // single authoritative place that multiplies it by the selected modifiers' combined
        // coefficient. HUD, the result screen, best-score records, and difficulty tier score all
        // consume the resulting FinalScore, never the raw number, so they can never disagree.
        var appliedModifiers = ActiveSession != null ? ActiveSession.Modifiers : new FrozenModifierRules(GetSelectedModifiers());
        float combinedScoreMultiplier = appliedModifiers.ScoreMultiplier;
        int finalScore = ModifierScoreRules.ComputeFinalScore(score, combinedScoreMultiplier);
        var appliedModifierIds = new string[appliedModifiers.Ids.Count];
        for (int i = 0; i < appliedModifierIds.Length; i++) appliedModifierIds[i] = appliedModifiers.Ids[i];
        int duration = ActiveSession != null ? ActiveSession.Draft.shiftDurationMinutes : SelectedShiftDurationMinutes;

        var result = new SessionResult {
            reason = reason,
            grossEarnings = sessionEarnings,
            keptEarnings = kept,
            repairCost = repair,
            repairBeforeClamp = repairRaw,
            bankBefore = bankBefore,
            bankAfter = totalMoney,
            rankAfter = CurrentRank,
            isFreeplay = freeplay,
            isFinalShift = wasFinalShift,
            shiftDurationMinutes = duration,
            competitiveEligibility = EvaluateCompetitiveEligibility(duration, reason, freeplay),
            rawScore = score,
            scoreMultiplier = combinedScoreMultiplier,
            finalScore = finalScore,
            appliedModifierIds = appliedModifierIds
        };

        // Rent is charged inside this same call, atomically with the day's last shift, rather than
        // as a separate check the garage could run later. Rent affects currency only and never rating.
        if (!freeplay && Career != null) {
            int rankBefore = CurrentRank;
            float healthRetainedRatio = maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
            int ratingDelta = Career.ComputeRatingDelta(activeSessionSeconds, ordersCompleted, ordersOffered,
                missedCustomers, healthRetainedRatio, reason);
            totalReputation = Mathf.Max(0, totalReputation + ratingDelta);

            int rankAfter = Career.ComputeRank(totalReputation, out _);
            if (rankAfter > highestRankAchieved) highestRankAchieved = rankAfter;
            if (rankAfter > rankBefore) PlaySfx(rankUpClip);

            result.ratingDelta = ratingDelta;
            result.rankAfter = rankAfter;

            shiftsCompletedToday++;
            if (shiftsCompletedToday >= careerData.shiftsPerDay) {
                var rent = Career.ComputeRentSettlement(rankAfter, lastRentChargeRank, everPaidRent, totalMoney);
                totalMoney = rent.bankAfter;
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

        bool finalShiftSucceeded = wasFinalShift &&
            (reason == EndReason.TimeUp || reason == EndReason.Extracted) &&
            ordersCompleted >= FinalShiftOrderTarget;
        if (finalShiftSucceeded) {
            careerCompleted = true;
            reachedEnding = true;
        }

        RecordSessionStatistics(freeplay, deliveredPizzas, ordersCompleted, finalScore);
        RecordDifficultyScore(finalScore, reason);
        result.careerCompleted = careerCompleted;
        result.finalShiftSucceeded = finalShiftSucceeded;
        result.personalBestScore = !freeplay && finalScore > 0 && finalScore >= bestShiftScore;
        result.personalBestDeliveries = !freeplay && deliveredPizzas > 0 && deliveredPizzas >= bestShiftDeliveries;
        result.personalBestFreeplayDeliveries = freeplay && deliveredPizzas > 0 && deliveredPizzas >= bestFreeplayDeliveries;
        result.bankAfter = totalMoney;

        // The shift is over however it ended, so the interrupted-shift flag is cleared here rather
        // than on any one ending path.
        HasInterruptedShift = false;
        isFreeplayMode = false;
        SaveGame();

        return result;
    }

    void RecordSessionStatistics(bool freeplay, int deliveredPizzas, int ordersCompleted, int score) {
        int safeDelivered = Mathf.Max(0, deliveredPizzas);
        int safeOrders = Mathf.Max(0, ordersCompleted);
        int safeScore = Mathf.Max(0, score);

        if (freeplay) {
            bestFreeplayDeliveries = Mathf.Max(bestFreeplayDeliveries, safeDelivered);
            return;
        }

        totalShiftsSettled++;
        totalOrdersCompleted += safeOrders;
        totalPizzasDelivered += safeDelivered;
        bestShiftScore = Mathf.Max(bestShiftScore, safeScore);
        bestShiftDeliveries = Mathf.Max(bestShiftDeliveries, safeDelivered);
    }
}
