using System;
using UnityEngine;

/// <summary>Explicit scene-local opt-in bridge. Only its authored map can enable validated traffic.</summary>
[DisallowMultipleComponent]
public sealed class TrafficSessionHost : MonoBehaviour {
    [SerializeField] MapData map;
    [SerializeField] TrafficValidationLimits validationLimits = new TrafficValidationLimits();
    [SerializeField] TrafficDamageSettings damageSettings;
    bool closed;

    /// <summary>Authoritative session lifecycle and shared identity registry, available after preparation.</summary>
    public TrafficSessionCoordinator Coordinator { get; private set; }
    /// <summary>Explicit map binding; never falls back to the manager's selected map.</summary>
    public MapData Map => map;
    /// <summary>Detached validated navigation, or null when unsupported or rejected.</summary>
    public MapNavigationDocument Navigation { get; private set; }
    /// <summary>Authored validation limits used before traffic activation.</summary>
    public TrafficValidationLimits ValidationLimits => validationLimits;
    /// <summary>Authored damage settings; null leaves the optional damage world disabled.</summary>
    public TrafficDamageSettings DamageSettings => damageSettings;
    /// <summary>Registered scene player, available for late-binding adapters.</summary>
    public GameObject Player { get; private set; }
    /// <summary>Registry to inject into all NPC pools in this session.</summary>
    public VehicleIdentityRegistry IdentityRegistry => Coordinator?.IdentityRegistry;
    /// <summary>True only after validation and player registration, until session end.</summary>
    public bool IsActive => Coordinator != null && Coordinator.IsActive;
    /// <summary>True for an explicitly bound matching map that intentionally has no traffic support.</summary>
    public bool IsLegacyMap => map != null && !string.IsNullOrEmpty(map.mapId) &&
        map.sceneName == gameObject.scene.name && map.trafficMapData == null;
    /// <summary>Raised after both readiness gates have opened; late subscribers should also inspect IsActive.</summary>
    public event Action Activated;
    /// <summary>Raised once after the coordinator ends, before economy settlement.</summary>
    public event Action Ended;
    /// <summary>Raised once with the registered player after world configuration and identity assignment.</summary>
    public event Action<GameObject> PlayerReady;

    void Start() { PrepareSession(GameManager.Instance); }

    /// <summary>Captures rules and validates the authored map once, independently of player notification order.</summary>
    public void PrepareSession(GameManager manager) {
        if (closed || Coordinator != null) return;
        var context = manager != null ? manager.PrepareSession(gameObject.scene, map, true) :
            SessionSceneRules.Create(null, map, true, gameObject.scene.name);
        if (manager == null) context.Integrity.MarkInvalid("Direct scene test without career setup.");
        Navigation = SessionSceneRules.ValidateNavigation(map, gameObject.scene.name, validationLimits, context.Integrity);
        SessionSceneRules.Freeze(context, Navigation != null, Navigation?.documentId);
        Coordinator = new TrafficSessionCoordinator(context);
        Coordinator.Initialized += HandleActivated;
        Coordinator.Ended += HandleEnded;
        if (damageSettings != null) {
            var world = GetComponent<TrafficDamageWorld>();
            if (world == null) world = gameObject.AddComponent<TrafficDamageWorld>();
            world.Configure(Coordinator, damageSettings);
        }
        if (Navigation != null) Coordinator.NotifyMapReady();
        if (Player != null) PublishPlayer();
    }

    /// <summary>Registers a same-scene player once; notification may precede map preparation.</summary>
    public void RegisterPlayer(GameObject player) {
        if (closed || Player != null || player == null || player.scene != gameObject.scene) return;
        Player = player;
        if (Coordinator != null) PublishPlayer();
    }

    void PublishPlayer() {
        Coordinator.NotifyPlayerReady();
        PlayerReady?.Invoke(Player);
    }

    /// <summary>Closes active and pending work once. A disabled or ended host cannot reopen its session.</summary>
    public void EndSession() {
        if (closed) return;
        closed = true;
        Coordinator?.NotifySessionEnded();
    }

    void HandleActivated() { Activated?.Invoke(); }
    void HandleEnded() { Ended?.Invoke(); }
    void OnDisable() { EndSession(); }
    void OnDestroy() {
        EndSession();
        if (Coordinator == null) return;
        Coordinator.Initialized -= HandleActivated;
        Coordinator.Ended -= HandleEnded;
    }
}
