using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inspector-authored time/heat/composition rules for one police director profile. The runtime
/// director treats this asset as read-only; session state lives in its own runtime objects.
/// </summary>
[CreateAssetMenu(fileName = "NewPoliceDirectorData", menuName = "PizzaGame/Traffic/Police Director Data")]
public sealed class PoliceDirectorData : ScriptableObject {
    /// <summary>Stable profile identifier referenced by map difficulty bindings.</summary>
    public string profileId;

    /// <summary>Heat present when the active session clock starts.</summary>
    [Min(0f)] public float baseHeatAtStart;

    /// <summary>Small persistent heat growth per active session second before incident heat is included.</summary>
    [Min(0f)] public float baseHeatPerActiveSecond = 0.1f;

    /// <summary>Maximum effective heat; freeplay cannot increase beyond this authored cap.</summary>
    [Min(0f)] public float maximumHeat = 100f;

    /// <summary>Earliest active second at which the first police request may be emitted.</summary>
    [Min(0f)] public float earliestPoliceTime = 15f;

    /// <summary>Ordered heat tiers. Thresholds must be ascending and IDs must be unique.</summary>
    public List<PoliceHeatTier> heatTiers = new List<PoliceHeatTier>();

    /// <summary>Composition mode selected for a future police scene owner.</summary>
    public PoliceNavigationMode navigationMode = PoliceNavigationMode.LegacyRoad;

    /// <summary>Authored free-drive settings copied into a detached session snapshot.</summary>
    public PoliceFreeChaseSettings freeChaseSettings = new PoliceFreeChaseSettings();

    /// <summary>Authored per-life variation bounds copied into a detached session snapshot.</summary>
    public PoliceUnitVariationSettings unitVariationSettings = new PoliceUnitVariationSettings();

    /// <summary>Validates the complete profile before a session can enable police director work.</summary>
    /// <param name="reason">Stable failure reason when validation fails.</param>
    /// <returns>True when the profile can safely drive a session.</returns>
    public bool TryValidate(out string reason) {
        reason = null;
        if (string.IsNullOrWhiteSpace(profileId)) return Fail("director profile id is empty", out reason);
        if (!FiniteNonNegative(baseHeatAtStart) || !FiniteNonNegative(baseHeatPerActiveSecond) ||
            !FiniteNonNegative(maximumHeat) || !FiniteNonNegative(earliestPoliceTime))
            return Fail("director heat or timing value is invalid", out reason);
        if (heatTiers == null || heatTiers.Count == 0) return Fail("director has no heat tiers", out reason);
        if (!System.Enum.IsDefined(typeof(PoliceNavigationMode), navigationMode))
            return Fail("director navigation mode is invalid", out reason);
        if (freeChaseSettings == null || !freeChaseSettings.IsValid(out reason)) return false;
        if (unitVariationSettings == null || !unitVariationSettings.IsValid(out reason)) return false;

        var ids = new HashSet<string>();
        float previousHeat = 0f;
        bool first = true;
        foreach (var tier in heatTiers) {
            if (tier == null || !tier.TryValidate(out reason)) return false;
            if (!ids.Add(tier.tierId)) return Fail("director has duplicate tier id", out reason);
            if (!first && tier.minimumHeat < previousHeat) return Fail("director tier thresholds are not ascending", out reason);
            previousHeat = tier.minimumHeat;
            first = false;
        }
        return true;
    }

    /// <summary>Returns the highest authored tier whose threshold is not above the supplied heat.</summary>
    /// <param name="effectiveHeat">Clamped effective heat value.</param>
    /// <returns>The selected tier, or null when no tier threshold has been reached.</returns>
    public PoliceHeatTier ResolveTier(float effectiveHeat) {
        if (heatTiers == null || heatTiers.Count == 0) return null;
        PoliceHeatTier selected = null;
        for (int i = 0; i < heatTiers.Count; i++) {
            var tier = heatTiers[i];
            if (tier == null || tier.minimumHeat > effectiveHeat) break;
            selected = tier;
        }
        return selected;
    }

    /// <summary>
    /// Creates a deep detached copy for one active session so authoring mutations cannot alter the
    /// already-frozen director contract or the profile used by a later session.
    /// </summary>
    /// <returns>A profile with independent tier and composition objects.</returns>
    internal PoliceDirectorData CreateDetachedCopy() {
        PoliceDirectorData copy = CreateInstance<PoliceDirectorData>();
        copy.profileId = profileId;
        copy.baseHeatAtStart = baseHeatAtStart;
        copy.baseHeatPerActiveSecond = baseHeatPerActiveSecond;
        copy.maximumHeat = maximumHeat;
        copy.earliestPoliceTime = earliestPoliceTime;
        copy.navigationMode = navigationMode;
        copy.freeChaseSettings = freeChaseSettings != null ? freeChaseSettings.Clone() : null;
        copy.unitVariationSettings = unitVariationSettings != null ? unitVariationSettings.Clone() : null;
        copy.heatTiers = new List<PoliceHeatTier>();
        if (heatTiers == null) return copy;
        foreach (PoliceHeatTier tier in heatTiers) {
            PoliceHeatTier tierCopy = new PoliceHeatTier {
                tierId = tier.tierId,
                minimumHeat = tier.minimumHeat,
                targetCount = tier.targetCount,
                reinforcementInterval = tier.reinforcementInterval,
                destroyedReplacementDelay = tier.destroyedReplacementDelay,
                relocationCooldown = tier.relocationCooldown,
                compositions = new List<PoliceCompositionEntry>()
            };
            foreach (PoliceCompositionEntry entry in tier.compositions) {
                tierCopy.compositions.Add(new PoliceCompositionEntry {
                    vehicleProfileId = entry.vehicleProfileId,
                    behaviorProfileId = entry.behaviorProfileId,
                    tacticalRole = entry.tacticalRole,
                    weight = entry.weight,
                    maximumCount = entry.maximumCount
                });
            }
            copy.heatTiers.Add(tierCopy);
        }
        return copy;
    }

    static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    static bool Fail(string message, out string reason) { reason = message; return false; }
}
