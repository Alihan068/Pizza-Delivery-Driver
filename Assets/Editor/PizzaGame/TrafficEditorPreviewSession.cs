#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only owner of one detached, bounded civilian traffic preview. The session snapshots the
/// supplied navigation/profile/rule assets, composes the normal traffic manager and body provider
/// in an unsaved scene with its own 2D physics world, and owns the camera, render target and editor callbacks until it
/// is stopped. It is refused while the player is running and never writes an authoring draft, an
/// asset, GameManager state, save data, or the Unity Undo history.
/// </summary>
public sealed class TrafficEditorPreviewSession : IDisposable {
    /// <summary>Explicit visual binding for a profile visual catalog id; no fallback lookup is used.</summary>
    [Serializable]
    public sealed class ProfilePrefabBinding {
        /// <summary>Stable profile visual id resolved by this explicit preview-only prefab table.</summary>
        public string visualCatalogId;
        /// <summary>Production civilian body prefab used for this visual id in the isolated scene.</summary>
        public CivilianVehicleBody prefab;
    }

    /// <summary>
    /// Immutable-at-start editor inputs. The caller retains ownership: Start validates and clones
    /// all mutable traffic data before composing the preview, and never normalizes this instance.
    /// </summary>
    [Serializable]
    public sealed class Configuration {
        /// <summary>Detached authoring DTO to validate and snapshot before preview composition.</summary>
        public MapNavigationDocument draft;
        /// <summary>Approved source profiles, each cloned so preview cannot write the source asset.</summary>
        public NpcVehicleProfile[] profiles;
        /// <summary>Explicit visual-id-to-production-body bindings used by the normal prefab provider.</summary>
        public ProfilePrefabBinding[] prefabCatalog;
        /// <summary>Source population budget cloned for the preview-only service bundle.</summary>
        public PopulationBudgetData budget;
        /// <summary>Source respawn rules cloned for the preview-only traffic manager.</summary>
        public RespawnTimingRules respawnRules;
        /// <summary>Bounded structural validation limits applied to the detached draft.</summary>
        public TrafficValidationLimits limits;
        /// <summary>Existing civilian pool id to retain while every other route target is disabled in the clone.</summary>
        public string selectedPoolId;
        /// <summary>World-space origin used once when translating detached map-local coordinates.</summary>
        public Vector2 mapOriginWorld;
        /// <summary>Deterministic random seed passed to the production traffic manager.</summary>
        public int seed;
        /// <summary>Safety duration in seconds; Start rejects values longer than the approved 60-second bound.</summary>
        public float durationSeconds = 60f;
        /// <summary>Fixed simulation delta passed directly to follower, motor and isolated physics.</summary>
        public float fixedStep = 0.02f;
        /// <summary>Maximum fixed steps consumed in one editor callback or manual tick request.</summary>
        public int maxStepsPerEditorUpdate = 4;
        /// <summary>Capacity of the isolated non-allocating physics clearance query.</summary>
        public int physicsQueryCapacity = 64;
        /// <summary>Owned preview render target width in pixels.</summary>
        public int renderWidth = 640;
        /// <summary>Owned preview render target height in pixels.</summary>
        public int renderHeight = 360;
        /// <summary>Extra map-space margin around local bounds in the preview camera framing.</summary>
        public float cameraPadding = 2f;
    }

    sealed class Snapshot {
        internal MapNavigationDocument draft;
        internal NpcVehicleProfile[] profiles;
        internal ProfilePrefabBinding[] prefabCatalog;
        internal PopulationBudgetData budget;
        internal RespawnTimingRules respawnRules;
        internal TrafficValidationLimits limits;
        internal string selectedPoolId;
        internal Vector2 mapOriginWorld;
        internal int seed;
        internal float durationSeconds;
        internal float fixedStep;
        internal int maxStepsPerEditorUpdate;
        internal int physicsQueryCapacity;
        internal int renderWidth;
        internal int renderHeight;
        internal float cameraPadding;

        internal void Dispose() {
            if (profiles != null) for (int i = 0; i < profiles.Length; i++) DestroySnapshotObject(profiles[i]);
            DestroySnapshotObject(budget);
            DestroySnapshotObject(respawnRules);
            profiles = null;
            budget = null;
            respawnRules = null;
        }

        static void DestroySnapshotObject(UnityEngine.Object value) {
            if (value != null) UnityEngine.Object.DestroyImmediate(value);
        }
    }

    sealed class PreviewBodyProvider : ICivilianVehicleBodyProvider {
        readonly PrefabCivilianVehicleBodyProvider backend;
        readonly PhysicsScene2D physicsScene;
        readonly List<CivilianVehicleBody> live = new List<CivilianVehicleBody>();

