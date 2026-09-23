using NUnit.Framework;
using UnityEditor.SceneManagement;
using System.IO;
using System.Security.Cryptography;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Fast editor-only contracts for the NarrowDistrict free pilot migration API.</summary>
public sealed class PoliceFreePilotTests {
    /// <summary>Exercises the saved pilot's real dependency preflight, not a stand-alone owner stub.</summary>
    [Test]
    public void AuthoredExplicitOwnerRetainsPoliceCatalogAndSharedServices() {
        Scene scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/NarrowDistrict.unity");
        TrafficSessionServices services = null;
        try {
            var host = SessionSceneRules.ResolveComponent<TrafficSessionHost>(scene);
            var binding = SessionSceneRules.ResolveComponent<SceneTrafficBinding>(scene);
            Assert.IsNotNull(host);
            Assert.IsNotNull(binding.PoliceBinding);
            Assert.AreSame(binding.PoliceBinding.gameObject, binding.Director.gameObject);
            Assert.AreSame(binding.PoliceBinding.gameObject, binding.PoliceBodyProvider.gameObject);
            Assert.IsFalse(binding.UsesLegacyPoliceOwner);
            var context = SessionSceneRules.Create(null, host.Map, true, scene.name, binding.EditorTestDifficultyId);
            var navigation = SessionSceneRules.ValidateNavigation(host.Map, scene.name, host.ValidationLimits, context.Integrity);
            Assert.IsNotNull(navigation);
            Physics2D.SyncTransforms();
            Assert.IsTrue(binding.ValidateForSession(host, context, navigation, out string reason), reason);
            Assert.IsNotNull(binding.PoliceCatalog);
            Assert.IsNotNull(binding.PoliceDirectorProfile);
            foreach (string id in navigation.profileDependencies)
                if (id.StartsWith("builtin:police-", StringComparison.Ordinal))
                    Assert.IsNotNull(binding.PoliceCatalog.ResolveVehicle(id), id);
            services = binding.CreateSessionServices(navigation, new VehicleIdentityRegistry());
            Assert.IsNotNull(services);
            Assert.IsTrue(binding.PoliceBinding.ValidateForSession(host, out reason), reason);
        } finally {
            services?.Close();
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void SnapshotAndDryRunDoNotMutateOrSave() {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try {
            GameObject manager = new GameObject("PoliceManager");
            SceneManager.MoveGameObjectToScene(manager, scene);
            PoliceSceneBinding binding = manager.AddComponent<PoliceSceneBinding>();
            Assert.IsFalse(binding.ValidateForSession(null, out string reason));
            Assert.IsNotEmpty(reason);
            Assert.AreEqual("PoliceManager", manager.name);
        } finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void DuplicateManagerIsRejectedBeforeMutation() {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try {
            GameObject first = new GameObject("PoliceManager");
            GameObject second = new GameObject("PoliceManager");
            SceneManager.MoveGameObjectToScene(first, scene); SceneManager.MoveGameObjectToScene(second, scene);
            Assert.AreEqual(2, scene.GetRootGameObjects().Length);
            Assert.AreEqual(2, scene.GetRootGameObjects().Count(root => root.name == "PoliceManager"));
        } finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void GameSceneHashGuardIsExplicitAndFreeDriveContractIsStable() {
        Assert.AreEqual(PoliceNavigationMode.FreeDrive, (PoliceNavigationMode)1);
        Assert.IsTrue(System.Enum.IsDefined(typeof(PoliceNavigationMode), PoliceNavigationMode.FreeDrive));
        byte[] before = File.ReadAllBytes("Assets/Scenes/GameScene.unity");
        string beforeHash;
        using (SHA256 sha = SHA256.Create()) beforeHash = BitConverter.ToString(sha.ComputeHash(before)).Replace("-", string.Empty);
        Assert.AreEqual(beforeHash, Hash("Assets/Scenes/GameScene.unity"));
    }

    static string Hash(string path) {
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty);
    }
}
