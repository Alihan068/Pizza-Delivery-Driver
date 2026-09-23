using System.Collections.Generic;
using UnityEngine;

/// <summary>Fair deterministic work scheduler for free-drive police planners.</summary>
public sealed class PoliceNavigationScheduler {
    sealed class Slot {
        public int lifeId;
        public PoliceFreePathPlanner planner;
        public PoliceFreePathPlanner.Request activeRequest;
        public PoliceFreePathPlanner.Request latestRequest;
        public bool hasLatestRequest;
        public bool active;
        public PoliceFreePathPlanner.Result result;
    }

    readonly List<Slot> slots = new List<Slot>();
    int cursor;

    /// <summary>Adds or replaces the planner owned by one deterministic unit life.</summary>
    public void Register(int lifeId, PoliceFreePathPlanner planner) {
        if (planner == null) return;
        for (int i = 0; i < slots.Count; i++) {
            if (slots[i].lifeId != lifeId) continue;
            slots[i].planner = planner;
            slots[i].active = false;
            slots[i].hasLatestRequest = false;
            slots[i].result = null;
            return;
        }

        slots.Add(new Slot { lifeId = lifeId, planner = planner });
        slots.Sort((a, b) => a.lifeId.CompareTo(b.lifeId));
        cursor = cursor % Mathf.Max(1, slots.Count);
    }

    /// <summary>Removes a unit and cancels its pending search.</summary>
    public void Unregister(int lifeId) {
        for (int i = 0; i < slots.Count; i++) {
            if (slots[i].lifeId != lifeId) continue;
            slots[i].planner.Cancel();
            slots.RemoveAt(i);
            cursor = slots.Count == 0 ? 0 : cursor % slots.Count;
            return;
        }
    }

    /// <summary>Queues the newest request without resetting an active search.</summary>
    public void Submit(PoliceFreePathPlanner.Request request) {
        for (int i = 0; i < slots.Count; i++) {
            Slot slot = slots[i];
            if (slot.lifeId != request.unitLifeId) continue;
            if (slot.result != null && !MatchesLifeTargetRevision(slot.result, request)) slot.result = null;
            slot.latestRequest = request;
            slot.hasLatestRequest = true;
            return;
        }
    }

    /// <summary>Advances fair work; paused ticks consume neither time nor planner work.</summary>
    public int Step(int workGrant, bool paused = false) {
        if (paused || workGrant <= 0 || slots.Count == 0) return 0;
        int used = 0;
        int idle = 0;
        while (used < workGrant && idle < slots.Count) {
            Slot slot = slots[cursor];
            cursor = (cursor + 1) % slots.Count;
            if (!slot.active && !slot.hasLatestRequest) {
                idle++;
                continue;
            }

            idle = 0;
            if (!slot.active) BeginLatest(slot);
            PoliceFreePathPlanner.Result stepResult = slot.planner.Step(1);
            used++;
            if (stepResult.status == PoliceFreePathPlanner.Status.Pending) continue;

            PoliceFreePathPlanner.Result completed = stepResult;
            bool validCompletion = MatchesLifeTargetRevision(completed, slot.activeRequest);
            bool hasNewerRequest = slot.hasLatestRequest;
            slot.active = false;
            if (validCompletion && (!hasNewerRequest || MatchesLifeTargetRevision(completed, slot.latestRequest)))
                slot.result = completed;
            else if (!validCompletion || hasNewerRequest)
                slot.result = null;
        }
        return used;
    }

    /// <summary>Reads the latest valid result for a unit without exposing scheduler state.</summary>
    public bool TryGetResult(int lifeId, out PoliceFreePathPlanner.Result result) {
        for (int i = 0; i < slots.Count; i++) {
            if (slots[i].lifeId != lifeId) continue;
            result = slots[i].result;
            return result != null;
        }
        result = null;
        return false;
    }

    void BeginLatest(Slot slot) {
        slot.activeRequest = slot.latestRequest;
        slot.hasLatestRequest = false;
        slot.active = true;
        slot.planner.Begin(slot.activeRequest);
    }

    static bool MatchesLifeTargetRevision(PoliceFreePathPlanner.Result result,
        PoliceFreePathPlanner.Request request) {
        return result != null && result.unitLifeId == request.unitLifeId &&
            result.targetLifeId == request.targetLifeId &&
            result.geometryRevision == request.geometryRevision;
    }
}