        internal IReadOnlyList<CivilianVehicleBody> Live => live;

        internal PreviewBodyProvider(PrefabCivilianVehicleBodyProvider provider, PhysicsScene2D scene) {
            backend = provider;
            physicsScene = scene;
        }

        public CivilianVehicleBody Acquire(NpcVehicleProfile profile) {
            CivilianVehicleBody body = backend.Acquire(profile);
            if (body == null) return null;
            if (!body.InitializeEditorPreview(physicsScene)) {
                backend.Release(body);
                return null;
            }
            live.Add(body);
            return body;
        }

        public void Release(CivilianVehicleBody body) {
            if (body == null) return;
            live.Remove(body);
            backend.Release(body);
        }

        internal bool TickBodies(float deltaTime) {
            int countAtStart = live.Count;
            for (int i = 0; i < countAtStart; i++) {
                CivilianVehicleBody body = live[i];
                if (body == null || !body.gameObject.activeInHierarchy) continue;
                if (!body.Follower.TickEditorPreview(deltaTime, physicsScene) ||
                    !body.Motor.TickEditorPreview(deltaTime, physicsScene)) return false;
            }
            return true;
        }
    }

    Snapshot snapshot;
    PreviewBodyProvider bodies;
    TrafficManager manager;
    TrafficSessionServices services;
    Scene previewScene;
    PhysicsScene2D previewPhysicsScene;
    Camera previewCamera;
    RenderTexture previewTexture;
    double lastEditorTime;
    float accumulator;
    float elapsed;
    bool subscribed;
    bool servicesClosed;

    /// <summary>True only while the production manager owns an active preview session.</summary>
    public bool IsRunning => manager != null && manager.IsRunning;

    /// <summary>Last start/tick rejection. Cleared only by a new Start attempt.</summary>
    public string FailureReason { get; private set; }

    /// <summary>Elapsed simulated seconds; it never advances beyond the validated duration.</summary>
    public float ElapsedSeconds => elapsed;

    /// <summary>True while this session still owns its disposable preview scene.</summary>
    public bool IsPreviewSceneOpen => previewScene.IsValid();

    /// <summary>Bounded camera result rendered exclusively from the isolated preview scene.</summary>
    public RenderTexture PreviewTexture => previewTexture;

    /// <summary>True after Stop has closed the shared traffic service bundle for the most recent run.</summary>
    public bool WereServicesClosed => servicesClosed;

    /// <summary>Live bodies acquired through the normal prefab provider for diagnostics and tests.</summary>
    public IReadOnlyList<CivilianVehicleBody> LiveBodies => bodies == null ? null : bodies.Live;

    /// <summary>
    /// Validates and starts an isolated preview. All failures, including construction exceptions,
    /// close created services/scenes before returning false; input objects are never changed.
    /// </summary>
    public bool Start(Configuration input, out string issue) {
        issue = null;
        Stop();
        FailureReason = null;
        servicesClosed = false;
        elapsed = 0f;
        accumulator = 0f;
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return Fail("Preview is unavailable while entering or running Play Mode.", out issue);
        if (!ValidateInput(input, out issue)) return Fail(issue, out issue);

        try {
            snapshot = CreateSnapshot(input);
            List<TrafficValidationIssue> structuralIssues = TrafficMapValidator.Validate(snapshot.draft, snapshot.limits);
            if (structuralIssues.Count > 0) return Fail("Draft rejected: " + structuralIssues[0].message, out issue);

            previewScene = EditorSceneManager.NewPreviewScene();
            previewPhysicsScene = previewScene.GetPhysicsScene2D();
            if (!previewPhysicsScene.IsValid() || previewPhysicsScene == Physics2D.defaultPhysicsScene)
                return Fail("Preview scene did not provide isolated 2D physics.", out issue);

            CreateCamera();
            var providerObject = CreatePreviewObject("Traffic Editor Preview Bodies");
            var backend = providerObject.AddComponent<PrefabCivilianVehicleBodyProvider>();
            backend.maxInstancesPerVisual = snapshot.budget.maxCivilianMoving;
            for (int i = 0; i < snapshot.prefabCatalog.Length; i++)
                backend.Register(snapshot.prefabCatalog[i].visualCatalogId, snapshot.prefabCatalog[i].prefab);
            bodies = new PreviewBodyProvider(backend, previewPhysicsScene);

            var catalog = new BuiltInTrafficProfileCatalog(snapshot.profiles);
            var identity = new VehicleIdentityRegistry();
            var graph = new RoadGraphRuntime(snapshot.draft);
            var pool = new NpcVehiclePool(snapshot.budget, catalog, identity);
            var population = new VehiclePopulationService(snapshot.budget);
            services = new TrafficSessionServices(graph, catalog, pool, population, snapshot.budget, identity);
            services.MarkRoleOwnerCleaned(VehicleRole.Police);

            var managerObject = CreatePreviewObject("Traffic Editor Preview Manager");
            manager = managerObject.AddComponent<TrafficManager>();
            var clearance = new PhysicsSceneAreaClearanceQuery(previewPhysicsScene, Vector2.zero, true, snapshot.physicsQueryCapacity);
            var config = new TrafficManager.Config {
                navigation = snapshot.draft,
                catalog = catalog,
                bodies = bodies,
                budget = snapshot.budget,
                services = services,
                respawnRules = snapshot.respawnRules,
                identityRegistry = identity,
                clearance = clearance,
                rejoinClearance = clearance,
                mapOriginWorld = snapshot.mapOriginWorld,
                seed = snapshot.seed,
                cameraWorldBounds = null,
                playerWorldPosition = null,
                playerWorldVelocity = null
            };
            if (!manager.Initialize(config)) return Fail("TrafficManager rejected the isolated preview configuration.", out issue);
            if (manager.Diagnostics.Count > 0) return Fail("Preview spawn rejected: " + manager.Diagnostics[0], out issue);

            elapsed = 0f;
            accumulator = 0f;
            lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            subscribed = true;
            RenderPreview();
            return true;
        }
        catch (Exception exception) {
            return Fail("Preview construction failed: " + exception.Message, out issue);
        }
    }

