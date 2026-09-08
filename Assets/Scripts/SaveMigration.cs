using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Brings a save file forward from the schema version it was written with to the current one.
/// </summary>
/// <remarks>
/// <para>
/// Steps are held in an ordered list rather than a growing switch: <c>steps[n]</c> upgrades a file
/// from version <c>n</c> to <c>n + 1</c>, and <see cref="CurrentVersion"/> is simply how many steps
/// exist. Adding a version means appending one method, never editing the loop.
/// </para>
/// <para>
/// Version 0 is every file written before versioning existed. Those keyed vehicles by display name,
/// so migrating them needs a name-to-id lookup supplied by the caller.
/// </para>
/// </remarks>
public static class SaveMigration {

    /// <summary>Upgrades a save from one version to the next, in place.</summary>
    /// <param name="data">The save being upgraded.</param>
    /// <param name="resolveVehicleIdFromName">Maps a legacy display name to a permanent vehicle id.</param>
    /// <param name="notes">Collects human readable notes about what the step changed.</param>
    delegate void MigrationStep(GameSaveData data, Func<string, string> resolveVehicleIdFromName, List<string> notes);

    // Index is the version being upgraded FROM. steps[0] turns a v0 file into a v1 file.
    static readonly MigrationStep[] steps = {
        MigrateV0ToV1,
        MigrateV1ToV2,
        MigrateV2ToV3,
        MigrateV3ToV4,
        MigrateV4ToV5,
        MigrateV5ToV6,
        MigrateV6ToV7
    };

    /// <summary>The schema version this build writes.</summary>
    public static int CurrentVersion => steps.Length;

    /// <summary>
    /// Upgrades a save to <see cref="CurrentVersion"/>, applying every intermediate step in order.
    /// </summary>
    /// <param name="data">The save to upgrade. Ignored when null.</param>
    /// <param name="resolveVehicleIdFromName">Maps a legacy vehicle display name to its permanent id.</param>
    /// <returns>True when the save was changed, false when it was already current.</returns>
    public static bool Migrate(GameSaveData data, Func<string, string> resolveVehicleIdFromName) {
        if (data == null) return false;
        if (data.saveVersion >= CurrentVersion) return false;

        var notes = new List<string>();
        int from = Mathf.Clamp(data.saveVersion, 0, CurrentVersion);

        while (data.saveVersion < CurrentVersion) {
            steps[data.saveVersion](data, resolveVehicleIdFromName, notes);
            data.saveVersion++;
        }

        Debug.Log("[Save] Migrated profile from version " + from + " to " + CurrentVersion +
                  (notes.Count > 0 ? ": " + string.Join("; ", notes) : "."));
        return true;
    }

    // v0 keyed everything by display name. v1 keys by permanent id, so every name is resolved once
    // here and the legacy fields stop being read afterwards. A name that no longer matches any
    // installed vehicle leaves an empty id, which the loader reports rather than silently dropping.
    static void MigrateV0ToV1(GameSaveData data, Func<string, string> resolveVehicleIdFromName, List<string> notes) {
        if (resolveVehicleIdFromName == null) return;

        if (string.IsNullOrEmpty(data.currentVehicleId) && !string.IsNullOrEmpty(data.currentVehicleName)) {
            data.currentVehicleId = resolveVehicleIdFromName(data.currentVehicleName);
            notes.Add("current vehicle '" + data.currentVehicleName + "' -> id '" + data.currentVehicleId + "'");
        }

        if (data.vehicleSaveList == null) return;
        int resolved = 0;
        int unresolved = 0;
        foreach (var record in data.vehicleSaveList) {
            if (record == null || !string.IsNullOrEmpty(record.vehicleId)) continue;
            record.vehicleId = resolveVehicleIdFromName(record.vehicleName);
            if (string.IsNullOrEmpty(record.vehicleId)) unresolved++;
            else resolved++;
        }
        notes.Add(resolved + " vehicle record(s) keyed by id" + (unresolved > 0 ? ", " + unresolved + " unresolved" : ""));
    }

