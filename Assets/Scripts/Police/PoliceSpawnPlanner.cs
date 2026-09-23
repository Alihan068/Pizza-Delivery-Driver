using System.Collections.Generic;
using UnityEngine;

/// <summary>Immutable world pose returned by the police placement planner.</summary>
public readonly struct PoliceSpawnPose {
    /// <summary>World-space root pivot position.</summary>
    public readonly Vector2 worldPosition;
    /// <summary>World heading using the map's counter-clockwise-from-up convention.</summary>
    public readonly float headingDegrees;

    /// <summary>Creates a detached police spawn pose.</summary>
    public PoliceSpawnPose(Vector2 worldPosition, float headingDegrees) {
        this.worldPosition = worldPosition;
        this.headingDegrees = headingDegrees;
    }
}

/// <summary>Immutable copy of the route prepared by <see cref="PoliceRoadTargetQuery"/>.</summary>
public sealed class PolicePreparedRoute {
    readonly IReadOnlyList<RoadPathQuery.PathSpan> spans;

    /// <summary>Graph version against which the spans were validated.</summary>
    public readonly long graphVersion;
    /// <summary>Map-local road point projected for the actual player target.</summary>
    public readonly Vector2 roadTarget;
    /// <summary>Ordered directed spans ready for controller binding.</summary>
    public IReadOnlyList<RoadPathQuery.PathSpan> Spans => spans;
    /// <summary>Whether a displaced connector precedes the road spans.</summary>
    public readonly bool requiresConnector;
    /// <summary>Whether a static-clear final approach was proven.</summary>
    public readonly bool hasFinalApproach;
    /// <summary>Final approach map-local start point.</summary>
    public readonly Vector2 finalApproachStart;
    /// <summary>Final approach map-local end point.</summary>
    public readonly Vector2 finalApproachEnd;

    /// <summary>Copies a successful route result so later query scratch state cannot mutate the plan.</summary>
    public PolicePreparedRoute(PoliceRoadTargetQuery.Result result) {
        graphVersion = result == null ? 0L : result.graphVersion;
        roadTarget = result == null ? Vector2.zero : result.roadTarget;
        requiresConnector = result != null && result.requiresConnector;
        hasFinalApproach = result != null && result.hasFinalApproach;
        finalApproachStart = result == null ? Vector2.zero : result.finalApproachStart;
        finalApproachEnd = result == null ? Vector2.zero : result.finalApproachEnd;
        var copy = result == null || result.spans == null ? new List<RoadPathQuery.PathSpan>() :
            new List<RoadPathQuery.PathSpan>(result.spans);
        spans = copy.AsReadOnly();
    }
}

/// <summary>
/// Immutable accepted output from one bounded police placement attempt. It contains no pool,
/// Rigidbody, controller, teleport or population side effect and is safe to hand to the materializer.
/// </summary>
public sealed class PoliceSpawnAcceptedCandidate {
    /// <summary>Authored police entry that selected this candidate.</summary>
    public readonly string entryId;
    /// <summary>Exact directed edge anchor resolved from the authored spawnId.</summary>
    public readonly RoadPathQuery.EdgeAnchor edgeAnchor;
    /// <summary>World-space placement pose.</summary>
    public readonly PoliceSpawnPose pose;
    /// <summary>Route prepared from the candidate anchor to the actual player.</summary>
    public readonly PolicePreparedRoute preparedRoute;
    /// <summary>Graph version used by both anchor resolution and route preparation.</summary>
    public readonly long graphVersion;

    /// <summary>Creates a detached accepted candidate.</summary>
    public PoliceSpawnAcceptedCandidate(string entryId, RoadPathQuery.EdgeAnchor edgeAnchor,
        PoliceSpawnPose pose, PolicePreparedRoute preparedRoute, long graphVersion) {
        this.entryId = entryId;
        this.edgeAnchor = edgeAnchor;
        this.pose = pose;
        this.preparedRoute = preparedRoute;
        this.graphVersion = graphVersion;
    }
}

