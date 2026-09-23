using System;
using UnityEngine;

/// <summary>
/// Owns one finite, non-renewable police recovery episode around the shared crash policy.
/// It records time, physical displacement, and the single reverse credit, but performs no
/// physics query, planner call, motor write, damage operation, or lifecycle mutation.
/// </summary>
public sealed class PoliceRecoveryTraversal {
    readonly PoliceRecoverySettings settings;
    readonly float stoppedSpeedThreshold;
    readonly CrashRecoveryPolicy policy;
    bool configured;
    bool episodeActive;
    bool exhausted;
    bool reverseCreditAvailable;
    bool reverseAttempted;
    bool reverseActive;
    bool reverseBraking;
    bool reverseTravelSettled;
    bool crashObserved;
    bool heavyEscalated;
    bool hasClock;
    bool hasPosition;
    bool observationPending;
    float lastClock;
    float episodeStartClock;
    float episodeElapsed;
    float accumulatedEpisodeSeconds;
    float pendingPolicyDelta;
    float reverseStartClock;
    float reverseSeconds;
    float reverseAccumulatedSeconds;
    float reverseTravel;
    Vector2 lastPosition;

    /// <summary>Creates a detached traversal with validated recovery settings and a fixed stop threshold.</summary>
    /// <param name="recoverySettings">Authored recovery values; cloned and never mutated by this wrapper.</param>
    /// <param name="stoppedThreshold">Existing motor speed threshold used for settling and release.</param>
    public PoliceRecoveryTraversal(PoliceRecoverySettings recoverySettings, float stoppedThreshold) {
        if (recoverySettings == null || !FiniteNonnegative(stoppedThreshold) ||
            !recoverySettings.IsValid(stoppedThreshold, out _)) return;
        settings = recoverySettings.Clone();
        this.stoppedSpeedThreshold = stoppedThreshold;
        policy = new CrashRecoveryPolicy(new CrashRecoveryPolicy.Parameters(
            settings.lightImpactSpeed, settings.heavyImpactSpeed, settings.lightHoldSeconds,
            settings.settleSpeed, settings.settleTimeoutSeconds, settings.rejoinRetrySeconds,
            settings.reverseMaxSeconds, settings.maxRejoinDistance));
        configured = true;
        ResetForNewLife();
    }

    /// <summary>True while the current episode still owns recovery priority, including exhausted wait.</summary>
    public bool IsRecovering => configured && episodeActive;
    /// <summary>True after the finite episode bound has closed all recovery motion.</summary>
    public bool IsExhausted => exhausted;
    /// <summary>True only while the one reverse command credit is currently active.</summary>
    public bool IsReversing => reverseActive && policy.Current == CrashRecoveryPolicy.Phase.Reversing;
    /// <summary>True when policy is reversing and its one non-renewable reverse credit is unused.</summary>
    public bool CanStartReverse => configured && episodeActive && !exhausted && !reverseAttempted &&
        reverseCreditAvailable && policy.Current == CrashRecoveryPolicy.Phase.Reversing;
    /// <summary>Elapsed episode time, using the larger monotonic-clock or accumulated-step measure.</summary>
    public float ActiveSeconds => episodeElapsed;
    /// <summary>Elapsed time since the single reverse attempt began, including its braking tail.</summary>
    public float ReverseSeconds => reverseSeconds;
    /// <summary>Euclidean physical travel observed during reverse and its braking tail only.</summary>
    public float ReverseTravel => reverseTravel;
    /// <summary>Actual shared crash-policy phase; exhaustion is exposed separately.</summary>
    public CrashRecoveryPolicy.Phase Phase => policy != null ? policy.Current : CrashRecoveryPolicy.Phase.Driving;

    /// <summary>
    /// Opens one episode or performs the single permitted light-to-heavy escalation. Later impacts
    /// consume the reverse opportunity and stop active reverse without renewing episode deadlines.
    /// </summary>
    public bool NotifyImpact(float impactSpeed, float clock, Vector2 position) {
        if (!configured || !ValidClockPosition(clock, position) || !FiniteNonnegative(impactSpeed) || (hasClock && clock < lastClock)) {
            if (episodeActive) FailClosed();
            return false;
        }
        if (!episodeActive) {
            if (impactSpeed < settings.lightImpactSpeed || exhausted) return false;
            OpenEpisode(clock, position, impactSpeed >= settings.heavyImpactSpeed);
            return true;
        }

        lastClock = clock;
        hasClock = true;
        if (impactSpeed >= settings.heavyImpactSpeed && !heavyEscalated) {
            policy.NotifyImpact(impactSpeed);
            heavyEscalated = true;
        }
        ConsumeReverseCredit();
        return false;
    }

