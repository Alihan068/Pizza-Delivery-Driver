#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Owns a bounded standalone-only real scene lifecycle; never drives or relocates a body.</summary>
[DefaultExecutionOrder(-1200)]
public sealed partial class S12BenchmarkRunner : MonoBehaviour {
    enum Stage { Boot, Garage, Ready, Measuring, Closing, Unloading, Finished }
    Stage stage;
    double started, stageStarted, measuredStarted;
    int index, successfulSaves, failedSaves, savesBefore, failedBefore, shiftsBefore, oldSceneHandle;
    GameManager manager;
    MapData map;
    string difficultyId, vehicleId;
    TrafficSessionHost host;
    TrafficSessionServices services;
    TrafficDamageWorld damage;
    ScoreHandler score;
    Rigidbody2D body;
    S12BenchmarkCase plan;
    S12BenchmarkCaseReport current;
    readonly S12BenchmarkReport report = new S12BenchmarkReport();
    readonly HashSet<string> sessions = new HashSet<string>();
    readonly S12BenchmarkCapture capture = new S12BenchmarkCapture();

    void Awake() {
        if (Application.isEditor || !S12BenchmarkGate.Requested || S12BenchmarkGate.BlockCareerStartup) {
            enabled = false; return;
        }
        started = stageStarted = Time.realtimeSinceStartupAsDouble;
        SceneManager.sceneLoaded += SuppressSceneInput;
        GameManager.GameSaved += Saved;
    }

    void Saved(bool success) { if (success) successfulSaves++; else failedSaves++; }
    void Enter(Stage next) { stage = next; stageStarted = Time.realtimeSinceStartupAsDouble; }

    void Update() {
        if (stage == Stage.Finished) return;
        try {
            double now = Time.realtimeSinceStartupAsDouble;
            Require(!S12BenchmarkPlan.Expired(started, now, S12BenchmarkPlan.TotalTimeout), "Total wall deadline.");
            switch (stage) {
                case Stage.Boot:
                    ReadyDeadline(now);
                    if (GameManager.Instance != null) InitializeSelection();
                    break;
                case Stage.Garage:
                    ReadyDeadline(now);
                    if (SceneManager.GetActiveScene().name == manager.Config.garageScene) LoadCase();
                    break;
                case Stage.Ready:
                    ReadyDeadline(now);
                    TryStartMeasurement(now);
                    break;
                case Stage.Measuring: Measure(now); break;
                case Stage.Closing: CloseCase(now); break;
                case Stage.Unloading: FinishUnload(now); break;
            }
        } catch (Exception exception) { Finish("Failed: " + exception.Message); }
    }

    void ReadyDeadline(double now) {
        Require(!S12BenchmarkPlan.Expired(stageStarted, now, S12BenchmarkPlan.ReadyTimeout), "Load/readiness deadline: " + stage);
    }