/// <summary>Bounded authored-entry police placement planner with no materialization side effects.</summary>
public sealed class PoliceSpawnPlanner {
    /// <summary>All detached world/map data required for one attempt; coordinates are converted exactly once.</summary>
    public sealed class Context {
        /// <summary>Authored navigation document containing police entries and spawn records.</summary>
        public MapNavigationDocument navigation;
        /// <summary>Indexed graph built from the same navigation document.</summary>
        public RoadGraphRuntime graph;
        /// <summary>World translation of the map-local navigation origin.</summary>
        public Vector2 mapOriginWorld;
        /// <summary>Exact NPC profile used for route width and turning-radius constraints.</summary>
        public NpcVehicleProfile profile;
        /// <summary>Scaled collider and visual bounds read from the selected police prefab.</summary>
        public PolicePrefabGeometry prefabGeometry;
        /// <summary>Current player world position.</summary>
        public Vector2 playerWorldPosition;
        /// <summary>Current player world velocity.</summary>
        public Vector2 playerWorldVelocity;
        /// <summary>Current finite world camera rectangle.</summary>
        public Rect cameraWorldBounds;
        /// <summary>Current bounded route-query settings snapshot.</summary>
        public PoliceNavigationSettings navigationSettings;
        /// <summary>Fresh static-plus-dynamic world clearance query with saturation fail-closed.</summary>
        public IAreaClearanceQuery worldClearance;
        /// <summary>Fresh map-local static clearance query with saturation fail-closed.</summary>
        public IAreaClearanceQuery localStaticClearance;
        /// <summary>Player pivot-to-surface radius used by reaction spacing.</summary>
        public float playerSurfaceRadius;
        /// <summary>Current player collider footprint used to derive a conservative surface radius.</summary>
        public Vector2 playerFootprint;
    }

    /// <summary>Per-attempt planner controls copied before any authored candidate is inspected.</summary>
    public sealed class Config {
        /// <summary>Validated bounded placement tuning snapshot.</summary>
        public PolicePlacementSettings placementSettings;
        /// <summary>Deterministic session seed mixed with the rotating authored-entry cursor.</summary>
        public int deterministicSeed;
        /// <summary>When true, only entries behind the player's current velocity are eligible.</summary>
        public bool requireBehind;
    }

    int rotatingCursor;

    /// <summary>Last bounded failure reason; no partial candidate is retained.</summary>
    public string LastFailureReason { get; private set; }

