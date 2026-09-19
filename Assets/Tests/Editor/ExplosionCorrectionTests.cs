using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pure S05 regression coverage for bounded complete queries, immutable/idempotent blast requests,
/// damage attribution and receiver lifecycle gates. Creates no scene objects or physics worlds.
/// </summary>
public sealed class ExplosionCorrectionTests {
    sealed class VictimQuery : IBlastVictimQuery {
        internal readonly List<BlastVictim> victims = new List<BlastVictim>();
        internal readonly List<int> capacities = new List<int>();
        internal Func<Vector2, float, BlastVictim[], int> findOverride;
        internal Vector2 lastOrigin;
        internal float lastRadius;
        internal BlastVictim[] lastBuffer;

        /// <summary>Reports exact saturation, including duplicate collider candidates, without truncation concealment.</summary>
        public int FindVictimsInRadius(Vector2 origin, float radius, BlastVictim[] buffer) {
            capacities.Add(buffer.Length);
            lastOrigin = origin;
            lastRadius = radius;
            lastBuffer = buffer;
            if (findOverride != null) return findOverride(origin, radius, buffer);
            int count = Math.Min(victims.Count, buffer.Length);
            for (int i = 0; i < count; i++) buffer[i] = victims[i];
            return count;
        }
    }

    static BlastVictim Victim(int lifeId) => new BlastVictim(lifeId, VehicleRole.Civilian, Vector2.zero, 0f);

    static DamageContext Collision() => new DamageContext("collision", 20, InstigatorKind.Player, 30, DamageKind.Collision, "root");

    static PendingBlast Blast(string id) => new PendingBlast(id, Vector2.zero, 20f, 10f, Collision());

    static VehicleExplosionService Service(VictimQuery query, int initial = 2, int maximum = 16,
        int queryBudget = 8, int blastBudget = 4, int idBudget = 64) {
        return new VehicleExplosionService(query, new BlastRoleMultipliers(), blastBudget,
            initial, maximum, queryBudget, idBudget);
    }

    static void AssertVictims(List<BlastApplication> applications, params int[] expected) {
        var actual = new int[applications.Count];
        for (int i = 0; i < applications.Count; i++) actual[i] = applications[i].victimLifeId;
        CollectionAssert.AreEquivalent(expected, actual);
    }

    /// <summary>Three receivers queried through two slots all receive one hit after a complete retry.</summary>
    [Test]
    public void SaturatedBufferGrowsBeforeEmittingAnyDamage() {
        var query = new VictimQuery();
        query.victims.AddRange(new[] { Victim(1), Victim(2), Victim(3) });
        var service = Service(query);
        service.EnqueueBlast(Blast("blast"));
        var output = new List<BlastApplication>(3);

        Assert.AreEqual(1, service.ProcessTick(new BlastVictim[2], output));

        AssertVictims(output, 1, 2, 3);
        CollectionAssert.AreEqual(new[] { 2, 4 }, query.capacities);
        Assert.AreEqual(0, service.PendingCount);
        Assert.AreEqual(VehicleExplosionService.ProcessingStatus.Completed, service.LastProcessStatus);
    }

    /// <summary>An exact fit is still ambiguous and must be retried with spare capacity.</summary>
    [Test]
    public void ExactFitRequiresACompleteRetry() {
        var query = new VictimQuery();
        query.victims.AddRange(new[] { Victim(1), Victim(2) });
        var service = Service(query);
        service.EnqueueBlast(Blast("blast"));

        AssertVictims(service.ProcessTick(new BlastVictim[2]), 1, 2);
        CollectionAssert.AreEqual(new[] { 2, 4 }, query.capacities);
    }

