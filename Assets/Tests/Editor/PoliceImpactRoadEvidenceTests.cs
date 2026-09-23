using NUnit.Framework;

/// <summary>Pure lifetime and eligibility coverage for same-step police impact road evidence.</summary>
public sealed class PoliceImpactRoadEvidenceTests {
    /// <summary>Checks one valid observation resolves once with exact anchor, graph, life, and role identity.</summary>
    [Test]
    public void Observe_ValidVersionZeroEvidenceResolvesOnce() {
        var evidence = new PoliceImpactRoadEvidence();
        var anchor = new RoadPathQuery.EdgeAnchor("road-a", 4.25f);
        VehicleIdentity police = new VehicleIdentity(11, VehicleRole.Police);
        VehicleIdentity target = new VehicleIdentity(22, VehicleRole.Player);

        evidence.Observe(anchor, 0L, police, target);
        Assert.IsTrue(evidence.TryResolveImpactAnchor(false, default, 0L, police, target, true, out var resolved));
        AssertAnchor(anchor, resolved);
        Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 0L, police, target, true, out _));
    }

    /// <summary>Checks Clear is idempotent and the next resolve cannot reuse expired evidence.</summary>
    [Test]
    public void Clear_ExpiresPendingEvidenceAndRepeatedClearStaysEmpty() {
        var evidence = new PoliceImpactRoadEvidence();
        evidence.Observe(new RoadPathQuery.EdgeAnchor("road-a", 2f), 3L, Police(), Player());
        evidence.Clear();
        evidence.Clear();

        Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 3L, Police(), Player(), true, out var resolved));
        AssertDefault(resolved);
    }

    /// <summary>Checks a second observation replaces, rather than queues behind, the first observation.</summary>
    [Test]
    public void Observe_NewObservationReplacesOlderPendingEvidence() {
        var evidence = new PoliceImpactRoadEvidence();
        var police = Police();
        var target = Player();
        evidence.Observe(new RoadPathQuery.EdgeAnchor("old", 1f), 5L, police, target);
        var latest = new RoadPathQuery.EdgeAnchor("latest", 9f);
        evidence.Observe(latest, 5L, police, target);

        Assert.IsTrue(evidence.TryResolveImpactAnchor(false, default, 5L, police, target, true, out var resolved));
        AssertAnchor(latest, resolved);
    }

    /// <summary>Checks a current valid Road anchor wins and consumes pending evidence even when pending use is disabled.</summary>
    [Test]
    public void TryResolve_CurrentRoadAnchorTakesPreferenceAndConsumesPending() {
        var evidence = new PoliceImpactRoadEvidence();
        var police = Police();
        var target = Player();
        evidence.Observe(new RoadPathQuery.EdgeAnchor("pending", 3f), 7L, police, target);
        var current = new RoadPathQuery.EdgeAnchor("current", 6f);

        Assert.IsTrue(evidence.TryResolveImpactAnchor(true, current, 999L, default, default, false, out var resolved));
        AssertAnchor(current, resolved);
        Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 7L, police, target, true, out _));
    }

    /// <summary>Checks an invalid current anchor cannot fall back to otherwise matching pending evidence.</summary>
    [Test]
    public void TryResolve_InvalidCurrentAnchorRefusesWithoutPendingFallback() {
        var evidence = new PoliceImpactRoadEvidence();
        var police = Police();
        var target = Player();
        evidence.Observe(new RoadPathQuery.EdgeAnchor("pending", 3f), 7L, police, target);

        Assert.IsFalse(evidence.TryResolveImpactAnchor(true, default, 7L, police, target, true, out var resolved));
        AssertDefault(resolved);
        Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 7L, police, target, true, out _));
    }

    /// <summary>Checks pending evidence requires explicit allowPending and is consumed on refusal.</summary>
    [Test]
    public void TryResolve_DisallowedPendingEvidenceExpiresWithoutResolution() {
        var evidence = new PoliceImpactRoadEvidence();
        var police = Police();
        var target = Player();
        evidence.Observe(new RoadPathQuery.EdgeAnchor("pending", 3f), 7L, police, target);

        Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 7L, police, target, false, out var resolved));
        AssertDefault(resolved);
        Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 7L, police, target, true, out _));
    }

    /// <summary>Checks graph-version equality, including valid zero and mismatch consumption semantics.</summary>
    [Test]
    public void TryResolve_VersionMustMatchExactlyAndMismatchConsumes() {
        var evidence = new PoliceImpactRoadEvidence();
        var police = Police();
        var target = Player();
        evidence.Observe(new RoadPathQuery.EdgeAnchor("road", 4f), 12L, police, target);
        Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 13L, police, target, true, out var mismatch));
        AssertDefault(mismatch);
        Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 12L, police, target, true, out _));

        evidence.Observe(new RoadPathQuery.EdgeAnchor("zero", 0f), 0L, police, target);
        Assert.IsTrue(evidence.TryResolveImpactAnchor(false, default, 0L, police, target, true, out var zero));
        AssertAnchor(new RoadPathQuery.EdgeAnchor("zero", 0f), zero);
    }

    /// <summary>Checks every invalid observation form fails closed and clears any previous valid observation first.</summary>
    [Test]
    public void Observe_InvalidAnchorInputsAreRejectedAndCannotResurrectOldEvidence() {
        var invalidAnchors = new[] {
            new RoadPathQuery.EdgeAnchor(null, 1f),
            new RoadPathQuery.EdgeAnchor(string.Empty, 1f),
            new RoadPathQuery.EdgeAnchor("negative", -1f),
            new RoadPathQuery.EdgeAnchor("nan", float.NaN),
            new RoadPathQuery.EdgeAnchor("positive-infinity", float.PositiveInfinity),
            new RoadPathQuery.EdgeAnchor("negative-infinity", float.NegativeInfinity)
        };

        foreach (var invalid in invalidAnchors) {
            var evidence = new PoliceImpactRoadEvidence();
            evidence.Observe(new RoadPathQuery.EdgeAnchor("old", 2f), 1L, Police(), Player());
            evidence.Observe(invalid, 1L, Police(), Player());
            Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 1L, Police(), Player(), true, out var resolved),
                "invalid anchor unexpectedly resolved: " + invalid.edgeId);
            AssertDefault(resolved);
        }
    }

    /// <summary>Checks invalid identity roles and life identifiers are rejected without allowing cross-life evidence.</summary>
    [Test]
    public void Observe_InvalidRolesAndLifeIdsAreRejected() {
        var invalidPairs = new[] {
            new IdentityPair(new VehicleIdentity(0, VehicleRole.Police), Player()),
            new IdentityPair(new VehicleIdentity(-1, VehicleRole.Police), Player()),
            new IdentityPair(new VehicleIdentity(11, VehicleRole.Civilian), Player()),
            new IdentityPair(Police(), new VehicleIdentity(0, VehicleRole.Player)),
            new IdentityPair(Police(), new VehicleIdentity(-1, VehicleRole.Player)),
            new IdentityPair(Police(), new VehicleIdentity(22, VehicleRole.Civilian))
        };

        foreach (var pair in invalidPairs) {
            var evidence = new PoliceImpactRoadEvidence();
            evidence.Observe(new RoadPathQuery.EdgeAnchor("road", 1f), 4L, pair.police, pair.target);
            Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 4L, Police(), Player(), true, out var resolved));
            AssertDefault(resolved);
        }
    }

    /// <summary>Checks matching life and role identities are required even when the graph version is correct.</summary>
    [Test]
    public void TryResolve_MismatchedLifeOrRoleCannotConsumeAsMatchingEvidence() {
        var mismatches = new[] {
            new IdentityPair(new VehicleIdentity(12, VehicleRole.Police), Player()),
            new IdentityPair(Police(), new VehicleIdentity(23, VehicleRole.Player)),
            new IdentityPair(new VehicleIdentity(11, VehicleRole.Civilian), Player()),
            new IdentityPair(Police(), new VehicleIdentity(22, VehicleRole.Civilian))
        };

        foreach (var pair in mismatches) {
            var evidence = new PoliceImpactRoadEvidence();
            evidence.Observe(new RoadPathQuery.EdgeAnchor("road", 1f), 4L, Police(), Player());
            Assert.IsFalse(evidence.TryResolveImpactAnchor(false, default, 4L, pair.police, pair.target, true, out var resolved));
            AssertDefault(resolved);
        }
    }

    static VehicleIdentity Police() => new VehicleIdentity(11, VehicleRole.Police);
    static VehicleIdentity Player() => new VehicleIdentity(22, VehicleRole.Player);

    static void AssertAnchor(RoadPathQuery.EdgeAnchor expected, RoadPathQuery.EdgeAnchor actual) {
        Assert.AreEqual(expected.edgeId, actual.edgeId);
        Assert.AreEqual(expected.distanceAlongEdge, actual.distanceAlongEdge);
    }

    static void AssertDefault(RoadPathQuery.EdgeAnchor actual) {
        Assert.IsNull(actual.edgeId);
        Assert.AreEqual(0f, actual.distanceAlongEdge);
    }

    readonly struct IdentityPair {
        internal readonly VehicleIdentity police;
        internal readonly VehicleIdentity target;

        internal IdentityPair(VehicleIdentity police, VehicleIdentity target) {
            this.police = police;
            this.target = target;
        }
    }
}
