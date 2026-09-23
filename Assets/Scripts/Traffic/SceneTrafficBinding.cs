using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit scene composition and preflight gate for a map's civilian pilot; never rebuilds city geometry.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-30)]
public sealed class SceneTrafficBinding : MonoBehaviour {
    [SerializeField] MapData map;
    [SerializeField] TrafficSessionHost host;
    [SerializeField] TrafficManager traffic;
    [SerializeField] PrefabCivilianVehicleBodyProvider bodies;
    [SerializeField] Camera gameplayCamera;
    [SerializeField] NpcVehicleProfile[] profiles;
    [SerializeField] PoliceVehicleProfile[] policeVehicleProfiles;
    [SerializeField] PoliceBehaviorProfile[] policeBehaviorProfiles;
    [SerializeField] PoliceDirectorData[] policeDirectorProfiles;
    [SerializeField] PoliceVehiclePrefabCatalog policePrefabCatalog;
    [SerializeField] PrefabPoliceVehicleBodyProvider policeBodyProvider;
    [SerializeField] PoliceDirector policeDirector;
    [SerializeField] PoliceSceneBinding policeSceneBinding;
    [SerializeField] PopulationBudgetData population;
    [SerializeField] RespawnTimingRules respawn;
    [SerializeField] string populationProfileId;
    [SerializeField] string editorTestDifficultyId;
    [SerializeField] Vector2 mapOrigin;
    [SerializeField, Min(1)] int queryCapacity = 256;
    [SerializeField, Min(0f)] float clearanceMargin = 0.4f;
    [SerializeField, Min(0f)] float cameraMargin = 4f;
    [SerializeField, Min(0f)] float minimumApproachSeconds = 2f;
    [SerializeField] PoliceNavigationSettings policeNavigationSettings = new PoliceNavigationSettings();
    [SerializeField] PolicePlacementSettings policePlacementSettings = new PolicePlacementSettings();
    BuiltInTrafficProfileCatalog catalog;
    PoliceProfileCatalog policeCatalog;
    PoliceDirectorData policeDirectorProfile;
    PolicePopulationRuntime policePopulationRuntime;
    bool policeSpawnSubscribed;
    bool policePlayerReadySubscribed;
    bool policeOwnerFrozen;
    bool legacyPoliceOwner;
    PhysicsSceneAreaClearanceQuery staticClearance;
    Rigidbody2D playerBody;
    Collider2D playerCollider;

    /// <summary>Authored map identity; must match the host, document and frozen session.</summary>
    public MapData Map => map;
    /// <summary>Explicit direct-editor tier; an empty value provides no fallback.</summary>
    public string EditorTestDifficultyId => editorTestDifficultyId;
    /// <summary>Validated typed police profile catalog, or null when police composition is absent.</summary>
    public PoliceProfileCatalog PoliceCatalog => policeCatalog;
    /// <summary>Validated director profile selected by the active difficulty, or null when absent.</summary>
    public PoliceDirectorData PoliceDirectorProfile => policeDirectorProfile;
    /// <summary>Validated police body provider, or null when police composition is absent.</summary>
    public PrefabPoliceVehicleBodyProvider PoliceBodyProvider => policeBodyProvider;
    /// <summary>Validated police prefab catalog, or null when police composition is absent.</summary>
    public PoliceVehiclePrefabCatalog PolicePrefabCatalog => policePrefabCatalog;
    /// <summary>Optional scene-local police director used by the validated police composition.</summary>
    public PoliceDirector Director => policeDirector;
    /// <summary>Placement settings retained as migration input for the explicit police owner.</summary>
    public PolicePlacementSettings PolicePlacementSettings => policePlacementSettings;
    /// <summary>Navigation settings retained as migration input for the explicit police owner.</summary>
    public PoliceNavigationSettings PoliceNavigationSettings => policeNavigationSettings;
    /// <summary>Explicit police owner seam; null means the legacy owner remains selected.</summary>
    public PoliceSceneBinding PoliceBinding => policeSceneBinding;
    /// <summary>True when this binding still owns the legacy police population path.</summary>
    public bool UsesLegacyPoliceOwner => !policeOwnerFrozen ? policeSceneBinding == null : legacyPoliceOwner;
    /// <summary>Gameplay camera used by traffic and later police composition.</summary>
    public Camera GameplayCamera => gameplayCamera;
    /// <summary>Map-local to world coordinate offset authored by the binding.</summary>
    public Vector2 MapOriginWorld => mapOrigin;
    /// <summary>Maximum number of colliders used by bounded clearance queries.</summary>
    public int QueryCapacity => queryCapacity;
    /// <summary>Static route clearance margin in world units.</summary>
    public float ClearanceMargin => clearanceMargin;
    /// <summary>Camera visibility margin in world units.</summary>
    public float CameraMargin => cameraMargin;
    /// <summary>Minimum approach time used by bounded spawn safety.</summary>
    public float MinApproachSeconds => minimumApproachSeconds;

