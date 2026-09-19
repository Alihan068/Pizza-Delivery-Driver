using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Combines an already-resolved, duplicate-free, canonically-ordered modifier selection (see
/// <see cref="ModifierSelectionResolver"/>) into the actual gameplay effects a shift applies:
/// summed stat deltas, OR-combined traffic/police disable flags, a multiplied score coefficient,
/// and a deterministic light blend. For exactly one selected modifier every result here is
/// identical to the pre-multi-select single-modifier behavior it replaces.
/// </summary>
public static class ModifierEffectResolver {
    /// <summary>Sums one stat's authored delta across every resolved modifier. Domain clamping is the caller's responsibility.</summary>
    /// <param name="resolvedModifiers">Canonically-ordered, duplicate-free modifiers.</param>
    /// <param name="stat">Stat to sum.</param>
    public static float GetCombinedStatDelta(IReadOnlyList<ShiftModifierData> resolvedModifiers, VehicleStatId stat) {
        float total = 0f;
        if (resolvedModifiers == null) return total;
        for (int i = 0; i < resolvedModifiers.Count; i++) {
            var modifier = resolvedModifiers[i];
            if (modifier != null) total += modifier.GetStatDelta(stat);
        }
        return total;
    }

    /// <summary>True when any resolved modifier disables civilian traffic.</summary>
    public static bool GetCombinedDisablesCivilianTraffic(IReadOnlyList<ShiftModifierData> resolvedModifiers) {
        if (resolvedModifiers == null) return false;
        for (int i = 0; i < resolvedModifiers.Count; i++)
            if (resolvedModifiers[i] != null && resolvedModifiers[i].disablesCivilianTraffic) return true;
        return false;
    }

    /// <summary>True when any resolved modifier disables police pursuit.</summary>
    public static bool GetCombinedDisablesPolice(IReadOnlyList<ShiftModifierData> resolvedModifiers) {
        if (resolvedModifiers == null) return false;
        for (int i = 0; i < resolvedModifiers.Count; i++)
            if (resolvedModifiers[i] != null && resolvedModifiers[i].disablesPolice) return true;
        return false;
    }

    /// <summary>
    /// Product of every resolved modifier's authored score multiplier. Empty selection is neutral (1).
    /// A modifier with an invalid multiplier (NaN/Infinity/out of [0,1]) contributes neutrally instead
    /// of corrupting the product; whether that makes the whole session ineligible is a higher-level
    /// (S01.8/S10) decision, not this pure math helper's.
    /// </summary>
    public static float GetCombinedScoreMultiplier(IReadOnlyList<ShiftModifierData> resolvedModifiers) {
        float product = 1f;
        if (resolvedModifiers == null) return product;
        for (int i = 0; i < resolvedModifiers.Count; i++) {
            var modifier = resolvedModifiers[i];
            if (modifier != null && modifier.HasValidScoreMultiplier()) product *= modifier.scoreMultiplier;
        }
        return product;
    }

    /// <summary>
    /// Deterministically blends every resolved modifier's light effect onto a base color/intensity,
    /// in the caller-supplied (already canonical) order. An empty/null selection returns the base
    /// unchanged; a single modifier reproduces the original single-modifier Lerp/multiply exactly.
    /// </summary>
    /// <param name="resolvedModifiers">Canonically-ordered, duplicate-free modifiers.</param>
    /// <param name="baseColor">Scene's authored base light color.</param>
    /// <param name="baseIntensity">Scene's authored base light intensity.</param>
    /// <param name="resultColor">Blended color.</param>
    /// <param name="resultIntensity">Blended intensity.</param>
    public static void GetCombinedLight(IReadOnlyList<ShiftModifierData> resolvedModifiers, Color baseColor, float baseIntensity,
        out Color resultColor, out float resultIntensity) {
        resultColor = baseColor;
        resultIntensity = baseIntensity;
        if (resolvedModifiers == null) return;
        for (int i = 0; i < resolvedModifiers.Count; i++) {
            var modifier = resolvedModifiers[i];
            if (modifier == null) continue;
            resultColor = Color.Lerp(resultColor, modifier.lightColor, Mathf.Clamp01(modifier.lightBlend));
            resultIntensity *= Mathf.Max(0.01f, modifier.lightIntensityMultiplier);
        }
    }
}
