using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Graph-independent, typed placement planner for police relocation and free-drive spawn.</summary>
public sealed class PoliceFreePlacementPlanner {
    /// <summary>Outcome of a bounded placement attempt.</summary>
    public enum Status { Found, Pending, NoEligibleSource, Rejected }

    /// <summary>Why a placement attempt did not produce a teleportable result.</summary>
    public enum Reason { None, PlayerStopped, NoEligibleSource, NoSafeDestination, PathPending, PathUnavailable, Stale, InvalidInput }

    /// <summary>Live police source facts frozen for one planning attempt.</summary>
    public sealed class Source {
        /// <summary>Stable life identity used as the deterministic tie-breaker.</summary>
        public int lifeId;
        /// <summary>Current world pivot.</summary>
        public Vector2 position;
        /// <summary>Current world heading.</summary>
        public float headingDegrees;
        /// <summary>Full source geometry.</summary>
        public PolicePrefabGeometry geometry;
        /// <summary>Whether the source is a live active police body.</summary>
        public bool isLive = true;
        /// <summary>Whether the source is behind the moving player.</summary>
        public bool isBehind;
        /// <summary>Whether offscreen dwell has completed.</summary>
        public bool offscreenDwellComplete;
        /// <summary>Whether the unit relocation cooldown has completed.</summary>
        public bool relocationCooldownComplete;
        /// <summary>Whether the unit is in contact, arrest, recovery or ramming state.</summary>
        public bool hasContactOrArrestOrRecoveryOrRamming;
    }

    /// <summary>Destination facts supplied by the free placement source.</summary>
    public sealed class Destination {
        /// <summary>Candidate world pivot.</summary>
        public Vector2 position;
        /// <summary>Candidate heading.</summary>
        public float headingDegrees;
        /// <summary>Stable deterministic destination key.</summary>
        public int order;
    }

    /// <summary>Fresh source and destination queries used by one bounded attempt.</summary>
    public sealed class Context {
        /// <summary>Player world pivot.</summary>
        public Vector2 playerPosition;
        /// <summary>Current player world velocity.</summary>
        public Vector2 playerVelocity;
        /// <summary>Actual camera rectangle for the current frame.</summary>
        public Rect cameraBounds;
        /// <summary>Actual camera visibility margin.</summary>
        public float cameraMargin;
        /// <summary>Frozen authored relocation radius.</summary>
        public float relocationRadius;
        /// <summary>Minimum player-to-police distance before reaction spacing.</summary>
        public float minimumDistance;
        /// <summary>Reaction time used to reserve high-speed spacing.</summary>
        public float reactionSeconds;
        /// <summary>Player surface radius.</summary>
        public float playerSurfaceRadius;
        /// <summary>Player footprint when its surface radius is not authored.</summary>
        public Vector2 playerFootprint;
        /// <summary>World origin of the local map.</summary>
        public Vector2 mapOriginWorld;
        /// <summary>Map-local bounds for the full candidate envelope.</summary>
        public Rect localMapBounds;
        /// <summary>Authored no-spawn regions.</summary>
        public IReadOnlyList<NoSpawnRegion> noSpawnRegions;
        /// <summary>Fresh static-plus-dynamic destination clearance.</summary>
        public IAreaClearanceQuery worldClearance;
        /// <summary>Fresh static destination clearance.</summary>
        public IAreaClearanceQuery localStaticClearance;
        /// <summary>Live police sources, normally ordered by the caller only for allocation control.</summary>
        public IReadOnlyList<Source> sources;
        /// <summary>Graph-independent destination candidates.</summary>
        public IReadOnlyList<Destination> destinations;
        /// <summary>Fresh free-drive reachability check. Null means defer.</summary>
        public Func<Vector2, float, PoliceFreePathPlanner.Result> reachability;
        /// <summary>Final source/destination recheck immediately before commit.</summary>
        public Func<Source, Destination, bool> freshRecheck;
        /// <summary>Maximum candidates inspected in this attempt.</summary>
        public int maxCandidates = 8;
    }

    /// <summary>Detached accepted placement result; no body or population side effect occurs here.</summary>
    public sealed class Result {
        /// <summary>Creates a result.</summary>
        public Result(Status status, Reason reason, Source source, Destination destination) {
            this.status = status; this.reason = reason; this.source = source; this.destination = destination;
        }
        /// <summary>Attempt status.</summary>
        public readonly Status status;
        /// <summary>Attempt reason.</summary>
        public readonly Reason reason;
        /// <summary>Selected source, only for Found.</summary>
        public readonly Source source;
        /// <summary>Selected destination, only for Found.</summary>
        public readonly Destination destination;
        /// <summary>Whether this result authorizes a caller to continue to its atomic commit.</summary>
        public bool IsFound => status == Status.Found && source != null && destination != null;
    }

