using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// S08.4 authored police-type and civilian-contact matrix. It exercises real isolated physics,
/// authored police clones, and one authored civilian follower without duplicating motor or query
/// unit coverage.
/// </summary>
public sealed class PoliceTypeTrafficInteractionPlayTests {
    const string PoliceDataRoot = "Assets/ScriptableObjects/Police/Police";
    const string PoliceStandardVehiclePath = "Assets/ScriptableObjects/Police/PoliceStandard.asset";
    const string PoliceSportVehiclePath = "Assets/ScriptableObjects/Police/PoliceSport.asset";
    const string PoliceHeavyVehiclePath = "Assets/ScriptableObjects/Police/PoliceHeavy.asset";
    const string PursueBehaviorPath = "Assets/ScriptableObjects/Police/PolicePursueBehavior.asset";
    const string DamagePath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Damage.asset";
    const string CivilianPath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Compact_yellow.asset";

    /// <summary>Confirms the authored straight-road speed identity remains Sport, Standard, then Heavy.</summary>
    [Test]
    public void AuthoredTypes_StraightSpeedOrderingIsSportStandardHeavy() {
        float standard = MeasureStraightSpeed("Standard");
        float sport = MeasureStraightSpeed("Sport");
        float heavy = MeasureStraightSpeed("Heavy");

        Assert.Greater(sport, standard + 0.2f);
        Assert.Greater(standard, heavy + 0.1f);
    }

    /// <summary>Confirms a tangent available to Standard 1.2 but not Sport 3: Standard commits/releases, Sport refuses safely.</summary>
    [Test]
    public void AuthoredTypes_TightTurnUsesAuthoredRadiusRefusalAndRelease() {
        TurnResult standard = MeasureTightTurn("Standard", true, new Vector2(10f, 2.5f));
        Assert.IsTrue(standard.committed, standard.facts);
        Assert.IsTrue(standard.released, standard.facts);
        Assert.Greater(standard.outgoingMotion, 1.5f, standard.facts);
        Assert.IsTrue(standard.finite, standard.facts);

        TurnResult sport = MeasureTightTurn("Sport", true, new Vector2(10f, 2.5f));
        Assert.IsFalse(sport.committed, sport.facts);
        Assert.IsFalse(sport.reachedExit, sport.facts);
        Assert.AreEqual(sport.initialHealth, sport.finalHealth, sport.facts);
        Assert.IsTrue(sport.finite, sport.facts);
    }

    /// <summary>Retains the original short outgoing road as a safe-stop case without impact or release.</summary>
    [Test]
    public void AuthoredTypes_OriginalShortTurnStopsSafelyWithoutRelease() {
        TurnResult result = MeasureTightTurn("Standard", false, new Vector2(2.5f, 2.5f));
        Assert.IsFalse(result.impactObserved, result.facts);
        Assert.AreEqual(result.initialHealth, result.finalHealth, result.facts);
        Assert.IsFalse(result.released, result.facts);
        Assert.IsTrue(result.finite, result.facts);
    }

