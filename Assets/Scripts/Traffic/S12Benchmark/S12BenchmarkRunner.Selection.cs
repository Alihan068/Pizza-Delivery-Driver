#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed partial class S12BenchmarkRunner {
    void InitializeSelection() {
        manager = GameManager.Instance;
        Require(manager.Saves != null && manager.ActiveSlot == 0 && manager.Config != null &&
            DevelopmentTestProfile.IsContainedPath(S12BenchmarkGate.Profile.RootPath, manager.Saves.GetPath(0)),
            "GameManager did not bind the isolated slot.");
        Require(SceneManager.GetActiveScene().name == manager.Config.mainMenuScene && manager.ActiveSession == null,
            "Build must boot into configured main menu, never directly into gameplay.");
        map = manager.Content.GetMap(S12BenchmarkPlan.MapId);
        Require(map != null && map.sceneName != "GameScene" && !string.IsNullOrWhiteSpace(map.sceneName) &&
            map.sceneName != manager.Config.garageScene &&
            Application.CanStreamedLevelBeLoaded(map.sceneName) &&
            Application.CanStreamedLevelBeLoaded(manager.Config.garageScene), "Approved map/garage absent from build.");
        if (!manager.IsMapOwned(map)) {
            manager.DeveloperUnlockAllMapsAndDifficulties();
            report.isolatedDevelopmentUnlock = true;
        }
        manager.SelectMap(map);
        Require(manager.currentMap == map && manager.CurrentDifficulty != null && manager.currentVehicle != null,
            "Authored map/difficulty/default vehicle selection failed.");
        difficultyId = manager.CurrentDifficulty.difficultyId;
        vehicleId = manager.currentVehicle.vehicleId;
        report.vehicleId = vehicleId;
        report.difficultyId = difficultyId;
        report.profileGuid = S12BenchmarkGate.Profile.GuidText;
        report.unityVersion = Application.unityVersion;
        report.buildGuid = Application.buildGUID;
        report.device = SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName;
        Require(Screen.width == S12BenchmarkPlan.Width && Screen.height == S12BenchmarkPlan.Height &&
            Screen.fullScreenMode == FullScreenMode.Windowed, "Use documented windowed launch arguments.");
        Time.timeScale = 1f;
        SceneManager.LoadScene(manager.Config.garageScene);
        Enter(Stage.Garage);
    }

    void LoadCase() {
        plan = S12BenchmarkPlan.GetCase(index);
        Require(manager.ActiveSession == null && manager.currentVehicle != null && manager.currentVehicle.vehicleId == vehicleId,
            "Prior context leaked or selected vehicle changed.");
        manager.SelectMap(map);
        Require(manager.SelectDifficulty(map, difficultyId), "Authored difficulty was rejected.");
        string[] modifiers = plan.Empty ? new[] { S12BenchmarkPlan.PeacefulId, S12BenchmarkPlan.NoTrafficId } :
            plan.Peaceful ? new[] { S12BenchmarkPlan.PeacefulId } : Array.Empty<string>();
        var resolution = manager.SelectModifiers(modifiers);
        Require(resolution.rejectedIds.Count == 0 && resolution.resolvedModifiers.Count == modifiers.Length,
            "Modifier selection was rejected.");
        Require(manager.SelectShiftDuration(plan.Minutes) && manager.SelectedShiftDurationMinutes == plan.Minutes,
            "Selected duration readback failed.");
        Require(!manager.IsFreeplayMode, "Previous settlement did not release FREEPLAY mode.");
        Time.timeScale = 1f;
        if (plan.Freeplay) manager.StartFreeplay();
        else SceneManager.LoadScene(map.sceneName);
        Enter(Stage.Ready);
    }

    void ValidateSnapshot() {
        var context = host.Coordinator.Context;
        var snapshot = context.Snapshot;
        Require(snapshot != null && snapshot.ContentEvidence != null &&
            snapshot.mapId == map.mapId && snapshot.vehicleId == vehicleId && snapshot.difficultyId == difficultyId &&
            snapshot.shiftDurationMinutes == plan.Minutes && score.ShiftDurationMinutes == plan.Minutes &&
            snapshot.isFreeplay == plan.Freeplay && score.IsFreeplay == plan.Freeplay && snapshot.seed == 0 &&
            snapshot.policeEnabled == !plan.Peaceful && snapshot.trafficEnabled == !plan.Empty &&
            context.Integrity.InvalidationReasons.Contains("S12 stationary benchmark; not a competitive career run."),
            "Frozen rules or benchmark ineligibility mismatch.");
        Require(sessions.Add(context.SessionId), "Session identity reused.");
        Require(snapshot.resolvedModifierIds.Count == (plan.Empty ? 2 : plan.Peaceful ? 1 : 0), "Unexpected frozen modifiers.");
    }

    static void SuppressSceneInput(Scene scene, LoadSceneMode mode) {
        if (!S12BenchmarkGate.Requested) return;
        // Scene-loaded runs before Start; all UI modules (including inactive panels) are disabled.
        foreach (var root in scene.GetRootGameObjects()) {
            foreach (var input in root.GetComponentsInChildren<BaseInputModule>(true)) input.enabled = false;
            foreach (var input in root.GetComponentsInChildren<PlayerInput>(true)) input.enabled = false;
            foreach (var input in root.GetComponentsInChildren<VehicleInput>(true)) input.ClearInput();
        }
        // Players instantiated later are protected by VehicleInput.Snapshot, not this discovery pass.
    }
}
#endif
