using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Reads and writes career profiles on disk. Owns the slot layout, atomic writes, backups and the
/// handling of files that cannot be parsed.
/// </summary>
/// <remarks>
/// <para>
/// Writes never truncate the live file. A profile is written to a temporary file first and then
/// swapped in, so losing power mid-write costs the new state, never the old one. The swap also
/// produces the backup for free.
/// </para>
/// <para>
/// A profile that fails to parse is never overwritten or deleted. The backup is tried first, and
/// if that fails too the files are renamed aside so the player can be told their profile is damaged
/// instead of quietly being handed a new game.
/// </para>
/// </remarks>
public class SaveSlotService {

    readonly GameConfig config;
    readonly Func<string, string> resolveVehicleIdFromName;
    readonly string rootPath;
    readonly bool isolatedProfile;

    /// <summary>Creates the service.</summary>
    /// <param name="config">Supplies slot count and file naming. Required.</param>
    /// <param name="resolveVehicleIdFromName">Maps legacy display names to permanent ids during migration.</param>
    public SaveSlotService(GameConfig config, Func<string, string> resolveVehicleIdFromName) {
        this.config = config;
        this.resolveVehicleIdFromName = resolveVehicleIdFromName;
        rootPath = Application.persistentDataPath;
    }

    /// <summary>Creates a save service scoped to one validated S12 development profile.</summary>
    /// <param name="config">Supplies slot count and file naming. Required.</param>
    /// <param name="resolveVehicleIdFromName">Maps legacy display names during migration.</param>
    /// <param name="profile">Validated profile resolution; inactive means production storage.</param>
    public SaveSlotService(GameConfig config, Func<string, string> resolveVehicleIdFromName,
                           DevelopmentTestProfile profile)
        : this(config, resolveVehicleIdFromName, profile, null) {
    }

    /// <summary>Test-scoped overload allowing a temporary containment root without global state.</summary>
    /// <param name="config">Supplies slot count and file naming. Required.</param>
    /// <param name="resolveVehicleIdFromName">Maps legacy display names during migration.</param>
    /// <param name="profile">Validated profile resolution; it must be active and valid.</param>
    /// <param name="validationRootOverride">Temporary root used only to validate the injected profile.</param>
    public SaveSlotService(GameConfig config, Func<string, string> resolveVehicleIdFromName,
                           DevelopmentTestProfile profile, string validationRootOverride) {
        this.config = config;
        this.resolveVehicleIdFromName = resolveVehicleIdFromName;
        isolatedProfile = profile.IsRequested && profile.IsValid;
        if (profile.IsRequested && !profile.IsValid)
            throw new InvalidOperationException("Requested S12 profile is invalid.");

        rootPath = isolatedProfile ? profile.RootPath : Application.persistentDataPath;
        string containmentRoot = string.IsNullOrEmpty(validationRootOverride)
            ? Application.persistentDataPath : validationRootOverride;
        if (isolatedProfile && !DevelopmentTestProfile.IsContainedPath(containmentRoot, rootPath))
            throw new InvalidOperationException("S12 profile root escaped its containment root.");
    }

    /// <summary>How many profile slots exist. Comes from configuration, never assumed.</summary>
    public int SlotCount => config != null ? Mathf.Max(1, config.saveSlotCount) : 1;

