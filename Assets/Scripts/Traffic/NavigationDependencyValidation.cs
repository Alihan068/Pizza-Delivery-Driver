using System;
using System.Collections.Generic;

/// <summary>
/// Shared static authority for validating typed navigation dependencies before package validation or
/// staging. It validates the detached document's required ids, every declared fingerprint, and the
/// authoritative catalog result without loading assets or mutating active state.
/// </summary>
public static class NavigationDependencyValidation {
    /// <summary>Fingerprint kind for entries referenced by vehicle pools.</summary>
    public const string VehicleProfileKind = "vehicleProfile";
    /// <summary>Fingerprint kind for difficulty civilian population profiles.</summary>
    public const string PopulationProfileKind = "populationProfile";
    /// <summary>Fingerprint kind for difficulty police director profiles.</summary>
    public const string PoliceDirectorProfileKind = "policeDirectorProfile";
    /// <summary>Fingerprint kind for non-empty route respawn profiles.</summary>
    public const string RespawnProfileKind = "respawnProfile";

    /// <summary>
    /// Validates every declared fingerprint and every typed id required by a detached document.
    /// Duplicate ids are rejected even when their declared kinds differ, preventing ambiguous
    /// explicit profileDependencies resolution.
    /// </summary>
    /// <param name="document">Decoded detached navigation document.</param>
    /// <param name="manifest">Manifest containing declared typed fingerprints.</param>
    /// <param name="catalog">Authoritative exact-id vehicle catalog.</param>
    /// <param name="resolver">Authoritative fingerprint resolver for all declared dependencies.</param>
    /// <param name="limits">Existing bounded traffic validation limits.</param>
    /// <param name="reason">Stable failure reason when validation fails.</param>
    /// <returns>True only when every declared and required dependency is valid.</returns>
    public static bool TryValidate(MapNavigationDocument document, NavigationModuleManifest manifest,
                                   ITrafficProfileCatalog catalog,
                                   ITrafficDependencyFingerprintResolver resolver,
                                   TrafficValidationLimits limits, out string reason) {
        reason = null;
        limits = limits ?? new TrafficValidationLimits();
        if (document == null || manifest == null || catalog == null || resolver == null)
            return Fail("Navigation dependency inputs are missing.", out reason);
        if (catalog is PackageTrafficProfileCatalog packageCatalog && packageCatalog.HasConflicts)
            return Fail("Traffic profile catalog contains conflicts.", out reason);
        if (manifest.dependencies == null || manifest.dependencies.Count > limits.maxTotalRecords)
            return Fail("Navigation dependency manifest is missing or oversized.", out reason);
        if (document.profileDependencies == null || document.vehiclePools == null ||
            document.civilianRoutes == null || document.difficultyProfileBindings == null)
            return Fail("Navigation dependency collections are missing.", out reason);

        var declared = new Dictionary<string, NavigationDependency>(StringComparer.Ordinal);
        foreach (NavigationDependency dependency in manifest.dependencies) {
            if (dependency == null || !CheckString(dependency.dependencyId, limits) ||
                !CheckString(dependency.dependencyKind, limits) ||
                !CheckString(dependency.contentHash, limits) ||
                !IsKnownKind(dependency.dependencyKind) ||
                !declared.TryAdd(dependency.dependencyId, dependency))
                return Fail("Navigation dependency is malformed, unknown, or duplicated.", out reason);

            if (!resolver.TryResolveDependencyHash(dependency.dependencyId, dependency.dependencyKind,
                    out string actualHash) ||
                string.IsNullOrEmpty(actualHash) ||
                !string.Equals(actualHash, dependency.contentHash, StringComparison.OrdinalIgnoreCase))
                return Fail("Navigation dependency fingerprint mismatch: " + dependency.dependencyId, out reason);
        }

        var required = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string dependencyId in document.profileDependencies) {
            if (!CheckString(dependencyId, limits) ||
                !declared.TryGetValue(dependencyId, out NavigationDependency dependency))
                return Fail("Explicit navigation profile dependency is missing from the manifest.", out reason);
            if (!AddRequired(required, dependencyId, dependency.dependencyKind, out reason))
                return false;
        }

        foreach (VehiclePoolRecord pool in document.vehiclePools) {
            if (pool == null || pool.entries == null) return Fail("Vehicle pool dependency data is missing.", out reason);
            foreach (VehiclePoolEntry entry in pool.entries) {
                if (entry == null || !CheckString(entry.vehicleProfileId, limits) ||
                    !AddRequired(required, entry.vehicleProfileId, VehicleProfileKind, out reason))
                    return false;
            }
        }

        foreach (DifficultyTrafficBinding binding in document.difficultyProfileBindings) {
            if (binding == null || !CheckString(binding.difficultyId, limits))
                return Fail("Difficulty dependency binding is malformed.", out reason);
            if (!string.IsNullOrEmpty(binding.civilianPopulationProfileId) &&
                (!CheckString(binding.civilianPopulationProfileId, limits) ||
                 !AddRequired(required, binding.civilianPopulationProfileId, PopulationProfileKind, out reason)))
                return false;
            if (!string.IsNullOrEmpty(binding.policeDirectorProfileId) &&
                (!CheckString(binding.policeDirectorProfileId, limits) ||
                 !AddRequired(required, binding.policeDirectorProfileId, PoliceDirectorProfileKind, out reason)))
                return false;
        }

        foreach (CivilianRouteRecord route in document.civilianRoutes) {
            if (route == null) return Fail("Route dependency data is missing.", out reason);
            if (!string.IsNullOrEmpty(route.respawnProfileId) &&
                (!CheckString(route.respawnProfileId, limits) ||
                 !AddRequired(required, route.respawnProfileId, RespawnProfileKind, out reason)))
                return false;
        }

        foreach (KeyValuePair<string, string> requirement in required) {
            if (!declared.TryGetValue(requirement.Key, out NavigationDependency dependency) ||
                !string.Equals(dependency.dependencyKind, requirement.Value, StringComparison.Ordinal))
                return Fail("Required navigation dependency kind mismatch: " + requirement.Key, out reason);
            if (requirement.Value == VehicleProfileKind &&
                catalog.ResolveVehicleProfile(requirement.Key) == null)
                return Fail("Required vehicle profile is unresolved: " + requirement.Key, out reason);
        }

        return true;
    }

    static bool AddRequired(Dictionary<string, string> required, string id, string kind, out string reason) {
        if (!IsKnownKind(kind)) return Fail("Unknown navigation dependency kind: " + kind, out reason);
        if (required.TryGetValue(id, out string existingKind) &&
            !string.Equals(existingKind, kind, StringComparison.Ordinal))
            return Fail("Navigation dependency id has ambiguous kinds: " + id, out reason);
        required[id] = kind;
        reason = null;
        return true;
    }

    static bool IsKnownKind(string kind) {
        return string.Equals(kind, VehicleProfileKind, StringComparison.Ordinal) ||
               string.Equals(kind, PopulationProfileKind, StringComparison.Ordinal) ||
               string.Equals(kind, PoliceDirectorProfileKind, StringComparison.Ordinal) ||
               string.Equals(kind, RespawnProfileKind, StringComparison.Ordinal);
    }

    static bool CheckString(string value, TrafficValidationLimits limits) {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= limits.maxStringLength;
    }

    static bool Fail(string message, out string reason) {
        reason = message;
        return false;
    }
}
