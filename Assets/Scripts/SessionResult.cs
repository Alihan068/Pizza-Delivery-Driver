public struct SessionResult {
    public EndReason reason;
    public int grossEarnings;
    public int keptEarnings;
    public int repairCost;
    public int repairBeforeClamp;
    public int bankBefore;
    public int bankAfter;

    /// <summary>Finite shift duration selected before this session began, in minutes.</summary>
    public int shiftDurationMinutes;

    /// <summary>Competitive eligibility decision produced when the session was settled.</summary>
    public CompetitiveEligibilityStatus competitiveEligibility;

    /// <summary>Signed courier-rating change produced by this shift.</summary>
    public int ratingDelta;

    /// <summary>Rank after this shift's reputation was applied.</summary>
    public int rankAfter;

    /// <summary>True when this shift was the day's last, so <see cref="Rent"/> applies.</summary>
    public bool dayEnded;

    /// <summary>Day number that just finished. Only meaningful when <see cref="dayEnded"/> is true.</summary>
    public int dayNumber;

    /// <summary>Rent settlement for the day that just ended. Null unless <see cref="dayEnded"/> is true.</summary>
    public RentSettlementResult rent;

    /// <summary>True when the result came from an endless session rather than a career shift.</summary>
    public bool isFreeplay;

    /// <summary>True when this career shift was the authored final challenge.</summary>
    public bool isFinalShift;

    /// <summary>True when the final career challenge was completed successfully.</summary>
    public bool finalShiftSucceeded;

    /// <summary>True when this settlement leaves the career complete.</summary>
    public bool careerCompleted;

    /// <summary>True when this shift reached or tied the saved career score record.</summary>
    public bool personalBestScore;

    /// <summary>True when this shift reached or tied the saved career delivery record.</summary>
    public bool personalBestDeliveries;

    /// <summary>True when this endless session reached or tied the saved endless delivery record.</summary>
    public bool personalBestFreeplayDeliveries;

    /// <summary>True when no customer timed out during the shift.</summary>
    public bool noMissedOrders;

    /// <summary>True when the driver took no collision damage during the shift.</summary>
    public bool noCollisionDamage;

    /// <summary>True when the driver lost no pizzas during the shift.</summary>
    public bool noPizzasLost;

    /// <summary>True when all three authored perfect-shift checks passed.</summary>
    public bool perfectShift;

    /// <summary>Plain sum of every AddScore event for the shift, before any modifier multiplier.</summary>
    public int rawScore;

    /// <summary>Product of every selected modifier's score multiplier applied to this shift. Neutral (1) when none were selected.</summary>
    public float scoreMultiplier;

    /// <summary>The number shown on HUD, the result screen, best-score records, and difficulty tier score: floor(max(0, rawScore) * scoreMultiplier).</summary>
    public int finalScore;

    /// <summary>Permanent ids of the modifiers applied to this shift, in canonical order. Empty when none were selected.</summary>
    public string[] appliedModifierIds;
}
