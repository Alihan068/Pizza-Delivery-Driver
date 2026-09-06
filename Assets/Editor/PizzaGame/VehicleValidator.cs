using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Pure static engine that validates vehicle prefabs, <see cref="VehicleData"/> assets and the
/// vehicle registry (<c>allVehicles</c> on the GameManager prefab).
/// </summary>
/// <remarks>
/// This class never draws UI and never writes to the console; it only returns a list of findings.
/// The same engine is used from four places: the creation wizard's live preview, the post-creation
/// self check, the standalone validation window and the VehicleData custom inspector. Having a
/// single engine makes it impossible for the wizard to accept something the validator rejects.
/// </remarks>
public static class VehicleValidator {

    /// <summary>Project path of the GameManager prefab that holds the vehicle registry.</summary>
    public const string GameManagerPrefabPath = "Assets/Prefabs/GameManager.prefab";

    // ---------------------------------------------------------------- prefab

    /// <summary>
    /// Validates that a vehicle prefab is structurally complete: required components, tag,
    /// sprite, collider and Inspector references.
    /// </summary>
    /// <param name="root">Root GameObject of the prefab to validate.</param>
    /// <returns>The findings; an empty list when nothing is wrong.</returns>
    public static List<VehicleIssue> ValidatePrefab(GameObject root) {
        var issues = new List<VehicleIssue>();
        if (root == null) {
            issues.Add(VehicleIssue.Error("No vehicle prefab assigned."));
            return issues;
        }

        string id = Quote(root.name);

        if (!root.CompareTag("Player")) {
            issues.Add(VehicleIssue.Error(id + " root is not tagged Player (found " + root.tag + "). No trigger will recognise the vehicle.", root));
        }

        var sr = root.GetComponent<SpriteRenderer>();
        if (sr == null) {
            issues.Add(VehicleIssue.Error(id + " root has no SpriteRenderer. The vehicle would be invisible.", root));
        }
        else if (sr.sprite == null) {
            issues.Add(VehicleIssue.Error(id + " root SpriteRenderer has no sprite. The vehicle would be invisible.", root));
        }

        RequireComponent<Rigidbody2D>(root, issues, "movement physics");
        RequireComponent<Driver>(root, issues, "driving, damage and death");
        RequireComponent<Delivery>(root, issues, "pizza carrying and delivery");
        RequireComponent<PlayerInput>(root, issues, "keyboard input");
        RequireComponent<AudioSource>(root, issues, "crash audio");
        RequireComponent<CinemachineImpulseSource>(root, issues, "screen shake on impact");

        var col = root.GetComponent<Collider2D>();
        if (col == null) {
            issues.Add(VehicleIssue.Error(id + " root has no Collider2D. The vehicle would never collide with anything.", root));
        }
        else {
            if (col.isTrigger) {
                issues.Add(VehicleIssue.Error(id + " collider is marked as a trigger. The vehicle would drive through walls.", root));
            }
            ValidateColliderCoverage(root, col, sr, issues);
        }

        ValidatePlayerInput(root, issues);
        ValidateDeliveryRefs(root, issues);
        ValidateDriverRefs(root, issues);
        ValidateNavigation(root, issues);

        return issues;
    }

    static string Quote(string s) {
        return "[" + s + "]";
    }

    static void RequireComponent<T>(GameObject root, List<VehicleIssue> issues, string purpose) where T : Component {
        if (root.GetComponent<T>() == null) {
            issues.Add(VehicleIssue.Error(Quote(root.name) + " root has no " + typeof(T).Name + " (" + purpose + " would not work).", root));
        }
    }

