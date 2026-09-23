using UnityEngine;

/// <summary>Pure heat calculations shared by the director and EditMode verification.</summary>
public static class PoliceHeatModel {
    /// <summary>Evaluates baseline heat from active session seconds without wall-clock time.</summary>
    /// <param name="data">Validated director profile.</param>
    /// <param name="activeSeconds">SessionClock seconds; invalid values are treated as zero.</param>
    /// <returns>Non-negative authored baseline heat.</returns>
    public static float EvaluateBaseHeat(PoliceDirectorData data, float activeSeconds) {
        if (data == null) return 0f;
        float safeSeconds = Finite(activeSeconds) ? Mathf.Max(0f, activeSeconds) : 0f;
        return Mathf.Max(0f, data.baseHeatAtStart) + Mathf.Max(0f, data.baseHeatPerActiveSecond) * safeSeconds;
    }

    /// <summary>Combines baseline and incident heat and applies the profile maximum.</summary>
    /// <param name="data">Validated director profile.</param>
    /// <param name="baseHeat">Baseline heat from active time.</param>
    /// <param name="incidentHeat">Accepted player-originated incident heat.</param>
    /// <returns>Effective heat in the authored profile range.</returns>
    public static float EvaluateEffectiveHeat(PoliceDirectorData data, float baseHeat, float incidentHeat) {
        if (data == null) return 0f;
        float safeBase = Finite(baseHeat) ? Mathf.Max(0f, baseHeat) : 0f;
        float safeIncident = Finite(incidentHeat) ? Mathf.Max(0f, incidentHeat) : 0f;
        return Mathf.Clamp(safeBase + safeIncident, 0f, Mathf.Max(0f, data.maximumHeat));
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
