using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit scene-local owner seam for police composition and navigation dependencies.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-9000)]
public sealed class PoliceSceneBinding : MonoBehaviour {
    [SerializeField] TrafficSessionHost host;
    [SerializeField] TrafficDamageWorld damageWorld;
    [SerializeField] PoliceDirector director;
    [SerializeField] PrefabPoliceVehicleBodyProvider bodyProvider;
    [SerializeField] PoliceVehiclePrefabCatalog prefabCatalog;
    [SerializeField] PoliceDirectorData directorData;
    [SerializeField] SceneTrafficBinding trafficBinding;
    [SerializeField] Camera gameplayCamera;
    [SerializeField] Vector2 mapOrigin;
    [SerializeField, Min(1)] int queryCapacity = 256;
    [SerializeField, Min(0f)] float clearanceMargin = 0.4f;
    [SerializeField, Min(0f)] float cameraMargin = 4f;
    [SerializeField, Min(0f)] float minimumApproachSeconds = 2f;
    [SerializeField] PoliceNavigationSettings navigationSettings = new PoliceNavigationSettings();
    [SerializeField] PolicePlacementSettings placementSettings = new PolicePlacementSettings();

    PolicePopulationRuntime policePopulationRuntime;
    bool spawnSubscribed;
    PhysicsSceneAreaClearanceQuery staticClearance;
    Rigidbody2D playerBody;
    PoliceFreeChaseSettings frozenFreeChase;
    PoliceUnitVariationSettings frozenVariation;
    PoliceNavigationMode frozenMode;

    /// <summary>Navigation mode explicitly selected by this scene binding.</summary>
    public PoliceNavigationMode NavigationMode => directorData != null ? directorData.navigationMode : PoliceNavigationMode.LegacyRoad;
    /// <summary>Explicit session host required by the police owner.</summary>
    public TrafficSessionHost Host => host;
    /// <summary>Explicit shared damage world required by the police owner.</summary>
    public TrafficDamageWorld DamageWorld => damageWorld;
    /// <summary>Explicit police director retained as the heat/contact authority.</summary>
    public PoliceDirector Director => director;
    /// <summary>Shared traffic binding used only as an explicit catalog and camera source.</summary>
    public SceneTrafficBinding TrafficBinding => trafficBinding;

    /// <summary>Copies the authored placement settings into this explicit police owner.</summary>
    /// <param name="source">Placement settings authored on the legacy scene binding.</param>
    /// <param name="reason">Failure description, or null when copied.</param>
    /// <returns>True when a valid detached copy was stored.</returns>
    public bool TrySetPlacementSettings(PolicePlacementSettings source, out string reason) {
        reason = null;
        if (source == null) return Fail("Police placement settings are missing.", out reason);
        PolicePlacementSettings copy = source.Clone();
        if (!copy.IsValid(out reason)) return false;
        placementSettings = copy;
        return true;
    }

    /// <summary>Copies shared scene services and authored camera/query settings into this owner.</summary>
    /// <param name="source">The scene traffic binding already validated by the session boundary.</param>
    /// <param name="reason">Failure description, or null when copied.</param>
    /// <returns>True when the explicit shared source is valid.</returns>
    public bool TrySetTrafficBinding(SceneTrafficBinding source, out string reason) {
        reason = null;
        if (source == null || source.gameObject.scene != gameObject.scene || source.GameplayCamera == null)
            return Fail("Police scene traffic source is missing or belongs to another scene.", out reason);
        trafficBinding = source;
        gameplayCamera = source.GameplayCamera;
        mapOrigin = source.MapOriginWorld;
        queryCapacity = Mathf.Max(1, source.QueryCapacity);
        clearanceMargin = Mathf.Max(0f, source.ClearanceMargin);
        cameraMargin = Mathf.Max(0f, source.CameraMargin);
        minimumApproachSeconds = Mathf.Max(0f, source.MinApproachSeconds);
        navigationSettings = source.PoliceNavigationSettings != null
            ? source.PoliceNavigationSettings.Clone() : new PoliceNavigationSettings();
        return true;
    }