    /// <summary>Confirms Heavy rejects the narrow shortcut and physically traverses the wide alternative in one branched graph.</summary>
    [Test]
    public void HeavyType_BranchedRoadSelectsWideAlternativeAfterNarrowShortcutRefusal() {
        var authored = LoadAuthored("Heavy", includeCivilian: false);
        using (var fixture = new PursuitFixture(authored: authored)) {
            fixture.ConfigureAuthoredNavigation(HeavyBranchDocument(), Vector2.zero, new Vector2(0f, 2f), 0f, new Vector2(8f, 20f));
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);

            bool sawWideBranch = false;
            bool sawNarrowShortcut = false;
            for (int i = 0; i < 900; i++) {
                fixture.Step(1);
                if (!fixture.Cursor.IsBound) continue;
                string edge = fixture.Cursor.CurrentAnchor.edgeId;
                sawWideBranch |= edge == "wideBranch" || edge == "wideExit";
                sawNarrowShortcut |= edge == "narrowShortcut" || edge == "narrowExit";
            }

            Assert.IsFalse(sawNarrowShortcut, Facts(fixture, "Heavy used narrow shortcut"));
            Assert.IsTrue(sawWideBranch, Facts(fixture, "Heavy never traversed wide branch"));
            Assert.Greater(fixture.PolicePosition.x, 2f, Facts(fixture, "Heavy did not reach wide route"));
        }
    }

    /// <summary>Runs the real contact matrix in Play Mode, where isolated PhysicsScene2D callbacks are delivered.</summary>
    [UnityTest]
    public IEnumerator AuthoredTypes_CivilianContactUsesMeasuredImpactDamageMomentumAndRecovery() {
        yield return new EnterPlayMode();
        ContactResult standard = RunContactCase("Standard");
        ContactResult sport = RunContactCase("Sport");
        ContactResult heavy = RunContactCase("Heavy");

        Assert.LessOrEqual(Mathf.Abs(standard.policeImpact - sport.policeImpact), 0.75f);
        Assert.LessOrEqual(Mathf.Abs(standard.policeImpact - heavy.policeImpact), 0.75f);
        Assert.LessOrEqual(Mathf.Abs(sport.policeImpact - heavy.policeImpact), 0.75f);
        Assert.Greater(heavy.policeMass, standard.policeMass);
        Assert.Greater(standard.policeMass, sport.policeMass);
    }

    /// <summary>Always leaves the editor in Edit Mode after the one Play Mode contact matrix.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static ContactResult RunContactCase(string type) {
        var authored = LoadAuthored(type, includeCivilian: true);
        using (var fixture = new PursuitFixture(authored: authored)) {
            fixture.ConfigureAuthoredNavigation(CrossingDocument(), Vector2.zero, new Vector2(0f, 10f), 0f, new Vector2(0f, 36f));
            // Worst-case Heavy half-extents are (0.9, 1.5); the -90 civilian presents (0.75, 0.4).
            // At x=-1.8/y=12 the initial gaps are 1.8 and 2.0, just outside both envelopes, while
            // the seeded crossing times are about 0.6s (civilian) and 0.5s (police), so the
            // authored followers meet during the first approach instead of crossing too late.
            var civilian = fixture.CreateAuthoredCivilian(CivilianRoute(), new Vector2(-1.8f, 12f), -90f);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);

            float initialPoliceHealth = fixture.PoliceReceiver.CurrentHealth;
            float initialCivilianHealth = civilian.receiver.CurrentHealth;
            // Seed both bodies before the first simulation; the follower and controller remain genuine.
            fixture.SetPoliceVelocity(Vector2.up * 4f);
            // Seed along the body's actual authored forward axis. This keeps the motor's lateral-grip
            // pass from converting the intended crossing speed when a Rigidbody2D has not yet pushed
            // its Transform rotation through the first editor physics tick.
            civilian.body.linearVelocity = civilian.body.transform.up * 3f;
            float policeImpact = -1f, civilianImpact = -1f;
            Vector2 policePreImpact = Vector2.zero, civilianPreImpact = Vector2.zero;
            Vector2 civilianPostImpact = Vector2.zero;
            fixture.PoliceReceiver.ImpactObserved += value => {
                if (policeImpact >= 0f) return;
                policeImpact = value;
                policePreImpact = fixture.PoliceReceiver.Sample.linearVelocity;
                civilianPreImpact = civilian.receiver.Sample.linearVelocity;
                civilianPostImpact = civilian.body.linearVelocity;
            };
            civilian.receiver.ImpactObserved += value => {
                if (civilianImpact >= 0f) return;
                civilianImpact = value;
                policePreImpact = fixture.PoliceReceiver.Sample.linearVelocity;
                civilianPreImpact = civilian.receiver.Sample.linearVelocity;
                civilianPostImpact = civilian.body.linearVelocity;
            };

            for (int i = 0; i < 120 && (policeImpact < 0f || civilianImpact < 0f); i++) {
                fixture.Step(1);
            }

            Assert.GreaterOrEqual(policeImpact, 1.5f, Facts(fixture, "police contact callback missing", civilian));
            Assert.GreaterOrEqual(civilianImpact, 1.5f, Facts(fixture, "civilian contact callback missing", civilian));
            Assert.LessOrEqual(Mathf.Abs(policeImpact - civilianImpact), 0.2f, Facts(fixture, "impact mismatch", civilian));
            Assert.Less(fixture.PoliceReceiver.CurrentHealth, initialPoliceHealth, Facts(fixture, "police HP unchanged", civilian));
            Assert.Less(civilian.receiver.CurrentHealth, initialCivilianHealth, Facts(fixture, "civilian HP unchanged", civilian));
            float policeHealthAfterImpact = fixture.PoliceReceiver.CurrentHealth;
            float civilianHealthAfterImpact = civilian.receiver.CurrentHealth;
            Assert.Greater(policePreImpact.magnitude, 0.5f);
            Assert.Greater(civilianPreImpact.magnitude, 0.5f);
            Assert.Greater(Vector2.Distance(civilianPreImpact, civilianPostImpact), 0.01f);

            bool civilianRecovery = false;
            for (int i = 0; i < 60; i++) {
                fixture.Step(1);
                civilianRecovery |= civilian.follower.RecoveryPhase != CrashRecoveryPolicy.Phase.Driving;
            }
            Assert.IsTrue(civilianRecovery);

            Assert.IsTrue(fixture.World.Settings.TryResolve("nd-civilian", out var collision));
            float policeDamage = initialPoliceHealth - policeHealthAfterImpact;
            float civilianDamage = initialCivilianHealth - civilianHealthAfterImpact;
            Assert.AreEqual(ExpectedDamage(collision.collision, policeImpact, authored.profile.collisionArmor), policeDamage, 0.1f);
            Assert.AreEqual(ExpectedDamage(collision.collision, civilianImpact, civilian.profile.collisionArmor), civilianDamage, 0.1f);
            return new ContactResult {
                policeImpact = policeImpact,
                policeDamage = policeDamage,
                civilianDamage = civilianDamage,
                policeMass = authored.profile.baseMass,
                civilianMass = civilian.profile.baseMass
            };
        }
    }

    static float MeasureStraightSpeed(string type) {
        var authored = LoadAuthored(type, includeCivilian: false);
        using (var fixture = new PursuitFixture(authored: authored)) {
            fixture.ConfigureAuthoredNavigation(StraightDocument(), Vector2.zero, new Vector2(0f, 2f), 0f, new Vector2(0f, 60f));
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            float peak = 0f;
            for (int i = 0; i < 220; i++) { fixture.Step(1); peak = Mathf.Max(peak, fixture.ForwardSpeed); }
            return peak;
        }
    }

    struct TurnResult {
        internal bool committed;
        internal bool released;
        internal bool reachedExit;
        internal bool finite;
        internal float outgoingMotion;
        internal float initialHealth;
        internal float finalHealth;
        internal string facts;
        internal bool impactObserved;
    }

    struct ContactResult {
        internal float policeImpact;
        internal float policeDamage;
        internal float civilianDamage;
        internal float policeMass;
        internal float civilianMass;
    }

    static TurnResult MeasureTightTurn(string type, bool extendedOutgoing, Vector2 target) {
        var authored = LoadAuthored(type, includeCivilian: false);
        using (var fixture = new PursuitFixture(authored: authored)) {
            fixture.ConfigureAuthoredNavigation(TurnDocument(extendedOutgoing), Vector2.zero, new Vector2(0f, 0.5f), 0f, target);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            bool committed = false, released = false, reachedExit = false, finite = true;
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            float maxOutgoingMotion = 0f;
            for (int i = 0; i < 700; i++) {
                fixture.Step(1);
                committed |= fixture.Controller.IsTurnCommitted;
                if (committed && !fixture.Controller.IsTurnCommitted && !fixture.Controller.IsTurnExpired) released = true;
                if (fixture.Cursor.IsBound && fixture.Cursor.CurrentAnchor.edgeId == "turnExit") {
                    reachedExit = true;
                    maxOutgoingMotion = Mathf.Max(maxOutgoingMotion, fixture.PolicePosition.x);
                }
                finite &= IsFinite(fixture.PolicePosition) && IsFinite(fixture.PoliceVelocity);
            }
            return new TurnResult { committed = committed, released = released, reachedExit = reachedExit,
                finite = finite, outgoingMotion = maxOutgoingMotion, initialHealth = fixture.Profile.maxHealth,
                finalHealth = fixture.PoliceReceiver.CurrentHealth, facts = Facts(fixture, type + " tight turn"), impactObserved = impactObserved };
        }
    }

    static PursuitFixture.AuthoredSetup LoadAuthored(string type, bool includeCivilian) {
        var profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>(PoliceDataRoot + type + "Data.asset");
        string vehiclePath = VehiclePath(type);
        var vehicle = AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>(vehiclePath);
        var behavior = AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>(PursueBehaviorPath);
        var damage = AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>(DamagePath);
        var civilian = includeCivilian ? AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>(CivilianPath) : null;
        Assert.IsNotNull(profile, PoliceDataRoot + type + "Data.asset"); Assert.IsNotNull(vehicle, vehiclePath);
        Assert.IsNotNull(behavior); Assert.IsNotNull(damage);
        return new PursuitFixture.AuthoredSetup { profile = profile, vehicle = vehicle, behavior = behavior, damage = damage, civilianProfile = civilian };
    }

    static string VehiclePath(string type) {
        switch (type) {
            case "Standard": return PoliceStandardVehiclePath;
            case "Sport": return PoliceSportVehiclePath;
            case "Heavy": return PoliceHeavyVehiclePath;
            default: Assert.Fail("No authored police vehicle path for type " + type); return null;
        }
    }

    static float ExpectedDamage(NpcCollisionDamageProfile profile, float impact, float armor) {
        return (profile.damageBase + profile.damageFactor * Mathf.Pow(impact, profile.damageExponent)) * (1f - Mathf.Clamp01(armor));
    }

    static bool IsFinite(Vector2 value) {
        return IsFinite(value.x) && IsFinite(value.y);
    }

    static bool IsFinite(float value) {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static string Facts(PursuitFixture fixture, string label, PursuitFixture.CivilianActor civilian = null) {
        var result = fixture.Planner.CurrentResult;
        string planner = result == null ? "<none>" : result.status + "/" + result.waitReason;
        string anchor = fixture.Cursor.IsBound
            ? fixture.Cursor.CurrentAnchor.edgeId + "@" + fixture.Cursor.CurrentAnchor.distanceAlongEdge
            : "<unbound>";
        var command = fixture.Controller.LastCommand;
        string civilianFacts = civilian == null ? string.Empty :
            "; civilianPosition=" + civilian.body.position + "; civilianSpeed=" + civilian.body.linearVelocity.magnitude;
        return label + "; lastPosition=" + fixture.LastControllerPosition +
            "; policePosition=" + fixture.PolicePosition + "; policeSpeed=" + fixture.PoliceVelocity.magnitude +
            "; playerPosition=" + fixture.PlayerPosition + "; playerSpeed=" + fixture.PlayerVelocity.magnitude +
            civilianFacts +
            "; command=" + command.throttle + "/" + command.brake + "/" + command.steering +
            "; targetSpeed=" + command.targetSpeed + "; aim=" + fixture.Controller.LastAimPoint + "; planned=" + fixture.Controller.LastPlannedSpeed + "; reverse=" + command.reverseAllowed +
            "; planner=" + fixture.Controller.PlannerAttemptCount + "/" + planner +
            "; anchor=" + anchor + "; phase=" + fixture.Controller.CurrentPhase +
            "; ramming=" + fixture.Controller.IsRamming + "; ramStarts=" + fixture.Controller.RamStarts +
            "; ramCooldown=" + fixture.Controller.RamCooldownUntil +
            "; recovery=" + fixture.Controller.RecoveryPhase + "/" + fixture.Controller.IsRecoveryExhausted +
            "; gate=" + fixture.Recovery.Gate + "; reverseTravel=" + fixture.Recovery.ReverseTravel +
            "; resumeReady=" + fixture.Recovery.ResumeReady + "; turn=" + fixture.Controller.IsTurnCommitted + "/" + fixture.Controller.IsTurnExpired +
            "; turnCap=" + fixture.Controller.ActiveTurnCap + "; roadContinuations=" + fixture.Controller.ProvenRoadContinuations +
            "; connectorContinuations=" + fixture.Controller.ConnectorMetadataContinuations + "; safety=" + SafetyFacts(fixture);
    }

    static string SafetyFacts(PursuitFixture fixture) {
        var field = typeof(PolicePursuitController).GetField("ramSafety", BindingFlags.Instance | BindingFlags.NonPublic);
        var safety = field != null ? field.GetValue(fixture.Controller) as PoliceRamSafety : null;
        return safety == null ? "<none>" : safety.IsCertified + "/" + safety.FarEndpoint;
    }

    static MapNavigationDocument StraightDocument() {
        var document = new MapNavigationDocument { localBounds = new Rect(-20f, -10f, 40f, 90f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 80f });
        document.edges.Add(Edge("straight", "a", "b", Vector2.zero, Vector2.up * 80f, 8f, 10f, VehicleRole.Police));
        return document;
    }

    static MapNavigationDocument TurnDocument(bool extendedOutgoing) {
        var document = new MapNavigationDocument { localBounds = new Rect(-20f, -10f, 35f, 35f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 2.5f });
        float outgoingLength = extendedOutgoing ? 12f : 2.5f;
        document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = outgoingLength, y = 2.5f });
        // The incoming tangent remains 2.5: above Standard's 1.2 radius and below Sport's 3 radius.
        document.edges.Add(Edge("turnEntry", "a", "b", Vector2.zero, Vector2.up * 2.5f, 8f, 8f, VehicleRole.Police));
        document.edges.Add(Edge("turnExit", "b", "c", Vector2.up * 2.5f, new Vector2(outgoingLength, 2.5f), 8f, 8f, VehicleRole.Police));
        return document;
    }

    static MapNavigationDocument HeavyBranchDocument() {
        var document = new MapNavigationDocument { localBounds = new Rect(-20f, -10f, 50f, 50f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "start", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "junction", x = 0f, y = 10f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "narrow", x = 0f, y = 20f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "wide", x = 8f, y = 10f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "goal", x = 8f, y = 20f });
        document.edges.Add(Edge("approach", "start", "junction", Vector2.zero, Vector2.up * 10f, 8f, 8f, VehicleRole.Police));
        document.edges.Add(Edge("narrowShortcut", "junction", "narrow", Vector2.up * 10f, Vector2.up * 20f, 2f, 8f, VehicleRole.Police));
        document.edges.Add(Edge("narrowExit", "narrow", "goal", Vector2.up * 20f, new Vector2(8f, 20f), 2f, 8f, VehicleRole.Police));
        document.edges.Add(Edge("wideBranch", "junction", "wide", Vector2.up * 10f, new Vector2(8f, 10f), 8f, 8f, VehicleRole.Police));
        document.edges.Add(Edge("wideExit", "wide", "goal", new Vector2(8f, 10f), new Vector2(8f, 20f), 8f, 8f, VehicleRole.Police));
        return document;
    }

    static MapNavigationDocument CrossingDocument() {
        var document = new MapNavigationDocument { localBounds = new Rect(-20f, -10f, 50f, 50f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "p0", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "p1", x = 0f, y = 36f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c0", x = -12f, y = 12f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c1", x = 12f, y = 12f });
        document.edges.Add(Edge("policeRoad", "p0", "p1", Vector2.zero, Vector2.up * 36f, 8f, 8f, VehicleRole.Police));
        document.edges.Add(Edge("civilianCross", "c0", "c1", new Vector2(-12f, 12f), new Vector2(12f, 12f), 8f, 8f, VehicleRole.Civilian));
        return document;
    }

    static CivilianRouteRecord CivilianRoute() => new CivilianRouteRecord {
        routeId = "civilian-cross-route", loop = false, edgeIds = new List<string> { "civilianCross" }
    };

    static RoadEdgeRecord Edge(string id, string from, string to, Vector2 start, Vector2 end, float width, float speed, VehicleRole role) {
        return new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = width, speedLimit = speed,
            orderedPoints = new List<Vector2> { start, end }, allowedRoles = new List<VehicleRole> { role } };
    }
}
