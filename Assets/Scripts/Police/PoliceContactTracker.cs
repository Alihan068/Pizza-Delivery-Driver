using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physics-step contact state machine for the S09 arrest rule. Threshold crossing creates a pending
/// request; LateUpdate integration must peek and finalize it after damage, while rejection resets and
/// rearms the tracker.
/// </summary>
public sealed class PoliceContactTracker {
    readonly HashSet<int> contacts = new HashSet<int>();
    readonly HashSet<int> pendingEnters = new HashSet<int>();
    readonly HashSet<int> pendingExits = new HashSet<int>();
    readonly List<int> staleScratch = new List<int>();
    Vector2 stationaryAnchor;
    bool hasAnchor;
    bool active;
    bool arrestRequested;
    bool arrestFinalized;
    float holdSeconds;
    float lastStationarySpeedThreshold;
    float lastStationaryDistanceTolerance;

    /// <summary>Current active hold duration in session seconds.</summary>
    public float HoldSeconds => holdSeconds;
    /// <summary>Number of currently committed live police contacts.</summary>
    public int ContactCount => contacts.Count;
    /// <summary>True while the threshold request awaits LateUpdate validation.</summary>
    public bool ArrestRequested => arrestRequested;
    /// <summary>True after one request has been successfully finalized for this session.</summary>
    public bool ArrestFinalized => arrestFinalized;
    /// <summary>Explicit non-consuming pending-request check for coordinator integration.</summary>
    public bool HasPendingArrestRequest => arrestRequested && !arrestFinalized;

    /// <summary>Starts a new session-local contact state at the player's current position.</summary>
    /// <param name="playerPosition">World position used as the initial stationary anchor.</param>
    public void Begin(Vector2 playerPosition) {
        contacts.Clear();
        pendingEnters.Clear();
        pendingExits.Clear();
        stationaryAnchor = playerPosition;
        hasAnchor = IsFinite(playerPosition);
        active = true;
        arrestRequested = false;
        arrestFinalized = false;
        holdSeconds = 0f;
    }

    /// <summary>Queues a non-trigger contact enter for the next physics-step commit.</summary>
    /// <param name="policeLifeId">Live police life identity.</param>
    public void QueueEnter(int policeLifeId) {
        if (!active || arrestFinalized || policeLifeId <= 0) return;
        pendingExits.Remove(policeLifeId);
        pendingEnters.Add(policeLifeId);
    }

    /// <summary>Queues a non-trigger contact exit for the next physics-step commit.</summary>
    /// <param name="policeLifeId">Police life identity leaving contact.</param>
    public void QueueExit(int policeLifeId) {
        if (!active || policeLifeId <= 0) return;
        pendingEnters.Remove(policeLifeId);
        pendingExits.Add(policeLifeId);
    }

    /// <summary>
    /// Commits one completed physics step and advances the hold only while the live contact set,
    /// world velocity, anchor displacement, and authored values are valid. Paused steps do not add
    /// time, but still apply contact-set changes.
    /// </summary>
    public void CommitStep(IReadOnlyCollection<int> livePoliceContacts, Vector2 playerPosition, Vector2 playerVelocity,
        float deltaSeconds, float stationarySpeedThreshold, float stationaryDistanceTolerance, float arrestHoldSeconds, bool paused) {
        if (!active || arrestFinalized) return;
        ApplyPendingContacts(livePoliceContacts);
        pendingEnters.Clear();
        pendingExits.Clear();
        lastStationarySpeedThreshold = stationarySpeedThreshold;
        lastStationaryDistanceTolerance = stationaryDistanceTolerance;
        if (contacts.Count == 0) {
            ResetHold(playerPosition);
            return;
        }
        if (!IsFinite(playerPosition) || !IsFinite(playerVelocity) || !FiniteNonNegative(deltaSeconds) ||
            !FiniteNonNegative(stationarySpeedThreshold) || !FiniteNonNegative(stationaryDistanceTolerance) ||
            !FiniteNonNegative(arrestHoldSeconds)) {
            ResetHold(playerPosition);
            return;
        }
        if (!hasAnchor) { stationaryAnchor = playerPosition; hasAnchor = true; }
        bool stationary = playerVelocity.magnitude <= stationarySpeedThreshold &&
            Vector2.Distance(playerPosition, stationaryAnchor) <= stationaryDistanceTolerance;
        if (!stationary) {
            ResetHold(playerPosition);
            return;
        }
        if (arrestRequested || paused) return;
        holdSeconds += deltaSeconds;
        if (holdSeconds >= arrestHoldSeconds) arrestRequested = true;
    }

