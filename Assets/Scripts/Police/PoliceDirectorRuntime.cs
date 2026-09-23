using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Pure deterministic police director core with session-scoped logical reservations.</summary>
public sealed class PoliceDirectorRuntime : IDisposable {
    /// <summary>Why a police request was queued.</summary>
    public enum RequestReason { InitialTimeGate, HeatTargetDeficit, DestroyedReplacement }

    /// <summary>Immutable request identity and composition data passed to materialization.</summary>
    public readonly struct SpawnRequest {
        /// <summary>Session identity that issued this request.</summary>
        public readonly long sessionNonce;
        /// <summary>Monotonic identity of this request within its runtime.</summary>
        public readonly long requestId;
        /// <summary>Stable vehicle profile to resolve.</summary>
        public readonly string vehicleProfileId;
        /// <summary>Stable behavior profile to resolve.</summary>
        public readonly string behaviorProfileId;
        /// <summary>Authored tactical role.</summary>
        public readonly PoliceTacticalRole tacticalRole;
        /// <summary>Active session time at request creation.</summary>
        public readonly float requestedAtSeconds;
        /// <summary>Reason that the request entered the queue.</summary>
        public readonly RequestReason reason;

        /// <summary>Preserves the legacy constructor without creating an accepted identity.</summary>
        public SpawnRequest(string vehicleId, string behaviorId, PoliceTacticalRole role, float time, RequestReason requestReason) :
            this(0L, 0L, vehicleId, behaviorId, role, time, requestReason) { }

        internal SpawnRequest(long nonce, long id, string vehicleId, string behaviorId, PoliceTacticalRole role,
            float time, RequestReason requestReason) {
            sessionNonce = nonce;
            requestId = id;
            vehicleProfileId = vehicleId;
            behaviorProfileId = behaviorId;
            tacticalRole = role;
            requestedAtSeconds = time;
            reason = requestReason;
        }
    }

    enum ReservationState { Queued, InFlight, Committed, Rejected, Destroyed }

    sealed class Reservation {
        public SpawnRequest request;
        public ReservationState state;
        public int lifeId;
        public string entryKey;
    }

    static long nextSessionNonce;
    PoliceDirectorData data;
    readonly bool policeEnabled;
    readonly System.Random random;
    readonly PoliceIncidentLedger incidents = new PoliceIncidentLedger();
    readonly Queue<SpawnRequest> pending = new Queue<SpawnRequest>();
    readonly Dictionary<long, Reservation> reservations = new Dictionary<long, Reservation>();
    readonly HashSet<long> rejectedPendingIds = new HashSet<long>();
    readonly Dictionary<int, Reservation> committedByLife = new Dictionary<int, Reservation>();
    readonly Dictionary<string, int> activeByEntry = new Dictionary<string, int>();
    readonly List<float> replacementNotBefore = new List<float>();
    readonly long sessionNonce;
    long nextRequestId;
    bool ended;
    float nextRequestAt = float.PositiveInfinity;
    int activeCount;
    int reservedCount;
    float activeSeconds;

    /// <summary>Current baseline heat based on the last tick.</summary>
    public float BaseHeat { get; private set; }
    /// <summary>Accepted victim-destruction incident heat under authored policy.</summary>
    public float IncidentHeat => incidents.IncidentHeat;
    /// <summary>Clamped heat used for tier selection.</summary>
    public float EffectiveHeat { get; private set; }
    /// <summary>Current data-driven tier, or null before its threshold is reached.</summary>
    public PoliceHeatTier CurrentTier { get; private set; }
    /// <summary>Living police count reported by composition.</summary>
    public int ActiveCount => activeCount;
    /// <summary>Requests waiting to be materialized.</summary>
    public int PendingCount => CountQueuedReservations();
    /// <summary>Dequeued requests whose materialization outcome is unresolved.</summary>
    public int InFlightCount => reservedCount - PendingCount;
    /// <summary>Living plus queued and in-flight logical reservations.</summary>
    public int ReservedCount => activeCount + reservedCount;
    /// <summary>Whether this runtime can emit police requests.</summary>
    public bool IsEnabled => !ended && policeEnabled && data != null;
    /// <summary>Unique identity of this session runtime.</summary>
    public long SessionNonce => sessionNonce;

