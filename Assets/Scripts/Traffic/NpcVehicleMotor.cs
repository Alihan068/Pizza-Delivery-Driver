using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The single force/torque-based physics writer for one NPC's Rigidbody2D. Every civilian/police
/// controller (route follower, pursuit) only ever writes an <see cref="NpcDriveCommand"/> here —
/// nothing else touches this Rigidbody2D's velocity/rotation directly, so a physical impact's
/// momentum is never silently overwritten by a stale follow command. Never sets velocity/rotation
/// directly to a target: lowering targetSpeed only removes forward force, it never snaps velocity
/// down, and turning is a speed-and-radius-limited MoveRotation, never a per-frame teleport to a
/// waypoint heading.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public sealed class NpcVehicleMotor : MonoBehaviour {
    [SerializeField] NpcMotorSettings settings;

    [Range(0f, 1f)]
    [Tooltip("Multiplies engine/brake force while in crash mode, so a dazed vehicle doesn't fight its way back to speed immediately after an impact.")]
    public float crashModeForceMultiplier = 0.35f;

    Rigidbody2D rb;
    NpcDriveCommand command = NpcDriveCommand.Stopped;
    bool crashMode;
    bool stoppedPermanently;
    bool reverseManeuverPermitted;

    public bool IsCrashMode => crashMode;

    /// <summary>True once StopMovement (death/session-end) has fired and not yet been cleared by ResetForNewLife — a normal SetCommand(Stopped) never sets this.</summary>
    public bool IsStoppedPermanently => stoppedPermanently;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
    }

    /// <summary>Assigns (or replaces) the motor tuning this instance drives with.</summary>
    public void Configure(NpcMotorSettings motorSettings) {
        settings = motorSettings;
    }

    /// <summary>
    /// Assigns this instance's Rigidbody2D mass, resolved once by whatever composes a profile with
    /// this motor (S06+). Only affects collision push/momentum — never this vehicle's own
    /// acceleration target, since every force here is already scaled by rb.mass to cancel it out.
    /// </summary>
    public void SetMass(float mass) {
        if (rb != null && !float.IsNaN(mass) && !float.IsInfinity(mass) && mass > 0f) rb.mass = mass;
    }

    /// <summary>Sets this frame's driving intent. Ignored outright after StopMovement (death/session-end) — a gas command has no effect on a stopped-permanently vehicle. Never applied immediately either way — only read on the next FixedUpdate.</summary>
    public void SetCommand(NpcDriveCommand newCommand) {
        if (stoppedPermanently) return;
        command = newCommand;
    }

    /// <summary>Reduces motor force while true. Toggled by whatever detects the impact; this class only reacts to the flag.</summary>
    public void SetCrashMode(bool value) {
        crashMode = value;
    }

    /// <summary>
    /// Opens or closes reverse maneuvering as a whole, independent of any single command's own
    /// reverseAllowed flag — both must be true for reverse to actually happen. Whatever owns the
    /// recovery-state decision and rear-sensor clearance (VehicleObstacleSensor, S04.4) is the only
    /// thing meant to call this; a command's own reverseAllowed is the issuer's momentary intent,
    /// this is the structural "is reverse even safe/permitted right now" gate. Closed by default.
    /// </summary>
    public void SetReverseManeuverPermission(bool permitted) {
        reverseManeuverPermitted = permitted;
    }

    /// <summary>
    /// Hard-stops the vehicle for death/session-end — distinct from a normal SetCommand(Stopped),
    /// which still lets a later SetCommand resume driving. After this, every SetCommand is ignored
    /// until ResetForNewLife clears the flag for pool reuse.
    /// </summary>
    public void StopMovement() {
        stoppedPermanently = true;
        command = NpcDriveCommand.Stopped;
        if (rb != null) {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    /// <summary>Clears velocity/angular velocity and all driving/crash/permanent-stop state for pool reuse. Wired as an S03 NpcVehiclePool reset hook by whatever composes motor+pool (S04.5+).</summary>
    public void ResetForNewLife() {
        if (rb != null) {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        command = NpcDriveCommand.Stopped;
        crashMode = false;
        stoppedPermanently = false;
        reverseManeuverPermitted = false;
    }

    void FixedUpdate() {
        SimulateStep(Time.fixedDeltaTime);
    }

    void SimulateStep(float deltaTime) {
        if (rb == null || settings == null) return;
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
        ApplyLateralGrip();
        ApplyDrive(deltaTime);
        ApplyTurn(deltaTime);
    }

    void ApplyLateralGrip() {
        Vector2 forward = transform.up;
        Vector2 velocity = rb.linearVelocity;
        float forwardSpeed = Vector2.Dot(velocity, forward);
        Vector2 forwardVelocity = forward * forwardSpeed;
        Vector2 lateralVelocity = velocity - forwardVelocity;
        rb.linearVelocity = forwardVelocity + lateralVelocity * (1f - settings.lateralGrip);
    }

    void ApplyDrive(float deltaTime) {
        Vector2 forward = transform.up;
        float forwardSpeed = Vector2.Dot(rb.linearVelocity, forward);
        float forceMultiplier = crashMode ? crashModeForceMultiplier : 1f;

        if (command.brake > 0f) {
            float brakeForceMag = Mathf.Min(rb.mass * settings.brakeDeceleration, settings.maxBrakeForce) * command.brake * forceMultiplier;
            // Limit this step's braking impulse to the remaining longitudinal momentum.
            // A fixed speed threshold cannot prevent overshoot across masses and timesteps.
            float stoppingForce = rb.mass * Mathf.Abs(forwardSpeed) / deltaTime;
            brakeForceMag = Mathf.Min(brakeForceMag, stoppingForce);
            rb.AddForce(-Mathf.Sign(forwardSpeed) * forward * brakeForceMag);
            return; // brake never converts into an autonomous reverse push
        }

        if (Mathf.Approximately(command.throttle, 0f)) return;

        bool reversing = command.throttle < 0f;
        if (reversing && (!command.reverseAllowed || !reverseManeuverPermitted)) return;

        float speedCap = reversing ? Mathf.Min(settings.reverseSpeed, command.targetSpeed) : Mathf.Min(settings.maxSpeed, command.targetSpeed);
        float speedInCommandedDirection = reversing ? -forwardSpeed : forwardSpeed;
        if (speedInCommandedDirection >= speedCap) return; // governor: already at/above the commanded speed this way

        float engineForceMag = Mathf.Min(rb.mass * settings.acceleration, settings.maxEngineForce) * Mathf.Abs(command.throttle) * forceMultiplier;
        rb.AddForce(forward * engineForceMag * Mathf.Sign(command.throttle));
    }

    void ApplyTurn(float deltaTime) {
        if (Mathf.Approximately(command.steering, 0f)) return;

        float forwardSpeedAbs = Mathf.Abs(Vector2.Dot(rb.linearVelocity, (Vector2)transform.up));
        float radiusLimitedTurnRate = forwardSpeedAbs > 0.01f
            ? (forwardSpeedAbs / Mathf.Max(0.01f, settings.minimumTurningRadius)) * Mathf.Rad2Deg
            : settings.turnRate;
        float turnRateThisStep = Mathf.Min(settings.turnRate, radiusLimitedTurnRate);

        float turnThisStep = turnRateThisStep * command.steering * deltaTime;
        rb.MoveRotation(rb.rotation + turnThisStep);
    }

#if UNITY_EDITOR
    /// <summary>Initializes Rigidbody caching through the normal private Awake path only in a non-playing isolated 2D physics scene.</summary>
    public bool InitializeEditorPreview(PhysicsScene2D previewPhysicsScene) {
        if (Application.isPlaying || !previewPhysicsScene.IsValid() || previewPhysicsScene == Physics2D.defaultPhysicsScene || gameObject.scene.GetPhysicsScene2D() != previewPhysicsScene) return false;
        Awake();
        return rb != null;
    }

    /// <summary>Runs the normal private motor step for one finite editor delta without reflection, rejecting Play Mode, default physics, foreign scenes, or an unresolved Rigidbody2D.</summary>
    public bool TickEditorPreview(float deltaTime, PhysicsScene2D previewPhysicsScene) {
        if (Application.isPlaying || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || !previewPhysicsScene.IsValid() || previewPhysicsScene == Physics2D.defaultPhysicsScene || gameObject.scene.GetPhysicsScene2D() != previewPhysicsScene || rb == null) return false;
        SimulateStep(deltaTime);
        return true;
    }
#endif
}
