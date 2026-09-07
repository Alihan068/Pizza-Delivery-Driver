/// <summary>
/// Signed courier-rating adjustment associated with one way a shift can end.
/// </summary>
[System.Serializable]
public class EndReasonRatingOffset {

    /// <summary>Shift ending this adjustment applies to.</summary>
    public EndReason reason;

    /// <summary>Points added to or removed from the courier rating.</summary>
    public int offset;
}
