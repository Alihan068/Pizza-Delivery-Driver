using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Root bundle of one civilian NPC prefab: the physical body plus the motor, route follower,
/// optional obstacle sensor and optional damage receiver the <see cref="TrafficManager"/> composes
/// per life. References are resolved once in Awake when not authored. Placement happens only while
/// the object is inactive (spawn), so a visible vehicle is never teleported through here.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(NpcVehicleMotor))]
[RequireComponent(typeof(CivilianRouteFollower))]
public sealed class CivilianVehicleBody : MonoBehaviour {
    [SerializeField] Rigidbody2D body;
    [SerializeField] Collider2D mainCollider;
    [SerializeField] NpcVehicleMotor motor;
    [SerializeField] CivilianRouteFollower follower;
    [SerializeField] VehicleObstacleSensor sensor;
    [SerializeField] VehicleDamageReceiver receiver;

    /// <summary>Catalog id of the visual/prefab this body came from; set by the provider that instantiated it.</summary>
    public string VisualCatalogId { get; set; }

    public Rigidbody2D Body => body;
    public NpcVehicleMotor Motor => motor;
    public CivilianRouteFollower Follower => follower;
    /// <summary>Optional; null when the prefab has no sensor (then the follower never brakes for obstacles).</summary>
    public VehicleObstacleSensor Sensor => sensor;
    /// <summary>Optional; null when the prefab takes no damage.</summary>
    public VehicleDamageReceiver Receiver => receiver;

    /// <summary>Collider footprint (width, length) in world units, including transform scale; falls back to 2×4 without a collider.</summary>
    public Vector2 Footprint {
        get {
            if (mainCollider is BoxCollider2D box) {
                Vector3 scale = transform.lossyScale;
                return new Vector2(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.y * scale.y));
            }
            if (mainCollider != null) {
                var bounds = mainCollider.bounds;
                return new Vector2(bounds.size.x, bounds.size.y);
            }
            return new Vector2(2f, 4f);
        }
    }

    void Awake() {
        if (body == null) body = GetComponent<Rigidbody2D>();
        if (mainCollider == null) mainCollider = GetComponent<Collider2D>();
        if (motor == null) motor = GetComponent<NpcVehicleMotor>();
        if (follower == null) follower = GetComponent<CivilianRouteFollower>();
        if (sensor == null) sensor = GetComponent<VehicleObstacleSensor>();
        if (receiver == null) receiver = GetComponent<VehicleDamageReceiver>();
    }

    /// <summary>Places an inactive body at a spawn pose with zero velocity. Refused on an active object — that would be a teleport.</summary>
    /// <returns>True when placed.</returns>
    public bool PlaceForSpawn(Vector2 worldPosition, float headingDegrees) {
        if (gameObject.activeInHierarchy) return false;
        var rotation = Quaternion.Euler(0f, 0f, headingDegrees);
        transform.SetPositionAndRotation(worldPosition, rotation);
        if (body != null) {
            body.position = worldPosition;
            body.rotation = headingDegrees;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
        return true;
    }

#if UNITY_EDITOR
    /// <summary>Initializes cached production collaborators only for a non-playing body in a valid non-default 2D physics scene, directly using the normal Awake path.</summary>
    public bool InitializeEditorPreview(PhysicsScene2D previewPhysicsScene) {
        if (Application.isPlaying || !previewPhysicsScene.IsValid() || previewPhysicsScene == Physics2D.defaultPhysicsScene || gameObject.scene.GetPhysicsScene2D() != previewPhysicsScene) return false;
        Awake();
        if (motor == null || follower == null || !motor.InitializeEditorPreview(previewPhysicsScene) || !follower.InitializeEditorPreview(previewPhysicsScene)) return false;
        return sensor == null || sensor.InitializeEditorPreview(previewPhysicsScene);
    }
#endif
}
