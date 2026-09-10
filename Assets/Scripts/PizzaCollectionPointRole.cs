/// <summary>
/// Identifies the authored purpose of a pizza collection point when a map builder
/// needs to place known gameplay services from a blueprint.
/// </summary>
public enum PizzaCollectionPointRole {
    /// <summary>The primary pizza shop used by the built-in map shell.</summary>
    Shop,

    /// <summary>A roadside pizza depot used by the built-in map shell.</summary>
    Roadside,

    /// <summary>A custom supply point whose placement is controlled by its map author.</summary>
    Custom
}
