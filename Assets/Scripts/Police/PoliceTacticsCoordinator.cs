using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Pure role, approach-claim, and target-fact policy for free-drive police lives.</summary>
public sealed class PoliceTacticsCoordinator {
    /// <summary>Approach positions distributed around the current player target.</summary>
    public enum ApproachSlot { Rear, Left, Right, Front }

    /// <summary>Immutable target identity supplied by the current session owner.</summary>
    public readonly struct TargetFacts {
        /// <summary>Creates target identity facts.</summary>
        public TargetFacts(int targetLifeId, int targetRevision) { this.targetLifeId = targetLifeId; this.targetRevision = targetRevision; }
        /// <summary>Player life identity.</summary>
        public readonly int targetLifeId;
        /// <summary>Monotonic target fact revision.</summary>
        public readonly int targetRevision;
    }

    /// <summary>Result of a role assignment attempt.</summary>
    public readonly struct RoleDecision {
        /// <summary>Creates a role decision result.</summary>
        public RoleDecision(bool accepted, bool changed, PoliceTacticalRole role) { this.accepted = accepted; this.changed = changed; this.role = role; }
        /// <summary>Whether the life and profile facts were accepted.</summary>
        public readonly bool accepted;
        /// <summary>Whether a new role was actually bound.</summary>
        public readonly bool changed;
        /// <summary>Role to use for the current intent.</summary>
        public readonly PoliceTacticalRole role;
    }

    /// <summary>Stable approach claim with life, target, and expiry provenance.</summary>
    public readonly struct ApproachClaim {
        /// <summary>Creates an approach claim.</summary>
        public ApproachClaim(int life, TargetFacts target, ApproachSlot approach, float expiry) { lifeId = life; targetFacts = target; slot = approach; expiresAt = expiry; }
        /// <summary>Police life owning the claim.</summary>
        public readonly int lifeId;
        /// <summary>Target identity captured at claim time.</summary>
        public readonly TargetFacts targetFacts;
        /// <summary>Approach slot claimed.</summary>
        public readonly ApproachSlot slot;
        /// <summary>Active-clock expiry time.</summary>
        public readonly float expiresAt;
    }

    sealed class LifeState {
        public PoliceTacticalRole role;
        public float roleBoundAt;
        public int assignmentCount;
        public TargetFacts target;
        public PoliceDriverTraits traits = PoliceDriverTraits.Reset();
        public ApproachClaim claim;
        public bool hasRole;
        public bool hasTarget;
        public bool hasClaim;
        public Dictionary<PoliceTacticalRole, float> weights;
        public bool hasOpening;
        public Vector2 opening;
        public float openingExpiresAt;
    }

    readonly PoliceFreeChaseSettings settings;
    readonly Dictionary<int, LifeState> lives = new Dictionary<int, LifeState>();
    static readonly PoliceTacticalRole[] roles = { PoliceTacticalRole.Pursue, PoliceTacticalRole.Intercept, PoliceTacticalRole.Ram };

    /// <summary>Creates a coordinator from detached cadence and hold settings.</summary>
    /// <param name="chaseSettings">Frozen session chase settings.</param>
    public PoliceTacticsCoordinator(PoliceFreeChaseSettings chaseSettings) {
        settings = chaseSettings != null ? chaseSettings.Clone() : new PoliceFreeChaseSettings();
    }

    /// <summary>Starts or replaces one life identity and clears stale claims and target facts.</summary>
    /// <param name="lifeId">Fresh positive police life identity.</param>
    /// <param name="target">Current target facts.</param>
    /// <param name="traits">Detached traits for this life.</param>
    /// <returns>True when a fresh life state was accepted.</returns>
    public bool BeginLife(int lifeId, TargetFacts target, PoliceDriverTraits traits) {
        if (lifeId <= 0 || target.targetLifeId <= 0 || target.targetRevision < 0) return false;
        var state = new LifeState { target = target, traits = traits ?? PoliceDriverTraits.Reset(), hasTarget = true };
        lives[lifeId] = state;
        return true;
    }

    /// <summary>Clears one life, its role, its traits ownership, and any approach claim.</summary>
    /// <param name="lifeId">Life identity to release.</param>
    /// <returns>True when a live state was released.</returns>
    public bool EndLife(int lifeId) => lives.Remove(lifeId);

