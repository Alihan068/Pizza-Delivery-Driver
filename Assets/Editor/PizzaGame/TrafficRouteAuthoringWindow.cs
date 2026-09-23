using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Asset-only traffic authoring adapter. All route, pool, target, spawn, split, delete and police
/// mutations go through <see cref="MapNavigationEditCommands"/>; Unity Undo wraps the selected
/// <see cref="TrafficMapData"/> asset only. This window never creates scene geometry or a player
/// editor. Its optional bounded preview composes production traffic only in an owned local-physics
/// scene and render target, never in the authoring asset or player scene.
/// </summary>
public sealed class TrafficRouteAuthoringWindow : EditorWindow {
    [Serializable] sealed class Envelope { public MapNavigationDocument document; }
    sealed class PendingRouteEdit { public string edgeIds; public string poolId; public bool loop; public int target; }
    sealed class PendingPoolEdit { public List<VehiclePoolEntry> entries = new List<VehiclePoolEntry>(); }
    sealed class PendingSpawnEdit { public float distance; }
    sealed class PendingPoliceEdit { public string spawnId; }
    sealed class PendingEdgeEdit { public float width; public float speed; }
    [SerializeField] TrafficMapData asset;
    [SerializeField] Vector2 mapOrigin;
    [SerializeField] NpcVehicleProfile[] previewProfiles = Array.Empty<NpcVehicleProfile>();
    [SerializeField] TrafficEditorPreviewSession.ProfilePrefabBinding[] previewPrefabCatalog = Array.Empty<TrafficEditorPreviewSession.ProfilePrefabBinding>();
    [SerializeField] PopulationBudgetData previewBudget;
    [SerializeField] RespawnTimingRules previewRespawnRules;
    [SerializeField] string previewPoolId;
    Vector2 scroll;
    string selectedNode, selectedEdge, selectedSpawn, selectedRoute, selectedPoliceEntry, newFrom, newTo;
    string splitNodeId = "split", splitFirstEdgeId = "edge_a", splitSecondEdgeId = "edge_b";
    float splitDistance;
    MapNavigationDocument draft;
    List<TrafficValidationIssue> issues = new List<TrafficValidationIssue>();
    string statusMessage;
    MessageType statusType;
    [NonSerialized] readonly Dictionary<string, PendingRouteEdit> pendingRoutes = new Dictionary<string, PendingRouteEdit>();
    [NonSerialized] readonly Dictionary<string, PendingPoolEdit> pendingPools = new Dictionary<string, PendingPoolEdit>();
    [NonSerialized] readonly Dictionary<string, PendingSpawnEdit> pendingSpawns = new Dictionary<string, PendingSpawnEdit>();
    [NonSerialized] readonly Dictionary<string, PendingPoliceEdit> pendingPoliceEntries = new Dictionary<string, PendingPoliceEdit>();
    [NonSerialized] readonly Dictionary<string, PendingEdgeEdit> pendingEdges = new Dictionary<string, PendingEdgeEdit>();
    string pendingNewPoliceEntryId, pendingNewPoliceSpawnId;
    TrafficEditorPreviewSession previewSession;

    /// <summary>Opens the traffic route authoring window.</summary>
    [MenuItem("PizzaGame/Traffic/Route Authoring")]
    static void Open() { GetWindow<TrafficRouteAuthoringWindow>("Traffic Routes"); }

    void OnEnable() { SceneView.duringSceneGui += DrawScene; Undo.undoRedoPerformed += Reload; EditorApplication.playModeStateChanged += HandlePlayModeStateChanged; Reload(); }
    void OnDisable() { StopPreview(); SceneView.duringSceneGui -= DrawScene; Undo.undoRedoPerformed -= Reload; EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged; }

    void Reload() {
        StopPreview();
        draft = asset != null ? asset.ResolveDocument() : null;
        issues = draft != null ? TrafficMapValidator.Validate(draft, null) : new List<TrafficValidationIssue>();
        pendingRoutes.Clear();
        pendingPools.Clear();
        pendingSpawns.Clear();
        pendingPoliceEntries.Clear();
        pendingEdges.Clear();
        pendingNewPoliceEntryId = null;
        pendingNewPoliceSpawnId = null;
        Repaint(); SceneView.RepaintAll();
    }