    void OnEnable() {
        if (host == null) return;
        host.Activated += Activate;
        host.Ended += StopTraffic;
        if (UsesLegacyPoliceOwner) {
            host.PlayerReady += TryActivatePolicePopulation;
            policePlayerReadySubscribed = true;
        }
    }

    void Start() { if (host != null && host.IsActive) Activate(); }
    void Update() {
        if (host != null && host.IsActive) FreezePoliceOwnerSelection();
        if (!UsesLegacyPoliceOwner) return;
        if (Time.timeScale <= 0f) return;
        if (policePopulationRuntime != null &&
            (policeDirector == null || !policeDirector.isActiveAndEnabled || policeDirector.Runtime == null))
            StopPolicePopulation();
        TryActivatePolicePopulation(host != null ? host.Player : null);
        if (policePopulationRuntime != null) {
            TrafficDamageWorld damageWorld = host != null ? host.GetComponent<TrafficDamageWorld>() : null;
            if (damageWorld != null) policePopulationRuntime.Tick(damageWorld.SessionTime);
        }
    }
    void OnDisable() {
        if (host != null) {
            host.Activated -= Activate;
            host.Ended -= StopTraffic;
            if (policePlayerReadySubscribed) host.PlayerReady -= TryActivatePolicePopulation;
        }
        policePlayerReadySubscribed = false;
        StopTraffic();
    }

