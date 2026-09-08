using UnityEngine;

/// <summary>
/// Owns Rigidbody2D movement for one spawned vehicle.
/// </summary>
/// <remarks>
/// The motor deliberately does not know about money, delivery, UI or save data. It receives the
/// already resolved effective Speed and Handling values from Driver and keeps all physics writes in
/// FixedUpdate. Lateral velocity is reduced through grip, so changing direction never teleports
/// the vehicle's entire momentum.
/// </remarks>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(VehicleInput))]
public class VehicleMovement : MonoBehaviour {

    Rigidbody2D rb;
    VehicleInput vehicleInput;
    VehicleDrivingSettings settings;
    float effectiveMaximumSpeed;
    float effectiveTurnRate;
    float speedMultiplier = 1f;
    float currentGrip;
    float currentAngularRate;
    float reverseRequestTime;
    float driftEnterTimer;
    float driftExitTimer;
    bool initialized;
    bool stopped;
    bool isDrifting;

    /// <summary>Whether actual velocity currently satisfies the drift detector.</summary>
    public bool IsDrifting => isDrifting;

    /// <summary>Current absolute slip angle in degrees.</summary>
    public float SlipAngle { get; private set; }

    /// <summary>Current speed divided by this vehicle's effective maximum speed.</summary>
    public float NormalizedSpeed { get; private set; }

    /// <summary>Current effective forward speed used by the motor and UI.</summary>
    public float ForwardSpeed { get; private set; }

    /// <summary>Initializes this motor once the spawned Driver has resolved its effective stats.</summary>
    /// <param name="input">The single gameplay input source.</param>
    /// <param name="body">The Rigidbody2D owned by this motor.</param>
    /// <param name="maximumSpeed">Resolved Speed stat in world units per second.</param>
    /// <param name="turnRate">Resolved Handling/Turn stat in degrees per second.</param>
    /// <param name="authoredSettings">The vehicle asset's driving settings.</param>
    public void Initialize(VehicleInput input, Rigidbody2D body, float maximumSpeed, float turnRate,
        VehicleDrivingSettings authoredSettings) {
        if (initialized) return;
        vehicleInput = input;
        rb = body != null ? body : GetComponent<Rigidbody2D>();
        settings = authoredSettings != null ? authoredSettings.Clone() : new VehicleDrivingSettings();
        effectiveMaximumSpeed = Mathf.Max(0.01f, maximumSpeed);
        effectiveTurnRate = Mathf.Max(0f, turnRate);
        currentGrip = Mathf.Max(0f, settings.normalGrip);
        if (rb != null) rb.linearDamping = Mathf.Max(0f, settings.linearDamping);
        initialized = rb != null;
        stopped = false;
    }

    /// <summary>Sets the temporary speed multiplier used by obstacle debuffs.</summary>
    /// <param name="multiplier">Multiplier clamped to a safe non-negative range.</param>
    public void SetSpeedMultiplier(float multiplier) {
        speedMultiplier = Mathf.Clamp(multiplier, 0f, 1f);
    }

    /// <summary>Returns the speed currently available after temporary effects.</summary>
    /// <returns>The effective maximum speed.</returns>
    public float GetEffectiveMaximumSpeed() {
        return effectiveMaximumSpeed * speedMultiplier;
    }