    /// <summary>Creates a validated, detached, session-local director runtime.</summary>
    public PoliceDirectorRuntime(PoliceDirectorData directorData, bool policeIsEnabled, int seed) {
        sessionNonce = ++nextSessionNonce;
        random = new System.Random(seed);
        if (!policeIsEnabled || directorData == null || !directorData.TryValidate(out _)) {
            policeEnabled = false;
            data = null;
            return;
        }
        policeEnabled = true;
        data = directorData.CreateDetachedCopy();
    }

    /// <summary>Reports one destruction event to the incident ledger.</summary>
    /// <param name="destroyedEvent">Published destruction event.</param>
    /// <param name="rules">Authored heat award values.</param>
    /// <returns>True when the life was newly consumed by the ledger.</returns>
    public bool RecordIncident(VehicleDestroyedEvent destroyedEvent, HeatAwardRules rules) {
        if (ended) return false;
        return incidents.TryRecord(destroyedEvent, rules, out _);
    }

    /// <summary>Updates heat and schedules at most one request after both cadence gates pass.</summary>
    /// <param name="sessionActiveSeconds">Active session time from the session clock.</param>
    public void Tick(float sessionActiveSeconds) {
        if (!IsEnabled || !Finite(sessionActiveSeconds) || sessionActiveSeconds < activeSeconds) return;
        activeSeconds = Mathf.Max(0f, sessionActiveSeconds);
        BaseHeat = PoliceHeatModel.EvaluateBaseHeat(data, activeSeconds);
        EffectiveHeat = PoliceHeatModel.EvaluateEffectiveHeat(data, BaseHeat, incidents.IncidentHeat);
        PoliceHeatTier previousTier = CurrentTier;
        CurrentTier = data.ResolveTier(EffectiveHeat);
        if (CurrentTier == null || activeSeconds < data.earliestPoliceTime) return;
        if (previousTier != null && previousTier.tierId != CurrentTier.tierId) CancelExcessPending(CurrentTier);
        if (nextRequestAt == float.PositiveInfinity) nextRequestAt = data.earliestPoliceTime;
        if (activeCount + reservedCount >= CurrentTier.targetCount || activeSeconds < nextRequestAt) return;
        if (!TryConsumeReadyReplacement(out bool isReplacement)) return;
        if (!TrySelectComposition(CurrentTier, out PoliceCompositionEntry entry)) {
            if (isReplacement) AddReplacementDeadline(activeSeconds);
            return;
        }

        SpawnRequest request = new SpawnRequest(sessionNonce, NextRequestId(), entry.vehicleProfileId,
            entry.behaviorProfileId, entry.tacticalRole, activeSeconds,
            isReplacement ? RequestReason.DestroyedReplacement :
            activeCount == 0 ? RequestReason.InitialTimeGate : RequestReason.HeatTargetDeficit);
        pending.Enqueue(request);
        reservations.Add(request.requestId, new Reservation {
            request = request, state = ReservationState.Queued, entryKey = EntryKey(request)
        });
        reservedCount++;
        nextRequestAt = activeSeconds + Mathf.Max(0f, CurrentTier.reinforcementInterval);
    }

    /// <summary>Dequeues one logical reservation and marks it in-flight.</summary>
    /// <param name="request">The next request when one is available.</param>
    /// <returns>True when a request was dequeued.</returns>
    public bool TryDequeue(out SpawnRequest request) {
        while (pending.Count > 0) {
            request = pending.Dequeue();
            if (!reservations.TryGetValue(request.requestId, out Reservation reservation) ||
                !IsCurrentRequest(reservation.request) || reservation.state != ReservationState.Queued) {
                rejectedPendingIds.Remove(request.requestId);
                reservations.Remove(request.requestId);
                continue;
            }
            reservation.state = ReservationState.InFlight;
            return true;
        }
        request = default;
        return false;
    }

    /// <summary>Commits one in-flight request to exactly one positive police life identity.</summary>
    /// <param name="request">The request returned by <see cref="TryDequeue"/>.</param>
    /// <param name="lifeId">Fresh positive identity assigned by the population service.</param>
    /// <returns>True only when the first valid commit was applied.</returns>
    public bool Commit(SpawnRequest request, int lifeId) {
        if (lifeId <= 0 || !TryGetOutstanding(request, out Reservation reservation) ||
            reservation.state != ReservationState.InFlight || committedByLife.ContainsKey(lifeId)) return false;
        reservation.state = ReservationState.Committed;
        reservation.lifeId = lifeId;
        reservedCount--;
        activeCount++;
        activeByEntry[reservation.entryKey] = GetCount(activeByEntry, reservation.entryKey) + 1;
        committedByLife.Add(lifeId, reservation);
        return true;
    }

