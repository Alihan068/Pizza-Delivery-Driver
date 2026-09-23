using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Bounded canonical codec for the portable navigation DTO.</summary>
public static class NavigationDocumentCodec {
    /// <summary>Only schema version currently understood by this codec.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Encodes and hashes a validated navigation document using deterministic JSON.</summary>
    public static bool TryEncode(MapNavigationDocument document, TrafficValidationLimits limits, out string json, out string hash, out string reason) {
        json = null; hash = null; reason = null;
        limits = limits ?? new TrafficValidationLimits();
        var issues = TrafficMapValidator.Validate(document, limits);
        if (issues.Count != 0) { reason = issues[0].message; return false; }
        if (!TryCheckDocumentBounds(document, limits, out reason)) return false;
        var builder = new StringBuilder();
        try { WriteDocument(builder, document); }
        catch (Exception exception) { reason = "Navigation contains an invalid numeric value: " + exception.Message; return false; }
        json = builder.ToString();
        if (Encoding.UTF8.GetByteCount(json) > limits.maxPackageBytes) { reason = "Canonical document exceeds the package byte limit."; json = null; return false; }
        hash = ComputeHash(json);
        return true;
    }

    /// <summary>Decodes bounded JSON with duplicate-key rejection and explicit Unity-value conversion.</summary>
    public static bool TryDecode(byte[] utf8, TrafficValidationLimits limits, out MapNavigationDocument document, out string reason) {
        document = null; reason = null; limits = limits ?? new TrafficValidationLimits();
        if (utf8 == null || utf8.Length == 0) { reason = "Navigation input is empty."; return false; }
        if (utf8.Length > limits.maxPackageBytes) { reason = "Navigation input exceeds the package byte limit."; return false; }
        try {
            var encoding = new UTF8Encoding(false, true);
            string json = encoding.GetString(utf8);
            if (!ValidateReaderBounds(json, limits, out reason)) return false;
            using (var stringReader = new System.IO.StringReader(json))
            using (var reader = new JsonTextReader(stringReader) { MaxDepth = limits.maxJsonDepth, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double }) {
                var settings = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Ignore, LineInfoHandling = LineInfoHandling.Load };
                JObject root = JObject.Load(reader, settings);
                if (reader.Read()) { reason = "Navigation JSON contains trailing tokens."; return false; }
                if (!ValidateTokenBounds(root, limits, out reason)) return false;
                document = ReadDocument(root, out reason);
            }
        } catch (Exception exception) {
            reason = "Navigation JSON is malformed or truncated: " + exception.Message;
            document = null;
            return false;
        }
        if (document == null) { reason = "Navigation document is missing."; return false; }
        if (document.schemaVersion != CurrentSchemaVersion) { reason = "Unsupported navigation schema version."; document = null; return false; }
        if (!TryCheckDocumentBounds(document, limits, out reason)) { document = null; return false; }
        var issues = TrafficMapValidator.Validate(document, limits);
        if (issues.Count != 0) { reason = issues[0].message; document = null; return false; }
        return true;
    }

    /// <summary>Computes the canonical hash used by the navigation module descriptor.</summary>
    public static string ComputeHash(string canonicalJson) {
        using (var sha = SHA256.Create()) {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalJson ?? string.Empty));
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    static bool TryCheckDocumentBounds(MapNavigationDocument d, TrafficValidationLimits l, out string reason) {
        reason = null;
        if (d.schemaVersion != CurrentSchemaVersion) { reason = "Unsupported navigation schema version."; return false; }
        if (string.IsNullOrEmpty(d.mapId) || string.IsNullOrEmpty(d.documentId) || string.IsNullOrEmpty(d.coordinateConvention)) { reason = "Navigation identity fields are missing."; return false; }
        int total = Count(d.nodes) + Count(d.edges) + Count(d.junctions) + Count(d.vehiclePools) + Count(d.civilianRoutes) + Count(d.spawnPoints) + Count(d.policeEntries) + Count(d.difficultyProfileBindings);
        if (total > l.maxTotalRecords) { reason = "Navigation aggregate record limit exceeded."; return false; }
        if (!CheckString(d.mapId, l) || !CheckString(d.documentId, l) || !CheckString(d.coordinateConvention, l)) { reason = "Navigation string limit exceeded."; return false; }
        if (d.nodes == null || d.edges == null || d.junctions == null || d.vehiclePools == null || d.civilianRoutes == null || d.spawnPoints == null || d.policeEntries == null || d.difficultyProfileBindings == null || d.profileDependencies == null || d.noSpawnRegions == null) { reason = "Navigation collections are missing."; return false; }
        foreach (var region in d.noSpawnRegions) if (region == null || !CheckOptionalString(region.reason, l) || !CheckOptionalString(region.anchorId, l) || !Finite(region.area.x) || !Finite(region.area.y) || !Finite(region.area.width) || !Finite(region.area.height)) { reason = "Navigation no-spawn region is missing, non-finite or oversized."; return false; }
        foreach (var n in d.nodes) if (n == null || !CheckString(n.nodeId, l)) { reason = "Navigation node data is missing or oversized."; return false; }
        if (!Finite(d.localBounds.x) || !Finite(d.localBounds.y) || !Finite(d.localBounds.width) || !Finite(d.localBounds.height)) { reason = "Navigation bounds contain a non-finite value."; return false; }
        foreach (var n in d.nodes) if (n == null || !Finite(n.x) || !Finite(n.y)) { reason = "Navigation node contains a non-finite value."; return false; }
        foreach (var e in d.edges) if (e == null || !CheckString(e.edgeId, l) || !CheckString(e.fromNodeId, l) || !CheckString(e.toNodeId, l) || e.orderedPoints == null || e.allowedRoles == null || e.orderedPoints.Count > l.maxNestedCollectionItems || e.allowedRoles.Count > l.maxNestedCollectionItems || !Finite(e.usableWidth) || !Finite(e.speedLimit) || !CheckOptionalString(e.startJunctionId, l) || !CheckOptionalString(e.endJunctionId, l)) { reason = "Navigation edge data is missing, non-finite or oversized."; return false; }
        foreach (var p in d.edges.SelectMany(x => x.orderedPoints)) if (!Finite(p.x) || !Finite(p.y)) { reason = "Navigation point contains a non-finite value."; return false; }
        foreach (var j in d.junctions) if (j == null || !CheckString(j.junctionId, l) || j.allowedTransitions == null || j.allowedTransitions.Count > l.maxNestedCollectionItems || !Finite(j.conflictZone.x) || !Finite(j.conflictZone.y) || !Finite(j.conflictZone.width) || !Finite(j.conflictZone.height)) { reason = "Navigation junction data is missing, non-finite or oversized."; return false; }
        foreach (var p in d.junctions.SelectMany(x => x.allowedTransitions)) if (p == null || !CheckString(p.fromEdgeId, l) || !CheckString(p.toEdgeId, l)) { reason = "Navigation junction transition data is missing or oversized."; return false; }
        foreach (var p in d.vehiclePools) if (p == null || !CheckString(p.poolId, l) || p.entries == null || p.entries.Count > l.maxNestedCollectionItems) { reason = "Navigation vehicle pool data is missing or oversized."; return false; }
        foreach (var p in d.vehiclePools.SelectMany(x => x.entries)) if (p == null || !CheckString(p.vehicleProfileId, l) || !Finite(p.weight)) { reason = "Navigation vehicle pool entry is missing or non-finite."; return false; }
        foreach (var r in d.civilianRoutes) if (r == null || !CheckString(r.routeId, l) || !CheckString(r.vehiclePoolId, l) || !CheckOptionalString(r.respawnProfileId, l) || r.edgeIds == null || r.edgeIds.Count > l.maxNestedCollectionItems) { reason = "Navigation route data is missing or oversized."; return false; }
        foreach (var r in d.civilianRoutes) foreach (var id in r.edgeIds) if (!CheckString(id, l)) { reason = "Navigation route edge id is oversized."; return false; }
        foreach (var p in d.spawnPoints) if (p == null || !CheckString(p.spawnId, l) || !CheckString(p.edgeId, l) || !CheckOptionalString(p.routeId, l) || !Finite(p.distanceAlongEdge) || !Finite(p.clearanceWidth) || !Finite(p.clearanceLength)) { reason = "Navigation spawn data is missing or non-finite."; return false; }
        foreach (var p in d.policeEntries) if (p == null || !CheckString(p.entryId, l) || !CheckString(p.spawnId, l) || p.allowedRoles == null || p.allowedRoles.Count > l.maxNestedCollectionItems) { reason = "Navigation police entry data is missing or oversized."; return false; }
        foreach (var p in d.difficultyProfileBindings) if (p == null || !CheckString(p.difficultyId, l) || !CheckString(p.civilianPopulationProfileId, l) || !CheckString(p.policeDirectorProfileId, l)) { reason = "Navigation difficulty binding is missing or oversized."; return false; }
        foreach (var p in d.profileDependencies) if (!CheckString(p, l)) { reason = "Navigation dependency string is oversized."; return false; }
        return true;
    }

    static int Count<T>(List<T> list) => list == null ? 0 : list.Count;
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool CheckString(string value, TrafficValidationLimits l) => value != null && value.Length <= l.maxStringLength;
    static bool CheckOptionalString(string value, TrafficValidationLimits l) => value == null || value.Length <= l.maxStringLength;
    static string F(float value) { if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Non-finite navigation number."); return value.ToString("R", CultureInfo.InvariantCulture); }
    static void S(StringBuilder b, string value) { b.Append('"'); if (value != null) foreach (char c in value) { switch (c) { case '\\': b.Append("\\\\"); break; case '"': b.Append("\\\""); break; case '\n': b.Append("\\n"); break; case '\r': b.Append("\\r"); break; case '\t': b.Append("\\t"); break; default: if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); break; } } b.Append('"'); }
    static void K(StringBuilder b, string key) { S(b, key); b.Append(':'); }
    static void BeginList(StringBuilder b) { b.Append('['); }
    static void EndList(StringBuilder b) { b.Append(']'); }
    static void Sep(StringBuilder b, bool first) { if (!first) b.Append(','); }
    static void WriteDocument(StringBuilder b, MapNavigationDocument d) {
        var noSpawnRegions = d.noSpawnRegions.OrderBy(x => x.anchorId, StringComparer.Ordinal).ThenBy(x => x.reason, StringComparer.Ordinal).ThenBy(x => x.area.x).ThenBy(x => x.area.y).ThenBy(x => x.area.width).ThenBy(x => x.area.height).ToList();
        b.Append('{'); K(b,"schemaVersion"); b.Append(d.schemaVersion); b.Append(','); K(b,"mapId"); S(b,d.mapId); b.Append(','); K(b,"documentId"); S(b,d.documentId); b.Append(','); K(b,"coordinateConvention"); S(b,d.coordinateConvention); b.Append(','); K(b,"localBounds"); RectValue(b,d.localBounds); b.Append(','); K(b,"noSpawnRegions"); ListValue(b,noSpawnRegions, NoSpawn); b.Append(','); K(b,"nodes"); ListValue(b,d.nodes.OrderBy(x=>x.nodeId,StringComparer.Ordinal).ToList(), Node); b.Append(','); K(b,"edges"); ListValue(b,d.edges.OrderBy(x=>x.edgeId,StringComparer.Ordinal).ToList(), Edge); b.Append(','); K(b,"junctions"); ListValue(b,d.junctions.OrderBy(x=>x.junctionId,StringComparer.Ordinal).ToList(), Junction); b.Append(','); K(b,"vehiclePools"); ListValue(b,d.vehiclePools.OrderBy(x=>x.poolId,StringComparer.Ordinal).ToList(), Pool); b.Append(','); K(b,"civilianRoutes"); ListValue(b,d.civilianRoutes.OrderBy(x=>x.routeId,StringComparer.Ordinal).ToList(), Route); b.Append(','); K(b,"spawnPoints"); ListValue(b,d.spawnPoints.OrderBy(x=>x.spawnId,StringComparer.Ordinal).ToList(), Spawn); b.Append(','); K(b,"policeEntries"); ListValue(b,d.policeEntries.OrderBy(x=>x.entryId,StringComparer.Ordinal).ToList(), Police); b.Append(','); K(b,"difficultyProfileBindings"); ListValue(b,d.difficultyProfileBindings.OrderBy(x=>x.difficultyId,StringComparer.Ordinal).ToList(), Difficulty); b.Append(','); K(b,"profileDependencies"); Strings(b,d.profileDependencies.OrderBy(x=>x,StringComparer.Ordinal).ToList()); b.Append('}');
    }
    delegate void Writer<T>(StringBuilder b,T value);
    static void ListValue<T>(StringBuilder b,List<T> values,Writer<T> writer){BeginList(b);bool first=true;foreach(var v in values){Sep(b,first);first=false;writer(b,v);}EndList(b);}
    static void Strings(StringBuilder b,List<string> values){BeginList(b);for(int i=0;i<values.Count;i++){if(i>0)b.Append(',');S(b,values[i]);}EndList(b);}
    static void RectValue(StringBuilder b,Rect v){b.Append('{');K(b,"x");b.Append(F(v.x));b.Append(',');K(b,"y");b.Append(F(v.y));b.Append(',');K(b,"width");b.Append(F(v.width));b.Append(',');K(b,"height");b.Append(F(v.height));b.Append('}');}
    static void Vec(StringBuilder b,Vector2 v){b.Append('{');K(b,"x");b.Append(F(v.x));b.Append(',');K(b,"y");b.Append(F(v.y));b.Append('}');}
    static void NoSpawn(StringBuilder b,NoSpawnRegion v){b.Append('{');K(b,"area");RectValue(b,v.area);b.Append(',');K(b,"reason");S(b,v.reason);b.Append(',');K(b,"anchorId");S(b,v.anchorId);b.Append('}');}
    static void Node(StringBuilder b,RoadNodeRecord v){b.Append('{');K(b,"nodeId");S(b,v.nodeId);b.Append(',');K(b,"x");b.Append(F(v.x));b.Append(',');K(b,"y");b.Append(F(v.y));b.Append('}');}
    static void Roles(StringBuilder b,List<VehicleRole> values){BeginList(b);var a=values.OrderBy(x=>(int)x).ToList();for(int i=0;i<a.Count;i++){if(i>0)b.Append(',');b.Append((int)a[i]);}EndList(b);}
    static void Edge(StringBuilder b,RoadEdgeRecord v){b.Append('{');K(b,"edgeId");S(b,v.edgeId);b.Append(',');K(b,"fromNodeId");S(b,v.fromNodeId);b.Append(',');K(b,"toNodeId");S(b,v.toNodeId);b.Append(',');K(b,"orderedPoints");ListValue(b,v.orderedPoints,Vec);b.Append(',');K(b,"usableWidth");b.Append(F(v.usableWidth));b.Append(',');K(b,"speedLimit");b.Append(F(v.speedLimit));b.Append(',');K(b,"allowedRoles");Roles(b,v.allowedRoles);b.Append(',');K(b,"startJunctionId");S(b,v.startJunctionId);b.Append(',');K(b,"endJunctionId");S(b,v.endJunctionId);b.Append('}');}
    static void Transition(StringBuilder b,JunctionTransition v){b.Append('{');K(b,"fromEdgeId");S(b,v.fromEdgeId);b.Append(',');K(b,"toEdgeId");S(b,v.toEdgeId);b.Append(',');K(b,"priority");b.Append(v.priority);b.Append('}');}
    static void Junction(StringBuilder b,JunctionRecord v){b.Append('{');K(b,"junctionId");S(b,v.junctionId);b.Append(',');K(b,"conflictZone");RectValue(b,v.conflictZone);b.Append(',');K(b,"allowedTransitions");ListValue(b,v.allowedTransitions.OrderBy(x=>x==null?string.Empty:x.fromEdgeId,StringComparer.Ordinal).ThenBy(x=>x==null?string.Empty:x.toEdgeId,StringComparer.Ordinal).ThenBy(x=>x==null?0:x.priority).ToList(),Transition);b.Append('}');}
    static void PoolEntry(StringBuilder b,VehiclePoolEntry v){b.Append('{');K(b,"vehicleProfileId");S(b,v.vehicleProfileId);b.Append(',');K(b,"weight");b.Append(F(v.weight));b.Append('}');}
    static void Pool(StringBuilder b,VehiclePoolRecord v){b.Append('{');K(b,"poolId");S(b,v.poolId);b.Append(',');K(b,"entries");ListValue(b,v.entries.OrderBy(x=>x==null?string.Empty:x.vehicleProfileId,StringComparer.Ordinal).ToList(),PoolEntry);b.Append('}');}
    static void Route(StringBuilder b,CivilianRouteRecord v){b.Append('{');K(b,"routeId");S(b,v.routeId);b.Append(',');K(b,"edgeIds");Strings(b,v.edgeIds);b.Append(',');K(b,"loop");b.Append(v.loop?"true":"false");b.Append(',');K(b,"vehiclePoolId");S(b,v.vehiclePoolId);b.Append(',');K(b,"targetCount");b.Append(v.targetCount);b.Append(',');K(b,"respawnProfileId");S(b,v.respawnProfileId);b.Append('}');}
    static void Spawn(StringBuilder b,VehicleSpawnRecord v){b.Append('{');K(b,"spawnId");S(b,v.spawnId);b.Append(',');K(b,"edgeId");S(b,v.edgeId);b.Append(',');K(b,"distanceAlongEdge");b.Append(F(v.distanceAlongEdge));b.Append(',');K(b,"routeId");S(b,v.routeId);b.Append(',');K(b,"role");b.Append((int)v.role);b.Append(',');K(b,"clearanceWidth");b.Append(F(v.clearanceWidth));b.Append(',');K(b,"clearanceLength");b.Append(F(v.clearanceLength));b.Append('}');}
    static void Police(StringBuilder b,PoliceEntryRecord v){b.Append('{');K(b,"entryId");S(b,v.entryId);b.Append(',');K(b,"spawnId");S(b,v.spawnId);b.Append(',');K(b,"allowedRoles");Roles(b,v.allowedRoles);b.Append('}');}
    static void Difficulty(StringBuilder b,DifficultyTrafficBinding v){b.Append('{');K(b,"difficultyId");S(b,v.difficultyId);b.Append(',');K(b,"civilianPopulationProfileId");S(b,v.civilianPopulationProfileId);b.Append(',');K(b,"policeDirectorProfileId");S(b,v.policeDirectorProfileId);b.Append('}');}

    static bool ValidateReaderBounds(string json, TrafficValidationLimits limits, out string reason) {
        reason = null;
        using (var stringReader = new System.IO.StringReader(json))
        using (var reader = new JsonTextReader(stringReader) { MaxDepth = limits.maxJsonDepth, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double }) {
            int tokenCount = 0;
            while (reader.Read()) {
                tokenCount++;
                if (tokenCount > limits.maxTotalJsonTokens) { reason = "Navigation JSON token limit exceeded."; return false; }
                if (reader.TokenType == JsonToken.Comment) { reason = "Navigation JSON comments are not allowed."; return false; }
                if (reader.TokenType == JsonToken.String && reader.Value is string value && value.Length > limits.maxStringLength) { reason = "Navigation JSON string limit exceeded."; return false; }
            }
        }
        return true;
    }

    static bool ValidateTokenBounds(JContainer token, TrafficValidationLimits limits, out string reason) {
        reason = null;
        int totalRecords = 0;
        foreach (var current in token.DescendantsAndSelf()) {
            if (current is JArray array) {
                if (array.Count > limits.maxNestedCollectionItems) { reason = "Navigation nested collection limit exceeded."; return false; }
            }
            if (current is JObject) {
                totalRecords++;
                if (totalRecords > limits.maxTotalRecords) { reason = "Navigation aggregate JSON object limit exceeded."; return false; }
            }
        }
        return true;
    }

    static MapNavigationDocument ReadDocument(JObject root, out string reason) {
        reason = null;
        string[] requiredFields = { "schemaVersion", "mapId", "documentId", "coordinateConvention", "localBounds", "noSpawnRegions", "nodes", "edges", "junctions", "vehiclePools", "civilianRoutes", "spawnPoints", "policeEntries", "difficultyProfileBindings", "profileDependencies" };
        foreach (string field in requiredFields) {
            if (!root.TryGetValue(field, out var token) || token == null || token.Type == JTokenType.Null) {
                reason = "Required navigation field is missing: " + field;
                return null;
            }
        }
        if (root["schemaVersion"].Type != JTokenType.Integer || root["mapId"].Type != JTokenType.String || root["documentId"].Type != JTokenType.String || root["coordinateConvention"].Type != JTokenType.String || root["localBounds"].Type != JTokenType.Object) {
            reason = "Required navigation field has the wrong JSON type.";
            return null;
        }
        string[] requiredArrays = { "noSpawnRegions", "nodes", "edges", "junctions", "vehiclePools", "civilianRoutes", "spawnPoints", "policeEntries", "difficultyProfileBindings", "profileDependencies" };
        foreach (string field in requiredArrays) if (root[field].Type != JTokenType.Array) {
            reason = "Required navigation collection has the wrong JSON type: " + field;
            return null;
        }
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "schemaVersion", "mapId", "documentId", "coordinateConvention", "localBounds", "noSpawnRegions", "nodes", "edges", "junctions", "vehiclePools", "civilianRoutes", "spawnPoints", "policeEntries", "difficultyProfileBindings", "profileDependencies" };
        foreach (var property in root.Properties()) if (!allowed.Contains(property.Name)) { reason = "Unknown navigation top-level property: " + property.Name; return null; }
        var document = new MapNavigationDocument {
            schemaVersion = Int(root, "schemaVersion", 1),
            mapId = String(root, "mapId", null),
            documentId = String(root, "documentId", null),
            coordinateConvention = String(root, "coordinateConvention", "MapLocalUnits_XRight_YUp_YForward_HeadingCCWFromY"),
            localBounds = Rect(root["localBounds"])
        };
        ReadList(root["noSpawnRegions"] as JArray, document.noSpawnRegions, ReadNoSpawn);
        ReadList(root["nodes"] as JArray, document.nodes, ReadNode);
        ReadList(root["edges"] as JArray, document.edges, ReadEdge);
        ReadList(root["junctions"] as JArray, document.junctions, ReadJunction);
        ReadList(root["vehiclePools"] as JArray, document.vehiclePools, ReadPool);
        ReadList(root["civilianRoutes"] as JArray, document.civilianRoutes, ReadRoute);
        ReadList(root["spawnPoints"] as JArray, document.spawnPoints, ReadSpawn);
        ReadList(root["policeEntries"] as JArray, document.policeEntries, ReadPolice);
        ReadList(root["difficultyProfileBindings"] as JArray, document.difficultyProfileBindings, ReadDifficulty);
        if (root["profileDependencies"] is JArray dependencies) foreach (var dependency in dependencies) document.profileDependencies.Add(dependency.Type == JTokenType.String ? (string)dependency : null);
        return document;
    }

    delegate T Reader<T>(JObject value);
    static void ReadList<T>(JArray values, List<T> target, Reader<T> reader) { if (values == null) return; foreach (var value in values) target.Add(value as JObject == null ? default(T) : reader((JObject)value)); }
    static string String(JObject value, string name, string fallback) { return value[name] == null || value[name].Type == JTokenType.Null ? fallback : (string)value[name]; }
    static int Int(JObject value, string name, int fallback) { return value[name] == null || value[name].Type == JTokenType.Null ? fallback : (int)value[name]; }
    static float Float(JToken value) { return value == null || value.Type == JTokenType.Null ? 0f : (float)(double)value; }
    static Vector2 Vector(JToken value) { var objectValue = value as JObject; return objectValue == null ? Vector2.zero : new Vector2(Float(objectValue["x"]), Float(objectValue["y"])); }
    static Rect Rect(JToken value) { var objectValue = value as JObject; return objectValue == null ? default(Rect) : new Rect(Float(objectValue["x"]), Float(objectValue["y"]), Float(objectValue["width"]), Float(objectValue["height"])); }
    static List<string> Strings(JToken value) { var result = new List<string>(); if (value is JArray array) foreach (var item in array) result.Add(item.Type == JTokenType.String ? (string)item : null); return result; }
    static List<VehicleRole> Roles(JToken value) { var result = new List<VehicleRole>(); if (value is JArray array) foreach (var item in array) result.Add((VehicleRole)(int)item); return result; }
    static NoSpawnRegion ReadNoSpawn(JObject value) => new NoSpawnRegion { area = Rect(value["area"]), reason = String(value, "reason", null), anchorId = String(value, "anchorId", null) };
    static RoadNodeRecord ReadNode(JObject value) => new RoadNodeRecord { nodeId = String(value, "nodeId", null), x = Float(value["x"]), y = Float(value["y"]) };
    static RoadEdgeRecord ReadEdge(JObject value) => new RoadEdgeRecord { edgeId = String(value, "edgeId", null), fromNodeId = String(value, "fromNodeId", null), toNodeId = String(value, "toNodeId", null), orderedPoints = Vectors(value["orderedPoints"]), usableWidth = Float(value["usableWidth"]), speedLimit = Float(value["speedLimit"]), allowedRoles = Roles(value["allowedRoles"]), startJunctionId = String(value, "startJunctionId", string.Empty), endJunctionId = String(value, "endJunctionId", string.Empty) };
    static List<Vector2> Vectors(JToken value) { var result = new List<Vector2>(); if (value is JArray array) foreach (var item in array) result.Add(Vector(item)); return result; }
    static JunctionTransition ReadTransition(JObject value) => new JunctionTransition { fromEdgeId = String(value, "fromEdgeId", null), toEdgeId = String(value, "toEdgeId", null), priority = Int(value, "priority", 0) };
    static JunctionRecord ReadJunction(JObject value) { var result = new JunctionRecord { junctionId = String(value, "junctionId", null), conflictZone = Rect(value["conflictZone"]) }; ReadList(value["allowedTransitions"] as JArray, result.allowedTransitions, ReadTransition); return result; }
    static VehiclePoolEntry ReadPoolEntry(JObject value) => new VehiclePoolEntry { vehicleProfileId = String(value, "vehicleProfileId", null), weight = Float(value["weight"]) };
    static VehiclePoolRecord ReadPool(JObject value) { var result = new VehiclePoolRecord { poolId = String(value, "poolId", null) }; ReadList(value["entries"] as JArray, result.entries, ReadPoolEntry); return result; }
    static CivilianRouteRecord ReadRoute(JObject value) { var result = new CivilianRouteRecord { routeId = String(value, "routeId", null), edgeIds = Strings(value["edgeIds"]), loop = value["loop"] != null && (bool)value["loop"], vehiclePoolId = String(value, "vehiclePoolId", null), targetCount = Int(value, "targetCount", 0), respawnProfileId = String(value, "respawnProfileId", string.Empty) }; return result; }
    static VehicleSpawnRecord ReadSpawn(JObject value) => new VehicleSpawnRecord { spawnId = String(value, "spawnId", null), edgeId = String(value, "edgeId", null), distanceAlongEdge = Float(value["distanceAlongEdge"]), routeId = String(value, "routeId", string.Empty), role = (VehicleRole)Int(value, "role", 0), clearanceWidth = Float(value["clearanceWidth"]), clearanceLength = Float(value["clearanceLength"]) };
    static PoliceEntryRecord ReadPolice(JObject value) => new PoliceEntryRecord { entryId = String(value, "entryId", null), spawnId = String(value, "spawnId", null), allowedRoles = Roles(value["allowedRoles"]) };
    static DifficultyTrafficBinding ReadDifficulty(JObject value) => new DifficultyTrafficBinding { difficultyId = String(value, "difficultyId", null), civilianPopulationProfileId = String(value, "civilianPopulationProfileId", null), policeDirectorProfileId = String(value, "policeDirectorProfileId", string.Empty) };
}
