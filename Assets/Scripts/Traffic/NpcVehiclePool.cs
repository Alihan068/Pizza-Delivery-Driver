using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fixed-size, profile-agnostic pool of reusable <see cref="NpcVehicleInstance"/> slots. Total
/// capacity is computed once from the minimum of the authored wreck and global physics caps. This
/// is the tightest possible bound on simultaneous physical NPC objects. Acquiring a slot here
/// grants no spawn permission by itself; that decision belongs to <see cref="VehiclePopulationService"/>
/// and whatever director calls both.
/// </summary>
public sealed class NpcVehiclePool {
    readonly List<NpcVehicleInstance> instances;
    readonly VehicleIdentityRegistry identityRegistry;
    readonly ITrafficProfileCatalog catalog;
    readonly List<Action<NpcVehicleInstance>> resetHooks = new List<Action<NpcVehicleInstance>>();

    public int Capacity => instances.Count;

    public int PooledCount {
        get {
            int count = 0;
            foreach (var instance in instances) {
                if (instance.State == VehicleLifeState.Pooled) count++;
            }
            return count;
        }
    }

    /// <summary>Builds a pool pre-warmed to its full, budget-capped size.</summary>
    /// <param name="budget">Supplies the minimum wreck/physics instance cap. Null yields an empty pool.</param>
    /// <param name="catalog">Catalog used to reject unknown/disallowed profile ids atomically at acquire time.</param>
    /// <param name="identityRegistry">Registry every reservation draws a fresh lifeId from.</param>
    public NpcVehiclePool(PopulationBudgetData budget, ITrafficProfileCatalog catalog, VehicleIdentityRegistry identityRegistry) {
        this.catalog = catalog;
        this.identityRegistry = identityRegistry;
        int capacity = budget != null ? Mathf.Max(0, Mathf.Min(budget.maxWreckSlots, budget.maxTotalPhysicsObjects)) : 0;
        instances = new List<NpcVehicleInstance>(capacity);
        for (int i = 0; i < capacity; i++) instances.Add(new NpcVehicleInstance());
    }

    /// <summary>
    /// Registers a callback invoked on every <see cref="Release"/>, before the slot returns to the
    /// pool. Motor/health/AI systems (S04+) use this to explicitly clear their own subscriptions,
    /// velocity, and tint rather than relying on the next life to overwrite stale state.
    /// </summary>
    public void RegisterResetHook(Action<NpcVehicleInstance> hook) {
        if (hook != null) resetHooks.Add(hook);
    }

    /// <summary>
    /// Attempts to reserve one free slot for the given profile and role. Rejects atomically — no
    /// slot is touched — when the profile id is unknown to the catalog, the profile does not allow
    /// the requested role, or every slot is already on loan.
    /// </summary>
    /// <param name="vehicleProfileId">Profile id to validate against the catalog before reserving.</param>
    /// <param name="role">Role the caller intends to use the slot for.</param>
    /// <param name="instance">The reserved instance, or null on rejection.</param>
    public bool TryAcquire(string vehicleProfileId, VehicleRole role, out NpcVehicleInstance instance) {
        instance = null;
        var profile = catalog?.ResolveVehicleProfile(vehicleProfileId);
        if (profile == null) return false;
        if (profile.allowedRoles == null || !profile.allowedRoles.Contains(role)) return false;

        foreach (var candidate in instances) {
            if (candidate.State != VehicleLifeState.Pooled) continue;
            if (!candidate.TryReserve(identityRegistry, role)) continue;
            instance = candidate;
            return true;
        }
        return false; // pool exhausted
    }

    /// <summary>
    /// Runs every registered reset hook, then returns the slot to the pool with its identity
    /// cleared. A slot that is already Pooled is rejected without running hooks — there is nothing
    /// to reset.
    /// </summary>
    public bool Release(NpcVehicleInstance instance) {
        if (instance == null || instance.State == VehicleLifeState.Pooled) return false;
        foreach (var hook in resetHooks) hook(instance);
        return instance.TryReturnToPool();
    }
}
