using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Captures and stages the authored NarrowDistrict collision geometry for isolated police tests.</summary>
public sealed class PoliceNarrowDistrictGeometrySnapshot {
    const string PayloadKey = "PizzaDeliveryDriver.PoliceNarrowDistrictGeometrySnapshot.v1";
    const string ScenePath = "Assets/Scenes/NarrowDistrict.unity";
    const string MapPath = "Assets/ScriptableObjects/Map_NarrowDistrict.asset";
    const string NavigationPath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Navigation.asset";
    const int MaxColliders = 2048;
    const int MaxNodes = 4096;
    const int MaxPayloadBytes = 16 * 1024 * 1024;
    const float Epsilon = 0.0001f;
    const float BoundaryEpsilon = 0.01f;

    SnapshotData data;

    /// <summary>Gets the authored map origin.</summary>
    public Vector2 Origin => data.origin;

    /// <summary>Gets the number of supported solid colliders captured.</summary>
    public int ColliderCount => data.colliders.Count;

    /// <summary>Gets the unique capture token.</summary>
    public string Token => data.token;

    PoliceNarrowDistrictGeometrySnapshot(SnapshotData captured) {
        data = captured;
    }

    /// <summary>Captures the clean loaded NarrowDistrict into the private editor session payload.</summary>
    public static void CaptureForTestRun() {
        if (!string.IsNullOrEmpty(SessionState.GetString(PayloadKey, string.Empty))) throw new InvalidOperationException("A NarrowDistrict snapshot is already pending.");
        try { CaptureForTestRunCore(); }
        catch { SessionState.EraseString(PayloadKey); throw; }
    }

    static void CaptureForTestRunCore() {
        if (Application.isPlaying) throw new InvalidOperationException("Capture requires edit mode.");
        Scene source = SceneManager.GetActiveScene();
        if (!source.IsValid() || !source.isLoaded || source.path != ScenePath || source.name != "NarrowDistrict")
            throw new InvalidOperationException("The clean NarrowDistrict scene must be active and loaded.");
        if (SceneManager.sceneCount != 1 || source.isDirty)
            throw new InvalidOperationException("Capture requires the clean scene only and no unsaved changes.");

        PizzaMapSceneRoot[] roots = UnityEngine.Object.FindObjectsByType<PizzaMapSceneRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        PizzaMapSceneRoot root = null;
        for (int i = 0; i < roots.Length; i++) if (roots[i].gameObject.scene == source) {
            if (root != null) throw new InvalidOperationException("NarrowDistrict has multiple typed map roots.");
            root = roots[i];
        }
        if (root == null || root.blueprintId != "NarrowDistrict_Asymmetric_PrefabCity" || root.mapGrid == null || root.decorationRoot == null)
            throw new InvalidOperationException("NarrowDistrict authored root preflight failed.");
        RequireIdentity(root.transform, "map root");
        RequireIdentity(root.mapGrid.transform, "map grid");

        MapData map = AssetDatabase.LoadAssetAtPath<MapData>(MapPath);
        TrafficMapData traffic = AssetDatabase.LoadAssetAtPath<TrafficMapData>(NavigationPath);
        if (map == null || map.mapId != "4c46aef37a1f4f468fc0915175730643" || map.sceneName != "NarrowDistrict" || traffic == null)
            throw new InvalidOperationException("NarrowDistrict map assets failed identity validation.");
        MapNavigationDocument document = traffic.ResolveDocument();
        if (document == null || document.mapId != map.mapId || document.localBounds != new Rect(-76f, -56f, 152f, 112f)) throw new InvalidOperationException("Navigation identity failed.");

        SnapshotData captured = new SnapshotData { token = Guid.NewGuid().ToString("N"), origin = Vector2.zero, mapBounds = document.localBounds,
            navigationJson = JsonUtility.ToJson(document), sceneHash = HashFile(ScenePath) };
        captured.sourceHashes.Add(new HashRecord { path = ScenePath, hash = captured.sceneHash });
        captured.sourceHashes.Add(new HashRecord { path = MapPath, hash = HashFile(MapPath) });
        captured.sourceHashes.Add(new HashRecord { path = NavigationPath, hash = HashFile(NavigationPath) });
        AddRequiredSourceHash(captured, "Assets/ScriptableObjects/Police/PoliceStandardData.asset");
        AddRequiredSourceHash(captured, "Assets/ScriptableObjects/Police/PoliceStandard.asset");
        AddRequiredSourceHash(captured, "Assets/ScriptableObjects/Traffic/NarrowDistrict/Damage.asset");
        AddRequiredSourceHash(captured, "Assets/ScriptableObjects/Police/PolicePursueBehavior.asset");
        AddRequiredSourceHash(captured, "Assets/ScriptableObjects/Police/PoliceRamBehavior.asset");
        CaptureLayers(captured);
        HashSet<Transform> required = CaptureColliders(root.transform, captured);
        CaptureRequiredTransform(root.transform, 0, required, captured);
        if (captured.transforms.Count > MaxNodes || captured.colliders.Count > MaxColliders)
            throw new InvalidOperationException("NarrowDistrict geometry exceeds snapshot limits.");
        if (captured.colliders.Count != 648 || captured.boxCount != 646 || captured.capsuleCount != 2 || captured.outsideCount != 0)
            throw new InvalidOperationException("NarrowDistrict collider counts do not match the accepted authored contract.");
        ValidateBoundaryEvidence(captured);
        new PoliceNarrowDistrictGeometrySnapshot(captured).AssertSourcesUnchanged();
        WritePayload(captured);
    }

