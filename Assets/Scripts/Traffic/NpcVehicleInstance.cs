/// <summary>
/// Single owner of one pooled slot's <see cref="VehicleLifeState"/> and current
/// <see cref="VehicleIdentity"/>. Every legal transition goes through this class, so a slot can
/// never be freed twice, activated without first being reserved, or have its destruction event
/// counted twice for the same life. Relocation (moving an Active instance to a new position without
/// ending its life) is deliberately not a state transition at all here — it is a position change
/// performed elsewhere while this instance stays Active with the same identity untouched.
/// </summary>
public sealed class NpcVehicleInstance {
    bool wreckedThisLife;

    public VehicleIdentity? Identity { get; private set; }
    public VehicleLifeState State { get; private set; } = VehicleLifeState.Pooled;

    /// <summary>Reserves this pooled instance with a fresh identity for a new life. Only valid while Pooled.</summary>
    public bool TryReserve(VehicleIdentityRegistry registry, VehicleRole role) {
        if (State != VehicleLifeState.Pooled || registry == null) return false;
        Identity = registry.Assign(role);
        State = VehicleLifeState.Reserved;
        wreckedThisLife = false;
        return true;
    }

    /// <summary>Activates a reserved instance. Only valid while Reserved — an instance can never jump straight from Pooled to Active.</summary>
    public bool TryActivate() {
        if (State != VehicleLifeState.Reserved) return false;
        State = VehicleLifeState.Active;
        return true;
    }

    /// <summary>Enters crash recovery. Only valid while Active.</summary>
    public bool TryEnterCrashRecovery() {
        if (State != VehicleLifeState.Active) return false;
        State = VehicleLifeState.CrashRecovery;
        return true;
    }

    /// <summary>Recovers back to Active from CrashRecovery.</summary>
    public bool TryRecoverToActive() {
        if (State != VehicleLifeState.CrashRecovery) return false;
        State = VehicleLifeState.Active;
        return true;
    }

    /// <summary>
    /// Marks this life destroyed. Valid from Active or CrashRecovery. A second call for the same
    /// life (before it returns to the pool and gets a new identity) is rejected — the destruction
    /// event can only ever free this slot's moving budget once.
    /// </summary>
    public bool TryMarkWrecked() {
        if (wreckedThisLife) return false;
        if (State != VehicleLifeState.Active && State != VehicleLifeState.CrashRecovery) return false;
        State = VehicleLifeState.Wreck;
        wreckedThisLife = true;
        return true;
    }

    /// <summary>Returns this instance to the pool, invalidating its identity. A second call while already Pooled is rejected — the same slot cannot be freed twice.</summary>
    public bool TryReturnToPool() {
        if (State == VehicleLifeState.Pooled) return false;
        State = VehicleLifeState.Pooled;
        Identity = null;
        wreckedThisLife = false;
        return true;
    }
}
