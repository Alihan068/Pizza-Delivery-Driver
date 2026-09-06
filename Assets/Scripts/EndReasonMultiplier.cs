/// <summary>
/// How much a shift's ending affects the reputation it earns. Kept as an array entry on
/// <see cref="CareerData"/> rather than one named field per <see cref="EndReason"/>, so a future
/// ending reason needs only a new array entry, not a schema change.
/// </summary>
[System.Serializable]
public class EndReasonMultiplier {

    /// <summary>Which shift ending this multiplier applies to.</summary>
    public EndReason reason;

    /// <summary>Reputation earned for the shift is multiplied by this.</summary>
    public float multiplier = 1f;
}
