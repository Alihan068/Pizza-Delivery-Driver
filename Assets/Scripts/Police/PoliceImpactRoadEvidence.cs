/// <summary>Stores one measured same-step road anchor for a guarded impact callback.</summary>
public sealed class PoliceImpactRoadEvidence {
    RoadPathQuery.EdgeAnchor pendingAnchor;
    VehicleIdentity pendingPolice;
    VehicleIdentity pendingTarget;
    long pendingVersion;
    bool hasPending;

    /// <summary>Clears the single pending observation; repeated clearing is harmless.</summary>
    public void Clear() {
        pendingAnchor = default;
        pendingPolice = default;
        pendingTarget = default;
        pendingVersion = 0L;
        hasPending = false;
    }

    /// <summary>Records only one finite, positive-life, role-validated measured road observation.</summary>
    /// <param name="anchor">The successfully measured current road cursor anchor.</param>
    /// <param name="version">The graph version token associated with that measurement.</param>
    /// <param name="police">The measured police life identity.</param>
    /// <param name="target">The measured player life identity.</param>
    public void Observe(RoadPathQuery.EdgeAnchor anchor, long version, VehicleIdentity police, VehicleIdentity target) {
        Clear();
        if (!ValidAnchor(anchor) || !ValidIdentity(police, VehicleRole.Police) || !ValidIdentity(target, VehicleRole.Player)) return;
        pendingAnchor = anchor;
        pendingVersion = version;
        pendingPolice = police;
        pendingTarget = target;
        hasPending = true;
    }

    /// <summary>Consumes the observation once, preferring a valid current road anchor over pending evidence.</summary>
    /// <param name="hasCurrentRoad">Whether the caller has a currently bound Road cursor.</param>
    /// <param name="currentAnchor">The caller's current measured anchor when available.</param>
    /// <param name="version">The current graph version token.</param>
    /// <param name="police">The current police life identity.</param>
    /// <param name="target">The current player life identity.</param>
    /// <param name="allowPending">Whether pending same-step evidence may be used.</param>
    /// <param name="resolved">The selected measured anchor, or default on refusal.</param>
    /// <returns>True when a valid current or matching pending anchor was consumed.</returns>
    public bool TryResolveImpactAnchor(bool hasCurrentRoad, RoadPathQuery.EdgeAnchor currentAnchor,
        long version, VehicleIdentity police, VehicleIdentity target, bool allowPending,
        out RoadPathQuery.EdgeAnchor resolved) {
        resolved = default;
        bool pending = hasPending;
        RoadPathQuery.EdgeAnchor candidate = pendingAnchor;
        VehicleIdentity candidatePolice = pendingPolice;
        VehicleIdentity candidateTarget = pendingTarget;
        long candidateVersion = pendingVersion;
        Clear();
        if (hasCurrentRoad) {
            if (!ValidAnchor(currentAnchor)) return false;
            resolved = currentAnchor;
            return true;
        }
        if (!allowPending || !pending || !ValidAnchor(candidate) || candidateVersion != version ||
            !SameIdentity(candidatePolice, police, VehicleRole.Police) || !SameIdentity(candidateTarget, target, VehicleRole.Player)) return false;
        resolved = candidate;
        return true;
    }

    static bool ValidAnchor(RoadPathQuery.EdgeAnchor anchor) => !string.IsNullOrEmpty(anchor.edgeId) &&
        !float.IsNaN(anchor.distanceAlongEdge) && !float.IsInfinity(anchor.distanceAlongEdge) && anchor.distanceAlongEdge >= 0f;
    static bool ValidIdentity(VehicleIdentity identity, VehicleRole role) => identity.lifeId > 0 && identity.role == role;
    static bool SameIdentity(VehicleIdentity actual, VehicleIdentity expected, VehicleRole role) =>
        ValidIdentity(actual, role) && ValidIdentity(expected, role) && actual.lifeId == expected.lifeId;
}
