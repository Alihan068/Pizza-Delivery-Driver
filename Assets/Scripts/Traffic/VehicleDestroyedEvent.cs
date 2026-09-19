/// <summary>One vehicle's single, terminal destruction for its current life: who died, what role they had, the killing DamageContext, and when.</summary>
public readonly struct VehicleDestroyedEvent {
    public readonly int victimLifeId;
    public readonly VehicleRole victimRole;
    public readonly DamageContext killingContext;
    public readonly float sessionTime;

    public VehicleDestroyedEvent(int victimLifeId, VehicleRole victimRole, DamageContext killingContext, float sessionTime) {
        this.victimLifeId = victimLifeId;
        this.victimRole = victimRole;
        this.killingContext = killingContext;
        this.sessionTime = sessionTime;
    }
}
