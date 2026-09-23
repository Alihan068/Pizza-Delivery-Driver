using UnityEngine;

/// <summary>Authored tuning for a <see cref="CivilianRouteFollower"/>: route tracking, speed planning, car-following, junction behaviour, crash recovery and stuck handling. Motor limits live in <see cref="NpcMotorSettings"/>.</summary>
[System.Serializable]
public class CivilianFollowerSettings {
    [Tooltip("Shortest lookahead distance (world units) even when stopped.")]
    public float lookaheadMin = 2.5f;

    [Tooltip("Longest lookahead distance (world units) at any speed.")]
    public float lookaheadMax = 8f;

    [Tooltip("Extra lookahead per unit of forward speed, in seconds (lookahead = min + speed × this).")]
    public float lookaheadTime = 0.5f;

    [Tooltip("Direction change (degrees) at a polyline vertex that counts as a corner the lookahead must not aim across.")]
    public float cornerAngleThreshold = 30f;

    [Tooltip("How far past an upcoming corner vertex the lookahead point may reach (world units).")]
    public float cornerLookaheadMargin = 1f;

    [Tooltip("Angle (degrees) between vehicle forward and the aim point at which steering saturates to full lock.")]
    public float fullLockAngle = 25f;

    [Tooltip("Arc length (world units) the cursor searches forward each step for its projection; must exceed the distance travelled in one physics step at top speed.")]
    public float transitionSearchWindow = 6f;

    [Tooltip("Lateral acceleration (world units/s²) the vehicle accepts in a full corner; corner speed = sqrt(this × minimum turning radius).")]
    public float cornerLateralAcceleration = 4f;

    [Tooltip("Turn angle (degrees) at which the full corner slowdown applies; gentler bends interpolate between cruise and corner speed.")]
    public float fullSlowdownAngle = 90f;

    [Tooltip("Fraction of the motor's brake deceleration the speed plan assumes when computing braking distance (lower = brake earlier, smoother).")]
    [Range(0.1f, 1f)] public float brakingComfortFactor = 0.7f;

    [Tooltip("Speed excess (world units/s) above the target at which the brake pedal reaches full; smaller excess brakes proportionally.")]
    public float brakeBand = 1.5f;

    [Tooltip("Arc length (world units) ahead within which upcoming corners and edge speed limits shape the speed target.")]
    public float speedPlanningDistance = 20f;

    [Tooltip("Extra road width (world units) required beyond the vehicle's own width before a route is accepted.")]
    public float widthSafetyMargin = 0.5f;

    [Tooltip("Largest direction change (degrees) at any route transition this follower will attempt; sharper routes are refused at bind time.")]
    public float maxTurnAngle = 135f;

    [Tooltip("Fraction of the motor's minimum gap below which the follower brakes at full pedal regardless of the speed plan.")]
    [Range(0.1f, 1f)] public float emergencyGapFraction = 0.5f;

    [Tooltip("Extra clearance (world units) beyond the minimum gap the road ahead must open up before a stopped follower pulls away again; prevents stop/go chatter in a queue.")]
    public float resumeGapHysteresis = 1f;

    [Tooltip("Forward speed (world units/s) at or below which the follower counts as stopped behind an obstacle.")]
    public float stoppedSpeedThreshold = 0.1f;

    [Tooltip("Distance (world units) before a junction's stop line at which the follower starts asking for the zone.")]
    public float junctionApproachDistance = 10f;

    [Tooltip("Extra distance (world units) the follower keeps short of a junction's conflict zone while waiting for it.")]
    public float junctionStopMargin = 0.5f;

    [Tooltip("Distance (world units) the follower must travel past the zone boundary on its exit edge before it releases the junction; roughly vehicle length plus margin.")]
    public float junctionClearMargin = 4f;

    [Tooltip("Closing speed (world units/s) below which a contact is ignored by the driving logic.")]
    public float lightImpactSpeed = 1f;

    [Tooltip("Closing speed (world units/s) at or above which an impact cuts motor pressure and starts a full recovery.")]
    public float heavyImpactSpeed = 3f;

    [Tooltip("Seconds the car holds the brake after a light touch before resuming.")]
    public float lightContactHoldSeconds = 0.6f;

    [Tooltip("Forward speed (world units/s) at or below which post-impact momentum counts as settled.")]
    public float settleSpeed = 1f;

    /// <summary>Minimum seconds a heavy civilian crash remains settling before rejoin is considered.</summary>
    [Tooltip("Minimum seconds a heavy crash remains in settling before a rejoin decision is allowed.")]
    public float minimumCrashWaitSeconds = 3f;

    [Tooltip("Longest time (seconds) the car waits for momentum to settle before looking for a rejoin anyway.")]
    public float settleTimeoutSeconds = 2f;

    [Tooltip("Gentle brake (0..1) applied while momentum settles after a heavy impact.")]
    [Range(0f, 1f)] public float settleBrake = 0.3f;

    [Tooltip("Seconds between rejoin attempts while the way back to the route is blocked.")]
    public float rejoinRetrySeconds = 1f;

    [Tooltip("Longest single reverse manoeuvre (seconds) during recovery; 0 disables reversing entirely.")]
    public float reverseMaxSeconds = 1.5f;

    [Tooltip("Distance (world units) the rear sweep checks before a recovery reverse is allowed.")]
    public float reverseClearanceDistance = 3f;

    [Tooltip("Farthest distance (world units) from the route at which the car still tries to drive back onto it; beyond this it waits for stuck handling.")]
    public float maxRejoinDistance = 8f;

    [Tooltip("Route progress (world units/s) below which the car counts as not progressing for stuck detection.")]
    public float stuckProgressThreshold = 0.2f;

    [Tooltip("Seconds without route progress before a local recovery is tried and a recycle becomes eligible.")]
    public float stuckSeconds = 8f;

    [Tooltip("Seconds without progress after which the recycle cooldown no longer applies (still never in view).")]
    public float maxStuckSeconds = 30f;

    [Tooltip("Minimum seconds between stuck recycles this car honours.")]
    public float stuckRecycleCooldownSeconds = 5f;

    [Tooltip("Player distance (world units) below which a stuck car is never recycled, even off screen.")]
    public float stuckRecycleMinPlayerDistance = 25f;
}
