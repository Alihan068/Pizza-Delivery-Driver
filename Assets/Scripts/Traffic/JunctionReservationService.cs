using System.Collections.Generic;

/// <summary>
/// Right-of-way arbiter for authored junctions: approach → reserve → traverse → release. A junction's
/// conflict zone is granted to at most one movement at a time, except that vehicles taking the
/// exact same movement (same from-edge and to-edge) may platoon through together — their spacing
/// is the obstacle sensor's job, not this service's. Waiting vehicles are ordered by stable
/// arrival time, then authored transition priority, then life id, so two vehicles arriving from
/// two directions always resolve the same way. Every grant carries a bounded timeout measured on
/// the <see cref="SessionClock"/>; a holder that never releases (stuck, dead without cleanup)
/// loses the zone on expiry so a queue can never deadlock forever. External occupancy (a player
/// sitting in the zone, reported by whoever observes it) blocks new grants without touching
/// current holders. Police do not use this service; they are plain colliders to everyone else.
/// Pure C#: no scene lookups, no per-frame allocation after warm-up.
/// </summary>
public sealed class JunctionReservationService {
    /// <summary>Result of a reservation request.</summary>
    public enum Outcome {
        /// <summary>The zone is yours; traverse and release when clear.</summary>
        Granted,
        /// <summary>Someone else holds a conflicting movement or the zone is externally occupied; stop at the line and retry.</summary>
        Waiting,
        /// <summary>The movement is not an authored allowed transition of this junction (or the junction is unknown); do not enter.</summary>
        Refused
    }

    sealed class Holder {
        public int lifeId;
        public string fromEdgeId;
        public string toEdgeId;
        public float expiresAt;
    }

    sealed class Waiter {
        public int lifeId;
        public string fromEdgeId;
        public string toEdgeId;
        public float arrivedAt;
        public int priority;
    }

    sealed class JunctionState {
        public JunctionRecord record;
        public readonly List<Holder> holders = new List<Holder>();
        public readonly List<Waiter> waiters = new List<Waiter>();
        public bool externallyOccupied;
    }

    readonly SessionClock clock;
    readonly float holdTimeoutSeconds;
    readonly Dictionary<string, JunctionState> junctions = new Dictionary<string, JunctionState>();
    readonly List<Holder> holderPool = new List<Holder>();
    readonly List<Waiter> waiterPool = new List<Waiter>();

    /// <summary>Creates the arbiter over a graph's junctions.</summary>
    /// <param name="graph">Graph whose junction records define the allowed movements.</param>
    /// <param name="junctionIds">Junction ids to manage (authoring order); unknown ids are ignored.</param>
    /// <param name="clock">Active session clock for arrival ordering and grant expiry.</param>
    /// <param name="holdTimeoutSeconds">Seconds a grant survives without release before it is revoked.</param>
    public JunctionReservationService(RoadGraphRuntime graph, IEnumerable<string> junctionIds, SessionClock clock, float holdTimeoutSeconds) {
        this.clock = clock;
        this.holdTimeoutSeconds = holdTimeoutSeconds > 0f && !float.IsNaN(holdTimeoutSeconds) && !float.IsInfinity(holdTimeoutSeconds) ? holdTimeoutSeconds : 10f;
        if (graph == null || junctionIds == null) return;
        foreach (var id in junctionIds) {
            var record = graph.GetJunction(id);
            if (record != null && !junctions.ContainsKey(id)) junctions[id] = new JunctionState { record = record };
        }
    }

    /// <summary>Number of junctions this service manages.</summary>
    public int JunctionCount => junctions.Count;

    /// <summary>Whether a life currently holds a grant on the junction.</summary>
    public bool Holds(string junctionId, int lifeId) {
        if (!junctions.TryGetValue(junctionId ?? string.Empty, out var state)) return false;
        Expire(state);
        foreach (var holder in state.holders) if (holder.lifeId == lifeId) return true;
        return false;
    }

    /// <summary>Number of current grant holders on a junction (after expiring stale ones).</summary>
    public int HolderCount(string junctionId) {
        if (!junctions.TryGetValue(junctionId ?? string.Empty, out var state)) return 0;
        Expire(state);
        return state.holders.Count;
    }

    /// <summary>Marks the zone as physically occupied by something outside this service (the player). New grants are withheld while true.</summary>
    public void SetExternalOccupancy(string junctionId, bool occupied) {
        if (junctions.TryGetValue(junctionId ?? string.Empty, out var state)) state.externallyOccupied = occupied;
    }

