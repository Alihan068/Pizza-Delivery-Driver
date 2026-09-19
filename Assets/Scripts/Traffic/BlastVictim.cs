using UnityEngine;

/// <summary>One damage-receiver candidate reported by an <see cref="IBlastVictimQuery"/>. Multiple entries can share a lifeId (one vehicle with several colliders); <see cref="VehicleExplosionService"/> dedupes by lifeId per blast.</summary>
public readonly struct BlastVictim {
    public readonly int lifeId;
    public readonly VehicleRole role;
    public readonly Vector2 position;
    public readonly float explosionResistance;

    public BlastVictim(int lifeId, VehicleRole role, Vector2 position, float explosionResistance) {
        this.lifeId = lifeId;
        this.role = role;
        this.position = position;
        this.explosionResistance = explosionResistance;
    }
}