    /// <summary>Validates identities, difficulty, prefab catalog, damage profiles and static route sweeps before snapshot freeze.</summary>
    public bool ValidateForSession(TrafficSessionHost owner, TrafficSessionContext context, MapNavigationDocument document, out string reason) {
        reason = null;
        FreezePoliceOwnerSelection();
        if (!isActiveAndEnabled || owner != host || map == null || host.Map != map || document == null ||
            document.mapId != map.mapId || context.Draft.mapId != map.mapId || map.sceneName != gameObject.scene.name)
            return Reject("Traffic map/scene/session identities do not match.", out reason);
        if (traffic == null || bodies == null || gameplayCamera == null || !gameplayCamera.orthographic || population == null || respawn == null || host.DamageSettings == null ||
            traffic.gameObject.scene != gameObject.scene || bodies.gameObject.scene != gameObject.scene || gameplayCamera.gameObject.scene != gameObject.scene)
            return Reject("Traffic binding requires same-scene manager, bodies, orthographic camera, population, respawn and damage settings.", out reason);
        if (map.levelData == null || map.levelData.GetDifficulty(context.Draft.difficultyId) == null)
            return Reject("No authored traffic difficulty. Configure an explicit editor test context for direct-open scenes.", out reason);
        if (document.difficultyProfileBindings == null || document.profileDependencies == null || document.civilianRoutes == null || document.vehiclePools == null ||
            !host.DamageSettings.IsValid() || population.maxCivilianMoving < 0 || population.maxTotalMoving < 0 || population.maxWreckSlots < 0 || population.maxTotalPhysicsObjects < 0)
            return Reject("Malformed traffic catalog or service budgets.", out reason);
        int matches = 0;
        DifficultyTrafficBinding selectedDifficultyBinding = null;
        foreach (var binding in document.difficultyProfileBindings) {
            if (binding == null || binding.difficultyId != context.Draft.difficultyId) continue;
            if (string.IsNullOrEmpty(populationProfileId) || binding.civilianPopulationProfileId != populationProfileId)
                return Reject("Difficulty population profile is unresolved.", out reason);
            matches++;
            selectedDifficultyBinding = binding;
        }
        if (matches != 1) return Reject("Difficulty traffic binding is missing or ambiguous.", out reason);
        staticClearance = new PhysicsSceneAreaClearanceQuery(gameObject.scene.GetPhysicsScene2D(), mapOrigin, true, queryCapacity);
        if (profiles == null || profiles.Length == 0) return Reject("Traffic profile catalog is empty.", out reason);
        var ids = new HashSet<string>();
        foreach (var profile in profiles) {
            if (profile == null || string.IsNullOrEmpty(profile.vehicleProfileId) || !ids.Add(profile.vehicleProfileId) ||
                !NpcVehicleBodyValidator.ValidateProfileBody(profile, out reason)) return Reject("Invalid or duplicated traffic profile: " + reason, out reason);
            if (!bodies.TryGetAuthoredPrefab(profile.visualCatalogId, out var prefab) || prefab.Body == null || prefab.Motor == null || prefab.Follower == null || prefab.Sensor == null || prefab.Receiver == null ||
                Vector2.Distance(prefab.Footprint, profile.colliderSize) > 0.001f)
                return Reject("Missing or mismatched civilian prefab: " + profile.visualCatalogId, out reason);
            if (!host.DamageSettings.TryResolve(profile.damageProfileId, out _) || !host.DamageSettings.TryResolve(profile.explosionProfileId, out _))
                return Reject("Missing damage/explosion profile: " + profile.vehicleProfileId, out reason);
        }
        NpcVehicleProfile[] mergedProfiles = BuildMergedProfiles(out reason);
        if (mergedProfiles == null) return false;
        catalog = new BuiltInTrafficProfileCatalog(mergedProfiles);
        // Catalog membership is independent of which component owns police lifetimes.
        if (!ValidatePoliceConfiguration(selectedDifficultyBinding, catalog, out reason)) return false;
        if (!UsesLegacyPoliceOwner && (policeSceneBinding == null || !policeSceneBinding.ValidateForSession(owner, out reason))) {
            return false;
        }
        foreach (string id in document.profileDependencies)
            if (catalog.ResolveVehicleProfile(id) == null) return Reject("Unresolved profile dependency: " + id, out reason);
        var graph = new RoadGraphRuntime(document);
        foreach (var route in document.civilianRoutes) {
            VehiclePoolRecord pool = null;
            foreach (var candidate in document.vehiclePools) if (candidate != null && candidate.poolId == route.vehiclePoolId) pool = candidate;
            if (pool == null || pool.entries == null || pool.entries.Count == 0) return Reject("Missing route vehicle pool.", out reason);
            var cursor = new RouteCursor();
            if (!cursor.TryBind(graph, route, out reason)) return false;
            foreach (var entry in pool.entries) {
                var profile = entry != null ? catalog.ResolveVehicleProfile(entry.vehicleProfileId) : null;
                if (profile == null || !profile.allowedRoles.Contains(VehicleRole.Civilian)) return Reject("Unresolved civilian pool profile.", out reason);
                var constraints = new RouteTransitionFilter.VehicleConstraints(profile.colliderSize.x, traffic.FollowerSettings.widthSafetyMargin, profile.motorSettings.minimumTurningRadius, traffic.FollowerSettings.maxTurnAngle);
                if (!RouteTransitionFilter.IsDrivable(cursor, constraints, out reason)) return false;
                foreach (string edgeId in route.edgeIds) {
                    var edge = graph.GetEdge(edgeId);
                    if (!RoadClearanceChecker.IsEdgeSweepClear(graph.GetNode(edge.fromNodeId), edge, graph.GetNode(edge.toNodeId), profile.colliderSize, Vector2.zero, clearanceMargin, document.localBounds, document.noSpawnRegions, staticClearance))
                        return Reject("Route footprint blocked: " + edgeId, out reason);
                }
            }
        }
        if (!ConfigurePoliceComposition(out reason)) return false;
        if (!UsesLegacyPoliceOwner && policeSceneBinding.NavigationMode == PoliceNavigationMode.FreeDrive)
            context.Integrity.MarkInvalid("Free-drive police tuning has no versioned competitive content fingerprint yet.");
        return true;
    }

