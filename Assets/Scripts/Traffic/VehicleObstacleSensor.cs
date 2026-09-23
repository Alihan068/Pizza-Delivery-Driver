using UnityEngine;
using UnityEngine.SceneManagement;

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
    /// <summary>Outcome of an explicit bounded collider sweep.</summary>
    public enum SweepStatus {
        /// <summary>No physical collider was found inside the requested sweep.</summary>
        Clear,
        /// <summary>A physical collider was found and returned.</summary>
        Blocked,
        /// <summary>The sensor, direction, or range input was invalid.</summary>
        Invalid,
        /// <summary>The result buffer was full and the caller must fail safe.</summary>
        Saturated
    }
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
        return Sweep(forward, sweepDistance, null, out distance, out obstacle);
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
        return Sweep(-(Vector2)transform.up, sweepDistance, null, out distance, out obstacle);
    }

    /// <summary>Performs a caller-bounded sweep in an explicit normalized direction.</summary>
    /// <param name="direction">Finite nonzero sweep direction; normalization is performed internally.</param>
    /// <param name="distance">Finite positive caller-authorized sweep distance.</param>
    /// <param name="gap">Nearest physical clearance, or positive infinity when clear/invalid.</param>
    /// <param name="obstacle">Nearest physical collider when blocked; otherwise null.</param>
    /// <returns>A status that distinguishes invalid input, clear space, blocking geometry, and saturation.</returns>
    public SweepStatus QuerySweep(Vector2 direction, float distance, out float gap, out Collider2D obstacle) {
        return QuerySweep(direction, distance, null, out gap, out obstacle);
    }

    /// <summary>Performs a bounded collider sweep while excluding one validated rigidbody from blocking selection.</summary>
    /// <param name="direction">Finite nonzero sweep direction; normalization is performed internally.</param>
    /// <param name="distance">Finite positive caller-authorized sweep distance.</param>
    /// <param name="excludedTarget">Optional active simulated rigidbody in this physics scene to skip; null preserves ordinary blocking.</param>
    /// <param name="gap">Nearest nonexcluded physical clearance, or positive infinity when clear/invalid.</param>
    /// <param name="obstacle">Nearest nonexcluded physical collider when blocked; otherwise null.</param>
    /// <returns>Invalid for self/inactive/nonsimulated/foreign-scene exclusions. Raw full buffers stay Saturated even if every hit belongs to the excluded body.</returns>
    public SweepStatus QuerySweep(Vector2 direction, float distance, Rigidbody2D excludedTarget,
        out float gap, out Collider2D obstacle) {
        gap = float.PositiveInfinity;
        obstacle = null;
        LastQuerySaturated = false;
        if (!isActiveAndEnabled || rb == null || selfCollider == null || settings == null || !rb.simulated || !selfCollider.enabled ||
            !rb.gameObject.activeInHierarchy || !Finite(direction) || !FinitePositive(direction.sqrMagnitude) || !FinitePositive(distance)) {
            return SweepStatus.Invalid;
        }
        if (excludedTarget != null &&
            (excludedTarget == rb || !excludedTarget.simulated || !excludedTarget.gameObject.activeInHierarchy ||
             excludedTarget.gameObject.scene.GetPhysicsScene2D() != gameObject.scene.GetPhysicsScene2D()))
            return SweepStatus.Invalid;
        Vector2 normalizedDirection = direction.normalized;
        if (!FinitePositive(normalizedDirection.sqrMagnitude)) return SweepStatus.Invalid;
        bool blocked = Sweep(normalizedDirection, distance, excludedTarget, out gap, out obstacle);
        if (LastQuerySaturated) return SweepStatus.Saturated;
        return blocked ? SweepStatus.Blocked : SweepStatus.Clear;
    }

    bool Sweep(Vector2 direction, float sweepDistance, Rigidbody2D excludedTarget, out float distance, out Collider2D obstacle) {
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
            if (excludedTarget != null && hit.collider.attachedRigidbody == excludedTarget) continue;
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

    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;

#if UNITY_EDITOR
    /// <summary>Initializes cached sensor references through private Awake only for a non-playing object in the supplied valid non-default 2D physics scene.</summary>
    public bool InitializeEditorPreview(PhysicsScene2D previewPhysicsScene) {
        if (Application.isPlaying || !previewPhysicsScene.IsValid() || previewPhysicsScene == Physics2D.defaultPhysicsScene || gameObject.scene.GetPhysicsScene2D() != previewPhysicsScene) return false;
        Awake();
        return rb != null && selfCollider != null;
    }
#endif
}
