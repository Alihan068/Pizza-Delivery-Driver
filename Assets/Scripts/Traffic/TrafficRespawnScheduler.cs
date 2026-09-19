using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps exactly one pending respawn record per population-deficit id, so a death event and an
/// independent timeout racing for the same lifeId never register two records and never produce two
/// replacement vehicles. A relocation (an existing live instance moved elsewhere) is never a deficit
/// and must never be registered here — only an actual population shortfall goes through this
/// scheduler. Every time value is an explicit session time supplied by the caller, never
/// Time.time/Time.deltaTime directly, so timers correctly stop advancing during pause/loading/end.
/// </summary>
public sealed class TrafficRespawnScheduler {
    readonly Dictionary<string, float> incidentTimeByDeficitId = new Dictionary<string, float>();
    readonly Dictionary<string, Vector2> incidentPositionByDeficitId = new Dictionary<string, Vector2>();

    public int PendingCount => incidentTimeByDeficitId.Count;

    /// <summary>Registers a new deficit. Returns false without changing anything if this id is already pending — the dedupe guarantee.</summary>
    public bool TryRegisterDeficit(string deficitId, float sessionTime, Vector2 incidentWorldPosition) {
        if (string.IsNullOrEmpty(deficitId) || incidentTimeByDeficitId.ContainsKey(deficitId)) return false;
        incidentTimeByDeficitId[deficitId] = sessionTime;
        incidentPositionByDeficitId[deficitId] = incidentWorldPosition;
        return true;
    }

    public bool IsPending(string deficitId) => !string.IsNullOrEmpty(deficitId) && incidentTimeByDeficitId.ContainsKey(deficitId);

    /// <summary>
    /// True once minimum delay has elapsed AND (the player is far enough from the incident OR the
    /// replacement deadline has passed). Never consumes the record — a deadline reached with no safe
    /// spot found simply keeps returning true on the next check without ever registering twice.
    /// </summary>
    public bool IsReadyToAttempt(string deficitId, float sessionTime, Vector2 playerPosition, RespawnTimingRules rules) {
        if (rules == null || string.IsNullOrEmpty(deficitId) || !incidentTimeByDeficitId.TryGetValue(deficitId, out float incidentTime)) return false;

        float elapsed = sessionTime - incidentTime;
        if (elapsed < rules.minimumDelaySeconds) return false;

        bool farEnough = incidentPositionByDeficitId.TryGetValue(deficitId, out var incidentPosition) &&
            Vector2.Distance(playerPosition, incidentPosition) >= rules.minimumPlayerDistance;
        bool deadlinePassed = elapsed >= rules.replacementDeadlineSeconds;
        return farEnough || deadlinePassed;
    }

    /// <summary>Clears a deficit once a safe respawn has actually been placed for it. Call only on real success, never speculatively.</summary>
    public bool CompleteRespawn(string deficitId) {
        if (string.IsNullOrEmpty(deficitId) || !incidentTimeByDeficitId.Remove(deficitId)) return false;
        incidentPositionByDeficitId.Remove(deficitId);
        return true;
    }
}