    /// <summary>
    /// Creates the shared per-session service bundle from the validated merged catalog and authored
    /// population budget. This method does not activate either role or allocate a physical body.
    /// </summary>
    /// <param name="document">Validated detached navigation document for the current session.</param>
    /// <param name="identityRegistry">Coordinator-owned identity registry shared by all role owners.</param>
    /// <returns>A shared bundle, or null when validation has not completed or an input is missing.</returns>
    public TrafficSessionServices CreateSessionServices(MapNavigationDocument document, VehicleIdentityRegistry identityRegistry) {
        if (document == null || catalog == null || population == null || identityRegistry == null) return null;
        var graph = new RoadGraphRuntime(document);
        var pool = new NpcVehiclePool(population, catalog, identityRegistry);
        var populationService = new VehiclePopulationService(population);
        return new TrafficSessionServices(graph, catalog, pool, populationService, population, identityRegistry);
    }

    /// <summary>
    /// Captures detached content evidence after navigation and service validation. This exposes
    /// authored identity only; it does not spawn, configure, or otherwise mutate the runtime world.
    /// </summary>
    public SessionContentEvidence CaptureContentEvidence(TrafficSessionContext context, GameManager manager,
        MapNavigationDocument document, bool navigationValidated, bool requiredSystemsValid,
        TrafficValidationLimits limits = null) {
        var draft = context != null ? context.Draft : null;
        var modifiers = context != null ? context.Modifiers : new FrozenModifierRules(null);
        var vehicle = manager != null ? manager.currentVehicle : null;
        if (context == null || draft == null || map == null || map.mapId != draft.mapId ||
            (manager != null && (vehicle == null || vehicle.vehicleId != draft.vehicleId)) ||
            (manager != null && manager.SelectedShiftDurationMinutes != draft.shiftDurationMinutes)) {
            context?.Integrity.MarkInvalid("Captured content evidence does not match the frozen session selection.");
            return null;
        }
        var vehicleSave = manager != null ? manager.GetCurrentVehicleSave() : null;
        float vehicleMass = vehicle != null && vehicle.bodySettings != null
            ? vehicle.bodySettings.ResolveMass(manager.GetLevel(VehicleStatId.Health)) : 0f;
        bool builtInMap = manager != null && manager.Content != null &&
            manager.Content.GetMapProviderId(map) == BuiltInContentProvider.SourceId;
        bool builtInVehicle = manager != null && manager.Content != null && vehicle != null &&
            manager.Content.GetVehicleProviderId(vehicle) == BuiltInContentProvider.SourceId;
        var civilianEvidence = new List<SessionNpcProfileEvidence>();
        if (profiles != null) foreach (var profile in profiles) {
            if (profile == null) continue;
            civilianEvidence.Add(new SessionNpcProfileEvidence(profile.vehicleProfileId, profile.baseMass,
                profile.collisionArmor, profile.explosionResistance, profile.maxHealth, profile.visualCatalogId,
                profile.motorSettings != null ? JsonUtility.ToJson(profile.motorSettings) : string.Empty,
                string.Empty, profile.damageProfileId,
                profile.explosionProfileId));
        }
        var policeEvidence = new List<SessionNpcProfileEvidence>();
        if (policeVehicleProfiles != null) foreach (var profile in policeVehicleProfiles) {
            var shared = profile != null ? profile.sharedNpc : null;
            if (shared == null) continue;
            policeEvidence.Add(new SessionNpcProfileEvidence(shared.vehicleProfileId, shared.baseMass,
                shared.collisionArmor, shared.explosionResistance, shared.maxHealth, shared.visualCatalogId,
                shared.motorSettings != null ? JsonUtility.ToJson(shared.motorSettings) : string.Empty,
                string.Join("|", GetPoliceBehaviorFingerprints()),
                shared.damageProfileId, shared.explosionProfileId));
        }
        var director = policeDirectorProfile;
        string navigationHash = string.Empty;
        if (document != null && NavigationDocumentCodec.TryEncode(document, limits, out _, out string encodedHash, out _))
            navigationHash = encodedHash;
        string heatTierFingerprint = string.Empty;
        if (director != null && director.heatTiers != null)
            heatTierFingerprint = string.Join("|", director.heatTiers.ConvertAll(tier => JsonUtility.ToJson(tier)));
        string populationFingerprint = population != null ? JsonUtility.ToJson(population) : string.Empty;
        string modifierFingerprint = BuildModifierFingerprint(modifiers);
        var evidence = new SessionContentEvidence(draft != null ? draft.sessionId : string.Empty,
            draft != null ? draft.mapId : string.Empty, draft != null ? draft.vehicleId : string.Empty,
            draft != null ? draft.difficultyId : string.Empty, draft != null ? draft.shiftDurationMinutes : 0,
            draft != null && draft.isFreeplay, builtInMap, builtInVehicle,
            vehicleSave != null && vehicleSave.hasCustomTuning,
            navigationValidated && !modifiers.DisablesCivilianTraffic,
            director != null && navigationValidated && !modifiers.DisablesPolice,
            navigationValidated, requiredSystemsValid, document != null ? document.documentId : string.Empty,
            populationProfileId, director != null ? director.profileId : string.Empty,
            SessionContentEvidence.ImplementedTrafficRulesetVersion, string.Empty, string.Empty, vehicleMass,
            vehicle != null ? vehicle.explosionResistance : 0f,
            director != null ? director.baseHeatAtStart : 0f,
            director != null ? director.baseHeatPerActiveSecond : 0f,
            director != null ? director.maximumHeat : 0f,
            director != null ? director.earliestPoliceTime : 0f,
            modifiers, modifiers.Ids, civilianEvidence, policeEvidence, navigationHash,
            modifierFingerprint, populationFingerprint, heatTierFingerprint, false, false, false,
            population != null ? population.maxCivilianMoving : 0,
            population != null ? population.maxTotalMoving : 0,
            population != null ? population.maxWreckSlots : 0,
            population != null ? population.maxTotalPhysicsObjects : 0);
        if (!evidence.MatchesSessionIdentity(draft, modifiers)) {
            context.Integrity.MarkInvalid("Captured content evidence identity does not match the session draft.");
            return null;
        }
        return evidence;
    }

