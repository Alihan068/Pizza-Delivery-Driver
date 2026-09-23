using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>Targeted structural tests for S08.1 police configuration and prefab preflight.</summary>
public sealed class PoliceProfileTests {
    static readonly List<Object> created = new List<Object>();

    /// <summary>Cleans every temporary GameObject and ScriptableObject created by a test.</summary>
    [TearDown]
    public void TearDown() {
        foreach (var item in created) if (item != null) Object.DestroyImmediate(item);
        created.Clear();
    }

    /// <summary>Checks strict ID, duplicate, unresolved, and whitespace rejection.</summary>
    [Test]
    public void PoliceCatalog_RejectsBlankDuplicateUnknownAndConflictingIds() {
        var valid = MakeProfile("builtin:police-standard");
        var shared = new BuiltInTrafficProfileCatalog(new[] { valid });
        var wrapper = MakeWrapper(valid, PoliceTacticalRole.Pursue);
        var behavior = MakeBehavior("builtin:police-pursue", PoliceTacticalRole.Pursue);
        Assert.IsTrue(new PoliceProfileCatalog(shared, new[] { wrapper }, new[] { behavior }).TryValidate(out _));
        valid.vehicleProfileId = string.Empty;
        Assert.IsFalse(new PoliceProfileCatalog(shared, new[] { wrapper }, new[] { behavior }).TryValidate(out _));
        valid.vehicleProfileId = "builtin:police-standard";
        Assert.IsFalse(new PoliceProfileCatalog(shared, new[] { wrapper, wrapper }, new[] { behavior }).TryValidate(out _));
        var unresolved = MakeProfile("builtin:unknown");
        Assert.IsFalse(new PoliceProfileCatalog(shared, new[] { MakeWrapper(unresolved, PoliceTacticalRole.Pursue) }, new[] { behavior }).TryValidate(out _));
        behavior.behaviorProfileId = " ";
        Assert.IsFalse(new PoliceProfileCatalog(shared, new[] { wrapper }, new[] { behavior }).TryValidate(out _));
    }

    /// <summary>Checks multiple roles on one vehicle without a vehicle-type switch.</summary>
    [Test]
    public void PoliceVehicle_AllowsTwoDistinctBehaviorsWithoutVehicleTypeSwitch() {
        var profile = MakeProfile("builtin:police-standard");
        var wrapper = MakeWrapper(profile, PoliceTacticalRole.Pursue, PoliceTacticalRole.Intercept);
        var shared = new BuiltInTrafficProfileCatalog(new[] { profile });
        var catalog = new PoliceProfileCatalog(shared, new[] { wrapper }, new[] {
            MakeBehavior("builtin:police-pursue", PoliceTacticalRole.Pursue),
            MakeBehavior("builtin:police-intercept", PoliceTacticalRole.Intercept)
        });
        Assert.IsTrue(catalog.TryValidate(out string reason), reason);
        Assert.AreEqual(wrapper, catalog.ResolveVehicle("builtin:police-standard"));
        Assert.AreEqual(PoliceTacticalRole.Intercept, catalog.ResolveBehavior("builtin:police-intercept").tacticalRole);
    }

    /// <summary>Checks unsupported roles and Police permission.</summary>
    [Test]
    public void PoliceProfile_RejectsUnsupportedRoleAndMissingPolicePermission() {
        var profile = MakeProfile("builtin:police-standard");
        var shared = new BuiltInTrafficProfileCatalog(new[] { profile });
        Assert.IsFalse(new PoliceProfileCatalog(shared, new[] { MakeWrapper(profile, PoliceTacticalRole.Pursue) }, new[] { MakeBehavior("b", PoliceTacticalRole.Ram) }).TryValidate(out _));
        profile.allowedRoles.Clear();
        Assert.IsFalse(new PoliceProfileCatalog(shared, new[] { MakeWrapper(profile, PoliceTacticalRole.Pursue) }, new[] { MakeBehavior("b", PoliceTacticalRole.Pursue) }).TryValidate(out _));
    }

