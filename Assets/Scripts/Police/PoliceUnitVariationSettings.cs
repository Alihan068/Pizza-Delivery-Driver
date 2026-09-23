using System;

/// <summary>Authorable bounds for stable, per-life police driver variation.</summary>
[Serializable]
public sealed class PoliceUnitVariationSettings {
    /// <summary>Maximum relative variation applied to reaction timing.</summary>
    public float reactionVariation = 0.1f;
    /// <summary>Maximum relative variation applied to prediction timing.</summary>
    public float predictionVariation = 0.1f;
    /// <summary>Maximum relative variation applied to follow gap.</summary>
    public float followGapVariation = 0.1f;
    /// <summary>Maximum bounded side-preference variation.</summary>
    public float sidePreferenceVariation = 0.1f;
    /// <summary>Maximum relative risk-bias variation.</summary>
    public float riskBiasVariation = 0.1f;

    /// <summary>Creates a detached copy of this authored variation contract.</summary>
    /// <returns>An independent settings snapshot.</returns>
    public PoliceUnitVariationSettings Clone() => new PoliceUnitVariationSettings {
        reactionVariation = reactionVariation,
        predictionVariation = predictionVariation,
        followGapVariation = followGapVariation,
        sidePreferenceVariation = sidePreferenceVariation,
        riskBiasVariation = riskBiasVariation
    };

    /// <summary>Validates finite variation bounds without clamping or mutating them.</summary>
    /// <param name="reason">Failure description, or null when valid.</param>
    /// <returns>True only when all variation bounds are within the authored ten-percent envelope.</returns>
    public bool IsValid(out string reason) {
        reason = null;
        if (!Valid(reactionVariation) || !Valid(predictionVariation) || !Valid(followGapVariation) ||
            !Valid(sidePreferenceVariation) || !Valid(riskBiasVariation)) {
            reason = "invalid police unit variation settings";
            return false;
        }
        return true;
    }

    static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 0.1f;
}
