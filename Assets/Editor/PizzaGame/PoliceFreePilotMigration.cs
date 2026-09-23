using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Editor-only, unsaved NarrowDistrict migration and readback contract for the free police pilot.</summary>
public static class PoliceFreePilotMigration {
    const string ScenePath = "Assets/Scenes/NarrowDistrict.unity";
    const string ManagerName = "PoliceManager";
    const string DirectorPath = "Assets/ScriptableObjects/Police/PoliceDirector_NarrowDistrict.asset";
    const string CatalogPath = "Assets/ScriptableObjects/Police/PoliceVehiclePrefabCatalog.asset";
    const string StandardPath = "Assets/ScriptableObjects/Police/PoliceStandard.asset";
    const string SportPath = "Assets/ScriptableObjects/Police/PoliceSport.asset";
    const string HeavyPath = "Assets/ScriptableObjects/Police/PoliceHeavy.asset";

    /// <summary>Outcome of one dry-run, apply, readback, or rollback request.</summary>
    public sealed class Result {
        /// <summary>Operation status.</summary>
        public Status status;
        /// <summary>Stable failure or diagnostic text.</summary>
        public string reason;
        /// <summary>Undo snapshot captured before mutation.</summary>
        public Snapshot snapshot;
        /// <summary>Readback of the selected scene composition.</summary>
        public Readback readback;
        /// <summary>True when serialized state was changed in memory.</summary>
        public bool changed;
        /// <summary>True when the caller may request a scene/asset save.</summary>
        public bool savePermitted;
    }

    /// <summary>Operation status returned by the migration API.</summary>
    public enum Status { DryRun, Applied, NoOp, Failed, RolledBack }

    /// <summary>Undo identity captured before a migration mutation.</summary>
    public sealed class Snapshot {
        /// <summary>Undo group containing all migration mutations.</summary>
        public int undoGroup;
        /// <summary>Scene path captured before mutation.</summary>
        public string scenePath;
        /// <summary>Objects and assets registered in the group.</summary>
        public UnityEngine.Object[] targets;
    }

    /// <summary>Compact serialized readback used by editor tests and operator tooling.</summary>
    public sealed class Readback {
        /// <summary>Whether the explicit PoliceManager root exists exactly once.</summary>
        public bool hasOneManager;
        /// <summary>Whether the binding owns the police path instead of legacy traffic.</summary>
        public bool usesExplicitOwner;
        /// <summary>Whether all binding dependencies resolve in the same scene.</summary>
        public bool sameSceneReferences;
        /// <summary>Whether the selected director uses FreeDrive.</summary>
        public bool freeDrive;
        /// <summary>Director profile identifier read back from the binding.</summary>
        public string profileId;
        /// <summary>Number of authored migration-target vehicle profiles.</summary>
        public int authoredProfileCount;
        /// <summary>Whether the legacy civilian owner remains present and enabled.</summary>
        public bool civilianTrafficPreserved;
        /// <summary>Whether duplicate owner components were detected.</summary>
        public bool duplicateOwners;
        /// <summary>Whether the director and physical provider live on the explicit manager.</summary>
        public bool ownsPoliceComponents;
        /// <summary>Hash of the protected GameScene before and after the operation.</summary>
        public string gameSceneHash;
    }

    /// <summary>Inspects NarrowDistrict without changing scene, asset, or undo state.</summary>
    /// <param name="scene">Loaded NarrowDistrict scene to inspect.</param>
    /// <returns>A dry-run result with validation and readback details.</returns>
    public static Result DryRun(Scene scene) => Execute(scene, false);

    /// <summary>Applies the idempotent migration in memory and records one Undo group.</summary>
    /// <param name="scene">Loaded NarrowDistrict scene to migrate.</param>
    /// <returns>Applied, no-op, or failure result; nothing is saved.</returns>
    public static Result Apply(Scene scene) => Execute(scene, true);

    /// <summary>Reverts a previously applied result through its dedicated Undo group.</summary>
    /// <param name="result">Applied result returned by <see cref="Apply"/>.</param>
    /// <returns>Rollback result with fresh readback.</returns>
    public static Result Rollback(Result result) {
        if (result == null || result.snapshot == null) return Failure(Status.Failed, "Migration snapshot is missing.");
        try {
            Undo.RevertAllDownToGroup(result.snapshot.undoGroup);
            Scene scene = SceneManager.GetSceneByPath(result.snapshot.scenePath);
            return new Result { status = Status.RolledBack, reason = "Migration rollback completed.", readback = Read(scene) };
        } catch (Exception exception) { return Failure(Status.Failed, "Migration rollback failed: " + exception.Message); }
    }

