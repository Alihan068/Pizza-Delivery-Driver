using System.Collections.Generic;
using UnityEngine;

/// <summary>Typed police visual binding table used by preflight before any police body wakes.</summary>
[CreateAssetMenu(fileName = "PoliceVehiclePrefabCatalog", menuName = "PizzaGame/Traffic/Police Vehicle Prefab Catalog")]
public sealed class PoliceVehiclePrefabCatalog : ScriptableObject {
    /// <summary>One stable visual id to police prefab binding.</summary>
    [System.Serializable]
    public sealed class Entry {
        /// <summary>Profile visualCatalogId resolved by this entry.</summary>
        public string visualCatalogId;
        /// <summary>Authored police body prefab.</summary>
        public PoliceVehicleBody prefab;
    }

    /// <summary>Authored visual bindings. Duplicate ids are rejected by validation.</summary>
    public List<Entry> entries = new List<Entry>();

    /// <summary>Resolves exactly one authored police prefab without relying on Awake.</summary>
    public bool TryResolve(string visualCatalogId, out PoliceVehicleBody prefab) {
        prefab = null;
        if (entries == null || string.IsNullOrWhiteSpace(visualCatalogId)) return false;
        foreach (var entry in entries) {
            if (entry == null || entry.visualCatalogId != visualCatalogId) continue;
            if (prefab != null || entry.prefab == null) { prefab = null; return false; }
            prefab = entry.prefab;
        }
        return prefab != null;
    }
}