    IEnumerable<string> GetPoliceBehaviorFingerprints() {
        if (policeBehaviorProfiles == null) yield break;
        foreach (var profile in policeBehaviorProfiles)
            if (profile != null) yield return profile.behaviorProfileId + ":" + JsonUtility.ToJson(profile);
    }

    static string BuildModifierFingerprint(FrozenModifierRules modifiers) {
        if (modifiers == null) return string.Empty;
        string fingerprint = string.Join("|", modifiers.Ids) + ":" +
            modifiers.ScoreMultiplier.ToString("R", CultureInfo.InvariantCulture) + ":" +
            modifiers.DisablesCivilianTraffic + ":" + modifiers.DisablesPolice;
        foreach (VehicleStatId stat in System.Enum.GetValues(typeof(VehicleStatId)))
            fingerprint += ":" + stat + "=" + modifiers.GetStatDelta(stat).ToString("R", CultureInfo.InvariantCulture);
        return fingerprint;
    }

    NpcVehicleProfile[] BuildMergedProfiles(out string reason) {
        reason = null;
        var merged = new List<NpcVehicleProfile>();
        var ids = new HashSet<string>();
        if (profiles != null) foreach (var profile in profiles) {
            if (profile == null || string.IsNullOrWhiteSpace(profile.vehicleProfileId) || !ids.Add(profile.vehicleProfileId)) {
                reason = "Civilian profile catalog is invalid or duplicated.";
                return null;
            }
            merged.Add(profile);
        }
        if (HasPoliceConfiguration) {
            if (policeVehicleProfiles == null || policeVehicleProfiles.Length == 0) {
                reason = "Police vehicle profiles are required when police configuration is present.";
                return null;
            }
            foreach (var policeProfile in policeVehicleProfiles) {
                NpcVehicleProfile shared = policeProfile != null ? policeProfile.sharedNpc : null;
                if (shared == null || string.IsNullOrWhiteSpace(shared.vehicleProfileId) || !ids.Add(shared.vehicleProfileId)) {
                    reason = "Police shared profile is invalid or conflicts with the civilian catalog.";
                    return null;
                }
                merged.Add(shared);
            }
        }
        return merged.ToArray();
    }

