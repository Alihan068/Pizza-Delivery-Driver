using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Pure and component-level R01 contracts for detached police data and owner selection.</summary>
public sealed class PoliceFreeCompositionTests {
    [Test]
    public void FreeChaseSettingsCloneIsDetachedAndInvalidDataFailsClosed() {
        var authored = new PoliceFreeChaseSettings();
        var clone = authored.Clone();
        clone.goalRadius = float.NaN;
        Assert.IsTrue(authored.IsValid(out _));
        Assert.IsFalse(clone.IsValid(out _));
        Assert.AreNotSame(authored, clone);
    }

    [Test]
    public void ModeContractContainsLegacyAndFreeDrive() {
        Assert.AreEqual(0, (int)PoliceNavigationMode.LegacyRoad);
        Assert.AreEqual(1, (int)PoliceNavigationMode.FreeDrive);
        Assert.IsTrue(System.Enum.IsDefined(typeof(PoliceNavigationMode), PoliceNavigationMode.FreeDrive));
    }

    [Test]
    public void ExplicitBindingRejectsMissingDependenciesBeforeFallback() {
        var root = new GameObject("Police composition fixture");
        try {
            var binding = root.AddComponent<PoliceSceneBinding>();
            Assert.IsFalse(binding.ValidateForSession(null, out var reason));
            StringAssert.Contains("incomplete", reason);
        } finally {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SceneTrafficBindingStartsAsLegacyAndCannotChangeOwnerAfterFreeze() {
        var root = new GameObject("Traffic composition fixture");
        try {
            var traffic = root.AddComponent<SceneTrafficBinding>();
            var binding = root.AddComponent<PoliceSceneBinding>();
            Assert.IsTrue(traffic.UsesLegacyPoliceOwner);
            Assert.IsTrue(traffic.TrySetPoliceBinding(binding, out var reason), reason);
            Assert.IsFalse(traffic.UsesLegacyPoliceOwner);
            Assert.IsFalse(traffic.TrySetPoliceBinding(null, out reason));
        } finally {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ExplicitDependenciesMustBeInTheSameScene() {
        var first = new GameObject("Police scene one");
        var second = new GameObject("Police scene two");
        var secondScene = EditorSceneManager.NewPreviewScene();
        try {
            SceneManager.MoveGameObjectToScene(second, secondScene);
            var binding = first.AddComponent<PoliceSceneBinding>();
            var host = first.AddComponent<TrafficSessionHost>();
            var world = second.AddComponent<TrafficDamageWorld>();
            var director = first.AddComponent<PoliceDirector>();
            var provider = first.AddComponent<PrefabPoliceVehicleBodyProvider>();
            var catalog = ScriptableObject.CreateInstance<PoliceVehiclePrefabCatalog>();
            var profile = ScriptableObject.CreateInstance<PoliceDirectorData>();
            try {
                Assert.IsFalse(binding.TryConfigure(host, world, director, provider, catalog, profile, out var reason));
                StringAssert.Contains("same scene", reason);
            } finally {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(profile);
            }
        } finally {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
            EditorSceneManager.ClosePreviewScene(secondScene);
        }
    }
}
