using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Session-owned iterative blast resolver. Requests are immutable snapshots, IDs remain deduplicated
/// for the entire session, and incomplete queries never emit partial damage. Buffers grow within
/// explicit limits and remain reusable. Composition owns receiver dispatch and must check each
/// receiver's session/life gate again when applying returned results.
/// </summary>
public sealed class VehicleExplosionService {
    /// <summary>Outcome of the last tick; capacity/query faults require an integration diagnostic.</summary>
    public enum ProcessingStatus {
        /// <summary>All accepted pending blasts were resolved.</summary>
        Completed,
        /// <summary>Work remains queued after a blast/query budget was used.</summary>
        TickBudgetReached,
        /// <summary>The maximum buffer is still full; the head remains pending without partial damage.</summary>
        VictimCapacityReached,
        /// <summary>The query count violates its contract; the head remains pending.</summary>
        InvalidQueryResult,
        /// <summary>The session ended permanently and pending work was cancelled.</summary>
        SessionEnded
    }

    readonly Queue<PendingBlast.Snapshot> queue = new Queue<PendingBlast.Snapshot>();
    readonly HashSet<string> acceptedBlastIds = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<int> seenLifeIdsScratch;
    readonly IBlastVictimQuery victimQuery;
    readonly BlastRoleMultipliers roleMultipliers;
    readonly int maxBlastsProcessedPerTick;
    readonly int maxQueriesPerTick;
    readonly int maxTrackedBlastIds;
    readonly int maxVictimCapacity;
    BlastVictim[] ownedVictimBuffer;
    bool sessionActive = true;

    /// <summary>Accepted blasts awaiting a complete query, including any blocked head blast.</summary>
    public int PendingCount => queue.Count;
    /// <summary>Pending and completed IDs retained until session end, bounded by MaxTrackedBlastIds.</summary>
    public int TrackedBlastCount => acceptedBlastIds.Count;
    /// <summary>Whole-session event budget; exhaustion explicitly rejects new IDs without evicting old ones.</summary>
    public int MaxTrackedBlastIds => maxTrackedBlastIds;
    /// <summary>Maximum query capacity; allow one spare entry beyond the largest expected collider count.</summary>
    public int MaxVictimCapacity => maxVictimCapacity;
    /// <summary>Service-owned scratch capacity, retained after growth for subsequent ticks.</summary>
    public int VictimBufferCapacity => ownedVictimBuffer.Length;
    /// <summary>False permanently after SetSessionActive(false); a new session needs a new service.</summary>
    public bool IsSessionActive => sessionActive;
    /// <summary>Inspect after every tick; capacity/query faults retain work and must not be silently ignored.</summary>
    public ProcessingStatus LastProcessStatus { get; private set; }

    /// <summary>
    /// Compatibility constructor with bounded fallback limits: 16 initial candidates, 4096 maximum,
    /// 64 queries/tick and 65536 session IDs. Runtime composition should use the explicit overload
    /// with authored budgets. Non-positive legacy blast budgets still resolve to one.
    /// </summary>
    public VehicleExplosionService(IBlastVictimQuery victimQuery, BlastRoleMultipliers roleMultipliers,
        int maxBlastsProcessedPerTick)
        : this(victimQuery, roleMultipliers, Mathf.Max(1, maxBlastsProcessedPerTick), 16, 4096, 64, 65536) { }