    static void ValidateColliderCoverage(GameObject root, Collider2D col, SpriteRenderer sr, List<VehicleIssue> issues) {
        if (sr == null || sr.sprite == null) return;
        if (!TryGetLocalColliderBounds(col, out Bounds colBounds)) return;

        Vector2 colSize = colBounds.size;
        Vector2 sprSize = sr.sprite.bounds.size;

        if (colSize.x <= 0.0001f || colSize.y <= 0.0001f) {
            issues.Add(VehicleIssue.Error(Quote(root.name) + " collider has zero area. The shape was never generated.", root));
            return;
        }

        float ratioX = colSize.x / Mathf.Max(sprSize.x, 0.0001f);
        float ratioY = colSize.y / Mathf.Max(sprSize.y, 0.0001f);

        if (ratioX < 0.5f || ratioY < 0.5f) {
            issues.Add(VehicleIssue.Warning(Quote(root.name) + " collider is much smaller than the sprite (" +
                ratioX.ToString("0.00") + "x / " + ratioY.ToString("0.00") +
                "x). It may have been inherited from the template; consider regenerating it from the sprite.", root));
        }
        else if (ratioX > 1.6f || ratioY > 1.6f) {
            issues.Add(VehicleIssue.Warning(Quote(root.name) + " collider is much larger than the sprite (" +
                ratioX.ToString("0.00") + "x / " + ratioY.ToString("0.00") +
                "x). The vehicle would collide with an invisible box.", root));
        }
    }

    /// <summary>
    /// Computes the local space bounds of a 2D collider.
    /// </summary>
    /// <remarks>
    /// On prefab assets <c>Collider2D.bounds</c> is always zero (an object that is not in a scene
    /// has no world bounds), so the geometry is read manually instead.
    /// </remarks>
    /// <param name="col">The collider to measure.</param>
    /// <param name="bounds">The computed local bounds.</param>
    /// <returns>True when the collider type is supported and has geometry.</returns>
    public static bool TryGetLocalColliderBounds(Collider2D col, out Bounds bounds) {
        bounds = new Bounds();
        switch (col) {
            case PolygonCollider2D poly: {
                if (poly.pathCount == 0) return false;
                bool any = false;
                Vector2 min = Vector2.zero;
                Vector2 max = Vector2.zero;
                for (int p = 0; p < poly.pathCount; p++) {
                    foreach (var pt in poly.GetPath(p)) {
                        if (!any) {
                            min = pt;
                            max = pt;
                            any = true;
                            continue;
                        }
                        min = Vector2.Min(min, pt);
                        max = Vector2.Max(max, pt);
                    }
                }
                if (!any) return false;
                bounds = new Bounds((min + max) * 0.5f + poly.offset, max - min);
                return true;
            }
            case BoxCollider2D box:
                bounds = new Bounds(box.offset, box.size);
                return true;
            case CircleCollider2D circle:
                bounds = new Bounds(circle.offset, Vector2.one * circle.radius * 2f);
                return true;
            case CapsuleCollider2D capsule:
                bounds = new Bounds(capsule.offset, capsule.size);
                return true;
            default:
                return false;
        }
    }

    static void ValidatePlayerInput(GameObject root, List<VehicleIssue> issues) {
        var input = root.GetComponent<PlayerInput>();
        if (input == null) return;

        if (input.actions == null) {
            issues.Add(VehicleIssue.Error(Quote(root.name) + " PlayerInput has no Actions asset. The vehicle would not respond to any key.", root));
            return;
        }
        if (string.IsNullOrEmpty(input.defaultActionMap)) {
            issues.Add(VehicleIssue.Error(Quote(root.name) + " PlayerInput has no Default Map selected. No action map would ever be enabled.", root));
        }
        else if (input.actions.FindActionMap(input.defaultActionMap) == null) {
            issues.Add(VehicleIssue.Error(Quote(root.name) + " PlayerInput Default Map (" + input.defaultActionMap +
                ") does not exist in " + input.actions.name + ".", root));
        }
        if (input.notificationBehavior != PlayerNotifications.SendMessages) {
            issues.Add(VehicleIssue.Error(Quote(root.name) + " PlayerInput Behavior is not SendMessages (found " +
                input.notificationBehavior + "). Driver.OnMove would never be called and the vehicle would not move.", root));
        }
    }