    void TryStartMeasurement(double now) {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != map.sceneName) return;
        if (host == null) host = SessionSceneRules.ResolveHost(scene);
        if (score == null) score = SessionSceneRules.ResolveComponent<ScoreHandler>(scene);
        if (host == null || !host.IsActive || host.Player == null || score == null || !score.IsGameActive) return;
        services = host.Services;
        damage = host.GetComponent<TrafficDamageWorld>();
        body = host.Player.GetComponent<Rigidbody2D>();
        Require(services != null && services.Population != null && services.Budget != null && body != null &&
            damage != null && damage.IsActive, "Missing measurement/lifecycle dependencies.");
        ValidateSnapshot();
        current = S12BenchmarkCaseReport.Begin(plan, host, body, score);
        report.cases.Add(current);
        savesBefore = successfulSaves; failedBefore = failedSaves; shiftsBefore = manager.totalShiftsSettled;
        oldSceneHandle = scene.handle;
        capture.Begin(S12BenchmarkGate.Profile, host, plan, current);
        measuredStarted = now;
        Enter(Stage.Measuring);
    }

    void Measure(double now) {
        Require(host != null && score != null && body != null, "Gameplay objects disappeared before teardown.");
        current.wallSeconds = now - measuredStarted;
        current.Observe(host, body, damage);
        if (!score.IsGameActive || !host.IsActive) {
            current.window = "Partial: natural ending before explicit boundary; actual reason not observed";
            BeginClose(false); return;
        }
        Require(Time.timeScale == 1f && score.IsFreeplay == plan.Freeplay, "Mode/timeScale changed during measurement.");
        Require(Screen.width == S12BenchmarkPlan.Width && Screen.height == S12BenchmarkPlan.Height &&
            Screen.fullScreenMode == FullScreenMode.Windowed, "Launch resolution/window mode changed.");
        Require(Application.runInBackground && QualitySettings.vSyncCount == 0 &&
            Application.targetFrameRate == S12BenchmarkPlan.FrameCap, "Process measurement conditions changed.");
        capture.Sample();
        if (current.wallSeconds < plan.Seconds) return;
        current.window = "Complete realtime window; not natural TimeUp";
        BeginClose(true);
    }

    void BeginClose(bool explicitEnd) {
        capture.End(current);
        current.explicitAbandoned = explicitEnd;
        if (explicitEnd) score.EndLevel(EndReason.Abandoned, manager.Config.garageScene);
        Enter(Stage.Closing);
    }

    void CloseCase(double now) {
        Require(!S12BenchmarkPlan.Expired(stageStarted, now, S12BenchmarkPlan.CleanupTimeout), "Shutdown deadline.");
        if (host.IsActive || score.IsGameActive || !services.IsClosed || damage.PendingBlasts != 0) return;
        Require(successfulSaves - savesBefore == 1 && failedSaves == failedBefore, "Settlement must save successfully exactly once.");
        Require(manager.totalShiftsSettled - shiftsBefore == (plan.Freeplay ? 0 : 1), "Unexpected settlement count.");
        Require(S12BenchmarkCaseReport.PopulationIsZero(services.Population), "Population remains after role cleanup.");
        current.settlementSaves = successfulSaves - savesBefore;
        current.poolAfterClose = services.Pool != null ? services.Pool.PooledCount : -1;
        Time.timeScale = 1f;
        SceneManager.LoadScene(manager.Config.garageScene);
        Enter(Stage.Unloading);
    }

    void FinishUnload(double now) {
        Require(!S12BenchmarkPlan.Expired(stageStarted, now, S12BenchmarkPlan.CleanupTimeout), "Scene unload deadline.");
        bool oldSceneStillLoaded = false;
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++) {
            if (SceneManager.GetSceneAt(sceneIndex).handle == oldSceneHandle) {
                oldSceneStillLoaded = SceneManager.GetSceneAt(sceneIndex).isLoaded;
                break;
            }
        }
        if (SceneManager.GetActiveScene().name != manager.Config.garageScene || oldSceneStillLoaded) return;
        Require(manager.ActiveSession == null && services.IsClosed &&
            S12BenchmarkCaseReport.PopulationIsZero(services.Population), "Old session survived scene unload.");
        Require(successfulSaves - savesBefore == 1 && failedSaves == failedBefore, "Duplicate/failed save during teardown.");
        current.cleanup = "Passed: settled once, roles closed, old scene unloaded, context released";
        current.garageObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length;
        current.managedBytesAfterCleanup = GC.GetTotalMemory(false);
        report.Write(S12BenchmarkGate.Profile, "case-" + index);
        host = null; services = null; damage = null; score = null; body = null; current = null;
        index++;
        if (index == S12BenchmarkPlan.CaseCount) Finish("Plan executed; inspect individual measurement/lifecycle outcomes");
        else Enter(Stage.Garage);
    }

    void Finish(string outcome) {
        if (stage == Stage.Finished) return;
        stage = Stage.Finished;
        try {
            capture.End(current);
            report.outcome = outcome;
            report.totalWallSeconds = Time.realtimeSinceStartupAsDouble - started;
            report.Write(S12BenchmarkGate.Profile, "final");
        } catch (Exception exception) { Debug.LogError("S12 output failure: " + exception.Message); outcome = "Failed output"; }
        Detach();
        Debug.Log("S12: " + outcome);
        Application.Quit(outcome.StartsWith("Failed", StringComparison.Ordinal) ? 2 : 0);
    }

    void Detach() {
        SceneManager.sceneLoaded -= SuppressSceneInput;
        GameManager.GameSaved -= Saved;
        capture.Dispose();
    }
    void OnDestroy() { Detach(); }
    void OnApplicationQuit() {
        if (stage != Stage.Finished) {
            try {
                capture.End(current);
                report.outcome = "Partial: process quit before plan completion";
                report.Write(S12BenchmarkGate.Profile, "interrupted");
            } catch (Exception exception) { Debug.LogError(exception.Message); }
        }
        Detach();
    }
    static void Require(bool valid, string reason) { if (!valid) throw new InvalidOperationException(reason); }
}
#endif
