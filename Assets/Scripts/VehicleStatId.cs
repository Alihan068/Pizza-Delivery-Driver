/// <summary>
/// The upgradable stats a vehicle has.
/// </summary>
/// <remarks>
/// These identify fields that physically exist on <see cref="VehicleData"/> (baseSpeed, speedStep,
/// maxSpeedLevel, speedCostMult and so on), so the set is schema, not tuning data. Every number
/// behind them is still authored in the asset — only the identity of a stat lives here.
/// <para>
/// This replaces the magic strings ("Speed", "Turn", ...) that used to be repeated across six
/// switch statements in GameManager and GarageManager, where a typo silently took the player's
/// money without granting a level.
/// </para>
/// </remarks>
public enum VehicleStatId {
    /// <summary>Top speed. Raises damage taken too, because impact damage is superlinear in speed.</summary>
    Speed,

    /// <summary>Turn rate, shown to the player as Handling.</summary>
    Turn,

    /// <summary>Maximum health, shown to the player as Chassis.</summary>
    Health,

    /// <summary>Incoming damage reduction as a fraction.</summary>
    Armor,

    /// <summary>Pizza carrying capacity, shown to the player as Storage.</summary>
    Capacity,

    /// <summary>Chance to keep a pizza on impact, shown to the player as Stabilizer.</summary>
    Protection
}