    /// <summary>Several colliders on one vehicle cannot hide another receiver behind a full buffer.</summary>
    [Test]
    public void DuplicateCollidersAcrossGrowthStillProduceOneHitPerLife() {
        var query = new VictimQuery();
        query.victims.AddRange(new[] { Victim(1), Victim(1), Victim(1), Victim(2) });
        var service = Service(query);
        service.EnqueueBlast(Blast("blast"));

        AssertVictims(service.ProcessTick(new BlastVictim[2]), 1, 2);
        CollectionAssert.AreEqual(new[] { 2, 4, 8 }, query.capacities);
    }

    /// <summary>A retry budget defers the whole blast; no prefix is applied twice or lost between ticks.</summary>
    [Test]
    public void QueryBudgetRetainsWholeBlastUntilAllCandidatesFit() {
        var query = new VictimQuery();
        for (int i = 1; i <= 9; i++) query.victims.Add(Victim(i));
        var service = Service(query, queryBudget: 1);
        var output = new List<BlastApplication>(9);
        service.EnqueueBlast(Blast("blast"));

        for (int tick = 0; tick < 3; tick++) {
            Assert.AreEqual(0, service.ProcessTick(output));
            Assert.IsEmpty(output);
            Assert.AreEqual(1, service.PendingCount);
            Assert.AreEqual(VehicleExplosionService.ProcessingStatus.TickBudgetReached, service.LastProcessStatus);
            Assert.AreEqual(tick + 1, query.capacities.Count);
            Assert.IsFalse(service.TryEnqueueBlast(Blast("blast")));
        }

        Assert.AreEqual(1, service.ProcessTick(output));
        AssertVictims(output, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        CollectionAssert.AreEqual(new[] { 2, 4, 8, 16 }, query.capacities);
        Assert.AreEqual(0, service.PendingCount);
    }

    /// <summary>A maximum-capacity query is an explicit fault, retaining the complete event on every retry.</summary>
    [Test]
    public void HardCapacityReportsFaultWithoutPartialDamageOrEventConsumption() {
        var query = new VictimQuery();
        query.victims.AddRange(new[] { Victim(1), Victim(2), Victim(3), Victim(4) });
        var service = Service(query, maximum: 4);
        var output = new List<BlastApplication>();
        service.EnqueueBlast(Blast("blast"));

        for (int tick = 0; tick < 2; tick++) {
            Assert.AreEqual(0, service.ProcessTick(output));
            Assert.IsEmpty(output);
            Assert.AreEqual(1, service.PendingCount);
            Assert.AreEqual(1, service.TrackedBlastCount);
            Assert.AreEqual(4, service.VictimBufferCapacity);
            Assert.AreEqual(VehicleExplosionService.ProcessingStatus.VictimCapacityReached, service.LastProcessStatus);
        }
        // The world later contains fewer eligible receivers; the retained event can now complete.
        query.victims.RemoveAt(3);
        Assert.AreEqual(1, service.ProcessTick(output));
        AssertVictims(output, 1, 2, 3);
        Assert.AreEqual(0, service.PendingCount);
    }

    /// <summary>A later blocked blast cannot erase the output from an earlier completed blast in the same tick.</summary>
    [Test]
    public void CapacityFaultPreservesPreviouslyCompletedOutput() {
        var query = new VictimQuery();
        query.findOverride = (origin, radius, buffer) => {
            int count = origin.x == 0f ? 1 : buffer.Length;
            for (int i = 0; i < count; i++) buffer[i] = Victim(i + 1);
            return count;
        };
        var service = Service(query, maximum: 4);
        service.EnqueueBlast(Blast("complete"));
        var blocked = Blast("blocked");
        blocked.origin = Vector2.right;
        service.EnqueueBlast(blocked);
        var output = new List<BlastApplication>();

        Assert.AreEqual(1, service.ProcessTick(output));

        Assert.AreEqual(1, output.Count);
        Assert.AreEqual("complete", output[0].blastId);
        Assert.AreEqual(1, service.PendingCount);
        Assert.AreEqual(VehicleExplosionService.ProcessingStatus.VictimCapacityReached, service.LastProcessStatus);
        Assert.IsFalse(service.TryEnqueueBlast(Blast("complete")));
    }

    /// <summary>Invalid query counts never consume an event; a repaired adapter can complete the retained request.</summary>
    [TestCase(-1)]
    [TestCase(3)]
    public void InvalidQueryCountReportsFaultAndRetainsEvent(int invalidCount) {
        var query = new VictimQuery { findOverride = (origin, radius, buffer) => invalidCount };
        var service = Service(query);
        service.EnqueueBlast(Blast("blast"));
        var output = new List<BlastApplication>();

        Assert.AreEqual(0, service.ProcessTick(output));
        Assert.AreEqual(VehicleExplosionService.ProcessingStatus.InvalidQueryResult, service.LastProcessStatus);
        Assert.AreEqual(1, service.PendingCount);
        Assert.IsEmpty(output);

        query.findOverride = null;
        query.victims.Add(Victim(1));
        Assert.AreEqual(1, service.ProcessTick(output));
        AssertVictims(output, 1);
    }

    /// <summary>Invalid scratch storage and missing dependencies fail explicitly before any blast is consumed.</summary>
    [Test]
    public void InvalidStorageCannotSilentlyConsumePendingBlast() {
        var query = new VictimQuery();
        query.victims.Add(Victim(1));
        var service = Service(query);
        service.EnqueueBlast(Blast("blast"));
        var output = new List<BlastApplication>();

        Assert.Throws<ArgumentNullException>(() => service.ProcessTick(null, output));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.ProcessTick(Array.Empty<BlastVictim>(), output));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.ProcessTick(new BlastVictim[17], output));
        Assert.Throws<ArgumentNullException>(() => service.ProcessTick(new BlastVictim[2], null));
        Assert.Throws<ArgumentNullException>(() => new VehicleExplosionService(null, null, 1));
        Assert.AreEqual(1, service.PendingCount);
        Assert.AreEqual(1, service.ProcessTick(output));
        AssertVictims(output, 1);
    }

    /// <summary>Only accepted finite payloads consume an ID, so a corrected invalid request remains retryable.</summary>
    [Test]
    public void InvalidPayloadDoesNotReserveBlastIdentity() {
        var service = Service(new VictimQuery());
        var request = Blast("blast");
        request.origin = new Vector2(float.NaN, 0f);
        Assert.Throws<ArgumentException>(() => service.EnqueueBlast(request));
        request.origin = Vector2.zero;
        request.baseBlast = float.PositiveInfinity;
        Assert.Throws<ArgumentException>(() => service.EnqueueBlast(request));
        request.baseBlast = 20f;
        request.blastRadius = 0f;
        Assert.Throws<ArgumentException>(() => service.EnqueueBlast(request));
        request.blastRadius = 10f;
        request.blastId = " ";
        Assert.Throws<ArgumentException>(() => service.EnqueueBlast(request));
        Assert.AreEqual(0, service.PendingCount);
        Assert.AreEqual(0, service.TrackedBlastCount);
        request.blastId = "blast";
        Assert.IsTrue(service.TryEnqueueBlast(request));
    }

    /// <summary>Pending and completed IDs are idempotent, while genuinely separate blasts still damage the same life.</summary>
    [Test]
    public void DuplicateIdCannotApplyInSameOrLaterTick() {
        var query = new VictimQuery();
        query.victims.Add(Victim(7));
        var service = Service(query);
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(100f, 7);
        Assert.IsTrue(service.TryEnqueueBlast(Blast("first")));
        Assert.IsFalse(service.TryEnqueueBlast(Blast("first")));
        var output = service.ProcessTick(new BlastVictim[2]);
        Assert.AreEqual(1, output.Count);
        Assert.IsTrue(health.TryApplyDamage(7, output[0].damage, output[0].context, VehicleRole.Civilian, 0f, out _));
        Assert.AreEqual(80f, health.CurrentHealth);

        Assert.IsFalse(service.TryEnqueueBlast(Blast("first")));
        Assert.AreEqual(0, service.ProcessTick(output));
        Assert.IsEmpty(output);
        Assert.IsTrue(service.TryEnqueueBlast(Blast("second")));
        service.ProcessTick(output);
        Assert.AreEqual(1, output.Count);
        health.TryApplyDamage(7, output[0].damage, output[0].context, VehicleRole.Civilian, 1f, out _);
        Assert.AreEqual(60f, health.CurrentHealth);
    }

    /// <summary>Original values survive mutation of every public request field and the supplied role tuning.</summary>
    [Test]
    public void AcceptedPayloadAndRoleTuningAreDetachedSnapshots() {
        var query = new VictimQuery();
        var origin = new Vector2(3f, 4f);
        query.victims.Add(new BlastVictim(7, VehicleRole.Civilian, origin, 0f));
        var roles = new BlastRoleMultipliers { civilianMultiplier = 2f };
        var service = new VehicleExplosionService(query, roles, 1, 2, 16, 8, 8);
        var request = new PendingBlast("original", origin, 20f, 10f, Collision());
        service.EnqueueBlast(request);
        request.blastId = "mutated";
        request.origin = Vector2.one;
        request.baseBlast = 0f;
        request.blastRadius = 1f;
        request.context = default;
        roles.civilianMultiplier = 0f;

        var output = service.ProcessTick(new BlastVictim[2]);

        Assert.AreEqual(1, output.Count);
        Assert.AreEqual("original", output[0].blastId);
        Assert.AreEqual(origin, query.lastOrigin);
        Assert.AreEqual(10f, query.lastRadius);
        Assert.AreEqual(40f, output[0].damage);
        Assert.AreEqual("original", output[0].context.eventId);
        Assert.AreEqual(20, output[0].context.sourceLifeId);
        Assert.AreEqual("root", output[0].context.rootIncidentId);
        Assert.AreEqual(DamageKind.Explosion, output[0].context.damageKind);
        Assert.IsFalse(service.TryEnqueueBlast(Blast("original")));
    }

    /// <summary>Bounded session history never evicts completed IDs to admit duplicates or silently discard new events.</summary>
    [Test]
    public void SessionIdBudgetIsExplicitAndHistorySurvivesCompletion() {
        var service = Service(new VictimQuery(), idBudget: 2);
        service.EnqueueBlast(Blast("one"));
        service.EnqueueBlast(Blast("two"));
        Assert.Throws<InvalidOperationException>(() => service.EnqueueBlast(Blast("three")));
        Assert.AreEqual(2, service.ProcessTick(new List<BlastApplication>()));
        Assert.AreEqual(2, service.TrackedBlastCount);
        Assert.IsFalse(service.TryEnqueueBlast(Blast("one")));
        Assert.Throws<InvalidOperationException>(() => service.TryEnqueueBlast(Blast("three")));
        service.SetSessionActive(false);
        Assert.AreEqual(0, service.TrackedBlastCount);
        Assert.IsFalse(service.TryEnqueueBlast(Blast("three")));
    }

    /// <summary>Output contents are replaced each tick and grown service-owned scratch is reused by identity.</summary>
    [Test]
    public void OutputOverloadReusesListAndGrownQueryStorage() {
        var query = new VictimQuery();
        query.victims.AddRange(new[] { Victim(1), Victim(2), Victim(3) });
        var service = Service(query, blastBudget: 1);
        var output = new List<BlastApplication>(3);
        service.EnqueueBlast(Blast("one"));
        service.EnqueueBlast(Blast("two"));
        Assert.AreEqual(1, service.ProcessTick(output));
        Assert.AreEqual(VehicleExplosionService.ProcessingStatus.TickBudgetReached, service.LastProcessStatus);
        var warmedBuffer = query.lastBuffer;
        Assert.AreEqual(1, service.ProcessTick(output));
        Assert.AreSame(warmedBuffer, query.lastBuffer);
        Assert.AreEqual(3, output.Count);
        Assert.AreEqual("two", output[0].blastId);
        Assert.AreEqual(0, service.ProcessTick(output));
        Assert.IsEmpty(output);
        Assert.AreEqual(3, output.Capacity);
    }

    /// <summary>Zero falloff, complete resistance and malformed receivers never emit ineffective or non-finite hits.</summary>
    [Test]
    public void IneffectiveCandidatesAreNotEmittedAndFiniteOverflowStaysFinite() {
        var query = new VictimQuery();
        query.victims.Add(new BlastVictim(1, VehicleRole.Player, new Vector2(10f, 0f), 0f));
        query.victims.Add(new BlastVictim(2, VehicleRole.Civilian, Vector2.zero, 1f));
        query.victims.Add(new BlastVictim(3, VehicleRole.Civilian, Vector2.zero, float.NaN));
        query.victims.Add(new BlastVictim(4, VehicleRole.Civilian, new Vector2(float.NaN, 0f), 0f));
        query.victims.Add(Victim(5));
        var roles = new BlastRoleMultipliers { civilianMultiplier = float.MaxValue };
        var service = new VehicleExplosionService(query, roles, 1, 8, 8, 1, 2);
        service.EnqueueBlast(new PendingBlast("large", Vector2.zero, float.MaxValue, 10f, Collision()));

        var output = service.ProcessTick(new BlastVictim[8]);

        AssertVictims(output, 5);
        Assert.AreEqual(float.MaxValue, output[0].damage);
    }

    /// <summary>Ending a service cannot be reversed by true; pending work and reused output are cleared.</summary>
    [Test]
    public void EndedExplosionServiceCannotReopen() {
        var query = new VictimQuery();
        query.victims.Add(Victim(1));
        var service = Service(query);
        service.EnqueueBlast(Blast("one"));
        var output = service.ProcessTick(new BlastVictim[2]);
        service.EnqueueBlast(Blast("two"));
        service.SetSessionActive(false);
        service.SetSessionActive(true);

        Assert.IsFalse(service.IsSessionActive);
        Assert.AreEqual(0, service.PendingCount);
        Assert.IsFalse(service.TryEnqueueBlast(Blast("three")));
        Assert.AreEqual(0, service.ProcessTick(output));
        Assert.IsEmpty(output);
        Assert.AreEqual(VehicleExplosionService.ProcessingStatus.SessionEnded, service.LastProcessStatus);
    }

    /// <summary>Collision-origin explosions change physical kind while each hop preserves root instigator attribution.</summary>
    [Test]
    public void CollisionExplosionExplosionCarriesFreshSourceAndEventButStableRoot() {
        var collision = Collision();
        var first = DamageContextFactory.CreateChainedContext(collision, "blast-one", 7);
        var second = DamageContextFactory.CreateChainedContext(first, "blast-two", 8);

        Assert.AreEqual(DamageKind.Collision, collision.damageKind);
        Assert.AreEqual(DamageKind.Explosion, first.damageKind);
        Assert.AreEqual(DamageKind.Explosion, second.damageKind);
        Assert.AreEqual("blast-one", first.eventId);
        Assert.AreEqual("blast-two", second.eventId);
        Assert.AreEqual(7, first.sourceLifeId);
        Assert.AreEqual(8, second.sourceLifeId);
        Assert.AreEqual(collision.instigatorKind, first.instigatorKind);
        Assert.AreEqual(collision.instigatorKind, second.instigatorKind);
        Assert.AreEqual(collision.instigatorLifeId, first.instigatorLifeId);
        Assert.AreEqual(collision.instigatorLifeId, second.instigatorLifeId);
        Assert.AreEqual(collision.rootIncidentId, first.rootIncidentId);
        Assert.AreEqual(collision.rootIncidentId, second.rootIncidentId);
        Assert.AreEqual(DamageKind.Collision,
            DamageContextFactory.CreateChainedContext(second, "explicit", 9, DamageKind.Collision).damageKind);
    }

    /// <summary>Uninitialized receivers never turn default zero HP into a fabricated destruction event.</summary>
    [Test]
    public void UninitializedHealthRejectsDamageEvenForDefaultLife() {
        var health = new NpcVehicleHealth();
        Assert.IsFalse(health.CanReceiveDamage(0));
        Assert.IsFalse(health.ApplyDamage(0, 100f, Collision(), VehicleRole.Civilian, 0f, out var destroyed));
        Assert.AreEqual(default(VehicleDestroyedEvent), destroyed);
        Assert.IsFalse(health.IsDestroyed);
        Assert.IsFalse(health.IsInitialized);
    }

    /// <summary>Invalid health cannot initialize a life or corrupt/heal an existing receiver.</summary>
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void InvalidInitialHealthPreservesAllLifeState(float invalidHealth) {
        var health = new NpcVehicleHealth();
        Assert.IsFalse(health.TryInitializeForNewLife(invalidHealth, 7));
        Assert.IsFalse(health.IsInitialized);
        Assert.AreEqual(0, health.LifeId);
        health.InitializeForNewLife(100f, 7);
        health.TryApplyDamage(7, 10f, Collision(), VehicleRole.Civilian, 0f, out _);
        health.InitializeForNewLife(invalidHealth, 8);
        Assert.AreEqual(90f, health.CurrentHealth);
        Assert.AreEqual(100f, health.MaxHealth);
        Assert.AreEqual(7, health.LifeId);
    }

    /// <summary>Invalid, repeated and older IDs cannot reset damage or reopen a released life.</summary>
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(6)]
    [TestCase(7)]
    public void NonIncreasingLifeCannotResetHealth(int staleLifeId) {
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(100f, 7);
        health.TryApplyDamage(7, 10f, Collision(), VehicleRole.Civilian, 0f, out _);
        Assert.IsFalse(health.TryInitializeForNewLife(100f, staleLifeId));
        Assert.AreEqual(90f, health.CurrentHealth);
        health.InvalidateLife();
        Assert.IsFalse(health.TryInitializeForNewLife(100f, staleLifeId));
        Assert.IsFalse(health.IsInitialized);
        Assert.IsFalse(health.CanReceiveDamage(7));
        Assert.IsTrue(health.TryInitializeForNewLife(100f, 8));
        Assert.IsTrue(health.CanReceiveDamage(8));
    }

    /// <summary>Non-positive and non-finite amounts produce no HP/event side effects and do not block the next valid hit.</summary>
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void InvalidDamageCannotCorruptHealth(float invalidDamage) {
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(100f, 7);
        Assert.IsFalse(health.TryApplyDamage(7, invalidDamage, Collision(), VehicleRole.Civilian, 0f, out var destroyed));
        Assert.AreEqual(default(VehicleDestroyedEvent), destroyed);
        Assert.AreEqual(100f, health.CurrentHealth);
        Assert.IsFalse(health.IsDestroyed);
        Assert.IsTrue(health.TryApplyDamage(7, 10f, Collision(), VehicleRole.Civilian, 0f, out _));
        Assert.AreEqual(90f, health.CurrentHealth);
    }

    /// <summary>Invalid session timestamps reject the whole hit before changing health or emitting destruction.</summary>
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void InvalidSessionTimeCannotCreateDamage(float invalidTime) {
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(100f, 7);
        Assert.IsFalse(health.ApplyDamage(7, 1000f, Collision(), VehicleRole.Civilian, invalidTime, out var destroyed));
        Assert.AreEqual(default(VehicleDestroyedEvent), destroyed);
        Assert.AreEqual(100f, health.CurrentHealth);
        Assert.IsFalse(health.IsDestroyed);
    }

    /// <summary>Acceptance and lethal-return APIs remain distinct, and source/instigator IDs never replace the target ID.</summary>
    [Test]
    public void ReceiverApiPreservesLethalCompatibilityAndDistinctIdentities() {
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(100f, 7);
        Assert.IsTrue(health.TryApplyDamage(7, 10f, Collision(), VehicleRole.Civilian, 0f, out var nonlethal));
        Assert.AreEqual(default(VehicleDestroyedEvent), nonlethal);
        Assert.IsFalse(health.ApplyDamage(7, 10f, Collision(), VehicleRole.Civilian, 0f, out _));
        Assert.AreEqual(80f, health.CurrentHealth);
        Assert.IsFalse(health.TryApplyDamage(20, 1000f, Collision(), VehicleRole.Civilian, 0f, out _));
        Assert.IsTrue(health.ApplyDamage(7, float.MaxValue, Collision(), VehicleRole.Civilian, 2f, out var destroyed));
        Assert.AreEqual(7, destroyed.victimLifeId);
        Assert.AreEqual(20, destroyed.killingContext.sourceLifeId);
        Assert.AreEqual(30, destroyed.killingContext.instigatorLifeId);
        Assert.AreEqual(2f, destroyed.sessionTime);
        Assert.AreEqual(0f, health.CurrentHealth);
        Assert.IsFalse(health.CanReceiveDamage(7));
        Assert.IsFalse(health.ApplyDamage(7, 100f, Collision(), VehicleRole.Civilian, 2f, out _));
    }

    /// <summary>Pool release rejects delayed output, and a new life accepts only a genuinely new blast event.</summary>
    [Test]
    public void ReleasedAndReusedReceiverRejectsOldApplicationAndOldBlast() {
        var query = new VictimQuery();
        query.victims.Add(Victim(7));
        var service = Service(query);
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(100f, 7);
        service.EnqueueBlast(Blast("old"));
        var output = service.ProcessTick(new BlastVictim[2]);
        var stale = output[0];
        health.InvalidateLife();
        Assert.IsFalse(health.TryApplyDamage(stale.victimLifeId, stale.damage, stale.context, stale.victimRole, 0f, out _));
        Assert.IsTrue(health.TryInitializeForNewLife(100f, 8));
        Assert.IsFalse(health.TryApplyDamage(stale.victimLifeId, stale.damage, stale.context, stale.victimRole, 0f, out _));
        Assert.AreEqual(100f, health.CurrentHealth);
        query.victims.Clear();
        query.victims.Add(Victim(8));
        Assert.IsFalse(service.TryEnqueueBlast(Blast("old")));
        service.EnqueueBlast(Blast("new"));
        service.ProcessTick(output);
        AssertVictims(output, 8);
        Assert.IsTrue(health.TryApplyDamage(8, output[0].damage, output[0].context, output[0].victimRole, 0f, out _));
        Assert.AreEqual(80f, health.CurrentHealth);
    }

    /// <summary>Already-returned output cannot damage a receiver after session end, including attempted reinitialization.</summary>
    [Test]
    public void SessionEndMakesReturnedApplicationsPermanentlyInert() {
        var query = new VictimQuery();
        query.victims.Add(Victim(7));
        var service = Service(query);
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(100f, 7);
        service.EnqueueBlast(Blast("blast"));
        var output = service.ProcessTick(new BlastVictim[2]);
        service.SetSessionActive(false);
        health.SetSessionActive(false);
        health.SetSessionActive(true);
        Assert.IsFalse(health.TryInitializeForNewLife(100f, 8));
        health.InitializeForNewLife(100f, 8);
        var stale = output[0];
        Assert.IsFalse(health.TryApplyDamage(stale.victimLifeId, stale.damage, stale.context, stale.victimRole, 0f, out _));
        Assert.IsFalse(health.IsSessionActive);
        Assert.AreEqual(100f, health.CurrentHealth);
        Assert.AreEqual(7, health.LifeId);
    }

    /// <summary>Ending the session on a first lethal dispatch prevents subsequent applications from changing another receiver.</summary>
    [Test]
    public void FirstLethalApplicationCanCloseRemainingReceiverDispatch() {
        var query = new VictimQuery();
        query.victims.AddRange(new[] { Victim(7), Victim(8) });
        var service = Service(query);
        var first = new NpcVehicleHealth();
        var second = new NpcVehicleHealth();
        first.InitializeForNewLife(10f, 7);
        second.InitializeForNewLife(100f, 8);
        service.EnqueueBlast(Blast("blast"));
        var output = service.ProcessTick(new BlastVictim[4]);
        Assert.IsTrue(first.ApplyDamage(7, output[0].damage, output[0].context, VehicleRole.Civilian, 0f, out _));
        service.SetSessionActive(false);
        first.SetSessionActive(false);
        second.SetSessionActive(false);
        Assert.IsFalse(second.TryApplyDamage(8, output[1].damage, output[1].context, VehicleRole.Civilian, 0f, out _));
        Assert.AreEqual(100f, second.CurrentHealth);
    }

    /// <summary>Each next blast is created by an actual newly killed health receiver, preserving attribution without recursion.</summary>
    [Test]
    public void HealthDeathsDriveAnIterativeBlastChain() {
        const int length = 32;
        var health = new NpcVehicleHealth[length];
        for (int i = 0; i < length; i++) {
            health[i] = new NpcVehicleHealth();
            health[i].InitializeForNewLife(5f, i + 1);
        }
        var query = new VictimQuery();
        query.findOverride = (origin, radius, buffer) => {
            int count = 0;
            for (int i = 0; i < length; i++) {
                var position = new Vector2(i * 10f, 0f);
                if (!health[i].CanReceiveDamage(i + 1) || Vector2.Distance(origin, position) > radius) continue;
                if (count == buffer.Length) return count;
                buffer[count++] = new BlastVictim(i + 1, VehicleRole.Civilian, position, 0f);
            }
            return count;
        };
        var service = Service(query, blastBudget: 1);
        Assert.IsTrue(health[0].ApplyDamage(1, 5f, Collision(), VehicleRole.Civilian, 0f, out var firstDeath));
        service.EnqueueBlast(new PendingBlast("blast-1", Vector2.zero, 100f, 11f,
            DamageContextFactory.CreateChainedContext(firstDeath.killingContext, "blast-1", 1)));
        var output = new List<BlastApplication>(2);
        int deaths = 1;
        for (int tick = 0; tick <= length && service.PendingCount > 0; tick++) {
            service.ProcessTick(output);
            foreach (var application in output) {
                int index = application.victimLifeId - 1;
                Assert.IsTrue(health[index].ApplyDamage(application.victimLifeId, application.damage,
                    application.context, application.victimRole, tick, out var destroyed));
                Assert.AreEqual(DamageKind.Explosion, destroyed.killingContext.damageKind);
                Assert.AreEqual("root", destroyed.killingContext.rootIncidentId);
                Assert.AreEqual(30, destroyed.killingContext.instigatorLifeId);
                Assert.AreEqual(index, destroyed.killingContext.sourceLifeId);
                deaths++;
                string nextId = "blast-" + application.victimLifeId;
                service.EnqueueBlast(new PendingBlast(nextId, new Vector2(index * 10f, 0f), 100f, 11f,
                    DamageContextFactory.CreateChainedContext(destroyed.killingContext, nextId, application.victimLifeId)));
            }
        }
        Assert.AreEqual(length, deaths);
        Assert.AreEqual(length, service.TrackedBlastCount);
        Assert.AreEqual(0, service.PendingCount);
        foreach (var receiver in health) Assert.IsTrue(receiver.IsDestroyed);
    }
}
