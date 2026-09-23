using UnityEngine;

/// <summary>
/// Pure post-impact state machine for a civilian follower. A light touch only holds the car
/// briefly; a heavy hit cuts motor pressure and lets momentum settle before the car looks for a
/// safe way back onto its route. Rejoining is only attempted when the route is near enough and the
/// swept path to it is reported clear; otherwise the car waits calmly and retries on an interval.
/// Reversing is a last resort, only while the road ahead is blocked and the rear is reported
/// clear, and only for a bounded time. Continued contact during recovery never restarts the timer
/// — a car pinned against a wall must not recover "forever". No transform writes anywhere: the
/// policy only says what the follower should be doing.
/// </summary>
public sealed class CrashRecoveryPolicy {
    /// <summary>What the follower should do this step.</summary>
    public enum Phase {
        /// <summary>Normal route following.</summary>
        Driving,
        /// <summary>Light contact: hold the brake briefly.</summary>
        HoldingAfterContact,
        /// <summary>Heavy impact: motor pressure cut, momentum settling.</summary>
        Settling,
        /// <summary>Waiting for a clear rejoin path (or for the route to be within reach); retry on an interval.</summary>
        WaitingForRejoin,
        /// <summary>Backing up to open space ahead; bounded.</summary>
        Reversing
    }

    /// <summary>Authored thresholds and durations.</summary>
    public readonly struct Parameters {
        public readonly float lightImpactSpeed;
        public readonly float heavyImpactSpeed;
        public readonly float lightHoldSeconds;
        public readonly float settleSpeed;
        public readonly float settleTimeoutSeconds;
        public readonly float rejoinRetrySeconds;
        public readonly float reverseMaxSeconds;
        public readonly float maxRejoinDistance;
        /// <summary>Minimum settling duration before a low-speed recovery may request rejoin.</summary>
        public readonly float minimumSettleSeconds;

        /// <summary>Creates bounded crash-recovery thresholds and durations.</summary>
        /// <param name="lightImpactSpeed">Minimum closing speed that counts as contact.</param>
        /// <param name="heavyImpactSpeed">Closing speed that starts full settling recovery.</param>
        /// <param name="lightHoldSeconds">Duration of the light-contact brake hold.</param>
        /// <param name="settleSpeed">Speed at or below which momentum is considered settled.</param>
        /// <param name="settleTimeoutSeconds">Maximum settling duration before a rejoin decision.</param>
        /// <param name="rejoinRetrySeconds">Delay between refused rejoin attempts.</param>
        /// <param name="reverseMaxSeconds">Maximum duration of one recovery reverse.</param>
        /// <param name="maxRejoinDistance">Maximum route distance eligible for rejoin.</param>
        /// <param name="minimumSettleSeconds">Minimum settling duration before any rejoin decision.</param>
        public Parameters(float lightImpactSpeed, float heavyImpactSpeed, float lightHoldSeconds, float settleSpeed,
            float settleTimeoutSeconds, float rejoinRetrySeconds, float reverseMaxSeconds, float maxRejoinDistance,
            float minimumSettleSeconds = 0f) {
            this.lightImpactSpeed = Mathf.Max(0f, lightImpactSpeed);
            this.heavyImpactSpeed = Mathf.Max(this.lightImpactSpeed, heavyImpactSpeed);
            this.lightHoldSeconds = Mathf.Max(0f, lightHoldSeconds);
            this.settleSpeed = Mathf.Max(0f, settleSpeed);
            this.settleTimeoutSeconds = Mathf.Max(0f, settleTimeoutSeconds);
            this.rejoinRetrySeconds = Mathf.Max(0.01f, rejoinRetrySeconds);
            this.reverseMaxSeconds = Mathf.Max(0f, reverseMaxSeconds);
            this.maxRejoinDistance = Mathf.Max(0f, maxRejoinDistance);
            this.minimumSettleSeconds = Mathf.Max(0f, minimumSettleSeconds);
        }
    }

    /// <summary>What the follower observed about the world when a rejoin is being considered.</summary>
    public readonly struct RejoinObservation {
        /// <summary>Distance from the vehicle to the nearest route point.</summary>
        public readonly float distanceToRoute;
        /// <summary>Whether the swept path from the vehicle to its rejoin point is free of static geometry.</summary>
        public readonly bool pathClear;
        /// <summary>Whether something blocks the vehicle immediately ahead (within its minimum gap).</summary>
        public readonly bool blockedAhead;
        /// <summary>Whether the space behind the vehicle is free to back into.</summary>
        public readonly bool rearClear;

        public RejoinObservation(float distanceToRoute, bool pathClear, bool blockedAhead, bool rearClear) {
            this.distanceToRoute = distanceToRoute;
            this.pathClear = pathClear;
            this.blockedAhead = blockedAhead;
            this.rearClear = rearClear;
        }
    }