    /// <summary>Creates the detached tactical coordinator owned by the current session binding.</summary>
    /// <param name="coordinator">Detached role and claim policy when valid.</param>
    /// <param name="reason">Failure description, or null when created.</param>
    /// <returns>True when the binding has a valid free-drive settings snapshot.</returns>
    public bool TryCreateTacticsCoordinator(out PoliceTacticsCoordinator coordinator, out string reason) {
        coordinator = null;
        reason = null;
        if (directorData == null || directorData.freeChaseSettings == null || !directorData.freeChaseSettings.IsValid(out reason)) return false;
        coordinator = new PoliceTacticsCoordinator(directorData.freeChaseSettings);
        return true;
    }

    /// <summary>Validates this binding against a same-scene shared session host.</summary>
    /// <param name="owner">The shared host that will activate the session.</param>
    /// <param name="reason">Failure description, or null when valid.</param>
    /// <returns>True only when all explicit dependencies and selected settings are valid.</returns>
    public bool ValidateForSession(TrafficSessionHost owner, out string reason) {
        reason = null;
        if (owner == null || host == null || owner != host || damageWorld == null || director == null ||
            bodyProvider == null || prefabCatalog == null || directorData == null)
            return Fail("Police scene binding has an incomplete explicit dependency set.", out reason);
        if (!SameScene(owner, damageWorld) || !SameScene(owner, director) || !SameScene(owner, bodyProvider) ||
            (trafficBinding != null && !SameScene(owner, trafficBinding)))
            return Fail("Police scene binding dependencies must belong to the same scene.", out reason);
        if (!directorData.TryValidate(out reason)) return false;
        if (directorData.freeChaseSettings == null || !directorData.freeChaseSettings.IsValid(out reason)) return false;
        if (directorData.unitVariationSettings == null || !directorData.unitVariationSettings.IsValid(out reason)) return false;
        if (placementSettings == null || !placementSettings.IsValid(out reason)) return false;
        if (navigationSettings == null || !navigationSettings.IsValid(out reason)) return false;
        if (trafficBinding != null && (trafficBinding.Director != director || trafficBinding.PoliceBodyProvider != bodyProvider ||
            trafficBinding.PolicePrefabCatalog != prefabCatalog ||
            (trafficBinding.PoliceDirectorProfile != null && trafficBinding.PoliceDirectorProfile != directorData)))
            return Fail("Police binding and shared catalog composition disagree.", out reason);
        if (!System.Enum.IsDefined(typeof(PoliceNavigationMode), directorData.navigationMode))
            return Fail("Police scene binding navigation mode is invalid.", out reason);
        return true;
    }

    /// <summary>Copies this binding's mutable settings into detached session snapshots.</summary>
    /// <param name="freeChase">Detached free chase settings.</param>
    /// <param name="unitVariation">Detached unit variation settings.</param>
    /// <param name="placement">Detached placement settings.</param>
    /// <param name="reason">Failure description, or null when snapshots were created.</param>
    /// <returns>True when validation and detached cloning succeeded.</returns>
    public bool TryCreateDetachedSettings(out PoliceFreeChaseSettings freeChase,
        out PoliceUnitVariationSettings unitVariation, out PolicePlacementSettings placement, out string reason) {
        freeChase = null;
        unitVariation = null;
        placement = null;
        if (!ValidateForSession(host, out reason)) return false;
        freeChase = directorData.freeChaseSettings.Clone();
        unitVariation = directorData.unitVariationSettings.Clone();
        placement = placementSettings.Clone();
        return true;
    }

