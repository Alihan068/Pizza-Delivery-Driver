using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Caches the player's driving commands for the physics motor.
/// </summary>
/// <remarks>
/// PlayerInput uses SendMessages in the existing vehicle prefabs. This component is the only
/// receiver for gameplay movement callbacks; <see cref="Driver"/> no longer writes movement from
/// an input callback. Separate throttle and brake actions are supported, while the existing Move
/// action remains a compatibility fallback until the input asset migration is complete. The
/// keyboard Space and gamepad south-button state is also sampled as a fallback so a lost callback
/// cannot leave the handbrake unavailable while the configured action remains unchanged.
/// </remarks>
public class VehicleInput : MonoBehaviour {

    [SerializeField] float deadzone = 0.08f;

    Vector2 moveInput;
    float throttleInput;
    float brakeInput;
    bool handbrakeInput;
    bool hasThrottleAction;
    bool hasBrakeAction;
    bool hasHandbrakeAction;

    void Update() {
        if (S12BenchmarkGate.Requested) { ClearInput(); return; }
        bool keyboardHeld = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
        bool gamepadHeld = Gamepad.current != null && Gamepad.current.buttonSouth.isPressed;
        if (keyboardHeld || gamepadHeld) {
            hasHandbrakeAction = true;
            handbrakeInput = true;
        }
        else if (hasHandbrakeAction) {
            handbrakeInput = false;
        }
    }

    /// <summary>Current normalized steering, throttle, brake and handbrake command.</summary>
    public VehicleInputSnapshot Snapshot {
        get {
            if (S12BenchmarkGate.Requested) return default;
            float steering = ApplyDeadzone(moveInput.x);
            float fallbackThrottle = Mathf.Max(0f, moveInput.y);
            float fallbackBrake = Mathf.Max(0f, -moveInput.y);
            float effectiveThrottle = hasThrottleAction ? throttleInput : fallbackThrottle;
            float effectiveBrake = hasBrakeAction ? brakeInput : fallbackBrake;

            // Braking wins when both analogue triggers or both digital commands are held.
            // This keeps the command deterministic and prevents a stopped vehicle from
            // accelerating because the throttle branch was evaluated first.
            if (effectiveThrottle > 0f && effectiveBrake > 0f) {
                if (effectiveBrake >= effectiveThrottle) effectiveThrottle = 0f;
                else effectiveBrake = 0f;
            }

            return new VehicleInputSnapshot(
                steering,
                effectiveThrottle,
                effectiveBrake,
                hasHandbrakeAction ? handbrakeInput : false);
        }
    }

    /// <summary>Receives the existing Move action and keeps its steering compatibility.</summary>
    /// <param name="value">The action value from PlayerInput.</param>
    public void OnMove(InputValue value) {
        moveInput = value.Get<Vector2>();
    }

    /// <summary>Receives an independent throttle action when the migrated input asset provides it.</summary>
    /// <param name="value">The action value from PlayerInput.</param>
    public void OnThrottle(InputValue value) {
        hasThrottleAction = true;
        throttleInput = Mathf.Clamp01(value.Get<float>());
    }

    /// <summary>Receives an independent brake/reverse action when the migrated input asset provides it.</summary>
    /// <param name="value">The action value from PlayerInput.</param>
    public void OnBrake(InputValue value) {
        hasBrakeAction = true;
        brakeInput = Mathf.Clamp01(value.Get<float>());
    }

    /// <summary>Receives the handbrake action without coupling it to a UI submit action.</summary>
    /// <param name="value">The action value from PlayerInput.</param>
    public void OnHandbrake(InputValue value) {
        hasHandbrakeAction = true;
        handbrakeInput = value.isPressed;
    }

    /// <summary>Clears all cached gameplay input after a pause, focus loss or device loss.</summary>
    public void ClearInput() {
        moveInput = Vector2.zero;
        throttleInput = 0f;
        brakeInput = 0f;
        handbrakeInput = false;
    }

    void OnApplicationFocus(bool hasFocus) {
        if (!hasFocus) ClearInput();
    }

    void OnApplicationPause(bool pauseStatus) {
        if (pauseStatus) ClearInput();
    }

    float ApplyDeadzone(float value) {
        float magnitude = Mathf.Abs(value);
        if (magnitude <= deadzone) return 0f;
        return Mathf.Sign(value) * Mathf.InverseLerp(deadzone, 1f, magnitude);
    }
}

/// <summary>Immutable normalized input values consumed by <see cref="VehicleMovement"/>.</summary>
public readonly struct VehicleInputSnapshot {
    /// <summary>Steering from full left to full right.</summary>
    public readonly float steering;
    /// <summary>Forward throttle from zero to one.</summary>
    public readonly float throttle;
    /// <summary>Brake/reverse input from zero to one.</summary>
    public readonly float brake;
    /// <summary>Whether the handbrake is held.</summary>
    public readonly bool handbrake;

    /// <summary>Creates a normalized driving command.</summary>
    public VehicleInputSnapshot(float steering, float throttle, float brake, bool handbrake) {
        this.steering = Mathf.Clamp(steering, -1f, 1f);
        this.throttle = Mathf.Clamp01(throttle);
        this.brake = Mathf.Clamp01(brake);
        this.handbrake = handbrake;
    }
}
