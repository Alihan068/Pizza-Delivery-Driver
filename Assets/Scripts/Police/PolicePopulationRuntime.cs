using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns police-only physical materialization for one traffic session. Logical director requests
/// become physical permission only after the shared population and pool reservations succeed.
/// This class never resets shared services globally and never creates a civilian respawn path.
/// </summary>
public sealed class PolicePopulationRuntime : IDisposable {
    /// <summary>Explicit session dependencies required by the police composition owner.</summary>
    public sealed class Config {
        /// <summary>Session coordinator that owns the player identity and active-session gate.</summary>
        public TrafficSessionCoordinator sessionCoordinator;
        /// <summary>Shared graph, catalog, pool, population and identity services.</summary>
        public TrafficSessionServices services;
        /// <summary>Active damage world used for receiver registration and death publication.</summary>
        public TrafficDamageWorld damageWorld;
        /// <summary>Session-local director whose request identity is committed after binding.</summary>
        public PoliceDirectorRuntime directorRuntime;
        /// <summary>Provider that owns inactive physical police bodies.</summary>
        public IPoliceVehicleBodyProvider bodyProvider;
        /// <summary>Typed police vehicle and behavior catalog.</summary>
        public PoliceProfileCatalog policeCatalog;
        /// <summary>Typed police visual prefab catalog used during profile validation.</summary>
        public PoliceVehiclePrefabCatalog policePrefabCatalog;
        /// <summary>Detached map navigation document containing authored police entries.</summary>
        public MapNavigationDocument navigation;
        /// <summary>World translation of map-local navigation coordinates.</summary>
        public Vector2 mapOriginWorld;
        /// <summary>Current player damage receiver.</summary>
        public VehicleDamageReceiver playerReceiver;
        /// <summary>Current player physics body.</summary>
        public Rigidbody2D playerBody;
        /// <summary>Current finite camera rectangle.</summary>
        public Func<Rect> currentCameraBounds;
        /// <summary>Fresh static-plus-dynamic clearance query for candidate placement.</summary>
        public IAreaClearanceQuery worldClearance;
        /// <summary>Fresh map-local static clearance query for route preparation.</summary>
        public IAreaClearanceQuery localStaticClearance;
        /// <summary>Bounded route-query settings snapshot source.</summary>
        public PoliceNavigationSettings navigationSettings;
        /// <summary>Bounded placement settings snapshot source.</summary>
        public PolicePlacementSettings placementSettings;
        /// <summary>Deterministic planner seed for this session.</summary>
        public int deterministicSeed;
        /// <summary>Player pivot-to-surface radius supplied by scene composition.</summary>
        public float playerSurfaceRadius;
        /// <summary>Removes player contact pairs without ending or replacing a police life.</summary>
        public Action<int> removePoliceContacts;
        /// <summary>Explicit navigation composition; FreeDrive never consumes a road placement result.</summary>
        public PoliceNavigationMode navigationMode = PoliceNavigationMode.LegacyRoad;
        /// <summary>Detached chase tuning used only by the free-drive owner.</summary>
        public PoliceFreeChaseSettings freeChaseSettings;
        /// <summary>Detached per-session bounds for stable individual driver traits.</summary>
        public PoliceUnitVariationSettings unitVariationSettings;
        /// <summary>Typed static source that preserves unavailable/saturated query results.</summary>
        public PoliceFreeNavigationQuery.IClearanceSource freeStaticSource;
    }

    sealed class LivePolice {
        public PoliceDirectorRuntime.SpawnRequest request;
        public NpcVehicleInstance instance;
        public PoliceVehicleBody body;
        public PoliceVehicleProfile vehicle;
        public PoliceBehaviorProfile behavior;
        public PolicePursuitController controller;
        public PoliceFreeAgent freeDrive;
        public int lifeId;
        public PolicePrefabGeometry geometry;
        public float invisibleSince = float.NaN;
        public float nextRelocationAt;
    }

    readonly Config config;
    readonly PoliceSpawnPlanner planner = new PoliceSpawnPlanner();
    readonly PoliceNavigationScheduler freeScheduler = new PoliceNavigationScheduler();
    readonly Dictionary<int, LivePolice> alive = new Dictionary<int, LivePolice>();
    readonly List<LivePolice> wrecks = new List<LivePolice>();
    readonly List<string> diagnostics = new List<string>();
    float lastActiveClock;
    bool ended;
    int nextFreeRequestId;
    int spawnOrdinal;
    int placementOrdinal;
    float nextFreeRelocationAt;
    float nextAheadWaveAt;
    readonly List<LivePolice> aheadWaveScratch = new List<LivePolice>();
    readonly List<FreePlacementWork> pendingFreePlacements = new List<FreePlacementWork>();
    readonly PoliceFreeChaseSettings freeChase;
    readonly PoliceUnitVariationSettings variation;
    readonly PoliceTacticsCoordinator tactics;
    readonly PolicePlacementSettings placement;
    readonly PoliceNavigationSettings navigationSettings;

    /// <summary>Creates a police composition owner with explicit session dependencies.</summary>
    public PolicePopulationRuntime(Config configuration) {
        config = configuration;
        placement = config?.placementSettings?.Clone();
        navigationSettings = config?.navigationSettings?.Clone();
        freeChase = config?.freeChaseSettings?.Clone();
        variation = config?.unitVariationSettings?.Clone() ?? new PoliceUnitVariationSettings();
        tactics = new PoliceTacticsCoordinator(freeChase);
        if (!ValidateConfig(out string reason)) diagnostics.Add(reason);
        if (config != null && config.damageWorld != null) config.damageWorld.VehicleDestroyed += HandleDestroyed;
    }