    /// <summary>Assigns a weighted supported role at cadence without rebinding every tick.</summary>
    /// <param name="lifeId">Police life identity.</param>
    /// <param name="profile">Authored stable vehicle profile.</param>
    /// <param name="activeSeconds">Current active session clock.</param>
    /// <param name="feasibleRoles">Roles currently physically feasible.</param>
    /// <returns>An accepted decision, or an unchanged/rejected result.</returns>
    public RoleDecision AssignRole(int lifeId, PoliceVehicleProfile profile, float activeSeconds, ISet<PoliceTacticalRole> feasibleRoles = null) {
        if (!lives.TryGetValue(lifeId, out LifeState state) || profile == null || !Finite(activeSeconds)) return new RoleDecision(false, false, PoliceTacticalRole.Pursue);
        if (state.weights == null) {
            if (!profile.SupportsRole(PoliceTacticalRole.Pursue)) return new RoleDecision(false, false, PoliceTacticalRole.Pursue);
            state.weights = new Dictionary<PoliceTacticalRole, float>();
            foreach (PoliceTacticalRole role in roles)
                if (profile.SupportsRole(role) && profile.TryGetRoleWeight(role, out float weight)) state.weights.Add(role, weight);
        }
        if (state.hasRole && activeSeconds - state.roleBoundAt < settings.minimumRoleHoldSeconds) return new RoleDecision(true, false, state.role);
        if (state.hasRole && activeSeconds - state.roleBoundAt < settings.roleCadenceSeconds) return new RoleDecision(true, false, state.role);
        if (!TrySelectRole(state.weights, state.assignmentCount++, state.traits, feasibleRoles, out PoliceTacticalRole selected)) return new RoleDecision(false, false, state.hasRole ? state.role : PoliceTacticalRole.Pursue);
        bool changed = !state.hasRole || state.role != selected;
        state.role = selected;
        state.roleBoundAt = activeSeconds;
        state.hasRole = true;
        if (changed) state.hasClaim = false;
        return new RoleDecision(true, changed, selected);
    }

    /// <summary>Predicts and validates one measured target fact update without accepting stale identity.</summary>
    /// <param name="lifeId">Police life identity receiving the update.</param>
    /// <param name="target">New target facts.</param>
    /// <returns>True when the revision is current or newer for the live target.</returns>
    public bool TryAcceptTargetFacts(int lifeId, TargetFacts target) {
        if (!lives.TryGetValue(lifeId, out LifeState state) || target.targetLifeId <= 0 || target.targetRevision < 0) return false;
        if (state.hasTarget && (target.targetLifeId != state.target.targetLifeId || target.targetRevision < state.target.targetRevision)) return false;
        state.target = target;
        state.hasTarget = true;
        if (state.hasClaim && (state.claim.targetFacts.targetLifeId != target.targetLifeId || state.claim.targetFacts.targetRevision != target.targetRevision)) state.hasClaim = false;
        return true;
    }

    /// <summary>Claims an approach slot only for current life and target facts.</summary>
    /// <param name="lifeId">Police life identity.</param>
    /// <param name="target">Target facts captured by the caller.</param>
    /// <param name="slot">Approach slot to claim.</param>
    /// <param name="activeSeconds">Current active session clock.</param>
    /// <param name="duration">Claim lifetime.</param>
    /// <param name="claim">Created claim when accepted.</param>
    /// <returns>True when the claim is current and non-expired.</returns>
    public bool TryClaimApproach(int lifeId, TargetFacts target, ApproachSlot slot, float activeSeconds, float duration, out ApproachClaim claim) {
        claim = default;
        if (!lives.TryGetValue(lifeId, out LifeState state) || !Finite(activeSeconds) || !Finite(duration) || duration <= 0f ||
            !state.hasTarget || state.target.targetLifeId != target.targetLifeId || state.target.targetRevision != target.targetRevision) return false;
        if (!Finite(activeSeconds + duration)) return false;
        foreach (var pair in lives) {
            LifeState other = pair.Value;
            if (pair.Key != lifeId && other.hasClaim && other.claim.slot == slot &&
                other.claim.targetFacts.targetLifeId == target.targetLifeId && other.claim.expiresAt > activeSeconds) return false;
        }
        claim = new ApproachClaim(lifeId, target, slot, activeSeconds + duration);
        state.claim = claim;
        state.hasClaim = true;
        return true;
    }