    /// <summary>
    /// Requests (or re-requests) the zone for one movement. Idempotent: a current holder is told
    /// Granted again and its expiry is refreshed; a waiter keeps its original arrival time.
    /// </summary>
    /// <param name="junctionId">Junction to enter.</param>
    /// <param name="lifeId">Requesting vehicle's session-scoped life id.</param>
    /// <param name="fromEdgeId">Edge the vehicle arrives on.</param>
    /// <param name="toEdgeId">Edge the vehicle will leave on.</param>
    public Outcome Request(string junctionId, int lifeId, string fromEdgeId, string toEdgeId) {
        if (!junctions.TryGetValue(junctionId ?? string.Empty, out var state)) return Outcome.Refused;
        int priority;
        if (!TryGetTransition(state.record, fromEdgeId, toEdgeId, out priority)) return Outcome.Refused;
        Expire(state);

        foreach (var holder in state.holders) {
            if (holder.lifeId == lifeId) {
                holder.expiresAt = Now + holdTimeoutSeconds;
                return Outcome.Granted;
            }
        }

        Waiter mine = null;
        foreach (var waiter in state.waiters) if (waiter.lifeId == lifeId) { mine = waiter; break; }
        if (mine == null) {
            mine = RentWaiter();
            mine.lifeId = lifeId;
            mine.fromEdgeId = fromEdgeId;
            mine.toEdgeId = toEdgeId;
            mine.arrivedAt = Now;
            mine.priority = priority;
            InsertWaiter(state, mine);
        }

        if (state.externallyOccupied) return Outcome.Waiting;
        if (!IsCompatibleWithHolders(state, fromEdgeId, toEdgeId)) return Outcome.Waiting;
        // Only the head of the queue (among waiters that conflict with me) may take the zone: a
        // later arrival on a free-looking movement must not jump a queued conflicting vehicle.
        foreach (var waiter in state.waiters) {
            if (waiter == mine) break;
            if (!SameMovement(waiter.fromEdgeId, waiter.toEdgeId, fromEdgeId, toEdgeId)) return Outcome.Waiting;
        }

        state.waiters.Remove(mine);
        var granted = RentHolder();
        granted.lifeId = lifeId;
        granted.fromEdgeId = fromEdgeId;
        granted.toEdgeId = toEdgeId;
        granted.expiresAt = Now + holdTimeoutSeconds;
        state.holders.Add(granted);
        ReturnWaiter(mine);
        return Outcome.Granted;
    }

    /// <summary>Gives the zone back (or withdraws from the queue). Safe to call when nothing is held.</summary>
    public void Release(string junctionId, int lifeId) {
        if (!junctions.TryGetValue(junctionId ?? string.Empty, out var state)) return;
        RemoveFrom(state, lifeId);
    }

    /// <summary>Drops every grant and queue entry of a life — for death, pool return or relocation. The next requester takes over.</summary>
    public void ReleaseAll(int lifeId) {
        foreach (var state in junctions.Values) RemoveFrom(state, lifeId);
    }

    float Now => clock != null ? clock.ElapsedActiveSeconds : 0f;

    void Expire(JunctionState state) {
        float now = Now;
        for (int i = state.holders.Count - 1; i >= 0; i--) {
            if (state.holders[i].expiresAt <= now) {
                ReturnHolder(state.holders[i]);
                state.holders.RemoveAt(i);
            }
        }
    }

    void RemoveFrom(JunctionState state, int lifeId) {
        for (int i = state.holders.Count - 1; i >= 0; i--) {
            if (state.holders[i].lifeId == lifeId) {
                ReturnHolder(state.holders[i]);
                state.holders.RemoveAt(i);
            }
        }
        for (int i = state.waiters.Count - 1; i >= 0; i--) {
            if (state.waiters[i].lifeId == lifeId) {
                ReturnWaiter(state.waiters[i]);
                state.waiters.RemoveAt(i);
            }
        }
    }

    static bool TryGetTransition(JunctionRecord record, string fromEdgeId, string toEdgeId, out int priority) {
        priority = 0;
        if (record == null || record.allowedTransitions == null || string.IsNullOrEmpty(fromEdgeId) || string.IsNullOrEmpty(toEdgeId)) return false;
        foreach (var transition in record.allowedTransitions) {
            if (transition != null && transition.fromEdgeId == fromEdgeId && transition.toEdgeId == toEdgeId) {
                priority = transition.priority;
                return true;
            }
        }
        return false;
    }

    static bool SameMovement(string fromA, string toA, string fromB, string toB) => fromA == fromB && toA == toB;

    static bool IsCompatibleWithHolders(JunctionState state, string fromEdgeId, string toEdgeId) {
        foreach (var holder in state.holders) {
            if (!SameMovement(holder.fromEdgeId, holder.toEdgeId, fromEdgeId, toEdgeId)) return false;
        }
        return true;
    }

    static void InsertWaiter(JunctionState state, Waiter waiter) {
        int index = state.waiters.Count;
        for (int i = 0; i < state.waiters.Count; i++) {
            if (Precedes(waiter, state.waiters[i])) {
                index = i;
                break;
            }
        }
        state.waiters.Insert(index, waiter);
    }

    /// <summary>Stable ordering: earlier arrival first; same arrival → higher authored priority first; then lower life id.</summary>
    static bool Precedes(Waiter a, Waiter b) {
        if (a.arrivedAt != b.arrivedAt) return a.arrivedAt < b.arrivedAt;
        if (a.priority != b.priority) return a.priority > b.priority;
        return a.lifeId < b.lifeId;
    }

    Holder RentHolder() {
        if (holderPool.Count == 0) return new Holder();
        var holder = holderPool[holderPool.Count - 1];
        holderPool.RemoveAt(holderPool.Count - 1);
        return holder;
    }

    void ReturnHolder(Holder holder) => holderPool.Add(holder);

    Waiter RentWaiter() {
        if (waiterPool.Count == 0) return new Waiter();
        var waiter = waiterPool[waiterPool.Count - 1];
        waiterPool.RemoveAt(waiterPool.Count - 1);
        return waiter;
    }

    void ReturnWaiter(Waiter waiter) => waiterPool.Add(waiter);
}
