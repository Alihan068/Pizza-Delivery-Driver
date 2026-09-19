using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-authored prefab table implementing <see cref="ICivilianVehicleBodyProvider"/>: maps each
/// visual catalog id to a <see cref="CivilianVehicleBody"/> prefab and keeps released instances
/// per id for reuse. Instantiation is lazy and capped by <see cref="maxInstancesPerVisual"/>; no
/// prefab reference ever leaves the Unity wrapper layer (profiles carry only the id).
/// </summary>
public sealed class PrefabCivilianVehicleBodyProvider : MonoBehaviour, ICivilianVehicleBodyProvider {
    [System.Serializable]
    public class Entry {
        [Tooltip("Visual catalog id referenced by NpcVehicleProfile.visualCatalogId.")]
        public string visualCatalogId;
        public CivilianVehicleBody prefab;
    }

    [SerializeField] List<Entry> entries = new List<Entry>();

    [Tooltip("Upper bound on live instances per visual id; a request beyond it returns null instead of growing unboundedly.")]
    [Min(1)] public int maxInstancesPerVisual = 16;

    readonly Dictionary<string, Entry> entriesById = new Dictionary<string, Entry>();
    readonly Dictionary<string, Stack<CivilianVehicleBody>> freeById = new Dictionary<string, Stack<CivilianVehicleBody>>();
    readonly Dictionary<string, int> createdById = new Dictionary<string, int>();

    void Awake() {
        entriesById.Clear();
        foreach (var entry in entries) {
            if (entry != null && !string.IsNullOrEmpty(entry.visualCatalogId) && entry.prefab != null) entriesById[entry.visualCatalogId] = entry;
        }
    }

    /// <summary>Registers an entry at runtime (tests, generated content). Later entries with the same id replace earlier ones.</summary>
    public void Register(string visualCatalogId, CivilianVehicleBody prefab) {
        if (string.IsNullOrEmpty(visualCatalogId) || prefab == null) return;
        entriesById[visualCatalogId] = new Entry { visualCatalogId = visualCatalogId, prefab = prefab };
    }

    public CivilianVehicleBody Acquire(NpcVehicleProfile profile) {
        if (profile == null || string.IsNullOrEmpty(profile.visualCatalogId)) return null;
        if (!entriesById.TryGetValue(profile.visualCatalogId, out var entry)) return null;
        if (freeById.TryGetValue(profile.visualCatalogId, out var free) && free.Count > 0) return free.Pop();

        createdById.TryGetValue(profile.visualCatalogId, out int created);
        if (created >= maxInstancesPerVisual) return null;
        var instance = Instantiate(entry.prefab, transform);
        instance.gameObject.SetActive(false);
        instance.VisualCatalogId = profile.visualCatalogId;
        createdById[profile.visualCatalogId] = created + 1;
        return instance;
    }

    public void Release(CivilianVehicleBody body) {
        if (body == null) return;
        body.gameObject.SetActive(false);
        string id = body.VisualCatalogId ?? string.Empty;
        if (!freeById.TryGetValue(id, out var free)) {
            free = new Stack<CivilianVehicleBody>();
            freeById[id] = free;
        }
        free.Push(body);
    }
}