    /// <summary>Living police bodies owned by this runtime.</summary>
    public int ActiveCount => alive.Count;
    /// <summary>Police wreck bodies still awaiting receiver-owned recycling.</summary>
    public int WreckCount => wrecks.Count;
    /// <summary>Materialization and lifecycle diagnostics.</summary>
    public IReadOnlyList<string> Diagnostics => diagnostics;
    /// <summary>Last accepted active session clock value.</summary>
    public float LastActiveClock => lastActiveClock;
    /// <summary>True while this owner accepts requests and owns session resources.</summary>
    public bool IsRunning => !ended && ValidateConfig(out _);

    /// <summary>Returns the physical body for one currently owned living police identity.</summary>
    public bool TryGetBody(int lifeId, out PoliceVehicleBody body) {
        body = alive.TryGetValue(lifeId, out var police) ? police.body : null;
        return body != null;
    }

    /// <summary>
    /// Legacy placement commits synchronously. Free placement first queues bounded reachability;
    /// PreparePhysics later commits it or rejects the director request without leaking resources.
    /// </summary>
    /// <param name="request">Director request with a genuine current-session identity.</param>
    /// <returns>True only after the director commit succeeds.</returns>
    public bool TryMaterialize(PoliceDirectorRuntime.SpawnRequest request) {
        if (config != null && config.navigationMode == PoliceNavigationMode.FreeDrive) return QueueFreeSpawn(request);
        return Materialize(request, null);
    }

    bool Materialize(PoliceDirectorRuntime.SpawnRequest request, PoliceSpawnPose? freePose) {
        if (!IsRunning) { config?.directorRuntime?.Reject(request); return false; }
        if (!config.directorRuntime.IsInFlight(request)) return false;
        if (!config.policeCatalog.TrySelect(request.vehicleProfileId, request.behaviorProfileId,
            config.policePrefabCatalog, out PoliceVehicleProfile vehicle, out PoliceBehaviorProfile behavior,
            out _, out string profileReason)) return Reject(request, profileReason);

        if (!config.services.Population.TryReserveSpawn(VehicleRole.Police)) return Reject(request, "population reservation refused");
        bool populationReserved = true;
        NpcVehicleInstance life = null;
        PoliceVehicleBody body = null;
        bool poolAcquired = false;
        bool bodyAcquired = false;
        bool populationCommitted = false;
        bool activeLife = false;
        VehicleDamageReceiver receiver = null;
        PolicePursuitController controller = null;
        try {
            body = config.bodyProvider.Acquire(vehicle.sharedNpc);
            if (body == null) return FailTransaction(request, "police body provider returned no body", populationReserved,
                populationCommitted, activeLife, life, poolAcquired, body, bodyAcquired, receiver, controller);
            bodyAcquired = true;
            controller = body.GetComponent<PolicePursuitController>();
            if (controller == null) controller = body.gameObject.AddComponent<PolicePursuitController>();
            controller.enabled = true;
            PolicePrefabGeometry geometry = ReadGeometry(body);
            PoliceSpawnPose pose;
            if (freePose.HasValue) {
                pose = freePose.Value;
                if (!IsFreePoseSafe(pose, geometry, false, out string placementReason))
                    return FailTransaction(request, "free placement stale: " + placementReason, populationReserved,
                        populationCommitted, activeLife, life, poolAcquired, body, bodyAcquired, receiver, controller);
            } else if (Plan(vehicle.sharedNpc, geometry, false, out PoliceSpawnAcceptedCandidate candidate)) pose = candidate.pose;
            else
                return FailTransaction(request, "police placement refused: " + planner.LastFailureReason, populationReserved,
                    populationCommitted, activeLife, life, poolAcquired, body, bodyAcquired, receiver, controller);

            if (!config.services.Pool.TryAcquire(request.vehicleProfileId, VehicleRole.Police, out life))
                return FailTransaction(request, "police pool acquisition refused", populationReserved, populationCommitted,
                    activeLife, life, poolAcquired, body, bodyAcquired, receiver, controller);
            poolAcquired = true;

            body.transform.SetPositionAndRotation(pose.worldPosition, Quaternion.Euler(0f, 0f, pose.headingDegrees));
            body.Body.position = pose.worldPosition;
            body.Body.rotation = pose.headingDegrees;
            if (!life.TryActivate())
                return FailTransaction(request, "pool life activation refused", populationReserved, populationCommitted, activeLife,
                    life, poolAcquired, body, bodyAcquired, receiver, controller);
            activeLife = true;
            if (!config.services.Population.CommitSpawn(VehicleRole.Police))
                return FailTransaction(request, "population commit refused", populationReserved, populationCommitted, activeLife,
                    life, poolAcquired, body, bodyAcquired, receiver, controller);
            populationCommitted = true;

            receiver = body.DamageReceiver;
            if (receiver == null || !receiver.BindNpc(config.damageWorld, life, vehicle.sharedNpc, config.services.Pool,
                config.services.Population))
                return FailTransaction(request, "damage receiver binding refused", populationReserved, populationCommitted, activeLife,
                    life, poolAcquired, body, bodyAcquired, receiver, controller);

            body.gameObject.SetActive(true);
            int lifeId = life.Identity.Value.lifeId;
            string bindReason = null;
            PoliceFreeAgent freeDrive = null;
            if (config.navigationMode == PoliceNavigationMode.FreeDrive) {
                freeDrive = CreateFreeDriveState(body, vehicle, behavior, geometry, lifeId);
                if (freeDrive == null || !controller.TryBindFreeDrive(body, config.damageWorld, freeDrive, vehicle.sharedNpc) ||
                    !controller.TrySetTarget(config.playerReceiver, out bindReason)) {
                    freeDrive?.Reset();
                    return FailTransaction(request, "free-drive binding refused", populationReserved, populationCommitted,
                        activeLife, life, poolAcquired, body, bodyAcquired, receiver, controller);
                }
            } else {
                PolicePursuitController.Binding binding = new PolicePursuitController.Binding {
                    body = body,
                    vehicle = vehicle,
                    behavior = behavior,
                    damageWorld = config.damageWorld,
                    graph = config.services.Graph,
                    navigation = config.navigation,
                    mapOriginWorld = config.mapOriginWorld,
                    staticClearance = config.localStaticClearance,
                    navigationSettings = navigationSettings
                };
                if (!controller.TryBind(binding, out bindReason) || !controller.TrySetTarget(config.playerReceiver, out bindReason))
                    return FailTransaction(request, bindReason ?? "pursuit binding refused", populationReserved,
                        populationCommitted, activeLife, life, poolAcquired, body, bodyAcquired, receiver, controller);
            }
            if (!config.directorRuntime.Commit(request, lifeId))
                return FailTransaction(request, "director commit refused", populationReserved, populationCommitted, activeLife,
                    life, poolAcquired, body, bodyAcquired, receiver, controller);
            alive.Add(lifeId, new LivePolice {
                request = request, instance = life, body = body, vehicle = vehicle, behavior = behavior,
                controller = controller, lifeId = lifeId, geometry = geometry,
                freeDrive = freeDrive,
                nextRelocationAt = config.damageWorld.SessionTime + RelocationCooldown
            });
            return true;
        }
        catch (Exception exception) {
            return FailTransaction(request, "police materialization exception: " + exception.Message, populationReserved,
                populationCommitted, activeLife, life, poolAcquired, body, bodyAcquired, receiver, controller);
        }
    }