    /// <summary>Stops all movement and clears the current input after death or session close.</summary>
    public void StopMovement() {
        stopped = true;
        currentAngularRate = 0f;
        if (vehicleInput != null) vehicleInput.ClearInput();
        if (rb != null) {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    void FixedUpdate() {
        if (!initialized || stopped || rb == null || vehicleInput == null) return;

        VehicleInputSnapshot command = vehicleInput.Snapshot;
        float deltaTime = Time.fixedDeltaTime;
        Vector2 forward = transform.up;
        Vector2 right = transform.right;
        Vector2 velocity = rb.linearVelocity;
        float forwardSpeed = Vector2.Dot(velocity, forward);
        float lateralSpeed = Vector2.Dot(velocity, right);
        float maximumSpeed = GetEffectiveMaximumSpeed();
        float maximumReverseSpeed = maximumSpeed * Mathf.Clamp(settings.reverseSpeedFraction, 0.05f, 0.8f);

        ApplyLongitudinalForces(command, forward, forwardSpeed, maximumSpeed, maximumReverseSpeed, deltaTime);
        ApplyLateralGrip(command, right, lateralSpeed, deltaTime);
        ApplySteering(command, forwardSpeed, maximumSpeed, deltaTime);
        UpdateDriftState(forwardSpeed, lateralSpeed, maximumSpeed, command.steering, command.handbrake, deltaTime);
        ForwardSpeed = forwardSpeed;
        NormalizedSpeed = maximumSpeed > 0.01f ? Mathf.Abs(forwardSpeed) / maximumSpeed : 0f;
    }

    void ApplyLongitudinalForces(VehicleInputSnapshot command, Vector2 forward, float forwardSpeed,
        float maximumSpeed, float maximumReverseSpeed, float deltaTime) {
        float acceleration = VehicleDrivingMath.AccelerationForTime(maximumSpeed, settings.accelerationTime);
        float brakeAcceleration = VehicleDrivingMath.AccelerationForTime(maximumSpeed, settings.brakeTime);
        float coastAcceleration = VehicleDrivingMath.AccelerationForTime(maximumSpeed, settings.coastTime);
        if (command.brake > 0f && forwardSpeed > settings.reverseEntrySpeed) {
            AddAcceleration(-forward * brakeAcceleration * command.brake);
        }
        else if (command.throttle > 0f && forwardSpeed < -settings.reverseEntrySpeed) {
            AddAcceleration(forward * brakeAcceleration * command.throttle);
        }
        else if (command.throttle > 0f) {
            AddAcceleration(forward * acceleration * command.throttle);
        }
        else if (command.brake > 0f && forwardSpeed <= settings.reverseEntrySpeed) {
            reverseRequestTime += deltaTime;
            if (reverseRequestTime >= settings.reverseEntryDelay)
                AddAcceleration(-forward * acceleration * command.brake);
        }
        else {
            reverseRequestTime = 0f;
            if (Mathf.Abs(forwardSpeed) > 0.01f)
                AddAcceleration(-forward * Mathf.Sign(forwardSpeed) * coastAcceleration);
        }

        if (command.handbrake && Mathf.Abs(forwardSpeed) > 0.01f)
            AddAcceleration(-forward * Mathf.Sign(forwardSpeed) * settings.handbrakeDeceleration);

        Vector2 velocity = rb.linearVelocity;
        Vector2 right = transform.right;
        float lateralSpeed = Vector2.Dot(velocity, right);
        float currentForwardSpeed = Vector2.Dot(velocity, transform.up);
        float targetForwardSpeed = VehicleDrivingMath.ClampForwardSpeed(currentForwardSpeed,
            maximumSpeed, maximumReverseSpeed);
        float limiterAcceleration = VehicleDrivingMath.AccelerationForTime(effectiveMaximumSpeed,
            settings.brakeTime);
        float updatedForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetForwardSpeed,
            limiterAcceleration * deltaTime);
        if (!Mathf.Approximately(currentForwardSpeed, updatedForwardSpeed))
            rb.linearVelocity = (Vector2)transform.up * updatedForwardSpeed + right * lateralSpeed;
    }

    /// <summary>
    /// Applies a world-space acceleration while keeping the authored driving settings independent
    /// from the vehicle Rigidbody2D mass.
    /// </summary>
    /// <param name="acceleration">Acceleration vector in world units per second squared.</param>
    /// <remarks>
    /// Unity's 2D force mode applies a force rather than an explicit acceleration. Multiplying by
    /// the cached body mass gives every vehicle the same authored response while still allowing
    /// mass to affect external physics interactions.
    /// </remarks>
    void AddAcceleration(Vector2 acceleration) {
        if (rb == null) return;
        rb.AddForce(acceleration * Mathf.Max(0.01f, rb.mass), ForceMode2D.Force);
    }

    void ApplyLateralGrip(VehicleInputSnapshot command, Vector2 right, float lateralSpeed, float deltaTime) {
        float targetGrip = command.handbrake ? settings.driftGrip : settings.normalGrip;
        float transitionTime = command.handbrake ? settings.gripEnterTime : settings.gripRecoverTime;
        currentGrip = Mathf.MoveTowards(currentGrip, targetGrip,
            Mathf.Abs(settings.normalGrip - settings.driftGrip) * deltaTime / Mathf.Max(0.01f, transitionTime));

        float recoveryAssist = isDrifting ? 1f : 1f + Mathf.Clamp01(settings.handlingRecoveryAssist) * 0.25f;
        AddAcceleration(-right * lateralSpeed * currentGrip * recoveryAssist);
    }

    void ApplySteering(VehicleInputSnapshot command, float forwardSpeed, float maximumSpeed, float deltaTime) {
        float absoluteSpeed = Mathf.Abs(forwardSpeed);
        if (absoluteSpeed <= settings.reverseEntrySpeed) {
            currentAngularRate = Mathf.MoveTowards(currentAngularRate, 0f, effectiveTurnRate * deltaTime /
                Mathf.Max(0.02f, settings.steeringResponseTime));
            rb.MoveRotation(rb.rotation + currentAngularRate * deltaTime);
            return;
        }

        float normalizedSpeed = maximumSpeed > 0.01f ? Mathf.Clamp01(absoluteSpeed / maximumSpeed) : 0f;
        float speedScale = VehicleDrivingMath.SteeringSpeedScale(normalizedSpeed, settings.steeringHighSpeedScale);
        float driftSteeringScale = command.handbrake ? Mathf.Max(1f, settings.driftSteeringMultiplier) : 1f;
        float targetRate = -command.steering * effectiveTurnRate * settings.steeringGain * speedScale * driftSteeringScale;
        if (forwardSpeed < 0f) targetRate *= -1f;
        float response = effectiveTurnRate * deltaTime / Mathf.Max(0.02f, settings.steeringResponseTime);
        currentAngularRate = Mathf.MoveTowards(currentAngularRate, targetRate, response);
        rb.MoveRotation(rb.rotation + currentAngularRate * deltaTime);
    }

    void UpdateDriftState(float forwardSpeed, float lateralSpeed, float maximumSpeed, float steering,
        bool handbrake, float deltaTime) {
        Vector2 velocity = rb.linearVelocity;
        if (velocity.sqrMagnitude <= 0.0001f || forwardSpeed <= 0.01f) {
            SlipAngle = 0f;
            driftEnterTimer = 0f;
            driftExitTimer = 0f;
            isDrifting = false;
            return;
        }

        SlipAngle = Mathf.Abs(Vector2.SignedAngle(transform.up, velocity));
        float minimumSpeed = maximumSpeed * Mathf.Clamp01(settings.driftMinimumSpeedFraction);
        bool angleAllowsDrift = SlipAngle >= settings.driftEnterAngle && SlipAngle <= settings.driftMaximumAngle;
        bool handbrakeAllowsDrift = !settings.driftRequiresHandbrake || handbrake;
        bool handbrakeTurnIntent = handbrake && Mathf.Abs(steering) > 0.01f;
        bool shouldEnter = forwardSpeed >= minimumSpeed && handbrakeAllowsDrift &&
                           (handbrakeTurnIntent || (angleAllowsDrift && Mathf.Abs(lateralSpeed) > 0.01f));
        bool shouldExit = !handbrakeAllowsDrift || forwardSpeed < minimumSpeed ||
                          SlipAngle <= settings.driftExitAngle || SlipAngle > settings.driftMaximumAngle;

        if (!isDrifting) {
            driftExitTimer = 0f;
            driftEnterTimer = shouldEnter ? driftEnterTimer + deltaTime : 0f;
            if (shouldEnter && driftEnterTimer >= settings.driftEnterDwell) {
                isDrifting = true;
                driftEnterTimer = 0f;
            }
        }
        else {
            driftEnterTimer = 0f;
            driftExitTimer = shouldExit ? driftExitTimer + deltaTime : 0f;
            if (shouldExit && driftExitTimer >= settings.driftExitDwell) {
                isDrifting = false;
                driftExitTimer = 0f;
            }
        }
    }
}