    /// <summary>Plans one deterministic graphless placement attempt.</summary>
    public Result TryPlan(Context context) {
        if (!IsValid(context)) return new Result(Status.Rejected, Reason.InvalidInput, null, null);
        if (context.playerVelocity.sqrMagnitude <= 0.000001f)
            return new Result(Status.Rejected, Reason.PlayerStopped, null, null);

        var eligible = new List<Source>();
        for (int i = 0; i < context.sources.Count; i++) {
            Source source = context.sources[i];
            if (IsEligibleSource(source, context)) eligible.Add(source);
        }
        eligible.Sort((a, b) => {
            float aRemaining = RemainingDistance(a, context);
            float bRemaining = RemainingDistance(b, context);
            int distance = bRemaining.CompareTo(aRemaining);
            return distance != 0 ? distance : a.lifeId.CompareTo(b.lifeId);
        });
        if (eligible.Count == 0) return new Result(Status.NoEligibleSource, Reason.NoEligibleSource, null, null);

        int inspected = 0;
        for (int sourceIndex = 0; sourceIndex < eligible.Count && inspected < context.maxCandidates; sourceIndex++) {
            Source source = eligible[sourceIndex];
            for (int destinationIndex = 0; destinationIndex < context.destinations.Count && inspected < context.maxCandidates; destinationIndex++) {
                inspected++;
                Destination destination = context.destinations[destinationIndex];
                if (destination == null || !Finite(destination.position) || !Finite(destination.headingDegrees)) continue;
                var candidate = new PoliceSpawnCandidate { position = destination.position, headingDegrees = destination.headingDegrees,
                    footprint = source.geometry.colliderFootprint, localPosition = MapNavigationCoordinates.WorldToLocal(destination.position, context.mapOriginWorld), hasLocalPosition = true };
                var safety = new PoliceSpawnSafetyContext { cameraBounds = context.cameraBounds, cameraMargin = context.cameraMargin,
                    playerPosition = context.playerPosition, playerVelocity = context.playerVelocity, playerFootprint = context.playerFootprint,
                    playerSurfaceRadius = context.playerSurfaceRadius, minimumDistance = context.minimumDistance, reactionSeconds = context.reactionSeconds,
                    mapOriginWorld = context.mapOriginWorld, localMapBounds = context.localMapBounds, noSpawnRegions = context.noSpawnRegions,
                    geometry = source.geometry, worldClearance = context.worldClearance, localStaticClearance = context.localStaticClearance };
                if (!PoliceSpawnSafetyPolicy.IsSafe(candidate, safety, out _)) continue;
                if (Vector2.Distance(destination.position, context.playerPosition) + source.geometry.WholeSurfaceRadius >= context.relocationRadius) continue;
                if (context.reachability == null) return new Result(Status.Pending, Reason.PathPending, null, null);
                PoliceFreePathPlanner.Result path = context.reachability(destination.position, destination.headingDegrees);
                if (path == null || path.status == PoliceFreePathPlanner.Status.Pending) return new Result(Status.Pending, Reason.PathPending, null, null);
                if (path.status != PoliceFreePathPlanner.Status.Found) continue;
                if (context.freshRecheck != null && !context.freshRecheck(source, destination)) return new Result(Status.Pending, Reason.Stale, null, null);
                return new Result(Status.Found, Reason.None, source, destination);
            }
        }
        return new Result(Status.Rejected, Reason.NoSafeDestination, null, null);
    }

    /// <summary>Commits a prepared relocation atomically and invokes rollback when binding fails.</summary>
    public static bool TryCommitAtomic(Func<bool> prepare, Action apply, Func<bool> bind, Action rollback) {
        if (prepare == null || apply == null || bind == null || !prepare()) return false;
        try {
            apply();
            if (bind()) return true;
        } catch {
            rollback?.Invoke();
            throw;
        }
        rollback?.Invoke();
        return false;
    }

    /// <summary>Checks the strict source-side relocation gates before any destination query.</summary>
    public static bool IsEligibleSource(Source source, Context context) {
        if (source == null || context == null || !source.isLive || !source.isBehind || !source.offscreenDwellComplete ||
            !source.relocationCooldownComplete || source.hasContactOrArrestOrRecoveryOrRamming || source.geometry == null || !source.geometry.IsValid) return false;
        if (context.playerVelocity.sqrMagnitude <= 0.000001f || !PoliceSpawnSafetyPolicy.IsValidReferenceRadius(context.relocationRadius)) return false;
        if (PoliceSpawnSafetyPolicy.IsWholeFootprintVisible(new PoliceSpawnCandidate { position = source.position, headingDegrees = source.headingDegrees },
            context.cameraBounds, context.cameraMargin, source.geometry)) return false;
        return RemainingDistance(source, context) > context.relocationRadius;
    }

    static float RemainingDistance(Source source, Context context) => Vector2.Distance(source.position, context.playerPosition) - source.geometry.WholeSurfaceRadius;
    static bool IsValid(Context value) => value != null && value.sources != null && value.destinations != null && value.sources.Count > 0 &&
        value.destinations.Count > 0 && value.maxCandidates > 0 && PoliceSpawnSafetyPolicy.IsValidReferenceRadius(value.relocationRadius) &&
        IsValidRect(value.cameraBounds) && IsValidRect(value.localMapBounds) && Finite(value.playerPosition) && Finite(value.playerVelocity) &&
        Finite(value.playerSurfaceRadius) && value.playerSurfaceRadius >= 0f && Finite(value.minimumDistance) && value.minimumDistance >= 0f &&
        Finite(value.reactionSeconds) && value.reactionSeconds >= 0f && Finite(value.cameraMargin) && value.cameraMargin >= 0f &&
        value.worldClearance != null && value.localStaticClearance != null;

    static bool IsValidRect(Rect value) => Finite(value.position) && Finite(value.size) && value.width > 0f && value.height > 0f &&
        Finite(value.xMax) && Finite(value.yMax);
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