    /// <summary>Explicitly saves a successful migration when the caller chooses to do so.</summary>
    /// <param name="result">Successful result returned by <see cref="Apply"/>.</param>
    /// <returns>True only when the NarrowDistrict scene was saved.</returns>
    public static bool Save(Result result) {
        if (result == null || result.readback == null || !result.savePermitted) return false;
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        return scene.IsValid() && EditorSceneManager.SaveScene(scene);
    }

    static Result Execute(Scene scene, bool mutate) {
        try {
            ValidateScene(scene);
            Readback before = Read(scene);
            ValidateReadback(before);
            if (!mutate) return new Result { status = Status.DryRun, reason = "Dry-run completed.", readback = before };
            if (before.hasOneManager && before.usesExplicitOwner && before.freeDrive && before.ownsPoliceComponents)
                return new Result { status = Status.NoOp, reason = "NarrowDistrict free pilot is already migrated.", readback = before };
            Snapshot snapshot = Capture(scene);
            try {
                ApplyMutation(scene, snapshot);
                Readback after = Read(scene);
                ValidateReadback(after);
                return new Result { status = Status.Applied, reason = "NarrowDistrict free pilot migrated in memory.", snapshot = snapshot, readback = after, changed = true, savePermitted = true };
            } catch (Exception exception) {
                Undo.RevertAllDownToGroup(snapshot.undoGroup);
                return Failure(Status.Failed, "Migration failed and was reverted: " + exception.Message);
            }
        } catch (Exception exception) { return Failure(Status.Failed, exception.Message); }
    }

