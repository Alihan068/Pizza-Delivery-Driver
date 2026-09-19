/// <summary>
/// Supplies physical civilian bodies for a profile's visual catalog id and takes them back. The
/// manager never instantiates prefabs itself; a scene provider (prefab table) or a test fake
/// implements this so the composition stays EditMode-testable.
/// </summary>
public interface ICivilianVehicleBodyProvider {
    /// <summary>Returns an inactive body for the profile, or null when its visual id is unknown or the provider is exhausted.</summary>
    CivilianVehicleBody Acquire(NpcVehicleProfile profile);

    /// <summary>Takes a body back (already deactivated and unbound by the manager) for later reuse.</summary>
    void Release(CivilianVehicleBody body);
}