    /// <summary>Creates a session service with explicit positive budgets and a detached copy of role tuning.</summary>
    /// <param name="victimQuery">Required side-effect-free adapter using the saturation contract.</param>
    /// <param name="roleMultipliers">Finite non-negative tuning copied at construction; null uses existing defaults.</param>
    /// <param name="maxBlastsProcessedPerTick">Maximum completely resolved blasts in one call.</param>
    /// <param name="initialVictimCapacity">Initial scratch capacity; set equal to maximum to prewarm query storage.</param>
    /// <param name="maxVictimCapacity">Hard scratch limit, including a spare entry to certify completeness.</param>
    /// <param name="maxQueriesPerTick">Hard query-call limit, including retries after saturation.</param>
    /// <param name="maxTrackedBlastIds">Hard whole-session accepted-ID limit, including completed blasts.</param>
    /// <exception cref="ArgumentException">A budget, required dependency or role multiplier is invalid.</exception>
    public VehicleExplosionService(IBlastVictimQuery victimQuery, BlastRoleMultipliers roleMultipliers,
        int maxBlastsProcessedPerTick, int initialVictimCapacity, int maxVictimCapacity,
        int maxQueriesPerTick, int maxTrackedBlastIds) {
        this.victimQuery = victimQuery ?? throw new ArgumentNullException(nameof(victimQuery));
        if (maxBlastsProcessedPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(maxBlastsProcessedPerTick));
        if (initialVictimCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(initialVictimCapacity));
        if (maxVictimCapacity < initialVictimCapacity) throw new ArgumentOutOfRangeException(nameof(maxVictimCapacity));
        if (maxQueriesPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(maxQueriesPerTick));
        if (maxTrackedBlastIds <= 0) throw new ArgumentOutOfRangeException(nameof(maxTrackedBlastIds));
        var roles = roleMultipliers ?? new BlastRoleMultipliers();
        if (!IsNonNegativeFinite(roles.civilianMultiplier) || !IsNonNegativeFinite(roles.policeMultiplier) ||
            !IsNonNegativeFinite(roles.playerMultiplier))
            throw new ArgumentException("Blast role multipliers must be finite and non-negative.", nameof(roleMultipliers));

        this.roleMultipliers = new BlastRoleMultipliers {
            civilianMultiplier = roles.civilianMultiplier,
            policeMultiplier = roles.policeMultiplier,
            playerMultiplier = roles.playerMultiplier
        };
        this.maxBlastsProcessedPerTick = maxBlastsProcessedPerTick;
        this.maxVictimCapacity = maxVictimCapacity;
        this.maxQueriesPerTick = maxQueriesPerTick;
        this.maxTrackedBlastIds = maxTrackedBlastIds;
        ownedVictimBuffer = new BlastVictim[initialVictimCapacity];
        seenLifeIdsScratch = new HashSet<int>(maxVictimCapacity);
    }

    /// <summary>
    /// Compatibility submission; null, duplicate and ended-session requests are ignored. Invalid
    /// payloads and exhausted session-ID budgets throw before accepting or consuming an ID.
    /// </summary>
    public void EnqueueBlast(PendingBlast blast) {
        TryEnqueueBlast(blast);
    }

    /// <summary>Snapshots a new blast once. False means null, duplicate or ended session; true means queued.</summary>
    /// <exception cref="ArgumentException">The ID is blank or spatial/damage values are invalid.</exception>
    /// <exception cref="InvalidOperationException">The session-ID budget is exhausted; no event was accepted.</exception>
    public bool TryEnqueueBlast(PendingBlast blast) {
        if (!sessionActive || blast == null) return false;
        if (string.IsNullOrWhiteSpace(blast.blastId))
            throw new ArgumentException("A blast requires a non-empty session event ID.", nameof(blast));
        if (acceptedBlastIds.Contains(blast.blastId)) return false;
        if (!IsFinite(blast.origin.x) || !IsFinite(blast.origin.y) || !IsNonNegativeFinite(blast.baseBlast) ||
            !IsFinite(blast.blastRadius) || blast.blastRadius <= 0f)
            throw new ArgumentException("Blast origin, damage and positive radius must be finite.", nameof(blast));
        if (acceptedBlastIds.Count >= maxTrackedBlastIds)
            throw new InvalidOperationException("The whole-session blast ID budget is exhausted.");

        queue.Enqueue(new PendingBlast.Snapshot(blast));
        acceptedBlastIds.Add(blast.blastId);
        return true;
    }

    /// <summary>False permanently closes the service and clears pending work/IDs; true never reopens it.</summary>
    public void SetSessionActive(bool active) {
        sessionActive &= active;
        if (sessionActive) return;
        queue.Clear();
        acceptedBlastIds.Clear();
        seenLifeIdsScratch.Clear();
        LastProcessStatus = ProcessingStatus.SessionEnded;
    }

    /// <summary>
    /// Allocating compatibility wrapper. Inspect LastProcessStatus for deferred work or query faults.
    /// Runtime callers should use a reusable output list overload instead.
    /// </summary>
    public List<BlastApplication> ProcessTick(BlastVictim[] victimBuffer) {
        var applications = new List<BlastApplication>();
        ProcessTick(victimBuffer, applications);
        return applications;
    }

    /// <summary>
    /// Clears and fills caller-owned output using service-owned query storage. Returns completed blast
    /// count, not application count. Reuses storage after query/output capacity is warmed; inspect
    /// LastProcessStatus after each call. Buffer growth can allocate until the configured maximum.
    /// </summary>
    public int ProcessTick(List<BlastApplication> applications) {
        return ProcessTick(ownedVictimBuffer, applications);
    }

