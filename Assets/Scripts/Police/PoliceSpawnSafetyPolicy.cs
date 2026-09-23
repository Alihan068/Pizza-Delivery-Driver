using System.Collections.Generic;
using UnityEngine;

/// <summary>Detached candidate supplied by a police spawn or relocation planner.</summary>
public sealed class PoliceSpawnCandidate {
    /// <summary>Stable map entry identifier that produced this candidate.</summary>
    public string entryId;
    /// <summary>World position of the candidate pivot.</summary>
    public Vector2 position;
    /// <summary>World footprint used for legacy clearance checks.</summary>
    public Vector2 footprint;
    /// <summary>World heading in degrees using the map heading convention.</summary>
    public float headingDegrees;
    /// <summary>Map-local pivot supplied by a planner that already performed the one-way conversion.</summary>
    public Vector2 localPosition;
    /// <summary>Whether <see cref="localPosition"/> is valid and should be reused.</summary>
    public bool hasLocalPosition;
}

/// <summary>
/// Runtime geometry extracted from the selected police prefab. Dimensions are already scaled into
/// world units; offsets remain in the prefab's local right/forward axes and are rotated by heading.
/// A zero visual footprint means that the prefab has no separately authored renderer envelope.
/// </summary>
public sealed class PolicePrefabGeometry {
    /// <summary>Scaled collider width and length in local right/forward axes.</summary>
    public readonly Vector2 colliderFootprint;
    /// <summary>Scaled collider-center offset from the root pivot in local axes.</summary>
    public readonly Vector2 colliderOffset;
    /// <summary>Scaled renderer envelope in local axes, or zero when unavailable.</summary>
    public readonly Vector2 visualFootprint;
    /// <summary>Scaled renderer-envelope offset from the root pivot in local axes.</summary>
    public readonly Vector2 visualOffset;

    /// <summary>Creates a detached geometry snapshot so planning never reads a live Transform.</summary>
    public PolicePrefabGeometry(Vector2 colliderFootprint, Vector2 colliderOffset,
        Vector2 visualFootprint = default, Vector2 visualOffset = default) {
        this.colliderFootprint = colliderFootprint;
        this.colliderOffset = colliderOffset;
        this.visualFootprint = visualFootprint;
        this.visualOffset = visualOffset;
    }

    /// <summary>Returns the conservative pivot-to-collider surface radius used by reaction spacing.</summary>
    public float ColliderSurfaceRadius => colliderFootprint.magnitude * 0.5f + colliderOffset.magnitude;

    /// <summary>Conservative radius containing both collision and rendered geometry.</summary>
    public float WholeSurfaceRadius => Mathf.Max(ColliderSurfaceRadius, visualFootprint.magnitude * 0.5f + visualOffset.magnitude);

    /// <summary>Returns true when all supplied dimensions and offsets are finite and usable.</summary>
    public bool IsValid => FinitePositive(colliderFootprint.x) && FinitePositive(colliderFootprint.y) &&
        Finite(colliderOffset) && ValidOptionalFootprint(visualFootprint) && Finite(visualOffset);

    static bool ValidOptionalFootprint(Vector2 value) => value == Vector2.zero ||
        FinitePositive(value.x) && FinitePositive(value.y);
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
}