    /// <summary>Checks null and malformed profile values.</summary>
    [Test]
    public void PolicePreflight_RejectsNullInvalidAndMismatchedProfileValues() {
        var profile = MakeProfile("builtin:police-standard");
        var shared = new BuiltInTrafficProfileCatalog(new[] { profile });
        Assert.IsFalse(PolicePreflight.ValidateVehicleProfile(null, shared, out string nullReason));
        StringAssert.Contains("null", nullReason);
        var validBody = MakeBody();
        Assert.IsTrue(PolicePreflight.ValidatePrefab(validBody, profile, out string validReason), validReason);
        profile.maxHealth = float.NaN;
        Assert.IsFalse(PolicePreflight.ValidateVehicleProfile(MakeWrapper(profile, PoliceTacticalRole.Pursue), shared, out _));
        profile.maxHealth = 80f;
        profile.collisionArmor = 2f;
        Assert.IsFalse(PolicePreflight.ValidateVehicleProfile(MakeWrapper(profile, PoliceTacticalRole.Pursue), shared, out _));
        profile.collisionArmor = 0.1f;
        profile.colliderSize = new Vector2(99f, 99f);
        Assert.IsFalse(PolicePreflight.ValidatePrefab(validBody, profile, out string footprintReason));
        StringAssert.Contains("footprint", footprintReason);
    }

    /// <summary>Checks child forbidden components and invalid physics.</summary>
    [Test]
    public void PolicePreflight_RejectsForbiddenChildAndBadPhysics() {
        var body = MakeBody();
        var profile = MakeProfile("builtin:police-standard");
        Assert.IsTrue(PolicePreflight.ValidatePrefab(body, profile, out string baselineReason), baselineReason);
        var child = new GameObject("ForbiddenChild");
        created.Add(child);
        child.transform.SetParent(body.transform);
        child.AddComponent<VehicleInput>();
        Assert.IsFalse(PolicePreflight.ValidatePrefab(body, profile, out _));
        Object.DestroyImmediate(child);
        body.Body.gravityScale = 1f;
        Assert.IsFalse(PolicePreflight.ValidatePrefab(body, profile, out _));
        body.Body.gravityScale = 0f;
        body.MainCollider.enabled = false;
        Assert.IsFalse(PolicePreflight.ValidatePrefab(body, profile, out string disabledColliderReason));
        StringAssert.Contains("disabled", disabledColliderReason);
        body.MainCollider.enabled = true;
        var renderer = body.GetComponentInChildren<SpriteRenderer>(true);
        renderer.enabled = false;
        Assert.IsFalse(PolicePreflight.ValidatePrefab(body, profile, out string disabledRendererReason));
        StringAssert.Contains("enabled authored sprite", disabledRendererReason);
    }

    /// <summary>Checks duplicate physics components and forbidden child managers.</summary>
    [Test]
    public void PolicePreflight_RejectsDuplicatePhysicsComponentsAndChildGameManager() {
        var body = MakeBody();
        var profile = MakeProfile("builtin:police-standard");
        Assert.IsTrue(PolicePreflight.ValidatePrefab(body, profile, out string baselineReason), baselineReason);
        var child = new GameObject("DuplicatePhysics");
        created.Add(child);
        child.transform.SetParent(body.transform);
        child.AddComponent<BoxCollider2D>();
        Assert.IsFalse(PolicePreflight.ValidatePrefab(body, profile, out _));
        Object.DestroyImmediate(child);
        var duplicateMarker = new GameObject("DuplicateBodyMarker");
        created.Add(duplicateMarker);
        duplicateMarker.transform.SetParent(body.transform);
        duplicateMarker.AddComponent<PoliceVehicleBody>();
        Assert.IsFalse(PolicePreflight.ValidatePrefab(body, profile, out _));
        Object.DestroyImmediate(duplicateMarker);
        var managerChild = new GameObject("ForbiddenManager");
        created.Add(managerChild);
        managerChild.transform.SetParent(body.transform);
        managerChild.AddComponent<GameManager>();
        Assert.IsFalse(PolicePreflight.ValidatePrefab(body, profile, out _));
    }

