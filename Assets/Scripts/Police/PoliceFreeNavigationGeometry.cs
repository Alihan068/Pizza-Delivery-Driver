using UnityEngine;

/// <summary>Pure analytic footprint envelopes used by free-drive navigation queries.</summary>
public static class PoliceFreeNavigationGeometry {
    /// <summary>Describes a vehicle footprint and the local offset from its pivot to its collider center.</summary>
    public readonly struct Footprint {
        /// <summary>Creates a footprint in (width, length) order.</summary>
        public Footprint(Vector2 size, Vector2 offset) { this.size = size; this.offset = offset; }

        /// <summary>Full collider size in vehicle-local axes.</summary>
        public readonly Vector2 size;

        /// <summary>Collider-center offset from the vehicle pivot in vehicle-local axes.</summary>
        public readonly Vector2 offset;
    }

    /// <summary>Conservative oriented envelope submitted to a clearance source.</summary>
    public readonly struct Envelope {
        /// <summary>Creates an envelope.</summary>
        public Envelope(Vector2 center, Vector2 size, float headingDegrees) {
            this.center = center; this.size = size; this.headingDegrees = headingDegrees;
        }

        /// <summary>Center of the oriented envelope.</summary>
        public readonly Vector2 center;

        /// <summary>Full size of the oriented envelope in local axes.</summary>
        public readonly Vector2 size;

        /// <summary>Heading of the envelope, counter-clockwise from +Y.</summary>
        public readonly float headingDegrees;
    }

    /// <summary>Builds the exact fixed-heading translation envelope for a straight sweep.</summary>
    public static bool TryGetLineEnvelope(Vector2 startPivot, Vector2 endPivot, Footprint footprint,
        float headingDegrees, float margin, out Envelope envelope) {
        envelope = default;
        if (!IsFinite(startPivot) || !IsFinite(endPivot) || !IsValidFootprint(footprint) ||
            !IsFinite(headingDegrees) || !IsFinite(margin) || margin < 0f) return false;

        Vector2 localDelta = Rotate(endPivot - startPivot, -headingDegrees);
        Vector2 center = (startPivot + endPivot) * 0.5f + Rotate(footprint.offset, headingDegrees);
        Vector2 size = footprint.size + new Vector2(Mathf.Abs(localDelta.x), Mathf.Abs(localDelta.y)) +
            Vector2.one * (2f * margin);
        if (!IsFinite(center) || !IsFinite(size) || size.x <= 0f || size.y <= 0f) return false;
        envelope = new Envelope(center, size, headingDegrees);
        return true;
    }

    /// <summary>
    /// Builds an analytic chord envelope for a circular pose sweep. Rotation and arc sagitta pads
    /// cover the full changing-heading footprint between the two poses.
    /// </summary>
    public static bool TryGetArcEnvelope(Vector2 startPivot, float startHeadingDegrees, float turnRadius,
        float deltaHeadingDegrees, Footprint footprint, float margin, out Envelope envelope) {
        envelope = default;
        if (!IsFinite(startPivot) || !IsFinite(startHeadingDegrees) || !IsFinite(turnRadius) ||
            !IsFinite(deltaHeadingDegrees) || !IsValidFootprint(footprint) || !IsFinite(margin) ||
            turnRadius <= 0f || margin < 0f || Mathf.Abs(deltaHeadingDegrees) <= 0f) return false;

        float deltaRadians = deltaHeadingDegrees * Mathf.Deg2Rad;
        Vector2 startForward = Rotate(Vector2.up, startHeadingDegrees);
        Vector2 left = new Vector2(-startForward.y, startForward.x);
        Vector2 circleCenter = startPivot + left * Mathf.Sign(deltaRadians) * turnRadius;
        Vector2 fromCenter = startPivot - circleCenter;
        float startAngle = Mathf.Atan2(fromCenter.y, fromCenter.x);
        float middleAngle = startAngle + deltaRadians * 0.5f;
        float endAngle = startAngle + deltaRadians;
        Vector2 middlePivot = circleCenter + new Vector2(Mathf.Cos(middleAngle), Mathf.Sin(middleAngle)) * turnRadius;
        Vector2 endPivot = circleCenter + new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * turnRadius;
        float middleHeading = startHeadingDegrees + deltaHeadingDegrees * 0.5f;
        Vector2 chord = Rotate(endPivot - startPivot, -middleHeading);
        float bodyRadius = footprint.offset.magnitude + footprint.size.magnitude * 0.5f;
        float rotationPad = 2f * bodyRadius * Mathf.Sin(Mathf.Abs(deltaRadians) * 0.25f);
        float arcPad = turnRadius * (1f - Mathf.Cos(Mathf.Abs(deltaRadians) * 0.5f));
        float pad = rotationPad + arcPad + margin;
        Vector2 center = middlePivot + Rotate(footprint.offset, middleHeading);
        Vector2 size = footprint.size + new Vector2(Mathf.Abs(chord.x), Mathf.Abs(chord.y)) + Vector2.one * (2f * pad);
        if (!IsFinite(center) || !IsFinite(size) || size.x <= 0f || size.y <= 0f) return false;
        envelope = new Envelope(center, size, middleHeading);
        return true;
    }

    /// <summary>Returns whether an oriented envelope is wholly inside finite local bounds.</summary>
    public static bool IsWithinBounds(Envelope envelope, Rect localBounds) {
        if (!IsFinite(envelope.center) || !IsFinite(envelope.size) || !IsFinite(envelope.headingDegrees) ||
            envelope.size.x <= 0f || envelope.size.y <= 0f || !IsValidRect(localBounds)) return false;
        Vector2 right = Rotate(Vector2.right, envelope.headingDegrees);
        Vector2 forward = Rotate(Vector2.up, envelope.headingDegrees);
        Vector2 half = envelope.size * 0.5f;
        Vector2 extent = new Vector2(Mathf.Abs(right.x) * half.x + Mathf.Abs(forward.x) * half.y,
            Mathf.Abs(right.y) * half.x + Mathf.Abs(forward.y) * half.y);
        return envelope.center.x - extent.x >= localBounds.xMin && envelope.center.x + extent.x <= localBounds.xMax &&
            envelope.center.y - extent.y >= localBounds.yMin && envelope.center.y + extent.y <= localBounds.yMax;
    }

    /// <summary>Rotates a vector by degrees counter-clockwise.</summary>
    public static Vector2 Rotate(Vector2 vector, float degrees) {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians) * vector.x - Mathf.Sin(radians) * vector.y,
            Mathf.Sin(radians) * vector.x + Mathf.Cos(radians) * vector.y);
    }

    static bool IsValidFootprint(Footprint footprint) => IsFinite(footprint.size) && IsFinite(footprint.offset) &&
        footprint.size.x > 0f && footprint.size.y > 0f;

    static bool IsValidRect(Rect rect) => IsFinite(rect.position) && IsFinite(rect.size) &&
        rect.width > 0f && rect.height > 0f && IsFinite(rect.xMax) && IsFinite(rect.yMax);

    static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
