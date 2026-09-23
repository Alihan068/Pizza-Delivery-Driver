using UnityEngine;

/// <summary>Passive authored component bundle for a police vehicle prefab.</summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(NpcVehicleMotor))]
[RequireComponent(typeof(VehicleObstacleSensor))]
[RequireComponent(typeof(VehicleDamageReceiver))]
public sealed class PoliceVehicleBody : MonoBehaviour {
    [SerializeField] Rigidbody2D body;
    [SerializeField] BoxCollider2D mainCollider;
    [SerializeField] NpcVehicleMotor motor;
    [SerializeField] VehicleObstacleSensor sensor;
    [SerializeField] VehicleDamageReceiver damageReceiver;

    /// <summary>Physics body resolved from this root.</summary>
    public Rigidbody2D Body => body != null ? body : GetComponent<Rigidbody2D>();
    /// <summary>Required physical envelope collider.</summary>
    public BoxCollider2D MainCollider => mainCollider != null ? mainCollider : GetComponent<BoxCollider2D>();
    /// <summary>Shared NPC motor reference.</summary>
    public NpcVehicleMotor Motor => motor != null ? motor : GetComponent<NpcVehicleMotor>();
    /// <summary>Shared obstacle sensor reference.</summary>
    public VehicleObstacleSensor Sensor => sensor != null ? sensor : GetComponent<VehicleObstacleSensor>();
    /// <summary>Shared damage receiver reference.</summary>
    public VehicleDamageReceiver DamageReceiver => damageReceiver != null ? damageReceiver : GetComponent<VehicleDamageReceiver>();

    /// <summary>World-space width and length of the authored police envelope.</summary>
    public Vector2 WorldFootprint {
        get {
            var collider = MainCollider;
            return collider == null ? Vector2.zero : new Vector2(Mathf.Abs(collider.size.x * transform.lossyScale.x), Mathf.Abs(collider.size.y * transform.lossyScale.y));
        }
    }

    void Awake() {
        if (body == null) body = GetComponent<Rigidbody2D>();
        if (mainCollider == null) mainCollider = GetComponent<BoxCollider2D>();
        if (motor == null) motor = GetComponent<NpcVehicleMotor>();
        if (sensor == null) sensor = GetComponent<VehicleObstacleSensor>();
        if (damageReceiver == null) damageReceiver = GetComponent<VehicleDamageReceiver>();
    }
}
