/// <summary>
/// Issues fresh <see cref="VehicleIdentity"/> lifeIds within one session. One instance belongs to
/// exactly one <see cref="TrafficSessionContext"/>; a new session gets a new registry, so lifeIds
/// never collide across sessions and an old session's identities are simply discarded, not reused.
/// S00.4 only issues the player identity; S03 issues civilian/police identities through the same
/// method as the pool comes online.
/// </summary>
public sealed class VehicleIdentityRegistry {
    int nextLifeId = 1;

    /// <summary>Issues a new identity for the given role. Every call returns a distinct lifeId.</summary>
    /// <param name="role">Role the new identity belongs to.</param>
    public VehicleIdentity Assign(VehicleRole role) {
        return new VehicleIdentity(nextLifeId++, role);
    }
}
