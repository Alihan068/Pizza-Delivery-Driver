using UnityEngine;

/// <summary>Authored timing gates consumed by <see cref="TrafficRespawnScheduler"/>. Every value is Inspector data, never hardcoded.</summary>
[CreateAssetMenu(fileName = "NewRespawnTimingRules", menuName = "PizzaGame/Traffic/Respawn Timing Rules")]
public class RespawnTimingRules : ScriptableObject {
    [Tooltip("A deficit is never attempted before this many seconds have passed, regardless of player distance or deadline.")]
    public float minimumDelaySeconds = 3f;

    [Tooltip("Player distance from the incident position, at or beyond which a deficit becomes attemptable even before the deadline.")]
    public float minimumPlayerDistance = 15f;

    [Tooltip("Once this many seconds have passed since the incident, the deficit becomes attemptable regardless of player distance. Never itself permission to spawn inside camera view — VehicleSpawnPolicy still applies.")]
    public float replacementDeadlineSeconds = 12f;
}