    /// <summary>Consumes and validates the private capture payload, clearing it even when validation fails.</summary>
    public static PoliceNarrowDistrictGeometrySnapshot ConsumeForTestRun() {
        string json = SessionState.GetString(PayloadKey, string.Empty);
        try {
            if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("No NarrowDistrict snapshot is pending.");
            if (Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes) throw new InvalidOperationException("Snapshot payload is oversized.");
            SnapshotData captured = JsonUtility.FromJson<SnapshotData>(json);
            ValidateData(captured);
            PoliceNarrowDistrictGeometrySnapshot snapshot = new PoliceNarrowDistrictGeometrySnapshot(captured);
            snapshot.AssertSourcesUnchanged();
            return snapshot;
        } finally {
            SessionState.EraseString(PayloadKey);
        }
    }

    /// <summary>Clears only this helper's pending editor session payload.</summary>
    public static void ClearPending() {
        SessionState.EraseString(PayloadKey);
    }

    /// <summary>Verifies captured source files and the 32-by-32 physics layer matrix remain unchanged.</summary>
    public void AssertSourcesUnchanged() {
        for (int i = 0; i < data.sourceHashes.Count; i++) {
            HashRecord record = data.sourceHashes[i];
            RequireCleanAsset(record.path);
            if (HashFile(record.path) != record.hash) throw new InvalidOperationException("Authored source changed: " + record.path);
        }
        bool[] current = new bool[1024];
        CaptureLayers(current);
        for (int i = 0; i < current.Length; i++) if (current[i] != data.layers[i])
            throw new InvalidOperationException("Physics layer collision matrix changed.");
    }

    /// <summary>Returns a detached navigation document for each call.</summary>
    public MapNavigationDocument ResolveNavigation() {
        return JsonUtility.FromJson<MapNavigationDocument>(data.navigationJson);
    }

    /// <summary>Stages native transforms and supported colliders into an already-loaded isolated scene.</summary>
    public IDisposable StageInto(Scene scene) {
        PhysicsScene2D physicsScene = scene.GetPhysicsScene2D();
        if (!Application.isPlaying || !scene.IsValid() || !scene.isLoaded || scene.path == ScenePath || !physicsScene.IsValid() || physicsScene == Physics2D.defaultPhysicsScene)
            throw new ArgumentException("StageInto requires a loaded isolated 2D scene.", nameof(scene));
        AssertSourcesUnchanged();
        GameObject stage = null;
        try {
            stage = new GameObject("PoliceNarrowDistrictGeometry_" + data.token);
            SceneManager.MoveGameObjectToScene(stage, scene);
            Dictionary<int, Transform> map = new Dictionary<int, Transform>();
            for (int i = 0; i < data.transforms.Count; i++) {
                TransformRecord record = data.transforms[i];
                Transform transform;
                if (i == 0) transform = stage.transform;
                else {
                    GameObject go = new GameObject(record.name);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    transform = go.transform;
                    transform.SetParent(map[record.parentId], false);
                }
                transform.localPosition = record.position;
                transform.localRotation = record.rotation;
                transform.localScale = record.scale;
                transform.gameObject.layer = record.layer;
                transform.gameObject.SetActive(record.active);
                map.Add(record.id, transform);
            }
            Dictionary<int, Collider2D> staged = new Dictionary<int, Collider2D>();
            for (int i = 0; i < data.colliders.Count; i++) AddCollider(data.colliders[i], map, staged);
            VerifyStaged(staged, map, scene);
            return new StageHandle(stage);
        } catch {
            if (stage != null) UnityEngine.Object.DestroyImmediate(stage);
            throw;
        }
    }

