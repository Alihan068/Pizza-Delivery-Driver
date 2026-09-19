using UnityEngine;

/// <summary>Catalog entry resolving authored collision and explosion IDs for a vehicle profile.</summary>
[System.Serializable]
public sealed class TrafficDamageProfile {
    /// <summary>Stable ID matched by damageProfileId or explosionProfileId.</summary>
    public string profileId;
    /// <summary>Collision dead zone, cooldown and damage coefficients.</summary>
    public NpcCollisionDamageProfile collision = new NpcCollisionDamageProfile();
    /// <summary>Damage at the center before target resistance and role multiplier.</summary>
    [Min(0f)] public float blastDamage = 80f;
    /// <summary>World-space blast radius; zero disables the gameplay blast.</summary>
    [Min(0f)] public float blastRadius = 5f;

    /// <summary>Rejects malformed values before a runtime actor can consume this entry.</summary>
    public bool IsValid() {
        return !string.IsNullOrEmpty(profileId) && collision != null &&
            IsFiniteNonNegative(blastDamage) && IsFiniteNonNegative(blastRadius) &&
            IsFiniteNonNegative(collision.minimumImpactSpeed) && IsFiniteNonNegative(collision.damageCooldownSeconds) &&
            IsFiniteNonNegative(collision.damageBase) && IsFiniteNonNegative(collision.damageFactor) &&
            IsFiniteNonNegative(collision.damageExponent);
    }

    /// <summary>Copies tuning so editing an asset cannot alter an already-spawned vehicle's damage.</summary>
    public TrafficDamageProfile Copy() {
        return new TrafficDamageProfile {
            profileId = profileId, blastDamage = blastDamage, blastRadius = blastRadius,
            collision = new NpcCollisionDamageProfile {
                minimumImpactSpeed = collision.minimumImpactSpeed, damageCooldownSeconds = collision.damageCooldownSeconds,
                damageBase = collision.damageBase, damageFactor = collision.damageFactor, damageExponent = collision.damageExponent
            }
        };
    }

    static bool IsFiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
}
