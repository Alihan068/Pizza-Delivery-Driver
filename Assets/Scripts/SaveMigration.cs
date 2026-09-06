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
        MigrateV0ToV1
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
}
