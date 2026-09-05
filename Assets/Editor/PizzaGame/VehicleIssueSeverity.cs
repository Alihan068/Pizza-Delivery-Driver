/// <summary>
/// Severity of a single vehicle validation finding. The order is deliberate: a higher
/// numeric value means a more severe finding, so issues can be sorted by this field.
/// </summary>
public enum VehicleIssueSeverity {
    /// <summary>Informational only. Nothing is wrong, it is just worth knowing.</summary>
    Info,

    /// <summary>Suspicious but the game still runs. Possibly misconfigured.</summary>
    Warning,

    /// <summary>The vehicle is broken. It will throw at runtime or be unplayable.</summary>
    Error
}