    /// <summary>Checks actual vehicle and behavior selection compatibility plus ambiguity.</summary>
    [Test]
    public void PoliceCatalog_SelectionRequiresCompatibleBehaviorAndUniqueResolvers() {
        var profile = MakeProfile("builtin:police-standard");
        var wrapper = MakeWrapper(profile, PoliceTacticalRole.Pursue);
        var pursue = MakeBehavior("builtin:police-pursue", PoliceTacticalRole.Pursue);
        var intercept = MakeBehavior("builtin:police-intercept", PoliceTacticalRole.Intercept);
        var otherProfile = MakeProfile("builtin:police-sport");
        var otherWrapper = MakeWrapper(otherProfile, PoliceTacticalRole.Intercept);
        var validCatalog = new PoliceProfileCatalog(new BuiltInTrafficProfileCatalog(new[] { profile, otherProfile }), new[] { wrapper, otherWrapper }, new[] { pursue, intercept });
        Assert.IsTrue(validCatalog.TryValidate(out string catalogReason), catalogReason);
        Assert.IsNull(validCatalog.ResolveVehicle(" "));
        var prefabCatalog = ScriptableObject.CreateInstance<PoliceVehiclePrefabCatalog>();
        created.Add(prefabCatalog);
        prefabCatalog.entries.Add(new PoliceVehiclePrefabCatalog.Entry { visualCatalogId = profile.visualCatalogId, prefab = MakeBody() });
        Assert.IsTrue(validCatalog.TrySelect("builtin:police-standard", "builtin:police-pursue", prefabCatalog, out _, out _, out _, out string selectReason), selectReason);
        Assert.IsFalse(validCatalog.TrySelect("builtin:police-standard", "builtin:police-intercept", prefabCatalog, out _, out _, out _, out string incompatibleReason));
        StringAssert.Contains("not supported", incompatibleReason);
        Assert.IsFalse(new PoliceProfileCatalog(new BuiltInTrafficProfileCatalog(new[] { profile }), new[] { wrapper, wrapper }, new[] { pursue }).ResolveVehicle("builtin:police-standard") != null);
    }

    /// <summary>Checks missing and duplicate visual bindings.</summary>
    [Test]
    public void PolicePrefabCatalog_RejectsMissingDuplicateAndAcceptsFilenameIndependentBinding() {
        var catalog = ScriptableObject.CreateInstance<PoliceVehiclePrefabCatalog>();
        created.Add(catalog);
        var body = MakeBody();
        catalog.entries.Add(new PoliceVehiclePrefabCatalog.Entry { visualCatalogId = "builtin:police-standard-visual", prefab = body });
        Assert.IsTrue(PolicePreflight.ValidatePrefabCatalog(catalog, out string reason), reason);
        Assert.IsTrue(catalog.TryResolve("builtin:police-standard-visual", out var resolved));
        Assert.AreEqual(body, resolved);
        catalog.entries.Clear();
        catalog.entries.Add(null);
        Assert.IsFalse(catalog.TryResolve("builtin:police-standard-visual", out var nullEntryResult));
        Assert.IsNull(nullEntryResult);
        catalog.entries.Clear();
        catalog.entries.Add(new PoliceVehiclePrefabCatalog.Entry { visualCatalogId = "builtin:police-standard-visual", prefab = body });
        catalog.entries.Add(new PoliceVehiclePrefabCatalog.Entry { visualCatalogId = "builtin:police-standard-visual", prefab = body });
        Assert.IsFalse(PolicePreflight.ValidatePrefabCatalog(catalog, out _));
        Assert.IsFalse(catalog.TryResolve("builtin:police-standard-visual", out var duplicateResult));
        Assert.IsNull(duplicateResult);
    }

