using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Audits map-local difficulty data before it reaches a playable build.</summary>
/// <remarks>
/// The validator deliberately accepts any number of tiers and any non-negative pizza-loss
/// percentage. It catches malformed authoring while preserving the data-driven contract required
/// by the future map editor and Workshop provider.
/// </remarks>
public static class MapDifficultyValidator {

    /// <summary>Validates every MapData asset currently present in the project.</summary>
    /// <returns>Human-readable findings; an empty list means no issue was found.</returns>
    public static List<string> ValidateProject() {
        var findings = new List<string>();
        string[] guids = AssetDatabase.FindAssets("t:MapData");
        foreach (string guid in guids) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MapData map = AssetDatabase.LoadAssetAtPath<MapData>(path);
            if (map == null) continue;
            ValidateMap(map, path, findings);
        }
        return findings;
    }

    static void ValidateMap(MapData map, string path, List<string> findings) {
        string label = string.IsNullOrEmpty(map.mapId) ? path : map.mapId;
        if (map.levelData == null) {
            findings.Add("ERROR " + label + ": no LevelData is assigned.");
            return;
        }
        if (map.previewImage == null)
            findings.Add("WARNING " + label + ": no preview image is assigned.");

        List<MapDifficultyData> tiers = map.levelData.difficultyLevels;
        if (tiers == null || tiers.Count == 0) {
            findings.Add("WARNING " + label + ": no difficulty tiers are authored; legacy order fallback will be used.");
            return;
        }

        var ids = new HashSet<string>();
        for (int i = 0; i < tiers.Count; i++) {
            MapDifficultyData tier = tiers[i];
            if (tier == null) {
                findings.Add("ERROR " + label + ": difficulty row " + i + " is null.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(tier.difficultyId))
                findings.Add("ERROR " + label + ": difficulty row " + i + " has no permanent id.");
            else if (!ids.Add(tier.difficultyId))
                findings.Add("ERROR " + label + ": duplicate difficulty id '" + tier.difficultyId + "'.");
            if (tier.orderMin < 1 || tier.orderMax < tier.orderMin)
                findings.Add("ERROR " + label + ": difficulty row " + i + " has an invalid inclusive order range.");
            if (tier.pizzaLossChancePercent < 0f)
                findings.Add("ERROR " + label + ": difficulty row " + i + " has a negative pizza-loss budget.");
            if (tier.rewardMultiplier < 0f)
                findings.Add("ERROR " + label + ": difficulty row " + i + " has a negative reward multiplier.");
            bool hasNext = i < tiers.Count - 1;
            if (hasNext && tier.unlockTargetScoreForNext <= 0)
                findings.Add("ERROR " + label + ": difficulty row " + i + " has no positive next-tier score target.");
            if (!hasNext && tier.unlockTargetScoreForNext != 0)
                findings.Add("WARNING " + label + ": final difficulty row has an unused next-tier score target.");
        }
    }
}
