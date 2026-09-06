/// <summary>
/// The kinds of optional bonus objective a shift can offer. Each measures a different axis of play;
/// see <see cref="ShiftObjectiveCatalog"/> for how a type's target and reward are computed.
/// </summary>
public enum ShiftObjectiveType {
    /// <summary>Deliver a rank-scaled number of pizzas on time this shift.</summary>
    Quota,

    /// <summary>Finish the shift with zero customer timeouts.</summary>
    PerfectService,

    /// <summary>Finish the shift with zero collision-damage events.</summary>
    CleanRun,

    /// <summary>Finish the shift without a single pizza knocked loose by a collision.</summary>
    CargoGuard,

    /// <summary>Fully complete a rank-scaled number of large orders.</summary>
    BigOrder,

    /// <summary>Extract with time and deliveries to spare, rather than at the last second.</summary>
    FastExtraction
}