    static void ValidateDeliveryRefs(GameObject root, List<VehicleIssue> issues) {
        var delivery = root.GetComponent<Delivery>();
        if (delivery == null) return;
        var so = new SerializedObject(delivery);
        RequireRef(so, "pizzaObject", root, issues, "used unconditionally in Delivery.Start; a null here throws a NullReferenceException on the first frame");
        RequireRef(so, "wastedPizzaPrefab", root, issues, "dropped pizza effect");
        RequireRef(so, "pizzaDeliverClip", root, issues, "delivery sound");
    }

    static void ValidateDriverRefs(GameObject root, List<VehicleIssue> issues) {
        var driver = root.GetComponent<Driver>();
        if (driver == null) return;
        var so = new SerializedObject(driver);
        RequireArrayRef(so, "crashSound", root, issues, "crash audio");
        RequireRef(so, "wastedPizza", root, issues, "pizza thrown on impact");
    }

    static void RequireRef(SerializedObject so, string propertyPath, GameObject root, List<VehicleIssue> issues, string purpose) {
        var prop = so.FindProperty(propertyPath);
        if (prop == null) {
            issues.Add(VehicleIssue.Warning(Quote(root.name) + " has no field named " + propertyPath +
                ". The script may have changed; the validator needs updating.", root));
            return;
        }
        if (prop.objectReferenceValue == null) {
            issues.Add(VehicleIssue.Error(Quote(root.name) + " -> " + so.targetObject.GetType().Name + "." +
                propertyPath + " is empty (" + purpose + ").", root));
        }
    }

    static void RequireArrayRef(SerializedObject so, string propertyPath, GameObject root, List<VehicleIssue> issues, string purpose) {
        var prop = so.FindProperty(propertyPath);
        if (prop == null || !prop.isArray) {
            issues.Add(VehicleIssue.Warning(Quote(root.name) + " has no array named " + propertyPath +
                ". The validator needs updating.", root));
            return;
        }
        if (prop.arraySize == 0) {
            issues.Add(VehicleIssue.Warning(Quote(root.name) + " -> " + propertyPath + " array is empty (" + purpose + ").", root));
            return;
        }
        for (int i = 0; i < prop.arraySize; i++) {
            if (prop.GetArrayElementAtIndex(i).objectReferenceValue == null) {
                issues.Add(VehicleIssue.Warning(Quote(root.name) + " -> " + propertyPath + "[" + i + "] is empty.", root));
            }
        }
    }

    static void ValidateNavigation(GameObject root, List<VehicleIssue> issues) {
        var target = root.GetComponentInChildren<DriverTarget>(true);
        if (target == null) {
            issues.Add(VehicleIssue.Error(Quote(root.name) + " has no DriverTarget in its children. The direction arrow would not work and the player could not find customers.", root));
            return;
        }
        // DriverTarget resolves the arrow visual with exactly this expression. Rather than
        // matching a child by name we verify the same expression, which covers both child
        // order and active state in a single check.
        if (target.GetComponentInChildren<SpriteRenderer>() == null) {
            issues.Add(VehicleIssue.Error(Quote(root.name) + " -> DriverTarget has no active SpriteRenderer in its children. The direction arrow would be invisible.", target.gameObject));
        }
    }

    // ------------------------------------------------------------------ data

