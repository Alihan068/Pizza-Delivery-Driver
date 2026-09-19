/// <summary>
/// Session-scoped identity for one live vehicle instance (player or NPC). Carries a monotonic
/// lifeId instead of a Unity instanceId so damage dedupe and instigator attribution stay correct
/// across pool reuse: a vehicle leaving the pool always gets a fresh lifeId, while a relocation
/// (same live instance moved elsewhere) keeps its lifeId.
/// </summary>
public readonly struct VehicleIdentity {
    public readonly int lifeId;
    public readonly VehicleRole role;

    public VehicleIdentity(int lifeId, VehicleRole role) {
        this.lifeId = lifeId;
        this.role = role;
    }
}