    /// <summary>Full path of a slot's file.</summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <returns>An absolute path inside the persistent data folder.</returns>
    public string GetPath(int slotIndex) {
        if (!isolatedProfile)
            return Path.Combine(Application.persistentDataPath, config.GetSaveFileName(slotIndex));

        if (config == null) throw new InvalidOperationException("Save configuration is missing.");
        string fileName = config.GetSaveFileName(slotIndex);
        if (string.IsNullOrEmpty(fileName) || Path.IsPathRooted(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            throw new InvalidOperationException("S12 save file name must be a leaf file name.");

        string path = Path.GetFullPath(Path.Combine(rootPath, fileName));
        if (!DevelopmentTestProfile.IsContainedPath(rootPath, path))
            throw new InvalidOperationException("S12 save path escaped its scoped root.");
        return path;
    }

    string GetBackupPath(int slotIndex) {
        return GetPath(slotIndex) + config.backupSuffix;
    }

    string GetCorruptPath(int slotIndex) {
        return GetPath(slotIndex) + string.Format(config.corruptSuffix, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    }

    /// <summary>Whether a slot currently holds a file.</summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <returns>True when a profile file exists, readable or not.</returns>
    public bool Exists(int slotIndex) {
        return File.Exists(GetPath(slotIndex));
    }

    // ------------------------------------------------------------------ read

    /// <summary>
    /// Reads a profile, falling back to its backup and migrating it forward when needed.
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <param name="status">How the read turned out.</param>
    /// <returns>The profile, or null when the slot is empty or damaged.</returns>
    public GameSaveData Load(int slotIndex, out SaveLoadStatus status) {
        string path = GetPath(slotIndex);
        if (!File.Exists(path)) {
            status = SaveLoadStatus.Empty;
            return null;
        }

        var data = TryRead(path);
        if (data != null) {
            status = SaveLoadStatus.Loaded;
            MigrateAndPersist(slotIndex, data);
            return data;
        }

        string backup = GetBackupPath(slotIndex);
        if (File.Exists(backup)) {
            data = TryRead(backup);
            if (data != null) {
                Debug.LogWarning("[Save] Slot " + slotIndex + " was unreadable; recovered from its backup.");
                status = SaveLoadStatus.RecoveredFromBackup;
                MigrateAndPersist(slotIndex, data);
                return data;
            }
        }

        SetAside(slotIndex);
        status = SaveLoadStatus.Corrupt;
        return null;
    }

    // An upgraded save is written back straight away. Migrating only in memory would leave the old
    // format on disk until the player happens to make a transaction, so a profile that is loaded
    // and then quit would be migrated again on every launch.
    void MigrateAndPersist(int slotIndex, GameSaveData data) {
        if (!SaveMigration.Migrate(data, resolveVehicleIdFromName)) return;
        Save(slotIndex, data);
    }

    // A file that parses but carries nothing is not a valid zeroed career, it is a broken file.
    // Treating it as valid would hand the player 0 money and no vehicles with no explanation.
    GameSaveData TryRead(string path) {
        try {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var data = JsonUtility.FromJson<GameSaveData>(json);
            if (data == null) return null;

            bool hasVehicleRecords = data.vehicleSaveList != null && data.vehicleSaveList.Count > 0;
            bool namesAnyVehicle = !string.IsNullOrEmpty(data.currentVehicleId) || !string.IsNullOrEmpty(data.currentVehicleName);
            if (!hasVehicleRecords && !namesAnyVehicle) return null;

            if (data.vehicleSaveList == null) data.vehicleSaveList = new List<VehicleSaveData>();
            return data;
        }
        catch (Exception e) {
            Debug.LogWarning("[Save] Could not read '" + Path.GetFileName(path) + "': " + e.Message);
            return null;
        }
    }

    void SetAside(int slotIndex) {
        try {
            string corrupt = GetCorruptPath(slotIndex);
            File.Move(GetPath(slotIndex), corrupt);
            string backup = GetBackupPath(slotIndex);
            if (File.Exists(backup)) File.Move(backup, corrupt + config.backupSuffix);
            Debug.LogError("[Save] Slot " + slotIndex + " could not be read and was set aside as '" +
                           Path.GetFileName(corrupt) + "'. Nothing was deleted.");
        }
        catch (Exception e) {
            Debug.LogError("[Save] Slot " + slotIndex + " is damaged and could not be set aside: " + e.Message);
        }
    }

    // ----------------------------------------------------------------- write

    /// <summary>
    /// Writes a profile atomically, keeping the previous contents as its backup.
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <param name="data">The profile to write. Its version is stamped to the current schema.</param>
    /// <returns>True when the write completed.</returns>
    public bool Save(int slotIndex, GameSaveData data) {
        if (data == null) return false;
        data.saveVersion = SaveMigration.CurrentVersion;

        string path = GetPath(slotIndex);
        string temp = path + ".tmp";
        try {
            if (isolatedProfile) {
                Directory.CreateDirectory(rootPath);
                if (!DevelopmentTestProfile.IsContainedPath(rootPath, temp)) return false;
            }
            File.WriteAllText(temp, JsonUtility.ToJson(data, true));

            // File.Replace is atomic on NTFS and produces the backup in the same operation, but it
            // requires an existing destination, so a first-time write is a plain move.
            if (File.Exists(path)) File.Replace(temp, path, GetBackupPath(slotIndex));
            else File.Move(temp, path);
            return true;
        }
        catch (Exception e) {
            Debug.LogWarning("[Save] Failed to write slot " + slotIndex + ": " + e.Message);
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return false;
        }
    }

    /// <summary>Deletes a profile and its backup.</summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <returns>True when nothing is left in the slot afterwards.</returns>
    public bool Delete(int slotIndex) {
        try {
            string path = GetPath(slotIndex);
            if (File.Exists(path)) File.Delete(path);
            string backup = GetBackupPath(slotIndex);
            if (File.Exists(backup)) File.Delete(backup);
            return true;
        }
        catch (Exception e) {
            Debug.LogWarning("[Save] Failed to delete slot " + slotIndex + ": " + e.Message);
            return false;
        }
    }

    /// <summary>Copies one profile over another, replacing whatever the target held.</summary>
    /// <param name="fromSlot">Slot to copy from.</param>
    /// <param name="toSlot">Slot to copy onto.</param>
    /// <returns>True when the copy completed.</returns>
    public bool Copy(int fromSlot, int toSlot) {
        if (fromSlot == toSlot) return false;
        var data = Load(fromSlot, out var status);
        if (data == null || status == SaveLoadStatus.Corrupt) return false;
        return Save(toSlot, data);
    }

    // ------------------------------------------------------------------ info

    /// <summary>
    /// Reads just enough of a slot to draw its card, without disturbing the running game.
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <param name="registry">Used to turn stored ids back into display names.</param>
    /// <returns>A summary; never null.</returns>
    public SaveSlotInfo Peek(int slotIndex, ContentRegistry registry) {
        var info = new SaveSlotInfo { slotIndex = slotIndex };
        string path = GetPath(slotIndex);
        if (!File.Exists(path)) {
            info.status = SaveLoadStatus.Empty;
            return info;
        }

        info.lastPlayedUtc = File.GetLastWriteTimeUtc(path);

        var data = TryRead(path);
        if (data == null) {
            string backup = GetBackupPath(slotIndex);
            data = File.Exists(backup) ? TryRead(backup) : null;
            info.status = data != null ? SaveLoadStatus.RecoveredFromBackup : SaveLoadStatus.Corrupt;
        }
        else {
            info.status = SaveLoadStatus.Loaded;
        }
        if (data == null) return info;

        SaveMigration.Migrate(data, resolveVehicleIdFromName);
        info.totalMoney = data.totalMoney;

        var vehicle = registry != null ? registry.GetVehicle(data.currentVehicleId) : null;
        if (vehicle != null) info.vehicleDisplayName = vehicle.GetDisplayName();
        else info.hasMissingContent = true;

        return info;
    }

    // ---------------------------------------------------------------- legacy

    /// <summary>
    /// Moves a pre-slot <c>save.json</c> into a slot the first time this build runs, so progress
    /// made before profiles existed is not stranded.
    /// </summary>
    /// <param name="legacyFileName">Name of the old single save file.</param>
    /// <param name="targetSlot">Slot the old save becomes.</param>
    /// <returns>True when a legacy save was adopted.</returns>
    public bool AdoptLegacySave(string legacyFileName, int targetSlot) {
        if (isolatedProfile) return false;
        string legacy = Path.Combine(Application.persistentDataPath, legacyFileName);
        if (!File.Exists(legacy)) return false;
        if (Exists(targetSlot)) return false;

        try {
            File.Copy(legacy, GetPath(targetSlot));
            Debug.Log("[Save] Adopted legacy save into slot " + targetSlot + ".");
            return true;
        }
        catch (Exception e) {
            Debug.LogWarning("[Save] Could not adopt legacy save: " + e.Message);
            return false;
        }
    }
}
