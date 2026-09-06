/// <summary>Outcome of charging a day's rent, produced by <see cref="CareerManager"/>.</summary>
public class RentSettlementResult {

    /// <summary>True when this was a grace day and no rent was charged at all.</summary>
    public bool wasFree;

    /// <summary>Rent that was due. Zero when <see cref="wasFree"/> is true.</summary>
    public int rentDue;

    /// <summary>Bank balance after paying what could be paid.</summary>
    public int bankAfter;

    /// <summary>Portion of the rent that could not be paid. Never carried forward as debt.</summary>
    public int shortfall;

    /// <summary>Reputation lost for the shortfall. Zero when it was fully paid.</summary>
    public int reputationPenalty;
}
