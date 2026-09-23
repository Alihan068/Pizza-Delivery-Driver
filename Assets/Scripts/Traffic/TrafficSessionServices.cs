/// <summary>
/// Immutable per-session bundle shared by civilian and police role owners. The bundle owns the
/// common graph, profile catalog, pool, population counters, budget and identity registry, while
/// each role owner remains responsible for releasing only the bodies and lives it acquired.
/// </summary>
public sealed class TrafficSessionServices {
    bool civilianOwnerCleaned;
    bool policeOwnerCleaned;
    bool closed;

    /// <summary>Navigation graph detached for this session.</summary>
    public RoadGraphRuntime Graph { get; }
    /// <summary>Combined civilian and optional police shared profile catalog.</summary>
    public ITrafficProfileCatalog Catalog { get; }
    /// <summary>Shared logical NPC life pool for both role owners.</summary>
    public NpcVehiclePool Pool { get; }
    /// <summary>Shared moving, wreck and physics population counters.</summary>
    public VehiclePopulationService Population { get; }
    /// <summary>Authored caps used to build the shared pool and population counters.</summary>
    public PopulationBudgetData Budget { get; }
    /// <summary>Session-scoped identity authority shared by player, civilian and police owners.</summary>
    public VehicleIdentityRegistry IdentityRegistry { get; }
    /// <summary>True after both role owners have reported cleanup and the population was reset.</summary>
    public bool IsClosed => closed;

    /// <summary>Creates one shared service bundle; all references are retained, never cloned or replaced.</summary>
    public TrafficSessionServices(RoadGraphRuntime graph, ITrafficProfileCatalog catalog, NpcVehiclePool pool,
        VehiclePopulationService population, PopulationBudgetData budget, VehicleIdentityRegistry identityRegistry) {
        Graph = graph;
        Catalog = catalog;
        Pool = pool;
        Population = population;
        Budget = budget;
        IdentityRegistry = identityRegistry;
    }

    /// <summary>
    /// Records that one role owner has released all resources it acquired from this bundle. Closing
    /// before both civilian and police owners report is deliberately a no-op.
    /// </summary>
    /// <param name="role">Role owner that completed its cleanup.</param>
    /// <returns>True when the role was accepted; false for an invalid role or an already closed bundle.</returns>
    public bool MarkRoleOwnerCleaned(VehicleRole role) {
        if (closed) return false;
        if (role == VehicleRole.Civilian) { civilianOwnerCleaned = true; return true; }
        if (role == VehicleRole.Police) { policeOwnerCleaned = true; return true; }
        return false;
    }

    /// <summary>
    /// Closes the shared session state only after both role owners have completed cleanup. Repeated
    /// calls are harmless and cannot reset counters a second time.
    /// </summary>
    public void Close() {
        if (closed || !civilianOwnerCleaned || !policeOwnerCleaned) return;
        Population?.ResetAll();
        closed = true;
    }
}
