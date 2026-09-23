using System;
using System.Collections.Generic;

/// <summary>
/// Describes the navigation module portion of the future creator-owned content manifest. This is
/// deliberately not a second full-map package manifest: it identifies one detached navigation
/// document and its typed external dependencies, while the creator manifest remains the owner of
/// package identity, provenance and future scene/content modules.
/// </summary>
[Serializable]
public sealed class NavigationModuleManifest {
    /// <summary>Current navigation module schema. Older versions have no migration path.</summary>
    public int schemaVersion = 1;
    /// <summary>Stable map identity shared with the future creator manifest.</summary>
    public string mapId;
    /// <summary>Stable identity of the detached navigation document.</summary>
    public string documentId;
    /// <summary>SHA-256 of the canonical navigation document UTF-8 bytes.</summary>
    public string canonicalHash;
    /// <summary>Typed dependency fingerprints resolved before materialization.</summary>
    public List<NavigationDependency> dependencies = new List<NavigationDependency>();
}

/// <summary>One external, typed dependency fingerprint referenced by a navigation module.</summary>
[Serializable]
public sealed class NavigationDependency {
    /// <summary>Stable dependency id, normally namespaced for package-owned content.</summary>
    public string dependencyId;
    /// <summary>Dependency kind, such as vehicle profile or police tuning.</summary>
    public string dependencyKind;
    /// <summary>Expected SHA-256 of the dependency data, never a prefab path.</summary>
    public string contentHash;
}
