using System.Collections.Generic;

/// <summary>
/// Provides pure operations shared by the runtime modifier picker and its Edit Mode tests.
/// The helper keeps preview state separate from the authoritative GameManager selection and
/// rejects capability-dependent modifiers before they can be presented as active effects.
/// </summary>
public static class ModifierSelectionUiHelper {
    /// <summary>Returns whether a modifier can be previewed on the selected map.</summary>
    /// <param name="modifier">Modifier to inspect.</param>
    /// <param name="map">Map being previewed.</param>
    /// <returns>False when the modifier requires traffic/police support that the map does not provide.</returns>
    public static bool IsSupported(ShiftModifierData modifier, MapData map) {
        if (modifier == null) return false;
        if (!modifier.disablesCivilianTraffic && !modifier.disablesPolice) return true;
        return map != null && map.trafficMapData != null;
    }

    /// <summary>Builds a duplicate-free preview request after toggling one modifier id.</summary>
    /// <param name="currentIds">Current preview ids; null is treated as an empty selection.</param>
    /// <param name="modifierId">Stable id being toggled.</param>
    /// <param name="selected">True to add the id, false to remove it.</param>
    /// <returns>A new request list; the input collection is never mutated.</returns>
    public static List<string> Toggle(IReadOnlyList<string> currentIds, string modifierId, bool selected) {
        var result = new List<string>();
        if (currentIds != null) {
            for (int i = 0; i < currentIds.Count; i++) {
                string id = currentIds[i];
                if (!string.IsNullOrEmpty(id) && id != modifierId && !result.Contains(id)) result.Add(id);
            }
        }
        if (selected && !string.IsNullOrEmpty(modifierId)) result.Add(modifierId);
        return result;
    }

    /// <summary>Computes the product shown in the preview without applying session state.</summary>
    /// <param name="modifiers">Resolved preview modifiers.</param>
    /// <returns>The authored score multiplier product, with an empty selection equal to one.</returns>
    public static float GetPreviewScoreMultiplier(IReadOnlyList<ShiftModifierData> modifiers) {
        return ModifierEffectResolver.GetCombinedScoreMultiplier(modifiers);
    }
}