    /// <summary>Stops callbacks, ends production traffic, closes shared services, and releases preview-only render resources.</summary>
    public void Stop() {
        if (subscribed) {
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            subscribed = false;
        }
        try {
            if (manager != null) manager.EndSession();
        }
        finally {
            try {
                CloseServices();
            }
            finally {
                manager = null;
                bodies = null;
                services = null;
                try {
                    DisposeCameraAndTexture();
                }
                finally {
                    try {
                        if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
                    }
                    finally {
                        previewScene = default;
                        previewPhysicsScene = default;
                        snapshot?.Dispose();
                        snapshot = null;
                        accumulator = 0f;
                    }
                }
            }
        }
    }

    /// <summary>Equivalent to Stop for IDisposable callers.</summary>
    public void Dispose() => Stop();

    /// <summary>Advances the same isolated production path for a clamped editor-test delta and honors the duration bound.</summary>
    public bool TickEditorPreview(float deltaTime) {
        if (!IsRunning || !FinitePositive(deltaTime)) return false;
        float remaining = snapshot.durationSeconds - elapsed;
        if (remaining <= 0f) { Stop(); return false; }
        float available = Mathf.Min(deltaTime, remaining);
        int steps = 0;
        while (available > 0f && steps < snapshot.maxStepsPerEditorUpdate && IsRunning) {
            float step = Mathf.Min(snapshot.fixedStep, available, snapshot.durationSeconds - elapsed);
            if (step <= 0f || !TickStep(step)) return false;
            available -= step;
            elapsed += step;
            steps++;
        }
        if (elapsed >= snapshot.durationSeconds) { Stop(); return false; }
        RenderPreview();
        return IsRunning;
    }

    void Update() {
        if (!IsRunning) return;
        double now = EditorApplication.timeSinceStartup;
        float frameDelta = Mathf.Max(0f, (float)(now - lastEditorTime));
        lastEditorTime = now;
        accumulator += frameDelta;
        int steps = 0;
        while (accumulator >= snapshot.fixedStep && steps < snapshot.maxStepsPerEditorUpdate && IsRunning) {
            if (!TickEditorPreview(snapshot.fixedStep)) return;
            accumulator -= snapshot.fixedStep;
            steps++;
        }
        if (IsRunning) RenderPreview();
    }

    bool TickStep(float step) {
        if (!bodies.TickBodies(step)) { Fail("A production preview component rejected its isolated step.", out _); return false; }
        if (!previewPhysicsScene.Simulate(step)) { Fail("Isolated PhysicsScene2D simulation failed.", out _); return false; }
        manager.Tick(step);
        return IsRunning;
    }

