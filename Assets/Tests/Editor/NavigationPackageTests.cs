using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

/// <summary>Focused EditMode contract tests for the S11 navigation module boundary.</summary>
public sealed class NavigationPackageTests {
    [Test]
    /// <summary>Missing profile fingerprints and built-in shadowing are rejected by the package adapter.</summary>
    public void ProfileCatalogRejectsMissingDependencyAndBuiltinShadowing() {
        var builtIn = ScriptableObject.CreateInstance<NpcVehicleProfile>(); builtIn.vehicleProfileId = "builtin:car";
        var custom = ScriptableObject.CreateInstance<NpcVehicleProfile>(); custom.vehicleProfileId = "builtin:car";
        var reservedNamespaceCustom = ScriptableObject.CreateInstance<NpcVehicleProfile>(); reservedNamespaceCustom.vehicleProfileId = "builtin:other";
        try {
            var catalog = new PackageTrafficProfileCatalog(new[] { builtIn }, new[] { custom, reservedNamespaceCustom });
            Assert.IsTrue(catalog.HasConflicts);
            Assert.IsNull(catalog.ResolveVehicleProfile("builtin:car"));
            Assert.IsNull(catalog.ResolveVehicleProfile("builtin:other"));
            var nullDependencyCatalog = new PackageTrafficProfileCatalog(null, null, new NavigationDependency[] { null });
            Assert.IsTrue(nullDependencyCatalog.HasConflicts);
            var document = Fixture(); document.profileDependencies.Add("builtin:car");
            var manifest = new NavigationModuleManifest { mapId = document.mapId, documentId = document.documentId };
            Assert.IsFalse(catalog.TryValidateDependencies(document, manifest, out _));
        } finally {
            Object.DestroyImmediate(builtIn); Object.DestroyImmediate(custom); Object.DestroyImmediate(reservedNamespaceCustom);
        }
    }

    [Test]
    /// <summary>Stable-ID collection order is normalized while route and polyline order remains semantic.</summary>
    public void CanonicalEncodingSortsStableCollectionsButPreservesRouteAndPointOrder() {
        var first = Fixture(); var second = Fixture();
        second.nodes.Reverse(); second.edges.Reverse(); second.civilianRoutes[0].edgeIds.Reverse(); second.edges.Find(x => x.edgeId == "ab").orderedPoints.Reverse();
        Assert.IsTrue(NavigationDocumentCodec.TryEncode(first, null, out _, out var firstHash, out var firstReason), firstReason);
        Assert.IsTrue(NavigationDocumentCodec.TryEncode(second, null, out _, out var secondHash, out var secondReason), secondReason);
        Assert.AreNotEqual(firstHash, secondHash);
        second.civilianRoutes[0].edgeIds.Reverse(); second.edges.Find(x => x.edgeId == "ab").orderedPoints.Reverse();
        Assert.IsTrue(NavigationDocumentCodec.TryEncode(second, null, out _, out secondHash, out secondReason), secondReason);
        Assert.AreEqual(firstHash, secondHash);
    }

    [Test]
    public void ValueChangeChangesHashAndRoundTripIsDetached() {
        var source = Fixture();
        Assert.IsTrue(NavigationDocumentCodec.TryEncode(source, null, out var json, out var hash, out var reason), reason);
        Assert.IsTrue(NavigationDocumentCodec.TryDecode(Encoding.UTF8.GetBytes(json), null, out var decoded, out reason), reason);
        Assert.AreEqual(hash, NavigationDocumentCodec.ComputeHash(json));
        Assert.AreEqual(source.localBounds.x, decoded.localBounds.x);
        Assert.AreEqual(source.localBounds.width, decoded.localBounds.width);
        Assert.AreEqual(source.noSpawnRegions[0].area.y, decoded.noSpawnRegions[0].area.y);
        Assert.AreEqual(source.junctions[0].conflictZone.height, decoded.junctions[0].conflictZone.height);
        Assert.AreEqual(string.Empty, decoded.civilianRoutes[0].respawnProfileId);
        decoded.nodes[0].x += 1f;
        Assert.AreNotEqual(source.nodes[0].x, decoded.nodes[0].x);
        Assert.IsTrue(NavigationDocumentCodec.TryEncode(decoded, null, out _, out var changedHash, out reason), reason);
        Assert.AreNotEqual(hash, changedHash);
    }

