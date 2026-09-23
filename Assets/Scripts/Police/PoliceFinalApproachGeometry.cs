using UnityEngine;

/// <summary>
/// Pure, forward-only final-approach geometry. A plan is either a straight segment or one bounded
/// constant-radius arc followed by a tangent straight segment; it never changes the road graph.
/// </summary>
public sealed class PoliceFinalApproachGeometry {
    /// <summary>Available final-approach shapes.</summary>
    public enum Kind { Straight, ArcThenStraight }

    /// <summary>Immutable geometry selected by the bounded solver.</summary>
    public readonly struct Plan {
        /// <summary>Shape of the plan.</summary>
        public readonly Kind kind;
        /// <summary>Entry pivot position.</summary>
        public readonly Vector2 start;
        /// <summary>Target pivot position.</summary>
        public readonly Vector2 target;
        /// <summary>Arc exit pivot position, or start for a straight plan.</summary>
        public readonly Vector2 arcExit;
        /// <summary>Entry tangent.</summary>
        public readonly Vector2 entryDirection;
        /// <summary>Exit tangent and straight connector direction.</summary>
        public readonly Vector2 exitDirection;
        /// <summary>Circle radius for an arc plan, otherwise zero.</summary>
        public readonly float radius;
        /// <summary>Signed turn direction: -1 right, +1 left.</summary>
        public readonly int turnSign;
        /// <summary>Arc travel length.</summary>
        public readonly float arcLength;
        /// <summary>Final straight travel length.</summary>
        public readonly float straightLength;

        /// <summary>Creates an immutable final-approach plan.</summary>
        public Plan(Kind kind, Vector2 start, Vector2 target, Vector2 arcExit, Vector2 entryDirection,
            Vector2 exitDirection, float radius, int turnSign, float arcLength, float straightLength) {
            this.kind = kind; this.start = start; this.target = target; this.arcExit = arcExit;
            this.entryDirection = entryDirection; this.exitDirection = exitDirection; this.radius = radius;
            this.turnSign = turnSign; this.arcLength = arcLength; this.straightLength = straightLength;
        }

        /// <summary>Total forward travel including the whole arc and connector.</summary>
        public float TotalLength => arcLength + straightLength;
        /// <summary>Samples the immutable path by measured forward distance.</summary>
        public Vector2 Sample(float distance) => PoliceFinalApproachGeometry.Sample(this, distance);
    }

    const float MinimumAngle = 0.001f;
    const float MaximumAngle = 179.5f;
    const float TangentEpsilon = 0.0001f;

    /// <summary>Solves a bounded forward-only straight or tangent arc path.</summary>
    public static bool TrySolve(Vector2 start, Vector2 entryDirection, Vector2 target, float minimumRadius,
        float maximumDistance, out Plan plan) {
        plan = default;
        if (!Finite(start) || !Finite(target) || !Finite(entryDirection) || !FinitePositive(minimumRadius) ||
            !FinitePositive(maximumDistance) || entryDirection.sqrMagnitude <= 0f) return false;
        Vector2 forward = entryDirection.normalized;
        Vector2 delta = target - start;
        float straightDistance = Vector2.Dot(delta, forward);
        float lateral = Cross(forward, delta);
        if (straightDistance > TangentEpsilon && Mathf.Abs(lateral) <= TangentEpsilon) {
            plan = new Plan(Kind.Straight, start, target, start, forward, forward, 0f, 0, 0f, straightDistance);
            return straightDistance <= maximumDistance;
        }
        float distance = delta.magnitude;
        if (!FinitePositive(distance) || distance > maximumDistance) return false;
        float maximumRadius = Mathf.Max(minimumRadius, distance + minimumRadius);
        Plan best = default;
        bool found = false;
        // The radius is bounded and deterministic. The tangent construction itself is exact; the
        // small radius sweep only chooses among the finite family of safe, motor-feasible paths.
        for (int sign = -1; sign <= 1; sign += 2) {
            for (int index = 0; index < 8; index++) {
                float radius = Mathf.Lerp(minimumRadius, maximumRadius, index / 7f);
                if (!TryTangent(start, forward, target, radius, sign, out Plan candidate)) continue;
                if (candidate.TotalLength > maximumDistance) continue;
                if (!found || candidate.TotalLength < best.TotalLength) { best = candidate; found = true; }
            }
        }
        if (!found) return false;
        plan = best;
        return true;
    }

    /// <summary>Returns a pose on the plan, clamped to its finite travel interval.</summary>
    public static Vector2 Sample(Plan plan, float distance) {
        if (!FiniteNonnegative(distance) || !FinitePositive(plan.entryDirection.sqrMagnitude)) return Vector2.zero;
        if (plan.kind == Kind.Straight || distance <= 0f) return plan.kind == Kind.Straight
            ? plan.start + plan.exitDirection * Mathf.Min(plan.straightLength, Mathf.Max(0f, distance)) : plan.start;
        if (distance >= plan.TotalLength) return plan.target;
        if (distance <= plan.arcLength) {
            float angle = distance / plan.radius;
            Vector2 left = new Vector2(-plan.entryDirection.y, plan.entryDirection.x);
            Vector2 radial = -plan.turnSign * left;
            Vector2 center = plan.start - radial * plan.radius;
            Vector2 rotated = Rotate(radial, plan.turnSign * angle * Mathf.Rad2Deg);
            return center + rotated * plan.radius;
        }
        return plan.arcExit + plan.exitDirection * (distance - plan.arcLength);
    }

