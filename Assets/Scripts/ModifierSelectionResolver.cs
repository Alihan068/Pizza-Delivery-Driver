using System.Collections.Generic;

/// <summary>
/// Resolves a requested set of modifier ids into a duplicate-free, order-independent selection.
/// Pure and stateless: the same catalog and id set always produce the same canonical result no
/// matter what order the ids were requested in.
/// </summary>
public static class ModifierSelectionResolver {
    /// <summary>Resolves requested modifier ids against a catalog.</summary>
    /// <param name="catalog">Registered modifiers a selection may reference. Null/blank entries are skipped.</param>
    /// <param name="requestedIds">Requested modifier ids, in any order. May contain duplicates or unknown ids.</param>
    /// <returns>Canonical accepted modifiers plus every rejected id, in the order it was rejected.</returns>
    public static ModifierSelectionResult Resolve(IReadOnlyList<ShiftModifierData> catalog, IReadOnlyList<string> requestedIds) {
        var resolved = new List<ShiftModifierData>();
        var rejected = new List<string>();

        if (requestedIds != null) {
            var seen = new HashSet<string>();
            var usedGroups = new HashSet<string>();
            foreach (var id in requestedIds) {
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) {
                    rejected.Add(id);
                    continue;
                }

                ShiftModifierData match = FindById(catalog, id);
                if (match == null) {
                    rejected.Add(id);
                    continue;
                }

                if (!string.IsNullOrEmpty(match.exclusiveGroup) && !usedGroups.Add(match.exclusiveGroup)) {
                    rejected.Add(id);
                    continue;
                }

                resolved.Add(match);
            }
        }

        resolved.Sort((a, b) => string.CompareOrdinal(a.modifierId, b.modifierId));
        return new ModifierSelectionResult(resolved, rejected);
    }

    static ShiftModifierData FindById(IReadOnlyList<ShiftModifierData> catalog, string id) {
        if (catalog == null) return null;
        for (int i = 0; i < catalog.Count; i++) {
            var candidate = catalog[i];
            if (candidate != null && candidate.modifierId == id) return candidate;
        }
        return null;
    }
}