    /// <summary>
    /// Clears output and resolves at most the blast/query budgets. A full query grows and retries before
    /// emitting any damage from that blast. At the hard limit or an invalid query count, the head stays
    /// queued and LastProcessStatus reports the fault; earlier completed applications remain available.
    /// Null/empty/oversized storage throws without consuming work. Recheck receiver session/life validity
    /// on each dispatch. No new output list is allocated; callers should pre-size its capacity.
    /// </summary>
    /// <param name="victimBuffer">Reusable scratch no larger than MaxVictimCapacity; larger owned storage may replace it.</param>
    /// <param name="applications">Required reusable output; cleared before every tick, including ended sessions.</param>
    /// <returns>Completely resolved blast count, including blasts with no effective victims.</returns>
    public int ProcessTick(BlastVictim[] victimBuffer, List<BlastApplication> applications) {
        if (applications == null) throw new ArgumentNullException(nameof(applications));
        applications.Clear();
        if (!sessionActive) { LastProcessStatus = ProcessingStatus.SessionEnded; return 0; }
        if (victimBuffer == null) throw new ArgumentNullException(nameof(victimBuffer));
        if (victimBuffer.Length == 0 || victimBuffer.Length > maxVictimCapacity)
            throw new ArgumentOutOfRangeException(nameof(victimBuffer));

        var buffer = victimBuffer.Length >= ownedVictimBuffer.Length ? victimBuffer : ownedVictimBuffer;
        LastProcessStatus = ProcessingStatus.Completed;
        int processed = 0;
        int queriesRemaining = maxQueriesPerTick;
        while (sessionActive && queue.Count > 0 && processed < maxBlastsProcessedPerTick) {
            var blast = queue.Peek();
            if (!TryQueryComplete(blast, ref buffer, ref queriesRemaining, out int count)) break;
            ProcessOneBlast(blast, buffer, count, applications);
            queue.Dequeue();
            processed++;
        }
        if (!sessionActive) { applications.Clear(); LastProcessStatus = ProcessingStatus.SessionEnded; }
        else if (queue.Count > 0 && LastProcessStatus == ProcessingStatus.Completed)
            LastProcessStatus = ProcessingStatus.TickBudgetReached;
        return processed;
    }

    bool TryQueryComplete(PendingBlast.Snapshot blast, ref BlastVictim[] buffer, ref int queriesRemaining, out int count) {
        count = 0;
        while (queriesRemaining > 0) {
            queriesRemaining--;
            count = victimQuery.FindVictimsInRadius(blast.origin, blast.blastRadius, buffer);
            if (!sessionActive) return false;
            if (count < 0 || count > buffer.Length) { LastProcessStatus = ProcessingStatus.InvalidQueryResult; return false; }
            if (count < buffer.Length) return true;
            if (buffer.Length >= maxVictimCapacity) { LastProcessStatus = ProcessingStatus.VictimCapacityReached; return false; }
            int capacity = buffer.Length > maxVictimCapacity / 2 ? maxVictimCapacity : buffer.Length * 2;
            ownedVictimBuffer = new BlastVictim[capacity];
            buffer = ownedVictimBuffer;
        }
        LastProcessStatus = ProcessingStatus.TickBudgetReached;
        return false;
    }

    void ProcessOneBlast(PendingBlast.Snapshot blast, BlastVictim[] buffer, int count, List<BlastApplication> applications) {
        seenLifeIdsScratch.Clear();
        for (int i = 0; i < count; i++) {
            var victim = buffer[i];
            if (victim.lifeId <= 0 || !IsFinite(victim.position.x) || !IsFinite(victim.position.y) ||
                !IsFinite(victim.explosionResistance) || !seenLifeIdsScratch.Add(victim.lifeId)) continue;
            float distance = Vector2.Distance(blast.origin, victim.position);
            // Widen intermediates so finite authored values cannot overflow into an ignored hit.
            double damage = (double)blast.baseBlast * BlastDamageMath.ComputeDistanceFalloff(distance, blast.blastRadius) *
                roleMultipliers.Resolve(victim.role) * (1f - Mathf.Clamp01(victim.explosionResistance));
            if (damage <= 0d) continue;
            applications.Add(new BlastApplication(blast.blastId, victim.lifeId, victim.role,
                (float)Math.Min(float.MaxValue, damage), blast.context));
        }
    }

    static bool IsNonNegativeFinite(float value) => IsFinite(value) && value >= 0f;
    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
