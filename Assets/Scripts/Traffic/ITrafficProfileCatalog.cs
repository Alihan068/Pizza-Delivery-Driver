/// <summary>
/// Resolves a stable vehicleProfileId to its authored <see cref="NpcVehicleProfile"/>. The rest of
/// the traffic system depends only on this interface, never on where a profile actually came from —
/// the built-in project (S02.3) or an external package (S11.2) implement it identically.
/// </summary>
public interface ITrafficProfileCatalog {
    /// <summary>Resolves one profile by its permanent id.</summary>
    /// <param name="vehicleProfileId">Stable id to resolve.</param>
    /// <returns>The matching profile, or null when it is not registered in this catalog.</returns>
    NpcVehicleProfile ResolveVehicleProfile(string vehicleProfileId);
}
