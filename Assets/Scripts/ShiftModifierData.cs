using UnityEngine;

/// <summary>
/// Authored rules for one optional shift modifier. A modifier changes the presentation and the
/// effective vehicle stats for the next shift without changing permanent career data.
/// </summary>
[CreateAssetMenu(fileName = "NewShiftModifier", menuName = "PizzaGame/Shift Modifier")]
public class ShiftModifierData : ScriptableObject {
    [Header("Identity")]
    [Tooltip("Permanent identifier used by content systems. Never change it after release.")]
    public string modifierId;

    [Tooltip("Localization key for the modifier name.")]
    public string displayNameKey;

    [Tooltip("Localization key for the modifier description.")]
    public string descriptionKey;

    [Header("Visual Layer")]
    [Tooltip("Target color blended into the scene's global light while this modifier is active.")]
    public Color lightColor = Color.white;

    [Range(0f, 1f)]
    [Tooltip("How strongly the authored light color is blended with the scene's base color.")]
    public float lightBlend = 0.25f;

    [Min(0.01f)]
    [Tooltip("Multiplier applied to the scene's base global light intensity.")]
    public float lightIntensityMultiplier = 1f;

    [Header("Shift Stat Deltas")]
    [Tooltip("Temporary speed change for this shift.")]
    public float speedDelta;

    [Tooltip("Temporary handling change for this shift.")]
    public float turnDelta;

    [Tooltip("Temporary maximum health change for this shift.")]
    public float healthDelta;

    [Range(-1f, 1f)]
    [Tooltip("Temporary armor change for this shift.")]
    public float armorDelta;

    [Tooltip("Temporary pizza capacity change for this shift.")]
    public int capacityDelta;

    [Range(-1f, 1f)]
    [Tooltip("Temporary pizza protection chance change for this shift.")]
    public float protectionDelta;

    [Header("Score And Capability Flags")]
    [Range(0f, 1f)]
    [Tooltip("Nerf applied to this shift's final score when this modifier is selected. 1 is neutral, 0 zeroes the score. Combined with other selected modifiers by multiplication.")]
    public float scoreMultiplier = 1f;

    [Tooltip("When true, civilian traffic is disabled for the shift while this modifier is selected.")]
    public bool disablesCivilianTraffic;

    [Tooltip("When true, police pursuit is disabled for the shift while this modifier is selected.")]
    public bool disablesPolice;

    [Tooltip("Optional exclusivity group id. At most one modifier sharing the same non-empty group id may be selected at once. Leave empty for no exclusivity.")]
    public string exclusiveGroup = string.Empty;

    /// <summary>Whether the authored score multiplier is a finite value in the accepted [0, 1] range.</summary>
    /// <returns>False for NaN, infinity, or a value outside [0, 1]; such a modifier must be rejected, not clamped silently.</returns>
    public bool HasValidScoreMultiplier() {
        return !float.IsNaN(scoreMultiplier) && !float.IsInfinity(scoreMultiplier) &&
            scoreMultiplier >= 0f && scoreMultiplier <= 1f;
    }

    /// <summary>Gets the authored temporary delta for one vehicle stat.</summary>
    /// <param name="stat">Stat to resolve.</param>
    /// <returns>The delta, or zero when the stat is unknown.</returns>
    public float GetStatDelta(VehicleStatId stat) {
        switch (stat) {
            case VehicleStatId.Speed: return speedDelta;
            case VehicleStatId.Turn: return turnDelta;
            case VehicleStatId.Health: return healthDelta;
            case VehicleStatId.Armor: return armorDelta;
            case VehicleStatId.Capacity: return capacityDelta;
            case VehicleStatId.Protection: return protectionDelta;
            default: return 0f;
        }
    }

    /// <summary>Resolves the localized name shown in future modifier selection UI.</summary>
    /// <returns>The localized name, or the asset name when no key is authored.</returns>
    public string GetDisplayName() {
        return string.IsNullOrEmpty(displayNameKey) ? name : LocalizationManager.Get(displayNameKey);
    }

    /// <summary>Resolves the localized description shown in future modifier selection UI.</summary>
    /// <returns>The localized description, or an empty string when no key is authored.</returns>
    public string GetDescription() {
        return string.IsNullOrEmpty(descriptionKey) ? string.Empty : LocalizationManager.Get(descriptionKey);
    }
}
