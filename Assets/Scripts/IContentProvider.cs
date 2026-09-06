using System.Collections.Generic;

/// <summary>
/// A source of game content. Implementations supply vehicles and maps from somewhere — the built
/// project, a downloaded package, a local folder — without the rest of the game knowing where from.
/// </summary>
/// <remarks>
/// This is the seam that keeps externally supplied content possible. The registry that consumes
/// providers never learns the difference between content that shipped with the game and content
/// that arrived later, so adding a new source is a new implementation of this interface and
/// nothing else.
/// </remarks>
public interface IContentProvider {

    /// <summary>
    /// Identifies this source in diagnostics, so a conflicting id can be traced to the source
    /// that supplied it.
    /// </summary>
    string ProviderId { get; }

    /// <summary>Vehicles this source offers, in the order they should be presented.</summary>
    /// <returns>Never null; an empty sequence when the source has no vehicles.</returns>
    IEnumerable<VehicleData> GetVehicles();

    /// <summary>Maps this source offers, in the order they should be presented.</summary>
    /// <returns>Never null; an empty sequence when the source has no maps.</returns>
    IEnumerable<MapData> GetMaps();
}
