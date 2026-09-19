using UnityEngine;

/// <summary>
/// Measures how long a civilian has made no real route progress and decides what may be done
/// about it. Stuck time accumulates on low arc-length progress regardless of cause (queued behind
/// the player, waiting at a junction, waiting for a rejoin) and resets only when the vehicle
/// actually moves along its route. After <see cref="Parameters.stuckSeconds"/> the owner may try
/// one legal local recovery; a recycle (pool return + respawn elsewhere) is only ever allowed when
/// the vehicle is out of view, far from the player and the recycle cooldown has passed. Being
/// stuck in front of the player is never a reason to despawn. Past the hard cap the cooldown no
/// longer applies, but the visibility/distance rule still does. A recycle is requested at most
/// once per life.
/// </summary>
public sealed class StuckMonitor {
    /// <summary>What the owner should do for a stuck vehicle right now.</summary>
    public enum Decision {
        /// <summary>Not stuck (yet): nothing to do.</summary>
        None,
        /// <summary>Stuck, but in view / near the player / cooling down: keep waiting where it is.</summary>
        WaitInPlace,
        /// <summary>Stuck, invisible, far and past cooldown: return to the pool and respawn elsewhere. Emitted once per life.</summary>
        RequestRecycle
    }

    /// <summary>Authored thresholds.</summary>
    public readonly struct Parameters {
        /// <summary>Arc-length progress per second below which the vehicle counts as not progressing.</summary>
        public readonly float progressSpeedThreshold;
        /// <summary>Stuck time after which local recovery may be attempted and a recycle becomes eligible.</summary>
        public readonly float stuckSeconds;
        /// <summary>Stuck time after which the recycle cooldown is waived (still never in view).</summary>
        public readonly float maxStuckSeconds;
        /// <summary>Minimum session time between recycle requests across the whole fleet (owner-enforced; this tracks the vehicle's own).</summary>
        public readonly float recycleCooldownSeconds;
        /// <summary>Player distance below which a recycle is never allowed even when off screen.</summary>
        public readonly float minPlayerDistance;

        public Parameters(float progressSpeedThreshold, float stuckSeconds, float maxStuckSeconds, float recycleCooldownSeconds, float minPlayerDistance) {
            this.progressSpeedThreshold = Mathf.Max(0f, progressSpeedThreshold);
            this.stuckSeconds = Mathf.Max(0f, stuckSeconds);
            this.maxStuckSeconds = Mathf.Max(this.stuckSeconds, maxStuckSeconds);
            this.recycleCooldownSeconds = Mathf.Max(0f, recycleCooldownSeconds);
            this.minPlayerDistance = Mathf.Max(0f, minPlayerDistance);
        }
    }

    readonly Parameters parameters;
    float lastRecycleEligibleAt = float.NegativeInfinity;

    public StuckMonitor(Parameters parameters) {
        this.parameters = parameters;
    }

    /// <summary>Seconds of continuous non-progress.</summary>
    public float StuckSeconds { get; private set; }

    /// <summary>True once stuck time reached the stuck threshold.</summary>
    public bool IsStuck => StuckSeconds >= parameters.stuckSeconds && parameters.stuckSeconds >= 0f;

    /// <summary>True once stuck time reached the hard cap.</summary>
    public bool IsBeyondMaxDuration => StuckSeconds >= parameters.maxStuckSeconds;

    /// <summary>True after a local recovery was already triggered for this stuck episode.</summary>
    public bool LocalRecoveryTriggered { get; private set; }

    /// <summary>True after a recycle was requested for this life; never emitted twice.</summary>
    public bool RecycleRequested { get; private set; }

    /// <summary>Clears all state for a new life.</summary>
    public void Reset() {
        StuckSeconds = 0f;
        LocalRecoveryTriggered = false;
        RecycleRequested = false;
        lastRecycleEligibleAt = float.NegativeInfinity;
    }

    /// <summary>Accumulates or resets stuck time from this step's route progress.</summary>
    /// <param name="deltaTime">Active session step.</param>
    /// <param name="arcProgress">Arc length gained along the route this step (negative/NaN counts as none).</param>
    public void Tick(float deltaTime, float arcProgress) {
        if (deltaTime <= 0f || float.IsNaN(deltaTime)) return;
        float rate = float.IsNaN(arcProgress) ? 0f : Mathf.Max(0f, arcProgress) / deltaTime;
        if (rate > parameters.progressSpeedThreshold) {
            StuckSeconds = 0f;
            LocalRecoveryTriggered = false;
            return;
        }
        StuckSeconds += deltaTime;
    }

    /// <summary>
    /// Returns true exactly once per stuck episode when a legal local recovery (rejoin/bounded
    /// reverse) should be attempted before anyone considers a recycle.
    /// </summary>
    public bool TryTriggerLocalRecovery() {
        if (!IsStuck || LocalRecoveryTriggered) return false;
        LocalRecoveryTriggered = true;
        return true;
    }

    /// <summary>Decides whether a stuck vehicle may be recycled now.</summary>
    /// <param name="visibleToPlayer">Whether the vehicle bounds are inside the camera view plus margin.</param>
    /// <param name="distanceToPlayer">World distance to the player.</param>
    /// <param name="now">Active session time.</param>
    public Decision Evaluate(bool visibleToPlayer, float distanceToPlayer, float now) {
        if (!IsStuck || RecycleRequested) return RecycleRequested ? Decision.WaitInPlace : Decision.None;
        if (visibleToPlayer || distanceToPlayer < parameters.minPlayerDistance) return Decision.WaitInPlace;
        if (!IsBeyondMaxDuration && now < lastRecycleEligibleAt + parameters.recycleCooldownSeconds) return Decision.WaitInPlace;
        RecycleRequested = true;
        lastRecycleEligibleAt = now;
        return Decision.RequestRecycle;
    }

    /// <summary>Records that the fleet-wide recycle cooldown started at this time (owner calls after any recycle so this vehicle honours it too).</summary>
    public void NoteFleetRecycle(float now) {
        lastRecycleEligibleAt = Mathf.Max(lastRecycleEligibleAt, now);
    }
}
