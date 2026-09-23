#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;

[Serializable]
sealed class S12BenchmarkCaseReport {
    // Serializable output fields, never runtime configuration or exposed gameplay state.
    public string label, sessionId, scene, mapId, vehicleId, difficultyId, navigationId;
    public string navigationHash, manifestHash, populationFingerprint, modifierFingerprint, policeFingerprint, ruleset;
    public string npcProfilesJson, populationProfileId, policeProfileId;
    public int durationMinutes, seed, width, height, quality, frameCap, vSync;
    public bool freeplay, trafficEnabled, policeEnabled, explicitAbandoned, focusedAtStart;
    public string[] modifiers;
    public string window = "Not completed", cleanup = "Not completed", recorder = "Not Measured", recorderPath;
    public double wallSeconds;
    public float activeSeconds, lastDamageClock, secondsAtCivilianCap;
    public Vector2 initialPosition, finalPosition;
    public float initialHeading, finalHeading, finalSpeed, maxSpeed;
    public Vector3 cameraPosition;
    public Quaternion cameraRotation;
    public float cameraSize, cameraFov;
    public bool cameraOrthographic, cameraObserved;
    public int observedFrames, focusedFrames, civilianPeak, policePeak, sampledBudgetViolations, sampledEmptyViolations;
    public int civilianCap, policeCap, movingCap, wreckCap, physicsCap, poolCapacity, poolAfterClose;
    public int settlementSaves, garageObjects;
    public long managedBytesAfterCleanup;
    [NonSerialized] bool expectEmpty;

    internal static S12BenchmarkCaseReport Begin(S12BenchmarkCase plan, TrafficSessionHost host,
        Rigidbody2D body, ScoreHandler score) {
        var snapshot = host.Coordinator.Context.Snapshot;
        var evidence = snapshot.ContentEvidence;
        var budget = host.Services.Budget;
        var value = new S12BenchmarkCaseReport {
            label = plan.Label, sessionId = snapshot.sessionId, scene = host.gameObject.scene.name,
            mapId = snapshot.mapId, vehicleId = snapshot.vehicleId, difficultyId = snapshot.difficultyId,
            navigationId = snapshot.navigationDocumentId, navigationHash = evidence.navigationCanonicalHash,
            manifestHash = evidence.actualManifestHash, populationFingerprint = evidence.populationCanonicalFingerprint,
            modifierFingerprint = evidence.modifierCanonicalFingerprint, policeFingerprint = evidence.policeHeatTierFingerprint,
            ruleset = evidence.rulesetVersion, populationProfileId = evidence.populationProfileId,
            policeProfileId = evidence.policeDirectorProfileId, npcProfilesJson = S12BenchmarkProvenance.Npcs(evidence),
            durationMinutes = score.ShiftDurationMinutes, seed = snapshot.seed, freeplay = score.IsFreeplay,
            trafficEnabled = snapshot.trafficEnabled, policeEnabled = snapshot.policeEnabled,
            modifiers = new string[snapshot.resolvedModifierIds.Count], width = Screen.width, height = Screen.height,
            quality = QualitySettings.GetQualityLevel(), frameCap = Application.targetFrameRate, vSync = QualitySettings.vSyncCount,
            focusedAtStart = Application.isFocused, initialPosition = body.position, initialHeading = body.rotation,
            civilianCap = budget.maxCivilianMoving, policeCap = budget.maxPoliceMoving, movingCap = budget.maxTotalMoving,
            wreckCap = budget.maxWreckSlots, physicsCap = budget.maxTotalPhysicsObjects,
            poolCapacity = host.Services.Pool.Capacity, expectEmpty = plan.Empty
        };
        for (int i = 0; i < value.modifiers.Length; i++) value.modifiers[i] = snapshot.resolvedModifierIds[i];
        Camera camera = Camera.main;
        if (camera != null && camera.gameObject.scene == host.gameObject.scene) {
            value.cameraObserved = true; value.cameraPosition = camera.transform.position;
            value.cameraRotation = camera.transform.rotation; value.cameraSize = camera.orthographicSize;
            value.cameraFov = camera.fieldOfView; value.cameraOrthographic = camera.orthographic;
        }
        value.lastDamageClock = host.GetComponent<TrafficDamageWorld>().SessionTime;
        return value;
    }

    internal void Observe(TrafficSessionHost host, Rigidbody2D body, TrafficDamageWorld damage) {
        observedFrames++;
        if (Application.isFocused) focusedFrames++;
        finalPosition = body.position; finalHeading = body.rotation; finalSpeed = body.linearVelocity.magnitude;
        maxSpeed = Mathf.Max(maxSpeed, finalSpeed);
        float delta = Mathf.Max(0, damage.SessionTime - lastDamageClock);
        activeSeconds += delta; lastDamageClock = damage.SessionTime;
        var p = host.Services.Population;
        int civilian = p.ActiveCivilianCount + p.PendingCivilianCount;
        int police = p.ActivePoliceCount + p.PendingPoliceCount;
        civilianPeak = Mathf.Max(civilianPeak, p.ActiveCivilianCount);
        policePeak = Mathf.Max(policePeak, p.ActivePoliceCount);
        if (civilianCap == 10 && p.ActiveCivilianCount == civilianCap) secondsAtCivilianCap += delta;
        if (expectEmpty && (civilian + police + p.OccupiedWreckCount + p.ReservedFutureWreckCount != 0)) sampledEmptyViolations++;
        if (civilian > civilianCap || police > policeCap || civilian + police > movingCap ||
            p.OccupiedWreckCount + p.ReservedFutureWreckCount > wreckCap ||
            civilian + police + p.OccupiedWreckCount > physicsCap) sampledBudgetViolations++;
    }

    internal static bool PopulationIsZero(VehiclePopulationService p) {
        return p != null && p.ActiveCivilianCount == 0 && p.ActivePoliceCount == 0 &&
            p.PendingCivilianCount == 0 && p.PendingPoliceCount == 0 &&
            p.OccupiedWreckCount == 0 && p.ReservedFutureWreckCount == 0;
    }

    internal bool ComparableTo(S12BenchmarkCaseReport other) {
        return other != null && window.StartsWith("Complete", StringComparison.Ordinal) &&
            other.window.StartsWith("Complete", StringComparison.Ordinal) &&
            sampledBudgetViolations == 0 && other.sampledBudgetViolations == 0 &&
            mapId == other.mapId && vehicleId == other.vehicleId && difficultyId == other.difficultyId &&
            durationMinutes == other.durationMinutes && seed == other.seed && freeplay == other.freeplay &&
            navigationHash == other.navigationHash && populationFingerprint == other.populationFingerprint &&
            modifierFingerprint == other.modifierFingerprint && policeFingerprint == other.policeFingerprint &&
            npcProfilesJson == other.npcProfilesJson && width == other.width && height == other.height &&
            quality == other.quality && frameCap == other.frameCap && vSync == other.vSync &&
            initialPosition.Equals(other.initialPosition) && initialHeading.Equals(other.initialHeading) &&
            cameraObserved && other.cameraObserved && cameraPosition.Equals(other.cameraPosition) &&
            cameraRotation.Equals(other.cameraRotation) && cameraSize.Equals(other.cameraSize) &&
            cameraFov.Equals(other.cameraFov) && cameraOrthographic == other.cameraOrthographic &&
            focusedFrames == 0 && other.focusedFrames == 0 && !focusedAtStart && !other.focusedAtStart;
    }
}
#endif