    /// <summary>
    /// Plans at most placementSettings.maxCandidates authored entries. One SearchBudget is created
    /// for the entire attempt and is never reset for a fallback candidate.
    /// </summary>
    /// <param name="context">Explicit graph, profile, camera, player and fresh-query context.</param>
    /// <param name="config">Placement limits, deterministic seed and rear-quota filter for this attempt.</param>
    /// <param name="accepted">Detached accepted candidate, or null when the caller should wait.</param>
    /// <returns>True only when a safe candidate and complete player route were both prepared.</returns>
    public bool TryPlan(Context context, Config config, out PoliceSpawnAcceptedCandidate accepted) {
        accepted = null;
        LastFailureReason = null;
        if (!ValidateInput(context, config, out string validationReason)) return Fail(validationReason);

        var navigationSettings = context.navigationSettings.Clone();
        var placementSettings = config.placementSettings.Clone();
        if (!navigationSettings.IsValid(out string navigationReason)) return Fail(navigationReason);
        if (!placementSettings.IsValid(out string placementReason)) return Fail(placementReason);

        var budget = new RoadPathQuery.SearchBudget(navigationSettings.workBudget);
        var entries = new List<PoliceEntryRecord>();
        foreach (var entry in context.navigation.policeEntries) {
            if (!budget.TryConsume(1)) return Fail("placement entry scan budget exhausted");
            if (entry != null && !string.IsNullOrEmpty(entry.entryId)) entries.Add(entry);
        }
        if (entries.Count == 0) return Fail("no authored police entries");

        int seedOffset = (int)(unchecked((uint)config.deterministicSeed) % (uint)entries.Count);
        int start = (rotatingCursor + seedOffset) % entries.Count;
        int limit = Mathf.Min(placementSettings.maxCandidates, entries.Count);
        bool hasPlayerDirection = context.playerWorldVelocity.sqrMagnitude > 0.000001f;
        if (config.requireBehind && !hasPlayerDirection) return Fail("rear quota requires a nonzero player velocity");
        Vector2 playerLocal = MapNavigationCoordinates.WorldToLocal(context.playerWorldPosition, context.mapOriginWorld);

        for (int inspected = 0; inspected < limit; inspected++) {
            int entryIndex = (start + inspected) % entries.Count;
            rotatingCursor = (entryIndex + 1) % entries.Count;
            if (!budget.TryConsume(1)) return Fail("placement search budget exhausted");
            var entry = entries[entryIndex];
            if (!AllowsPolice(entry)) continue;
            if (!TryFindSpawn(context.navigation.spawnPoints, entry.spawnId, budget, out var spawn)) {
                if (budget.IsExhausted) return Fail("placement spawn scan budget exhausted");
                continue;
            }
            if (!IsAuthoredSpawnCompatible(spawn, context.profile, context.prefabGeometry)) continue;
            if (!RoadAnchorGeometry.TryGetPoint(context.graph, spawn.edgeId, spawn.distanceAlongEdge, budget,
                out Vector2 localPosition, out Vector2 direction)) {
                if (budget.IsExhausted) return Fail("placement search budget exhausted");
                continue;
            }
            float heading = MapNavigationCoordinates.DirectionToHeadingDegrees(direction);
            Vector2 worldPosition = MapNavigationCoordinates.LocalToWorld(localPosition, context.mapOriginWorld);
            if (config.requireBehind && Vector2.Dot(worldPosition - context.playerWorldPosition, context.playerWorldVelocity.normalized) >= 0f) continue;

            var candidate = new PoliceSpawnCandidate { entryId = entry.entryId, position = worldPosition,
                footprint = context.prefabGeometry.colliderFootprint, headingDegrees = heading,
                localPosition = localPosition, hasLocalPosition = true };
            var safety = new PoliceSpawnSafetyContext {
                cameraBounds = context.cameraWorldBounds, cameraMargin = placementSettings.cameraMargin,
                playerPosition = context.playerWorldPosition, playerVelocity = context.playerWorldVelocity,
                playerFootprint = context.playerFootprint, playerSurfaceRadius = context.playerSurfaceRadius,
                minimumDistance = placementSettings.minimumDistance,
                reactionSeconds = placementSettings.reactionSeconds, mapOriginWorld = context.mapOriginWorld,
                localMapBounds = context.navigation.localBounds, noSpawnRegions = context.navigation.noSpawnRegions,
                geometry = context.prefabGeometry, worldClearance = context.worldClearance,
                localStaticClearance = context.localStaticClearance
            };
            if (!PoliceSpawnSafetyPolicy.IsSafe(candidate, safety, out _)) continue;

            var targetInput = new PoliceRoadTargetQuery.Input {
                graph = context.graph, navigation = context.navigation, profile = context.profile,
                policePosition = localPosition, policeDirection = direction, targetPosition = playerLocal,
                localColliderOffset = context.prefabGeometry.colliderOffset, hasKnownAnchor = true,
                knownAnchor = new RoadPathQuery.EdgeAnchor(spawn.edgeId, spawn.distanceAlongEdge),
                staticClearance = context.localStaticClearance, settings = navigationSettings, budget = budget,
                targetKind = PoliceRoadTargetQuery.TargetKind.ActualPlayer
            };
            if (!PoliceRoadTargetQuery.TryQuery(targetInput, out var route)) {
                if (budget.IsExhausted || route.status == PoliceRoadTargetQuery.Status.BudgetExceeded)
                    return Fail("placement route search budget exhausted");
                continue;
            }

            // Recheck after route preparation: route validation may have consumed the last mutable world snapshot.
            if (!PoliceSpawnSafetyPolicy.IsSafe(candidate, safety, out _)) continue;
            var prepared = new PolicePreparedRoute(route);
            accepted = new PoliceSpawnAcceptedCandidate(entry.entryId,
                new RoadPathQuery.EdgeAnchor(spawn.edgeId, spawn.distanceAlongEdge),
                new PoliceSpawnPose(worldPosition, heading), prepared, context.graph.Version);
            return true;
        }
        return Fail(config.requireBehind ? "no eligible rear police entry" : "no safe reachable police entry");
    }