    /// <summary>Assigns the explicit dependencies before session activation.</summary>
    /// <param name="owner">Same-scene shared host.</param>
    /// <param name="world">Same-scene shared damage world.</param>
    /// <param name="policeDirector">Same-scene police director.</param>
    /// <param name="provider">Same-scene body provider.</param>
    /// <param name="catalog">Police prefab catalog.</param>
    /// <param name="profile">Validated director profile.</param>
    /// <param name="reason">Failure description, or null when assigned.</param>
    /// <returns>True when the dependency set is accepted.</returns>
    public bool TryConfigure(TrafficSessionHost owner, TrafficDamageWorld world, PoliceDirector policeDirector,
        PrefabPoliceVehicleBodyProvider provider, PoliceVehiclePrefabCatalog catalog, PoliceDirectorData profile,
        out string reason) {
        reason = null;
        if (host != null || damageWorld != null || director != null || bodyProvider != null || prefabCatalog != null || directorData != null)
            return Fail("Police scene binding dependencies are already configured.", out reason);
        host = owner;
        damageWorld = world;
        director = policeDirector;
        bodyProvider = provider;
        prefabCatalog = catalog;
        directorData = profile;
        if (ValidateForSession(owner, out reason)) return true;
        host = null; damageWorld = null; director = null; bodyProvider = null; prefabCatalog = null; directorData = null;
        return false;
    }

    void OnEnable() {
        if (host == null) return;
        host.Activated += Activate;
        host.Ended += Stop;
        host.PlayerReady += TryActivatePopulation;
    }

    void Start() { if (host != null && host.IsActive) Activate(); }

    void Update() {
        if (host == null || !host.IsActive || Time.timeScale <= 0f) return;
        TryActivatePopulation(host.Player);
        if (policePopulationRuntime != null && damageWorld != null) policePopulationRuntime.Tick(damageWorld.SessionTime);
    }

    void FixedUpdate() {
        if (host == null || !host.IsActive || Time.timeScale <= 0f || damageWorld == null) return;
        TryActivatePopulation(host.Player);
        policePopulationRuntime?.PreparePhysics(damageWorld.SessionTime);
    }

    void OnDisable() {
        if (host != null) {
            host.Activated -= Activate;
            host.Ended -= Stop;
            host.PlayerReady -= TryActivatePopulation;
        }
        Stop();
    }

    void Activate() {
        if (policePopulationRuntime != null || host == null || !host.IsActive || trafficBinding == null) return;
        playerBody = host.Player != null ? host.Player.GetComponent<Rigidbody2D>() : null;
    }

    void TryActivatePopulation(GameObject player) {
        if (policePopulationRuntime != null || host == null || !host.IsActive || trafficBinding == null ||
            host.Services == null || director == null || director.Runtime == null || !director.Runtime.IsEnabled ||
            player == null || player.scene != gameObject.scene || bodyProvider == null || prefabCatalog == null ||
            host.Navigation == null || trafficBinding.PoliceCatalog == null) return;
        TrafficDamageWorld world = damageWorld;
        VehicleDamageReceiver receiver = player.GetComponent<VehicleDamageReceiver>();
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (world == null || !world.IsActive || receiver == null || !receiver.CanTakeDamage || body == null) return;
        playerBody = body;
        var config = new PolicePopulationRuntime.Config {
            sessionCoordinator = host.Coordinator,
            services = host.Services,
            damageWorld = world,
            directorRuntime = director.Runtime,
            bodyProvider = bodyProvider,
            policeCatalog = trafficBinding.PoliceCatalog,
            policePrefabCatalog = prefabCatalog,
            navigation = host.Navigation,
            mapOriginWorld = mapOrigin,
            playerReceiver = receiver,
            playerBody = body,
            currentCameraBounds = CameraWorldBounds,
            worldClearance = new PhysicsSceneAreaClearanceQuery(gameObject.scene.GetPhysicsScene2D(), Vector2.zero, false, queryCapacity),
            localStaticClearance = staticClearance,
            navigationSettings = navigationSettings,
            placementSettings = placementSettings,
            deterministicSeed = host.Coordinator.Context.Snapshot.seed,
            playerSurfaceRadius = PlayerSurfaceRadius(player),
            removePoliceContacts = director.RemovePoliceContacts,
            navigationMode = frozenMode,
            freeChaseSettings = frozenFreeChase,
            unitVariationSettings = frozenVariation,
            freeStaticSource = new PoliceFreeNavigationQuery.PhysicsClearanceSource(gameObject.scene.GetPhysicsScene2D(),
                mapOrigin, PoliceFreeNavigationQuery.SourceKind.Static, queryCapacity, Physics2D.AllLayers)
        };
        policePopulationRuntime = new PolicePopulationRuntime(config);
        director.SpawnRequested += HandlePoliceSpawnRequested;
        spawnSubscribed = true;
    }

