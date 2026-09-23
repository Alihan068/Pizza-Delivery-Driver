using System;
using UnityEngine;

/// <summary>
/// Pure goal selection for the Shadow role. A Shadow unit never aims at the player: it drives slightly
/// ahead of where the player is going and, when the road ahead has a side street the player could turn
/// into, parks across that side street's mouth. Geometry is supplied as a clear-segment probe so the
/// selection can be tested without a physics scene.
/// </summary>
public static class PoliceShadowTactics {
    /// <summary>Returns true when a car could drive straight from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public delegate bool ClearSegment(Vector2 from, Vector2 to);

    /// <summary>Authored distances and times for Shadow goal selection.</summary>
    [Serializable]
    public sealed class Settings {
        /// <summary>Seconds of player velocity the shadow point leads by.</summary>
        public float leadSeconds = 0.9f;
        /// <summary>Shortest lead distance, used when the player is slow.</summary>
        public float minimumLead = 3f;
        /// <summary>Longest lead distance, used when the player is fast.</summary>
        public float maximumLead = 9f;
        /// <summary>Lateral distance from the player's line at which the unit shadows.</summary>
        public float sideOffset = 1.5f;
        /// <summary>How far past the lead point side streets are still searched.</summary>
        public float searchBeyondLead = 4f;
        /// <summary>Nearest distance ahead of the player at which a side street is worth cutting off.</summary>
        public float minimumOpeningAhead = 2.5f;
        /// <summary>Spacing of the samples along the player's line.</summary>
        public float sampleSpacing = 1f;
        /// <summary>A side direction must be clear this far to count as a street.</summary>
        public float openingProbeDistance = 4f;
        /// <summary>A wall must stand this far before the opening along the road, so an open plaza is not a street.</summary>
        public float wallBeforeOpening = 2f;
        /// <summary>How far into the side street the unit parks to block it.</summary>
        public float mouthDepth = 2f;
        /// <summary>Player speed below which the unit just holds a standoff instead of leading.</summary>
        public float slowPlayerSpeed = 1f;
        /// <summary>Standoff distance kept from a slow or stopped player.</summary>
        public float slowStandoff = 3f;

        /// <summary>Returns a detached copy.</summary>
        public Settings Clone() => (Settings)MemberwiseClone();
    }

    /// <summary>Selected Shadow goal.</summary>
    public readonly struct Goal {
        /// <summary>World point the unit drives to.</summary>
        public readonly Vector2 point;
        /// <summary>True when the point is a side-street mouth the unit should stop in.</summary>
        public readonly bool blocking;

        /// <summary>Creates a goal.</summary>
        public Goal(Vector2 point, bool blocking) {
            this.point = point;
            this.blocking = blocking;
        }
    }

    /// <summary>Selects where a Shadow unit should be.</summary>
    /// <param name="playerPosition">Player world position.</param>
    /// <param name="playerVelocity">Player world velocity.</param>
    /// <param name="policePosition">This unit's world position; decides which side it prefers.</param>
    /// <param name="settings">Authored selection settings.</param>
    /// <param name="isClear">Clear-segment probe over static geometry.</param>
    /// <param name="openingAvailable">Returns false for a mouth another unit already holds; null accepts all.</param>
    /// <returns>The goal; always finite for finite input.</returns>
    public static Goal Select(Vector2 playerPosition, Vector2 playerVelocity, Vector2 policePosition, Settings settings,
        ClearSegment isClear, Func<Vector2, bool> openingAvailable = null) {
        if (settings == null || isClear == null || !Finite(playerPosition) || !Finite(playerVelocity) || !Finite(policePosition))
            return new Goal(playerPosition, false);
        float speed = playerVelocity.magnitude;
        if (speed < Mathf.Max(0.01f, settings.slowPlayerSpeed)) return Standoff(playerPosition, policePosition, settings, isClear);

        Vector2 direction = playerVelocity / speed;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        float policeSide = Vector2.Dot(policePosition - playerPosition, perpendicular) >= 0f ? 1f : -1f;
        float lead = Mathf.Clamp(speed * settings.leadSeconds, settings.minimumLead, Mathf.Max(settings.minimumLead, settings.maximumLead));
        float spacing = Mathf.Max(0.25f, settings.sampleSpacing);
        float searchEnd = lead + Mathf.Max(0f, settings.searchBeyondLead);

        // How far the road ahead of the player stays open along its current heading.
        float reach = 0f;
        for (float distance = spacing; distance <= searchEnd + 0.001f; distance += spacing) {
            if (!isClear(playerPosition + direction * (distance - spacing), playerPosition + direction * distance)) break;
            reach = distance;
        }

        // Side streets the player could turn into, nearest first, on this unit's side first.
        for (float distance = Mathf.Max(spacing, settings.minimumOpeningAhead); distance <= reach + 0.001f; distance += spacing) {
            Vector2 onRoad = playerPosition + direction * distance;
            for (int pass = 0; pass < 2; pass++) {
                float side = pass == 0 ? policeSide : -policeSide;
                Vector2 lateral = perpendicular * side;
                if (!isClear(onRoad, onRoad + lateral * settings.openingProbeDistance)) continue;
                Vector2 before = onRoad - direction * Mathf.Max(0.5f, settings.wallBeforeOpening);
                if (isClear(before, before + lateral * settings.openingProbeDistance)) continue; // open ground, not a street
                Vector2 mouth = onRoad + lateral * Mathf.Max(0.5f, settings.mouthDepth);
                if (openingAvailable != null && !openingAvailable(mouth)) continue;
                return new Goal(mouth, true);
            }
        }

        // No side street: shadow slightly ahead of the player on this unit's side.
        Vector2 ahead = playerPosition + direction * Mathf.Min(lead, Mathf.Max(reach, spacing));
        Vector2 beside = ahead + perpendicular * (policeSide * settings.sideOffset);
        return new Goal(isClear(ahead, beside) ? beside : ahead, false);
    }

    static Goal Standoff(Vector2 playerPosition, Vector2 policePosition, Settings settings, ClearSegment isClear) {
        Vector2 away = policePosition - playerPosition;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.up;
        Vector2 point = playerPosition + away * Mathf.Max(0.5f, settings.slowStandoff);
        return new Goal(isClear(playerPosition, point) ? point : policePosition, false);
    }

    static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
        !float.IsInfinity(value.x) && !float.IsInfinity(value.y);
}
