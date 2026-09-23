using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

/// <summary>Focused S11 dependency-kind and active-document preservation tests.</summary>
public sealed class NavigationDependencyValidationTests {
    [Test]
    /// <summary>Rejects required population, police director, and respawn kind mismatches.</summary>
    public void RequiredPopulationDirectorAndRespawnKindsMustMatch() {
        MapNavigationDocument document = CreateDocument();
        NpcVehicleProfile profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profile.vehicleProfileId = "vehicle";
        try {
            List<NavigationDependency> dependencies = Dependencies();
            PackageTrafficProfileCatalog catalog = new PackageTrafficProfileCatalog(
                new[] { profile }, null, dependencies);
            NavigationModuleManifest manifest = CreateManifest(document, dependencies);

            foreach (string id in new[] { "population", "director", "respawn" }) {
                NavigationDependency dependency = manifest.dependencies.Find(x => x.dependencyId == id);
                dependency.dependencyKind = NavigationDependencyValidation.VehicleProfileKind;

                Assert.IsFalse(NavigationDependencyValidation.TryValidate(
                    document, manifest, catalog, catalog, new TrafficValidationLimits(), out _), id);
                dependency.dependencyKind = KindFor(id);
            }
        } finally {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [Test]
    /// <summary>Rejects a required fingerprint mismatch without replacing the active document.</summary>
    public void StagerPreservesActiveDocumentWhenRequiredFingerprintMismatches() {
        MapNavigationDocument document = CreateDocument();
        Assert.IsTrue(NavigationDocumentCodec.TryEncode(document, null, out string json, out string hash, out string reason), reason);
        List<NavigationDependency> dependencies = Dependencies();
        NpcVehicleProfile profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profile.vehicleProfileId = "vehicle";
        try {
            PackageTrafficProfileCatalog catalog = new PackageTrafficProfileCatalog(new[] { profile }, null, dependencies);
            NavigationModuleManifest manifest = CreateManifest(document, dependencies);
            NavigationPackageStager stager = new NavigationPackageStager();
            Assert.IsTrue(stager.TryStage(Encoding.UTF8.GetBytes(json), manifest, hash, catalog, out reason), reason);
            Assert.IsTrue(stager.TryActivate(out reason), reason);
            string activeDocumentId = stager.ActiveDocument.documentId;

            manifest.dependencies.Find(x => x.dependencyId == "population").contentHash = "wrong";
            Assert.IsFalse(stager.TryStage(Encoding.UTF8.GetBytes(json), manifest, hash, catalog, out _));
            Assert.AreEqual(activeDocumentId, stager.ActiveDocument.documentId);
        } finally {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    static string KindFor(string id) {
        if (id == "population") return NavigationDependencyValidation.PopulationProfileKind;
        if (id == "director") return NavigationDependencyValidation.PoliceDirectorProfileKind;
        return NavigationDependencyValidation.RespawnProfileKind;
    }

    static List<NavigationDependency> Dependencies() {
        return new List<NavigationDependency> {
            new NavigationDependency { dependencyId = "vehicle", dependencyKind = "vehicleProfile", contentHash = "vehicle-hash" },
            new NavigationDependency { dependencyId = "population", dependencyKind = "populationProfile", contentHash = "population-hash" },
            new NavigationDependency { dependencyId = "director", dependencyKind = "policeDirectorProfile", contentHash = "director-hash" },
            new NavigationDependency { dependencyId = "respawn", dependencyKind = "respawnProfile", contentHash = "respawn-hash" }
        };
    }

    static NavigationModuleManifest CreateManifest(MapNavigationDocument document, List<NavigationDependency> dependencies) {
        Assert.IsTrue(NavigationDocumentCodec.TryEncode(document, null, out _, out string hash, out string reason), reason);
        return new NavigationModuleManifest {
            schemaVersion = NavigationDocumentCodec.CurrentSchemaVersion,
            mapId = document.mapId,
            documentId = document.documentId,
            canonicalHash = hash,
            dependencies = dependencies
        };
    }

    static MapNavigationDocument CreateDocument() {
        var document = new MapNavigationDocument {
            mapId = "map",
            documentId = "doc",
            localBounds = new Rect(-10f, -10f, 20f, 20f)
        };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 4f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "ab", fromNodeId = "a", toNodeId = "b", usableWidth = 3f, speedLimit = 4f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "ba", fromNodeId = "b", toNodeId = "a", usableWidth = 3f, speedLimit = 4f });
        document.vehiclePools.Add(new VehiclePoolRecord {
            poolId = "pool",
            entries = new List<VehiclePoolEntry> {
                new VehiclePoolEntry { vehicleProfileId = "vehicle", weight = 1f }
            }
        });
        document.civilianRoutes.Add(new CivilianRouteRecord {
            routeId = "route",
            loop = true,
            vehiclePoolId = "pool",
            targetCount = 1,
            respawnProfileId = "respawn",
            edgeIds = new List<string> { "ab", "ba" }
        });
        document.difficultyProfileBindings.Add(new DifficultyTrafficBinding {
            difficultyId = "tier",
            civilianPopulationProfileId = "population",
            policeDirectorProfileId = "director"
        });
        return document;
    }
}
