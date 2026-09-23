using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using System.Collections;
using StackFrame = System.Diagnostics.StackFrame;
using StackTrace = System.Diagnostics.StackTrace;

/// <summary>Bounded diagnostic probe for authored NarrowDistrict turn and final-connector behavior.</summary>
public sealed class PoliceNarrowDistrictTurnProbeTests {
    const string MapPath = "Assets/ScriptableObjects/Map_NarrowDistrict.asset";
    const string NavigationPath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Navigation.asset";
    const string DamagePath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Damage.asset";
    const string PoliceDataPath = "Assets/ScriptableObjects/Police/PoliceStandardData.asset";
    const string PoliceVehiclePath = "Assets/ScriptableObjects/Police/PoliceStandard.asset";
    const string PursueBehaviorPath = "Assets/ScriptableObjects/Police/PolicePursueBehavior.asset";

    /// <summary>Runs the two authored probes and reports first static-clearance failures without treating them as acceptance.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrictTurnAndFinalProbe() => RunProbe(false);

    /// <summary>Runs the authored turn and final-connector cases with movement and phase-transition acceptance checks.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrictTurnAndFinalAcceptance() => RunProbe(true);

    static IEnumerator RunProbe(bool requireCompletion) {
        AssertEmptyBootstrap();
        VerifySourceAssetBindings();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);