    static bool ValidateInput(Context input, Config config, out string reason) {
        reason = null;
        if (input == null || input.navigation == null || input.graph == null || input.profile == null ||
            input.prefabGeometry == null || !input.prefabGeometry.IsValid || input.navigationSettings == null ||
            config == null || config.placementSettings == null || input.worldClearance == null || input.localStaticClearance == null ||
            !Finite(input.mapOriginWorld) || !Finite(input.playerWorldPosition) || !Finite(input.playerWorldVelocity) ||
            !Finite(input.playerFootprint) || !ValidOptionalFootprint(input.playerFootprint) ||
            !IsValidRect(input.cameraWorldBounds) || !IsValidRect(input.navigation.localBounds) ||
            input.navigation.policeEntries == null || input.navigation.spawnPoints == null ||
            input.profile.allowedRoles == null || !input.profile.allowedRoles.Contains(VehicleRole.Police) ||
            input.profile.motorSettings == null || !FinitePositive(input.profile.colliderSize.x) ||
            !FinitePositive(input.profile.colliderSize.y) || !FiniteNonnegative(input.playerSurfaceRadius)) {
            reason = "invalid police placement input";
            return false;
        }
        return true;
    }

    static bool AllowsPolice(PoliceEntryRecord entry) {
        return entry.allowedRoles != null && entry.allowedRoles.Contains(VehicleRole.Police);
    }

    static bool TryFindSpawn(IReadOnlyList<VehicleSpawnRecord> spawns, string spawnId,
        RoadPathQuery.SearchBudget budget, out VehicleSpawnRecord found) {
        found = null;
        if (spawns == null || string.IsNullOrEmpty(spawnId) || budget == null) return false;
        foreach (var spawn in spawns) {
            if (!budget.TryConsume(1)) return false;
            if (spawn != null && spawn.spawnId == spawnId) {
                found = spawn;
                return true;
            }
        }
        return false;
    }

    static bool IsAuthoredSpawnCompatible(VehicleSpawnRecord spawn, NpcVehicleProfile profile,
        PolicePrefabGeometry geometry) {
        if (spawn == null || profile == null || geometry == null || spawn.role != VehicleRole.Police ||
            !FinitePositive(spawn.clearanceWidth) || !FinitePositive(spawn.clearanceLength)) return false;
        return geometry.colliderFootprint.x <= spawn.clearanceWidth && geometry.colliderFootprint.y <= spawn.clearanceLength &&
            profile.colliderSize.x <= spawn.clearanceWidth && profile.colliderSize.y <= spawn.clearanceLength;
    }
    bool Fail(string reason) { LastFailureReason = reason; return false; }
    static bool IsValidRect(Rect value) => Finite(value.position) && Finite(value.size) && Finite(value.xMax) && Finite(value.yMax) && value.width > 0f && value.height > 0f;
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
    static bool ValidOptionalFootprint(Vector2 value) => value == Vector2.zero ||
        FinitePositive(value.x) && FinitePositive(value.y);
}
