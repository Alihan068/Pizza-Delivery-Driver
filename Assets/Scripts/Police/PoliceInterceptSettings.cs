using System;

/// <summary>Authorable bounded prediction values for the police Intercept tactical role.</summary>
[Serializable]
public sealed class PoliceInterceptSettings {
    /// <summary>Seconds of actual target travel used to bound the forward junction scan.</summary>
    public float predictionSeconds = 2f;

    /// <summary>Minimum actual target speed required before Intercept prediction is attempted.</summary>
    public float minimumSpeed = 0.5f;

    /// <summary>Maximum number of stable authored junction candidates considered per attempt.</summary>
    public int maxCandidates = 4;

    /// <summary>Creates an independent runtime snapshot of these provisional settings.</summary>
    public PoliceInterceptSettings Clone() => new PoliceInterceptSettings {
        predictionSeconds = predictionSeconds,
        minimumSpeed = minimumSpeed,
        maxCandidates = maxCandidates
    };

    /// <summary>Validates the provisional tuning without mutating it.</summary>
    /// <param name="reason">Failure reason, or null when every value is valid.</param>
    /// <returns>True when prediction, speed and candidate limits are finite and positive.</returns>
    public bool IsValid(out string reason) {
        reason = null;
        if (!FinitePositive(predictionSeconds) || !FinitePositive(minimumSpeed) || maxCandidates <= 0) {
            reason = "invalid police intercept settings";
            return false;
        }
        return true;
    }

    static bool FinitePositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
}
