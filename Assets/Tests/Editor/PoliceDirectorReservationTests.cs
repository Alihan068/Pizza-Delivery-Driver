using NUnit.Framework;
using UnityEngine;

/// <summary>Pure EditMode coverage for S09 logical police reservation hardening.</summary>
public sealed class PoliceDirectorReservationTests {
    [Test]
    public void Reservations_CountQueuedAndInFlightAgainstGlobalAndEntryCaps() {
        PoliceDirectorData data = CreateData(2, 1f, 0f, 1);
        PoliceDirectorRuntime runtime = new PoliceDirectorRuntime(data, true, 7);
        try {
            runtime.Tick(0f);
            Assert.AreEqual(1, runtime.PendingCount);
            Assert.AreEqual(1, runtime.ReservedCount);
            Assert.IsTrue(runtime.TryDequeue(out var first));
            Assert.AreEqual(1, runtime.InFlightCount);

            runtime.Tick(1f);
            Assert.AreEqual(0, runtime.PendingCount, "The entry cap includes the in-flight request.");
            Assert.IsTrue(runtime.Commit(first, 101));
            runtime.Tick(2f);
            Assert.AreEqual(0, runtime.PendingCount, "The entry cap includes the committed life.");
            Assert.AreEqual(1, runtime.ActiveCount);
        }
        finally { runtime.End(); Object.DestroyImmediate(data); }
    }

    [Test]
    public void Reservations_RejectQueuedThenDequeueSkipsInvalidatedRequest() {
        PoliceDirectorData data = CreateData(2, 0f, 0f, 0);
        data.baseHeatPerActiveSecond = 1f;
        data.heatTiers.Add(new PoliceHeatTier {
            tierId = "tier.cancel",
            minimumHeat = 0.5f,
            targetCount = 0,
            compositions = new System.Collections.Generic.List<PoliceCompositionEntry> {
                new PoliceCompositionEntry {
                    vehicleProfileId = "police.standard",
                    behaviorProfileId = "pursue.standard",
                    weight = 1f
                }
            }
        });
        PoliceDirectorRuntime runtime = new PoliceDirectorRuntime(data, true, 8);
        try {
            runtime.Tick(0f);
            runtime.Tick(0f);
            Assert.AreEqual(2, runtime.PendingCount);
            runtime.Tick(1f);
            Assert.AreEqual(0, runtime.PendingCount);
            Assert.AreEqual(0, runtime.InFlightCount);
            Assert.AreEqual(0, runtime.ReservedCount);
            Assert.IsFalse(runtime.TryDequeue(out _), "A rejected queued item must not resurrect as in-flight.");
        }
        finally { runtime.End(); Object.DestroyImmediate(data); }
    }

    [Test]
    public void Reservations_RejectDuplicateAndStaleCallbacksWithoutChangingCounts() {
        PoliceDirectorData data = CreateData(1, 0f, 0f, 0);
        PoliceDirectorRuntime firstRuntime = new PoliceDirectorRuntime(data, true, 1);
        PoliceDirectorRuntime secondRuntime = new PoliceDirectorRuntime(data, true, 1);
        try {
            firstRuntime.Tick(0f);
            Assert.IsTrue(firstRuntime.TryDequeue(out var request));
            Assert.IsFalse(secondRuntime.Commit(request, 22));
            Assert.IsTrue(firstRuntime.Commit(request, 22));
            Assert.IsFalse(firstRuntime.Commit(request, 23));
            Assert.IsTrue(firstRuntime.Destroyed(22));
            Assert.AreEqual(0, firstRuntime.ActiveCount);
            Assert.IsFalse(firstRuntime.Destroyed(22));
            Assert.AreEqual(0, firstRuntime.ReservedCount);
        }
        finally { firstRuntime.End(); secondRuntime.End(); Object.DestroyImmediate(data); }
    }

    [Test]
    public void Reservations_ThreeDeathsRespectPerDeathDelayAndGlobalCadence() {
        PoliceDirectorData data = CreateData(3, 1f, 2f, 0);
        PoliceDirectorRuntime runtime = new PoliceDirectorRuntime(data, true, 4);
        try {
            int[] lifeIds = { 31, 32, 33 };
            for (int i = 0; i < lifeIds.Length; i++) {
                runtime.Tick(i);
                Assert.IsTrue(runtime.TryDequeue(out var request));
                Assert.IsTrue(runtime.Commit(request, lifeIds[i]));
            }
            runtime.Tick(3f);
            Assert.IsTrue(runtime.Destroyed(31));
            Assert.IsTrue(runtime.Destroyed(32));
            Assert.IsTrue(runtime.Destroyed(33));

            runtime.Tick(4.9f);
            Assert.AreEqual(0, runtime.PendingCount);
            runtime.Tick(5f);
            Assert.AreEqual(1, runtime.PendingCount);
            Assert.IsTrue(runtime.TryDequeue(out var firstReplacement));
            Assert.IsTrue(runtime.Reject(firstReplacement));
            runtime.Tick(5.1f);
            Assert.AreEqual(0, runtime.PendingCount, "A rejected request cannot create a same-frame burst.");
            runtime.Tick(6f);
            Assert.AreEqual(1, runtime.PendingCount);
        }
        finally { runtime.End(); Object.DestroyImmediate(data); }
    }

    [Test]
    public void InvalidProfile_DisablesRuntimeAndSourceMutationDoesNotLeakIntoDetachedSession() {
        PoliceDirectorData invalid = ScriptableObject.CreateInstance<PoliceDirectorData>();
        var invalidRuntime = new PoliceDirectorRuntime(invalid, true, 2);
        Assert.IsFalse(invalidRuntime.IsEnabled);
        invalidRuntime.End();
        Object.DestroyImmediate(invalid);

        PoliceDirectorData data = CreateData(1, 0f, 0f, 0);
        PoliceDirectorRuntime runtime = new PoliceDirectorRuntime(data, true, 3);
        try {
            data.heatTiers[0].targetCount = 0;
            data.heatTiers[0].compositions[0].vehicleProfileId = "mutated";
            runtime.Tick(0f);
            Assert.IsTrue(runtime.TryDequeue(out var request));
            Assert.AreEqual("police.standard", request.vehicleProfileId);
        }
        finally { runtime.End(); Object.DestroyImmediate(data); }
    }

    static PoliceDirectorData CreateData(int targetCount, float reinforcementInterval, float replacementDelay, int maximumCount) {
        PoliceDirectorData data = ScriptableObject.CreateInstance<PoliceDirectorData>();
        data.profileId = "test.director";
        data.earliestPoliceTime = 0f;
        data.maximumHeat = 100f;
        data.heatTiers.Add(new PoliceHeatTier {
            tierId = "tier.0",
            targetCount = targetCount,
            reinforcementInterval = reinforcementInterval,
            destroyedReplacementDelay = replacementDelay,
            compositions = new System.Collections.Generic.List<PoliceCompositionEntry> {
                new PoliceCompositionEntry {
                    vehicleProfileId = "police.standard",
                    behaviorProfileId = "pursue.standard",
                    weight = 1f,
                    maximumCount = maximumCount
                }
            }
        });
        return data;
    }
}
