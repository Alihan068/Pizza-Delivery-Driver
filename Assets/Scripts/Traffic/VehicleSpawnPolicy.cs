using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure safe-spawn candidate filter shared by initial placement and active-game replacement. Checks
/// road-graph access, heading validity, camera bounds+margin visibility, authored no-spawn service
/// regions, the player's near-future path (active replacement only), and collider-footprint
/// clearance. Never itself retries or loops — when nothing passes, it reports why and returns once,
/// leaving retry timing to the caller so an empty result can never hang a caller's loop.
/// </summary>
public static class VehicleSpawnPolicy {
    /// <summary>Scans candidates in order and returns the first that passes every check.</summary>
    /// <param name="candidates">Candidates to evaluate, in priority order.</param>
    /// <param name="context">Query surface every candidate is checked against.</param>
    /// <param name="chosen">The first safe candidate, or null when none pass.</param>
    /// <param name="rejectionReasons">One reason per rejected candidate, in the same order. Never null.</param>
    /// <returns>True when a safe candidate was found.</returns>
    public static bool TryPickSafeCandidate(IReadOnlyList<SpawnCandidate> candidates, SpawnQueryContext context,
        out SpawnCandidate chosen, out List<string> rejectionReasons) {
        chosen = null;
        rejectionReasons = new List<string>();

        if (candidates == null || candidates.Count == 0 || context == null) {
            rejectionReasons.Add("no candidates supplied");
            return false;
        }

        foreach (var candidate in candidates) {
            if (IsSafe(candidate, context, out string reason)) {
                chosen = candidate;
                return true;
            }
            rejectionReasons.Add(reason);
        }
        return false;
    }

    /// <summary>Evaluates a single candidate against every check. Exposed separately so callers/tests can inspect one candidate's fate directly.</summary>
    public static bool IsSafe(SpawnCandidate candidate, SpawnQueryContext context, out string reason) {
        reason = null;
        if (candidate == null || context == null) {
            reason = "null candidate or context";
            return false;
        }

        if (string.IsNullOrEmpty(candidate.graphNodeId) || (context.graph != null && context.graph.GetNode(candidate.graphNodeId) == null)) {
            reason = "candidate has no road-graph access";
            return false;
        }

        if (float.IsNaN(candidate.headingDegrees) || float.IsInfinity(candidate.headingDegrees)) {
            reason = "candidate heading is invalid";
            return false;
        }

        Rect expandedCameraBounds = Rect.MinMaxRect(
            context.cameraBounds.xMin - context.cameraMargin,
            context.cameraBounds.yMin - context.cameraMargin,
            context.cameraBounds.xMax + context.cameraMargin,
            context.cameraBounds.yMax + context.cameraMargin);
        if (expandedCameraBounds.Contains(candidate.position)) {
            reason = "candidate is inside camera bounds plus margin";
            return false;
        }

        if (context.noSpawnRegions != null) {
            foreach (var region in context.noSpawnRegions) {
                if (region != null && region.area.Contains(candidate.position)) {
                    reason = "candidate is inside no-spawn region: " + region.reason;
                    return false;
                }
            }
        }

        if (context.mode == SpawnPlacementMode.ActiveReplacement) {
            Vector2 toCandidate = candidate.position - context.playerPosition;
            float distance = toCandidate.magnitude;
            if (distance <= 0.0001f) {
                reason = "candidate coincides with the player's position";
                return false;
            }
            float closingSpeed = Vector2.Dot(context.playerVelocity, toCandidate / distance);
            if (closingSpeed > 0.0001f) {
                float approachTime = distance / closingSpeed;
                if (approachTime < context.minApproachTimeSeconds) {
                    reason = "candidate is on the player's near-future path";
                    return false;
                }
            }
        }

        if (context.clearanceQuery != null && !context.clearanceQuery.IsAreaClear(candidate.position, candidate.footprint, candidate.headingDegrees)) {
            reason = "candidate footprint is not clear";
            return false;
        }

        return true;
    }
}
