using System;
using System.Collections.Generic;

/// <summary>Resolves an injected, authoritative fingerprint for a typed package dependency.</summary>
public interface ITrafficDependencyFingerprintResolver {
    /// <summary>Returns the actual fingerprint for one injected dependency.</summary>
    bool TryResolveDependencyHash(string dependencyId, string dependencyKind, out string contentHash);
}

/// <summary>
/// Injected profile adapter for a staged navigation package. It resolves only supplied profile
/// assets, requires package-owned ids to be namespaced, and never loads arbitrary prefab paths.
/// Package provenance is intentionally non-competitive even when every dependency resolves.
/// </summary>
public sealed class PackageTrafficProfileCatalog : ITrafficProfileCatalog, ITrafficDependencyFingerprintResolver {
    readonly Dictionary<string, NpcVehicleProfile> profiles = new Dictionary<string, NpcVehicleProfile>(StringComparer.Ordinal);
    readonly HashSet<string> conflicts = new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<string, string> dependencyHashes = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>True when the injected catalog contains package-owned profiles.</summary>
    public bool HasCustomContent { get; private set; }
    /// <summary>Always false for this external adapter; trust must come from the future creator manifest.</summary>
    public bool IsCompetitiveEligible => false;
    /// <summary>Whether duplicate or namespace-conflicting injected ids were found.</summary>
    public bool HasConflicts => conflicts.Count != 0;

    /// <summary>Creates an adapter from already validated built-in and package-injected assets.</summary>
    public PackageTrafficProfileCatalog(NpcVehicleProfile[] builtInProfiles, NpcVehicleProfile[] customProfiles, IEnumerable<NavigationDependency> actualDependencies = null) {
        Add(builtInProfiles, false);
        Add(customProfiles, true);
        if (actualDependencies != null) foreach (var dependency in actualDependencies) {
            if (dependency == null) { conflicts.Add(string.Empty); continue; }
            if (string.IsNullOrEmpty(dependency.dependencyId) || string.IsNullOrEmpty(dependency.dependencyKind) || string.IsNullOrEmpty(dependency.contentHash) || !dependencyHashes.TryAdd(Key(dependency.dependencyId, dependency.dependencyKind), dependency.contentHash)) conflicts.Add(dependency.dependencyId ?? string.Empty);
        }
    }

    /// <summary>Resolves a profile by exact stable id, without asset or prefab discovery.</summary>
    public NpcVehicleProfile ResolveVehicleProfile(string vehicleProfileId) {
        if (string.IsNullOrEmpty(vehicleProfileId)) return null;
        if (conflicts.Contains(vehicleProfileId)) return null;
        profiles.TryGetValue(vehicleProfileId, out var profile);
        return profile;
    }

    /// <summary>Resolves a fingerprint supplied by the package host; it never derives trust locally.</summary>
    public bool TryResolveDependencyHash(string dependencyId, string dependencyKind, out string contentHash) {
        return dependencyHashes.TryGetValue(Key(dependencyId, dependencyKind), out contentHash);
    }

    /// <summary>Validates manifest hashes and every profile dependency used by the detached document.</summary>
    public bool TryValidateDependencies(MapNavigationDocument document, NavigationModuleManifest manifest, out string reason) {
        return NavigationDependencyValidation.TryValidate(document, manifest, this, this,
            new TrafficValidationLimits(), out reason);
    }

    static string Key(string dependencyId, string dependencyKind) => dependencyKind + "\u0000" + dependencyId;

    void Add(NpcVehicleProfile[] values, bool custom) {
        if (values == null) return;
        foreach (var profile in values) {
            if (profile == null || string.IsNullOrEmpty(profile.vehicleProfileId)) { conflicts.Add(string.Empty); continue; }
            bool namespaced = profile.vehicleProfileId.IndexOf(':') > 0;
            if (custom && (!namespaced || profile.vehicleProfileId.StartsWith("builtin:", StringComparison.Ordinal))) { conflicts.Add(profile.vehicleProfileId); continue; }
            if (custom) HasCustomContent = true;
            if (!profiles.TryAdd(profile.vehicleProfileId, profile)) conflicts.Add(profile.vehicleProfileId);
        }
    }
}