    [Test]
    public void UnsupportedTruncatedAndOversizedInputIsRejected() {
        var source = Fixture();
        Assert.IsTrue(NavigationDocumentCodec.TryEncode(source, null, out var json, out _, out var reason), reason);
        var unsupported = json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2");
        Assert.IsFalse(NavigationDocumentCodec.TryDecode(Encoding.UTF8.GetBytes(unsupported), null, out _, out _));
        Assert.IsFalse(NavigationDocumentCodec.TryDecode(Encoding.UTF8.GetBytes(json.Substring(0, json.Length - 2)), null, out _, out _));
        Assert.IsFalse(NavigationDocumentCodec.TryDecode(Encoding.UTF8.GetBytes(json), new TrafficValidationLimits { maxPackageBytes = 8 }, out _, out _));
        Assert.IsFalse(NavigationDocumentCodec.TryDecode(Encoding.UTF8.GetBytes(json.Replace("\"edges\":[", "\"edges\":null,")), null, out _, out _));
        string missingSchema = json.Replace("\"schemaVersion\":1,", string.Empty);
        Assert.IsFalse(NavigationDocumentCodec.TryDecode(Encoding.UTF8.GetBytes(missingSchema), null, out _, out _));
        string duplicate = json.Replace("\"documentId\":\"doc\"", "\"documentId\":\"doc\",\"documentId\":\"duplicate\"");
        Assert.IsFalse(NavigationDocumentCodec.TryDecode(Encoding.UTF8.GetBytes(duplicate), null, out _, out _));
    }

    [Test]
    public void StagingRejectsWrongHashAndPreservesActiveOnFailureThenRollsBack() {
        var source = Fixture(); Assert.IsTrue(NavigationDocumentCodec.TryEncode(source, null, out var json, out var hash, out var reason), reason);
        var profile = ScriptableObject.CreateInstance<NpcVehicleProfile>(); profile.vehicleProfileId = "car";
        var fingerprint = new NavigationDependency { dependencyId = "car", dependencyKind = "vehicleProfile", contentHash = "car-hash" };
        try {
            var stager = new NavigationPackageStager(); var catalog = new PackageTrafficProfileCatalog(new[] { profile }, null, new[] { fingerprint });
            var manifest = new NavigationModuleManifest { mapId = source.mapId, documentId = source.documentId, canonicalHash = hash, dependencies = new List<NavigationDependency> { fingerprint } };
            Assert.IsFalse(stager.TryStage(Encoding.UTF8.GetBytes(json), manifest, "wrong", catalog, out _));
            Assert.IsNull(stager.ActiveDocument);
            Assert.IsTrue(stager.TryStage(Encoding.UTF8.GetBytes(json), manifest, hash, catalog, out reason), reason);
            Assert.IsTrue(stager.TryActivate(out reason), reason); var old = stager.ActiveDocument;
            var second = Fixture(); second.documentId = "second"; Assert.IsTrue(NavigationDocumentCodec.TryEncode(second, null, out json, out hash, out reason), reason);
            manifest.documentId = second.documentId; manifest.canonicalHash = hash;
            Assert.IsTrue(stager.TryStage(Encoding.UTF8.GetBytes(json), manifest, hash, catalog, out reason), reason);
            Assert.IsTrue(stager.TryActivate(out reason), reason); Assert.AreNotSame(old, stager.ActiveDocument);
            Assert.IsTrue(stager.TryRollback(out reason), reason); Assert.AreEqual(old.documentId, stager.ActiveDocument.documentId);
            old.documentId = "mutated";
            Assert.AreEqual("doc", stager.ActiveDocument.documentId);
        } finally { Object.DestroyImmediate(profile); }
    }

    static MapNavigationDocument Fixture() {
        var d = new MapNavigationDocument { mapId = "map", documentId = "doc", localBounds = new Rect(-10f, -10f, 20f, 20f) };
        d.noSpawnRegions.Add(new NoSpawnRegion { anchorId = "spawn", reason = "test", area = new Rect(-3f, -2f, 4f, 5f) });
        d.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 4f }); d.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        d.edges.Add(new RoadEdgeRecord { edgeId = "ab", fromNodeId = "a", toNodeId = "b", usableWidth = 3f, speedLimit = 4f, orderedPoints = new List<Vector2> { new Vector2(0f, 1f), new Vector2(0f, 2f) } });
        d.edges.Add(new RoadEdgeRecord { edgeId = "ba", fromNodeId = "b", toNodeId = "a", usableWidth = 3f, speedLimit = 4f });
        d.junctions.Add(new JunctionRecord { junctionId = "junction", conflictZone = new Rect(1f, 2f, 3f, 4f) });
        d.vehiclePools.Add(new VehiclePoolRecord { poolId = "pool", entries = new List<VehiclePoolEntry> { new VehiclePoolEntry { vehicleProfileId = "car", weight = 1f } } });
        d.civilianRoutes.Add(new CivilianRouteRecord { routeId = "loop", loop = true, vehiclePoolId = "pool", targetCount = 1, edgeIds = new List<string> { "ab", "ba" } });
        d.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "spawn", edgeId = "ab", routeId = "loop", distanceAlongEdge = 1f, clearanceWidth = 1f, clearanceLength = 2f });
        return d;
    }
}