    bool ValidatePoliceConfiguration(DifficultyTrafficBinding selectedDifficultyBinding, BuiltInTrafficProfileCatalog mergedCatalog, out string reason) {
        reason = null;
        policeCatalog = null;
        policeDirectorProfile = null;
        if (!HasPoliceConfiguration) return true;
        if (policeVehicleProfiles == null || policeVehicleProfiles.Length == 0 || policeBehaviorProfiles == null || policeBehaviorProfiles.Length == 0 ||
            policeDirectorProfiles == null || policeDirectorProfiles.Length == 0 || policePrefabCatalog == null || policeBodyProvider == null || policeDirector == null)
            return Reject("Police configuration is incomplete.", out reason);
        if (policeBodyProvider.gameObject.scene != gameObject.scene || policeDirector.gameObject.scene != gameObject.scene)
            return Reject("Police body provider and director must belong to the bound scene.", out reason);
        policeCatalog = new PoliceProfileCatalog(mergedCatalog, policeVehicleProfiles, policeBehaviorProfiles);
        if (!policeCatalog.TryValidate(out reason)) return false;
        if (!PolicePreflight.ValidatePrefabCatalog(policePrefabCatalog, out reason)) return false;
        foreach (var policeProfile in policeVehicleProfiles) {
            if (!policePrefabCatalog.TryResolve(policeProfile.sharedNpc.visualCatalogId, out var body) ||
                !PolicePreflight.ValidatePrefab(body, policeProfile.sharedNpc, out reason)) return false;
            if (!host.DamageSettings.TryResolve(policeProfile.sharedNpc.damageProfileId, out _) ||
                !host.DamageSettings.TryResolve(policeProfile.sharedNpc.explosionProfileId, out _))
                return Reject("Missing police damage/explosion profile: " + policeProfile.sharedNpc.vehicleProfileId, out reason);
        }
        if (selectedDifficultyBinding == null || string.IsNullOrWhiteSpace(selectedDifficultyBinding.policeDirectorProfileId))
            return Reject("Selected difficulty has no police director profile.", out reason);
        int directorMatches = 0;
        foreach (var candidate in policeDirectorProfiles) {
            if (candidate == null || candidate.profileId != selectedDifficultyBinding.policeDirectorProfileId) continue;
            policeDirectorProfile = candidate;
            directorMatches++;
        }
        if (directorMatches != 1 || policeDirectorProfile == null)
            return Reject("Selected police director profile is missing or ambiguous.", out reason);
        if (!policeDirectorProfile.TryValidate(out reason)) return false;
        if (!ValidateDirectorCompositions(out reason)) return false;
        return true;
    }

    bool ConfigurePoliceComposition(out string reason) {
        reason = null;
        if (!UsesLegacyPoliceOwner) return policeSceneBinding.ConfigureForSession(out reason);
        if (!HasPoliceConfiguration) return true;
        if (!policeBodyProvider.Configure(policePrefabCatalog, policeBodyProvider.maxInstancesPerVisual, out reason)) return false;
        return policeDirector.TryConfigure(host, policeDirectorProfile, out reason);
    }

    bool ValidateDirectorCompositions(out string reason) {
        reason = null;
        if (policeDirectorProfile == null || policeCatalog == null)
            return Reject("Police director composition is unresolved.", out reason);
        foreach (var tier in policeDirectorProfile.heatTiers) {
            if (tier == null || tier.compositions == null)
                return Reject("Police director tier composition is missing.", out reason);
            foreach (var entry in tier.compositions) {
                if (entry == null)
                    return Reject("Police director composition entry is missing.", out reason);
                var vehicle = policeCatalog.ResolveVehicle(entry.vehicleProfileId);
                var behavior = policeCatalog.ResolveBehavior(entry.behaviorProfileId);
                if (vehicle == null || behavior == null)
                    return Reject("Police director composition references an unresolved profile or behavior.", out reason);
                if (entry.tacticalRole != behavior.tacticalRole)
                    return Reject("Police director composition tactical role does not match its behavior role.", out reason);
                if (!vehicle.supportedTacticalRoles.Contains(behavior.tacticalRole))
                    return Reject("Police director composition uses an incompatible vehicle and behavior role.", out reason);
            }
        }
        return true;
    }

    bool HasPoliceConfiguration => (policeVehicleProfiles != null && policeVehicleProfiles.Length > 0) ||
        (policeBehaviorProfiles != null && policeBehaviorProfiles.Length > 0) ||
        (policeDirectorProfiles != null && policeDirectorProfiles.Length > 0) || policePrefabCatalog != null || policeBodyProvider != null;