/// <summary>
/// Explicit safety inputs for one detached police-placement attempt. World clearance must be a
/// fresh static-plus-dynamic query with saturation treated as blocked; local clearance is the
/// map-local static query used by complete footprint bounds and no-spawn checks.
/// </summary>
public sealed class PoliceSpawnSafetyContext {
    /// <summary>Current world-space camera rectangle before the safety margin is applied.</summary>
    public Rect cameraBounds;
    /// <summary>Additional camera margin that must remain invisible.</summary>
    public float cameraMargin;
    /// <summary>Current player pivot in world space.</summary>
    public Vector2 playerPosition;
    /// <summary>Current player velocity in world units per second.</summary>
    public Vector2 playerVelocity;
    /// <summary>Current player collider footprint in world units, when available.</summary>
    public Vector2 playerFootprint;
    /// <summary>Player pivot-to-surface radius in world units.</summary>
    public float playerSurfaceRadius;
    /// <summary>Surface distance required in addition to both body radii.</summary>
    public float minimumDistance;
    /// <summary>Reaction interval multiplied by player speed for the placement envelope.</summary>
    public float reactionSeconds;
    /// <summary>Map origin used to convert the candidate world pivot to local space once.</summary>
    public Vector2 mapOriginWorld;
    /// <summary>Map-local bounds that the complete collider footprint must fit inside.</summary>
    public Rect localMapBounds;
    /// <summary>Authored map-local no-spawn rectangles.</summary>
    public IReadOnlyList<NoSpawnRegion> noSpawnRegions;
    /// <summary>Actual scaled prefab collider and optional renderer envelope.</summary>
    public PolicePrefabGeometry geometry;
    /// <summary>Fresh world query covering static and dynamic blockers; null is unsafe.</summary>
    public IAreaClearanceQuery worldClearance;
    /// <summary>Fresh map-local static query; null is unsafe.</summary>
    public IAreaClearanceQuery localStaticClearance;
}

/// <summary>
/// Pure whole-footprint safety gate for police reinforcement placement and relocation. It does not
/// select, retry or teleport a vehicle; callers own candidate ordering and life-state preservation.
/// </summary>
public static class PoliceSpawnSafetyPolicy {
    /// <summary>Returns true only for a finite, strictly positive frozen relocation radius.</summary>
    public static bool IsValidReferenceRadius(float radius) => FinitePositive(radius);
    /// <summary>
    /// Preserves the legacy signature while applying fail-closed camera, clearance and full-OBB
    /// semantics. The legacy candidate has no offset or separate visual envelope.
    /// </summary>
    public static bool IsSafe(PoliceSpawnCandidate candidate, Rect cameraBounds, float cameraMargin,
        Vector2 playerPosition, float minimumReactionDistance, IAreaClearanceQuery clearance, out string reason) {
        reason = null;
        if (candidate == null || !Finite(candidate.position) || !Finite(candidate.footprint) ||
            !Finite(candidate.headingDegrees) || candidate.footprint.x <= 0f || candidate.footprint.y <= 0f)
            return Fail("candidate geometry is invalid", out reason);
        if (!IsValidCamera(cameraBounds) || !FiniteNonNegative(cameraMargin) || !Finite(playerPosition) ||
            !FiniteNonNegative(minimumReactionDistance)) return Fail("spawn safety input is invalid", out reason);
        if (clearance == null) return Fail("clearance query is required", out reason);
        if (IsWholeFootprintVisible(candidate, cameraBounds, cameraMargin)) return Fail("candidate footprint is visible", out reason);
        float requiredDistance = candidate.footprint.magnitude * 0.5f + minimumReactionDistance;
        if (Vector2.Distance(candidate.position, playerPosition) < requiredDistance)
            return Fail("candidate is inside reaction distance", out reason);
        if (!clearance.IsAreaClear(candidate.position, candidate.footprint, candidate.headingDegrees))
            return Fail("candidate footprint is blocked", out reason);
        return true;
    }

