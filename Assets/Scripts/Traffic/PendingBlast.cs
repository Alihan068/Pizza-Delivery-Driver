using UnityEngine;

/// <summary>
/// Mutable submission DTO retained for caller compatibility. The explosion service copies every
/// field into an immutable snapshot on acceptance; later edits cannot alter queued gameplay.
/// A death caused by a blast submits a new ID through the queue, never recursive resolution.
/// </summary>
public sealed class PendingBlast {
    /// <summary>Non-empty ordinal ID unique to this explosion within the owning session.</summary>
    public string blastId;
    /// <summary>Finite world-space center captured when this request is accepted.</summary>
    public Vector2 origin;
    /// <summary>Finite, non-negative damage before falloff, role scaling and resistance.</summary>
    public float baseBlast;
    /// <summary>Finite, positive world-space radius used by both overlap and falloff.</summary>
    public float blastRadius;
    /// <summary>Event source and root attribution; the service normalizes its event ID and explosion kind.</summary>
    public DamageContext context;

    /// <summary>Builds a request; validation and a detached snapshot occur when the service accepts it.</summary>
    public PendingBlast(string blastId, Vector2 origin, float baseBlast, float blastRadius, DamageContext context) {
        this.blastId = blastId;
        this.origin = origin;
        this.baseBlast = baseBlast;
        this.blastRadius = blastRadius;
        this.context = context;
    }

    internal readonly struct Snapshot {
        internal readonly string blastId;
        internal readonly Vector2 origin;
        internal readonly float baseBlast;
        internal readonly float blastRadius;
        internal readonly DamageContext context;

        internal Snapshot(PendingBlast request) {
            blastId = request.blastId;
            origin = request.origin;
            baseBlast = request.baseBlast;
            blastRadius = request.blastRadius;
            context = DamageContextFactory.CreateChainedContext(request.context, blastId, request.context.sourceLifeId);
        }
    }
}