    readonly Parameters parameters;
    float phaseTimer;
    float retryTimer;

    public CrashRecoveryPolicy(Parameters parameters) {
        this.parameters = parameters;
    }

    /// <summary>Current phase.</summary>
    public Phase Current { get; private set; } = Phase.Driving;

    /// <summary>True whenever the follower should not be issuing normal route-following commands.</summary>
    public bool IsRecovering => Current != Phase.Driving;

    /// <summary>Seconds spent in the current phase.</summary>
    public float PhaseSeconds => phaseTimer;

    /// <summary>Number of rejoin attempts refused since the last successful rejoin, for stuck handling upstream.</summary>
    public int RefusedRejoinAttempts { get; private set; }

    /// <summary>Back to normal driving with all timers cleared (new life, or explicit reset).</summary>
    public void Reset() {
        Current = Phase.Driving;
        phaseTimer = 0f;
        retryTimer = 0f;
        RefusedRejoinAttempts = 0;
    }

    /// <summary>
    /// Reports one physical impact. Below the light threshold nothing happens; a heavy impact
    /// escalates any lighter phase to settling; impacts during an existing recovery of equal or
    /// higher severity are ignored so continued contact cannot keep restarting the timer.
    /// </summary>
    /// <param name="impactSpeed">Closing speed along the contact normal.</param>
    public void NotifyImpact(float impactSpeed) {
        if (float.IsNaN(impactSpeed) || impactSpeed < parameters.lightImpactSpeed) return;
        bool heavy = impactSpeed >= parameters.heavyImpactSpeed;
        if (heavy) {
            if (Current == Phase.Settling) return; // already settling: continued contact does not restart it
            Enter(Phase.Settling);
            return;
        }
        if (Current == Phase.Driving) Enter(Phase.HoldingAfterContact);
        // a light touch never downgrades or restarts a heavier recovery
    }

    /// <summary>Starts a full recovery without an impact (stuck handling): settles, then looks for a rejoin like after a heavy hit. Ignored while already recovering.</summary>
    public void BeginRecovery() {
        if (Current == Phase.Driving || Current == Phase.HoldingAfterContact) Enter(Phase.Settling);
    }

    /// <summary>
    /// Advances the machine by one step. The observation is only consulted when a rejoin decision
    /// is due (after settling, and on each retry while waiting or after a bounded reverse).
    /// </summary>
    /// <param name="deltaTime">Active session step.</param>
    /// <param name="forwardSpeed">Current absolute forward speed.</param>
    /// <param name="observation">Rejoin inputs gathered by the follower.</param>
    public void Tick(float deltaTime, float forwardSpeed, in RejoinObservation observation) {
        if (deltaTime < 0f || float.IsNaN(deltaTime)) deltaTime = 0f;
        phaseTimer += deltaTime;
        switch (Current) {
            case Phase.Driving:
                return;
            case Phase.HoldingAfterContact:
                if (phaseTimer >= parameters.lightHoldSeconds) Enter(Phase.Driving);
                return;
            case Phase.Settling:
                if (phaseTimer >= parameters.minimumSettleSeconds &&
                    (forwardSpeed <= parameters.settleSpeed || phaseTimer >= parameters.settleTimeoutSeconds))
                    DecideRejoin(observation);
                return;
            case Phase.WaitingForRejoin:
                retryTimer -= deltaTime;
                if (retryTimer <= 0f) DecideRejoin(observation);
                return;
            case Phase.Reversing:
                if (!observation.rearClear || !observation.blockedAhead || phaseTimer >= parameters.reverseMaxSeconds) DecideRejoin(observation);
                return;
        }
    }

    void DecideRejoin(in RejoinObservation observation) {
        bool reachable = observation.distanceToRoute <= parameters.maxRejoinDistance;
        if (reachable && observation.pathClear && !observation.blockedAhead) {
            RefusedRejoinAttempts = 0;
            Enter(Phase.Driving);
            return;
        }
        RefusedRejoinAttempts++;
        if (observation.blockedAhead && observation.rearClear && parameters.reverseMaxSeconds > 0f && Current != Phase.Reversing) {
            Enter(Phase.Reversing);
            return;
        }
        Enter(Phase.WaitingForRejoin);
        retryTimer = parameters.rejoinRetrySeconds;
    }

    void Enter(Phase phase) {
        Current = phase;
        phaseTimer = 0f;
    }
}
