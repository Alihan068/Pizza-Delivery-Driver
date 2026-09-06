public struct SessionResult {
    public EndReason reason;
    public int grossEarnings;
    public int keptEarnings;
    public int repairCost;
    public int repairBeforeClamp;
    public int bankBefore;
    public int bankAfter;

    /// <summary>Reputation earned for this shift.</summary>
    public int reputationEarned;

    /// <summary>Rank after this shift's reputation was applied.</summary>
    public int rankAfter;

    /// <summary>True when this shift was the day's last, so <see cref="Rent"/> applies.</summary>
    public bool dayEnded;

    /// <summary>Day number that just finished. Only meaningful when <see cref="dayEnded"/> is true.</summary>
    public int dayNumber;

    /// <summary>Rent settlement for the day that just ended. Null unless <see cref="dayEnded"/> is true.</summary>
    public RentSettlementResult rent;
}