    /// <summary>Reads the pending request without consuming or disabling the tracker.</summary>
    public bool TryPeekArrestRequest() => HasPendingArrestRequest;

    /// <summary>
    /// Finalizes a pending request after the damage flush. It rechecks session/player validity,
    /// current live contacts, current velocity, and current anchor displacement without advancing
    /// time. Rejection resets the hold and stays rearmed.
    /// </summary>
    public bool TryFinalizeArrestRequest(IReadOnlyCollection<int> livePoliceContacts, Vector2 playerPosition,
        Vector2 playerVelocity, bool sessionActive, bool playerAlive) {
        if (!HasPendingArrestRequest) return false;
        bool valid = sessionActive && playerAlive && livePoliceContacts != null &&
            HasAnyLiveContact(livePoliceContacts) && IsFinite(playerPosition) && IsFinite(playerVelocity) &&
            playerVelocity.magnitude <= lastStationarySpeedThreshold && hasAnchor &&
            Vector2.Distance(playerPosition, stationaryAnchor) <= lastStationaryDistanceTolerance;
        if (!valid) {
            arrestRequested = false;
            ResetHold(playerPosition);
            return false;
        }
        arrestFinalized = true;
        arrestRequested = false;
        active = false;
        return true;
    }

    /// <summary>Rejects a pending request and rearms the tracker without advancing its timer.</summary>
    public void CancelArrestRequest() {
        if (!HasPendingArrestRequest) return;
        arrestRequested = false;
        ResetHold(stationaryAnchor);
    }

    /// <summary>Consumes the legacy request API; new integration should peek and finalize instead.</summary>
    /// <returns>True only once after the authored hold is reached.</returns>
    public bool TryConsumeArrestRequest() {
        if (!HasPendingArrestRequest) return false;
        arrestRequested = false;
        arrestFinalized = true;
        active = false;
        return true;
    }

    /// <summary>Ends the session and clears all contact identities and timer state.</summary>
    public void End() {
        active = false;
        contacts.Clear();
        pendingEnters.Clear();
        pendingExits.Clear();
        holdSeconds = 0f;
        arrestRequested = false;
        arrestFinalized = false;
        hasAnchor = false;
    }

    void ApplyPendingContacts(IReadOnlyCollection<int> livePoliceContacts) {
        foreach (int lifeId in pendingExits) contacts.Remove(lifeId);
        foreach (int lifeId in pendingEnters) contacts.Add(lifeId);
        staleScratch.Clear();
        foreach (int lifeId in contacts) if (!Contains(livePoliceContacts, lifeId)) staleScratch.Add(lifeId);
        foreach (int lifeId in staleScratch) contacts.Remove(lifeId);
    }

    bool HasAnyLiveContact(IReadOnlyCollection<int> livePoliceContacts) {
        foreach (int lifeId in contacts) if (Contains(livePoliceContacts, lifeId)) return true;
        return false;
    }

    static bool Contains(IReadOnlyCollection<int> values, int sought) {
        if (values == null) return false;
        foreach (int value in values) if (value == sought) return true;
        return false;
    }

    void ResetHold(Vector2 playerPosition) {
        holdSeconds = 0f;
        arrestRequested = false;
        stationaryAnchor = playerPosition;
        hasAnchor = IsFinite(playerPosition);
    }

    static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    static bool IsFinite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
}
