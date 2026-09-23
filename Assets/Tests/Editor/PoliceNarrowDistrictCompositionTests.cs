using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Native isolated-scene composition coverage for the NarrowDistrict traffic variants.</summary>
public sealed class PoliceNarrowDistrictCompositionTests {
    /// <summary>Replays the owner's formerly stalled poses against the captured authored collision map.</summary>
    [UnityTest]
    public IEnumerator FreePursuitAtPreviouslyStalledPoses() {
        Assert.IsNull(GameManager.Instance);
        yield return new EnterPlayMode();
        var snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
        Assert.IsNotNull(snapshot);
        try {
            var noTraffic = AssetDatabase.LoadAssetAtPath<ShiftModifierData>("Assets/ScriptableObjects/Modifiers/NoTraffic.asset");
            RunVariant(snapshot, "free-stalled-regression", new[] { noTraffic.modifierId }, false, true,
                freeDrive: true, stalledRegression: true);
        } finally {
            snapshot.AssertSourcesUnchanged();
            PoliceNarrowDistrictGeometrySnapshot.ClearPending();
        }
    }

    /// <summary>Exercises free police ownership for all authored traffic/police modifier combinations.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrictFreeVariantsUseNativeComposition() {
        Assert.IsNull(GameManager.Instance);
        yield return new EnterPlayMode();
        Assert.IsNull(GameManager.Instance);
        var snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
        Assert.IsNotNull(snapshot);
        try {
            var noTraffic = AssetDatabase.LoadAssetAtPath<ShiftModifierData>("Assets/ScriptableObjects/Modifiers/NoTraffic.asset");
            var peaceful = AssetDatabase.LoadAssetAtPath<ShiftModifierData>("Assets/ScriptableObjects/Modifiers/Peaceful.asset");
            Assert.IsNotNull(noTraffic);
            Assert.IsNotNull(peaceful);
            RunVariant(snapshot, "free-normal", Array.Empty<string>(), true, true, freeDrive: true);
            RunVariant(snapshot, "free-NoTraffic", new[] { noTraffic.modifierId }, false, true, freeDrive: true);
            RunVariant(snapshot, "free-Peaceful", new[] { peaceful.modifierId }, true, false, freeDrive: true);
        } finally {
            snapshot.AssertSourcesUnchanged();
            PoliceNarrowDistrictGeometrySnapshot.ClearPending();
        }
    }

    const string MapPath = "Assets/ScriptableObjects/Map_NarrowDistrict.asset";
    const string DamagePath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Damage.asset";
    const string DirectorPath = "Assets/ScriptableObjects/Police/PoliceDirector_NarrowDistrict.asset";
    const string PoliceCatalogPath = "Assets/ScriptableObjects/Police/PoliceVehiclePrefabCatalog.asset";
    static readonly MethodInfo followerTick = typeof(CivilianRouteFollower).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo motorStep = typeof(NpcVehicleMotor).GetMethod("SimulateStep", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly string[] civilianIds = { "nd-compact-blue", "nd-compact-red", "nd-compact-green", "nd-compact-yellow" };
    static readonly string[] civilianPrefabPaths = {
        "Assets/Prefabs/Traffic/NarrowDistrict/TrafficCompact_blue.prefab",
        "Assets/Prefabs/Traffic/NarrowDistrict/TrafficCompact_red.prefab",
        "Assets/Prefabs/Traffic/NarrowDistrict/TrafficCompact_green.prefab",
        "Assets/Prefabs/Traffic/NarrowDistrict/TrafficCompact_yellow.prefab"
    };

    /// <summary>Composes normal, NoTraffic and Peaceful from one captured native geometry snapshot.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrictVariantsUseNativeComposition() {
        Assert.IsNull(GameManager.Instance);
        yield return new EnterPlayMode();
        Assert.IsNull(GameManager.Instance);
        var snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
        Assert.IsNotNull(snapshot);
        try {
            var noTraffic = AssetDatabase.LoadAssetAtPath<ShiftModifierData>("Assets/ScriptableObjects/Modifiers/NoTraffic.asset");
            var peaceful = AssetDatabase.LoadAssetAtPath<ShiftModifierData>("Assets/ScriptableObjects/Modifiers/Peaceful.asset");
            Assert.IsNotNull(noTraffic); Assert.IsNotNull(peaceful);
            RunVariant(snapshot, "normal", System.Array.Empty<string>(), true, true);
            RunVariant(snapshot, "NoTraffic", new[] { noTraffic.modifierId }, false, true);
            RunVariant(snapshot, "Peaceful", new[] { peaceful.modifierId }, true, false);
        } finally {
            snapshot.AssertSourcesUnchanged();
            PoliceNarrowDistrictGeometrySnapshot.ClearPending();
        }
    }

    /// <summary>Consumes one canonical staged navigation module through both real traffic role owners.</summary>
    [UnityTest]
    public IEnumerator CanonicalImportedNavigationDrivesNormalComposition() {
        Assert.IsNull(GameManager.Instance);
        yield return new EnterPlayMode();
        Assert.IsNull(GameManager.Instance);
        var snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
        Assert.IsNotNull(snapshot);
        try {
            var imported = StageCanonicalNavigation(snapshot.ResolveNavigation());
            RunVariant(snapshot, "canonical-imported", System.Array.Empty<string>(), true, true, imported);
        } finally {
            snapshot.AssertSourcesUnchanged();
            PoliceNarrowDistrictGeometrySnapshot.ClearPending();
        }
    }

    /// <summary>Exits Play Mode after any composition assertion or cleanup failure.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void RunVariant(PoliceNarrowDistrictGeometrySnapshot snapshot, string label, string[] modifierIds, bool civiliansExpected, bool policeExpected, MapNavigationDocument stagedNavigation = null, bool freeDrive = false, bool stalledRegression = false) {
        MapData sourceMap = AssetDatabase.LoadAssetAtPath<MapData>(MapPath);
        TrafficDamageSettings sourceDamage = AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>(DamagePath);
        VehicleData vehicle = AssetDatabase.LoadAssetAtPath<VehicleData>("Assets/ScriptableObjects/GreenSedanData.asset");
        PoliceDirectorData directorData = AssetDatabase.LoadAssetAtPath<PoliceDirectorData>(DirectorPath);
        PoliceVehiclePrefabCatalog policeCatalog = AssetDatabase.LoadAssetAtPath<PoliceVehiclePrefabCatalog>(PoliceCatalogPath);
        Assert.IsNotNull(sourceMap); Assert.IsNotNull(sourceDamage); Assert.IsNotNull(vehicle); Assert.IsNotNull(directorData); Assert.IsNotNull(policeCatalog);
        MapData map = null; TrafficDamageSettings damage = null; GameObject root = null; GameObject managerObject = null; GameObject cameraObject = null;
        TrafficSessionHost host = null; TrafficSessionServices services = null; PoliceDirectorRuntime runtime = null;
        GameObject policeRoot = null;
        using (var fixture = new PursuitFixture(authored: AuthoredSetup())) using (snapshot.StageInto(fixture.FixtureScene)) {
            fixture.Controller.ResetForNewLife(); fixture.Binding.body.gameObject.SetActive(false); fixture.Player.Unbind(); fixture.World.EndSession();
            fixture.ConfigureAuthoredNavigation(stagedNavigation ?? snapshot.ResolveNavigation(), snapshot.Origin, new Vector2(-70f, -50f), 0f,
                stalledRegression ? new Vector2(-66.09f, -23.35f) : new Vector2(6f, -5f));
            try {
                map = UnityEngine.Object.Instantiate(sourceMap); map.name = "DetachedMap_" + label; map.sceneName = fixture.FixtureScene.name;
                Assert.AreEqual(sourceMap.mapId, map.mapId);
                damage = UnityEngine.Object.Instantiate(sourceDamage);
                root = NewInScene("NarrowDistrictComposition_" + label, fixture.FixtureScene);
                managerObject = NewInScene("InactiveSessionData_" + label, fixture.FixtureScene); managerObject.SetActive(false);
                var manager = managerObject.AddComponent<GameManager>();
                manager.allModifiers = LoadedModifiers(); manager.currentMap = map; manager.currentDifficultyId = "tier_01"; manager.currentVehicle = vehicle;
                manager.ownedMapIds.Add(map.mapId); manager.vehicleSaveList.Add(new VehicleSaveData(vehicle.vehicleId, true));
                Assert.IsNull(GameManager.Instance); Assert.IsNull(manager.Saves); Assert.IsNull(manager.Content); Assert.IsNull(manager.Career);
                Assert.IsTrue(manager.SelectModifiers(modifierIds).rejectedIds.Count == 0, label + " modifiers");

                root.SetActive(false);
                host = root.AddComponent<TrafficSessionHost>(); var binding = root.AddComponent<SceneTrafficBinding>(); var traffic = root.AddComponent<TrafficManager>();
                var civilians = root.AddComponent<PrefabCivilianVehicleBodyProvider>();
                if (freeDrive) { policeRoot = NewInScene("PoliceManager", fixture.FixtureScene); policeRoot.SetActive(false); }
                GameObject owner = freeDrive ? policeRoot : root;
                var policeBodies = owner.AddComponent<PrefabPoliceVehicleBodyProvider>();
                var director = owner.AddComponent<PoliceDirector>();
                cameraObject = NewInScene("InactiveCompositionCamera_" + label, fixture.FixtureScene); cameraObject.SetActive(false);
                var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.transform.position = new Vector3(0f, 0f, -10f);
                ConfigureCivilianProvider(civilians); Configure(host, binding, traffic, civilians, policeBodies, director, camera, map, damage, policeCatalog, directorData);
                PoliceSceneBinding freeBinding = null;
                if (freeDrive) {
                    var world = root.AddComponent<TrafficDamageWorld>();
                    freeBinding = owner.AddComponent<PoliceSceneBinding>();
                    Set(freeBinding, "host", host); Set(freeBinding, "damageWorld", world);
                    Set(freeBinding, "director", director); Set(freeBinding, "bodyProvider", policeBodies);
                    Set(freeBinding, "prefabCatalog", policeCatalog); Set(freeBinding, "directorData", directorData);
                    Set(director, "sharedDamageWorld", world);
                    Set(binding, "policeSceneBinding", freeBinding);
                    Assert.IsTrue(freeBinding.TrySetTrafficBinding(binding, out string ownerReason), ownerReason);
                    Assert.IsTrue(freeBinding.TrySetPlacementSettings(binding.PolicePlacementSettings, out ownerReason), ownerReason);
                }
                root.SetActive(true); cameraObject.SetActive(true);
                if (policeRoot != null) policeRoot.SetActive(true);
                host.RegisterPlayer(fixture.Player.gameObject);
                if (stagedNavigation != null) host.PrepareSession(manager, stagedNavigation);
                else host.PrepareSession(manager);
                Assert.IsNull(GameManager.Instance); Assert.IsNull(manager.Saves); Assert.IsNull(manager.Content); Assert.IsNull(manager.Career);
                Assert.IsNotNull(host.Coordinator); Assert.IsNotNull(host.Services); Assert.IsNotNull(host.Navigation);
                Assert.AreEqual(civiliansExpected, host.Coordinator.Context.Snapshot.trafficEnabled); Assert.AreEqual(policeExpected, host.Coordinator.Context.Snapshot.policeEnabled);
                Assert.AreEqual(civiliansExpected, traffic.IsRunning && traffic.AliveCivilianCount > 0, label + " initial civilians");
                runtime = director.Runtime; services = host.Services; Assert.IsNotNull(runtime); Assert.IsNotNull(services); Assert.AreEqual(policeExpected, runtime.IsEnabled);
                RunSynchronousSteps(fixture, traffic, binding, director, host.GetComponent<TrafficDamageWorld>(), runtime, civiliansExpected, policeExpected, label, freeBinding, stalledRegression);
                if (stagedNavigation != null) AssertImportedNavigationConsumers(stagedNavigation, traffic, binding, services);
            } finally {
                try { if (host != null) host.EndSession(); }
                finally {
                    if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject); if (root != null) UnityEngine.Object.DestroyImmediate(root);
                    if (policeRoot != null) UnityEngine.Object.DestroyImmediate(policeRoot);
                    if (managerObject != null) UnityEngine.Object.DestroyImmediate(managerObject);
                    if (damage != null) UnityEngine.Object.DestroyImmediate(damage); if (map != null) UnityEngine.Object.DestroyImmediate(map);
                }
            }
            if (services != null) Assert.IsTrue(services.IsClosed, label + " services closed");
            if (runtime != null) { Assert.AreEqual(0, runtime.ActiveCount, label + " runtime active cleanup"); Assert.AreEqual(0, runtime.PendingCount, label + " runtime pending cleanup"); }
            Assert.IsNull(GameManager.Instance);
        }
    }

    static MapNavigationDocument StageCanonicalNavigation(MapNavigationDocument source) {
        Assert.IsTrue(NavigationDocumentCodec.TryEncode(source, null, out string json, out string hash, out string reason), reason);
        var dependencies = new List<NavigationDependency>();
        foreach (string profileId in source.profileDependencies)
            dependencies.Add(new NavigationDependency { dependencyId = profileId, dependencyKind = "vehicleProfile", contentHash = "fixture:" + profileId });
        PopulationBudgetData population = AssetDatabase.LoadAssetAtPath<PopulationBudgetData>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Population.asset");
        PoliceDirectorData director = AssetDatabase.LoadAssetAtPath<PoliceDirectorData>(DirectorPath);
        RespawnTimingRules respawn = AssetDatabase.LoadAssetAtPath<RespawnTimingRules>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Respawn.asset");
        Assert.IsNotNull(population); Assert.IsNotNull(director); Assert.IsNotNull(respawn);
        foreach (DifficultyTrafficBinding binding in source.difficultyProfileBindings) {
            AddDependency(dependencies, binding.civilianPopulationProfileId, "populationProfile", Fingerprint(binding.civilianPopulationProfileId, population));
            AddDependency(dependencies, binding.policeDirectorProfileId, "policeDirectorProfile", Fingerprint(binding.policeDirectorProfileId, director));
        }
        foreach (CivilianRouteRecord route in source.civilianRoutes)
            if (!string.IsNullOrEmpty(route.respawnProfileId))
                AddDependency(dependencies, route.respawnProfileId, "respawnProfile", Fingerprint(route.respawnProfileId, respawn));
        var manifest = new NavigationModuleManifest { schemaVersion = NavigationDocumentCodec.CurrentSchemaVersion, mapId = source.mapId, documentId = source.documentId, canonicalHash = hash, dependencies = dependencies };
        var catalog = new PackageTrafficProfileCatalog(LoadNavigationProfiles(), null, dependencies);
        var stager = new NavigationPackageStager();
        byte[] payload = Encoding.UTF8.GetBytes(json);
        Assert.IsTrue(stager.TryStage(payload, manifest, hash, catalog, out reason), reason);
        Assert.IsTrue(stager.TryActivate(out reason), reason);
        var active = stager.ActiveDocument;
        Assert.IsNotNull(active);
        Assert.IsFalse(stager.TryStage(payload, manifest, "wrong-external-hash", catalog, out _));
        Assert.AreEqual(active.documentId, stager.ActiveDocument.documentId);
        return active;
    }

    static void AddDependency(List<NavigationDependency> dependencies, string id, string kind, string fingerprint) {
        if (string.IsNullOrEmpty(id) || dependencies.Exists(x => x.dependencyId == id)) return;
        dependencies.Add(new NavigationDependency { dependencyId = id, dependencyKind = kind, contentHash = fingerprint });
    }

    static string Fingerprint(string id, UnityEngine.Object asset) {
        return "fixture:" + id + ":" + NavigationDocumentCodec.ComputeHash(JsonUtility.ToJson(asset));
    }

    static NpcVehicleProfile[] LoadNavigationProfiles() => new[] {
        AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Compact_blue.asset"),
        AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Compact_red.asset"),
        AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Compact_green.asset"),
        AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Compact_yellow.asset"),
        AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceStandardData.asset"),
        AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceSportData.asset"),
        AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceHeavyData.asset")
    };

    static void AssertImportedNavigationConsumers(MapNavigationDocument imported, TrafficManager traffic, SceneTrafficBinding binding, TrafficSessionServices services) {
        Assert.IsNotNull(imported);
        Assert.IsNotNull(services);
        Assert.IsNotNull(services.Graph);
        Assert.AreEqual(imported.edges.Count, services.Graph.Edges.Count);
        foreach (var edge in imported.edges) Assert.IsNotNull(services.Graph.GetEdge(edge.edgeId), edge.edgeId);
        var plannerGraph = typeof(CivilianPopulationPlanner).GetField("graph", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(traffic.Planner);
        Assert.AreSame(services.Graph, plannerGraph);
        var policeRuntime = typeof(SceneTrafficBinding).GetField("policePopulationRuntime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(binding);
        Assert.IsNotNull(policeRuntime);
        var policeConfig = typeof(PolicePopulationRuntime).GetField("config", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(policeRuntime);
        var policeServices = (TrafficSessionServices)policeConfig.GetType().GetField("services").GetValue(policeConfig);
        Assert.AreSame(services, policeServices);
        Assert.AreSame(services.Graph, policeServices.Graph);
    }

    static void RunSynchronousSteps(PursuitFixture fixture, TrafficManager traffic, SceneTrafficBinding binding, PoliceDirector director, TrafficDamageWorld world, PoliceDirectorRuntime runtime, bool civiliansExpected, bool policeExpected, string label, PoliceSceneBinding freeBinding = null, bool stalledRegression = false) {
        int preGateSteps = 0;
        var observedPolice = new Dictionary<int, Vector2>();
        float maximumPoliceTravel = 0f;
        string freeDiagnostic = string.Empty;
        for (int step = 0; step < (freeBinding != null ? 2000 : 800); step++) {
            const float dt = 0.02f;
            director.SendMessage("FixedUpdate", SendMessageOptions.DontRequireReceiver);
            binding.SendMessage("FixedUpdate", SendMessageOptions.DontRequireReceiver);
            if (freeBinding != null) freeBinding.SendMessage("FixedUpdate", SendMessageOptions.DontRequireReceiver);
            traffic.Tick(dt);
            foreach (var follower in UnityEngine.Object.FindObjectsByType<CivilianRouteFollower>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (follower.gameObject.scene == fixture.FixtureScene && follower.isActiveAndEnabled) followerTick.Invoke(follower, new object[] { dt });
            foreach (var controller in UnityEngine.Object.FindObjectsByType<PolicePursuitController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (controller.gameObject.scene != fixture.FixtureScene || !controller.isActiveAndEnabled) continue;
                controller.Tick(dt, false);
                if (freeBinding != null && controller.IsBound) {
                    Assert.IsTrue(controller.UsesFreeDrive, label + " free-drive facade");
                    var receiver = controller.GetComponent<VehicleDamageReceiver>();
                    var rigidbody = controller.GetComponent<Rigidbody2D>();
                    int lifeId = receiver.Identity.lifeId;
                    if (!observedPolice.TryGetValue(lifeId, out Vector2 initial)) {
                        if (stalledRegression) {
                            rigidbody.position = new Vector2(-46.08f, 22.08f);
                            rigidbody.rotation = 180f;
                            rigidbody.linearVelocity = Vector2.zero;
                            Physics2D.SyncTransforms();
                        }
                        observedPolice.Add(lifeId, rigidbody.position);
                    }
                    else maximumPoliceTravel = Mathf.Max(maximumPoliceTravel, Vector2.Distance(initial, rigidbody.position));
                    if (step == 1999) {
                        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        object policy = typeof(PolicePursuitController).GetField("freeDrive", flags).GetValue(controller);
                        var policyType = policy.GetType();
                        var planner = (PoliceFreePathPlanner)policyType.GetField("planner", flags).GetValue(policy);
                        var recovery = (PoliceFreeRecovery)policyType.GetField("recovery", flags).GetValue(policy);
                        freeDiagnostic = " pose=" + rigidbody.position + " target=" + fixture.Player.GetComponent<Rigidbody2D>().position +
                            " cmd=" + controller.LastCommand.throttle + "/" + controller.LastCommand.brake + "/" + controller.LastCommand.steering +
                            " path=" + planner.Current.status + "/" + planner.Current.reason + " work=" + planner.Current.consumedWork +
                            " applied=" + policyType.GetField("appliedRequestId", flags).GetValue(policy) + " recovery=" + recovery.CurrentState;
                    }
                }
            }
            foreach (var motor in UnityEngine.Object.FindObjectsByType<NpcVehicleMotor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (motor.gameObject.scene == fixture.FixtureScene && motor.isActiveAndEnabled) motorStep.Invoke(motor, new object[] { dt });
            world.BeginPhysicsStep(); Assert.IsTrue(fixture.Physics.Simulate(dt)); world.Tick(dt);
            binding.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            if (freeBinding != null) freeBinding.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            director.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            director.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
            if (world.SessionTime < 15f) { preGateSteps++; Assert.AreEqual(0, runtime.ActiveCount, label + " police before 15 seconds"); }
        }
        Assert.Greater(preGateSteps, 0); Assert.AreEqual(civiliansExpected, traffic.AliveCivilianCount > 0, label + " stepped civilians"); Assert.LessOrEqual(traffic.AliveCivilianCount, 10);
        if (policeExpected) {
            Assert.Greater(runtime.ActiveCount, 0, label + " materialized police"); Assert.Less(runtime.EffectiveHeat, 25f, label + " slow heat");
            if (freeBinding != null) {
                foreach (var candidate in freeBinding.GetComponentsInChildren<PolicePursuitController>(true))
                    freeDiagnostic += " owner-child=" + candidate.name + "/active=" + candidate.isActiveAndEnabled + "/bound=" + candidate.IsBound +
                        "/free=" + candidate.UsesFreeDrive + "/scene=" + candidate.gameObject.scene.name;
            }
            if (freeBinding != null) Assert.Greater(maximumPoliceTravel, stalledRegression ? 10f : 1f,
                label + " physical free pursuit displacement" + freeDiagnostic);
            foreach (var controller in UnityEngine.Object.FindObjectsByType<PolicePursuitController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (controller.gameObject.scene == fixture.FixtureScene && controller.isActiveAndEnabled) {
                    Assert.IsTrue(controller.IsBound, label + " controller");
                    if (!stalledRegression) continue;
                    Assert.Less(Vector2.Distance(controller.GetComponent<Rigidbody2D>().position, fixture.PlayerPosition),
                        Vector2.Distance(new Vector2(-46.08f, 22.08f), fixture.PlayerPosition) - 10f,
                        "Previously stalled police must close distance, not merely oscillate." + freeDiagnostic);
                    var receiver = controller.GetComponent<VehicleDamageReceiver>();
                    float heatBefore = runtime.EffectiveHeat;
                    var context = new DamageContext("free-root-death", 0, InstigatorKind.Environment, 0,
                        DamageKind.Explosion, "free-root-death");
                    Assert.IsTrue(receiver.ApplyBlast(new BlastApplication("free-root-death", receiver.Identity.lifeId,
                        VehicleRole.Police, 10000f, context)));
                    director.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                    Assert.Greater(runtime.EffectiveHeat, heatBefore, "Separate director receives every police death.");
                }
        } else { Assert.AreEqual(0, runtime.ActiveCount, label + " peaceful active police"); Assert.AreEqual(0, runtime.PendingCount, label + " peaceful requests"); }
    }

    static PursuitFixture.AuthoredSetup AuthoredSetup() => new PursuitFixture.AuthoredSetup {
        profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceStandardData.asset"),
        civilianProfile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Compact_green.asset"),
        damage = AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>(DamagePath),
        vehicle = AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>("Assets/ScriptableObjects/Police/PoliceStandard.asset"),
        behavior = AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>("Assets/ScriptableObjects/Police/PolicePursueBehavior.asset")
    };

    static ShiftModifierData[] LoadedModifiers() => new[] {
        AssetDatabase.LoadAssetAtPath<ShiftModifierData>("Assets/ScriptableObjects/Modifiers/NoTraffic.asset"),
        AssetDatabase.LoadAssetAtPath<ShiftModifierData>("Assets/ScriptableObjects/Modifiers/Peaceful.asset")
    };

    static GameObject NewInScene(string name, Scene scene) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }

    static void ConfigureCivilianProvider(PrefabCivilianVehicleBodyProvider provider) {
        var so = new SerializedObject(provider); var entries = so.FindProperty("entries"); entries.arraySize = civilianIds.Length;
        for (int i = 0; i < civilianIds.Length; i++) { var entry = entries.GetArrayElementAtIndex(i); entry.FindPropertyRelative("visualCatalogId").stringValue = civilianIds[i]; var prefabObject = AssetDatabase.LoadAssetAtPath<GameObject>(civilianPrefabPaths[i]); Assert.IsNotNull(prefabObject, civilianPrefabPaths[i]); var prefab = prefabObject.GetComponent<CivilianVehicleBody>(); Assert.IsNotNull(prefab, civilianPrefabPaths[i] + " CivilianVehicleBody"); entry.FindPropertyRelative("prefab").objectReferenceValue = prefab; }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Configure(TrafficSessionHost host, SceneTrafficBinding binding, TrafficManager traffic, PrefabCivilianVehicleBodyProvider civilians, PrefabPoliceVehicleBodyProvider policeBodies, PoliceDirector director, Camera camera, MapData map, TrafficDamageSettings damage, PoliceVehiclePrefabCatalog catalog, PoliceDirectorData directorData) {
        Set(host, "map", map); Set(host, "damageSettings", damage); Set(host, "trafficBinding", binding);
        Set(binding, "map", map); Set(binding, "host", host); Set(binding, "traffic", traffic); Set(binding, "bodies", civilians); Set(binding, "gameplayCamera", camera);
        Set(binding, "profiles", new[] { AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Compact_blue.asset"), AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Compact_red.asset"), AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Compact_green.asset"), AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Compact_yellow.asset") });
        Set(binding, "population", AssetDatabase.LoadAssetAtPath<PopulationBudgetData>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Population.asset")); Set(binding, "respawn", AssetDatabase.LoadAssetAtPath<RespawnTimingRules>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Respawn.asset")); Set(binding, "populationProfileId", "nd-sparse-pilot"); Set(binding, "editorTestDifficultyId", "tier_01");
        Set(binding, "policeVehicleProfiles", new[] { AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>("Assets/ScriptableObjects/Police/PoliceStandard.asset"), AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>("Assets/ScriptableObjects/Police/PoliceSport.asset"), AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>("Assets/ScriptableObjects/Police/PoliceHeavy.asset") });
        Set(binding, "policeBehaviorProfiles", new[] { AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>("Assets/ScriptableObjects/Police/PolicePursueBehavior.asset"), AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>("Assets/ScriptableObjects/Police/PoliceInterceptBehavior.asset"), AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>("Assets/ScriptableObjects/Police/PoliceRamBehavior.asset") });
        Set(binding, "policeDirectorProfiles", new[] { directorData }); Set(binding, "policePrefabCatalog", catalog); Set(binding, "policeBodyProvider", policeBodies); Set(binding, "policeDirector", director);
    }

    static void Set(UnityEngine.Object target, string name, UnityEngine.Object value) { var so = new SerializedObject(target); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    static void Set<T>(UnityEngine.Object target, string name, T[] values) where T : UnityEngine.Object {
        var so = new SerializedObject(target); var property = so.FindProperty(name); property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void Set(UnityEngine.Object target, string name, string value) { var so = new SerializedObject(target); so.FindProperty(name).stringValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
}
