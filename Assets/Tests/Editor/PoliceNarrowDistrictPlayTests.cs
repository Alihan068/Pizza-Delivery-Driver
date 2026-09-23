using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Evidence-only Play-mode probe for authored NarrowDistrict composition.</summary>
public sealed class PoliceNarrowDistrictPlayTests {
    const string MapPath = "Assets/ScriptableObjects/Map_NarrowDistrict.asset";
    const string NavigationPath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Navigation.asset";
    const string DamagePath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Damage.asset";
    const string PoliceDataPath = "Assets/ScriptableObjects/Police/PoliceStandardData.asset";
    const string PoliceVehiclePath = "Assets/ScriptableObjects/Police/PoliceStandard.asset";
    const string PursueBehaviorPath = "Assets/ScriptableObjects/Police/PolicePursueBehavior.asset";
    const string RamBehaviorPath = "Assets/ScriptableObjects/Police/PoliceRamBehavior.asset";
    static bool anchorDiagnosticsEnabled;

    /// <summary>Reproduces the owner's off-road target and lateral police drift against unchanged authored collision geometry.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrictDistantTarget_ResumesFromObservedStoppedPose() {
        Assert.IsFalse(Application.isPlaying);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        VerifySourceAssetBindings();
        yield return new EnterPlayMode();
        Assert.IsNull(GameManager.Instance, "The isolated regression must not initialize career services.");
        PoliceNarrowDistrictGeometrySnapshot snapshot = null;
        try {
            snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
            Assert.IsNotNull(snapshot);
            using (var fixture = new PursuitFixture(0.02f, authored: LoadAuthoredReferences(PursueBehaviorPath))) {
                using (snapshot.StageInto(fixture.FixtureScene)) {
                    fixture.ConfigureAuthoredNavigation(snapshot.ResolveNavigation(), snapshot.Origin,
                        new Vector2(-46.08f, 22.08f), 180.6f, new Vector2(-66.09f, -23.35f));
                    fixture.Binding.navigationSettings.workBudget = 4096;
                    BindTarget(fixture, "owner stopped-pose regression");
                    Vector2 start = fixture.PolicePosition;
                    float gap = fixture.Gap;
                    fixture.Controller.Tick(fixture.StepDelta, false);
                    Assert.AreEqual(start, fixture.PolicePosition, "Binding cannot teleport the police.");
                    Assert.IsTrue(fixture.Cursor.IsBound, Facts(fixture, "initial acquisition"));
                    Assert.Greater(fixture.Controller.LastCommand.throttle, 0f, Facts(fixture, "initial propulsion"));
                    fixture.Step(500);
                    Assert.IsTrue(fixture.Controller.IsBound);
                    Assert.Greater(Vector2.Distance(start, fixture.PolicePosition), 10f, Facts(fixture, "measured movement"));
                    Assert.Less(fixture.Gap, gap - 8f, Facts(fixture, "closing distance"));
                    Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
                    Assert.AreEqual(100f, fixture.Player.CurrentHealth);
                    Debug.Log("POLICE OWNER REGRESSION travel=" + Vector2.Distance(start, fixture.PolicePosition) +
                        " gapBefore=" + gap + " gapAfter=" + fixture.Gap + " position=" + fixture.PolicePosition);
                }
            }
        }
        finally {
            try { if (snapshot != null) snapshot.AssertSourcesUnchanged(); }
            finally { PoliceNarrowDistrictGeometrySnapshot.ClearPending(); }
        }
    }

    /// <summary>Consumes the operator-captured snapshot and records three bounded authored probes.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrictAuthoredProbe() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        VerifySourceAssetBindings();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance, "The authored probe must not initialize career services.");

        PoliceNarrowDistrictGeometrySnapshot snapshot = null;
        try {
            snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
            Assert.IsNotNull(snapshot, "The operator must capture NarrowDistrict before the TestRunner bootstrap.");
            Assert.IsNotEmpty(snapshot.Token);
            Assert.Greater(snapshot.ColliderCount, 0);
            Debug.Log("NARROWDISTRICT DISCOVERY ONLY token=" + snapshot.Token +
                ", colliders=" + snapshot.ColliderCount);

            RunScenario(snapshot, PursueBehaviorPath, new Vector2(6f, -12.5f), 0f,
                new Vector2(6f, 1f), "pursue-standoff");
            RunScenario(snapshot, RamBehaviorPath, new Vector2(6f, -12.5f), 0f,
                new Vector2(6f, -9.45f), "ram-force-aware-contact");
            RunScenario(snapshot, PursueBehaviorPath, new Vector2(6f, -12.5f), 0f,
                new Vector2(34f, 5f), "pursue-turn-final");
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

    /// <summary>Proves same-step directed evidence survives cancellation before a genuine authored Ram impact.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrictRamAnchorProbe() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        VerifySourceAssetBindings();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);

        PoliceNarrowDistrictGeometrySnapshot snapshot = null;
        anchorDiagnosticsEnabled = true;
        try {
            snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
            Assert.IsNotNull(snapshot, "The operator must capture NarrowDistrict before the TestRunner bootstrap.");
            Assert.IsNotEmpty(snapshot.Token);
            Assert.Greater(snapshot.ColliderCount, 0);
            RunScenario(snapshot, RamBehaviorPath, new Vector2(6f, -12.5f), 0f,
                new Vector2(6f, -9.45f), "ram-force-aware-contact");
        }
        finally {
            anchorDiagnosticsEnabled = false;
            try {
                if (snapshot != null) snapshot.AssertSourcesUnchanged();
            }
            finally {
                PoliceNarrowDistrictGeometrySnapshot.ClearPending();
            }
        }
    }

    /// <summary>Guarantees that a failed probe returns Unity to Edit Mode.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void RunScenario(PoliceNarrowDistrictGeometrySnapshot snapshot, string behaviorPath,
        Vector2 policePosition, float heading, Vector2 targetPosition, string caseId) {
        var authored = LoadAuthoredReferences(behaviorPath);
        try {
            using (var fixture = new PursuitFixture(0.02f, authored: authored)) {
                AssertFixtureClones(authored, fixture, caseId);
                using (snapshot.StageInto(fixture.FixtureScene)) {
                    var navigation = snapshot.ResolveNavigation();
                    Assert.IsNotNull(navigation, Facts(fixture, caseId + " detached navigation"));
                    fixture.ConfigureAuthoredNavigation(navigation, snapshot.Origin, policePosition, heading, targetPosition);
                    if (caseId == "ram-force-aware-contact") {
                        RunRamContact(fixture, caseId);
                    }
                    else {
                        BindTarget(fixture, caseId);
                        AssertFiniteState(fixture, caseId + " after bind");
                        if (caseId == "pursue-standoff") RunPursueStandoff(fixture, caseId);
                        else RunTurnFinal(fixture, caseId);
                    }
                }
            }
        }
        finally {
            snapshot.AssertSourcesUnchanged();
        }
    }

    static PursuitFixture.AuthoredSetup LoadAuthoredReferences(string behaviorPath) {
        var profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>(PoliceDataPath);
        var damage = AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>(DamagePath);
        var vehicle = AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>(PoliceVehiclePath);
        var behavior = AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>(behaviorPath);
        Assert.IsNotNull(profile, PoliceDataPath);
        Assert.IsNotNull(damage, DamagePath);
        Assert.IsNotNull(vehicle, PoliceVehiclePath);
        Assert.IsNotNull(behavior, behaviorPath);
        Assert.AreSame(profile, vehicle.sharedNpc, "Authored police wrapper must reference PoliceStandardData.");
        Assert.IsNotNull(behavior.driving);
        return new PursuitFixture.AuthoredSetup {
            profile = profile,
            damage = damage,
            vehicle = vehicle,
            behavior = behavior
        };
    }

    static void AssertFixtureClones(PursuitFixture.AuthoredSetup authored, PursuitFixture fixture, string caseId) {
        Assert.AreNotSame(authored.profile, fixture.Profile, Facts(fixture, caseId + " profile clone identity"));
        Assert.AreNotSame(authored.vehicle, fixture.Binding.vehicle, Facts(fixture, caseId + " vehicle clone identity"));
        Assert.AreNotSame(authored.behavior, fixture.Binding.behavior, Facts(fixture, caseId + " behavior clone identity"));
        Assert.AreSame(fixture.Profile, fixture.Binding.vehicle.sharedNpc, Facts(fixture, caseId + " wrapper/profile identity"));
        Assert.AreEqual(authored.profile.vehicleProfileId, fixture.Profile.vehicleProfileId, Facts(fixture, caseId + " profile id"));
        Assert.AreEqual(authored.profile.visualCatalogId, fixture.Profile.visualCatalogId, Facts(fixture, caseId + " visual id"));
        Assert.AreEqual(authored.profile.colliderSize, fixture.Profile.colliderSize, Facts(fixture, caseId + " collider shape"));
        Assert.AreEqual(authored.profile.baseMass, fixture.Profile.baseMass, Facts(fixture, caseId + " profile mass"));
        Assert.AreEqual(authored.profile.maxHealth, fixture.Profile.maxHealth, Facts(fixture, caseId + " profile health"));
        Assert.AreEqual(JsonUtility.ToJson(authored.profile), JsonUtility.ToJson(fixture.Profile), Facts(fixture, caseId + " full profile values"));
        Assert.AreEqual(JsonUtility.ToJson(authored.behavior.driving), JsonUtility.ToJson(fixture.ControllerSettings),
            Facts(fixture, caseId + " full driving values"));
        Assert.AreEqual(authored.behavior.behaviorProfileId, fixture.Binding.behavior.behaviorProfileId,
            Facts(fixture, caseId + " behavior id"));
        Assert.AreEqual(authored.behavior.tacticalRole, fixture.Binding.behavior.tacticalRole,
            Facts(fixture, caseId + " tactical role"));
        Assert.AreEqual(authored.vehicle.supportedTacticalRoles.Count, fixture.Binding.vehicle.supportedTacticalRoles.Count,
            Facts(fixture, caseId + " role count"));
        for (int index = 0; index < authored.vehicle.supportedTacticalRoles.Count; index++) {
            Assert.AreEqual(authored.vehicle.supportedTacticalRoles[index], fixture.Binding.vehicle.supportedTacticalRoles[index],
                Facts(fixture, caseId + " role " + index));
        }
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
        var document = navigation.ResolveDocument();
        Assert.IsNotNull(document);
        Assert.AreEqual(map.mapId, document.mapId);
        Assert.IsTrue(damage.TryResolve("nd-civilian", out _), "Authored damage must retain nd-civilian.");
    }

    static void RunPursueStandoff(PursuitFixture fixture, string caseId) {
        float maximumImpact = 0f;
        float maximumAngularVelocity = 0f;
        int callbacks = 0;
        fixture.PoliceReceiver.ImpactObserved += impact => { callbacks++; maximumImpact = Mathf.Max(maximumImpact, impact); };
        int steps = Mathf.CeilToInt(10f / fixture.StepDelta);
        float maximumProgress = fixture.Controller.CursorProgress;
        for (int step = 0; step < steps; step++) {
            fixture.Step(1);
            maximumProgress = Mathf.Max(maximumProgress, fixture.Controller.CursorProgress);
            maximumAngularVelocity = Mathf.Max(maximumAngularVelocity, Mathf.Abs(fixture.Binding.body.Body.angularVelocity));
            AssertFiniteState(fixture, caseId);
        }
        WriteObservation(fixture, caseId + ", callbacks=" + callbacks + ", maxImpact=" + maximumImpact +
            ", maxAngularVelocity=" + maximumAngularVelocity + ", maxProgress=" + maximumProgress);
    }

    static void RunRamContact(PursuitFixture fixture, string caseId) {
        RamAnchorTrace trace = anchorDiagnosticsEnabled ? new RamAnchorTrace() : null;
        trace?.Attach(fixture);
        BindTarget(fixture, caseId);
        AssertFiniteState(fixture, caseId + " after bind");
        float impact = 0f;
        int callbacks = 0;
        float initialPoliceHealth = fixture.PoliceReceiver.CurrentHealth, initialPlayerHealth = fixture.Player.CurrentHealth;
        bool evidenceAtFirstImpact = false;
        RoadPathQuery.EdgeAnchor firstImpactFloor = default;
        fixture.PoliceReceiver.ImpactObserved += value => {
            callbacks++;
            impact = Mathf.Max(impact, value);
            if (anchorDiagnosticsEnabled && callbacks == 1) {
                evidenceAtFirstImpact = RecoveryFlag(fixture, "hasEvidence");
                firstImpactFloor = RecoveryAnchor(fixture);
            }
            if (anchorDiagnosticsEnabled) Debug.Log("NARROWDISTRICT RAM AFTER_HANDLER " + trace.State(fixture) +
                ", recoveryEvidence=" + RecoveryFlag(fixture, "hasEvidence") +
                ", recoveryGate=" + RecoveryFlag(fixture, "hasGate") + ", floor=" + RecoveryFloor(fixture));
        };
        float targetGap = fixture.ColliderGap;
        float acceleration = Mathf.Min(fixture.Profile.motorSettings.acceleration,
            fixture.Profile.motorSettings.maxEngineForce / fixture.Binding.body.Body.mass);
        float braking = Mathf.Min(fixture.Profile.motorSettings.brakeDeceleration,
            fixture.Profile.motorSettings.maxBrakeForce / fixture.Binding.body.Body.mass) * fixture.ControllerSettings.comfort *
            Mathf.Min(1f, fixture.Binding.body.Motor.crashModeForceMultiplier);
        bool reserveFinite = PoliceRamDecision.TryReserve(fixture.PoliceVelocity.magnitude, acceleration, braking,
            fixture.ControllerSettings.ramMaximumActiveSeconds, fixture.Profile.motorSettings.reactionTime, fixture.StepDelta,
            fixture.ControllerSettings.stopGap, targetGap, fixture.ControllerSettings.maxSweepDistance,
            out float reserve, out float peakSpeed);
        Debug.Log((anchorDiagnosticsEnabled ? "NARROWDISTRICT RAM RESERVE case=" : "NARROWDISTRICT DISCOVERY ONLY case=") + caseId + ", actualColliderGap=" + targetGap +
            ", reserveFinite=" + reserveFinite + ", reserve=" + reserve + ", peakSpeed=" + peakSpeed);

        int steps = Mathf.CeilToInt(12f / fixture.StepDelta);
        float maximumAngularVelocity = 0f;
        bool sawReverse = false, sawNegativeVelocity = false, sawEvidence = false, sawGate = false, sawResume = false;
        float maximumReverseTravel = 0f;
        Vector2 retainedGate = Vector2.zero;
        for (int step = 0; step < steps; step++) {
            trace?.BeforeStep(fixture, step);
            fixture.Step(1);
            trace?.AfterStep(fixture, step);
            maximumAngularVelocity = Mathf.Max(maximumAngularVelocity, Mathf.Abs(fixture.Binding.body.Body.angularVelocity));
            sawReverse |= fixture.MotorReversePermitted && fixture.Controller.LastCommand.reverseAllowed;
            sawNegativeVelocity |= fixture.ForwardSpeed < 0f;
            sawEvidence |= RecoveryFlag(fixture, "hasEvidence");
            if (RecoveryFlag(fixture, "hasGate")) { sawGate = true; retainedGate = fixture.Recovery.Gate; }
            sawResume |= fixture.Recovery.ResumeReady;
            maximumReverseTravel = Mathf.Max(maximumReverseTravel, fixture.Recovery.ReverseTravel);
            AssertFiniteState(fixture, caseId);
        }
        if (anchorDiagnosticsEnabled) {
            Assert.Greater(callbacks, 0, "The road-evidence acceptance requires a genuine native impact callback.");
            Assert.Greater(impact, 0f);
            Assert.IsTrue(trace.FirstImpactHadUnboundCursor, "The native case must exercise fallback after ordinary route cancellation.");
            Assert.IsTrue(evidenceAtFirstImpact, "The first real callback lost the measured current-step road floor.");
            var geometry = fixture.Binding.graph.GetGeometry("nd-pilot-e0");
            Assert.AreEqual(2, geometry.Points.Count, "This oracle requires the authored straight pilot edge.");
            Vector2 localMeasuredPosition = trace.FirstImpactCommandPosition - fixture.Binding.mapOriginWorld;
            float expectedArc = Vector2.Dot(localMeasuredPosition - geometry.Points[0], (geometry.Points[1] - geometry.Points[0]).normalized);
            Assert.AreEqual("nd-pilot-e0", firstImpactFloor.edgeId);
            float rounding = 4f / 8388608f * Mathf.Max(Mathf.Abs(expectedArc), Mathf.Abs(firstImpactFloor.distanceAlongEdge));
            Assert.That(firstImpactFloor.distanceAlongEdge, Is.EqualTo(expectedArc).Within(rounding),
                "The callback must keep this Tick's measured floor, not the preceding Tick's anchor.");
            Assert.Less(fixture.PoliceReceiver.CurrentHealth, initialPoliceHealth);
            Assert.Less(fixture.Player.CurrentHealth, initialPlayerHealth);
        }
        WriteObservation(fixture, caseId + ", callbacks=" + callbacks + ", impact=" + impact +
            ", ramStarts=" + fixture.Controller.RamStarts + ", ramming=" + fixture.Controller.IsRamming +
            ", maxAngularVelocity=" + maximumAngularVelocity + ", evidence=" + sawEvidence + ", gate=" + sawGate +
            ", gatePosition=" + retainedGate + ", reverse=" + sawReverse + ", negativeVelocity=" + sawNegativeVelocity +
            ", reverseTravel=" + maximumReverseTravel + ", resumed=" + sawResume);
        trace?.Flush(fixture);
    }

    static void RunTurnFinal(PursuitFixture fixture, string caseId) {
        bool sawCommitted = false;
        bool sawCommitExit = false;
        bool previousCommitted = fixture.Controller.IsTurnCommitted;
        bool sawFinal = false;
        bool sawHolding = false;
        bool sawOutgoingEdge = false;
        float maximumProgress = fixture.Controller.CursorProgress;
        string lastEdge = fixture.Cursor.IsBound ? fixture.Cursor.CurrentAnchor.edgeId : "<unbound>";
        int steps = Mathf.CeilToInt(30f / fixture.StepDelta);
        for (int step = 0; step < steps; step++) {
            fixture.Step(1);
            bool committed = fixture.Controller.IsTurnCommitted;
            sawCommitted |= committed;
            sawCommitExit |= previousCommitted && !committed;
            previousCommitted = committed;
            sawFinal |= fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.FinalConnector;
            sawHolding |= fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.Holding;
            maximumProgress = Mathf.Max(maximumProgress, fixture.Controller.CursorProgress);
            if (fixture.Cursor.IsBound) {
                lastEdge = fixture.Cursor.CurrentAnchor.edgeId;
                sawOutgoingEdge |= lastEdge == "nd-pilot-e1";
            }
            AssertFiniteState(fixture, caseId);
        }
        WriteObservation(fixture, caseId + ", committed=" + sawCommitted + ", commitExit=" + sawCommitExit +
            ", outgoingEdgeE1=" + sawOutgoingEdge + ", final=" + sawFinal + ", holding=" + sawHolding + ", maxProgress=" + maximumProgress +
            ", lastEdge=" + lastEdge + ", finalPosition=" + fixture.PolicePosition +
            ", targetPosition=" + fixture.PlayerPosition);
    }

    static void BindTarget(PursuitFixture fixture, string caseId) {
        Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), caseId + ": " + reason);
        Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), caseId + ": " + reason);
    }

    static bool RecoveryFlag(PursuitFixture fixture, string field) {
        return (bool)typeof(PoliceRecoveryIntegration).GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(fixture.Recovery);
    }

    static string RecoveryFloor(PursuitFixture fixture) {
        var floor = RecoveryAnchor(fixture);
        return floor.edgeId + "@" + floor.distanceAlongEdge.ToString("R", CultureInfo.InvariantCulture);
    }

    static RoadPathQuery.EdgeAnchor RecoveryAnchor(PursuitFixture fixture) {
        var field = typeof(PoliceRecoveryIntegration).GetField("floor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        return (RoadPathQuery.EdgeAnchor)field.GetValue(fixture.Recovery);
    }

    sealed class RamAnchorTrace {
        readonly List<string> records = new List<string>();
        readonly List<string> recent = new List<string>();
        readonly List<string> impactHistory = new List<string>();
        PursuitFixture fixture;
        string previousState;
        int currentStep = -1;
        int impactStep = -1;
        bool impactSeen;
        internal bool FirstImpactHadUnboundCursor { get; private set; }
        internal Vector2 FirstImpactCommandPosition { get; private set; }

        internal void Attach(PursuitFixture value) {
            fixture = value;
            value.PoliceReceiver.ImpactObserved += OnFirstImpact;
        }

        internal void OnFirstImpact(float impact) {
            if (impactSeen) return;
            impactSeen = true;
            impactStep = currentStep;
            FirstImpactHadUnboundCursor = !fixture.Cursor.IsBound;
            FirstImpactCommandPosition = fixture.LastControllerPosition;
            impactHistory.Add("EARLY_IMPACT impact=" + F(impact) + ", " + State());
            for (int index = 0; index < recent.Count; index++) impactHistory.Add(recent[index]);
        }

        internal void BeforeStep(PursuitFixture value, int step) {
            currentStep = step;
            string before = "PRE step=" + step + ", " + State();
            AddRecent(before);
            if (step < 4) AddRecord(before);
        }

        internal void AfterStep(PursuitFixture value, int step) {
            currentStep = step;
            string state = State();
            string key = fixture.Controller.CurrentPhase + "/" + fixture.Cursor.IsBound + "/" +
                fixture.Controller.IsRamming + "/" + RamCertified() + "/" + fixture.Controller.RecoveryPhase;
            if (step < 4 || key != previousState) AddRecord("POST step=" + step + ", " + state);
            if (impactSeen && step == impactStep && impactHistory.Count < 8) impactHistory.Add("POST step=" + step + ", " + state);
            previousState = key;
        }

        internal string State(PursuitFixture value = null) {
            if (value != null) fixture = value;
            return "phase=" + fixture.Controller.CurrentPhase + ", cursor=" + Cursor() +
                ", ram=" + fixture.Controller.IsRamming + ", remaining=" + F(RamRemaining()) +
                ", certified=" + RamCertified() + ", clock=" + F(fixture.World.SessionTime) +
                ", body=" + V(fixture.PolicePosition) + ", rot=" + F(fixture.Binding.body.Body.rotation) +
                ", ang=" + F(fixture.Binding.body.Body.angularVelocity) + ", policeV=" + V(fixture.PoliceVelocity) +
                ", playerV=" + V(fixture.PlayerVelocity) + ", colliderGap=" + F(fixture.ColliderGap) +
                ", planner=" + fixture.Controller.PlannerAttemptCount + ", plan=" + PlanState() + ", cmd=" + F(fixture.Controller.LastCommand.throttle) + "/" +
                F(fixture.Controller.LastCommand.brake) + ", profile=" + fixture.Profile.vehicleProfileId +
                ", intent=" + fixture.Binding.behavior.tacticalRole;
        }

        internal void Flush(PursuitFixture value) {
            if (!impactSeen) AddRecord("NO_EARLY_IMPACT, " + State(value));
            for (int index = 0; index < records.Count; index++) Debug.Log("NARROWDISTRICT RAM TRACE " + records[index]);
            int historyCount = Mathf.Min(8, impactHistory.Count);
            for (int index = 0; index < historyCount; index++) Debug.Log("NARROWDISTRICT RAM IMPACT_HISTORY " + impactHistory[index]);
        }

        float RamRemaining() {
            var decision = (PoliceRamDecision)typeof(PolicePursuitController).GetField("ramDecision",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(fixture.Controller);
            return decision.RemainingActiveSeconds;
        }

        string PlanState() {
            var result = fixture.Planner != null ? fixture.Planner.CurrentResult : null;
            return result != null ? result.status + "/" + result.waitReason : "<none>";
        }

        bool RamCertified() {
            var safety = (PoliceRamSafety)typeof(PolicePursuitController).GetField("ramSafety",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(fixture.Controller);
            return safety != null && safety.IsCertified;
        }

        string Cursor() {
            if (!fixture.Cursor.IsBound) return "<unbound>";
            var anchor = fixture.Cursor.CurrentAnchor;
            return anchor.edgeId + "@" + F(anchor.distanceAlongEdge);
        }

        void AddRecord(string value) {
            if (records.Count < 14) records.Add(value);
        }

        void AddRecent(string value) {
            recent.Add(value);
            if (recent.Count > 6) recent.RemoveAt(0);
        }

        static string V(Vector2 value) => "(" + F(value.x) + "," + F(value.y) + ")";
        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }

    static void AssertFiniteState(PursuitFixture fixture, string caseId) {
        AssertFinite(fixture.PolicePosition.x, Facts(fixture, caseId + " police x"));
        AssertFinite(fixture.PolicePosition.y, Facts(fixture, caseId + " police y"));
        AssertFinite(fixture.PoliceVelocity.x, Facts(fixture, caseId + " police velocity x"));
        AssertFinite(fixture.PoliceVelocity.y, Facts(fixture, caseId + " police velocity y"));
        AssertFinite(fixture.Binding.body.Body.rotation, Facts(fixture, caseId + " rotation"));
        AssertFinite(fixture.Binding.body.Body.angularVelocity, Facts(fixture, caseId + " angular velocity"));
        AssertFinite(fixture.World.SessionTime, Facts(fixture, caseId + " clock"));
    }

    static void AssertFinite(float value, string message) {
        Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value), message);
    }

    static void WriteObservation(PursuitFixture fixture, string detail) {
        Debug.Log("NARROWDISTRICT DISCOVERY ONLY " + detail + ", phase=" + fixture.Controller.CurrentPhase +
            ", planner=" + fixture.Planner.CurrentResult.status + "/" + fixture.Planner.CurrentResult.waitReason +
            ", recovery=" + fixture.Controller.RecoveryPhase + ", bound=" + fixture.Controller.IsBound +
            ", reverseGate=" + fixture.MotorReversePermitted + ", commandBrake=" + fixture.Controller.LastCommand.brake +
            ", commandReverse=" + fixture.Controller.LastCommand.reverseAllowed + ", clock=" + fixture.World.SessionTime +
            ", cursor=" + fixture.Controller.CursorProgress + ", anchor=" +
            (fixture.Cursor.IsBound ? fixture.Cursor.CurrentAnchor.edgeId : "<unbound>") +
            ", rotation=" + fixture.Binding.body.Body.rotation + ", angularVelocity=" + fixture.Binding.body.Body.angularVelocity +
            ", policePosition=" + fixture.PolicePosition + ", playerPosition=" + fixture.PlayerPosition +
            ", policeHealth=" + fixture.PoliceReceiver.CurrentHealth + ", playerHealth=" + fixture.Player.CurrentHealth);
    }

    static string Facts(PursuitFixture fixture, string detail) {
        return "case=" + detail + ", phase=" + fixture.Controller.CurrentPhase + ", recovery=" + fixture.Controller.RecoveryPhase +
            ", bound=" + fixture.Controller.IsBound + ", clock=" + fixture.World.SessionTime +
            ", policePosition=" + fixture.PolicePosition + ", targetPosition=" + fixture.PlayerPosition;
    }
}