    void HandlePoliceSpawnRequested(PoliceDirectorRuntime.SpawnRequest request) {
        if (policePopulationRuntime == null) { director?.ReportSpawnRejected(request); return; }
        policePopulationRuntime.TryMaterialize(request);
    }

    void Stop() {
        if (spawnSubscribed && director != null) director.SpawnRequested -= HandlePoliceSpawnRequested;
        spawnSubscribed = false;
        if (policePopulationRuntime != null) policePopulationRuntime.Dispose();
        else host?.Services?.MarkRoleOwnerCleaned(VehicleRole.Police);
        policePopulationRuntime = null;
    }

    /// <summary>Configures the selected owner once shared profile and map validation has succeeded.</summary>
    /// <param name="reason">A dependency or provider failure, or null when ready for activation.</param>
    /// <returns>True when runtime dependencies are ready; validation alone never resets providers.</returns>
    public bool ConfigureForSession(out string reason) {
        reason = null;
        if (!ValidateForSession(host, out reason)) return false;
        if (trafficBinding == null || gameplayCamera == null || trafficBinding.PoliceCatalog == null)
            return Fail("Police runtime requires the validated shared catalog and gameplay camera.", out reason);
        frozenMode = directorData.navigationMode;
        frozenFreeChase = directorData.freeChaseSettings.Clone();
        frozenVariation = directorData.unitVariationSettings.Clone();
        navigationSettings = navigationSettings.Clone();
        placementSettings = placementSettings.Clone();
        staticClearance = new PhysicsSceneAreaClearanceQuery(host.gameObject.scene.GetPhysicsScene2D(), mapOrigin, true, queryCapacity);
        if (!bodyProvider.Configure(prefabCatalog, bodyProvider.maxInstancesPerVisual, out reason)) return false;
        return director.TryConfigure(host, directorData, out reason);
    }

    Rect CameraWorldBounds() {
        if (gameplayCamera == null) return new Rect();
        float depth = Mathf.Abs(gameplayCamera.transform.position.z);
        Vector3 bottomLeft = gameplayCamera.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
        Vector3 topLeft = gameplayCamera.ViewportToWorldPoint(new Vector3(0f, 1f, depth));
        Vector3 topRight = gameplayCamera.ViewportToWorldPoint(new Vector3(1f, 1f, depth));
        Vector3 bottomRight = gameplayCamera.ViewportToWorldPoint(new Vector3(1f, 0f, depth));
        return Rect.MinMaxRect(Mathf.Min(Mathf.Min(bottomLeft.x, topLeft.x), Mathf.Min(topRight.x, bottomRight.x)),
            Mathf.Min(Mathf.Min(bottomLeft.y, topLeft.y), Mathf.Min(topRight.y, bottomRight.y)),
            Mathf.Max(Mathf.Max(bottomLeft.x, topLeft.x), Mathf.Max(topRight.x, bottomRight.x)),
            Mathf.Max(Mathf.Max(bottomLeft.y, topLeft.y), Mathf.Max(topRight.y, bottomRight.y)));
    }

    float PlayerSurfaceRadius(GameObject player) {
        Collider2D[] colliders = player.GetComponentsInChildren<Collider2D>(true);
        if (colliders == null || colliders.Length == 0) return 0f;
        Bounds bounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++) bounds.Encapsulate(colliders[i].bounds);
        Vector2 center = playerBody != null ? playerBody.position : (Vector2)player.transform.position;
        return bounds.extents.magnitude + Vector2.Distance(bounds.center, center);
    }

    static bool SameScene(TrafficSessionHost owner, Component dependency) =>
        dependency != null && dependency.gameObject.scene == owner.gameObject.scene;

    static bool Fail(string message, out string reason) { reason = message; return false; }
}