    /// <summary>
    /// Accounts exactly one active step before any integration query. Invalid or regressing input
    /// fails closed; equal-clock calls still consume finite dt and cannot renew the episode bound.
    /// </summary>
    public bool Account(float clock, float deltaTime, Vector2 position, float actualSpeed, bool crashMode) {
        if (!configured || !ValidClockPosition(clock, position) || !FinitePositive(deltaTime) ||
            !FiniteNonnegative(actualSpeed) || (hasClock && clock < lastClock)) return FailClosed();
        Vector2 previousPosition = lastPosition;
        bool hadPreviousPosition = hasPosition;
        bool trackedPreviousInterval = reverseAttempted && !reverseTravelSettled && (reverseActive || reverseBraking);
        RememberClockPosition(clock, position);
        if (crashMode && !crashObserved && !episodeActive && !exhausted) {
            OpenEpisode(clock, position, true);
        }
        crashObserved = crashMode;
        if (!episodeActive) return true;
        double pendingDelta = (double)pendingPolicyDelta + deltaTime;
        if (!Finite(pendingDelta) || pendingDelta > float.MaxValue) return FailClosed();
        pendingPolicyDelta = (float)pendingDelta;
        observationPending = true;

        double accumulated = (double)accumulatedEpisodeSeconds + deltaTime;
        double clockElapsed = (double)clock - episodeStartClock;
        double elapsed = Math.Max(accumulated, clockElapsed);
        if (!Finite(elapsed) || elapsed > settings.maximumActiveSeconds || elapsed > float.MaxValue) {
            exhausted = true;
            reverseActive = false;
            reverseBraking = reverseAttempted && !reverseTravelSettled && actualSpeed > 0f;
            episodeElapsed = settings.maximumActiveSeconds;
        } else {
            accumulatedEpisodeSeconds = (float)accumulated;
            episodeElapsed = Mathf.Max((float)elapsed, 0f);
            if (episodeElapsed >= settings.maximumActiveSeconds) {
                exhausted = true;
                reverseActive = false;
                reverseBraking = reverseAttempted && !reverseTravelSettled && actualSpeed > 0f;
                episodeElapsed = settings.maximumActiveSeconds;
            }
        }

        if (reverseAttempted && !reverseTravelSettled) {
            double distance = hadPreviousPosition ? Vector2.Distance(previousPosition, position) : 0d;
            if (!Finite(distance) || distance > float.MaxValue) return FailClosed();
            if (trackedPreviousInterval) {
                double travel = (double)reverseTravel + distance;
                double seconds = (double)reverseAccumulatedSeconds + deltaTime;
                double clockSeconds = (double)clock - reverseStartClock;
                if (!Finite(travel) || travel > float.MaxValue || !Finite(seconds) || seconds > float.MaxValue ||
                    !Finite(clockSeconds) || clockSeconds < 0d)
                    return FailClosed();
                reverseTravel = (float)travel;
                reverseAccumulatedSeconds = (float)seconds;
                reverseSeconds = Mathf.Max(reverseSeconds, Mathf.Max(reverseAccumulatedSeconds, (float)clockSeconds));
                if (travel > settings.maximumReverseTravel) return FailClosed();
            }
            if (!reverseActive && actualSpeed == 0f) {
                reverseBraking = false;
                reverseTravelSettled = true;
            }
        }
        if (reverseActive && reverseSeconds >= settings.reverseMaxSeconds) CancelMovement();
        if (exhausted) reverseActive = false;
        return true;
    }

    /// <summary>
    /// Feeds one finite observation into the shared policy after Account. Invalid observations are
    /// represented by finite denied values, and rear clearance is never granted after the credit is spent.
    /// </summary>
    public void ObservePolicy(float actualSpeed, CrashRecoveryPolicy.RejoinObservation inRejoinObservation,
        bool observationValid) {
        if (!configured || !episodeActive || !observationPending) return;
        observationPending = false;
        float policyDelta = pendingPolicyDelta;
        pendingPolicyDelta = 0f;
        if (!FinitePositive(policyDelta) || !FiniteNonnegative(actualSpeed)) {
            policy.Tick(FinitePositive(policyDelta) ? policyDelta : 0f, 0f, DeniedObservation());
            return;
        }
        var observation = observationValid && FiniteObservation(inRejoinObservation)
            ? inRejoinObservation
            : DeniedObservation();
        bool rearClear = observation.rearClear && (reverseActive || (reverseCreditAvailable && !reverseAttempted)) && !exhausted;
        observation = new CrashRecoveryPolicy.RejoinObservation(observation.distanceToRoute,
            observation.pathClear, observation.blockedAhead, rearClear);
        policy.Tick(policyDelta, actualSpeed, observation);
        if (reverseActive && policy.Current != CrashRecoveryPolicy.Phase.Reversing) CancelMovement();
    }