    [Test]
    /// <summary>Checks all real inactive police assets without waking them.</summary>
    public void PolicePrefabAssets_ValidateWhileInactiveWithoutAwakeOrLiveBounds() {
        foreach (var path in new[] {
            "Assets/Prefabs/Police/PoliceStandard.prefab",
            "Assets/Prefabs/Police/PoliceSport.prefab",
            "Assets/Prefabs/Police/PoliceHeavy.prefab" }) {
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefabAsset, path);
            Assert.IsFalse(prefabAsset.activeSelf, path + " must remain inactive in the asset");
            var prefab = prefabAsset.GetComponent<PoliceVehicleBody>();
            Assert.IsNotNull(prefab, path + " missing PoliceVehicleBody");
            var profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>(path.Replace("Assets/Prefabs/Police/Police", "Assets/ScriptableObjects/Police/Police").Replace(".prefab", "Data.asset"));
            Assert.IsNotNull(profile, path);
            Assert.IsTrue(PolicePreflight.ValidatePrefab(prefab, profile, out string reason), path + ": " + reason);
        }
    }

    [Test]
    /// <summary>Checks authored local collider geometry while inactive and rotated.</summary>
    public void PolicePreflight_UsesAuthoredFootprintForInactiveRotatedBody() {
        var body = MakeBody();
        var profile = MakeProfile("builtin:police-standard");
        body.transform.rotation = Quaternion.Euler(0f, 0f, 37f);
        body.gameObject.SetActive(false);
        Assert.IsTrue(PolicePreflight.ValidatePrefab(body, profile, out string reason), reason);
    }

    [Test]
    /// <summary>Checks that a fourth vehicle is data-only and needs no vehicle enum entry.</summary>
    public void PoliceCatalog_AcceptsFourthVehicleByDataOnly() {
        var standard = MakeProfile("builtin:police-standard");
        var fourth = MakeProfile("builtin:police-fourth");
        var wrapperA = MakeWrapper(standard, PoliceTacticalRole.Pursue);
        var wrapperB = MakeWrapper(fourth, PoliceTacticalRole.Recover);
        var catalog = new PoliceProfileCatalog(new BuiltInTrafficProfileCatalog(new[] { standard, fourth }), new[] { wrapperA, wrapperB }, new[] { MakeBehavior("builtin:police-pursue", PoliceTacticalRole.Pursue), MakeBehavior("builtin:police-recover", PoliceTacticalRole.Recover) });
        Assert.IsTrue(catalog.TryValidate(out string reason), reason);
        Assert.IsNotNull(catalog.ResolveVehicle("builtin:police-fourth"));
    }

    /// <summary>Validates all authored police assets, bindings, behaviors, selections, and distinct vehicle tuning.</summary>
    [Test]
    public void PoliceRealAssets_ValidateCatalogSelectionsAndDistinctTuningWithoutElite() {
        var profiles = new[] {
            AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceStandardData.asset"),
            AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceSportData.asset"),
            AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceHeavyData.asset") };
        var wrappers = new[] {
            AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>("Assets/ScriptableObjects/Police/PoliceStandard.asset"),
            AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>("Assets/ScriptableObjects/Police/PoliceSport.asset"),
            AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>("Assets/ScriptableObjects/Police/PoliceHeavy.asset") };
        var behaviors = new[] {
            AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>("Assets/ScriptableObjects/Police/PolicePursueBehavior.asset"),
            AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>("Assets/ScriptableObjects/Police/PoliceInterceptBehavior.asset"),
            AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>("Assets/ScriptableObjects/Police/PoliceRamBehavior.asset"),
            AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>("Assets/ScriptableObjects/Police/PoliceRecoverBehavior.asset") };
        var prefabCatalog = AssetDatabase.LoadAssetAtPath<PoliceVehiclePrefabCatalog>("Assets/ScriptableObjects/Police/PoliceVehiclePrefabCatalog.asset");
        Assert.IsTrue(PolicePreflight.ValidatePrefabCatalog(prefabCatalog, out string prefabReason), prefabReason);
        var catalog = new PoliceProfileCatalog(new BuiltInTrafficProfileCatalog(profiles), wrappers, behaviors);
        Assert.IsTrue(catalog.TryValidate(out string catalogReason), catalogReason);
        for (int i = 0; i < wrappers.Length; i++) {
            Assert.IsNotNull(profiles[i]);
            Assert.IsNotNull(wrappers[i]);
            Assert.IsFalse(profiles[i].vehicleProfileId.Contains("elite"));
            Assert.IsFalse(profiles[i].visualCatalogId.Contains("elite"));
            foreach (var behavior in behaviors) {
                Assert.IsFalse(behavior.behaviorProfileId.Contains("elite"));
                Assert.IsTrue(catalog.TrySelect(profiles[i].vehicleProfileId, behavior.behaviorProfileId, prefabCatalog, out _, out _, out _, out string selectionReason), selectionReason);
            }
        }
        Assert.AreNotEqual(profiles[0].motorSettings.cruiseSpeed, profiles[1].motorSettings.cruiseSpeed);
        Assert.AreNotEqual(profiles[1].motorSettings.cruiseSpeed, profiles[2].motorSettings.cruiseSpeed);
        Assert.AreNotEqual(profiles[0].motorSettings.turnRate, profiles[1].motorSettings.turnRate);
        Assert.AreNotEqual(profiles[0].baseMass, profiles[2].baseMass);
        Assert.AreNotEqual(profiles[0].maxHealth, profiles[2].maxHealth);
    }

    static NpcVehicleProfile MakeProfile(string id) {
        var profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profile.vehicleProfileId = id;
        profile.allowedRoles = new List<VehicleRole> { VehicleRole.Police };
        profile.visualCatalogId = "visual:any-name";
        profile.motorSettings = new NpcMotorSettings { cruiseSpeed = 3.5f, maxSpeed = 4.5f, reverseSpeed = 1f, acceleration = 4f, brakeDeceleration = 8f, maxEngineForce = 16f, maxBrakeForce = 24f, turnRate = 150f, minimumTurningRadius = 1.2f, lateralGrip = 0.9f, sensorInterval = 0.2f, reactionTime = 0.3f, minimumGap = 1.2f };
        profile.colliderSize = new Vector2(0.9f, 1.7f);
        profile.baseMass = 1.4f;
        profile.maxHealth = 80f;
        profile.collisionArmor = 0.1f;
        profile.explosionResistance = 0.1f;
        profile.damageProfileId = "nd-civilian";
        profile.explosionProfileId = "nd-civilian";
        created.Add(profile);
        return profile;
    }

    static PoliceVehicleProfile MakeWrapper(NpcVehicleProfile profile, params PoliceTacticalRole[] roles) {
        var wrapper = ScriptableObject.CreateInstance<PoliceVehicleProfile>();
        wrapper.sharedNpc = profile;
        wrapper.supportedTacticalRoles = new List<PoliceTacticalRole>(roles);
        created.Add(wrapper);
        return wrapper;
    }

    static PoliceBehaviorProfile MakeBehavior(string id, PoliceTacticalRole role) {
        var behavior = ScriptableObject.CreateInstance<PoliceBehaviorProfile>();
        behavior.behaviorProfileId = id;
        behavior.tacticalRole = role;
        created.Add(behavior);
        return behavior;
    }

    PoliceVehicleBody MakeBody() {
        var root = new GameObject("PoliceBody");
        created.Add(root);
        var rb = root.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.gravityScale = 0f;
        var collider = root.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.9f, 1.7f);
        var visual = new GameObject("TemporaryPoliceVisual");
        created.Add(visual);
        visual.transform.SetParent(root.transform, false);
        var sprite = visual.AddComponent<SpriteRenderer>();
        sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/StoreAssets/Kenney/RacingSelected/car_blue_small_1.png");
        sprite.transform.localScale = new Vector3(0.9f / 2.5f, 1.7f / 4.38f, 1f);
        root.AddComponent<NpcVehicleMotor>();
        root.AddComponent<VehicleObstacleSensor>();
        root.AddComponent<VehicleDamageReceiver>();
        var body = root.AddComponent<PoliceVehicleBody>();
        var serialized = new SerializedObject(body);
        serialized.FindProperty("body").objectReferenceValue = rb;
        serialized.FindProperty("mainCollider").objectReferenceValue = collider;
        serialized.FindProperty("motor").objectReferenceValue = root.GetComponent<NpcVehicleMotor>();
        serialized.FindProperty("sensor").objectReferenceValue = root.GetComponent<VehicleObstacleSensor>();
        serialized.FindProperty("damageReceiver").objectReferenceValue = root.GetComponent<VehicleDamageReceiver>();
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return body;
    }
}
