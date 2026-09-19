using UnityEngine;

/// <summary>Pure blast damage formula (contracts §5): baseBlast × distanceFalloff × targetRoleMultiplier × (1−explosionResistance). Collision armor is a separate, unrelated reduction and is never applied here a second time.</summary>
public static class BlastDamageMath {
    /// <summary>Linear falloff: 1 at the origin, 0 at/beyond blastRadius.</summary>
    public static float ComputeDistanceFalloff(float distance, float blastRadius) {
        if (blastRadius <= 0f) return 0f;
        return Mathf.Clamp01(1f - Mathf.Max(0f, distance) / blastRadius);
    }

    /// <summary>Computes one victim's blast damage.</summary>
    public static float CalculateBlastDamage(float baseBlast, float distance, float blastRadius, float targetRoleMultiplier, float explosionResistance) {
        float falloff = ComputeDistanceFalloff(distance, blastRadius);
        float resistance = Mathf.Clamp01(explosionResistance);
        return Mathf.Max(0f, baseBlast) * falloff * Mathf.Max(0f, targetRoleMultiplier) * (1f - resistance);
    }
}
