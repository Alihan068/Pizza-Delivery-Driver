using UnityEngine;

/// <summary>
/// One frame's driving intent for <see cref="NpcVehicleMotor"/>: throttle/brake/steering plus a
/// governing target speed and an explicit reverse permission. Brake never converts into reverse on
/// its own — reverse only ever happens through a negative throttle with <see cref="reverseAllowed"/>
/// set, decided by whichever AI (route follower, pursuit controller) issues the command.
/// </summary>
public readonly struct NpcDriveCommand {
    /// <summary>-1..1: negative is reverse intent, only honored when reverseAllowed is true.</summary>
    public readonly float throttle;
    /// <summary>0..1 braking intent. Non-zero brake suppresses throttle entirely for this command.</summary>
    public readonly float brake;
    /// <summary>-1..1 steering intent.</summary>
    public readonly float steering;
    /// <summary>Speed governor in this command's direction of travel; the motor never accelerates past it even with full throttle.</summary>
    public readonly float targetSpeed;
    public readonly bool reverseAllowed;

    public NpcDriveCommand(float throttle, float brake, float steering, float targetSpeed, bool reverseAllowed) {
        this.throttle = Mathf.Clamp(throttle, -1f, 1f);
        this.brake = Mathf.Clamp01(brake);
        this.steering = Mathf.Clamp(steering, -1f, 1f);
        this.targetSpeed = Mathf.Max(0f, targetSpeed);
        this.reverseAllowed = reverseAllowed;
    }

    /// <summary>Full brake, no throttle/steering — the safe default between AI decisions.</summary>
    public static NpcDriveCommand Stopped => new NpcDriveCommand(0f, 1f, 0f, 0f, false);
}
