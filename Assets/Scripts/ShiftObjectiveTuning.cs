using UnityEngine;

/// <summary>
/// One objective type's share of the per-shift objective reward cap. Kept as an array entry on
/// <see cref="CareerData"/> rather than a named field per type, so the set of offered types can
/// change without touching the asset's schema.
/// </summary>
[System.Serializable]
public class ShiftObjectiveTuning {

    /// <summary>Which objective this tunes.</summary>
    public ShiftObjectiveType type;

    [Tooltip("Localization key for the objective title.")]
    public string displayNameKey;

    [Tooltip("Localization key for the objective description.")]
    public string descriptionKey;

    [Tooltip("Fraction of CareerData.objectiveCapFraction * GTypical(rank) this type pays when completed.")]
    public float rewardFraction = 1f;

    [Tooltip("When true, this objective succeeds only if no violation is recorded.")]
    public bool zeroTarget;

    [Tooltip("Base added to the target before capacity and rank contributions.")]
    public int targetBase;

    [Tooltip("Capacity multiplier used when calculating the target.")]
    public float targetCapacityMultiplier;

    [Tooltip("Rank divisor used for the target contribution. Zero disables the rank contribution.")]
    public int targetRankDivisor;

    [Tooltip("Minimum target after all target contributions are added.")]
    public int minimumTarget = 1;

    [Tooltip("Base value for the optional secondary parameter.")]
    public int parameterBase;

    [Tooltip("Capacity multiplier used for the optional secondary parameter.")]
    public float parameterCapacityMultiplier;
}
