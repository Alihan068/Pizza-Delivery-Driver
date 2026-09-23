/// <summary>Stable tactical behavior that a police vehicle may be configured to perform.</summary>
public enum PoliceTacticalRole {
    /// <summary>Behavior that closes distance to a target.</summary>
    Pursue = 0,
    /// <summary>Behavior that positions across a target route.</summary>
    Intercept = 1,
    /// <summary>Behavior that uses a controlled collision tactic.</summary>
    Ram = 2,
    /// <summary>Behavior that returns to a recoverable police state.</summary>
    Recover = 3,
    /// <summary>Behavior that never aims at the target: it drives slightly ahead of it and blocks the side streets it could turn into.</summary>
    Shadow = 4
}