    /// <summary>
    /// Validates that a <see cref="VehicleData"/> asset is internally consistent, and validates
    /// its prefab as well when one is assigned.
    /// </summary>
    /// <param name="data">The vehicle data to validate.</param>
    /// <returns>The findings; an empty list when nothing is wrong.</returns>
    public static List<VehicleIssue> ValidateData(VehicleData data) {
        var issues = new List<VehicleIssue>();
        if (data == null) {
            issues.Add(VehicleIssue.Error("No VehicleData assigned."));
            return issues;
        }

        string id = Quote(data.name);

        if (string.IsNullOrWhiteSpace(data.vehicleId)) {
            issues.Add(VehicleIssue.Error(id + " has an empty vehicleId. Saves are keyed by this id, so the vehicle cannot be owned or upgraded until it has one.", data));
        }

        if (string.IsNullOrWhiteSpace(data.vehicleName)) {
            issues.Add(VehicleIssue.Warning(id + " has an empty vehicleName. Nothing is saved against it, but the player would see a blank name in the garage.", data));
        }

        if (data.vehiclePrefab == null) {
            issues.Add(VehicleIssue.Error(id + " has an empty vehiclePrefab. The garage still opens, the vehicle can be bought and Start stays enabled, but PlayerSpawner calls Instantiate(null) and the player never spawns in GameScene.", data));
        }
        else {
            issues.AddRange(ValidatePrefab(data.vehiclePrefab));
        }

        if (data.price < 0) {
            issues.Add(VehicleIssue.Error(id + " has a negative price (" + data.price + ").", data));
        }

        CheckStat(issues, data, "Speed", data.speedStep, data.maxSpeedLevel, data.speedCostMult);
        CheckStat(issues, data, "Turn", data.turnStep, data.maxTurnLevel, data.turnCostMult);
        CheckStat(issues, data, "Health", data.healthStep, data.maxHealthLevel, data.healthCostMult);
        CheckStat(issues, data, "Armor", data.armorStep, data.maxArmorLevel, data.armorCostMult);
        CheckStat(issues, data, "Capacity", data.capacityStep, data.maxCapacityLevel, data.capacityCostMult);
        CheckStat(issues, data, "Protection", data.protectionStep, data.maxProtectionLevel, data.protectionCostMult);

        if (data.baseHealth <= 0f) issues.Add(VehicleIssue.Error(id + " baseHealth is zero or negative. The vehicle would die on the first frame.", data));
        if (data.baseSpeed <= 0f) issues.Add(VehicleIssue.Error(id + " baseSpeed is zero or negative. The vehicle would not move.", data));
        if (data.baseTurn <= 0f) issues.Add(VehicleIssue.Error(id + " baseTurn is zero or negative. The vehicle would not steer.", data));
        if (data.baseCapacity <= 0) issues.Add(VehicleIssue.Error(id + " baseCapacity is zero or negative. The player could not carry any pizza.", data));

        float maxArmor = data.baseArmor + data.armorStep * data.maxArmorLevel;
        if (maxArmor >= 1f) {
            issues.Add(VehicleIssue.Warning(id + " reaches 100 percent armor when fully upgraded (" + maxArmor.ToString("0.00") +
                "). The vehicle becomes immortal; GetArmor clamps it but the risk dial of the design disappears.", data));
        }
        float maxProtection = data.baseProtection + data.protectionStep * data.maxProtectionLevel;
        if (maxProtection >= 1f) {
            issues.Add(VehicleIssue.Warning(id + " reaches 100 percent pizza protection when fully upgraded (" +
                maxProtection.ToString("0.00") + "). Pizza would never drop.", data));
        }

        if (data.vehiclePrefab != null && !string.IsNullOrWhiteSpace(data.vehicleName) && data.vehiclePrefab.name != data.vehicleName) {
            issues.Add(VehicleIssue.Info(id + " has a vehicleName (" + data.vehicleName + ") that differs from its prefab name (" +
                data.vehiclePrefab.name + "). Harmless, but it makes the pair harder to follow.", data));
        }

        return issues;
    }