    /// <summary>Stores a detached draft as one Undo transaction without writing scene objects or during Play Mode.</summary>
    public static bool ApplyDraft(TrafficMapData target, MapNavigationDocument document, string undoLabel) {
        if (target == null || document == null || EditorApplication.isPlayingOrWillChangePlaymode) return false;
        Undo.RegisterCompleteObjectUndo(target, undoLabel);
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new Envelope { document = document }), target);
        EditorUtility.SetDirty(target);
        return true;
    }

    /// <summary>Moves a node through the shared command authority and records one asset Undo step.</summary>
    public static bool MoveNode(TrafficMapData target, string id, Vector2 localPosition) {
        if (target == null || EditorApplication.isPlayingOrWillChangePlaymode) return false;
        MapNavigationDocument document = target.ResolveDocument();
        return MapNavigationEditCommands.TryMoveNode(document, id, localPosition.x, localPosition.y, out _) && ApplyDraft(target, document, "Move traffic node");
    }

    void Commit(string label) {
        if (ApplyDraft(asset, draft, label)) { statusMessage = null; Reload(); }
    }

    void Reject(string message) { statusMessage = message; statusType = MessageType.Error; Repaint(); }

    void OnGUI() {
        EditorGUI.BeginChangeCheck();
        asset = (TrafficMapData)EditorGUILayout.ObjectField("Traffic asset", asset, typeof(TrafficMapData), false);
        mapOrigin = EditorGUILayout.Vector2Field("Map origin (world)", mapOrigin);
        if (EditorGUI.EndChangeCheck()) Reload();
        if (draft == null) { EditorGUILayout.HelpBox("Select a Traffic Map Data asset. No scene geometry is generated.", MessageType.Info); return; }

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) {
            if (!string.IsNullOrEmpty(statusMessage)) EditorGUILayout.HelpBox(statusMessage, statusType);
            EditorGUILayout.HelpBox(issues.Count == 0 ? "Structure valid. Profile and physics clearance still require scene validation." : "Invalid navigation: " + issues.Count + " issue(s)", issues.Count == 0 ? MessageType.Info : MessageType.Error);
            DrawPreviewBoundaryMessage();
            DrawValidationIssues();
            DrawPreviewPanel();
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Reload / Validate")) Reload();
                using (new EditorGUI.DisabledScope(issues.Count > 0)) if (GUILayout.Button("Save valid asset")) AssetDatabase.SaveAssetIfDirty(asset);
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawNodes();
            DrawEdges();
            DrawRoutes();
            DrawPools();
            DrawSpawns();
            DrawPoliceEntries();
            EditorGUILayout.EndScrollView();
        }
    }

    void DrawPreviewBoundaryMessage() {
        EditorGUILayout.HelpBox("Preview uses a detached traffic snapshot, owned local 2D physics scene, and a preview-only render target. It never changes assets, Undo history, GameManager, player state, or save data.", MessageType.Info);
    }

    void DrawValidationIssues() {
        foreach (var issue in issues) {
            if (issue == null) continue;
            string label = string.IsNullOrEmpty(issue.referenceId) ? issue.code : issue.code + "  [" + issue.referenceId + "]";
            if (GUILayout.Button(label + ": " + issue.message, EditorStyles.wordWrappedMiniLabel)) FocusReference(issue.referenceId);
        }
        bool missingProfile = false;
        if (draft.vehiclePools != null) foreach (var pool in draft.vehiclePools) {
            if (pool == null || pool.entries == null || pool.entries.Count == 0) { missingProfile = true; continue; }
            foreach (var entry in pool.entries) if (entry == null || string.IsNullOrWhiteSpace(entry.vehicleProfileId)) missingProfile = true;
        }
        if (draft.civilianRoutes != null) foreach (var route in draft.civilianRoutes) {
            if (route == null || string.IsNullOrWhiteSpace(route.vehiclePoolId) || FindPool(route.vehiclePoolId) == null)
                EditorGUILayout.HelpBox("Blocked: route " + (route == null ? "<null>" : route.routeId) + " has no resolvable vehicle pool/profile binding.", MessageType.Error);
        }
        if (missingProfile) EditorGUILayout.HelpBox("Blocked: one or more traffic pools have no resolvable vehicle profile ID. SceneTrafficBinding must resolve every referenced profile before runtime activation.", MessageType.Error);
        else EditorGUILayout.HelpBox("Profile resolution is deferred to the typed scene catalog; this asset-only window does not guess missing profile assets.", MessageType.Info);
    }

    void DrawNodes() {
        EditorGUILayout.LabelField("Nodes", EditorStyles.boldLabel);
        if (draft.nodes != null) foreach (var node in draft.nodes) {
            if (node == null) continue;
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button(node.nodeId, GUILayout.Width(120))) { selectedNode = node.nodeId; SceneView.RepaintAll(); }
                EditorGUI.BeginChangeCheck();
                Vector2 position = EditorGUILayout.Vector2Field(GUIContent.none, new Vector2(node.x, node.y));
                if (EditorGUI.EndChangeCheck()) {
                    if (MapNavigationEditCommands.TryMoveNode(draft, node.nodeId, position.x, position.y, out string issue)) { Commit("Move traffic node"); GUIUtility.ExitGUI(); }
                    else Reject(issue);
                }
            }
        }
        if (GUILayout.Button("Add node at map origin")) {
            if (!MapNavigationEditCommands.TryCreateNode(draft, Guid.NewGuid().ToString("N"), 0f, 0f, out string issue)) Reject(issue); else { Commit("Add traffic node"); GUIUtility.ExitGUI(); }
        }
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selectedNode))) if (GUILayout.Button("Delete selected node")) {
            if (!MapNavigationEditCommands.TryDeleteNode(draft, selectedNode, out string issue)) Reject(issue); else { Commit("Delete traffic node"); GUIUtility.ExitGUI(); }
        }
    }

    void DrawEdges() {
        EditorGUILayout.LabelField("Edges", EditorStyles.boldLabel);
        if (draft.edges != null) foreach (var edge in draft.edges) if (edge != null) {
            if (GUILayout.Button(edge.edgeId + "  " + edge.fromNodeId + " → " + edge.toNodeId)) { selectedEdge = edge.edgeId; SceneView.RepaintAll(); }
            PendingEdgeEdit pending = GetPendingEdge(edge);
            pending.width = EditorGUILayout.FloatField("Usable width", pending.width);
            pending.speed = EditorGUILayout.FloatField("Speed limit", pending.speed);
            if (edge.edgeId == selectedEdge && GUILayout.Button("Apply edge settings")) {
                MapNavigationDocument candidate = Clone(draft);
                if (!MapNavigationEditCommands.TryEditEdgeSettings(candidate, edge.edgeId, pending.width, pending.speed, out string issue)) Reject(issue);
                else { draft = candidate; Commit("Edit traffic edge settings"); GUIUtility.ExitGUI(); }
            }
        }
        newFrom = EditorGUILayout.TextField("From node ID", newFrom);
        newTo = EditorGUILayout.TextField("To node ID", newTo);
        if (GUILayout.Button("Connect nodes")) {
            if (!MapNavigationEditCommands.TryConnectEdge(draft, Guid.NewGuid().ToString("N"), newFrom, newTo, 3f, 3f, out string issue)) Reject(issue); else { Commit("Connect traffic nodes"); GUIUtility.ExitGUI(); }
        }
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selectedEdge))) if (GUILayout.Button("Delete selected edge")) {
            if (!MapNavigationEditCommands.TryDeleteEdge(draft, selectedEdge, out string issue)) Reject(issue); else { Commit("Delete traffic edge"); GUIUtility.ExitGUI(); }
        }
        EditorGUILayout.LabelField("Split selected edge", EditorStyles.miniBoldLabel);
        splitDistance = EditorGUILayout.FloatField("Arc distance", splitDistance);
        splitNodeId = EditorGUILayout.TextField("New node ID", splitNodeId);
        splitFirstEdgeId = EditorGUILayout.TextField("First edge ID", splitFirstEdgeId);
        splitSecondEdgeId = EditorGUILayout.TextField("Second edge ID", splitSecondEdgeId);
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selectedEdge))) if (GUILayout.Button("Split selected edge")) {
            if (!MapNavigationEditCommands.TrySplitEdge(draft, selectedEdge, splitDistance, splitNodeId, splitFirstEdgeId, splitSecondEdgeId, out string issue)) Reject(issue); else { Commit("Split traffic edge"); GUIUtility.ExitGUI(); }
        }
    }

    void DrawPreviewPanel() {
        EditorGUILayout.LabelField("Bounded civilian preview", EditorStyles.boldLabel);
        var poolIds = new List<string>(); if (draft.vehiclePools != null) foreach (var pool in draft.vehiclePools) if (pool != null && !string.IsNullOrWhiteSpace(pool.poolId)) poolIds.Add(pool.poolId);
        if (poolIds.Count == 0) { EditorGUILayout.HelpBox("Create a valid civilian pool before previewing.", MessageType.Error); return; }
        previewPoolId = poolIds[EditorGUILayout.Popup("Approved pool", Mathf.Max(0, poolIds.IndexOf(previewPoolId)), poolIds.ToArray())];
        previewBudget = (PopulationBudgetData)EditorGUILayout.ObjectField("Population budget", previewBudget, typeof(PopulationBudgetData), false);
        previewRespawnRules = (RespawnTimingRules)EditorGUILayout.ObjectField("Respawn rules", previewRespawnRules, typeof(RespawnTimingRules), false);
        for (int i = 0; i < previewProfiles.Length; i++) previewProfiles[i] = (NpcVehicleProfile)EditorGUILayout.ObjectField(previewProfiles[i], typeof(NpcVehicleProfile), false);
        if (GUILayout.Button("Add profile binding")) Array.Resize(ref previewProfiles, previewProfiles.Length + 1);
        for (int i = 0; i < previewPrefabCatalog.Length; i++) { if (previewPrefabCatalog[i] == null) previewPrefabCatalog[i] = new TrafficEditorPreviewSession.ProfilePrefabBinding(); previewPrefabCatalog[i].visualCatalogId = EditorGUILayout.TextField("Visual catalog ID", previewPrefabCatalog[i].visualCatalogId); previewPrefabCatalog[i].prefab = (CivilianVehicleBody)EditorGUILayout.ObjectField("Production body prefab", previewPrefabCatalog[i].prefab, typeof(CivilianVehicleBody), false); }
        if (GUILayout.Button("Add prefab binding")) Array.Resize(ref previewPrefabCatalog, previewPrefabCatalog.Length + 1);
        using (new EditorGUI.DisabledScope(previewSession != null && previewSession.IsRunning)) if (GUILayout.Button("Start bounded real preview")) StartPreview();
        using (new EditorGUI.DisabledScope(previewSession == null || !previewSession.IsRunning)) if (GUILayout.Button("Stop preview")) StopPreview();
        if (previewSession != null && !string.IsNullOrEmpty(previewSession.FailureReason)) EditorGUILayout.HelpBox(previewSession.FailureReason, MessageType.Error);
        if (previewSession != null && previewSession.PreviewTexture != null) { Rect rect = GUILayoutUtility.GetRect(previewSession.PreviewTexture.width, previewSession.PreviewTexture.height, GUILayout.ExpandWidth(true)); EditorGUI.DrawPreviewTexture(rect, previewSession.PreviewTexture, null, ScaleMode.ScaleToFit); Repaint(); }
    }

    void StartPreview() { StopPreview(); previewSession = new TrafficEditorPreviewSession(); var input = new TrafficEditorPreviewSession.Configuration { draft = Clone(draft), profiles = (NpcVehicleProfile[])previewProfiles.Clone(), prefabCatalog = (TrafficEditorPreviewSession.ProfilePrefabBinding[])previewPrefabCatalog.Clone(), budget = previewBudget, respawnRules = previewRespawnRules, limits = new TrafficValidationLimits(), selectedPoolId = previewPoolId, mapOriginWorld = mapOrigin, seed = 0x534E3131 }; if (!previewSession.Start(input, out string issue)) Reject(issue); }
    void StopPreview() { if (previewSession == null) return; previewSession.Stop(); previewSession = null; Repaint(); }
    void HandlePlayModeStateChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode) StopPreview(); }
    PendingEdgeEdit GetPendingEdge(RoadEdgeRecord edge) { if (!pendingEdges.TryGetValue(edge.edgeId, out PendingEdgeEdit pending)) { pending = new PendingEdgeEdit { width = edge.usableWidth, speed = edge.speedLimit }; pendingEdges.Add(edge.edgeId, pending); } return pending; }

    void DrawRoutes() {
        EditorGUILayout.LabelField("Routes", EditorStyles.boldLabel);
        if (draft.civilianRoutes != null) foreach (var route in draft.civilianRoutes) {
            if (route == null) continue;
            if (GUILayout.Button(route.routeId)) { selectedRoute = route.routeId; SceneView.RepaintAll(); }
            PendingRouteEdit pending = GetPendingRoute(route);
            pending.edgeIds = EditorGUILayout.TextField("Ordered edge IDs", pending.edgeIds);
            pending.poolId = EditorGUILayout.TextField("Vehicle pool ID", pending.poolId);
            pending.loop = EditorGUILayout.Toggle("Closed loop", pending.loop);
            pending.target = EditorGUILayout.IntField("Population", pending.target);
            if (GUILayout.Button("Apply route order / pool / target")) {
                MapNavigationDocument candidate = Clone(draft);
                if (!TryApplyRoute(candidate, route.routeId, pending.edgeIds, pending.loop, pending.poolId, pending.target, out string issue)) Reject(issue); else { draft = candidate; Commit("Edit civilian route"); GUIUtility.ExitGUI(); }
            }
        }
        if (GUILayout.Button("Add route from selected edge")) {
            if (string.IsNullOrEmpty(selectedEdge)) Reject("Select an edge before creating a route.");
            else if (!MapNavigationEditCommands.TryAddRoute(draft, Guid.NewGuid().ToString("N"), selectedEdge, false, out string issue)) Reject(issue);
            else { Commit("Add civilian route"); GUIUtility.ExitGUI(); }
        }
    }

    void DrawPools() {
        EditorGUILayout.LabelField("Vehicle pools", EditorStyles.boldLabel);
        if (draft.vehiclePools == null) return;
        foreach (var pool in draft.vehiclePools) {
            if (pool == null) continue;
            EditorGUILayout.LabelField(pool.poolId, EditorStyles.miniBoldLabel);
            PendingPoolEdit pending = GetPendingPool(pool);
            for (int i = 0; i < pending.entries.Count; i++) {
                VehiclePoolEntry entry = pending.entries[i];
                entry.vehicleProfileId = EditorGUILayout.TextField("Profile ID", entry.vehicleProfileId);
                entry.weight = EditorGUILayout.FloatField("Weight", entry.weight);
            }
            if (GUILayout.Button("Apply pool changes")) {
                MapNavigationDocument candidate = Clone(draft);
                if (!MapNavigationEditCommands.TryEditPool(candidate, pool.poolId, pending.entries, out string issue)) Reject(issue); else { draft = candidate; Commit("Edit traffic pool"); GUIUtility.ExitGUI(); }
            }
        }
    }

    void DrawSpawns() {
        EditorGUILayout.LabelField("Spawns", EditorStyles.boldLabel);
        if (draft.spawnPoints != null) foreach (var spawn in draft.spawnPoints) {
            if (spawn == null) continue;
            if (GUILayout.Button(spawn.spawnId + "  " + spawn.role)) { selectedSpawn = spawn.spawnId; SceneView.RepaintAll(); }
            EditorGUILayout.LabelField("Edge / footprint", spawn.edgeId + " / " + spawn.clearanceWidth + " × " + spawn.clearanceLength);
            PendingSpawnEdit pending = GetPendingSpawn(spawn);
            pending.distance = EditorGUILayout.FloatField("Arc distance", pending.distance);
            if (GUILayout.Button("Apply spawn distance")) {
                MapNavigationDocument candidate = Clone(draft);
                if (!MapNavigationEditCommands.TryMoveSpawn(candidate, spawn.spawnId, pending.distance, out string issue)) Reject(issue); else { draft = candidate; Commit("Move traffic spawn"); GUIUtility.ExitGUI(); }
            }
        }
        if (GUILayout.Button("Add civilian spawn on selected edge / route")) {
            if (string.IsNullOrEmpty(selectedEdge) || string.IsNullOrEmpty(selectedRoute)) Reject("Select both an edge and a route before adding a civilian spawn.");
            else if (!MapNavigationEditCommands.TryAddSpawn(draft, new VehicleSpawnRecord { spawnId = Guid.NewGuid().ToString("N"), edgeId = selectedEdge, routeId = selectedRoute, role = VehicleRole.Civilian, clearanceWidth = 1f, clearanceLength = 2f }, out string issue)) Reject(issue);
            else { Commit("Add traffic spawn"); GUIUtility.ExitGUI(); }
        }
    }

    void DrawPoliceEntries() {
        EditorGUILayout.LabelField("Police entries", EditorStyles.boldLabel);
        if (draft.policeEntries != null) foreach (var entry in draft.policeEntries) if (entry != null) {
            if (GUILayout.Button(entry.entryId + "  →  " + entry.spawnId)) { selectedPoliceEntry = entry.entryId; selectedSpawn = entry.spawnId; SceneView.RepaintAll(); }
            PendingPoliceEdit pending = GetPendingPolice(entry);
            pending.spawnId = EditorGUILayout.TextField("Police spawn ID", pending.spawnId);
            if (GUILayout.Button("Apply police entry")) {
                MapNavigationDocument candidate = Clone(draft);
                if (!MapNavigationEditCommands.TryEditPoliceEntry(candidate, entry.entryId, pending.spawnId, new[] { VehicleRole.Police }, out string issue)) Reject(issue); else { draft = candidate; Commit("Edit police entry"); GUIUtility.ExitGUI(); }
            }
        }
        pendingNewPoliceEntryId = EditorGUILayout.TextField("New police entry ID", pendingNewPoliceEntryId);
        pendingNewPoliceSpawnId = EditorGUILayout.TextField("New police spawn ID", pendingNewPoliceSpawnId ?? selectedSpawn);
        if (GUILayout.Button("Add police entry from selected Police spawn")) {
            VehicleSpawnRecord spawn = FindSpawn(pendingNewPoliceSpawnId);
            if (spawn == null || spawn.role != VehicleRole.Police) Reject("Select a spawn authored with the Police role.");
            else if (string.IsNullOrWhiteSpace(pendingNewPoliceEntryId)) Reject("Enter a stable police entry ID before adding it.");
            else {
                MapNavigationDocument candidate = Clone(draft);
                if (!MapNavigationEditCommands.TryAddPoliceEntry(candidate, pendingNewPoliceEntryId, spawn.spawnId, new[] { VehicleRole.Police }, out string issue)) Reject(issue);
                else { draft = candidate; Commit("Add police entry"); GUIUtility.ExitGUI(); }
            }
        }
    }

    PendingRouteEdit GetPendingRoute(CivilianRouteRecord route) {
        if (!pendingRoutes.TryGetValue(route.routeId, out PendingRouteEdit pending)) {
            pending = new PendingRouteEdit {
                edgeIds = string.Join(",", route.edgeIds ?? new List<string>()),
                poolId = route.vehiclePoolId,
                loop = route.loop,
                target = route.targetCount
            };
            pendingRoutes.Add(route.routeId, pending);
        }
        return pending;
    }

    PendingPoolEdit GetPendingPool(VehiclePoolRecord pool) {
        if (!pendingPools.TryGetValue(pool.poolId, out PendingPoolEdit pending)) {
            pending = new PendingPoolEdit();
            if (pool.entries != null) foreach (var source in pool.entries) if (source != null)
                pending.entries.Add(new VehiclePoolEntry { vehicleProfileId = source.vehicleProfileId, weight = source.weight });
            pendingPools.Add(pool.poolId, pending);
        }
        return pending;
    }

    PendingSpawnEdit GetPendingSpawn(VehicleSpawnRecord spawn) {
        if (!pendingSpawns.TryGetValue(spawn.spawnId, out PendingSpawnEdit pending)) {
            pending = new PendingSpawnEdit { distance = spawn.distanceAlongEdge };
            pendingSpawns.Add(spawn.spawnId, pending);
        }
        return pending;
    }

    PendingPoliceEdit GetPendingPolice(PoliceEntryRecord entry) {
        if (!pendingPoliceEntries.TryGetValue(entry.entryId, out PendingPoliceEdit pending)) {
            pending = new PendingPoliceEdit { spawnId = entry.spawnId };
            pendingPoliceEntries.Add(entry.entryId, pending);
        }
        return pending;
    }

    static bool TryApplyRoute(MapNavigationDocument candidate, string routeId, string text, bool loop, string pool, int target, out string issue) {
        var ids = new List<string>(); foreach (string value in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) { string id = value.Trim(); if (!string.IsNullOrEmpty(id)) ids.Add(id); }
        if (!MapNavigationEditCommands.TrySetRouteOrder(candidate, routeId, ids, loop, out issue)) return false;
        if (string.IsNullOrWhiteSpace(pool)) return MapNavigationEditCommands.TryEditRouteTarget(candidate, routeId, target, out issue);
        return MapNavigationEditCommands.TryEditRoutePoolAndTarget(candidate, routeId, pool.Trim(), target, out issue);
    }

    void FocusReference(string referenceId) {
        if (string.IsNullOrEmpty(referenceId) || draft == null) return;
        if (FindNode(referenceId) != null) selectedNode = referenceId;
        else if (FindEdge(referenceId) != null) selectedEdge = referenceId;
        else if (FindSpawn(referenceId) != null) selectedSpawn = referenceId;
        else if (FindRoute(referenceId) != null) selectedRoute = referenceId;
        else if (FindPoliceEntry(referenceId) != null) selectedPoliceEntry = referenceId;
        Selection.activeObject = asset;
        SceneView.RepaintAll(); Repaint();
    }

    void DrawScene(SceneView view) {
        if (draft == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        RoadGraphRuntime graph = new RoadGraphRuntime(draft);
        if (draft.edges != null) foreach (var edge in draft.edges) {
            if (edge == null) continue;
            RoadNodeRecord from = FindNode(edge.fromNodeId), to = FindNode(edge.toNodeId);
            if (from == null || to == null) continue;
            var points = new List<Vector2> { new Vector2(from.x, from.y) }; if (edge.orderedPoints != null) points.AddRange(edge.orderedPoints); points.Add(new Vector2(to.x, to.y));
            string routeId = RouteForEdge(edge.edgeId);
            Handles.color = edge.edgeId == selectedEdge ? Color.yellow : routeId == null ? Color.cyan : RouteColor(routeId);
            for (int i = 1; i < points.Count; i++) {
                Vector3 p = points[i - 1] + mapOrigin, q = points[i] + mapOrigin;
                Vector2 direction = points[i] - points[i - 1];
                Vector3 normal = direction.sqrMagnitude > 0f ? new Vector3(-direction.y, direction.x, 0f).normalized * edge.usableWidth * 0.5f : Vector3.zero;
                Handles.DrawLine(p, q); Handles.DrawLine(p + normal, q + normal); Handles.DrawLine(p - normal, q - normal);
                if (direction.sqrMagnitude > 0f) Handles.ArrowHandleCap(0, (p + q) * 0.5f, Quaternion.LookRotation(direction), 1f, EventType.Repaint);
            }
        }
        if (draft.nodes != null) foreach (var node in draft.nodes) {
            if (node == null) continue;
            Vector3 position = new Vector2(node.x, node.y) + mapOrigin;
            Handles.color = node.nodeId == selectedNode ? Color.yellow : Color.white;
            if (Handles.Button(position, Quaternion.identity, 0.3f, 0.4f, Handles.DotHandleCap)) { selectedNode = node.nodeId; Repaint(); }
            if (node.nodeId == selectedNode) Handles.Label(position, node.nodeId);
        }
        if (draft.spawnPoints != null) foreach (var spawn in draft.spawnPoints) DrawSpawn(graph, spawn);
    }

    void DrawSpawn(RoadGraphRuntime graph, VehicleSpawnRecord spawn) {
        if (spawn == null || !SpawnPoseLocator.TryLocate(graph, spawn, out Vector2 local, out float heading)) return;
        Vector3 position = local + mapOrigin;
        Handles.color = spawn.role == VehicleRole.Police ? new Color(1f, 0.2f, 0.8f) : Color.green;
        Matrix4x4 previousMatrix = Handles.matrix;
        Handles.matrix = Matrix4x4.TRS(position, Quaternion.Euler(0f, 0f, heading), Vector3.one);
        Handles.DrawWireCube(Vector3.zero, new Vector3(spawn.clearanceWidth, spawn.clearanceLength, 0.05f));
        Handles.matrix = previousMatrix;
        if (spawn.spawnId == selectedSpawn) Handles.Label(position, spawn.spawnId + (spawn.role == VehicleRole.Police ? " [Police]" : ""));
        if (spawn.role == VehicleRole.Police) Handles.Label(position + Vector3.up * spawn.clearanceLength, "Police entry spawn");
        if (Handles.Button(position, Quaternion.Euler(0f, 0f, heading), 0.4f, 0.5f, Handles.RectangleHandleCap)) { selectedSpawn = spawn.spawnId; Repaint(); }
    }

    string RouteForEdge(string edgeId) {
        if (draft.civilianRoutes != null) foreach (var route in draft.civilianRoutes) if (route != null && route.edgeIds != null && route.edgeIds.Contains(edgeId)) return route.routeId;
        return null;
    }

    static Color RouteColor(string routeId) {
        uint hash = 2166136261u; foreach (char value in routeId) { hash ^= value; hash *= 16777619u; }
        return Color.HSVToRGB((hash % 1000u) / 1000f, 0.75f, 0.95f);
    }

    MapNavigationDocument Clone(MapNavigationDocument source) => JsonUtility.FromJson<MapNavigationDocument>(JsonUtility.ToJson(source));
    RoadNodeRecord FindNode(string id) => draft.nodes == null ? null : draft.nodes.Find(item => item != null && item.nodeId == id);
    RoadEdgeRecord FindEdge(string id) => draft.edges == null ? null : draft.edges.Find(item => item != null && item.edgeId == id);
    VehicleSpawnRecord FindSpawn(string id) => draft.spawnPoints == null ? null : draft.spawnPoints.Find(item => item != null && item.spawnId == id);
    CivilianRouteRecord FindRoute(string id) => draft.civilianRoutes == null ? null : draft.civilianRoutes.Find(item => item != null && item.routeId == id);
    VehiclePoolRecord FindPool(string id) => draft.vehiclePools == null ? null : draft.vehiclePools.Find(item => item != null && item.poolId == id);
    PoliceEntryRecord FindPoliceEntry(string id) => draft.policeEntries == null ? null : draft.policeEntries.Find(item => item != null && item.entryId == id);
}