    void CreateCamera() {
        var lightObject = CreatePreviewObject("Traffic Editor Preview Light");
        var light = lightObject.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
        light.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Global;
        light.intensity = 1f;
        foreach (var layer in SortingLayer.layers) light.AddTargetSortingLayer(layer.id);
        var cameraObject = CreatePreviewObject("Traffic Editor Preview Camera");
        previewCamera = cameraObject.AddComponent<Camera>();
        previewTexture = new RenderTexture(snapshot.renderWidth, snapshot.renderHeight, 16, RenderTextureFormat.ARGB32);
        previewTexture.Create();
        Rect bounds = snapshot.draft.localBounds;
        float aspect = (float)snapshot.renderWidth / snapshot.renderHeight;
        float vertical = bounds.height * 0.5f + snapshot.cameraPadding;
        float horizontal = (bounds.width * 0.5f + snapshot.cameraPadding) / aspect;
        previewCamera.orthographic = true;
        previewCamera.scene = previewScene;
        previewCamera.orthographicSize = Mathf.Max(vertical, horizontal);
        previewCamera.transform.position = new Vector3(snapshot.mapOriginWorld.x + bounds.center.x, snapshot.mapOriginWorld.y + bounds.center.y, -10f);
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.black;
        previewCamera.targetTexture = previewTexture;
    }

    void RenderPreview() {
        if (previewCamera != null && previewTexture != null && previewTexture.IsCreated()) previewCamera.Render();
    }

    GameObject CreatePreviewObject(string name) {
        GameObject value = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
        SceneManager.MoveGameObjectToScene(value, previewScene);
        return value;
    }

    void DisposeCameraAndTexture() {
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (previewTexture != null) { previewTexture.Release(); UnityEngine.Object.DestroyImmediate(previewTexture); }
        previewTexture = null;
        previewCamera = null;
    }

    void CloseServices() {
        if (services == null) return;
        try {
            services.MarkRoleOwnerCleaned(VehicleRole.Civilian);
        }
        finally {
            try {
                services.MarkRoleOwnerCleaned(VehicleRole.Police);
            }
            finally {
                services.Close();
                servicesClosed = services.IsClosed;
            }
        }
    }

    static Snapshot CreateSnapshot(Configuration input) {
        var copy = new Snapshot();
        try {
            copy.draft = JsonUtility.FromJson<MapNavigationDocument>(JsonUtility.ToJson(input.draft));
            copy.prefabCatalog = CloneBindings(input.prefabCatalog);
            copy.budget = UnityEngine.Object.Instantiate(input.budget);
            copy.respawnRules = UnityEngine.Object.Instantiate(input.respawnRules);
            copy.limits = JsonUtility.FromJson<TrafficValidationLimits>(JsonUtility.ToJson(input.limits));
            copy.selectedPoolId = input.selectedPoolId;
            copy.mapOriginWorld = input.mapOriginWorld;
            copy.seed = input.seed;
            copy.durationSeconds = input.durationSeconds;
            copy.fixedStep = input.fixedStep;
            copy.maxStepsPerEditorUpdate = input.maxStepsPerEditorUpdate;
            copy.physicsQueryCapacity = input.physicsQueryCapacity;
            copy.renderWidth = input.renderWidth;
            copy.renderHeight = input.renderHeight;
            copy.cameraPadding = input.cameraPadding;
            copy.profiles = new NpcVehicleProfile[input.profiles.Length];
            for (int i = 0; i < input.profiles.Length; i++) copy.profiles[i] = UnityEngine.Object.Instantiate(input.profiles[i]);
            if (!ApplyPoolFilter(copy.draft, copy.selectedPoolId, out string issue)) throw new InvalidOperationException(issue);
            return copy;
        }
        catch {
            copy?.Dispose();
            throw;
        }
    }

    static ProfilePrefabBinding[] CloneBindings(ProfilePrefabBinding[] source) {
        var copy = new ProfilePrefabBinding[source.Length];
        for (int i = 0; i < source.Length; i++)
            copy[i] = new ProfilePrefabBinding { visualCatalogId = source[i].visualCatalogId, prefab = source[i].prefab };
        return copy;
    }