    /// <summary>Checks the complete current in-flight request before acquiring physical resources.</summary>
    public bool IsInFlight(SpawnRequest request) {
        return IsEnabled && TryGetOutstanding(request, out var reservation) &&
            reservation.state == ReservationState.InFlight &&
            request.vehicleProfileId == reservation.request.vehicleProfileId &&
            request.behaviorProfileId == reservation.request.behaviorProfileId &&
            request.tacticalRole == reservation.request.tacticalRole;
    }

    /// <summary>Rejects one queued or in-flight request exactly once.</summary>
    /// <param name="request">The logical request to roll back.</param>
    /// <returns>True only when a live logical reservation was released.</returns>
    public bool Reject(SpawnRequest request) {
        if (!TryGetOutstanding(request, out Reservation reservation) ||
            (reservation.state != ReservationState.Queued && reservation.state != ReservationState.InFlight)) return false;
        bool wasInFlight = reservation.state == ReservationState.InFlight;
        reservation.state = ReservationState.Rejected;
        reservedCount--;
        if (wasInFlight) reservations.Remove(request.requestId);
        else {
            reservations.Remove(request.requestId);
            rejectedPendingIds.Add(request.requestId);
        }
        return true;
    }

    /// <summary>Releases one committed police life exactly once and schedules its delayed replacement.</summary>
    /// <param name="lifeId">Identity assigned at commit time.</param>
    /// <returns>True only when a living police count was released.</returns>
    public bool Destroyed(int lifeId) {
        if (lifeId <= 0 || !committedByLife.TryGetValue(lifeId, out Reservation reservation) ||
            reservation.state != ReservationState.Committed) return false;
        reservation.state = ReservationState.Destroyed;
        committedByLife.Remove(lifeId);
        activeCount--;
        int count = GetCount(activeByEntry, reservation.entryKey);
        if (count <= 1) activeByEntry.Remove(reservation.entryKey); else activeByEntry[reservation.entryKey] = count - 1;
        if (CurrentTier != null) AddReplacementDeadline(activeSeconds + Mathf.Max(0f, CurrentTier.destroyedReplacementDelay));
        reservations.Remove(reservation.request.requestId);
        return true;
    }

    /// <summary>Legacy commit overload retained without fabricating a life identity.</summary>
    /// <param name="request">Ignored unless a future compatibility layer can supply a genuine life identity.</param>
    public void ReportSpawnCommitted(SpawnRequest request) { }

    /// <summary>Compatibility overload that forwards a genuine materialized life identity.</summary>
    /// <param name="request">Request returned by the runtime queue.</param>
    /// <param name="lifeId">Fresh positive identity assigned by the population service.</param>
    /// <returns>True when the request was committed.</returns>
    public bool ReportSpawnCommitted(SpawnRequest request, int lifeId) => Commit(request, lifeId);

    /// <summary>Legacy rejection overload forwards only an unambiguous in-flight request.</summary>
    public void ReportSpawnRejected() {
        Reservation candidate = null;
        foreach (Reservation reservation in reservations.Values) {
            if (reservation.state != ReservationState.InFlight) continue;
            if (candidate != null) return;
            candidate = reservation;
        }
        if (candidate != null) Reject(candidate.request);
    }

    /// <summary>Legacy death overload forwards only from a genuinely committed request.</summary>
    /// <param name="request">Original request associated with the committed life.</param>
    public void ReportPoliceDestroyed(SpawnRequest request) {
        if (TryGetOutstanding(request, out Reservation reservation) && reservation.state == ReservationState.Committed)
            Destroyed(reservation.lifeId);
    }

    /// <summary>Returns the active count for one vehicle/behavior pairing.</summary>
    /// <param name="vehicleProfileId">Vehicle profile identifier.</param>
    /// <param name="behaviorProfileId">Behavior profile identifier.</param>
    /// <returns>Number of committed living police using the pairing.</returns>
    public int GetActiveCount(string vehicleProfileId, string behaviorProfileId) {
        return GetCount(activeByEntry, (vehicleProfileId ?? string.Empty) + "\u001f" + (behaviorProfileId ?? string.Empty));
    }

