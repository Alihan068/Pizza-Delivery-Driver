using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authored police composition and timing values for one effective-heat tier. A tier is data, not
/// a code branch: adding a new tier or vehicle composition does not require changing the director.
/// </summary>
[System.Serializable]
public sealed class PoliceHeatTier {
    /// <summary>Stable identifier used by authoring, diagnostics and frozen session provenance.</summary>
    public string tierId;

    /// <summary>Minimum effective heat at which this tier becomes active.</summary>
    [Min(0f)] public float minimumHeat;

    /// <summary>Desired number of living or already reserved police vehicles for this tier.</summary>
    [Min(0)] public int targetCount;

    /// <summary>Minimum active seconds between newly requested reinforcements.</summary>
    [Min(0f)] public float reinforcementInterval = 1f;

    /// <summary>Delay before a destroyed police vehicle may create a replacement request.</summary>
    [Min(0f)] public float destroyedReplacementDelay = 1f;

    /// <summary>Cooldown before an already living police vehicle may be relocated.</summary>
    [Min(0f)] public float relocationCooldown = 1f;

    /// <summary>Weighted, role-aware police choices available to this tier.</summary>
    public List<PoliceCompositionEntry> compositions = new List<PoliceCompositionEntry>();

    /// <summary>Returns whether all tier values are finite and structurally usable.</summary>
    /// <param name="reason">Stable failure reason when validation fails.</param>
    /// <returns>True when the tier can be consumed by the director.</returns>
    public bool TryValidate(out string reason) {
        reason = null;
        if (string.IsNullOrWhiteSpace(tierId)) return Fail("tier id is empty", out reason);
        if (!FiniteNonNegative(minimumHeat) || !FiniteNonNegative(reinforcementInterval) ||
            !FiniteNonNegative(destroyedReplacementDelay) || !FiniteNonNegative(relocationCooldown))
            return Fail("tier timing or threshold is invalid", out reason);
        if (targetCount < 0) return Fail("tier target count is negative", out reason);
        if (compositions == null || compositions.Count == 0) return Fail("tier composition is empty", out reason);
        bool hasWeight = false;
        foreach (var entry in compositions) {
            if (entry == null || !entry.TryValidate(out reason)) return false;
            hasWeight |= entry.weight > 0f;
        }
        return hasWeight || Fail("tier has no positive composition weight", out reason);
    }

    static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    static bool Fail(string message, out string reason) { reason = message; return false; }
}

/// <summary>One weighted vehicle/behavior pairing that a police heat tier may request.</summary>
[System.Serializable]
public sealed class PoliceCompositionEntry {
    /// <summary>Stable vehicle profile identifier resolved by the police catalog.</summary>
    public string vehicleProfileId;

    /// <summary>Stable behavior profile identifier resolved by the police catalog.</summary>
    public string behaviorProfileId;

    /// <summary>Tactical role selected for the requested police life.</summary>
    public PoliceTacticalRole tacticalRole;

    /// <summary>Relative selection weight inside the tier.</summary>
    [Min(0f)] public float weight = 1f;

    /// <summary>Optional per-entry cap; zero means the tier has no extra entry cap.</summary>
    [Min(0)] public int maximumCount;

    /// <summary>Validates stable IDs and finite selection values.</summary>
    /// <param name="reason">Stable failure reason when validation fails.</param>
    /// <returns>True when this entry can be selected.</returns>
    public bool TryValidate(out string reason) {
        reason = null;
        if (string.IsNullOrWhiteSpace(vehicleProfileId) || string.IsNullOrWhiteSpace(behaviorProfileId))
            return Fail("composition profile id is empty", out reason);
        if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f)
            return Fail("composition weight is invalid", out reason);
        if (maximumCount < 0) return Fail("composition maximum count is negative", out reason);
        return true;
    }

    static bool Fail(string message, out string reason) { reason = message; return false; }
}