    static bool ValidateInput(Configuration input, out string issue) {
        issue = null;
        if (input == null || input.draft == null || input.profiles == null || input.prefabCatalog == null || input.budget == null || input.respawnRules == null || input.limits == null)
            return Reject("Preview requires draft, profiles, prefab bindings, budget, respawn rules and validation limits.", out issue);
        if (string.IsNullOrWhiteSpace(input.selectedPoolId) || !Finite(input.mapOriginWorld) || !FinitePositive(input.durationSeconds) || input.durationSeconds > 60f ||
            !FinitePositive(input.fixedStep) || input.maxStepsPerEditorUpdate <= 0 || input.physicsQueryCapacity <= 0 || input.renderWidth <= 0 || input.renderHeight <= 0 || !Finite(input.cameraPadding) || input.cameraPadding < 0f)
            return Reject("Preview timing, bounds, camera, and physics capacities must be finite, positive and bounded to 60 seconds.", out issue);
        if (!FiniteNonNegative(input.budget.maxCivilianMoving) || !FiniteNonNegative(input.budget.maxTotalMoving) || input.budget.maxCivilianMoving <= 0 || input.budget.maxTotalMoving <= 0)
            return Reject("Preview budget must permit at least one civilian and one total moving vehicle.", out issue);
        if (!FiniteNonNegative(input.respawnRules.minimumDelaySeconds) || !FiniteNonNegative(input.respawnRules.minimumPlayerDistance) || !FiniteNonNegative(input.respawnRules.replacementDeadlineSeconds))
            return Reject("Respawn rules must be finite and non-negative.", out issue);
        if (input.profiles.Length == 0 || input.prefabCatalog.Length == 0) return Reject("Preview requires at least one profile and one prefab binding.", out issue);
        var profiles = new HashSet<string>();
        for (int i = 0; i < input.profiles.Length; i++) {
            NpcVehicleProfile profile = input.profiles[i];
            if (profile == null || string.IsNullOrWhiteSpace(profile.vehicleProfileId) || string.IsNullOrWhiteSpace(profile.visualCatalogId) || !profiles.Add(profile.vehicleProfileId) ||
                profile.motorSettings == null || !ValidMotor(profile.motorSettings) || !FinitePositive(profile.colliderSize.x) || !FinitePositive(profile.colliderSize.y) || !FinitePositive(profile.baseMass))
                return Reject("Every preview profile needs a unique id, visual id, finite motor data, footprint and mass.", out issue);
        }
        var visuals = new HashSet<string>();
        for (int i = 0; i < input.prefabCatalog.Length; i++) {
            ProfilePrefabBinding binding = input.prefabCatalog[i];
            if (binding == null || string.IsNullOrWhiteSpace(binding.visualCatalogId) || binding.prefab == null || !visuals.Add(binding.visualCatalogId) ||
                binding.prefab.GetComponent<Rigidbody2D>() == null || binding.prefab.GetComponent<NpcVehicleMotor>() == null || binding.prefab.GetComponent<CivilianRouteFollower>() == null)
                return Reject("Every preview prefab binding needs one visual id and a production body, Rigidbody2D, motor and follower.", out issue);
        }
        for (int i = 0; i < input.profiles.Length; i++) if (!visuals.Contains(input.profiles[i].visualCatalogId)) return Reject("A profile has no explicit preview prefab binding.", out issue);
        if (input.draft.vehiclePools == null) return Reject("Draft has no selected civilian pool.", out issue);
        bool poolFound = false;
        for (int i = 0; i < input.draft.vehiclePools.Count; i++) if (input.draft.vehiclePools[i] != null && input.draft.vehiclePools[i].poolId == input.selectedPoolId) { poolFound = true; break; }
        return poolFound ? true : Reject("Selected preview pool does not exist in the draft.", out issue);
    }

    static bool ApplyPoolFilter(MapNavigationDocument draft, string selectedPoolId, out string issue) {
        issue = null;
        if (draft.civilianRoutes == null) return true;
        for (int i = 0; i < draft.civilianRoutes.Count; i++) {
            CivilianRouteRecord route = draft.civilianRoutes[i];
            if (route != null && route.vehiclePoolId != selectedPoolId) route.targetCount = 0;
        }
        return true;
    }

    bool Fail(string message, out string issue) {
        FailureReason = string.IsNullOrWhiteSpace(message) ? "Preview failed without a diagnostic." : message;
        issue = FailureReason;
        Stop();
        return false;
    }

    static bool Reject(string message, out string issue) { issue = message; return false; }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonNegative(float value) => Finite(value) && value >= 0f;
    static bool ValidMotor(NpcMotorSettings settings) =>
        FinitePositive(settings.cruiseSpeed) && FinitePositive(settings.maxSpeed) && FinitePositive(settings.reverseSpeed) &&
        FinitePositive(settings.acceleration) && FinitePositive(settings.brakeDeceleration) && FinitePositive(settings.maxEngineForce) &&
        FinitePositive(settings.maxBrakeForce) && FinitePositive(settings.turnRate) && FinitePositive(settings.minimumTurningRadius) &&
        FiniteNonNegative(settings.lateralGrip) && settings.lateralGrip <= 1f && FinitePositive(settings.sensorInterval) &&
        FiniteNonNegative(settings.reactionTime) && FinitePositive(settings.minimumGap);
}
#endif