    void Activate() {
        if (traffic == null || traffic.IsRunning || host == null || !host.IsActive || catalog == null) return;
        playerBody = host.Player.GetComponent<Rigidbody2D>();
        playerCollider = host.Player.GetComponent<Collider2D>();
        var snapshot = host.Coordinator.Context.Snapshot;
        if (!traffic.Initialize(new TrafficManager.Config {
            navigation = host.Navigation, catalog = catalog, bodies = bodies, budget = population, respawnRules = respawn,
            services = host.Services,
            identityRegistry = host.IdentityRegistry, damageWorld = host.GetComponent<TrafficDamageWorld>(),
            clearance = new PhysicsSceneAreaClearanceQuery(gameObject.scene.GetPhysicsScene2D(), Vector2.zero, false, queryCapacity), rejoinClearance = staticClearance,
            mapOriginWorld = mapOrigin, seed = snapshot.seed, civilianTrafficEnabled = snapshot.trafficEnabled,
            playerWorldPosition = PlayerPosition, playerWorldVelocity = PlayerVelocity, cameraWorldBounds = CameraWorldBounds,
            cameraMargin = cameraMargin, minApproachTimeSeconds = minimumApproachSeconds
        })) {
            host.Coordinator.Context.Integrity.MarkInvalid("Traffic composition failed.");
            Debug.LogError("Traffic composition failed: " + string.Join("; ", traffic.Diagnostics), this);
        }
    }

    void TryActivatePolicePopulation(GameObject player) {
        if (!UsesLegacyPoliceOwner) return;
        if (!HasPoliceConfiguration || policePopulationRuntime != null || host == null || !host.IsActive ||
            host.Services == null || policeDirector == null || policeDirector.Runtime == null ||
            !policeDirector.Runtime.IsEnabled || player == null ||
            player.scene != gameObject.scene || policeBodyProvider == null || policeCatalog == null ||
            policePrefabCatalog == null || host.Navigation == null) return;
        TrafficDamageWorld damageWorld = host.GetComponent<TrafficDamageWorld>();
        VehicleDamageReceiver receiver = player.GetComponent<VehicleDamageReceiver>();
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (damageWorld == null || !damageWorld.IsActive || receiver == null || !receiver.CanTakeDamage || body == null)
            return;
        if (policeNavigationSettings == null) policeNavigationSettings = new PoliceNavigationSettings();
        if (policePlacementSettings == null) policePlacementSettings = new PolicePlacementSettings();
        var worldClearance = new PhysicsSceneAreaClearanceQuery(gameObject.scene.GetPhysicsScene2D(), Vector2.zero, false, queryCapacity);
        var config = new PolicePopulationRuntime.Config {
            sessionCoordinator = host.Coordinator,
            services = host.Services,
            damageWorld = damageWorld,
            directorRuntime = policeDirector.Runtime,
            bodyProvider = policeBodyProvider,
            policeCatalog = policeCatalog,
            policePrefabCatalog = policePrefabCatalog,
            navigation = host.Navigation,
            mapOriginWorld = mapOrigin,
            playerReceiver = receiver,
            playerBody = body,
            currentCameraBounds = CameraWorldBounds,
            worldClearance = worldClearance,
            localStaticClearance = staticClearance,
            navigationSettings = policeNavigationSettings,
            placementSettings = policePlacementSettings,
            deterministicSeed = host.Coordinator.Context.Snapshot.seed,
            playerSurfaceRadius = PlayerSurfaceRadius(player),
            removePoliceContacts = policeDirector.RemovePoliceContacts
        };
        policePopulationRuntime = new PolicePopulationRuntime(config);
        policeDirector.SpawnRequested += HandlePoliceSpawnRequested;
        policeSpawnSubscribed = true;
    }

    void HandlePoliceSpawnRequested(PoliceDirectorRuntime.SpawnRequest request) {
        if (policePopulationRuntime == null) {
            policeDirector?.ReportSpawnRejected(request);
            return;
        }
        policePopulationRuntime.TryMaterialize(request);
    }

    void StopPolicePopulation() {
        if (!UsesLegacyPoliceOwner) return;
        if (policeSpawnSubscribed && policeDirector != null) policeDirector.SpawnRequested -= HandlePoliceSpawnRequested;
        policeSpawnSubscribed = false;
        if (policePopulationRuntime != null) policePopulationRuntime.Dispose();
        else host?.Services?.MarkRoleOwnerCleaned(VehicleRole.Police);
        policePopulationRuntime = null;
    }

