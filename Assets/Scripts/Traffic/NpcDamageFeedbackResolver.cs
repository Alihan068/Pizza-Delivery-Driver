using UnityEngine;

/// <summary>
/// Resolves a purely visual/audio feedback tier from remaining health fraction and an authored
/// profile's thresholds. Never itself plays an effect or touches gameplay state — damage/death
/// already happened through NpcVehicleHealth regardless of whether any smoke/critical effect exists
/// or plays; this resolver only tells a presentation layer what to show, if anything.
/// </summary>
public static class NpcDamageFeedbackResolver {
    public static NpcDamageFeedbackLevel Resolve(float currentHealth, float maxHealth, NpcVehicleProfile profile) {
        if (profile == null || maxHealth <= 0f) return NpcDamageFeedbackLevel.None;

        float fraction = Mathf.Clamp01(currentHealth / maxHealth);
        if (fraction <= profile.criticalHealthFraction01) return NpcDamageFeedbackLevel.Critical;
        if (fraction <= profile.smokeHealthFraction01) return NpcDamageFeedbackLevel.Smoking;
        return NpcDamageFeedbackLevel.None;
    }
}
