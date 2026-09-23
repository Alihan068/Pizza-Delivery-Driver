using UnityEngine;

/// <summary>Pure bounded recovery state machine for free-drive police movement.</summary>
public sealed class PoliceFreeRecovery {
    /// <summary>Recovery phases owned by this policy.</summary>
    public enum State { Pursuing, Avoiding, Braking, CrashWait, Reversing, Replanning }
    readonly PoliceDrivingSettings settings;
    float waitTime;
    float reverseTime;
    float reverseDistance;
    float cooldown;
    float stuckTime;
    float resumedDistance;
    int attempts;
    bool aggressiveImpact;

    /// <summary>Current bounded recovery state.</summary>
    public State CurrentState { get; private set; } = State.Pursuing;
    /// <summary>True when a fresh free path should be requested.</summary>
    public bool NeedsReplan { get; private set; }
    /// <summary>Creates a recovery policy from detached authored settings.</summary>
    public PoliceFreeRecovery(PoliceDrivingSettings driving) { settings = driving != null ? driving.Clone() : null; }
    /// <summary>Clears all clocks, attempts, path intent, and recovery ownership for reuse.</summary>
    public void Reset() { waitTime = 0f; reverseTime = 0f; reverseDistance = 0f; cooldown = 0f; stuckTime = 0f; resumedDistance = 0f; attempts = 0; aggressiveImpact = false; CurrentState = State.Pursuing; NeedsReplan = false; }
    /// <summary>Reports an actual impact and enters an active crash wait.</summary>
    public void ReportImpact() {
        if (settings == null || CurrentState == State.CrashWait) return;
        CurrentState = State.CrashWait;
        waitTime = 0f;
        NeedsReplan = false;
    }

    /// <summary>Reports an impact for reckless police: retain only a short stun before pursuit resumes.</summary>
    public void ReportAggressiveImpact() {
        if (settings == null) return;
        CurrentState = State.CrashWait;
        waitTime = 0f;
        aggressiveImpact = true;
        NeedsReplan = false;
    }

    /// <summary>Starts a bounded escape immediately when an obstacle, rather than the player, has trapped the body.</summary>
    public void BeginImmediateEscape() {
        if (settings == null || CurrentState == State.CrashWait) return;
        waitTime = 0f;
        stuckTime = 0f;
        NeedsReplan = false;
        CurrentState = State.Braking;
    }
    /// <summary>Advances recovery using actual measured displacement and rear-clear evidence.</summary>
    public NpcDriveCommand Step(float deltaTime, float speed, float measuredDistance, bool rearClear, bool blocked, bool pendingOrBudgetExceeded) {
        NeedsReplan = false; if (settings == null || settings.recovery == null || deltaTime <= 0f || !Finite(deltaTime) ||
            !Finite(speed) || !Finite(measuredDistance) || measuredDistance < 0f) return NpcDriveCommand.Stopped;
        cooldown = Mathf.Max(0f, cooldown - deltaTime);
        if (CurrentState == State.Pursuing && !blocked && !pendingOrBudgetExceeded && speed > settings.recovery.settleSpeed) {
            resumedDistance += measuredDistance;
            if (resumedDistance >= settings.reverseMaxDistance) { attempts = 0; resumedDistance = 0f; }
        } else resumedDistance = 0f;
        if (blocked && !pendingOrBudgetExceeded) stuckTime += deltaTime; else stuckTime = 0f;
        if ((CurrentState == State.Pursuing || CurrentState == State.Avoiding) && blocked &&
            stuckTime >= settings.stuckDuration && cooldown <= 0f) { waitTime = 0f; CurrentState = State.Braking; }
        switch (CurrentState) {
            case State.CrashWait:
                waitTime += deltaTime;
                if (aggressiveImpact) {
                    if (waitTime < Mathf.Min(settings.recovery.lightHoldSeconds, 0.25f))
                        return new NpcDriveCommand(0f, 1f, 0f, 0f, false);
                    aggressiveImpact = false;
                    CurrentState = State.Pursuing;
                    return NpcDriveCommand.Stopped;
                }
                if (waitTime < settings.recovery.lightHoldSeconds ||
                    (Mathf.Abs(speed) > settings.recovery.settleSpeed && waitTime < settings.recovery.settleTimeoutSeconds))
                    return new NpcDriveCommand(0f, 1f, 0f, 0f, false);
                waitTime = 0f;
                CurrentState = State.Braking;
                return NpcDriveCommand.Stopped;
            case State.Braking:
                waitTime += deltaTime;
                if (attempts >= settings.reverseMaxAttempts || cooldown > 0f || waitTime >= settings.recovery.settleTimeoutSeconds) {
                    cooldown = settings.reverseCooldown;
                    CurrentState = State.Replanning;
                    NeedsReplan = true;
                    return NpcDriveCommand.Stopped;
                }
                if (Mathf.Abs(speed) > settings.recovery.settleSpeed)
                    return new NpcDriveCommand(0f, settings.recovery.settleBrake, 0f, 0f, false);
                if (!rearClear) return new NpcDriveCommand(0f, 1f, 0f, 0f, false);
                attempts++;
                waitTime = 0f;
                reverseTime = 0f;
                reverseDistance = 0f;
                CurrentState = State.Reversing;
                goto case State.Reversing;
            case State.Reversing: reverseTime += deltaTime; reverseDistance += Mathf.Max(0f, measuredDistance); if (!rearClear || reverseTime >= settings.reverseMaxDuration || reverseDistance >= settings.reverseMaxDistance) { cooldown = settings.reverseCooldown; CurrentState = State.Replanning; NeedsReplan = true; return NpcDriveCommand.Stopped; } return new NpcDriveCommand(-1f, 0f, 0f, settings.recovery.maneuverSpeed, true);
            case State.Replanning: NeedsReplan = true; return NpcDriveCommand.Stopped;
            default: return NpcDriveCommand.Stopped;
        }
    }
    /// <summary>Marks the bounded local avoidance phase without changing the actual pose.</summary>
    public void BeginAvoiding() { if (CurrentState == State.Pursuing) CurrentState = State.Avoiding; }
    /// <summary>Returns to ordinary pursuit after a valid fresh path is adopted.</summary>
    public void ResumePursuit() { if (CurrentState == State.Avoiding || CurrentState == State.Replanning) { CurrentState = State.Pursuing; NeedsReplan = false; stuckTime = 0f; } }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