    /// <summary>Reclaims released wreck bodies and attempts at most one bounded offscreen relocation per tick.</summary>
    /// <param name="activeClock">Current active session clock, reserved for lifecycle diagnostics.</param>
    public void Tick(float activeClock) {
        if (ended || !Finite(activeClock) || !IsClockNonRegressing(lastActiveClock, activeClock)) return;
        bool clockAdvanced = activeClock > lastActiveClock;
        lastActiveClock = activeClock;
        for (int i = wrecks.Count - 1; i >= 0; i--) {
            LivePolice wreck = wrecks[i];
            if (OwnsLife(wreck)) continue;
            wrecks.RemoveAt(i);
            if (wreck.body != null) config.bodyProvider.Release(wreck.body);
        }
        if (!clockAdvanced || !IsRunning) return;
        if (config.navigationMode == PoliceNavigationMode.FreeDrive) {
            TryQueueFreeRelocation(activeClock);
            return;
        }
        if (config.playerBody.linearVelocity.sqrMagnitude <= 0.000001f) return;
        LivePolice selected = null;
        float selectedRemaining = float.MinValue;
        foreach (var police in alive.Values) {
            if (!OwnsLife(police) || police.instance.State != VehicleLifeState.Active ||
                police.controller.IsRamming || police.controller.IsRecovering || police.controller.IsTurnCommitted ||
                !IsBehind(police.body.Body.position) || IsVisible(police)) {
                police.invisibleSince = float.NaN;
                continue;
            }
            if (float.IsNaN(police.invisibleSince)) police.invisibleSince = activeClock;
            if (activeClock - police.invisibleSince < placement.offscreenDwellSeconds || activeClock < police.nextRelocationAt ||
                Vector2.Distance(police.body.Body.position, config.playerBody.position) - police.geometry.ColliderSurfaceRadius <= placement.relocationRadius) continue;
            float remaining = Vector2.Distance(police.body.Body.position, config.playerBody.position) - police.geometry.ColliderSurfaceRadius;
            if (selected == null || remaining > selectedRemaining ||
                (Mathf.Approximately(remaining, selectedRemaining) && police.lifeId < selected.lifeId)) { selected = police; selectedRemaining = remaining; }
        }
        if (selected != null && TryRelocate(selected)) selected.nextRelocationAt = activeClock + RelocationCooldown;
    }

    /// <summary>Releases only police-owned alive bodies, wrecks and reservations.</summary>
    public void End() {
        if (ended) return;
        ended = true;
        foreach (var pending in pendingFreePlacements) {
            pending.planner.Cancel();
            if (pending.source == null) config?.directorRuntime?.Reject(pending.request);
        }
        pendingFreePlacements.Clear();
        if (config != null && config.damageWorld != null) config.damageWorld.VehicleDestroyed -= HandleDestroyed;
        foreach (LivePolice police in new List<LivePolice>(alive.Values)) ReleaseAlive(police);
        alive.Clear();
        foreach (LivePolice wreck in wrecks) ReleaseWreck(wreck);
        wrecks.Clear();
        config?.services?.MarkRoleOwnerCleaned(VehicleRole.Police);
    }

    /// <summary>Releases all police-owned session resources; repeated calls are harmless.</summary>
    public void Dispose() => End();

    void HandleDestroyed(VehicleDestroyedEvent death, float heat) {
        if (ended || !alive.TryGetValue(death.victimLifeId, out LivePolice police)) return;
        alive.Remove(death.victimLifeId);
        if (police.freeDrive != null) freeScheduler.Unregister(police.lifeId);
        police.controller.ResetForNewLife();
        if (police.freeDrive != null) police.freeDrive.Reset();
        police.body.DamageReceiver.ClearContactHistoryForRelocation();
        config.removePoliceContacts?.Invoke(police.lifeId);
        config.directorRuntime.Destroyed(death.victimLifeId);
        wrecks.Add(police);
    }

    bool Reject(PoliceDirectorRuntime.SpawnRequest request, string reason) {
        RecordDiagnostic(reason);
        config?.directorRuntime?.Reject(request);
        return false;
    }

    bool FailTransaction(PoliceDirectorRuntime.SpawnRequest request, string reason, bool populationReserved,
        bool populationCommitted, bool activeLife, NpcVehicleInstance life, bool poolAcquired, PoliceVehicleBody body,
        bool bodyAcquired, VehicleDamageReceiver receiver, PolicePursuitController controller) {
        RecordDiagnostic(reason);
        if (controller != null) controller.ResetForNewLife();
        if (receiver != null) receiver.Unbind();
        if (body != null) body.gameObject.SetActive(false);
        if (populationCommitted) {
            life?.TryMarkWrecked();
            if (config.services.Population.MarkActiveVehicleWrecked(VehicleRole.Police))
                config.services.Population.ReleaseWreck();
        }
        else if (populationReserved && !populationCommitted) config.services.Population.CancelReservation(VehicleRole.Police);
        if (poolAcquired && life != null) config.services.Pool.Release(life);
        if (bodyAcquired && body != null) config.bodyProvider.Release(body);
        config.directorRuntime.Reject(request);
        return false;
    }

