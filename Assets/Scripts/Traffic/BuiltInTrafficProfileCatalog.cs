/// <summary>
/// Resolves vehicle profiles authored in the project itself, mirroring how
/// <see cref="BuiltInContentProvider"/> supplies built-in vehicles/maps. Used both by real gameplay
/// (an array authored on a director/manager) and by tests (a small in-memory array of fixture profiles).
/// </summary>
public class BuiltInTrafficProfileCatalog : ITrafficProfileCatalog {
    readonly NpcVehicleProfile[] profiles;

    /// <summary>Creates a catalog over an authored or test set of profiles.</summary>
    /// <param name="profiles">Profiles this catalog can resolve. May be null.</param>
    public BuiltInTrafficProfileCatalog(NpcVehicleProfile[] profiles) {
        this.profiles = profiles;
    }

    /// <summary>Resolves a profile by permanent id via a linear scan of the authored/test array.</summary>
    /// <param name="vehicleProfileId">Stable id to resolve.</param>
    public NpcVehicleProfile ResolveVehicleProfile(string vehicleProfileId) {
        if (profiles == null || string.IsNullOrEmpty(vehicleProfileId)) return null;
        foreach (var profile in profiles) {
            if (profile != null && profile.vehicleProfileId == vehicleProfileId) return profile;
        }
        return null;
    }
}