    /// <summary>Returns the forward arc/connector progress nearest to an observed pivot.</summary>
    public static float ProjectProgress(Plan plan, Vector2 position) {
        if (!Finite(position)) return -1f;
        if (plan.kind == Kind.Straight) return Mathf.Clamp(Vector2.Dot(position - plan.start, plan.exitDirection), 0f, plan.straightLength);
        float arcProgress = 0f;
        Vector2 left = new Vector2(-plan.entryDirection.y, plan.entryDirection.x);
        Vector2 radial = -plan.turnSign * left;
        Vector2 center = plan.start - radial * plan.radius;
        Vector2 observed = position - center;
        if (observed.sqrMagnitude > 0f) {
            float signed = Mathf.Atan2(Cross(radial, observed.normalized), Vector2.Dot(radial, observed.normalized));
            if (plan.turnSign < 0) signed = -signed;
            arcProgress = Mathf.Clamp(signed, 0f, plan.arcLength / plan.radius) * plan.radius;
        }
        float lineProgress = plan.arcLength + Mathf.Clamp(Vector2.Dot(position - plan.arcExit, plan.exitDirection), 0f, plan.straightLength);
        return (position - plan.Sample(lineProgress)).sqrMagnitude < (position - plan.Sample(arcProgress)).sqrMagnitude
            ? lineProgress : arcProgress;
    }

    /// <summary>Returns the unit tangent at a clamped path distance, including the exact arc exit.</summary>
    public static Vector2 DirectionAt(Plan plan, float distance) => plan.kind == Kind.Straight || distance >= plan.arcLength
        ? plan.exitDirection : Rotate(plan.entryDirection, Mathf.Max(0f, distance) / plan.radius * plan.turnSign * Mathf.Rad2Deg);

    /// <summary>
    /// Checks the entire remaining arc and tangent, charging every query. Each arc chord box is
    /// expanded by its exact circular sagitta and the padded rotating footprint circumradius plus
    /// offset; sampling therefore leaves no unchecked arc or yaw gaps. Tracking tolerance is not padding.
    /// </summary>
    public static bool IsRemainingClear(Plan plan, float progress, Vector2 padded, Vector2 offset,
        Rect bounds, IAreaClearanceQuery query, ref int work) {
        if (query == null || !FiniteNonnegative(progress) || !FinitePositive(plan.TotalLength) ||
            !FinitePositive(padded.x) || !FinitePositive(padded.y) || !Finite(offset)) return false;
        if (plan.kind == Kind.ArcThenStraight && progress < plan.arcLength) {
            if (!FinitePositive(plan.radius)) return false;
            // At most eight chords per half-circle; their envelopes, not samples, prove coverage.
            int count = Mathf.CeilToInt((plan.arcLength - progress) / plan.radius / (Mathf.PI / 8f));
            if (count <= 0 || count > 8) return false;
            float step = (plan.arcLength - progress) / count;
            float sagitta = plan.radius * (1f - Mathf.Cos(step / plan.radius * 0.5f));
            float expansion = padded.magnitude * 0.5f + offset.magnitude + sagitta;
            for (int i = 0; i < count; i++) {
                Vector2 a = plan.Sample(progress + step * i), b = plan.Sample(progress + step * (i + 1));
                Vector2 size = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)) + Vector2.one * (2f * expansion);
                if (work < 1) return false;
                work--;
                if (!RoadFootprintClearance.IsPoseClear((a + b) * 0.5f, size, Vector2.zero, 0f, bounds, null, query)) return false;
            }
        }
        if (work < 1) return false;
        work--;
        float heading = MapNavigationCoordinates.DirectionToHeadingDegrees(plan.exitDirection);
        return RoadFootprintClearance.IsSweepClear(plan.Sample(Mathf.Max(progress, plan.arcLength)), plan.target,
            padded, offset, heading, heading, bounds, null, query);
    }

    static bool TryTangent(Vector2 start, Vector2 forward, Vector2 target, float radius, int sign, out Plan plan) {
        plan = default;
        Vector2 left = new Vector2(-forward.y, forward.x);
        Vector2 center = start + left * sign * radius;
        Vector2 fromCenter = target - center;
        float distance = fromCenter.magnitude;
        if (!FinitePositive(distance) || distance <= radius + TangentEpsilon) return false;
        float baseAngle = Mathf.Atan2(fromCenter.y, fromCenter.x);
        float offset = Mathf.Acos(Mathf.Clamp(radius / distance, -1f, 1f));
        for (int branch = -1; branch <= 1; branch += 2) {
            Vector2 radial = Rotate(Vector2.right, (baseAngle + branch * offset) * Mathf.Rad2Deg);
            Vector2 exit = center + radial * radius;
            Vector2 tangent = sign > 0 ? new Vector2(-radial.y, radial.x) : new Vector2(radial.y, -radial.x);
            float turn = Vector2.Angle(forward, tangent);
            float signedTurn = sign * SignedAngle(forward, tangent);
            float straight = Vector2.Dot(target - exit, tangent);
            if (!FinitePositive(straight) || signedTurn <= MinimumAngle || signedTurn > MaximumAngle || turn > MaximumAngle) continue;
            float arcLength = radius * signedTurn * Mathf.Deg2Rad;
            if (!FinitePositive(arcLength)) continue;
            plan = new Plan(Kind.ArcThenStraight, start, target, exit, forward, tangent, radius, sign, arcLength, straight);
            return true;
        }
        return false;
    }

    static Vector2 Rotate(Vector2 value, float degrees) {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(value.x * Mathf.Cos(radians) - value.y * Mathf.Sin(radians), value.x * Mathf.Sin(radians) + value.y * Mathf.Cos(radians));
    }
    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static float SignedAngle(Vector2 a, Vector2 b) => Mathf.Atan2(Cross(a, b), Vector2.Dot(a, b)) * Mathf.Rad2Deg;
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