    /// <summary>Evaluates one candidate against whole-footprint visibility, reaction, map, and queries.</summary>
    /// <param name="candidate">World-space pivot and heading to inspect.</param>
    /// <param name="context">Detached current camera, player, geometry, map, and query inputs.</param>
    /// <param name="reason">Stable failure reason, or null on success.</param>
    /// <returns>True only when the candidate is safe to consume.</returns>
    public static bool IsSafe(PoliceSpawnCandidate candidate, PoliceSpawnSafetyContext context, out string reason) {
        reason = null;
        if (candidate == null || context == null || context.geometry == null || !context.geometry.IsValid)
            return Fail("candidate or geometry is invalid", out reason);
        if (!Finite(candidate.position) || !Finite(candidate.footprint) || candidate.footprint.x <= 0f || candidate.footprint.y <= 0f ||
            !Finite(candidate.headingDegrees) || !IsValidCamera(context.cameraBounds) ||
            !FiniteNonNegative(context.cameraMargin) || !Finite(context.playerPosition) || !Finite(context.playerVelocity) ||
            !Finite(context.playerFootprint) || !ValidOptionalFootprint(context.playerFootprint) ||
            !FiniteNonNegative(context.playerSurfaceRadius) || !FiniteNonNegative(context.minimumDistance) ||
            !FiniteNonNegative(context.reactionSeconds) || !Finite(context.mapOriginWorld) ||
            !IsValidRect(context.localMapBounds) || context.worldClearance == null || context.localStaticClearance == null)
            return Fail("spawn safety input is invalid", out reason);

        if (IsWholeFootprintVisible(candidate, context.cameraBounds, context.cameraMargin, context.geometry))
            return Fail("candidate footprint is visible", out reason);

        float speed = context.playerVelocity.magnitude;
        float playerSurfaceRadius = Mathf.Max(context.playerSurfaceRadius, context.playerFootprint.magnitude * 0.5f);
        float requiredDistance = context.geometry.ColliderSurfaceRadius + playerSurfaceRadius +
            context.minimumDistance + speed * context.reactionSeconds;
        float pivotDistance = Vector2.Distance(candidate.position, context.playerPosition);
        if (!Finite(requiredDistance) || !Finite(pivotDistance) || pivotDistance < requiredDistance)
            return Fail("candidate is inside reaction distance", out reason);

        Vector2 localPosition = candidate.hasLocalPosition ? candidate.localPosition :
            MapNavigationCoordinates.WorldToLocal(candidate.position, context.mapOriginWorld);
        if (!Finite(localPosition)) return Fail("candidate local geometry is invalid", out reason);
        if (!RoadFootprintClearance.IsPoseClear(localPosition, context.geometry.colliderFootprint,
            context.geometry.colliderOffset, candidate.headingDegrees, context.localMapBounds,
            context.noSpawnRegions, context.localStaticClearance))
            return Fail("candidate footprint is outside map or blocked by local geometry", out reason);

        Vector2 worldCenter = OffsetCenter(candidate.position, context.geometry.colliderOffset, candidate.headingDegrees);
        if (!context.worldClearance.IsAreaClear(worldCenter, context.geometry.colliderFootprint, candidate.headingDegrees))
            return Fail("candidate footprint is blocked by world geometry", out reason);
        return true;
    }

    /// <summary>
    /// Returns true when any part of the rotated/scaled collider or larger renderer envelope touches
    /// the camera rectangle after its margin is applied. Boundary contact is intentionally visible.
    /// </summary>
    public static bool IsWholeFootprintVisible(PoliceSpawnCandidate candidate, Rect cameraBounds,
        float cameraMargin, PolicePrefabGeometry geometry) {
        if (candidate == null || geometry == null || !geometry.IsValid || !IsValidCamera(cameraBounds) ||
            !FiniteNonNegative(cameraMargin) || !Finite(candidate.position) || !Finite(candidate.headingDegrees) ||
            !FinitePositive(geometry.ColliderSurfaceRadius)) return true;
        if (!TryExpand(cameraBounds, cameraMargin, out Rect expanded)) return true;
        return IsWholeFootprintVisible(expanded, candidate.position, candidate.headingDegrees, geometry);
    }

    /// <summary>Convenience visibility overload for callers that have only the legacy footprint.</summary>
    public static bool IsWholeFootprintVisible(PoliceSpawnCandidate candidate, Rect cameraBounds, float cameraMargin) {
        if (candidate == null || !IsValidCamera(cameraBounds) || !FiniteNonNegative(cameraMargin) ||
            !Finite(candidate.position) || !Finite(candidate.headingDegrees) || !Finite(candidate.footprint) ||
            candidate.footprint.x <= 0f || candidate.footprint.y <= 0f) return true;
        if (!TryExpand(cameraBounds, cameraMargin, out Rect expanded)) return true;
        return IsWholeFootprintVisible(expanded, candidate.position,
            candidate.headingDegrees, candidate.footprint, Vector2.zero, Vector2.zero, Vector2.zero);
    }