    float PlayerSurfaceRadius(GameObject player) {
        Collider2D[] colliders = player.GetComponentsInChildren<Collider2D>(true);
        if (colliders == null || colliders.Length == 0) return 0f;
        Bounds bounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++) bounds.Encapsulate(colliders[i].bounds);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Vector2 center = body != null ? body.position : (Vector2)player.transform.position;
        return bounds.extents.magnitude + Vector2.Distance(bounds.center, center);
    }

    void FixedUpdate() {
        if (traffic == null || !traffic.IsRunning || playerCollider == null) return;
        Bounds playerBounds = playerCollider.bounds;
        var playerRect = new Rect(playerBounds.min, playerBounds.size);
        foreach (var junction in host.Navigation.junctions) {
            var zone = junction.conflictZone;
            zone.position += mapOrigin;
            traffic.Junctions.SetExternalOccupancy(junction.junctionId, zone.Overlaps(playerRect));
        }
    }

    Vector2 PlayerPosition() => playerBody != null ? playerBody.position : (Vector2)host.Player.transform.position;
    Vector2 PlayerVelocity() => playerBody != null ? playerBody.linearVelocity : Vector2.zero;
    /// <summary>
    /// Returns the axis-aligned world rectangle containing all four camera viewport corners at the
    /// gameplay plane. Using every corner preserves the full rotated-camera envelope for visibility
    /// and approach checks.
    /// </summary>
    public Rect CameraWorldBounds() {
        float depth = Mathf.Abs(gameplayCamera.transform.position.z);
        Vector3 bottomLeft = gameplayCamera.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
        Vector3 topLeft = gameplayCamera.ViewportToWorldPoint(new Vector3(0f, 1f, depth));
        Vector3 topRight = gameplayCamera.ViewportToWorldPoint(new Vector3(1f, 1f, depth));
        Vector3 bottomRight = gameplayCamera.ViewportToWorldPoint(new Vector3(1f, 0f, depth));
        float minX = Mathf.Min(Mathf.Min(bottomLeft.x, topLeft.x), Mathf.Min(topRight.x, bottomRight.x));
        float minY = Mathf.Min(Mathf.Min(bottomLeft.y, topLeft.y), Mathf.Min(topRight.y, bottomRight.y));
        float maxX = Mathf.Max(Mathf.Max(bottomLeft.x, topLeft.x), Mathf.Max(topRight.x, bottomRight.x));
        float maxY = Mathf.Max(Mathf.Max(bottomLeft.y, topLeft.y), Mathf.Max(topRight.y, bottomRight.y));
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }
    void StopTraffic() {
        StopPolicePopulation();
        if (traffic != null) traffic.EndSession();
        if (traffic == null || !traffic.IsRunning) host?.Services?.MarkRoleOwnerCleaned(VehicleRole.Civilian);
    }
    static bool Reject(string message, out string reason) { reason = message; return false; }

    /// <summary>Assigns the explicit owner seam before the shared session is active.</summary>
    /// <param name="binding">Explicit police owner, or null to retain legacy ownership.</param>
    /// <param name="reason">Failure description, or null when the owner selection was accepted.</param>
    /// <returns>True when the owner selection is still allowed and was accepted.</returns>
    public bool TrySetPoliceBinding(PoliceSceneBinding binding, out string reason) {
        reason = null;
        if (host != null && host.IsActive) return Reject("Police composition owner is frozen after session activation.", out reason);
        if (policeOwnerFrozen) return Reject("Police composition owner is already frozen for this binding.", out reason);
        bool wasLegacy = UsesLegacyPoliceOwner;
        policeSceneBinding = binding;
        policeOwnerFrozen = true;
        legacyPoliceOwner = binding == null;
        if (wasLegacy && !legacyPoliceOwner && policePlayerReadySubscribed && host != null) {
            host.PlayerReady -= TryActivatePolicePopulation;
            policePlayerReadySubscribed = false;
        } else if (!wasLegacy && legacyPoliceOwner && host != null) {
            host.PlayerReady += TryActivatePolicePopulation;
            policePlayerReadySubscribed = true;
        }
        return true;
    }

    void FreezePoliceOwnerSelection() {
        if (!policeOwnerFrozen) {
            legacyPoliceOwner = policeSceneBinding == null;
            policeOwnerFrozen = true;
            return;
        }
        bool currentLegacy = policeSceneBinding == null;
        if (currentLegacy != legacyPoliceOwner && host != null && host.IsActive)
            host.Coordinator?.Context.Integrity.MarkInvalid("Police composition owner cannot change during an active session.");
    }
}
