using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

/// <summary>Detached resolved modifier values, captured once before the player prefab is instantiated.</summary>
public sealed class FrozenModifierRules {
    readonly IReadOnlyDictionary<VehicleStatId, float> statDeltas;
    readonly Color lightOffset;
    readonly float baseLightWeight;
    readonly float intensityMultiplier;

    /// <summary>Canonical modifier identities; the backing collection cannot be modified by callers.</summary>
    public IReadOnlyList<string> Ids { get; }
    /// <summary>Final score coefficient shared by HUD and settlement.</summary>
    public float ScoreMultiplier { get; }
    /// <summary>Whether the resolved selection disables civilian traffic.</summary>
    public bool DisablesCivilianTraffic { get; }
    /// <summary>Whether the resolved selection disables police.</summary>
    public bool DisablesPolice { get; }

    /// <summary>Copies values, never asset references, from the canonical resolved selection.</summary>
    public FrozenModifierRules(IReadOnlyList<ShiftModifierData> modifiers) {
        var ids = new List<string>();
        var deltas = new Dictionary<VehicleStatId, float>();
        foreach (VehicleStatId stat in Enum.GetValues(typeof(VehicleStatId)))
            deltas[stat] = ModifierEffectResolver.GetCombinedStatDelta(modifiers, stat);
        statDeltas = new ReadOnlyDictionary<VehicleStatId, float>(deltas);
        ScoreMultiplier = ModifierEffectResolver.GetCombinedScoreMultiplier(modifiers);
        DisablesCivilianTraffic = ModifierEffectResolver.GetCombinedDisablesCivilianTraffic(modifiers);
        DisablesPolice = ModifierEffectResolver.GetCombinedDisablesPolice(modifiers);
        float weight = 1f;
        Color offset = Color.clear;
        float intensity = 1f;
        if (modifiers != null) foreach (var modifier in modifiers) {
            if (modifier == null) continue;
            ids.Add(modifier.modifierId);
            float blend = Mathf.Clamp01(modifier.lightBlend);
            weight *= 1f - blend;
            offset = Color.Lerp(offset, modifier.lightColor, blend);
            intensity *= Mathf.Max(0.01f, modifier.lightIntensityMultiplier);
        }
        Ids = ids.AsReadOnly();
        baseLightWeight = weight;
        lightOffset = offset;
        intensityMultiplier = intensity;
    }

    /// <summary>Returns the frozen additive stat adjustment; unknown stats remain neutral.</summary>
    public float GetStatDelta(VehicleStatId stat) => statDeltas.TryGetValue(stat, out float value) ? value : 0f;

    /// <summary>Applies the captured ordered light blend to a scene's authored base lighting.</summary>
    public void ApplyLight(Color baseColor, float baseIntensity, out Color color, out float intensity) {
        color = baseColor * baseLightWeight + lightOffset;
        intensity = baseIntensity * intensityMultiplier;
    }
}
