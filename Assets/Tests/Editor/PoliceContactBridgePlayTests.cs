using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Play-mode physical callback gate for the police contact bridge.</summary>
public sealed class PoliceContactBridgePlayTests {
    const int ExpectedContactCount = 1;

    /// <summary>
    /// Runs one isolated callback matrix covering multi-collider retention, trigger exclusion, and
    /// stale removal after receiver wreck or disable. Each case owns a separate fixture.
    /// </summary>
    [UnityTest]
    public IEnumerator ContactBridgePhysicalCallbackMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance, "The isolated bootstrap must not initialize career or save services.");

        RunMultiColliderExitRetention();
        RunTriggerExclusion();
        RunWreckRemoval();
        RunDisabledReceiverRemoval();
    }

    /// <summary>Always restores Edit Mode after the isolated callback matrix.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void RunMultiColliderExitRetention() {
        using (var fixture = NewContactFixture(out var bridge)) {
            Collider2D first = fixture.PoliceCollider;
            var second = fixture.PoliceReceiver.gameObject.AddComponent<BoxCollider2D>();
            second.size = first.GetComponent<BoxCollider2D>().size;
            second.offset = new Vector2(0.2f, 0f);
            fixture.SetPoliceBodyType(RigidbodyType2D.Static);
            fixture.SetPolicePosition(fixture.PlayerPosition);
            fixture.SetPoliceVelocity(Vector2.zero);

            fixture.Step(1);
            Assert.IsTrue(first.IsTouching(fixture.Player.GetComponent<Collider2D>()));
            var liveContacts = new System.Collections.Generic.List<int>();
            Assert.AreEqual(ExpectedContactCount, bridge.CollectLivePoliceLifeIds(liveContacts));

            first.enabled = false;
            fixture.Step(1);
            Assert.AreEqual(ExpectedContactCount, bridge.ContactIds.Count,
                "The second solid pair must retain the same police life after the first pair exits.");

            second.enabled = false;
            fixture.Step(1);
            Assert.AreEqual(0, bridge.ContactIds.Count);
        }
    }

    static void RunTriggerExclusion() {
        using (var fixture = NewContactFixture(out var bridge)) {
            fixture.PoliceCollider.isTrigger = true;
            fixture.SetPoliceBodyType(RigidbodyType2D.Static);
            fixture.SetPolicePosition(fixture.PlayerPosition);
            fixture.Step(1);
            Assert.AreEqual(0, bridge.ContactIds.Count, "Trigger overlap must never become an arrest contact.");
        }
    }

    static void RunWreckRemoval() {
        using (var fixture = NewContactFixture(out var bridge)) {
            fixture.SetPoliceBodyType(RigidbodyType2D.Static);
            fixture.SetPolicePosition(fixture.PlayerPosition);
            fixture.Step(1);
            Assert.AreEqual(ExpectedContactCount, bridge.ContactIds.Count);

            int lifeId = fixture.PoliceReceiver.Identity.lifeId;
            var context = new DamageContext("contact-wreck", 0, InstigatorKind.Environment, 0,
                DamageKind.Explosion, "contact-wreck");
            Assert.IsTrue(fixture.PoliceReceiver.ApplyBlast(
                new BlastApplication("contact-wreck", lifeId, VehicleRole.Police, float.MaxValue, context)));
            Assert.IsTrue(fixture.PoliceReceiver.IsWreck);
            fixture.Step(1);
            Assert.AreEqual(0, bridge.ContactIds.Count, "A wrecked receiver must be removed from live contacts.");
        }
    }

    static void RunDisabledReceiverRemoval() {
        using (var fixture = NewContactFixture(out var bridge)) {
            fixture.SetPoliceBodyType(RigidbodyType2D.Static);
            fixture.SetPolicePosition(fixture.PlayerPosition);
            fixture.Step(1);
            Assert.AreEqual(ExpectedContactCount, bridge.ContactIds.Count);

            fixture.PoliceReceiver.enabled = false;
            fixture.Step(1);
            Assert.AreEqual(0, bridge.ContactIds.Count, "A disabled receiver must be removed from live contacts.");
        }
    }

    static PursuitFixture NewContactFixture(out PoliceContactBridge bridge) {
        var fixture = new PursuitFixture(0.02f);
        bridge = fixture.Player.gameObject.AddComponent<PoliceContactBridge>();
        bridge.Configure((PoliceContactTracker)null);
        return fixture;
    }
}
