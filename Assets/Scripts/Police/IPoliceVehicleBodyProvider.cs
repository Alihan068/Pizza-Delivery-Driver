/// <summary>
/// Supplies inactive police bodies for a profile's visual catalog id and accepts only bodies that
/// this provider owns. Police lifecycle state, profile binding, and health initialization remain
/// the responsibility of the manager that borrows the physical body.
/// </summary>
public interface IPoliceVehicleBodyProvider {
    /// <summary>Returns an inactive owned body for the profile, or null when the id is invalid, ambiguous, or capped.</summary>
    PoliceVehicleBody Acquire(NpcVehicleProfile profile);

    /// <summary>Returns an already-unbound owned body to its visual-specific free pool.</summary>
    void Release(PoliceVehicleBody body);
}
