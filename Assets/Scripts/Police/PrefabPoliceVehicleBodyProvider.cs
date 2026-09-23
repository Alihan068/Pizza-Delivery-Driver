using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lazily instantiates and pools police body prefabs resolved by a serialized visual catalog. Each
/// visual id has an independent instance cap and free stack. The provider owns only physical body
/// allocation; it never creates a life, binds a profile, or resets health.
/// </summary>
public sealed class PrefabPoliceVehicleBodyProvider : MonoBehaviour, IPoliceVehicleBodyProvider {
    [SerializeField] PoliceVehiclePrefabCatalog prefabCatalog;

    /// <summary>Maximum number of instantiated bodies permitted for each visual catalog id.</summary>
    [Tooltip("Upper bound on instantiated bodies per visual id; requests beyond the cap return null.")]
    [Min(1)] public int maxInstancesPerVisual = 16;

    readonly Dictionary<string, Stack<PoliceVehicleBody>> freeByVisual = new Dictionary<string, Stack<PoliceVehicleBody>>();
    readonly Dictionary<string, int> createdByVisual = new Dictionary<string, int>();
    readonly Dictionary<PoliceVehicleBody, string> ownedVisualByBody = new Dictionary<PoliceVehicleBody, string>();
    readonly HashSet<PoliceVehicleBody> rentedBodies = new HashSet<PoliceVehicleBody>();

    /// <summary>
    /// Configures the catalog and per-visual cap before the first allocation. Reconfiguration is
    /// rejected after any body has been created so existing ownership and free-pool accounting
    /// cannot be invalidated.
    /// </summary>
    /// <param name="catalog">Catalog used to resolve each profile visual id.</param>
    /// <param name="instanceCap">Positive maximum number of bodies created for each visual id.</param>
    /// <param name="reason">Validation or lifecycle failure description, or null on success.</param>
    /// <returns>True when configuration was accepted.</returns>
    public bool Configure(PoliceVehiclePrefabCatalog catalog, int instanceCap, out string reason) {
        reason = null;
        if (catalog == null) {
            reason = "Police body catalog is null.";
            return false;
        }
        if (instanceCap <= 0) {
            reason = "Police body instance cap must be positive.";
            return false;
        }
        if (ownedVisualByBody.Count > 0) {
            reason = "Police body configuration cannot change after allocation.";
            return false;
        }

        prefabCatalog = catalog;
        maxInstancesPerVisual = instanceCap;
        freeByVisual.Clear();
        createdByVisual.Clear();
        rentedBodies.Clear();
        return true;
    }

    /// <inheritdoc />
    public PoliceVehicleBody Acquire(NpcVehicleProfile profile) {
        if (profile == null || string.IsNullOrWhiteSpace(profile.visualCatalogId) || prefabCatalog == null || maxInstancesPerVisual <= 0) return null;
        string visualId = profile.visualCatalogId;
        if (!prefabCatalog.TryResolve(visualId, out PoliceVehicleBody prefab)) return null;

        if (freeByVisual.TryGetValue(visualId, out Stack<PoliceVehicleBody> free) && free.Count > 0) {
            PoliceVehicleBody reused = free.Pop();
            rentedBodies.Add(reused);
            return reused;
        }

        createdByVisual.TryGetValue(visualId, out int created);
        if (created >= maxInstancesPerVisual) return null;

        PoliceVehicleBody instance = Instantiate(prefab, transform);
        instance.gameObject.SetActive(false);
        ownedVisualByBody.Add(instance, visualId);
        rentedBodies.Add(instance);
        createdByVisual[visualId] = created + 1;
        return instance;
    }

    /// <inheritdoc />
    public void Release(PoliceVehicleBody body) {
        if (body == null || !ownedVisualByBody.TryGetValue(body, out string visualId) || !rentedBodies.Remove(body)) return;

        body.gameObject.SetActive(false);
        if (!freeByVisual.TryGetValue(visualId, out Stack<PoliceVehicleBody> free)) {
            free = new Stack<PoliceVehicleBody>();
            freeByVisual.Add(visualId, free);
        }
        free.Push(body);
    }
}
