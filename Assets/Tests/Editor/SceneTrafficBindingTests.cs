using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Focused direct-open identity and real physics-query regressions for the civilian scene adapter.</summary>
public sealed class SceneTrafficBindingTests {
    /// <summary>Only a named, existing test tier is accepted; unknown and missing tiers are never inferred.</summary>
    [TestCase("tier_01", "tier_01")]
    [TestCase("unknown", "")]
    [TestCase(null, "")]
    public void DirectContextRequiresExplicitAuthoredDifficulty(string authored, string expected) {
        var map = ScriptableObject.CreateInstance<MapData>();
        var level = ScriptableObject.CreateInstance<LevelData>();
        try {
            map.mapId = "map"; map.sceneName = "test-scene"; map.levelData = level;
            level.difficultyLevels.Add(new MapDifficultyData { difficultyId = "tier_01" });
            var context = SessionSceneRules.Create(null, map, true, "test-scene", authored);
            Assert.AreEqual("map", context.Draft.mapId);
            Assert.AreEqual(expected, context.Draft.difficultyId);
            if (authored != null) Assert.IsFalse(context.Integrity.IsValid);
            if (authored != null) Assert.IsEmpty(SessionSceneRules.Create(null, map, true, "wrong-scene", authored).Draft.difficultyId);
        } finally {
            Object.DestroyImmediate(map); Object.DestroyImmediate(level);
        }
    }

    /// <summary>Spawn queries see moving actors, rejoin queries ignore self, triggers are ignored and saturation fails closed.</summary>
    [Test]
    public void ScopedClearanceFiltersStaticDynamicTriggersAndSaturation() {
        var scene = EditorSceneManager.NewPreviewScene();
        try {
            var go = new GameObject("Query fixture");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(10f, 20f);
            var collider = go.AddComponent<BoxCollider2D>();
            var physics = scene.GetPhysicsScene2D();
            var world = new PhysicsSceneAreaClearanceQuery(physics, Vector2.zero, false, 8);
            var local = new PhysicsSceneAreaClearanceQuery(physics, new Vector2(10f, 20f), true, 8);
            Physics2D.SyncTransforms();
            Assert.IsFalse(world.IsAreaClear(new Vector2(10f, 20f), Vector2.one, 0f));
            Assert.IsFalse(local.IsAreaClear(Vector2.zero, Vector2.one, 0f));
            var body = go.AddComponent<Rigidbody2D>(); body.gravityScale = 0f;
            Physics2D.SyncTransforms();
            Assert.IsTrue(local.IsAreaClear(Vector2.zero, Vector2.one, 0f));
            Assert.IsFalse(world.IsAreaClear(new Vector2(10f, 20f), Vector2.one, 0f));
            Assert.IsFalse(new PhysicsSceneAreaClearanceQuery(physics, Vector2.zero, true, 1).IsAreaClear(new Vector2(10f, 20f), Vector2.one, 0f));
            collider.isTrigger = true;
            Assert.IsTrue(world.IsAreaClear(new Vector2(10f, 20f), Vector2.one, 0f));
            Assert.IsFalse(world.IsAreaClear(Vector2.zero, new Vector2(float.NaN, 1f), 0f));
        } finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
