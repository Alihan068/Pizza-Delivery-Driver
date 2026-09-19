using UnityEngine;

/// <summary>
/// Pure car-following decision used by <see cref="CivilianRouteFollower"/>: given the estimated
/// clearance to whatever is ahead and that thing's forward speed, the speed this vehicle may hold
/// so it can still brake down to the leader's speed by the time the gap closes to the minimum gap.
/// Inside the emergency fraction of the minimum gap the answer is a full stop. A vehicle that has
/// come to rest behind an obstacle is held (blocked) until the gap re-opens by the resume
/// hysteresis, so a queue never chatters and the last car never creeps into the one ahead. The
/// rules never produce a reverse intent — waiting is always the answer to a blocked road.
/// </summary>
public static class ObstacleFollowingRules {
    /// <summary>Tuning slice the rules need; copied from follower/motor settings by the caller.</summary>
    public readonly struct Parameters {
        /// <summary>Clearance the vehicle keeps to the leader once matched.</summary>
        public readonly float minimumGap;
        /// <summary>Fraction of minimumGap at/below which the pedal goes to full.</summary>
        public readonly float emergencyGapFraction;
        /// <summary>Extra clearance beyond minimumGap required before a blocked vehicle pulls away.</summary>
        public readonly float resumeGapHysteresis;
        /// <summary>Deceleration the plan assumes is available.</summary>
        public readonly float deceleration;
        /// <summary>Speed at or below which the vehicle counts as stopped.</summary>
        public readonly float stoppedSpeedThreshold;

        public Parameters(float minimumGap, float emergencyGapFraction, float resumeGapHysteresis, float deceleration, float stoppedSpeedThreshold) {
            this.minimumGap = Mathf.Max(0f, minimumGap);
            this.emergencyGapFraction = Mathf.Clamp01(emergencyGapFraction);
            this.resumeGapHysteresis = Mathf.Max(0f, resumeGapHysteresis);
            this.deceleration = Mathf.Max(0.01f, deceleration);
            this.stoppedSpeedThreshold = Mathf.Max(0f, stoppedSpeedThreshold);
        }
    }

    /// <summary>
    /// Allowed forward speed behind an obstacle. Returns +infinity when nothing is ahead.
    /// </summary>
    /// <param name="hasObstacle">Whether anything was detected ahead.</param>
    /// <param name="gap">Estimated clearance to the obstacle's nearest surface.</param>
    /// <param name="obstacleForwardSpeed">Obstacle's speed along this vehicle's forward axis, clamped at 0.</param>
    /// <param name="forwardSpeed">This vehicle's current forward speed.</param>
    /// <param name="parameters">Tuning.</param>
    /// <param name="blocked">Persistent blocked flag; set when the vehicle stops behind the obstacle, cleared when the road re-opens.</param>
    /// <param name="emergency">True when the gap is inside the emergency fraction and a full brake is required now.</param>
    public static float AllowedSpeed(bool hasObstacle, float gap, float obstacleForwardSpeed, float forwardSpeed,
        in Parameters parameters, ref bool blocked, out bool emergency) {
        emergency = false;
        if (!hasObstacle) {
            blocked = false;
            return float.PositiveInfinity;
        }

        gap = float.IsNaN(gap) ? 0f : Mathf.Max(0f, gap);
        obstacleForwardSpeed = float.IsNaN(obstacleForwardSpeed) ? 0f : Mathf.Max(0f, obstacleForwardSpeed);

        if (gap <= parameters.minimumGap * parameters.emergencyGapFraction) {
            emergency = true;
            blocked = true;
            return 0f;
        }

        if (blocked) {
            if (gap < parameters.minimumGap + parameters.resumeGapHysteresis) return 0f; // hold until the road really opens
            blocked = false;
        }

        float allowed = gap <= parameters.minimumGap
            ? obstacleForwardSpeed
            : Mathf.Sqrt(obstacleForwardSpeed * obstacleForwardSpeed + 2f * parameters.deceleration * (gap - parameters.minimumGap));
        if (allowed <= parameters.stoppedSpeedThreshold && forwardSpeed <= parameters.stoppedSpeedThreshold) blocked = true;
        return allowed;
    }

    /// <summary>Conservative gap estimate between sensor polls: assumes both parties kept their speed, never credits the obstacle for pulling away.</summary>
    public static float ExtrapolateGap(float gapAtQuery, float forwardSpeed, float obstacleForwardSpeed, float timeSinceQuery) {
        return Mathf.Max(0f, gapAtQuery - Mathf.Max(0f, forwardSpeed - Mathf.Max(0f, obstacleForwardSpeed)) * Mathf.Max(0f, timeSinceQuery));
    }
}