    static void CheckStat(List<VehicleIssue> issues, VehicleData data, string statName, float step, int maxLevel, float costMult) {
        string id = Quote(data.name) + " " + statName;
        if (maxLevel < 0) {
            issues.Add(VehicleIssue.Error(id + " has a negative maxLevel (" + maxLevel + ").", data));
        }
        if (maxLevel > 0 && Mathf.Approximately(step, 0f)) {
            issues.Add(VehicleIssue.Warning(id + " defines " + maxLevel + " levels but its step is 0. The player would pay and receive nothing.", data));
        }
        if (step < 0f) {
            issues.Add(VehicleIssue.Warning(id + " has a negative step (" + step + "). Upgrading would lower the stat.", data));
        }
        if (costMult <= 0f) {
            issues.Add(VehicleIssue.Warning(id + " has a zero or negative costMult (" + costMult + "). Upgrades would be free.", data));
        }
    }

    // -------------------------------------------------------------- registry

    /// <summary>
    /// Validates the vehicle registry (<c>allVehicles</c> on the GameManager prefab) and every
    /// <see cref="VehicleData"/> asset in the project.
    /// </summary>
    /// <returns>The findings; an empty list when nothing is wrong.</returns>
    public static List<VehicleIssue> ValidateRegistry() {
        var issues = new List<VehicleIssue>();

        var gmPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameManagerPrefabPath);
        if (gmPrefab == null) {
            issues.Add(VehicleIssue.Error("GameManager prefab not found at " + GameManagerPrefabPath +
                ". Without it the registry has no single source of truth and vehicles must be added to all three scenes by hand again."));
            return issues;
        }

        var gm = gmPrefab.GetComponent<GameManager>();
        if (gm == null) {
            issues.Add(VehicleIssue.Error(GameManagerPrefabPath + " has no GameManager component.", gmPrefab));
            return issues;
        }

        var registered = gm.allVehicles;
        if (registered == null || registered.Length == 0) {
            issues.Add(VehicleIssue.Error("GameManager.allVehicles is empty. No vehicle is defined and the game cannot start.", gmPrefab));
            return issues;
        }

        for (int i = 0; i < registered.Length; i++) {
            if (registered[i] == null) {
                issues.Add(VehicleIssue.Error("GameManager.allVehicles[" + i + "] is empty. InitializeVehicles would throw on this element.", gmPrefab));
            }
        }

        if (registered[0] != null && registered[0].price != 0) {
            issues.Add(VehicleIssue.Warning("allVehicles[0] (" + registered[0].name + ") has a price of " + registered[0].price +
                ". The first vehicle is treated as free; InitializeVehicles unlocks it unconditionally and its price is never read.", gmPrefab));
        }

        for (int i = 1; i < registered.Length; i++) {
            if (registered[i] != null && registered[i].price <= 0) {
                issues.Add(VehicleIssue.Warning("allVehicles[" + i + "] (" + registered[i].name + ") has a price of " + registered[i].price +
                    ". Vehicles after the first are unlocked by purchase, so a zero price unlocks them for nothing.", registered[i]));
            }
        }

