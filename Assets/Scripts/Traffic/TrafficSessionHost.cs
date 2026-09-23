using System;
using System.Text;
using UnityEngine;

/// <summary>Explicit scene-local opt-in bridge. Only its authored map can enable validated traffic.</summary>
[DisallowMultipleComponent]
public sealed class TrafficSessionHost : MonoBehaviour {
    [SerializeField] MapData map;
    [SerializeField] TrafficValidationLimits validationLimits = new TrafficValidationLimits();
    [SerializeField] TrafficDamageSettings damageSettings;
    [SerializeField] SceneTrafficBinding trafficBinding;
    bool closed;

    /// <summary>Authoritative session lifecycle and shared identity registry, available after preparation.</summary>
    public TrafficSessionCoordinator Coordinator { get; private set; }
    /// <summary>Explicit map binding; never falls back to the manager's selected map.</summary>
    public MapData Map => map;
    /// <summary>Scene composition binding that validated this host, or null for legacy/bindingless fixtures.</summary>
    public SceneTrafficBinding Binding => trafficBinding;
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
    /// <summary>Shared civilian/police services created after validation and before readiness notifications.</summary>
    public TrafficSessionServices Services { get; private set; }
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
        PrepareSession(manager, null);
    }

    /// <summary>
    /// Captures the same session draft as the authored path, then consumes an explicitly supplied
    /// staged navigation document. Imported navigation must belong to a non-legacy scene/map and
    /// is detached and validated again before the existing binding and shared-service activation.
    /// The missing trusted manifest marks integrity invalid but does not disable local gameplay.
    /// </summary>
    public void PrepareSession(GameManager manager, MapNavigationDocument importedNavigation) {
        if (closed || Coordinator != null) return;
        string testDifficulty = trafficBinding != null ? trafficBinding.EditorTestDifficultyId : null;
        var context = manager != null ? manager.PrepareSession(gameObject.scene, map, true, testDifficulty) :
            SessionSceneRules.Create(null, map, true, gameObject.scene.name, Application.isEditor ? testDifficulty : null);
        if (manager == null) context.Integrity.MarkInvalid("Direct scene test without career setup.");
        bool imported = importedNavigation != null;
        if (imported) {
            context.Integrity.MarkInvalid("Imported navigation has no trusted manifest.");
            Navigation = ValidateImportedNavigation(context, importedNavigation, out string importedIssue);
            if (Navigation == null && !string.IsNullOrEmpty(importedIssue)) context.Integrity.MarkInvalid(importedIssue);
        } else {
            Navigation = SessionSceneRules.ValidateNavigation(map, gameObject.scene.name, validationLimits, context.Integrity);
        }
        if (Navigation != null && trafficBinding != null && !trafficBinding.ValidateForSession(this, context, Navigation, out string issue)) {
            context.Integrity.MarkInvalid(issue);
            Debug.LogWarning("Traffic inactive: " + issue, this);
            Navigation = null;
        }
        Coordinator = new TrafficSessionCoordinator(context);
        Coordinator.Initialized += HandleActivated;
        Coordinator.Ended += HandleEnded;
        if (Navigation != null && trafficBinding != null) {
            Services = trafficBinding.CreateSessionServices(Navigation, Coordinator.IdentityRegistry);
            if (Services == null) {
                context.Integrity.MarkInvalid("Traffic shared service composition failed.");
                Navigation = null;
            }
        }
        SessionContentEvidence evidence = trafficBinding != null
            ? trafficBinding.CaptureContentEvidence(context, manager, Navigation, Navigation != null, Services != null, validationLimits)
            : null;
        SessionSceneRules.Freeze(context, Navigation != null, Navigation?.documentId, evidence);
        if (damageSettings != null) {
            var world = GetComponent<TrafficDamageWorld>();
            if (world == null) world = gameObject.AddComponent<TrafficDamageWorld>();
            world.Configure(Coordinator, damageSettings);
        }
        if (Navigation != null) Coordinator.NotifyMapReady();
        if (Player != null) PublishPlayer();
    }

    MapNavigationDocument ValidateImportedNavigation(TrafficSessionContext context, MapNavigationDocument source, out string reason) {
        reason = null;
        if (map == null || map.trafficMapData == null || string.IsNullOrEmpty(map.mapId) || map.sceneName != gameObject.scene.name || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) {
            reason = "Imported navigation requires a valid non-legacy scene/map identity.";
            return null;
        }
        if (context == null || context.Draft == null || context.Draft.mapId != map.mapId || source.mapId != map.mapId || string.IsNullOrEmpty(source.documentId)) {
            reason = "Imported navigation identity does not match the session draft.";
            return null;
        }
        if (!NavigationDocumentCodec.TryEncode(source, validationLimits, out string json, out _, out reason)) return null;
        if (!NavigationDocumentCodec.TryDecode(Encoding.UTF8.GetBytes(json), validationLimits, out MapNavigationDocument detached, out reason)) return null;
        return detached;
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
    void HandleEnded() {
        Ended?.Invoke();
        if (Services == null) return;
        Services.Close();
    }
    void OnDisable() { EndSession(); }
    void OnDestroy() {
        EndSession();
        if (Coordinator == null) return;
        Coordinator.Initialized -= HandleActivated;
        Coordinator.Ended -= HandleEnded;
    }
}
