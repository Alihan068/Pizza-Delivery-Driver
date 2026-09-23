using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Focused authored presentation, pool capacity, and real session-end cleanup coverage.</summary>
public sealed class TrafficDamageVisualTests {
    const string Root = "Assets/Prefabs/Traffic/NarrowDistrict/";
    Scene scene;

    /// <summary>Owns an isolated preview scene, leaving the current map untouched.</summary>
    [SetUp]
    public void SetUp() { scene = EditorSceneManager.NewPreviewScene(); }

    /// <summary>Closes all temporary objects even after an assertion failure.</summary>
    [TearDown]
    public void TearDown() { if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); }

    /// <summary>Seven NPC prefabs bind persistent effects and their unique authored body sprite.</summary>
    [Test]
    public void SevenAuthoredPrefabsBindOwnedVisibleEffects() {
        string[] paths = {
            Root + "TrafficCompact_blue.prefab", Root + "TrafficCompact_green.prefab",
            Root + "TrafficCompact_red.prefab", Root + "TrafficCompact_yellow.prefab",
            "Assets/Prefabs/Police/PoliceStandard.prefab", "Assets/Prefabs/Police/PoliceHeavy.prefab",
            "Assets/Prefabs/Police/PoliceSport.prefab"
        };
        foreach (string path in paths) {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            var presentation = prefab.GetComponent<NpcDamagePresentation>();
            Assert.IsNotNull(presentation, path);
            var data = new SerializedObject(presentation);
            var body = data.FindProperty("body").objectReferenceValue as SpriteRenderer;
            Assert.IsNotNull(body, path);
            foreach (string field in new[] { "smoke", "critical" }) {
                var effect = data.FindProperty(field).objectReferenceValue as GameObject;
                Assert.IsNotNull(effect, path + "/" + field);
                Assert.IsFalse(effect.activeSelf);
                var particles = effect.GetComponent<ParticleSystem>();
                Assert.IsTrue(particles.main.loop && particles.main.playOnAwake);
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                Assert.AreEqual(body.sortingLayerID, renderer.sortingLayerID);
                Assert.Greater(renderer.sortingOrder, body.sortingOrder);
                Assert.IsTrue(AssetDatabase.Contains(renderer.sharedMaterial));
                Assert.IsTrue(renderer.sharedMaterial.shader.isSupported);
            }
        }
        var burst = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "TrafficExplosionVisual.prefab");
        Assert.IsTrue(burst.activeSelf);
        Assert.IsFalse(burst.GetComponent<ParticleSystem>().main.loop);
        Assert.IsFalse(burst.GetComponent<ParticleSystem>().main.playOnAwake);
    }

    /// <summary>Actual emitted particles are cleared on wreck and reuse; original sprite color returns.</summary>
    [Test]
    public void PresentationClearsParticlesAcrossWreckAndReuse() {
        GameObject root = Node("Presentation");
        var sprite = root.AddComponent<SpriteRenderer>();
        sprite.color = Color.cyan;
        var smoke = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "DamageSmokeVisual.prefab"), root.transform);
        var critical = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "DamageCriticalVisual.prefab"), root.transform);
        var presentation = root.AddComponent<NpcDamagePresentation>();
        var data = new SerializedObject(presentation);
        data.FindProperty("body").objectReferenceValue = sprite;
        data.FindProperty("smoke").objectReferenceValue = smoke;
        data.FindProperty("critical").objectReferenceValue = critical;
        data.ApplyModifiedPropertiesWithoutUndo();
        presentation.ResetForLife();
        presentation.Show(NpcDamageFeedbackLevel.Critical, false);
        Assert.IsTrue(smoke.activeSelf && critical.activeSelf);
        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true)) {
            Assert.IsTrue(particles.isPlaying);
            particles.Emit(3);
            Assert.Greater(particles.particleCount, 0);
        }
        presentation.Show(NpcDamageFeedbackLevel.Critical, true);
        Assert.AreEqual(Color.gray, sprite.color);
        Assert.IsFalse(smoke.activeSelf || critical.activeSelf);
        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true)) Assert.AreEqual(0, particles.particleCount);
        presentation.ResetForLife();
        Assert.AreEqual(Color.cyan, sprite.color);
        presentation.Show(NpcDamageFeedbackLevel.Critical, false);
        presentation.ResetForLife();
        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true)) Assert.IsFalse(particles.IsAlive(true));
    }

    /// <summary>Real visual event subscription remains bounded, reuses slots, and clears on coordinator end.</summary>
    [Test]
    public void ExplosionPoolCapsReusesAndClearsOnSessionEnd() {
        var context = new TrafficSessionContext(new SessionSetupDraft("vfx", "map", "car", "tier", 5, false, null));
        var coordinator = new TrafficSessionCoordinator(context);
        coordinator.NotifyMapReady(); coordinator.NotifyPlayerReady();
        var settings = ScriptableObject.CreateInstance<TrafficDamageSettings>();
        var world = Node("World").AddComponent<TrafficDamageWorld>();
        try {
            var pool = Node("Pool").AddComponent<TrafficExplosionVisualPool>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "TrafficExplosionVisual.prefab").GetComponent<ParticleSystem>();
            pool.Initialize(world, prefab, 2);
            world.Configure(coordinator, settings);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var effects = (IList)typeof(TrafficExplosionVisualPool).GetField("effects", flags).GetValue(pool);
            var publish = (Action<Vector2>)typeof(TrafficDamageWorld).GetField("ExplosionVisualRequested", flags).GetValue(world);
            Assert.IsNotNull(publish);
            Assert.AreEqual(2, effects.Count);
            publish(Vector2.zero); publish(Vector2.one); publish(Vector2.right);
            Assert.AreEqual(2, pool.GetComponentsInChildren<ParticleSystem>(true).Length);
            var first = (ParticleSystem)effects[0];
            first.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            publish(Vector2.left);
            Assert.AreSame(first, effects[0]); Assert.IsTrue(first.isPlaying);
            coordinator.NotifySessionEnded();
            foreach (ParticleSystem effect in effects) {
                Assert.IsFalse(effect.IsAlive(true));
                Assert.AreEqual(0, effect.particleCount);
            }
        } finally { Object.DestroyImmediate(settings); }
    }

    GameObject Node(string name) {
        var node = new GameObject(name);
        SceneManager.MoveGameObjectToScene(node, scene);
        return node;
    }
}
