using UnityEngine;

/// <summary>Persistent arcade pursuit policy with retained serialized values from the retired relief draft.</summary>
public enum PolicePursuitLossPolicy {
    /// <summary>Keep pursuit active for the shift; no incident heat decay or respite is applied.</summary>
    Persistent,
    /// <summary>Legacy serialized value retained for compatibility; it no longer enables relief.</summary>
    ReliefAfterEscape
}

/// <summary>Legacy serialized loss settings retained for compatibility; persistent police ignores them.</summary>
[System.Serializable]
public sealed class PolicePursuitLossSettings {
    /// <summary>Retained loss-distance value; ignored by persistent pursuit.</summary>
    [Min(0f)] public float minimumLostDistance = 12f;
    /// <summary>Retained loss-duration value; ignored by persistent pursuit.</summary>
    [Min(0f)] public float lostDurationSeconds = 4f;
    /// <summary>Legacy serialized value retained for compatibility; incident heat never decays.</summary>
    [Min(0f)] public float incidentHeatDecayPerSecond = 1f;
}

/// <summary>
/// Compatibility surface for the retired pursuit-loss relief scaffold. The active product policy is
/// persistent omniscient police, so these methods deliberately cannot enter relief or reduce heat.
/// </summary>
public static class PolicePursuitLossRules {
    /// <summary>Preserves the legacy query signature while denying relief for every policy value.</summary>
    /// <param name="policy">Explicit product policy.</param>
    /// <param name="settings">Authored distance/time thresholds.</param>
    /// <param name="distanceFromPlayer">Current measured distance.</param>
    /// <param name="secondsWithoutContact">Active seconds without valid pursuit contact.</param>
    /// <returns>Always false because pursuit loss never grants relief.</returns>
    public static bool IsReliefReady(PolicePursuitLossPolicy policy, PolicePursuitLossSettings settings,
        float distanceFromPlayer, float secondsWithoutContact) {
        return false;
    }

    /// <summary>Legacy compatibility method that preserves incident heat because decay is retired.</summary>
    /// <param name="currentIncidentHeat">Current incident heat.</param>
    /// <param name="settings">Authored decay rate.</param>
    /// <param name="deltaSeconds">Active respite duration.</param>
    /// <returns>The non-negative current heat, unchanged by the retired relief scaffold.</returns>
    public static float DecayIncidentHeat(float currentIncidentHeat, PolicePursuitLossSettings settings, float deltaSeconds) {
        return Finite(currentIncidentHeat) ? Mathf.Max(0f, currentIncidentHeat) : 0f;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
