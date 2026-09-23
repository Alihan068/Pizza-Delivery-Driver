using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Pure EditMode coverage for R04 role, prediction, trait, and identity policy.</summary>
public sealed class PoliceFreeTacticsTests {
    /// <summary>Weighted selection varies over assignments and claims remain exclusive.</summary>
    [Test]
    public void RepeatedAssignmentsVaryAndApproachClaimsAreExclusive() {
        var profile = MakeProfile(new[] { PoliceTacticalRole.Pursue, PoliceTacticalRole.Intercept },
            Weight(PoliceTacticalRole.Pursue, 0.5f), Weight(PoliceTacticalRole.Intercept, 0.5f));
        var coordinator = NewCoordinator();
        coordinator.BeginLife(1, Target(8, 1), PoliceDriverTraits.Reset());
        coordinator.BeginLife(2, Target(8, 1), PoliceDriverTraits.Reset());
        var roles = new HashSet<PoliceTacticalRole>();
        for (int index = 0; index < 10; index++) roles.Add(coordinator.AssignRole(1, profile, index * 3f).role);
        Assert.AreEqual(2, roles.Count);
        Assert.IsTrue(coordinator.TryClaimApproach(1, Target(8, 1), PoliceTacticsCoordinator.ApproachSlot.Left, 30f, 1f, out var original));
        Assert.IsFalse(coordinator.TryClaimApproach(2, Target(8, 1), PoliceTacticsCoordinator.ApproachSlot.Left, 30f, 1f, out _));
        Assert.IsTrue(coordinator.TryClaimApproach(1, Target(8, 1), PoliceTacticsCoordinator.ApproachSlot.Right, 30f, 1f, out _));
        Assert.IsFalse(coordinator.IsClaimCurrent(original, 30f));
        Object.DestroyImmediate(profile);
    }

    [Test]
    public void RoleWeightsFilterUnsupportedRolesAndFallbackToPursue() {
        var profile = MakeProfile(new[] { PoliceTacticalRole.Pursue, PoliceTacticalRole.Intercept },
            Weight(PoliceTacticalRole.Pursue, 0.2f), Weight(PoliceTacticalRole.Intercept, 0.8f));
        var coordinator = NewCoordinator();
        Assert.IsTrue(coordinator.BeginLife(7, Target(2, 1), PoliceDriverTraits.Reset()));
        var selected = coordinator.AssignRole(7, profile, 0f);
        Assert.IsTrue(selected.accepted);
        Assert.AreEqual(PoliceTacticalRole.Intercept, selected.role);
        var feasible = new HashSet<PoliceTacticalRole> { PoliceTacticalRole.Pursue };
        selected = coordinator.AssignRole(7, profile, 2.1f, feasible);
        Assert.AreEqual(PoliceTacticalRole.Pursue, selected.role);
        Object.DestroyImmediate(profile);
    }

    [Test]
    public void AuthoredStandardSportHeavyWeightsSelectDistinctRoles() {
        var standard = MakeProfile(new[] { PoliceTacticalRole.Pursue, PoliceTacticalRole.Intercept, PoliceTacticalRole.Ram },
            Weight(PoliceTacticalRole.Pursue, 0.70f), Weight(PoliceTacticalRole.Intercept, 0.20f), Weight(PoliceTacticalRole.Ram, 0.10f));
        var sport = MakeProfile(new[] { PoliceTacticalRole.Pursue, PoliceTacticalRole.Intercept, PoliceTacticalRole.Ram },
            Weight(PoliceTacticalRole.Pursue, 0.25f), Weight(PoliceTacticalRole.Intercept, 0.65f), Weight(PoliceTacticalRole.Ram, 0.10f));
        var heavy = MakeProfile(new[] { PoliceTacticalRole.Pursue, PoliceTacticalRole.Intercept, PoliceTacticalRole.Ram },
            Weight(PoliceTacticalRole.Pursue, 0.30f), Weight(PoliceTacticalRole.Intercept, 0.10f), Weight(PoliceTacticalRole.Ram, 0.60f));
        var coordinator = NewCoordinator();
        coordinator.BeginLife(1, Target(2, 1), PoliceDriverTraits.Reset());
        Assert.AreEqual(PoliceTacticalRole.Pursue, coordinator.AssignRole(1, standard, 0f).role);
        coordinator.EndLife(1);
        coordinator.BeginLife(2, Target(2, 1), PoliceDriverTraits.Reset());
        Assert.AreEqual(PoliceTacticalRole.Intercept, coordinator.AssignRole(2, sport, 0f).role);
        coordinator.EndLife(2);
        coordinator.BeginLife(3, Target(2, 1), PoliceDriverTraits.Reset());
        Assert.AreEqual(PoliceTacticalRole.Ram, coordinator.AssignRole(3, heavy, 0f).role);
        Object.DestroyImmediate(standard);
        Object.DestroyImmediate(sport);
        Object.DestroyImmediate(heavy);
    }

    [Test]
    public void RoleAssignmentHonorsCadenceAndMinimumHold() {
        var profile = MakeProfile(new[] { PoliceTacticalRole.Pursue, PoliceTacticalRole.Ram },
            Weight(PoliceTacticalRole.Pursue, 1f), Weight(PoliceTacticalRole.Ram, 10f));
        var coordinator = NewCoordinator();
        coordinator.BeginLife(3, Target(2, 1), PoliceDriverTraits.Reset());
        var first = coordinator.AssignRole(3, profile, 0f);
        var held = coordinator.AssignRole(3, profile, 1.9f);
        var changedAfterHold = coordinator.AssignRole(3, profile, 2.1f);
        Assert.IsTrue(first.changed);
        Assert.IsFalse(held.changed);
        Assert.IsTrue(changedAfterHold.accepted);
        Object.DestroyImmediate(profile);
    }

