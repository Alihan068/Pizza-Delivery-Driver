using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Native free-drive integration checks independent of the road graph and real save data.</summary>
public sealed class PoliceFreeRuntimePlayTests {
    /// <summary>Prevents a close player from leaving a reckless police life in passive empty-goal holding.</summary>
    [UnityTest]
    public IEnumerator ClosePlayerStillReceivesAnAggressiveCommand() {
        Assert.IsNull(GameManager.Instance);
        yield return new EnterPlayMode();
        using (var harness = new PopulationHarness(freeDrive: true)) {
            Spawn(harness);
            var fixture = harness.Fixture;
            fixture.SetPolicePosition(new Vector2(0f, -2f));
            fixture.SetPoliceRotation(0f);
            var player = fixture.Player.GetComponent<Rigidbody2D>();
            player.position = new Vector2(0f, 2f);
            Physics2D.SyncTransforms();
            bool aggressiveCommand = false;
            for (int step = 0; step < 80; step++) {
                harness.Runtime.PreparePhysics(fixture.World.SessionTime);
                fixture.Step(1);
                harness.Runtime.Tick(fixture.World.SessionTime);
                aggressiveCommand |= fixture.Controller.LastCommand.throttle > 0.5f || fixture.Controller.IsRamming;
            }
            Assert.IsTrue(aggressiveCommand, "Close player entered passive holding without a police pursuit command.");
            Assert.IsTrue(fixture.Controller.IsBound);
        }
    }
    /// <summary>Confirms police commit into the player instead of applying civilian avoidance.</summary>
    [UnityTest]
    public IEnumerator PoliceCommitsIntoPlayerAcrossAuthoredPursuitRoles() {
        Assert.IsNull(GameManager.Instance);
        yield return new EnterPlayMode();
        using (var harness = new PopulationHarness(freeDrive: true)) {
            Spawn(harness);
            var fixture = harness.Fixture;
            fixture.SetPolicePosition(new Vector2(0f, -4f));
            fixture.SetPoliceRotation(0f);
            var player = fixture.Player.GetComponent<Rigidbody2D>();
            player.position = new Vector2(0f, 4f);
            player.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            float health = fixture.Player.CurrentHealth;
            for (int step = 0; step < 240 && fixture.Player.CurrentHealth >= health; step++) {
                harness.Runtime.PreparePhysics(fixture.World.SessionTime);
                fixture.Step(1);
                harness.Runtime.Tick(fixture.World.SessionTime);
            }
            Assert.Less(fixture.Player.CurrentHealth, health, "Police avoided/stopped instead of committing into the player.");
            Assert.IsTrue(fixture.Controller.IsBound);
        }
    }
    /// <summary>Checks obstacle detouring and a changed player target at two physics timesteps.</summary>
    [UnityTest]
    public IEnumerator FreePursuitDetoursAndReplansForMovingPlayer() {
        Assert.IsNull(GameManager.Instance);
        yield return new EnterPlayMode();
        foreach (float dt in new[] { .02f, .03f }) {
            using (var harness = new PopulationHarness(freeDrive: true, deltaTime: dt)) {
                Spawn(harness);
                var fixture = harness.Fixture;
                var player = fixture.Player.GetComponent<Rigidbody2D>();
                fixture.SetPolicePosition(new Vector2(0f, -12f));
                fixture.SetPoliceRotation(0f);
                player.position = new Vector2(8f, 14f);
                fixture.CreateBlocker(Vector2.zero, false, new Vector2(4f, 4f));
                Physics2D.SyncTransforms();
                float initialGap = fixture.Gap;
                float travel = 0f;
                for (int step = 0; step < Mathf.CeilToInt(20f / dt); step++) {
                    if (step == Mathf.CeilToInt(10f / dt)) player.linearVelocity = Vector2.right * .3f;
                    Vector2 before = fixture.PolicePosition;
                    harness.Runtime.PreparePhysics(fixture.World.SessionTime);
                    fixture.Step(1);
                    harness.Runtime.Tick(fixture.World.SessionTime);
                    travel += Vector2.Distance(before, fixture.PolicePosition);
                    Assert.IsTrue(fixture.Controller.IsBound, "free binding lost at " + fixture.World.SessionTime);
                }
                Assert.Greater(travel, 10f, "dt=" + dt);
                Assert.Less(fixture.Gap, initialGap - 10f, "dt=" + dt + " police=" + fixture.PolicePosition);
            }
        }
    }

    /// <summary>Checks a late obstacle causes native impact and recovery rather than an instantaneous stop.</summary>
    [UnityTest]
    public IEnumerator LateObstacleProducesNativeImpactAndFiniteRecovery() {
        Assert.IsNull(GameManager.Instance);
        yield return new EnterPlayMode();
        using (var harness = new PopulationHarness(freeDrive: true)) {
            Spawn(harness);
            var fixture = harness.Fixture;
            fixture.SetPolicePosition(Vector2.zero);
            fixture.SetPoliceRotation(0f);
            fixture.SetPoliceVelocity(Vector2.up * 8f);
            fixture.CreateBlocker(new Vector2(0f, 2f), false, new Vector2(6f, 1f));
            Physics2D.SyncTransforms();
            int impacts = 0;
            fixture.PoliceReceiver.ImpactObserved += speed => impacts++;
            float health = fixture.PoliceReceiver.CurrentHealth;
            bool resumedAggressivePursuit = false;
            bool stayedOnThrottle = false;
            harness.Runtime.PreparePhysics(fixture.World.SessionTime);
            fixture.Step(1);
            Assert.Greater(fixture.PoliceVelocity.magnitude, 0f, "No instantaneous velocity clamp.");
            for (int step = 0; step < 400; step++) {
                harness.Runtime.PreparePhysics(fixture.World.SessionTime);
                fixture.Step(1);
                harness.Runtime.Tick(fixture.World.SessionTime);
                if (impacts > 0 && !fixture.Controller.IsRecovering && fixture.Controller.LastCommand.throttle > 0.5f)
                    resumedAggressivePursuit = true;
                if (impacts > 0 && fixture.Controller.LastCommand.throttle > 0.5f && fixture.Controller.LastCommand.brake <= 0.01f)
                    stayedOnThrottle = true;
            }
            Assert.Greater(impacts, 0);
            Assert.Less(fixture.PoliceReceiver.CurrentHealth, health);
            Assert.IsFalse(fixture.Controller.IsRecovering, "Reckless police must not enter civilian recovery after impact.");
            Assert.IsTrue(resumedAggressivePursuit, "Police remained in civilian-style post-impact recovery.");
            Assert.IsTrue(stayedOnThrottle, "Police issued brake/stop instead of continuing impact pursuit.");
            Assert.IsTrue(fixture.Controller.IsBound);
        }
    }

    /// <summary>Always restores Edit Mode after the isolated native checks.</summary>
    [UnityTearDown]
    public IEnumerator LeavePlayMode() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void Spawn(PopulationHarness harness) {
        Assert.IsTrue(harness.TryRequest(out var request));
        harness.Runtime.TryMaterialize(request);
        for (int step = 0; step < 200 && harness.Runtime.ActiveCount == 0; step++) harness.Runtime.PreparePhysics(0f);
        Assert.AreEqual(1, harness.Runtime.ActiveCount);
    }
}
