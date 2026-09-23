using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Fresh conservative full-body motion envelopes for local free-drive avoidance.</summary>
internal sealed class PoliceFreeAvoidanceQuery : PoliceFreePursuitDriver.IAvoidanceQuery {
    const float FarTargetDistance = 10f;
    const float FarTargetHorizon = 1.25f;
    readonly PoliceVehicleBody body;
    readonly PoliceFreeNavigationGeometry.Footprint footprint;
    readonly PhysicsScene2D physics;
    readonly Rigidbody2D targetBody;
    readonly Collider2D[] hits;
    readonly ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
    readonly float minimumRadius;
    readonly float maximumDistance;
    readonly Rect bounds;
    readonly Vector2 origin;
    float targetDistance;

    internal PoliceFreeAvoidanceQuery(PoliceVehicleBody body, PolicePrefabGeometry geometry,
        NpcMotorSettings motor, PoliceDrivingSettings driving, Rect localBounds, Vector2 origin, Rigidbody2D targetBody) {
        this.body = body;
        this.origin = origin;
        this.targetBody = targetBody;
        bounds = localBounds;
        footprint = new PoliceFreeNavigationGeometry.Footprint(geometry.colliderFootprint, geometry.colliderOffset);
        minimumRadius = motor.minimumTurningRadius;
        maximumDistance = driving.maxSweepDistance;
        hits = new Collider2D[driving.sensorBuffer];
        physics = body.gameObject.scene.GetPhysicsScene2D();
    }

    internal void SetTargetDistance(float distance) {
        targetDistance = Mathf.Max(0f, distance);
    }

    /// <summary>Checks the executable curvature, including changing body orientation and all actors except self.</summary>
    public PoliceFreePursuitDriver.AvoidanceResult Check(Vector2 start, Vector2 end, float headingDegrees,
        float horizonSeconds, float lateralOffset) {
        if (body == null || body.Body == null || !physics.IsValid() || minimumRadius <= 0f)
            return new PoliceFreePursuitDriver.AvoidanceResult(false, 0f);
        Vector2 forward = body.transform.up;
        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 delta = end - start;
        float curvature = Mathf.Clamp(-2f * Vector2.Dot(delta, right) / Mathf.Max(delta.sqrMagnitude, 0.0001f),
            -1f / minimumRadius, 1f / minimumRadius);
        float awarenessHorizon = targetDistance >= FarTargetDistance
            ? Mathf.Max(horizonSeconds, FarTargetHorizon) : horizonSeconds;
        float travel = Mathf.Min(maximumDistance, Mathf.Max(footprint.size.y,
            body.Body.linearVelocity.magnitude * awarenessHorizon));
        if (Mathf.Abs(curvature * travel) > Mathf.PI)
            return new PoliceFreePursuitDriver.AvoidanceResult(false, 0f);
        PoliceFreeNavigationGeometry.Envelope envelope;
        bool valid = Mathf.Abs(curvature) < 0.0001f
            ? PoliceFreeNavigationGeometry.TryGetLineEnvelope(start - origin, start - origin + forward * travel,
                footprint, body.Body.rotation, 0f, out envelope)
            : PoliceFreeNavigationGeometry.TryGetArcEnvelope(start - origin, body.Body.rotation, 1f / Mathf.Abs(curvature),
                curvature * travel * Mathf.Rad2Deg, footprint, 0f, out envelope);
        if (!valid || !PoliceFreeNavigationGeometry.IsWithinBounds(envelope, bounds))
            return new PoliceFreePursuitDriver.AvoidanceResult(false, 0f);
        int count = physics.OverlapBox(envelope.center + origin, envelope.size, envelope.headingDegrees, filter, hits);
        if (count >= hits.Length) return new PoliceFreePursuitDriver.AvoidanceResult(false, 0f);
        for (int index = 0; index < count; index++) {
            Collider2D hit = hits[index];
            // Only the player is intentionally ignored. Walls, buildings, civilians, and every
            // other collider remain avoidance targets; reckless pursuit may still collide when
            // speed or turning radius makes the maneuver physically impossible.
            if (hit != null && hit.attachedRigidbody != body.Body && hit.attachedRigidbody != targetBody)
                return new PoliceFreePursuitDriver.AvoidanceResult(false, 0f);
        }
        return new PoliceFreePursuitDriver.AvoidanceResult(true, float.PositiveInfinity);
    }
}