    // v1 stored Capacity and Protection per vehicle, resetting when the player switched. v2 makes
    // them the driver's own permanent stats. The highest level already bought on any one vehicle
    // carries over, so migrating never undoes a purchase; the old per-vehicle fields are left in
    // place (unread from here on) rather than cleared, matching how v0's name fields were handled.
    static void MigrateV1ToV2(GameSaveData data, Func<string, string> resolveVehicleIdFromName, List<string> notes) {
        if (data.driverStats == null) data.driverStats = new DriverSaveData();
        if (data.vehicleSaveList == null) return;

        int capacity = data.driverStats.capacityLevel;
        int protection = data.driverStats.protectionLevel;
        foreach (var record in data.vehicleSaveList) {
            if (record == null) continue;
            capacity = Mathf.Max(capacity, record.capacityLevel);
            protection = Mathf.Max(protection, record.protectionLevel);
        }
        data.driverStats.capacityLevel = capacity;
        data.driverStats.protectionLevel = protection;
        notes.Add("Storage/Stabilizer moved to driver-owned stats (Storage lvl " + capacity + ", Stabilizer lvl " + protection + ")");
    }

    // v2 profiles predate the career layer entirely. A profile that already has upgrade progress
    // did not earn it through this new system, so it starts at rank 1 with no reputation rather
    // than something back-calculated from money spent - there is no honest way to infer "how much
    // career" a v2 save represents, and starting at zero costs nothing but a fresh rent clock.
    static void MigrateV2ToV3(GameSaveData data, Func<string, string> resolveVehicleIdFromName, List<string> notes) {
        data.totalReputation = 0;
        data.highestRankAchieved = 1;
        data.highestUnlockedRegionTier = 1;
        data.currentDay = 1;
        data.shiftsCompletedToday = 0;
        data.everPaidRent = false;
        data.lastRentChargeRank = 1;
        data.reachedEnding = false;
        notes.Add("career layer initialized at rank 1, day 1");
    }

    // v3 profiles had a selected map but no persistent ownership list. The selected map is treated
    // as owned so migration never takes away content the player had already been using. New careers
    // receive their starter map in GameManager.InitializeMaps.
    static void MigrateV3ToV4(GameSaveData data, Func<string, string> resolveVehicleIdFromName, List<string> notes) {
        if (data.ownedMapIds == null) data.ownedMapIds = new List<string>();
        if (!string.IsNullOrEmpty(data.currentMapId) && !data.ownedMapIds.Contains(data.currentMapId)) {
            data.ownedMapIds.Add(data.currentMapId);
            notes.Add("selected map marked as owned");
        }
    }

    // v4 profiles predate career records and the explicit final-shift completion flag. The new
    // fields intentionally start at zero; no honest personal-best value can be reconstructed from
    // the old profile because old sessions did not persist their metrics.
    static void MigrateV4ToV5(GameSaveData data, Func<string, string> resolveVehicleIdFromName, List<string> notes) {
        data.careerCompleted = false;
        data.totalShiftsSettled = Mathf.Max(0, data.totalShiftsSettled);
        data.totalOrdersCompleted = Mathf.Max(0, data.totalOrdersCompleted);
        data.totalPizzasDelivered = Mathf.Max(0, data.totalPizzasDelivered);
        data.bestShiftScore = Mathf.Max(0, data.bestShiftScore);
        data.bestShiftDeliveries = Mathf.Max(0, data.bestShiftDeliveries);
        data.bestFreeplayDeliveries = Mathf.Max(0, data.bestFreeplayDeliveries);
        notes.Add("career record fields initialized");
    }

    // v5 profiles predate map-local difficulty progress. Existing players start on the first
    // authored tier and earn later tiers from new scores; no old score has enough context to be
    // assigned to an arbitrary map difficulty honestly.
    static void MigrateV5ToV6(GameSaveData data, Func<string, string> resolveVehicleIdFromName, List<string> notes) {
        if (data.mapDifficultyProgress == null) data.mapDifficultyProgress = new List<MapDifficultyProgress>();
        data.currentDifficultyId = string.Empty;
        notes.Add("map difficulty progress initialized");
    }

    // v6 profiles predate Advanced Tuning. New tuning fields use an explicit opt-in flag, so old
    // profiles safely retain Classic behavior until the player saves a custom snapshot.
    static void MigrateV6ToV7(GameSaveData data, Func<string, string> resolveVehicleIdFromName, List<string> notes) {
        if (data.vehicleSaveList == null) {
            notes.Add("Advanced Tuning save fields initialized");
            return;
        }

        foreach (var record in data.vehicleSaveList) {
            if (record == null) continue;
            record.hasCustomTuning = false;
            record.tunedSpeed = 0f;
            record.tunedDriftGrip = 0f;
            record.tunedDriftSteeringMultiplier = 0f;
            record.tunedGripEnterTime = 0f;
        }
        notes.Add("Advanced Tuning save fields initialized");
    }
}