        // Duplicate ids are the serious case: two vehicles sharing an id share one save record, so
        // upgrades bought for one silently appear on the other. Checked across every VehicleData in
        // the project, not just the registered ones, because an unregistered asset can be added later.
        var seenIds = new Dictionary<string, VehicleData>();
        foreach (var guid in AssetDatabase.FindAssets("t:VehicleData")) {
            var asset = AssetDatabase.LoadAssetAtPath<VehicleData>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset == null || string.IsNullOrWhiteSpace(asset.vehicleId)) continue;
            string key = asset.vehicleId.Trim();
            if (seenIds.TryGetValue(key, out var clash)) {
                issues.Add(VehicleIssue.Error("Two vehicles share the same vehicleId: " + clash.name + " and " + asset.name +
                    " (both are '" + key + "'). They would share one save record and their upgrades would mix.", asset));
            }
            else {
                seenIds[key] = asset;
            }
        }

        var seenNames = new Dictionary<string, VehicleData>();
        foreach (var v in registered) {
            if (v == null || string.IsNullOrWhiteSpace(v.vehicleName)) continue;
            string key = v.vehicleName.Trim().ToLowerInvariant();
            if (seenNames.TryGetValue(key, out var other)) {
                issues.Add(VehicleIssue.Warning("Two vehicles share the display name '" + v.vehicleName + "': " +
                    other.name + " and " + v.name + ". Saves are keyed by id so nothing breaks, but the player sees two identical entries.", v));
            }
            else {
                seenNames[key] = v;
            }
        }

        foreach (var guid in AssetDatabase.FindAssets("t:VehicleData")) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<VehicleData>(path);
            if (asset == null) continue;
            if (!registered.Contains(asset)) {
                issues.Add(VehicleIssue.Warning(asset.name + " (" + path +
                    ") is not registered. It is missing from GameManager.allVehicles and never appears in the game.", asset));
            }
        }

        foreach (var v in registered) {
            if (v != null) issues.AddRange(ValidateData(v));
        }

        return issues;
    }

    /// <summary>
    /// Checks whether the C# field defaults in <see cref="VehicleData"/> still match the balance
    /// table that is actually in use.
    /// </summary>
    /// <remarks>
    /// Changing balance on the assets does not update the script defaults. Once they go stale,
    /// every vehicle made through <c>Assets &gt; Create &gt; PizzaGame &gt; Vehicle Data</c> is born
    /// with the wrong upgrade curve -- silently, with no error, only broken balance. The reference
    /// is the starting vehicle, <c>allVehicles[0]</c>: the comment in VehicleData.cs says the
    /// defaults track exactly that.
    /// </remarks>
    /// <returns>A single warning listing the drifted fields; an empty list when they still match.</returns>
    public static List<VehicleIssue> ValidateScriptDefaults() {
        var issues = new List<VehicleIssue>();

        var gmPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameManagerPrefabPath);
        var gm = gmPrefab != null ? gmPrefab.GetComponent<GameManager>() : null;
        if (gm == null || gm.allVehicles == null || gm.allVehicles.Length == 0) return issues;

        var reference = gm.allVehicles[0];
        if (reference == null) return issues;

        var probe = ScriptableObject.CreateInstance<VehicleData>();
        try {
            var drift = new List<string>();
            Compare(drift, "baseSpeed", probe.baseSpeed, reference.baseSpeed);
            Compare(drift, "speedStep", probe.speedStep, reference.speedStep);
            Compare(drift, "maxSpeedLevel", probe.maxSpeedLevel, reference.maxSpeedLevel);
            Compare(drift, "speedCostMult", probe.speedCostMult, reference.speedCostMult);
            Compare(drift, "baseTurn", probe.baseTurn, reference.baseTurn);
            Compare(drift, "turnStep", probe.turnStep, reference.turnStep);
            Compare(drift, "maxTurnLevel", probe.maxTurnLevel, reference.maxTurnLevel);
            Compare(drift, "turnCostMult", probe.turnCostMult, reference.turnCostMult);
            Compare(drift, "baseHealth", probe.baseHealth, reference.baseHealth);
            Compare(drift, "healthStep", probe.healthStep, reference.healthStep);
            Compare(drift, "maxHealthLevel", probe.maxHealthLevel, reference.maxHealthLevel);
            Compare(drift, "healthCostMult", probe.healthCostMult, reference.healthCostMult);
            Compare(drift, "baseArmor", probe.baseArmor, reference.baseArmor);
            Compare(drift, "armorStep", probe.armorStep, reference.armorStep);
            Compare(drift, "maxArmorLevel", probe.maxArmorLevel, reference.maxArmorLevel);
            Compare(drift, "armorCostMult", probe.armorCostMult, reference.armorCostMult);
            Compare(drift, "baseCapacity", probe.baseCapacity, reference.baseCapacity);
            Compare(drift, "capacityStep", probe.capacityStep, reference.capacityStep);
            Compare(drift, "maxCapacityLevel", probe.maxCapacityLevel, reference.maxCapacityLevel);
            Compare(drift, "capacityCostMult", probe.capacityCostMult, reference.capacityCostMult);
            Compare(drift, "baseProtection", probe.baseProtection, reference.baseProtection);
            Compare(drift, "protectionStep", probe.protectionStep, reference.protectionStep);
            Compare(drift, "maxProtectionLevel", probe.maxProtectionLevel, reference.maxProtectionLevel);
            Compare(drift, "protectionCostMult", probe.protectionCostMult, reference.protectionCostMult);

            if (drift.Count > 0) {
                issues.Add(VehicleIssue.Warning("The C# defaults in VehicleData.cs no longer match " + reference.name +
                    " (" + drift.Count + " fields): " + string.Join(", ", drift) +
                    ". Vehicles created from the Create menu would be born with these old values. " +
                    "The wizard is unaffected because it clones a template, but the defaults should be updated.", reference));
            }
        }
        finally {
            Object.DestroyImmediate(probe);
        }

        return issues;
    }

    static void Compare(List<string> drift, string field, float scriptValue, float assetValue) {
        if (!Mathf.Approximately(scriptValue, assetValue)) {
            drift.Add(field + " (" + scriptValue.ToString("0.###") + " -> " + assetValue.ToString("0.###") + ")");
        }
    }

    static void Compare(List<string> drift, string field, int scriptValue, int assetValue) {
        if (scriptValue != assetValue) {
            drift.Add(field + " (" + scriptValue + " -> " + assetValue + ")");
        }
    }

    /// <summary>
    /// Checks the GameManager instances in the open scenes: whether they are still prefab
    /// instances and whether they carry an override on <c>allVehicles</c>.
    /// </summary>
    /// <remarks>
    /// Only loaded scenes are inspected; scenes on disk are never opened, which makes this cheap
    /// enough to call on every repaint. If a scene overrides <c>allVehicles</c>, a vehicle added
    /// to the prefab will not appear in that scene -- exactly a repeat of BF-015.
    /// </remarks>
    /// <returns>The findings; an empty list when nothing is wrong.</returns>
    public static List<VehicleIssue> ValidateOpenScenes() {
        var issues = new List<VehicleIssue>();

        for (int s = 0; s < EditorSceneManager.sceneCount; s++) {
            var scene = EditorSceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (var rootGo in scene.GetRootGameObjects()) {
                foreach (var gm in rootGo.GetComponentsInChildren<GameManager>(true)) {
                    var go = gm.gameObject;

                    if (!PrefabUtility.IsPartOfPrefabInstance(go)) {
                        issues.Add(VehicleIssue.Warning("The GameManager in scene " + scene.name + " is not a prefab instance. " +
                            "Its registry lives separately in that scene, so new vehicles added to the prefab will not appear there.", go));
                        continue;
                    }

                    var mods = PrefabUtility.GetPropertyModifications(go);
                    if (mods == null) continue;
                    foreach (var m in mods) {
                        if (m.target is GameManager && m.propertyPath != null && m.propertyPath.StartsWith("allVehicles")) {
                            issues.Add(VehicleIssue.Error("The GameManager in scene " + scene.name + " overrides allVehicles (" +
                                m.propertyPath + "). Vehicles added to the prefab will not appear in this scene. Revert the override in the Inspector.", go));
                            break;
                        }
                    }
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Runs every check: the registry, the data and prefabs of registered vehicles, whether the
    /// script defaults are current, and the prefab links of the open scenes.
    /// </summary>
    /// <returns>The findings sorted by severity, most severe first.</returns>
    public static List<VehicleIssue> ValidateProject() {
        var issues = new List<VehicleIssue>();
        issues.AddRange(ValidateRegistry());
        issues.AddRange(ValidateScriptDefaults());
        issues.AddRange(ValidateOpenScenes());
        return issues.OrderByDescending(i => (int)i.severity).ToList();
    }
}
