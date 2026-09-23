/// <summary>
/// Explains whether a settled run can enter competitive services such as leaderboards or Steam
/// achievements.
/// </summary>
public enum CompetitiveEligibilityStatus {
    /// <summary>The run satisfies the current competitive ruleset.</summary>
    Eligible,

    /// <summary>The run was an endless session and has no fixed competitive duration.</summary>
    Freeplay,

    /// <summary>The run used a duration outside the current standard board.</summary>
    NonStandardDuration,

    /// <summary>The selected map came from an external or custom content provider.</summary>
    ExternalMap,

    /// <summary>The selected vehicle came from an external or custom content provider.</summary>
    ExternalVehicle,

    /// <summary>The vehicle used temporary custom performance tuning.</summary>
    CustomVehicleTuning,

    /// <summary>No trusted creator manifest expected hash was available for verification.</summary>
    MissingTrustedManifest,

    /// <summary>Detached content evidence or trusted manifest verification failed.</summary>
    ContentIntegrityFailure,

    /// <summary>The run ended before a competitive result could be finalized.</summary>
    Unfinished
}
