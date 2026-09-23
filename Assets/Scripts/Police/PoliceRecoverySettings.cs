using System;

/// <summary>Serializable finite authoring limits for one bounded police recovery episode.</summary>
[Serializable]
public sealed class PoliceRecoverySettings {
    /// <summary>Impact speed at which the recovery policy begins its light response.</summary>
    public float lightImpactSpeed = 1f;
    /// <summary>Impact speed at which the recovery policy begins its heavy response.</summary>
    public float heavyImpactSpeed = 3f;
    /// <summary>Time held in the light recovery response.</summary>
    public float lightHoldSeconds = 0.6f;
    /// <summary>Speed threshold below which settling may complete.</summary>
    public float settleSpeed = 1f;
    /// <summary>Maximum bounded settling observation time.</summary>
    public float settleTimeoutSeconds = 2f;
    /// <summary>Throttle-free braking command used during settling.</summary>
    public float settleBrake = 0.3f;
    /// <summary>Minimum interval between expensive recovery observations.</summary>
    public float rejoinRetrySeconds = 1f;
    /// <summary>Maximum distance from the directed gate allowed for rejoin.</summary>
    public float maxRejoinDistance = 8f;
    /// <summary>Maximum duration of one bounded reverse command.</summary>
    public float reverseMaxSeconds = 1.5f;
    /// <summary>Forward recovery speed cap used while rejoining.</summary>
    public float maneuverSpeed = 1.5f;
    /// <summary>Maximum measured travel allowed during one reverse maneuver.</summary>
    public float maximumReverseTravel = 3f;
    /// <summary>Maximum duration of the complete nonrenewable recovery episode.</summary>
    public float maximumActiveSeconds = 8f;

    /// <summary>Validates finite recovery values, cross-field bounds, and the existing stopped threshold.</summary>
    /// <param name="stoppedSpeedThreshold">Existing driving threshold that settling must not undercut.</param>
    /// <param name="reason">Stable English diagnostic explaining the first invalid constraint.</param>
    /// <returns>True only when every recovery setting is finite and bounded.</returns>
    public bool IsValid(float stoppedSpeedThreshold, out string reason) {
        reason = null;
        if (!Finite(stoppedSpeedThreshold) || stoppedSpeedThreshold < 0f) {
            reason = "recovery stopped speed threshold is invalid";
            return false;
        }
        if (!Finite(lightImpactSpeed) || !Finite(heavyImpactSpeed) || !Finite(lightHoldSeconds) || !Finite(settleSpeed) ||
            !Finite(settleTimeoutSeconds) || !Finite(settleBrake) || !Finite(rejoinRetrySeconds) || !Finite(maxRejoinDistance) ||
            !Finite(reverseMaxSeconds) || !Finite(maneuverSpeed) || !Finite(maximumReverseTravel) || !Finite(maximumActiveSeconds)) {
            reason = "police recovery settings contain a non-finite value";
            return false;
        }
        if (lightImpactSpeed < 0f || heavyImpactSpeed < 0f || heavyImpactSpeed < lightImpactSpeed ||
            lightHoldSeconds <= 0f || settleSpeed <= 0f || settleTimeoutSeconds <= 0f || settleBrake <= 0f || settleBrake > 1f ||
            rejoinRetrySeconds <= 0f || maxRejoinDistance <= 0f || reverseMaxSeconds < 0f || maneuverSpeed <= 0f ||
            maximumReverseTravel <= 0f || maximumActiveSeconds <= 0f) {
            reason = "police recovery settings contain an out-of-range value";
            return false;
        }
        if (settleSpeed < stoppedSpeedThreshold) {
            reason = "recovery settle speed is below the stopped speed threshold";
            return false;
        }
        if (maximumActiveSeconds < lightHoldSeconds || maximumActiveSeconds < settleTimeoutSeconds) {
            reason = "recovery active duration is shorter than a required phase";
            return false;
        }
        if (reverseMaxSeconds > maximumActiveSeconds) {
            reason = "recovery reverse duration exceeds the active duration";
            return false;
        }
        return true;
    }

    /// <summary>Creates a detached copy of every authored recovery value.</summary>
    /// <returns>A new serializable recovery settings object.</returns>
    public PoliceRecoverySettings Clone() {
        return new PoliceRecoverySettings {
            lightImpactSpeed = lightImpactSpeed, heavyImpactSpeed = heavyImpactSpeed,
            lightHoldSeconds = lightHoldSeconds, settleSpeed = settleSpeed,
            settleTimeoutSeconds = settleTimeoutSeconds, settleBrake = settleBrake,
            rejoinRetrySeconds = rejoinRetrySeconds, maxRejoinDistance = maxRejoinDistance,
            reverseMaxSeconds = reverseMaxSeconds, maneuverSpeed = maneuverSpeed,
            maximumReverseTravel = maximumReverseTravel, maximumActiveSeconds = maximumActiveSeconds
        };
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
