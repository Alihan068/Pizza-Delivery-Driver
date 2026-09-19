using UnityEngine;

/// <summary>
/// Reusable forward-obstacle query for one NPC: a sweep of its actual collider shape, over a range
/// derived from its current speed, authored stopping distance (brakeDeceleration), reaction time and
/// minimum gap — never a fixed hardcoded range. Filters out its own collider(s) and any trigger
/// collider (a trigger is game logic, not a physical obstacle). Makes no civilian/police intent
/// decision itself; callers act on what it reports.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class VehicleObstacleSensor : MonoBehaviour {
    [Tooltip("Layers this sensor can detect. Defaults to everything — do not assume a dedicated 'Player' layer; the project's own player prefabs currently sit on Default.")]
    [SerializeField] LayerMask detectionMask = ~0;

    Rigidbody2D rb;
    Collider2D selfCollider;
    NpcMotorSettings settings;
    RaycastHit2D[] resultsBuffer = new RaycastHit2D[8];

    /// <summary>True when the last query's hit count reached the buffer capacity — the result may be missing farther/closer hits. Never silently ignored: callers must grow the buffer or fail-safe stop.</summary>
    public bool LastQuerySaturated { get; private set; }

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        selfCollider = GetComponent<Collider2D>();
    }

    /// <summary>Assigns the motor tuning this sensor derives its sweep range from, and sizes its result buffer.</summary>
    public void Configure(NpcMotorSettings motorSettings, int bufferSize = 8) {
        settings = motorSettings;
        resultsBuffer = new RaycastHit2D[Mathf.Max(1, bufferSize)];
    }

    /// <summary>
    /// Sweeps the collider forward, preserving its rotation, offset and scale, over
    /// speed+stopping-distance+reaction-margin. Reports the closest genuine obstacle in the
    /// collider's own physics scene, if any; distance is the clearance from its leading surface.
    /// </summary>
    public bool TryDetectForwardObstacle(out float distance, out Collider2D obstacle) {
        distance = float.PositiveInfinity;
        obstacle = null;
        LastQuerySaturated = false;
        if (rb == null || selfCollider == null || settings == null) return false;

        Vector2 forward = transform.up;
        float forwardSpeed = Mathf.Max(0f, Vector2.Dot(rb.linearVelocity, forward));
        float stoppingDistance = (forwardSpeed * forwardSpeed) / Mathf.Max(0.01f, 2f * settings.brakeDeceleration);
        float reactionDistance = forwardSpeed * settings.reactionTime;
        float sweepDistance = Mathf.Max(settings.minimumGap, stoppingDistance + reactionDistance + settings.minimumGap);
        return Sweep(forward, sweepDistance, out distance, out obstacle);
    }

    /// <summary>
    /// Sweeps the collider backwards over a caller-chosen distance (recovery reverse manoeuvres only;
    /// normal driving never looks behind). Same self/trigger filtering as the forward query.
    /// </summary>
    public bool TryDetectRearObstacle(float sweepDistance, out float distance, out Collider2D obstacle) {
        distance = float.PositiveInfinity;
        obstacle = null;
        LastQuerySaturated = false;
        if (rb == null || selfCollider == null || settings == null || sweepDistance <= 0f || float.IsNaN(sweepDistance)) return false;
        return Sweep(-(Vector2)transform.up, sweepDistance, out distance, out obstacle);
    }

    bool Sweep(Vector2 direction, float sweepDistance, out float distance, out Collider2D obstacle) {
        distance = float.PositiveInfinity;
        obstacle = null;

        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(detectionMask);
        int hitCount = selfCollider.Cast(direction, filter, resultsBuffer, sweepDistance, true);
        if (hitCount >= resultsBuffer.Length) LastQuerySaturated = true;

        float closestDistance = float.PositiveInfinity;
        Collider2D closestCollider = null;
        int consideredCount = Mathf.Min(hitCount, resultsBuffer.Length);
        for (int i = 0; i < consideredCount; i++) {
            var hit = resultsBuffer[i];
            if (hit.collider == null) continue;
            if (hit.collider == selfCollider) continue;
            if (hit.collider.attachedRigidbody == rb) continue; // another collider on the same body
            if (hit.collider.isTrigger) continue; // game-logic triggers are never physical obstacles
            if (hit.distance < closestDistance) {
                closestDistance = hit.distance;
                closestCollider = hit.collider;
            }
        }

        if (closestCollider == null) return false;
        distance = closestDistance;
        obstacle = closestCollider;
        return true;
    }
}