    /// <summary>Checks claim provenance without extending its expiry.</summary>
    /// <param name="claim">Claim to validate.</param>
    /// <param name="activeSeconds">Current active session clock.</param>
    /// <returns>True only when life, target, revision, and expiry still match.</returns>
    public bool IsClaimCurrent(ApproachClaim claim, float activeSeconds) {
        return lives.TryGetValue(claim.lifeId, out LifeState state) && state.hasClaim &&
            state.claim.targetFacts.targetLifeId == claim.targetFacts.targetLifeId &&
            state.claim.targetFacts.targetRevision == claim.targetFacts.targetRevision &&
            state.claim.slot == claim.slot && state.claim.expiresAt == claim.expiresAt &&
            state.claim.expiresAt > activeSeconds && Finite(activeSeconds);
    }

    /// <summary>Releases a claim only when its life and target provenance are still current.</summary>
    /// <param name="claim">Claim to release.</param>
    /// <returns>True when the current claim was cleared.</returns>
    public bool ReleaseApproach(ApproachClaim claim) {
        if (!lives.TryGetValue(claim.lifeId, out LifeState state) || !state.hasClaim ||
            state.claim.slot != claim.slot || state.claim.expiresAt != claim.expiresAt ||
            state.claim.targetFacts.targetLifeId != claim.targetFacts.targetLifeId ||
            state.claim.targetFacts.targetRevision != claim.targetFacts.targetRevision) return false;
        state.hasClaim = false;
        return true;
    }

    /// <summary>Returns whether a side-street mouth is free of every other unit's live opening claim.</summary>
    /// <param name="lifeId">Asking police life; its own claim never blocks it.</param>
    /// <param name="point">World mouth point.</param>
    /// <param name="radius">Claims closer than this count as the same mouth.</param>
    /// <param name="activeSeconds">Current active session clock.</param>
    /// <returns>True when no other live unit holds a mouth within the radius.</returns>
    public bool IsOpeningFree(int lifeId, Vector2 point, float radius, float activeSeconds) {
        if (!Finite(activeSeconds)) return false;
        float radiusSquared = radius * radius;
        foreach (var pair in lives) {
            LifeState other = pair.Value;
            if (pair.Key != lifeId && other.hasOpening && other.openingExpiresAt > activeSeconds &&
                (other.opening - point).sqrMagnitude < radiusSquared) return false;
        }
        return true;
    }

    /// <summary>Claims a side-street mouth for a Shadow unit so no other unit picks the same one.</summary>
    /// <param name="lifeId">Claiming police life.</param>
    /// <param name="point">World mouth point.</param>
    /// <param name="radius">Claims closer than this count as the same mouth.</param>
    /// <param name="activeSeconds">Current active session clock.</param>
    /// <param name="duration">Claim lifetime; the unit renews it while it keeps the mouth.</param>
    /// <returns>True when the claim was recorded.</returns>
    public bool TryClaimOpening(int lifeId, Vector2 point, float radius, float activeSeconds, float duration) {
        if (!lives.TryGetValue(lifeId, out LifeState state) || !Finite(duration) || duration <= 0f ||
            float.IsNaN(point.x) || float.IsNaN(point.y) || !IsOpeningFree(lifeId, point, radius, activeSeconds)) return false;
        state.hasOpening = true;
        state.opening = point;
        state.openingExpiresAt = activeSeconds + duration;
        return true;
    }

    /// <summary>Drops this unit's side-street claim.</summary>
    /// <param name="lifeId">Police life releasing its claim.</param>
    public void ReleaseOpening(int lifeId) {
        if (lives.TryGetValue(lifeId, out LifeState state)) state.hasOpening = false;
    }

    static bool TrySelectRole(Dictionary<PoliceTacticalRole, float> weights, int ordinal, PoliceDriverTraits traits, ISet<PoliceTacticalRole> feasible, out PoliceTacticalRole selected) {
        selected = PoliceTacticalRole.Pursue;
        float total = 0f;
        foreach (PoliceTacticalRole role in roles) {
            if ((feasible != null && !feasible.Contains(role)) || !weights.TryGetValue(role, out float weight)) continue;
            total += weight;
        }
        // A stable stratified sample varies repeated assignments without Unity's global RNG.
        float sample = Mathf.Repeat(0.5f + ordinal * 0.618033989f + traits.sidePreference + traits.riskBias, 1f) * total;
        foreach (PoliceTacticalRole role in roles) {
            if ((feasible != null && !feasible.Contains(role)) || !weights.TryGetValue(role, out float weight) || weight <= 0f) continue;
            sample -= weight;
            if (sample < 0f) { selected = role; break; }
        }
        if (feasible != null && !feasible.Contains(selected)) selected = PoliceTacticalRole.Pursue;
        return feasible == null || feasible.Contains(selected);
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
