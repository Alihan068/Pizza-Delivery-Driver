/// <summary>
/// Binds one session's <see cref="VehiclePopulationService"/> to its <see cref="TrafficSessionCoordinator"/>
/// so every reservation gates on the coordinator's own active flag, and a single session-end event
/// cancels every still-pending reservation and detaches this gate's own subscription. Because each
/// session constructs an entirely new coordinator/service/gate triple (never a persistent singleton),
/// and this detach always runs exactly once, running session after session without a domain reload
/// never accumulates pending reservations or dangling event handlers. Owns no gameplay decision
/// itself — it only gates and tears down.
/// </summary>
public sealed class TrafficSessionLifecycleGate {
    readonly TrafficSessionCoordinator coordinator;
    readonly VehiclePopulationService populationService;
    bool detached;

    /// <summary>True only while this gate is still attached to a coordinator that is initialized and not yet ended.</summary>
    public bool IsActive => !detached && coordinator.IsActive;

    /// <summary>True until the session-end handler has run and unsubscribed — the leak-gate's own health check.</summary>
    public bool IsAttachedToSession => !detached;

    public TrafficSessionLifecycleGate(TrafficSessionCoordinator coordinator, VehiclePopulationService populationService) {
        this.coordinator = coordinator;
        this.populationService = populationService;
        coordinator.Ended += HandleSessionEnded;
    }

    /// <summary>Gate-checked reservation: rejected outright once the session is no longer active, before ever touching the population service.</summary>
    public bool TryReserveSpawn(VehicleRole role) {
        if (!IsActive) return false;
        return populationService.TryReserveSpawn(role);
    }

    void HandleSessionEnded() {
        if (detached) return;
        while (populationService.PendingCivilianCount > 0 && populationService.CancelReservation(VehicleRole.Civilian)) { }
        while (populationService.PendingPoliceCount > 0 && populationService.CancelReservation(VehicleRole.Police)) { }
        coordinator.Ended -= HandleSessionEnded;
        detached = true;
    }
}
