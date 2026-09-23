using System.Collections.Generic;

/// <summary>Typed police wrapper and behavior catalog over the existing shared traffic catalog.</summary>
public sealed class PoliceProfileCatalog {
    readonly ITrafficProfileCatalog sharedCatalog;
    readonly PoliceVehicleProfile[] vehicleProfiles;
    readonly PoliceBehaviorProfile[] behaviorProfiles;

    /// <summary>Creates a police catalog without creating a second shared vehicle registry.</summary>
    public PoliceProfileCatalog(ITrafficProfileCatalog sharedCatalog, PoliceVehicleProfile[] vehicleProfiles, PoliceBehaviorProfile[] behaviorProfiles) {
        this.sharedCatalog = sharedCatalog;
        this.vehicleProfiles = vehicleProfiles;
        this.behaviorProfiles = behaviorProfiles;
    }

    /// <summary>Resolves and validates all police configuration, failing closed on ambiguity.</summary>
    public bool TryValidate(out string reason) {
        reason = null;
        var vehicleIds = new HashSet<string>();
        var behaviorIds = new HashSet<string>();
        if (sharedCatalog == null) return Fail("shared traffic catalog is null", out reason);
        if (vehicleProfiles == null || vehicleProfiles.Length == 0) return Fail("police vehicle catalog is empty", out reason);
        if (behaviorProfiles == null || behaviorProfiles.Length == 0) return Fail("police behavior catalog is empty", out reason);
        foreach (var profile in vehicleProfiles) {
            if (!PolicePreflight.ValidateVehicleProfile(profile, sharedCatalog, out reason)) return false;
            if (!vehicleIds.Add(profile.sharedNpc.vehicleProfileId)) return Fail("duplicate police shared vehicle id", out reason);
        }
        foreach (var behavior in behaviorProfiles) {
            if (!PolicePreflight.ValidateBehaviorProfile(behavior, out reason)) return false;
            if (!behaviorIds.Add(behavior.behaviorProfileId)) return Fail("duplicate police behavior id", out reason);
            bool supported = false;
            foreach (var vehicle in vehicleProfiles)
                if (vehicle.supportedTacticalRoles.Contains(behavior.tacticalRole)) { supported = true; break; }
            if (!supported) return Fail("behavior role is unsupported by every police vehicle", out reason);
        }
        return true;
    }

    /// <summary>Resolves a police wrapper by its shared stable vehicle id.</summary>
    public PoliceVehicleProfile ResolveVehicle(string vehicleProfileId) {
        if (vehicleProfiles == null || string.IsNullOrWhiteSpace(vehicleProfileId)) return null;
        PoliceVehicleProfile result = null;
        foreach (var profile in vehicleProfiles)
            if (profile != null && profile.sharedNpc != null && profile.sharedNpc.vehicleProfileId == vehicleProfileId) { if (result != null) return null; result = profile; }
        return result;
    }

    /// <summary>Resolves a behavior by its stable behavior id.</summary>
    public PoliceBehaviorProfile ResolveBehavior(string behaviorProfileId) {
        if (behaviorProfiles == null || string.IsNullOrWhiteSpace(behaviorProfileId)) return null;
        PoliceBehaviorProfile result = null;
        foreach (var profile in behaviorProfiles)
            if (profile != null && profile.behaviorProfileId == behaviorProfileId) { if (result != null) return null; result = profile; }
        return result;
    }

    /// <summary>Resolves one compatible vehicle, behavior, and unique prefab binding fail-closed.</summary>
    public bool TrySelect(string vehicleProfileId, string behaviorProfileId, PoliceVehiclePrefabCatalog prefabCatalog, out PoliceVehicleProfile vehicle, out PoliceBehaviorProfile behavior, out PoliceVehicleBody body, out string reason) {
        vehicle = ResolveVehicle(vehicleProfileId); behavior = ResolveBehavior(behaviorProfileId); body = null; reason = null;
        if (!TryValidate(out reason)) return false;
        if (vehicle == null || behavior == null) return Fail("vehicle or behavior selection is ambiguous or unresolved", out reason);
        if (!vehicle.supportedTacticalRoles.Contains(behavior.tacticalRole)) return Fail("behavior role is not supported by the selected vehicle", out reason);
        if (prefabCatalog == null || !prefabCatalog.TryResolve(vehicle.sharedNpc.visualCatalogId, out body)) return Fail("selected vehicle visual binding is unresolved or ambiguous", out reason);
        if (!PolicePreflight.ValidatePrefab(body, vehicle.sharedNpc, out reason)) { body = null; return false; }
        return true;
    }

    static bool Fail(string message, out string reason) { reason = message; return false; }
}
