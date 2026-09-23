using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors only the opt-in NarrowDistrict damage presentation assets and binding.</summary>
public static class TrafficDamageVisualAuthoring {
    const string Root = "Assets/Prefabs/Traffic/NarrowDistrict/";
    const string ScenePath = "Assets/Scenes/NarrowDistrict.unity";
    const string MaterialPath = Root + "TrafficDamageParticles.mat";
    const string TexturePath = Root + "TrafficDamageParticleMask.asset";
    static readonly string[] Targets = {
        Root + "TrafficCompact_blue.prefab", Root + "TrafficCompact_green.prefab",
        Root + "TrafficCompact_red.prefab", Root + "TrafficCompact_yellow.prefab",
        "Assets/Prefabs/Police/PoliceStandard.prefab", "Assets/Prefabs/Police/PoliceHeavy.prefab",
        "Assets/Prefabs/Police/PoliceSport.prefab"
    };

    /// <summary>Creates missing effects, binds seven known prefabs, and saves only clean NarrowDistrict.</summary>
    public static void Apply() {
        Scene scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != ScenePath || scene.isDirty || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Open clean NarrowDistrict alone in Edit Mode before authoring effects.");
        var host = SessionSceneRules.ResolveComponent<TrafficSessionHost>(scene);
        var world = SessionSceneRules.ResolveComponent<TrafficDamageWorld>(scene);
        if (host == null || world == null || host.gameObject != world.gameObject)
            throw new InvalidOperationException("The unique host must own the damage world.");
        foreach (string path in Targets) {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponentsInChildren<SpriteRenderer>(true).Length != 1)
                throw new InvalidOperationException("Expected one body sprite: " + path);
            ValidateChild(prefab, "DamageSmokeVisual");
            ValidateChild(prefab, "DamageCriticalVisual");
        }
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) {
            if (AssetDatabase.LoadMainAssetAtPath(MaterialPath) != null)
                throw new InvalidOperationException("Material path is occupied by another asset.");
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Supported URP 2D sprite shader required.");
            material = new Material(shader) { name = "TrafficDamageParticles" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        if (material.shader == null || !material.shader.isSupported || material.shader.name != "Universal Render Pipeline/2D/Sprite-Unlit-Default")
            throw new InvalidOperationException("Owned particle material has an unsupported shader.");
        material.mainTexture = ParticleMask();
        EditorUtility.SetDirty(material);
        var smoke = Effect("DamageSmokeVisual", true, new Color(.32f, .32f, .32f, .85f), material);
        var critical = Effect("DamageCriticalVisual", true, new Color(1f, .35f, .05f, .95f), material);
        var explosion = Effect("TrafficExplosionVisual", false, new Color(1f, .25f, .02f, 1f), material);
        foreach (string path in Targets) {
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try {
                var body = prefab.GetComponentInChildren<SpriteRenderer>(true);
                var presentation = prefab.GetComponent<NpcDamagePresentation>();
                if (presentation == null) presentation = prefab.AddComponent<NpcDamagePresentation>();
                var serialized = new SerializedObject(presentation);
                serialized.FindProperty("body").objectReferenceValue = body;
                serialized.FindProperty("smoke").objectReferenceValue = Child(prefab, smoke, body);
                serialized.FindProperty("critical").objectReferenceValue = Child(prefab, critical, body);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.SaveAsPrefabAsset(prefab, path) == null) throw new InvalidOperationException("Prefab save failed: " + path);
            } finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        var pool = host.GetComponent<TrafficExplosionVisualPool>();
        if (pool == null) pool = host.gameObject.AddComponent<TrafficExplosionVisualPool>();
        var poolData = new SerializedObject(pool);
        poolData.FindProperty("world").objectReferenceValue = world;
        poolData.FindProperty("effectPrefab").objectReferenceValue = explosion.GetComponent<ParticleSystem>();
        poolData.FindProperty("capacity").intValue = 16;
        poolData.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(material);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("NarrowDistrict save failed.");
    }

    static void ValidateChild(GameObject prefab, string name) {
        FindOwnedChild(prefab, Root + name + ".prefab");
    }

    static Texture2D ParticleMask() {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (existing != null) return existing;
        if (AssetDatabase.LoadMainAssetAtPath(TexturePath) != null)
            throw new InvalidOperationException("Particle mask path is occupied.");
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
            name = "TrafficDamageParticleMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
            float radius = new Vector2((x + .5f) * 2f / size - 1f, (y + .5f) * 2f / size - 1f).magnitude;
            pixels[y * size + x] = new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(.15f, 1f, radius));
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        AssetDatabase.CreateAsset(texture, TexturePath);
        return texture;
    }

    static GameObject FindOwnedChild(GameObject root, string assetPath) {
        GameObject match = null;
        foreach (Transform child in root.transform) {
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject) != assetPath) continue;
            if (match != null) throw new InvalidOperationException("Duplicate owned presentation asset: " + assetPath);
            if (child.GetComponent<ParticleSystem>() == null) throw new InvalidOperationException("Owned effect has no particles: " + assetPath);
            match = child.gameObject;
        }
        return match;
    }

    static GameObject Effect(string name, bool loop, Color color, Material material) {
        string path = Root + name + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) {
            var particle = existing.GetComponent<ParticleSystem>();
            if (existing.name != name || particle == null || particle.main.loop != loop || existing.activeSelf == loop)
                throw new InvalidOperationException("Existing effect does not match the owned contract: " + path);
            return existing;
        }
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException("Effect path occupied: " + path);
        var temporary = new GameObject(name);
        try {
            var particles = temporary.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = loop; main.playOnAwake = loop; main.stopAction = ParticleSystemStopAction.None;
            main.duration = loop ? 1f : .5f; main.startLifetime = loop ? 1.1f : .45f;
            main.startSpeed = loop ? .15f : 1.8f; main.startSize = loop ? .16f : .22f;
            main.startColor = color; main.maxParticles = loop ? 16 : 12;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var shape = particles.shape;
            shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .1f;
            var fading = particles.colorOverLifetime;
            fading.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fading.color = gradient;
            var emission = particles.emission;
            emission.rateOverTime = loop ? 5f : 0f;
            if (!loop) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material; renderer.sortingOrder = 20;
            temporary.SetActive(!loop);
            var saved = PrefabUtility.SaveAsPrefabAsset(temporary, path);
            if (saved == null) throw new InvalidOperationException("Effect save failed: " + path);
            return saved;
        } finally { UnityEngine.Object.DestroyImmediate(temporary); }
    }

    static GameObject Child(GameObject root, GameObject source, SpriteRenderer body) {
        GameObject existing = FindOwnedChild(root, AssetDatabase.GetAssetPath(source));
        var child = existing != null ? existing : (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        child.SetActive(false);
        var renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.sortingLayerID = body.sortingLayerID; renderer.sortingOrder = body.sortingOrder + 1;
        return child;
    }
}
