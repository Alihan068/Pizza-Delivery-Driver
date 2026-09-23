using NUnit.Framework;
using UnityEngine;

/// <summary>Pure acceptance coverage for shared traffic caps and two-role session cleanup.</summary>
public sealed class TrafficSharedServicesTests {
    PopulationBudgetData budget;

    [SetUp]
    public void SetUp() {
        budget = ScriptableObject.CreateInstance<PopulationBudgetData>();
        budget.maxCivilianMoving = 4;
        budget.maxPoliceMoving = 4;
        budget.maxTotalMoving = 4;
        budget.maxWreckSlots = 4;
        budget.maxTotalPhysicsObjects = 2;
    }

    [TearDown]
    public void TearDown() {
        if (budget != null) Object.DestroyImmediate(budget);
    }

    /// <summary>Uses the minimum wreck/physics cap for both pool capacity and shared reservations.</summary>
    [Test]
    public void SharedServices_GlobalPhysicsCapLimitsPoolAndBothRoles() {
        var pool = new NpcVehiclePool(budget, null, new VehicleIdentityRegistry());
        var population = new VehiclePopulationService(budget);

        Assert.AreEqual(2, pool.Capacity);
        Assert.IsTrue(population.TryReserveSpawn(VehicleRole.Civilian));
        Assert.IsTrue(population.TryReserveSpawn(VehicleRole.Police));
        Assert.IsFalse(population.TryReserveSpawn(VehicleRole.Civilian));
        Assert.AreEqual(2, population.ReservedFutureWreckCount);
    }

    /// <summary>Rejects every role when the authored global moving or physical budget has no availability.</summary>
    [Test]
    public void SharedServices_NoTrafficAvailableRejectsReservations() {
        budget.maxTotalMoving = 0;
        var population = new VehiclePopulationService(budget);

        Assert.IsFalse(population.TryReserveSpawn(VehicleRole.Civilian));
        Assert.IsFalse(population.TryReserveSpawn(VehicleRole.Police));

        budget.maxTotalMoving = 4;
        budget.maxTotalPhysicsObjects = 0;
        Assert.IsFalse(population.TryReserveSpawn(VehicleRole.Civilian));
    }

    /// <summary>Confirms one role cleanup cannot reset the other role's counters or close the bundle prematurely.</summary>
    [Test]
    public void SharedServices_RoleCleanupWaitsForBothOwnersAndDoesNotResetTheOtherRole() {
        var population = new VehiclePopulationService(budget);
        Assert.IsTrue(population.TryReserveSpawn(VehicleRole.Civilian));
        Assert.IsTrue(population.TryReserveSpawn(VehicleRole.Police));
        var services = new TrafficSessionServices(null, null, null, population, budget, new VehicleIdentityRegistry());

        Assert.IsTrue(services.MarkRoleOwnerCleaned(VehicleRole.Civilian));
        services.Close();
        Assert.IsFalse(services.IsClosed);
        Assert.AreEqual(2, population.ReservedFutureWreckCount);

        Assert.IsTrue(services.MarkRoleOwnerCleaned(VehicleRole.Police));
        services.Close();
        Assert.IsTrue(services.IsClosed);
        Assert.AreEqual(0, population.ReservedFutureWreckCount);
        Assert.AreEqual(0, population.PendingCivilianCount);
        Assert.AreEqual(0, population.PendingPoliceCount);
    }
}