    void ReleaseAlive(LivePolice police) {
        if (police.freeDrive != null) freeScheduler.Unregister(police.lifeId);
        config.removePoliceContacts?.Invoke(police.lifeId);
        police.controller.ResetForNewLife();
        police.body.DamageReceiver.Unbind();
        police.body.gameObject.SetActive(false);
        if (OwnsLife(police)) {
            if (police.instance.TryMarkWrecked() && config.services.Population.MarkActiveVehicleWrecked(VehicleRole.Police))
                config.services.Population.ReleaseWreck();
            config.services.Pool.Release(police.instance);
        }
        config.bodyProvider.Release(police.body);
        config.directorRuntime.Destroyed(police.lifeId);
    }

    void ReleaseWreck(LivePolice police) {
        if (police.freeDrive != null) freeScheduler.Unregister(police.lifeId);
        police.controller.ResetForNewLife();
        police.body.DamageReceiver.Unbind();
        police.body.gameObject.SetActive(false);
        if (OwnsLife(police)) {
            config.services.Population.ReleaseWreck();
            config.services.Pool.Release(police.instance);
        }
        config.bodyProvider.Release(police.body);
    }

    PolicePrefabGeometry ReadGeometry(PoliceVehicleBody body) {
        BoxCollider2D collider = body.MainCollider;
        Vector2 scale = body.transform.lossyScale;
        Vector2 footprint = Vector2.Scale(collider.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
        Vector2 offset = Vector2.Scale(collider.offset, scale);
        Vector2 minimum = Vector2.positiveInfinity, maximum = Vector2.negativeInfinity;
        // Renderer local bounds are valid while the pooled body is inactive. Convert all corners
        // to root right/forward axes; world AABBs would change with the body's previous heading.
        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true)) {
            Bounds bounds = renderer.localBounds;
            for (int corner = 0; corner < 8; corner++) {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                Vector2 local = body.transform.InverseTransformPoint(renderer.transform.TransformPoint(point));
                local = Vector2.Scale(local, scale);
                minimum = Vector2.Min(minimum, local); maximum = Vector2.Max(maximum, local);
            }
        }
        return minimum.x == float.PositiveInfinity ? new PolicePrefabGeometry(footprint, offset) :
            new PolicePrefabGeometry(footprint, offset, maximum - minimum, (minimum + maximum) * 0.5f);
    }

    bool ValidateConfig(out string reason) {
        reason = null;
        if (config == null || config.sessionCoordinator == null || config.services == null ||
            (config.navigationMode == PoliceNavigationMode.LegacyRoad && config.services.Graph == null) ||
            config.services.Pool == null || config.services.Population == null || config.damageWorld == null ||
            config.directorRuntime == null || config.bodyProvider == null || config.policeCatalog == null ||
            config.policePrefabCatalog == null || config.navigation == null || config.playerReceiver == null ||
            config.playerBody == null || config.worldClearance == null || config.localStaticClearance == null ||
            config.currentCameraBounds == null || navigationSettings == null || placement == null ||
            (config.navigationMode == PoliceNavigationMode.FreeDrive &&
                (freeChase == null || !freeChase.IsValid(out _) || !variation.IsValid(out _))) ||
            !navigationSettings.IsValid(out _) || !placement.IsValid(out _) || !config.damageWorld.IsActive ||
            config.services.IsClosed || !config.sessionCoordinator.IsActive || !config.directorRuntime.IsEnabled ||
            config.damageWorld.Coordinator != config.sessionCoordinator ||
            config.services.IdentityRegistry != config.sessionCoordinator.IdentityRegistry ||
            !config.sessionCoordinator.PlayerIdentity.HasValue ||
            !config.playerReceiver.IsBoundTo(config.damageWorld, config.sessionCoordinator.PlayerIdentity.Value)) {
            reason = "police population configuration is incomplete";
            return false;
        }
        return true;
    }

    float RelocationCooldown => Mathf.Max(config.directorRuntime.CurrentTier?.relocationCooldown ?? 0f,
        navigationSettings.hardMinimumInterval);

    bool Plan(NpcVehicleProfile profile, PolicePrefabGeometry geometry, bool behind, out PoliceSpawnAcceptedCandidate result) {
        return planner.TryPlan(new PoliceSpawnPlanner.Context {
            navigation = config.navigation, graph = config.services.Graph, mapOriginWorld = config.mapOriginWorld,
            profile = profile, prefabGeometry = geometry, playerWorldPosition = config.playerBody.position,
            playerWorldVelocity = config.playerBody.linearVelocity, cameraWorldBounds = config.currentCameraBounds(),
            navigationSettings = navigationSettings, worldClearance = config.worldClearance,
            localStaticClearance = config.localStaticClearance, playerSurfaceRadius = config.playerSurfaceRadius
        }, new PoliceSpawnPlanner.Config { placementSettings = placement,
            deterministicSeed = config.deterministicSeed, requireBehind = behind }, out result);
    }

    /// <summary>Jam spots remembered for this whole session and shared by every unit's navigation query.</summary>
    public PoliceJamMemory JamMemory { get; } = new PoliceJamMemory(12f);

    PoliceFreeNavigationQuery CreateFreeQuery() {
        PoliceFreeNavigationQuery query = config.freeStaticSource != null
            ? new PoliceFreeNavigationQuery(config.freeStaticSource, config.navigation.localBounds)
            : new PoliceFreeNavigationQuery(config.localStaticClearance, config.navigation.localBounds);
        query.JamMemory = JamMemory;
        return query;
    }

    PoliceFreeAgent CreateFreeDriveState(PoliceVehicleBody body, PoliceVehicleProfile vehicle,
        PoliceBehaviorProfile behavior, PolicePrefabGeometry geometry, int lifeId) {
        if (freeChase == null || !freeChase.IsValid(out _) || !variation.IsValid(out _)) return null;
        PoliceDriverTraits traits = PoliceDriverTraits.CreateForLife(config.deterministicSeed, ++spawnOrdinal,
            vehicle.sharedNpc.vehicleProfileId, lifeId, variation);
        return new PoliceFreeAgent(body, config.damageWorld, config.playerReceiver, config.playerBody,
            vehicle, behavior, geometry, config.mapOriginWorld, CreateFreeQuery(), freeScheduler,
            freeChase, navigationSettings, traits, tactics, config.playerSurfaceRadius);
    }

    sealed class FreePlacementWork {
        public PoliceDirectorRuntime.SpawnRequest request;
        public LivePolice source;
        public NpcVehicleProfile profile;
        public PolicePrefabGeometry geometry;
        public PoliceFreeNavigationQuery query;
        public readonly PoliceFreePathPlanner planner = new PoliceFreePathPlanner();
        public PoliceSpawnPose pose;
        public Vector2 target;
        public int targetLifeId;
        public int candidateIndex;
        public int ordinal;
        public bool searching;
        /// <summary>True for a periodic wave move that places the unit in front of the player rather than behind-only catch-up.</summary>
        public bool aheadWave;
    }

    bool QueueFreeSpawn(PoliceDirectorRuntime.SpawnRequest request) {
        if (!IsRunning || freeChase == null) return Reject(request, "free police owner unavailable");
        if (!config.directorRuntime.IsInFlight(request)) return false;
        foreach (var pending in pendingFreePlacements)
            if (pending.source == null && pending.request.Equals(request)) return false;
        if (!config.policeCatalog.TrySelect(request.vehicleProfileId, request.behaviorProfileId,
            config.policePrefabCatalog, out PoliceVehicleProfile vehicle, out _, out _, out string reason))
            return Reject(request, reason);
        PoliceVehicleBody probe = null;
        try {
            probe = config.bodyProvider.Acquire(vehicle.sharedNpc);
            if (probe == null) return Reject(request, "free placement geometry unavailable");
            pendingFreePlacements.Add(new FreePlacementWork {
                request = request, profile = vehicle.sharedNpc, geometry = ReadGeometry(probe),
                query = CreateFreeQuery(), ordinal = placementOrdinal++
            });
        } catch (Exception exception) {
            return Reject(request, "free placement preparation failed: " + exception.Message);
        } finally {
            if (probe != null) config.bodyProvider.Release(probe);
        }
        // The director's request remains in flight until a bounded reachability query completes.
        return false;
    }

    /// <summary>Prepares free paths once before controller physics ticks, sharing a finite work grant.</summary>
    /// <param name="activeClock">Current monotonic active session time.</param>
    public void PreparePhysics(float activeClock) {
        if (!IsRunning || config.navigationMode != PoliceNavigationMode.FreeDrive ||
            !Finite(activeClock) || activeClock < lastActiveClock) return;
        foreach (var police in alive.Values)
            if (OwnsLife(police) && police.instance.State == VehicleLifeState.Active)
                police.freeDrive?.PreparePlan(activeClock, ++nextFreeRequestId);
        int budget = navigationSettings.workBudget;
        int placementGrant = pendingFreePlacements.Count > 0 ? Mathf.Max(1, budget / (alive.Count + 1)) : 0;
        int used = StepFreePlacement(placementGrant);
        freeScheduler.Step(Mathf.Max(0, budget - used));
        foreach (var police in alive.Values)
            if (OwnsLife(police) && police.instance.State == VehicleLifeState.Active) police.freeDrive?.AdoptResult();
    }

    int StepFreePlacement(int grant) {
        if (grant <= 0 || pendingFreePlacements.Count == 0) return 0;
        FreePlacementWork work = pendingFreePlacements[0];
        if (work.source != null ? !IsSourceEligible(work) :
            !config.directorRuntime.IsInFlight(work.request)) {
            FinishFreePlacement(work, "placement source/request expired");
            return 1;
        }
        if (!work.searching) {
            if (work.candidateIndex >= placement.maxCandidates) {
                FinishFreePlacement(work, "no safe reachable free placement within candidate limit");
                return 1;
            }
            int candidateIndex = work.candidateIndex++;
            if (work.source != null && !TrySampleRoadRelocationPose(work.geometry, candidateIndex, work.ordinal, work.aheadWave, out work.pose)) {
                RecordDiagnostic("free relocation rejected: no safe offscreen road candidate");
                return 1;
            }
            if (work.source == null) work.pose = SampleFreePose(work.geometry, candidateIndex, work.ordinal, false);
            if (!IsFreePoseSafe(work.pose, work.geometry, work.source != null, out string reason)) {
                RecordDiagnostic("free candidate rejected: " + reason);
                return 1;
            }
            work.target = config.playerBody.position;
            work.targetLifeId = config.sessionCoordinator.PlayerIdentity.Value.lifeId;
            work.planner.Begin(new PoliceFreePathPlanner.Request(++nextFreeRequestId,
                work.source != null ? work.source.lifeId : int.MaxValue, work.targetLifeId,
                work.query.GeometryRevision, work.pose.worldPosition - config.mapOriginWorld,
                work.pose.headingDegrees, work.target - config.mapOriginWorld,
                new PoliceFreeNavigationGeometry.Footprint(work.geometry.colliderFootprint, work.geometry.colliderOffset),
                work.query, freeChase.goalRadius, work.profile.motorSettings.minimumTurningRadius,
                turnCost: freeChase.turnPenalty, clearanceMargin: navigationSettings.clearanceMargin));
            work.searching = true;
            return 1;
        }
        int before = work.planner.Current.consumedWork;
        PoliceFreePathPlanner.Result result = work.planner.Step(grant);
        int used = Mathf.Max(1, result.consumedWork - before);
        if (result.status == PoliceFreePathPlanner.Status.Pending) return used;
        work.searching = false;
        if (result.status != PoliceFreePathPlanner.Status.Found) {
            RecordDiagnostic("free reachability: " + result.status + "/" + result.reason);
            return used;
        }
        if (work.targetLifeId != config.sessionCoordinator.PlayerIdentity.Value.lifeId ||
            Vector2.Distance(work.target, config.playerBody.position) > navigationSettings.targetDisplacementThreshold ||
            !IsFreePoseSafe(work.pose, work.geometry, work.source != null, out _)) return used;
        if (work.source == null) {
            pendingFreePlacements.RemoveAt(0);
            Materialize(work.request, work.pose);
            work.planner.Cancel();
        } else if (work.aheadWave) {
            // The wave does not use the behind-only catch-up rules; its own eligibility is rechecked at commit.
            CommitFreeRelocation(work.source, work.pose, true);
            FinishFreePlacement(work, null);
        } else {
            LivePolice source = work.source;
            var destination = new PoliceFreePlacementPlanner.Destination {
                position = work.pose.worldPosition, headingDegrees = work.pose.headingDegrees
            };
            var placementResult = new PoliceFreePlacementPlanner().TryPlan(new PoliceFreePlacementPlanner.Context {
                playerPosition = config.playerBody.position, playerVelocity = RelocationVelocity(),
                playerSurfaceRadius = config.playerSurfaceRadius, cameraBounds = config.currentCameraBounds(),
                cameraMargin = placement.cameraMargin, relocationRadius = placement.relocationRadius,
                minimumDistance = placement.minimumDistance, reactionSeconds = placement.reactionSeconds,
                mapOriginWorld = config.mapOriginWorld, localMapBounds = config.navigation.localBounds,
                noSpawnRegions = config.navigation.noSpawnRegions, worldClearance = config.worldClearance,
                localStaticClearance = config.localStaticClearance, maxCandidates = 1,
                sources = new[] { FreeSource(source, config.damageWorld.SessionTime) },
                destinations = new[] { destination }, reachability = (p, h) => result,
                freshRecheck = (s, d) => IsFreeRelocationEligible(source, config.damageWorld.SessionTime) &&
                    IsFreePoseSafe(work.pose, work.geometry, true, out _)
            });
            if (placementResult.IsFound) CommitFreeRelocation(source, work.pose);
            FinishFreePlacement(work, null);
        }
        return used;
    }

    void FinishFreePlacement(FreePlacementWork work, string reason) {
        work.planner.Cancel();
        pendingFreePlacements.Remove(work);
        if (work.source == null) config.directorRuntime.Reject(work.request);
        if (!string.IsNullOrEmpty(reason)) RecordDiagnostic(reason);
    }

    static readonly float[] AheadWaveAngles = { 0f, 25f, -25f, 50f, -50f };

    PoliceSpawnPose SampleFreePose(PolicePrefabGeometry geometry, int candidateIndex, int ordinal, bool relocating,
        bool aheadOnly = false) {
        Vector2 velocity = RelocationVelocity();
        Vector2 forward = velocity.normalized;
        // Alternate ahead, both sides and rear; subsequent attempts rotate through intermediate headings.
        // A wave move only fans out in front of the player so it becomes an obstacle ahead.
        float angle = aheadOnly ? AheadWaveAngles[(candidateIndex + ordinal) % AheadWaveAngles.Length]
            : (candidateIndex % 4) * 90f + (ordinal % 2) * 45f;
        Vector2 direction = PoliceFreeNavigationGeometry.Rotate(forward, angle);
        Rect camera = config.currentCameraBounds();
        float margin = placement.cameraMargin + geometry.WholeSurfaceRadius + navigationSettings.clearanceMargin;
        Vector2 player = config.playerBody.position;
        float xExit = Mathf.Abs(direction.x) < 0.0001f ? float.PositiveInfinity :
            ((direction.x > 0f ? camera.xMax + margin : camera.xMin - margin) - player.x) / direction.x;
        float yExit = Mathf.Abs(direction.y) < 0.0001f ? float.PositiveInfinity :
            ((direction.y > 0f ? camera.yMax + margin : camera.yMin - margin) - player.y) / direction.y;
        float distance = Mathf.Max(0f, Mathf.Min(xExit, yExit));
        float approach = geometry.ColliderSurfaceRadius + config.playerSurfaceRadius + placement.minimumDistance +
            velocity.magnitude * placement.reactionSeconds;
        distance = Mathf.Max(distance, approach + navigationSettings.clearanceMargin);
        distance += (candidateIndex / 4) * Mathf.Max(geometry.colliderFootprint.magnitude, placement.minimumDistance);
        Vector2 position = player + direction * distance;
        return new PoliceSpawnPose(position, Vector2.SignedAngle(Vector2.up, player - position));
    }

    bool TrySampleRoadRelocationPose(PolicePrefabGeometry geometry, int candidateIndex, int ordinal, bool aheadOnly,
        out PoliceSpawnPose pose) {
        pose = default;
        RoadGraphRuntime graph = config?.services?.Graph;
        if (graph == null || graph.Edges == null || graph.Edges.Count == 0 || navigationSettings == null) return false;
        PoliceSpawnPose desired = SampleFreePose(geometry, candidateIndex, ordinal, true, aheadOnly);
        var buildBudget = new RoadPathQuery.SearchBudget(Mathf.Max(65536, navigationSettings.workBudget));
        if (!RoadSegmentSpatialIndex.TryGetOrBuild(graph, navigationSettings.indexCellSize, buildBudget,
            out RoadSegmentSpatialIndex index, out _)) return false;
        var query = index.QueryAll(desired.worldPosition - config.mapOriginWorld,
            new RoadPathQuery.SearchBudget(Mathf.Max(65536, navigationSettings.workBudget)));
        if (query.status != RoadSegmentSpatialIndex.QueryStatus.Found || query.candidates == null) return false;
        float minimumWidth = geometry.colliderFootprint.x + navigationSettings.clearanceMargin * 2f;
        foreach (RoadSegmentSpatialIndex.ProjectionCandidate candidate in query.candidates) {
            RoadEdgeRecord edge = graph.GetEdge(candidate.edgeId);
            if (edge == null || (edge.allowedRoles != null && edge.allowedRoles.Count > 0 &&
                !edge.allowedRoles.Contains(VehicleRole.Police)) || edge.usableWidth < minimumWidth) continue;
            PoliceSpawnPose roadPose = new PoliceSpawnPose(candidate.point + config.mapOriginWorld,
                MapNavigationCoordinates.DirectionToHeadingDegrees(candidate.direction));
            if (!IsFreePoseSafe(roadPose, geometry, true, out _)) continue;
            if (aheadOnly && IsBehind(roadPose.worldPosition)) continue; // the nearest road must still be in front
            pose = roadPose;
            return true;
        }
        return false;
    }

    bool IsFreePoseSafe(PoliceSpawnPose pose, PolicePrefabGeometry geometry, bool relocating, out string reason) {
        reason = null;
        if (relocating && Vector2.Distance(pose.worldPosition, config.playerBody.position) + geometry.WholeSurfaceRadius >= placement.relocationRadius) {
            reason = "destination outside management radius";
            return false;
        }
        var candidate = new PoliceSpawnCandidate {
            position = pose.worldPosition, headingDegrees = pose.headingDegrees, footprint = geometry.colliderFootprint,
            localPosition = pose.worldPosition - config.mapOriginWorld, hasLocalPosition = true
        };
        return PoliceSpawnSafetyPolicy.IsSafe(candidate, new PoliceSpawnSafetyContext {
            cameraBounds = config.currentCameraBounds(), cameraMargin = placement.cameraMargin,
            playerPosition = config.playerBody.position, playerVelocity = RelocationVelocity(),
            playerSurfaceRadius = config.playerSurfaceRadius, minimumDistance = placement.minimumDistance,
            reactionSeconds = placement.reactionSeconds, mapOriginWorld = config.mapOriginWorld,
            localMapBounds = config.navigation.localBounds, noSpawnRegions = config.navigation.noSpawnRegions,
            geometry = geometry, worldClearance = config.worldClearance, localStaticClearance = config.localStaticClearance
        }, out reason);
    }

    PoliceFreePlacementPlanner.Source FreeSource(LivePolice police, float clock) =>
        new PoliceFreePlacementPlanner.Source {
            lifeId = police.lifeId, position = police.body.Body.position, headingDegrees = police.body.Body.rotation,
            geometry = police.geometry, isLive = OwnsLife(police) && police.instance.State == VehicleLifeState.Active,
            isBehind = IsBehind(police.body.Body.position),
            offscreenDwellComplete = !float.IsNaN(police.invisibleSince) && clock - police.invisibleSince >= placement.offscreenDwellSeconds,
            relocationCooldownComplete = clock >= police.nextRelocationAt,
            // FreeDrive obstacle recovery is precisely the case relocation must rescue. Keep only
            // active player rams and the brief native crash window as hard source blockers.
            hasContactOrArrestOrRecoveryOrRamming = police.controller.IsRamming || police.body.Motor.IsCrashMode
        };

    bool IsFreeRelocationEligible(LivePolice police, float clock) => PoliceFreePlacementPlanner.IsEligibleSource(
        FreeSource(police, clock), new PoliceFreePlacementPlanner.Context {
            playerPosition = config.playerBody.position, playerVelocity = config.playerBody.linearVelocity,
            cameraBounds = config.currentCameraBounds(), cameraMargin = placement.cameraMargin,
            relocationRadius = placement.relocationRadius
        });

    bool IsSourceEligible(FreePlacementWork work) => work.aheadWave
        ? IsAheadWaveEligible(work.source)
        : IsFreeRelocationEligible(work.source, config.damageWorld.SessionTime);

    /// <summary>
    /// A wave may move any living, fully offscreen unit that is not busy ramming or recovering from a
    /// crash, regardless of how far behind it is. Visible units are never moved.
    /// </summary>
    bool IsAheadWaveEligible(LivePolice police) =>
        police != null && OwnsLife(police) && police.instance.State == VehicleLifeState.Active &&
        !IsVisible(police) && !police.controller.IsRamming && !police.body.Motor.IsCrashMode;

    /// <summary>
    /// Every <see cref="PolicePlacementSettings.aheadWaveIntervalSeconds"/>, moves a share (at least one) of the
    /// offscreen police to an offscreen road point in front of the player, so driving straight away
    /// from the pack does not leave the road ahead empty. Units behind the player go first, farthest
    /// first; units already ahead are left where they are. Each move still passes the normal road,
    /// camera, clearance and reachability checks before it is committed.
    /// </summary>
    bool TryQueueAheadWave(float clock) {
        if (placement.aheadWaveIntervalSeconds <= 0f || placement.aheadWaveShare <= 0f) return false;
        if (clock < nextAheadWaveAt || pendingFreePlacements.Count > 0) return false;
        nextAheadWaveAt = clock + placement.aheadWaveIntervalSeconds;
        aheadWaveScratch.Clear();
        int offscreen = 0;
        foreach (var police in alive.Values) {
            if (!IsAheadWaveEligible(police)) continue;
            offscreen++;
            if (IsBehind(police.body.Body.position)) aheadWaveScratch.Add(police);
        }
        if (offscreen == 0 || aheadWaveScratch.Count == 0) return false;
        int share = Mathf.Max(1, Mathf.FloorToInt(offscreen * placement.aheadWaveShare));
        Vector2 player = config.playerBody.position;
        aheadWaveScratch.Sort((a, b) => {
            int byDistance = Vector2.SqrMagnitude(b.body.Body.position - player).CompareTo(Vector2.SqrMagnitude(a.body.Body.position - player));
            return byDistance != 0 ? byDistance : a.lifeId.CompareTo(b.lifeId);
        });
        int count = Mathf.Min(share, aheadWaveScratch.Count);
        for (int i = 0; i < count; i++) {
            LivePolice selected = aheadWaveScratch[i];
            pendingFreePlacements.Add(new FreePlacementWork {
                source = selected, profile = selected.vehicle.sharedNpc, geometry = selected.geometry,
                query = CreateFreeQuery(), ordinal = placementOrdinal++, aheadWave = true
            });
        }
        return true;
    }

    void TryQueueFreeRelocation(float clock) {
        if (TryQueueAheadWave(clock)) return;
        LivePolice selected = null;
        float farthest = 0f;
        foreach (var police in alive.Values) {
            if (!OwnsLife(police) || !IsBehind(police.body.Body.position) || IsVisible(police)) {
                police.invisibleSince = float.NaN;
                continue;
            }
            if (float.IsNaN(police.invisibleSince)) police.invisibleSince = clock;
            if (clock < nextFreeRelocationAt || pendingFreePlacements.Count > 0 || !IsFreeRelocationEligible(police, clock)) continue;
            float distance = Vector2.Distance(police.body.Body.position, config.playerBody.position);
            if (selected == null || distance > farthest || (distance == farthest && police.lifeId < selected.lifeId)) {
                selected = police;
                farthest = distance;
            }
        }
        if (selected == null) return;
        nextFreeRelocationAt = clock + Mathf.Max(RelocationCooldown, navigationSettings.refreshInterval);
        pendingFreePlacements.Add(new FreePlacementWork {
            source = selected, profile = selected.vehicle.sharedNpc, geometry = selected.geometry,
            query = CreateFreeQuery(), ordinal = placementOrdinal++
        });
    }

    bool CommitFreeRelocation(LivePolice police, PoliceSpawnPose pose, bool aheadWave = false) {
        Vector2 oldPosition = police.body.Body.position;
        float oldRotation = police.body.Body.rotation;
        Vector2 oldVelocity = police.body.Body.linearVelocity;
        float oldAngularVelocity = police.body.Body.angularVelocity;
        bool committed = PoliceFreePlacementPlanner.TryCommitAtomic(
            () => (aheadWave ? IsAheadWaveEligible(police) : IsFreeRelocationEligible(police, config.damageWorld.SessionTime)) &&
                IsFreePoseSafe(pose, police.geometry, true, out _) && police.controller.TryPrepareRelocation(),
            () => {
                police.body.Body.position = pose.worldPosition;
                police.body.Body.rotation = pose.headingDegrees;
                police.body.Body.linearVelocity = Vector2.zero;
                police.body.Body.angularVelocity = 0f;
            },
            () => OwnsLife(police) && police.controller.TrySetTarget(config.playerReceiver, out _),
            () => {
                police.body.Body.position = oldPosition;
                police.body.Body.rotation = oldRotation;
                police.body.Body.linearVelocity = oldVelocity;
                police.body.Body.angularVelocity = oldAngularVelocity;
            });
        if (!committed) return false;
        config.removePoliceContacts?.Invoke(police.lifeId);
        police.body.DamageReceiver.ClearContactHistoryForRelocation();
        police.body.DamageReceiver.CaptureBeforePhysics();
        police.invisibleSince = float.NaN;
        police.nextRelocationAt = config.damageWorld.SessionTime + RelocationCooldown;
        return true;
    }

    bool IsBehind(Vector2 position) => Vector2.Dot(position - config.playerBody.position, RelocationVelocity()) < 0f;

    Vector2 RelocationVelocity() {
        Vector2 velocity = config.playerBody.linearVelocity;
        if (velocity.sqrMagnitude > 0.000001f) return velocity;
        Vector2 forward = config.playerBody.transform.up;
        return forward.sqrMagnitude > 0.000001f ? forward.normalized * 0.01f : Vector2.up * 0.01f;
    }

    bool IsVisible(LivePolice police) => PoliceSpawnSafetyPolicy.IsWholeFootprintVisible(
        new PoliceSpawnCandidate { position = police.body.Body.position, headingDegrees = police.body.Body.rotation },
        config.currentCameraBounds(), placement.cameraMargin, police.geometry);

    bool TryRelocate(LivePolice police) {
        int rearCount = 0;
        foreach (var other in alive.Values)
            if (OwnsLife(other) && IsBehind(other.body.Body.position)) rearCount++;
        bool keepBehind = rearCount <= placement.keepRearPursuers;
        if (!Plan(police.vehicle.sharedNpc, police.geometry, keepBehind, out var candidate) ||
            candidate.graphVersion != config.services.Graph.Version || IsVisible(police) ||
            !police.controller.TryPrepareRelocation()) return false;
        config.removePoliceContacts?.Invoke(police.lifeId);
        police.body.DamageReceiver.ClearContactHistoryForRelocation();
        police.body.Body.linearVelocity = Vector2.zero;
        police.body.Body.angularVelocity = 0f;
        police.body.Body.position = candidate.pose.worldPosition;
        police.body.Body.rotation = candidate.pose.headingDegrees;
        police.body.DamageReceiver.CaptureBeforePhysics();
        police.invisibleSince = float.NaN;
        return true;
    }

    static bool OwnsLife(LivePolice police) => police.instance.Identity.HasValue &&
        police.instance.Identity.Value.lifeId == police.lifeId;

    void RecordDiagnostic(string reason) {
        if (diagnostics.Count == 16) diagnostics.RemoveAt(0);
        diagnostics.Add(reason);
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    internal static bool IsClockNonRegressing(float previous, float current) =>
        Finite(previous) && Finite(current) && current >= previous;
}