    [Test]
    public void InterceptPredictionUsesMeasuredVelocityAndClampsLead() {
        Assert.IsTrue(PoliceFreePursuitDriver.TryPredictIntercept(
            new PoliceFreePursuitDriver.InterceptTarget(new Vector2(1f, 2f), new Vector2(10f, 0f)),
            0.75f, 6f, out Vector2 predicted));
        Assert.That(predicted.x, Is.EqualTo(7f).Within(0.0001f));
        Assert.That(predicted.y, Is.EqualTo(2f).Within(0.0001f));
    }

    [Test]
    public void SameSeedMatchesDifferentSeedDiffersWithinBounds() {
        var bounds = new PoliceUnitVariationSettings();
        var a = PoliceDriverTraits.CreateForLife(12, 4, "builtin:police-standard", 9, bounds);
        var b = PoliceDriverTraits.CreateForLife(12, 4, "builtin:police-standard", 9, bounds);
        var c = PoliceDriverTraits.CreateForLife(13, 4, "builtin:police-standard", 9, bounds);
        Assert.That(a.reactionMultiplier, Is.EqualTo(b.reactionMultiplier));
        Assert.That(a.predictionMultiplier, Is.EqualTo(b.predictionMultiplier));
        Assert.That(a.followGapMultiplier, Is.EqualTo(b.followGapMultiplier));
        Assert.That(a.reactionMultiplier, Is.Not.EqualTo(c.reactionMultiplier));
        Assert.That(Mathf.Abs(a.reactionMultiplier - 1f), Is.LessThanOrEqualTo(bounds.reactionVariation));
        Assert.That(Mathf.Abs(a.predictionMultiplier - 1f), Is.LessThanOrEqualTo(bounds.predictionVariation));
        Assert.That(Mathf.Abs(a.followGapMultiplier - 1f), Is.LessThanOrEqualTo(bounds.followGapVariation));
        Assert.That(Mathf.Abs(a.sidePreference), Is.LessThanOrEqualTo(bounds.sidePreferenceVariation));
        Assert.That(Mathf.Abs(a.riskBias), Is.LessThanOrEqualTo(bounds.riskBiasVariation));
    }

    [Test]
    public void NewLifeResetsTraitsAndClaimsButRelocationCanRetainTraits() {
        var traits = PoliceDriverTraits.CreateForLife(2, 1, "builtin:police-sport", 5, new PoliceUnitVariationSettings());
        var coordinator = NewCoordinator();
        Assert.IsTrue(coordinator.BeginLife(5, Target(8, 1), traits));
        Assert.IsTrue(coordinator.TryClaimApproach(5, Target(8, 1), PoliceTacticsCoordinator.ApproachSlot.Left, 0f, 1f, out var claim));
        Assert.IsTrue(coordinator.IsClaimCurrent(claim, 0.5f));
        Assert.IsTrue(traits.IsCurrent(5));
        Assert.IsTrue(coordinator.EndLife(5));
        Assert.IsFalse(coordinator.IsClaimCurrent(claim, 0.5f));
        Assert.IsFalse(traits.IsCurrent(6));
        Assert.AreEqual(0, PoliceDriverTraits.Reset().lifeId);
    }

    [Test]
    public void StaleTargetRevisionAndClaimAreRejected() {
        var coordinator = NewCoordinator();
        coordinator.BeginLife(5, Target(8, 2), PoliceDriverTraits.Reset());
        Assert.IsFalse(coordinator.TryAcceptTargetFacts(5, Target(8, 1)));
        Assert.IsFalse(coordinator.TryClaimApproach(5, Target(8, 1), PoliceTacticsCoordinator.ApproachSlot.Rear, 0f, 1f, out _));
        Assert.IsTrue(coordinator.TryAcceptTargetFacts(5, Target(8, 3)));
        Assert.IsFalse(coordinator.TryClaimApproach(5, Target(8, 2), PoliceTacticsCoordinator.ApproachSlot.Rear, 0f, 1f, out _));
    }

    static PoliceTacticsCoordinator NewCoordinator() => new PoliceTacticsCoordinator(new PoliceFreeChaseSettings {
        roleCadenceSeconds = 0.5f,
        minimumRoleHoldSeconds = 2f
    });

    static PoliceTacticsCoordinator.TargetFacts Target(int life, int revision) => new PoliceTacticsCoordinator.TargetFacts(life, revision);

    static PoliceVehicleProfile.RoleWeight Weight(PoliceTacticalRole role, float value) => new PoliceVehicleProfile.RoleWeight { role = role, weight = value };

    static PoliceVehicleProfile MakeProfile(IList<PoliceTacticalRole> roles, params PoliceVehicleProfile.RoleWeight[] weights) {
        var profile = ScriptableObject.CreateInstance<PoliceVehicleProfile>();
        profile.supportedTacticalRoles = new List<PoliceTacticalRole>(roles);
        profile.roleWeights = new List<PoliceVehicleProfile.RoleWeight>(weights);
        return profile;
    }
}