    static bool IsWholeFootprintVisible(Rect expanded, Vector2 pivot, float heading, PolicePrefabGeometry geometry) {
        return IsWholeFootprintVisible(expanded, pivot, heading, geometry.colliderFootprint, geometry.colliderOffset,
            geometry.visualFootprint, geometry.visualOffset);
    }

    static bool IsWholeFootprintVisible(Rect expanded, Vector2 pivot, float heading, Vector2 colliderFootprint,
        Vector2 colliderOffset, Vector2 visualFootprint, Vector2 visualOffset) {
        if (Intersects(expanded, pivot, colliderFootprint, colliderOffset, heading)) return true;
        return visualFootprint != Vector2.zero && Intersects(expanded, pivot, visualFootprint, visualOffset, heading);
    }

    static Vector2 OffsetCenter(Vector2 pivot, Vector2 offset, float heading) {
        return pivot + Rotate(Vector2.right, heading) * offset.x + Rotate(Vector2.up, heading) * offset.y;
    }

    static bool Intersects(Rect rectangle, Vector2 pivot, Vector2 footprint, Vector2 offset, float heading) {
        Vector2 right = Rotate(Vector2.right, heading);
        Vector2 forward = Rotate(Vector2.up, heading);
        Vector2 center = pivot + right * offset.x + forward * offset.y;
        Vector2 half = footprint * 0.5f;
        Vector2 rectangleCenter = rectangle.center;
        Vector2 rectangleHalf = rectangle.size * 0.5f;
        if (!Finite(center) || !Finite(half) || !Finite(rectangleCenter) || !Finite(rectangleHalf)) return true;
        Vector2 delta = rectangleCenter - center;
        float xRadius = rectangleHalf.x + Mathf.Abs(right.x) * half.x + Mathf.Abs(forward.x) * half.y;
        float yRadius = rectangleHalf.y + Mathf.Abs(right.y) * half.x + Mathf.Abs(forward.y) * half.y;
        float rightRadius = half.x + rectangleHalf.x * Mathf.Abs(right.x) + rectangleHalf.y * Mathf.Abs(right.y);
        float forwardRadius = half.y + rectangleHalf.x * Mathf.Abs(forward.x) + rectangleHalf.y * Mathf.Abs(forward.y);
        if (!Finite(delta) || !Finite(xRadius) || !Finite(yRadius) || !Finite(rightRadius) || !Finite(forwardRadius)) return true;
        if (Mathf.Abs(delta.x) > xRadius) return false;
        if (Mathf.Abs(delta.y) > yRadius) return false;
        if (Mathf.Abs(Vector2.Dot(delta, right)) > rightRadius) return false;
        if (Mathf.Abs(Vector2.Dot(delta, forward)) > forwardRadius) return false;
        return true;
    }

    static bool TryExpand(Rect value, float margin, out Rect expanded) {
        expanded = Rect.MinMaxRect(value.xMin - margin, value.yMin - margin,
            value.xMax + margin, value.yMax + margin);
        return IsValidRect(expanded);
    }
    static bool IsValidCamera(Rect value) => IsValidRect(value);
    static bool IsValidRect(Rect value) => Finite(value.position) && Finite(value.size) &&
        Finite(value.xMax) && Finite(value.yMax) && value.width > 0f && value.height > 0f;
    static Vector2 Rotate(Vector2 vector, float degrees) {
        float radians = degrees * Mathf.Deg2Rad;
        float cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
        return new Vector2(cosine * vector.x - sine * vector.y, sine * vector.x + cosine * vector.y);
    }
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool ValidOptionalFootprint(Vector2 value) => value == Vector2.zero ||
        FinitePositive(value.x) && FinitePositive(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonNegative(float value) => Finite(value) && value >= 0f;
    static bool Fail(string message, out string reason) { reason = message; return false; }
}