        PoliceNarrowDistrictGeometrySnapshot snapshot = null;
        try {
            snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
            Assert.IsNotNull(snapshot, "The operator must capture NarrowDistrict before the TestRunner bootstrap.");
            Assert.Greater(snapshot.ColliderCount, 0);
            if (requireCompletion) {
                var completionFailures = new List<string>();
                RunCase(snapshot, new Vector2(6f, -12.5f), 0f, new Vector2(34f, 5f), "authored-turn-final",
                    requireCompletion, true, completionFailures);
                RunCase(snapshot, new Vector2(20f, 5f), -90f, new Vector2(34f, 5f), "authored-final-no-turn",
                    requireCompletion, false, completionFailures);
                Assert.IsEmpty(completionFailures, string.Join(" || ", completionFailures.ToArray()));
            }
            else {
                RunCase(snapshot, new Vector2(6f, -12.5f), 0f, new Vector2(34f, 5f), "authored-turn-final");
                RunCase(snapshot, new Vector2(20f, 5f), -90f, new Vector2(34f, 5f), "authored-final-no-turn");
            }
        }
        finally {
            try {
                if (snapshot != null) snapshot.AssertSourcesUnchanged();
            }
            finally {
                PoliceNarrowDistrictGeometrySnapshot.ClearPending();
            }
        }
    }

    /// <summary>Returns the isolated authored probe to Edit Mode after a diagnostic failure.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void RunCase(PoliceNarrowDistrictGeometrySnapshot snapshot, Vector2 policePosition, float heading,
        Vector2 targetPosition, string caseId, bool requireCompletion = false, bool expectTurn = false,
        List<string> completionFailures = null) {
        PursuitFixture.AuthoredSetup authored = LoadAuthoredReferences();
        using (var fixture = new PursuitFixture(0.02f, authored: authored)) {
            AssertFixtureClones(authored, fixture, caseId);
            using (snapshot.StageInto(fixture.FixtureScene)) {
                MapNavigationDocument navigation = snapshot.ResolveNavigation();
                Assert.IsNotNull(navigation, Facts(fixture, caseId + " detached navigation"));
                fixture.ConfigureAuthoredNavigation(navigation, snapshot.Origin, policePosition, heading, targetPosition);

                bool pureQuerySucceeded = fixture.TryQueryCurrent(out PoliceRoadTargetQuery.Result pureResult);
                Assert.IsNotNull(pureResult, Facts(fixture, caseId + " initial pure query result"));
                string pureObservation = DescribePureResult(pureResult);
                pureObservation = "returned=" + pureQuerySucceeded + ", " + pureObservation;
                var recorder = new StaticQueryProbe(fixture, snapshot.Origin, fixture.Binding.staticClearance,
                    fixture.ControllerSettings.sensorBuffer);
                fixture.Binding.staticClearance = recorder;

                Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), caseId + ": " + reason);
                Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), caseId + ": " + reason);
                Assert.IsTrue(fixture.Controller.IsBound, Facts(fixture, caseId + " binding"));

                float initialPoliceHealth = fixture.PoliceReceiver.CurrentHealth;
                float initialPlayerHealth = fixture.Player.CurrentHealth;
                Vector2 previousPosition = fixture.PolicePosition;
                float movedDistance = 0f;
                float maximumProgress = fixture.Controller.CursorProgress;
                bool previousCommitted = fixture.Controller.IsTurnCommitted;
                bool sawCommitted = false;
                bool sawCommitRelease = false;
                bool sawFinal = false;
                bool sawHolding = false;
                bool impactObserved = false;
                fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;

                int steps = Mathf.CeilToInt(30f / fixture.StepDelta);
                for (int frame = 0; frame < steps; frame++) {
                    recorder.Frame = frame;
                    fixture.Step(1);
                    Vector2 currentPosition = fixture.PolicePosition;
                    movedDistance += Vector2.Distance(previousPosition, currentPosition);
                    previousPosition = currentPosition;
                    maximumProgress = Mathf.Max(maximumProgress, fixture.Controller.CursorProgress);
                    bool committed = fixture.Controller.IsTurnCommitted;
                    sawCommitted |= committed;
                    sawCommitRelease |= previousCommitted && !committed;
                    previousCommitted = committed;
                    sawFinal |= fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.FinalConnector;
                    sawHolding |= fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.Holding;
                    AssertFinite(fixture, Facts(fixture, caseId + " frame=" + frame));
                }

                Assert.IsTrue(IsFinite(movedDistance) && IsFinite(maximumProgress), Facts(fixture, caseId + " telemetry nonfinite"));
                Assert.GreaterOrEqual(recorder.TotalQueries, 0, Facts(fixture, caseId + " query telemetry invalid"));

                Debug.Log("NARROWDISTRICT TURN PROBE " + (requireCompletion ? "acceptance" : "diagnosticOnly") + " case=" + caseId + ", pure=" + pureObservation +
                    ", queries=" + recorder.TotalQueries + ", failures=" + recorder.FailureCount +
                    ", movedDistance=" + movedDistance + ", maxProgress=" + maximumProgress +
                    ", committed=" + sawCommitted + ", commitRelease=" + sawCommitRelease +
                    ", final=" + sawFinal + ", holding=" + sawHolding + ", impact=" + impactObserved +
                    ", policeHp=" + fixture.PoliceReceiver.CurrentHealth + ", playerHp=" + fixture.Player.CurrentHealth +
                    ", facts=" + Facts(fixture, caseId) + ", rotation=" + fixture.Binding.body.Body.rotation +
                    ", angularVelocity=" + fixture.Binding.body.Body.angularVelocity + ", colliderGap=" + fixture.ColliderGap +
                    ", plannedSpeed=" + fixture.Controller.LastPlannedSpeed + ", command=" + fixture.Controller.LastCommand.throttle + "/" +
                     fixture.Controller.LastCommand.brake + "/" + fixture.Controller.LastCommand.steering +
                     ", targetSpeed=" + fixture.Controller.LastCommand.targetSpeed +
                     ", failuresDetail=" + recorder.DescribeFailures());
                if (requireCompletion) {
                    if (!(movedDistance > 0f)) completionFailures.Add(Facts(fixture, caseId + " did not move"));
                    if (!sawFinal) completionFailures.Add(Facts(fixture, caseId + " did not reach final connector"));
                    if (!sawHolding) completionFailures.Add(Facts(fixture, caseId + " did not reach holding"));
                    if (expectTurn) {
                        if (!sawCommitted) completionFailures.Add(Facts(fixture, caseId + " did not commit turn"));
                        if (!sawCommitRelease) completionFailures.Add(Facts(fixture, caseId + " did not release turn commit"));
                    }
                }
                Assert.AreEqual(initialPoliceHealth, fixture.PoliceReceiver.CurrentHealth,
                    Facts(fixture, caseId + " police health changed"));
                Assert.AreEqual(initialPlayerHealth, fixture.Player.CurrentHealth,
                    Facts(fixture, caseId + " player health changed"));
            }
            snapshot.AssertSourcesUnchanged();
        }
    }

    static PursuitFixture.AuthoredSetup LoadAuthoredReferences() {
        var profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>(PoliceDataPath);
        var damage = AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>(DamagePath);
        var vehicle = AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>(PoliceVehiclePath);
        var behavior = AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>(PursueBehaviorPath);
        Assert.IsNotNull(profile, PoliceDataPath);
        Assert.IsNotNull(damage, DamagePath);
        Assert.IsNotNull(vehicle, PoliceVehiclePath);
        Assert.IsNotNull(behavior, PursueBehaviorPath);
        Assert.AreSame(profile, vehicle.sharedNpc);
        Assert.IsNotNull(behavior.driving);
        return new PursuitFixture.AuthoredSetup { profile = profile, damage = damage, vehicle = vehicle, behavior = behavior };
    }

    static void AssertFixtureClones(PursuitFixture.AuthoredSetup authored, PursuitFixture fixture, string caseId) {
        Assert.AreNotSame(authored.profile, fixture.Profile, Facts(fixture, caseId + " profile clone"));
        Assert.AreNotSame(authored.vehicle, fixture.Binding.vehicle, Facts(fixture, caseId + " vehicle clone"));
        Assert.AreNotSame(authored.behavior, fixture.Binding.behavior, Facts(fixture, caseId + " behavior clone"));
        Assert.AreSame(fixture.Profile, fixture.Binding.vehicle.sharedNpc, Facts(fixture, caseId + " profile wrapper"));
        Assert.AreEqual(JsonUtility.ToJson(authored.profile), JsonUtility.ToJson(fixture.Profile), Facts(fixture, caseId + " profile values"));
        Assert.AreEqual(JsonUtility.ToJson(authored.behavior.driving), JsonUtility.ToJson(fixture.ControllerSettings),
            Facts(fixture, caseId + " driving values"));
    }

    static string DescribePureResult(PoliceRoadTargetQuery.Result result) {
        var text = new StringBuilder();
        text.Append("status=").Append(result.status).Append(", graphVersion=").Append(result.graphVersion);
        text.Append(", hasFinalApproach=").Append(result.hasFinalApproach);
        text.Append(", roadTarget=").Append(result.roadTarget);
        text.Append(", finalStart=").Append(result.finalApproachStart);
        text.Append(", finalEnd=").Append(result.finalApproachEnd);
        text.Append(", spans=");
        if (result.spans == null) text.Append("null");
        else {
            text.Append(result.spans.Count).Append("[");
            for (int index = 0; index < result.spans.Count; index++) {
                if (index > 0) text.Append(";");
                text.Append(result.spans[index].edgeId).Append("@").Append(result.spans[index].startDistance)
                    .Append("-").Append(result.spans[index].endDistance);
            }
            text.Append("]");
        }
        return text.ToString();
    }

    static void VerifySourceAssetBindings() {
        var map = AssetDatabase.LoadAssetAtPath<MapData>(MapPath);
        var navigation = AssetDatabase.LoadAssetAtPath<TrafficMapData>(NavigationPath);
        var damage = AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>(DamagePath);
        Assert.IsNotNull(map, MapPath);
        Assert.IsNotNull(navigation, NavigationPath);
        Assert.IsNotNull(damage, DamagePath);
        Assert.AreEqual("4c46aef37a1f4f468fc0915175730643", map.mapId);
        Assert.AreEqual("NarrowDistrict", map.sceneName);
        Assert.AreEqual(map.mapId, navigation.ResolveDocument().mapId);
        Assert.IsTrue(damage.TryResolve("nd-civilian", out _));
    }

    static void AssertEmptyBootstrap() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));
    }

    static void AssertFinite(PursuitFixture fixture, string facts) {
        Assert.IsTrue(IsFinite(fixture.PolicePosition.x) && IsFinite(fixture.PolicePosition.y), facts);
        Assert.IsTrue(IsFinite(fixture.PoliceVelocity.x) && IsFinite(fixture.PoliceVelocity.y), facts);
        Assert.IsTrue(IsFinite(fixture.Controller.CursorProgress), facts);
    }

    static string Facts(PursuitFixture fixture, string detail) {
        string edge = fixture.Cursor != null && fixture.Cursor.IsBound ? fixture.Cursor.CurrentAnchor.edgeId : "<unbound>";
        return "case=" + detail + ", phase=" + fixture.Controller.CurrentPhase + ", edge=" + edge +
            ", progress=" + fixture.Controller.CursorProgress + ", planner=" + fixture.Controller.PlannerAttemptCount +
            ", position=" + fixture.PolicePosition + ", velocity=" + fixture.PoliceVelocity;
    }

    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    sealed class StaticQueryProbe : IAreaClearanceQuery {
        readonly PursuitFixture fixture;
        readonly Vector2 origin;
        readonly IAreaClearanceQuery inner;
        readonly int originalCapacity;
        readonly Collider2D[] diagnosticResults = new Collider2D[512];
        readonly ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        readonly List<QueryFailure> failures = new List<QueryFailure>();
        QueryFailure mostRecentFailure;
        internal int Frame { get; set; }
        internal int TotalQueries { get; private set; }
        internal int FailureCount => failures.Count;

        internal StaticQueryProbe(PursuitFixture fixture, Vector2 origin, IAreaClearanceQuery inner, int originalCapacity) {
            this.fixture = fixture; this.origin = origin; this.inner = inner; this.originalCapacity = originalCapacity;
        }

        /// <summary>Forwards the exact production query while retaining first-by-callsite and bounded most-recent failure evidence.</summary>
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) {
            TotalQueries++;
            int rawCount = fixture.Physics.OverlapBox(center + origin, footprint, headingDegrees, filter, diagnosticResults);
            bool innerResult = inner.IsAreaClear(center, footprint, headingDegrees);
            if (!innerResult) {
                CaptureMostRecentFailure(center, footprint, headingDegrees, rawCount, innerResult, TotalQueries);
                CaptureFirstFailure(center, footprint, headingDegrees, rawCount, innerResult, TotalQueries);
            }
            return innerResult;
        }

        internal string DescribeFailures() {
            var text = new StringBuilder();
            for (int index = 0; index < failures.Count; index++) {
                if (index > 0) text.Append(" || ");
                text.Append(failures[index]);
            }
            if (mostRecentFailure != null) {
                if (text.Length > 0) text.Append(" || ");
                text.Append("mostRecent=").Append(mostRecentFailure);
            }
            return text.ToString();
        }

        // This bounded snapshot is overwritten without a stack trace or per-failure witness string allocation.
        void CaptureMostRecentFailure(Vector2 center, Vector2 footprint, float headingDegrees, int rawCount,
            bool innerResult, int queryIndex) {
            if (mostRecentFailure == null) mostRecentFailure = new QueryFailure { WitnessColliders = new Collider2D[6] };
            mostRecentFailure.Callsite = "<not captured for most-recent failure>";
            mostRecentFailure.QueryIndex = queryIndex;
            mostRecentFailure.Frame = Frame;
            mostRecentFailure.LocalCenter = center;
            mostRecentFailure.WorldCenter = center + origin;
            mostRecentFailure.Footprint = footprint;
            mostRecentFailure.Heading = headingDegrees;
            mostRecentFailure.RawCount = rawCount;
            mostRecentFailure.RawDiagnosticSaturated = rawCount == diagnosticResults.Length;
            mostRecentFailure.OriginalCapacity = originalCapacity;
            mostRecentFailure.InnerResult = innerResult;
            mostRecentFailure.SelfCount = 0;
            mostRecentFailure.Position = fixture.PolicePosition;
            mostRecentFailure.CursorProgress = fixture.Controller.CursorProgress;
            mostRecentFailure.PhaseValue = fixture.Controller.CurrentPhase;
            mostRecentFailure.HasPhaseValue = true;
            mostRecentFailure.PlannerAttempts = fixture.Controller.PlannerAttemptCount;
            Array.Clear(mostRecentFailure.WitnessColliders, 0, mostRecentFailure.WitnessColliders.Length);

            int witnessCount = 0;
            int witnessLimit = Mathf.Min(rawCount, diagnosticResults.Length);
            for (int index = 0; index < witnessLimit; index++) {
                Collider2D collider = diagnosticResults[index];
                if (collider == null) continue;
                if (collider.attachedRigidbody == fixture.Binding.body.Body) mostRecentFailure.SelfCount++;
                if (collider.attachedRigidbody != null && collider.attachedRigidbody.bodyType != RigidbodyType2D.Static)
                    continue;
                if (witnessCount < mostRecentFailure.WitnessColliders.Length)
                    mostRecentFailure.WitnessColliders[witnessCount++] = collider;
            }
        }

        void CaptureFirstFailure(Vector2 center, Vector2 footprint, float headingDegrees, int rawCount,
            bool innerResult, int queryIndex) {
            Assert.IsTrue(IsFinite(center.x) && IsFinite(center.y) && IsFinite(footprint.x) && IsFinite(footprint.y) && IsFinite(headingDegrees));
            Assert.GreaterOrEqual(rawCount, 0);
            Assert.Greater(originalCapacity, 0);
            if (failures.Count >= 8) return;
            string callsite = ProductionCallsite();
            for (int index = 0; index < failures.Count; index++) if (failures[index].Callsite == callsite) return;
            var failure = new QueryFailure {
                Callsite = callsite,
                QueryIndex = queryIndex,
                Frame = Frame,
                LocalCenter = center,
                WorldCenter = center + origin,
                Footprint = footprint,
                Heading = headingDegrees,
                RawCount = rawCount,
                RawDiagnosticSaturated = rawCount == diagnosticResults.Length,
                OriginalCapacity = originalCapacity,
                InnerResult = innerResult,
                SelfCount = 0,
                Position = fixture.PolicePosition,
                CursorProgress = fixture.Controller.CursorProgress,
                Phase = fixture.Controller.CurrentPhase.ToString(),
                PlannerAttempts = fixture.Controller.PlannerAttemptCount
            };
            int witnessLimit = Mathf.Min(rawCount, diagnosticResults.Length);
            for (int index = 0; index < witnessLimit; index++) {
                Collider2D collider = diagnosticResults[index];
                if (collider == null) continue;
                if (collider.attachedRigidbody == fixture.Binding.body.Body) failure.SelfCount++;
                if (failure.StaticWitnesses.Count >= 6) continue;
                if (collider.attachedRigidbody == null || collider.attachedRigidbody.bodyType == RigidbodyType2D.Static)
                    failure.StaticWitnesses.Add(collider.gameObject.name + " bounds=" + collider.bounds);
            }
            failures.Add(failure);
        }

        static string ProductionCallsite() {
            StackFrame[] frames = new StackTrace(1, false).GetFrames();
            if (frames == null) return "<no-stack>";
            bool afterProbe = false;
            var names = new List<string>();
            for (int index = 0; index < frames.Length && names.Count < 4; index++) {
                MethodBase method = frames[index].GetMethod();
                if (method == null) continue;
                string typeName = method.DeclaringType == null ? "<global>" : method.DeclaringType.Name;
                if (!afterProbe) {
                    if (typeName == nameof(StaticQueryProbe) && method.Name == nameof(IsAreaClear)) afterProbe = true;
                    continue;
                }
                if (typeName == nameof(PhysicsSceneAreaClearanceQuery) || typeName == nameof(StaticQueryProbe)) continue;
                names.Add(typeName + "." + method.Name);
            }
            return string.Join(" <- ", names.ToArray());
        }
    }

    sealed class QueryFailure {
        internal string Callsite;
        internal int QueryIndex;
        internal int Frame;
        internal Vector2 LocalCenter;
        internal Vector2 WorldCenter;
        internal Vector2 Footprint;
        internal float Heading;
        internal int RawCount;
        internal bool RawDiagnosticSaturated;
        internal int OriginalCapacity;
        internal bool InnerResult;
        internal int SelfCount;
        internal Vector2 Position;
        internal float CursorProgress;
        internal string Phase;
        internal PolicePursuitController.TraversalPhase PhaseValue;
        internal bool HasPhaseValue;
        internal int PlannerAttempts;
        internal readonly List<string> StaticWitnesses = new List<string>();
        internal Collider2D[] WitnessColliders;

        /// <summary>Formats bounded failure evidence with query/frame identity, raw saturation, self-hit facts, and retained static witnesses.</summary>
        public override string ToString() {
            return "callsite=" + Callsite + ", query=" + QueryIndex + ", frame=" + Frame + ", local=" + LocalCenter + ", world=" + WorldCenter +
                ", size=" + Footprint + ", heading=" + Heading + ", raw=" + RawCount + "/" + OriginalCapacity +
                ", rawDiagnosticSaturated=" + RawDiagnosticSaturated +
                ", self=" + SelfCount + ", inner=" + InnerResult + ", position=" + Position +
                ", cursor=" + CursorProgress + ", phase=" + (HasPhaseValue ? PhaseValue.ToString() : Phase) + ", planner=" + PlannerAttempts +
                ", static=" + DescribeWitnesses();
        }

        string DescribeWitnesses() {
            if (WitnessColliders == null) return string.Join(",", StaticWitnesses.ToArray());
            var text = new StringBuilder();
            for (int index = 0; index < WitnessColliders.Length; index++) {
                Collider2D collider = WitnessColliders[index];
                if (collider == null) continue;
                if (text.Length > 0) text.Append(",");
                text.Append(collider.gameObject.name).Append(" bounds=").Append(collider.bounds);
            }
            return text.ToString();
        }
    }
}
