/// <summary>
/// How a delivery session finished. Decides how much of the unbanked earnings survive and how the
/// repair bill is calculated.
/// </summary>
public enum EndReason {
    /// <summary>The selected finite shift timer ran out. Earnings are kept in full.</summary>
    TimeUp,

    /// <summary>The player reached the extraction zone and held it. Earnings are kept in full.</summary>
    Extracted,

    /// <summary>Health hit zero. Half the earnings survive and the full health bar is billed.</summary>
    Wrecked,

    /// <summary>The player quit from the pause menu. Earnings burn, repairs are billed normally.</summary>
    Abandoned,

    /// <summary>
    /// The process was killed while a session was running, so settlement never happened.
    /// Detected on the next load and settled as the worst case: no earnings, full repair bill.
    /// </summary>
    /// <remarks>
    /// Without this, force-quitting mid-shift skipped the repair bill entirely, which made killing
    /// the game strictly cheaper than any legitimate way out of a bad run.
    /// </remarks>
    Interrupted,

    /// <summary>
    /// A live police contact held the player stationary for the authored arrest duration. Earnings
    /// retain the Q05 share, while the player remains alive and is billed for actual damage.
    /// </summary>
    Arrested
}
