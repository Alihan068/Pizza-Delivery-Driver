using UnityEngine;

/// <summary>
/// A single vehicle validation finding: a severity, a human readable message and an
/// optional target object that can be pinged in the editor.
/// </summary>
/// <remarks>
/// This type is deliberately a dumb data carrier. <see cref="VehicleValidator"/> produces
/// them and windows draw them; the finding itself never depends on UI, the console or Debug.Log.
/// </remarks>
public class VehicleIssue {
    /// <summary>How severe this finding is.</summary>
    public VehicleIssueSeverity severity;

    /// <summary>The explanation shown to the user.</summary>
    public string message;

    /// <summary>Object to ping or select. May be null.</summary>
    public Object context;

    /// <summary>Creates a new finding.</summary>
    /// <param name="severity">How severe the finding is.</param>
    /// <param name="message">The explanation shown to the user.</param>
    /// <param name="context">Object to ping, or null when there is none.</param>
    public VehicleIssue(VehicleIssueSeverity severity, string message, Object context = null) {
        this.severity = severity;
        this.message = message;
        this.context = context;
    }

    /// <summary>Creates a finding for something that breaks the game.</summary>
    /// <param name="message">The explanation shown to the user.</param>
    /// <param name="context">Object to ping, or null when there is none.</param>
    /// <returns>A new finding with Error severity.</returns>
    public static VehicleIssue Error(string message, Object context = null) {
        return new VehicleIssue(VehicleIssueSeverity.Error, message, context);
    }

    /// <summary>Creates a finding for something suspicious that still runs.</summary>
    /// <param name="message">The explanation shown to the user.</param>
    /// <param name="context">Object to ping, or null when there is none.</param>
    /// <returns>A new finding with Warning severity.</returns>
    public static VehicleIssue Warning(string message, Object context = null) {
        return new VehicleIssue(VehicleIssueSeverity.Warning, message, context);
    }

    /// <summary>Creates a purely informational finding.</summary>
    /// <param name="message">The explanation shown to the user.</param>
    /// <param name="context">Object to ping, or null when there is none.</param>
    /// <returns>A new finding with Info severity.</returns>
    public static VehicleIssue Info(string message, Object context = null) {
        return new VehicleIssue(VehicleIssueSeverity.Info, message, context);
    }
}