    /// <summary>Spends the one reverse credit only after policy has entered its Reversing phase.</summary>
    public bool TryStartReverse() {
        if (!CanStartReverse) return false;
        reverseCreditAvailable = false;
        reverseAttempted = true;
        reverseActive = true;
        reverseBraking = false;
        reverseTravelSettled = false;
        reverseStartClock = lastClock;
        reverseSeconds = 0f;
        reverseAccumulatedSeconds = 0f;
        reverseTravel = 0f;
        return true;
    }

    /// <summary>Ends commanded reverse without resetting the episode or refunding its credit.</summary>
    public void CancelMovement() {
        reverseActive = false;
        reverseBraking = reverseAttempted && !reverseTravelSettled;
    }

    /// <summary>Pauses recovery motion and freezes its episode accounting until the next Account call.</summary>
    public void Pause() => CancelMovement();

    /// <summary>Releases only at a proven fresh legal resume gate with a settled, non-crash motion state.</summary>
    public bool TryRelease(bool provenFreshResume, bool crashMode, float actualSpeed) {
        if (!configured || !episodeActive || !provenFreshResume || crashMode ||
            !FiniteNonnegative(actualSpeed) || actualSpeed > stoppedSpeedThreshold) return false;
        ResetForNewLife();
        return true;
    }

    /// <summary>Clears all episode, reverse credit, timing, travel, and shared-policy state for a new life.</summary>
    public void ResetForNewLife() {
        episodeActive = false; exhausted = false; reverseCreditAvailable = true; reverseAttempted = false;
        reverseActive = false; reverseBraking = false; reverseTravelSettled = false; crashObserved = false;
        heavyEscalated = false; hasClock = false; hasPosition = false; lastClock = 0f; episodeStartClock = 0f;
        episodeElapsed = 0f; accumulatedEpisodeSeconds = 0f; pendingPolicyDelta = 0f; reverseStartClock = 0f;
        reverseSeconds = 0f; reverseAccumulatedSeconds = 0f; reverseTravel = 0f; lastPosition = Vector2.zero;
        observationPending = false;
        if (configured) policy.Reset();
    }

    void OpenEpisode(float clock, Vector2 position, bool heavy) {
        episodeActive = true; exhausted = false; reverseCreditAvailable = true; reverseAttempted = false;
        reverseActive = false; reverseBraking = false; reverseTravelSettled = false; heavyEscalated = heavy;
        episodeStartClock = clock; accumulatedEpisodeSeconds = 0f; episodeElapsed = 0f;
        pendingPolicyDelta = 0f; observationPending = false;
        lastClock = clock; hasClock = true;
        policy.NotifyImpact(heavy ? settings.heavyImpactSpeed : settings.lightImpactSpeed);
        lastPosition = position; hasPosition = true;
    }

    void ConsumeReverseCredit() {
        reverseCreditAvailable = false;
        if (reverseActive) {
            reverseAttempted = true;
            CancelMovement();
            return;
        }
        if (!reverseAttempted) {
            reverseAttempted = true;
            reverseTravelSettled = true;
        }
    }

    bool FailClosed() {
        exhausted = episodeActive;
        reverseActive = false;
        reverseBraking = reverseAttempted && !reverseTravelSettled;
        observationPending = false;
        return false;
    }

    void RememberClockPosition(float clock, Vector2 position) {
        lastClock = clock; hasClock = true; lastPosition = position; hasPosition = true;
    }

    CrashRecoveryPolicy.RejoinObservation DeniedObservation() => new CrashRecoveryPolicy.RejoinObservation(
        0f, false, false, false);

    static bool ValidClockPosition(float clock, Vector2 position) => FiniteNonnegative(clock) && Finite(position);
    static bool FiniteObservation(CrashRecoveryPolicy.RejoinObservation observation) =>
        FiniteNonnegative(observation.distanceToRoute);
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
