using UnityEngine;

/// <summary>
/// One side of a physics contact, sampled BEFORE the step resolves it (contracts §6: pre-impact
/// velocity/angular-velocity/center-of-mass snapshot). <see cref="isStaticWorld"/> forces this
/// side's contribution to zero regardless of any sampled velocity — static geometry is never blamed.
/// </summary>
public readonly struct ContactParticipant {
    public readonly int lifeId;
    public readonly InstigatorKind kind;
    public readonly Vector2 linearVelocity;
    public readonly float angularVelocityRadPerSec;
    public readonly Vector2 centerOfMass;
    public readonly bool isStaticWorld;

    public ContactParticipant(int lifeId, InstigatorKind kind, Vector2 linearVelocity, float angularVelocityRadPerSec,
        Vector2 centerOfMass, bool isStaticWorld = false) {
        this.lifeId = lifeId;
        this.kind = kind;
        this.linearVelocity = linearVelocity;
        this.angularVelocityRadPerSec = angularVelocityRadPerSec;
        this.centerOfMass = centerOfMass;
        this.isStaticWorld = isStaticWorld;
    }
}