    static void WritePayload(SnapshotData captured) {
        string json = JsonUtility.ToJson(captured);
        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes) throw new InvalidOperationException("Snapshot payload exceeds 16 MiB.");
        SessionState.SetString(PayloadKey, json);
    }

    static HashSet<Transform> CaptureColliders(Transform root, SnapshotData captured) {
        var required = new HashSet<Transform> { root };
        Collider2D[] all = UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all.Length > MaxColliders) throw new InvalidOperationException("Collider audit limit exceeded.");
        for (int i = 0; i < all.Length; i++) {
            Collider2D collider = all[i];
            if (collider.gameObject.scene != root.gameObject.scene) continue;
            bool included = collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger;
            if (!included) { captured.excluded.Add(new ExcludedRecord { id = collider.GetInstanceID(), reason = collider.isTrigger ? "trigger" : collider.enabled ? "inactive" : "disabled", type = collider.GetType().FullName, sourceId = collider.gameObject.GetInstanceID() }); continue; }
            if (!collider.transform.IsChildOf(root) || collider.attachedRigidbody != null || collider.sharedMaterial != null)
                throw new InvalidOperationException("Unsupported outside/body/material collider.");
            GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(collider.gameObject);
            if (prefab != null) AddPrefabDependencies(captured, AssetDatabase.GetAssetPath(prefab));
            ValidateNative(collider);
            ColliderRecord record = new ColliderRecord { id = collider.GetInstanceID(), transformId = collider.transform.gameObject.GetInstanceID(),
                offset = collider.offset, density = collider.density, boundsCenter = collider.bounds.center, boundsSize = collider.bounds.size, layer = collider.gameObject.layer,
                box = collider is BoxCollider2D, capsule = collider is CapsuleCollider2D };
            if (record.box) { BoxCollider2D box = (BoxCollider2D)collider; record.size = box.size; record.edgeRadius = box.edgeRadius; }
            else if (record.capsule) { CapsuleCollider2D capsule = (CapsuleCollider2D)collider; record.size = capsule.size; record.direction = (int)capsule.direction; }
            else throw new InvalidOperationException("Unsupported live collider shape.");
            if (record.box) captured.boxCount++; else captured.capsuleCount++;
            if (!collider.transform.IsChildOf(root)) captured.outsideCount++;
            record.faces = FaceFlags(collider.bounds, captured.mapBounds);
            captured.colliders.Add(record);
            for (Transform ancestor = collider.transform; ancestor != null; ancestor = ancestor.parent) {
                required.Add(ancestor);
                if (required.Count > MaxNodes) throw new InvalidOperationException("Required transform limit exceeded.");
                if (ancestor == root) break;
            }
        }
        return required;
    }

    static void CaptureRequiredTransform(Transform transform, int parentId, HashSet<Transform> required, SnapshotData captured) {
        if (!required.Contains(transform)) return;
        if (captured.transforms.Count >= MaxNodes) throw new InvalidOperationException("Transform node limit exceeded.");
        if (!Uniform(transform.localScale) || !Uniform(transform.lossyScale) || !Orthogonal(transform.localToWorldMatrix)) throw new InvalidOperationException("Nonuniform XY scale or shear is unsupported.");
        int id = transform.gameObject.GetInstanceID();
        GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(transform.gameObject);
        if (prefab != null) AddPrefabDependencies(captured, AssetDatabase.GetAssetPath(prefab));
        captured.transforms.Add(new TransformRecord { id = id, parentId = parentId, name = transform.name, position = transform.localPosition,
            rotation = transform.localRotation, scale = transform.localScale, layer = transform.gameObject.layer, active = transform.gameObject.activeSelf });
        for (int i = 0; i < transform.childCount; i++) CaptureRequiredTransform(transform.GetChild(i), id, required, captured);
    }

    static void AddCollider(ColliderRecord record, Dictionary<int, Transform> map, Dictionary<int, Collider2D> staged) {
        GameObject go = map[record.transformId].gameObject;
        Collider2D collider;
        if (record.box) { BoxCollider2D box = go.AddComponent<BoxCollider2D>(); box.size = record.size; box.edgeRadius = record.edgeRadius; box.autoTiling = false; collider = box; }
        else { CapsuleCollider2D capsule = go.AddComponent<CapsuleCollider2D>(); capsule.size = record.size; capsule.direction = (CapsuleDirection2D)record.direction; collider = capsule; }
        // Unity rejects density writes on bodyless static shapes. The captured default must read back unchanged.
        if (Mathf.Abs(collider.density - record.density) > Epsilon) throw new InvalidOperationException("Non-default bodyless collider density is unsupported.");
        collider.offset = record.offset; collider.enabled = true; collider.isTrigger = false; collider.sharedMaterial = null;
        collider.includeLayers = 0; collider.excludeLayers = 0; collider.layerOverridePriority = 0; collider.forceSendLayers = -1;
        collider.forceReceiveLayers = -1; collider.contactCaptureLayers = -1; collider.callbackLayers = -1; collider.usedByEffector = false;
        collider.compositeOperation = Collider2D.CompositeOperation.None; collider.compositeOrder = 0;
        staged.Add(record.id, collider);
    }

    void VerifyStaged(Dictionary<int, Collider2D> staged, Dictionary<int, Transform> stagedTransforms, Scene scene) {
        if (staged.Count != data.colliders.Count) throw new InvalidOperationException("Staged collider count failed.");
        if (stagedTransforms.Count != data.transforms.Count) throw new InvalidOperationException("Staged transform count failed.");
        for (int i = 0; i < data.transforms.Count; i++) {
            TransformRecord record = data.transforms[i];
            Transform transform = stagedTransforms[record.id];
            if (transform.gameObject.scene != scene || !Same(transform.localPosition, record.position) ||
                transform.localRotation != record.rotation || !Same(transform.localScale, record.scale) ||
                transform.gameObject.layer != record.layer || transform.gameObject.activeSelf != record.active ||
                (i == 0 ? transform.parent != null : transform.parent != stagedTransforms[record.parentId]))
                throw new InvalidOperationException("Staged ancestry readback failed for source " + record.id + ".");
        }
        int faces = 0;
        for (int i = 0; i < data.colliders.Count; i++) {
            ColliderRecord record = data.colliders[i];
            Collider2D collider = staged[record.id];
            if (collider.gameObject.scene != scene) throw new InvalidOperationException("Staged collider scene failed.");
            TransformRecord transformRecord = FindTransformRecord(record.transformId);
            Transform transform = stagedTransforms[record.transformId];
            if (!Same(transform.localPosition, transformRecord.position) || transform.localRotation != transformRecord.rotation || !Same(transform.localScale, transformRecord.scale) ||
                transform.gameObject.layer != transformRecord.layer || transform.gameObject.activeSelf != transformRecord.active)
                throw new InvalidOperationException("Staged local transform readback failed for source " + record.transformId + ".");
            ValidateNative(collider);
            if (!Same(collider.offset, record.offset) || Mathf.Abs(collider.density - record.density) > Epsilon || !Same(collider.bounds.center, record.boundsCenter) || !Same(collider.bounds.size, record.boundsSize))
                throw new InvalidOperationException("Staged collider world readback failed for source " + record.id + ".");
            BoxCollider2D box = collider as BoxCollider2D;
            if (record.box && (box == null || !Same(box.size, record.size) || Mathf.Abs(box.edgeRadius - record.edgeRadius) > Epsilon)) throw new InvalidOperationException("Staged box readback failed.");
            CapsuleCollider2D capsule = collider as CapsuleCollider2D;
            if (record.capsule && (capsule == null || !Same(capsule.size, record.size) || (int)capsule.direction != record.direction)) throw new InvalidOperationException("Staged capsule readback failed.");
            if (collider.attachedRigidbody != null || collider.sharedMaterial != null || !collider.enabled || collider.isTrigger) throw new InvalidOperationException("Staged collider native state failed.");
            faces |= FaceFlags(collider.bounds, data.mapBounds);
        }
        if ((faces & data.boundaryFaces) != data.boundaryFaces) throw new InvalidOperationException("Staged boundary evidence failed.");
    }

    TransformRecord FindTransformRecord(int id) {
        for (int i = 0; i < data.transforms.Count; i++) if (data.transforms[i].id == id) return data.transforms[i];
        throw new InvalidOperationException("Missing staged transform source.");
    }

    static void ValidateNative(Collider2D collider) {
        if (collider.includeLayers.value != 0 || collider.excludeLayers.value != 0 || collider.layerOverridePriority != 0 ||
            collider.forceSendLayers.value != -1 || collider.forceReceiveLayers.value != -1 || collider.contactCaptureLayers.value != -1 ||
            collider.callbackLayers.value != -1 || collider.usedByEffector || collider.compositeOperation != Collider2D.CompositeOperation.None || collider.compositeOrder != 0)
            throw new InvalidOperationException("Collider native flags differ from the authored contract.");
        BoxCollider2D box = collider as BoxCollider2D;
        if (box != null && (box.autoTiling || Mathf.Abs(box.edgeRadius) > Epsilon)) throw new InvalidOperationException("Box flags differ.");
        CapsuleCollider2D capsule = collider as CapsuleCollider2D;
        if (capsule != null && (capsule.direction != CapsuleDirection2D.Vertical || !Same(capsule.size, new Vector2(1.28f, 2.86f)))) throw new InvalidOperationException("Capsule shape differs.");
    }

    static void ValidateData(SnapshotData captured) {
        if (captured == null || string.IsNullOrEmpty(captured.token) || captured.transforms == null || captured.colliders == null ||
            captured.colliders.Count > MaxColliders || captured.transforms.Count > MaxNodes || captured.layers == null || captured.layers.Length != 1024)
            throw new InvalidOperationException("Invalid NarrowDistrict snapshot payload.");
        if (!Guid.TryParseExact(captured.token, "N", out Guid token) || string.IsNullOrEmpty(captured.navigationJson) || captured.sourceHashes == null)
            throw new InvalidOperationException("Invalid NarrowDistrict snapshot identity.");
        for (int i = 0; i < captured.sourceHashes.Count; i++) if (string.IsNullOrEmpty(captured.sourceHashes[i].path) || string.IsNullOrEmpty(captured.sourceHashes[i].hash) || captured.sourceHashes[i].hash.Length != 64)
            throw new InvalidOperationException("Invalid NarrowDistrict source hash.");
    }

    static void CaptureLayers(SnapshotData captured) { captured.layers = new bool[1024]; CaptureLayers(captured.layers); }
    static void CaptureLayers(bool[] layers) { for (int i = 0; i < 32; i++) for (int j = 0; j < 32; j++) layers[i * 32 + j] = Physics2D.GetIgnoreLayerCollision(i, j); }
    static void AddRequiredSourceHash(SnapshotData captured, string path) {
        if (string.IsNullOrEmpty(path) || !File.Exists(Path.GetFullPath(path))) throw new InvalidOperationException("Missing required source: " + path);
        RequireCleanAsset(path);
        AddOptionalSourceHash(captured, path);
    }

    static void AddOptionalSourceHash(SnapshotData captured, string path) {
        if (string.IsNullOrEmpty(path) || !File.Exists(Path.GetFullPath(path))) return;
        for (int i = 0; i < captured.sourceHashes.Count; i++) if (captured.sourceHashes[i].path == path) return;
        captured.sourceHashes.Add(new HashRecord { path = path, hash = HashFile(path) });
    }
    static void AddPrefabDependencies(SnapshotData captured, string path) {
        AddOptionalSourceHash(captured, path);
        if (string.IsNullOrEmpty(path)) return;
        string[] dependencies = AssetDatabase.GetDependencies(path, true);
        for (int i = 0; i < dependencies.Length; i++) {
            string extension = Path.GetExtension(dependencies[i]).ToLowerInvariant();
            if (extension == ".prefab" || extension == ".mat" || extension == ".asset" || extension == ".physicsmaterial2d") AddRequiredSourceHash(captured, dependencies[i]);
        }
    }
    static void RequireCleanAsset(string path) {
        UnityEngine.Object source = AssetDatabase.LoadMainAssetAtPath(path);
        if (source == null || EditorUtility.IsDirty(source)) throw new InvalidOperationException("Missing or dirty authored source: " + path);
    }
    static void RequireIdentity(Transform transform, string label) { if (transform.localPosition != Vector3.zero || transform.localRotation != Quaternion.identity || transform.localScale != Vector3.one) throw new InvalidOperationException(label + " must be identity."); }
    static bool Uniform(Vector3 value) {
        // Native 2D shapes consume XY scale; faithfully retain the independent authored Z scale.
        return !float.IsNaN(value.z) && !float.IsInfinity(value.z) && Mathf.Abs(value.x - value.y) <= Epsilon &&
            Mathf.Abs(value.x) > Epsilon && Mathf.Abs(value.z) > Epsilon;
    }
    static bool Orthogonal(Matrix4x4 matrix) { Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2); return Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) <= Epsilon && Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) <= Epsilon && Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) <= Epsilon; }
    static bool Same(Vector2 a, Vector2 b) { return Vector2.Distance(a, b) <= Epsilon; }
    static bool Same(Vector3 a, Vector3 b) { return Vector3.Distance(a, b) <= Epsilon; }
    static string HashFile(string path) { string full = Path.GetFullPath(path); if (!File.Exists(full)) throw new InvalidOperationException("Missing source: " + path); using (SHA256 sha = SHA256.Create()) using (FileStream stream = File.OpenRead(full)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty); }
    static int FaceFlags(Bounds bounds, Rect mapBounds) {
        int flags = 0;
        bool spansY = bounds.min.y <= mapBounds.yMin && bounds.max.y >= mapBounds.yMax;
        bool spansX = bounds.min.x <= mapBounds.xMin && bounds.max.x >= mapBounds.xMax;
        if (spansY && Mathf.Abs(bounds.max.x - mapBounds.xMin) < BoundaryEpsilon) flags |= 1;
        if (spansY && Mathf.Abs(bounds.min.x - mapBounds.xMax) < BoundaryEpsilon) flags |= 2;
        if (spansX && Mathf.Abs(bounds.max.y - mapBounds.yMin) < BoundaryEpsilon) flags |= 4;
        if (spansX && Mathf.Abs(bounds.min.y - mapBounds.yMax) < BoundaryEpsilon) flags |= 8;
        return flags;
    }
    static void ValidateBoundaryEvidence(SnapshotData captured) { int faces = 0; for (int i = 0; i < captured.colliders.Count; i++) faces |= captured.colliders[i].faces; captured.boundaryFaces = faces; if (faces != 15) throw new InvalidOperationException("Serialized map-boundary face evidence is incomplete."); }

    sealed class StageHandle : IDisposable { GameObject root; public StageHandle(GameObject ownedRoot) { root = ownedRoot; } public void Dispose() { if (root != null) UnityEngine.Object.DestroyImmediate(root); root = null; } }
    [Serializable] sealed class SnapshotData { public string token, sceneHash, navigationJson; public Vector2 origin; public Rect mapBounds; public List<HashRecord> sourceHashes = new List<HashRecord>(); public List<TransformRecord> transforms = new List<TransformRecord>(); public List<ColliderRecord> colliders = new List<ColliderRecord>(); public List<ExcludedRecord> excluded = new List<ExcludedRecord>(); public bool[] layers; public int boxCount, capsuleCount, outsideCount, boundaryFaces; }
    [Serializable] sealed class HashRecord { public string path, hash; }
    [Serializable] sealed class TransformRecord { public int id, parentId, layer; public string name; public Vector3 position, scale; public Quaternion rotation; public bool active; }
    [Serializable] sealed class ColliderRecord { public int id, transformId, layer, direction, faces; public bool box, capsule; public Vector2 offset, size; public float density, edgeRadius; public Vector3 boundsCenter, boundsSize; }
    [Serializable] sealed class ExcludedRecord { public int id, sourceId; public string reason, type; }
}