    bool TryGetOutstanding(SpawnRequest request, out Reservation reservation) {
        reservation = null;
        return request.sessionNonce == sessionNonce && request.requestId > 0 &&
            reservations.TryGetValue(request.requestId, out reservation) && reservation.request.sessionNonce == request.sessionNonce;
    }

    int CountQueuedReservations() {
        int count = 0;
        foreach (Reservation reservation in reservations.Values)
            if (reservation.state == ReservationState.Queued) count++;
        return count;
    }

    bool IsCurrentRequest(SpawnRequest request) => request.sessionNonce == sessionNonce && request.requestId > 0;

    void CancelExcessPending(PoliceHeatTier tier) {
        int allowedPending = Mathf.Max(0, tier.targetCount - activeCount - InFlightCount);
        if (pending.Count <= allowedPending) return;
        int keep = allowedPending;
        int count = pending.Count;
        for (int i = 0; i < count; i++) {
            SpawnRequest request = pending.Dequeue();
            if (keep > 0) { pending.Enqueue(request); keep--; } else Reject(request);
        }
    }

    bool TryConsumeReadyReplacement(out bool isReplacement) {
        isReplacement = false;
        if (replacementNotBefore.Count == 0) return true;
        if (activeSeconds < replacementNotBefore[0]) return false;
        replacementNotBefore.RemoveAt(0);
        isReplacement = true;
        return true;
    }

    void AddReplacementDeadline(float notBefore) {
        int index = replacementNotBefore.Count;
        while (index > 0 && replacementNotBefore[index - 1] > notBefore) index--;
        replacementNotBefore.Insert(index, notBefore);
    }

    bool TrySelectComposition(PoliceHeatTier tier, out PoliceCompositionEntry selected) {
        selected = null;
        float total = 0f;
        for (int i = 0; i < tier.compositions.Count; i++) {
            PoliceCompositionEntry entry = tier.compositions[i];
            if (entry == null || entry.weight <= 0f || (entry.maximumCount > 0 && GetReservationCount(EntryKey(entry)) >= entry.maximumCount)) continue;
            total += entry.weight;
        }
        if (total <= 0f) return false;
        double pick = random.NextDouble() * total;
        for (int i = 0; i < tier.compositions.Count; i++) {
            PoliceCompositionEntry entry = tier.compositions[i];
            if (entry == null || entry.weight <= 0f || (entry.maximumCount > 0 && GetReservationCount(EntryKey(entry)) >= entry.maximumCount)) continue;
            pick -= entry.weight;
            if (pick <= 0d) { selected = entry; return true; }
        }
        return false;
    }

    int GetReservationCount(string entryKey) {
        int count = GetCount(activeByEntry, entryKey);
        foreach (Reservation reservation in reservations.Values)
            if ((reservation.state == ReservationState.Queued || reservation.state == ReservationState.InFlight) && reservation.entryKey == entryKey) count++;
        return count;
    }

    static string EntryKey(PoliceCompositionEntry entry) => (entry.vehicleProfileId ?? string.Empty) + "\u001f" + (entry.behaviorProfileId ?? string.Empty);
    static string EntryKey(SpawnRequest request) => (request.vehicleProfileId ?? string.Empty) + "\u001f" + (request.behaviorProfileId ?? string.Empty);
    static int GetCount(Dictionary<string, int> counts, string key) => counts.TryGetValue(key, out int count) ? count : 0;
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    long NextRequestId() => ++nextRequestId;

    /// <summary>
    /// Ends this session and releases the detached ScriptableObject owned by the runtime. This
    /// method is idempotent and must be called by the session owner before discarding the runtime.
    /// </summary>
    public void End() => Dispose();

    /// <summary>Releases session reservations, heat state and the detached profile exactly once.</summary>
    public void Dispose() {
        if (ended) return;
        ended = true;
        pending.Clear();
        reservations.Clear();
        rejectedPendingIds.Clear();
        committedByLife.Clear();
        activeByEntry.Clear();
        replacementNotBefore.Clear();
        activeCount = 0;
        reservedCount = 0;
        BaseHeat = 0f;
        EffectiveHeat = 0f;
        CurrentTier = null;
        incidents.Reset();
        if (data != null) {
            if (Application.isPlaying) UnityEngine.Object.Destroy(data);
            else UnityEngine.Object.DestroyImmediate(data);
            data = null;
        }
    }
}