    static Snapshot Capture(Scene scene) {
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Migrate NarrowDistrict police free pilot");
        var targets = new List<UnityEngine.Object>();
        foreach (Component component in Components<MonoBehaviour>(scene)) targets.Add(component);
        foreach (string path in new[] { DirectorPath, StandardPath, SportPath, HeavyPath }) {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null) targets.Add(asset);
        }
        Undo.RegisterCompleteObjectUndo(targets.ToArray(), "Migrate NarrowDistrict police free pilot");
        return new Snapshot { undoGroup = group, scenePath = scene.path, targets = targets.ToArray() };
    }

    static void ApplyMutation(Scene scene, Snapshot snapshot) {
        SceneTrafficBinding traffic = Components<SceneTrafficBinding>(scene).Single();
        PoliceDirector director = Components<PoliceDirector>(scene).Single();
        PrefabPoliceVehicleBodyProvider provider = Components<PrefabPoliceVehicleBodyProvider>(scene).Single();
        TrafficSessionHost host = Components<TrafficSessionHost>(scene).Single();
        TrafficDamageWorld world = Components<TrafficDamageWorld>(scene).Single();
        PoliceDirectorData data = AssetDatabase.LoadAssetAtPath<PoliceDirectorData>(DirectorPath);
        PoliceVehiclePrefabCatalog catalog = AssetDatabase.LoadAssetAtPath<PoliceVehiclePrefabCatalog>(CatalogPath);
        if (data == null || catalog == null) throw new InvalidOperationException("NarrowDistrict police assets are missing.");

        ConfigureProfiles();
        data.navigationMode = PoliceNavigationMode.FreeDrive;
        EditorUtility.SetDirty(data);
        GameObject manager = FindManagers(scene).SingleOrDefault();
        if (manager == null) {
            manager = new GameObject(ManagerName);
            Undo.RegisterCreatedObjectUndo(manager, "Create PoliceManager");
            SceneManager.MoveGameObjectToScene(manager, scene);
        }
        PoliceSceneBinding binding = manager.GetComponent<PoliceSceneBinding>();
        if (binding == null) binding = Undo.AddComponent<PoliceSceneBinding>(manager);
        PoliceDirector previousDirector = director;
        PrefabPoliceVehicleBodyProvider previousProvider = provider;
        if (director.gameObject != manager) {
            director = Undo.AddComponent<PoliceDirector>(manager);
            EditorUtility.CopySerialized(previousDirector, director);
        }
        if (provider.gameObject != manager) {
            provider = Undo.AddComponent<PrefabPoliceVehicleBodyProvider>(manager);
            EditorUtility.CopySerialized(previousProvider, provider);
        }
        SetReference(director, "sharedDamageWorld", world);
        SetReference(director, "sessionHost", host);
        SetReference(traffic, "policeDirector", director);
        SetReference(traffic, "policeBodyProvider", provider);
        SetReference(binding, "host", host);
        SetReference(binding, "damageWorld", world);
        SetReference(binding, "director", director);
        SetReference(binding, "bodyProvider", provider);
        SetReference(binding, "prefabCatalog", catalog);
        SetReference(binding, "directorData", data);
        if (!binding.TrySetPlacementSettings(traffic.PolicePlacementSettings, out string placementReason))
            throw new InvalidOperationException(placementReason);
        if (!binding.TrySetTrafficBinding(traffic, out placementReason))
            throw new InvalidOperationException(placementReason);
        if (!binding.ValidateForSession(host, out string reason))
            throw new InvalidOperationException(reason);
        // Authoring must not freeze the runtime-only ownership latch before session activation.
        SetReference(traffic, "policeSceneBinding", binding);
        if (previousDirector != director) Undo.DestroyObjectImmediate(previousDirector);
        if (previousProvider != provider) Undo.DestroyObjectImmediate(previousProvider);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void SetReference(UnityEngine.Object target, string field, UnityEngine.Object value) {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null) throw new InvalidOperationException("Missing migration field: " + field);
        property.objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }

    static void ConfigureProfiles() {
        SetProfile(StandardPath, new[] { .70f, .20f, .10f });
        SetProfile(SportPath, new[] { .25f, .65f, .10f });
        SetProfile(HeavyPath, new[] { .30f, .10f, .60f });
    }

    static void SetProfile(string path, float[] weights) {
        PoliceVehicleProfile profile = AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>(path);
        if (profile == null) throw new InvalidOperationException("Missing migration-target profile: " + path);
        profile.roleWeights = new List<PoliceVehicleProfile.RoleWeight> {
            new PoliceVehicleProfile.RoleWeight { role = PoliceTacticalRole.Pursue, weight = weights[0] },
            new PoliceVehicleProfile.RoleWeight { role = PoliceTacticalRole.Intercept, weight = weights[1] },
            new PoliceVehicleProfile.RoleWeight { role = PoliceTacticalRole.Ram, weight = weights[2] }
        };
        if (profile.variationSettings == null) profile.variationSettings = new PoliceUnitVariationSettings();
        EditorUtility.SetDirty(profile);
    }

    static Readback Read(Scene scene) {
        var managers = FindManagers(scene).ToArray();
        var bindings = Components<PoliceSceneBinding>(scene).ToArray();
        SceneTrafficBinding traffic = Components<SceneTrafficBinding>(scene).SingleOrDefault();
        PoliceSceneBinding binding = managers.Length == 1 ? managers[0].GetComponent<PoliceSceneBinding>() : null;
        return new Readback {
            hasOneManager = managers.Length == 1,
            usesExplicitOwner = traffic != null && traffic.PoliceBinding == binding && binding != null,
            sameSceneReferences = binding != null && binding.Host != null && binding.DamageWorld != null && binding.Director != null && binding.Host.gameObject.scene == scene && binding.DamageWorld.gameObject.scene == scene && binding.Director.gameObject.scene == scene,
            freeDrive = binding != null && binding.NavigationMode == PoliceNavigationMode.FreeDrive,
            profileId = binding != null && binding.Director != null && binding.Director.DirectorData != null ? binding.Director.DirectorData.profileId : null,
            authoredProfileCount = new[] { StandardPath, SportPath, HeavyPath }.Count(path => AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>(path) != null),
            civilianTrafficPreserved = scene.GetRootGameObjects().Any(root => root.name == "CivilianTraffic" && root.activeSelf),
            duplicateOwners = managers.Length > 1 || bindings.Length > 1 || Components<PoliceDirector>(scene).Count() > 1 || Components<PrefabPoliceVehicleBodyProvider>(scene).Count() > 1,
            ownsPoliceComponents = binding != null && binding.Director != null && binding.Director.gameObject == binding.gameObject &&
                binding.GetComponent<PrefabPoliceVehicleBodyProvider>() != null,
            gameSceneHash = ProtectedGameSceneHash()
        };
    }

    static void ValidateScene(Scene scene) {
        if (Application.isPlaying) throw new InvalidOperationException("Migration requires Edit Mode.");
        if (!scene.IsValid() || scene.path != ScenePath || SceneManager.sceneCount != 1) throw new InvalidOperationException("Open clean NarrowDistrict alone in Edit Mode.");
        if (scene.isDirty) throw new InvalidOperationException("Preserve unsaved scene edits before migration.");
        if (!File.Exists("Assets/Scenes/GameScene.unity")) throw new InvalidOperationException("Protected GameScene is missing.");
    }

    static void ValidateReadback(Readback readback) {
        if (readback.duplicateOwners) throw new InvalidOperationException("Duplicate police owner components were detected.");
    }

    static Result Failure(Status status, string reason) => new Result { status = status, reason = reason };
    static string ProtectedGameSceneHash() {
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes("Assets/Scenes/GameScene.unity"))).Replace("-", string.Empty);
    }
    static IEnumerable<GameObject> FindManagers(Scene scene) => scene.GetRootGameObjects().Where(root => root.name == ManagerName);
    static IEnumerable<T> Components<T>(Scene scene) where T : Component {
        foreach (GameObject root in scene.GetRootGameObjects()) foreach (T component in root.GetComponentsInChildren<T>(true)) yield return component;
    }
}
