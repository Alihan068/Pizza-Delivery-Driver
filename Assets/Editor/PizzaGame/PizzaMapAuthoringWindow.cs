using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Inspector-friendly editor window for creating the reusable Pizza Map Tile Kit and rebuilding
/// the built-in secondary gameplay scenes from serialized blueprints. The generated scenes keep
/// their gameplay shell and service references while their visual layers come from compatible tilemaps.
/// </summary>
public class PizzaMapAuthoringWindow : EditorWindow {
    const string KitRoot = "Assets/TileSets/PizzaMapKit";
    const string TextureRoot = KitRoot + "/Textures";
    const string TileRoot = KitRoot + "/Tiles";
    const string ThemeRoot = KitRoot + "/Themes";
    const string BlueprintRoot = "Assets/MapBlueprints";
    const string PalettePath = KitRoot + "/PizzaMapTilePalette.prefab";
    const string ProtectedPrimarySceneName = "GameScene";

    bool rebuildScenes = true;
    bool createPalette = true;
    bool rebuildPrimaryScene;

    /// <summary>Opens the map authoring window from the PizzaGame menu.</summary>
    [MenuItem("PizzaGame/Map Authoring/Open Map Authoring Window")]
    public static void OpenWindow() {
        GetWindow<PizzaMapAuthoringWindow>("Pizza Map Authoring");
    }

    /// <summary>Runs the complete tile kit and built-in map generation workflow.</summary>
    [MenuItem("PizzaGame/Map Authoring/Generate Tile Kit and Built-in Maps")]
    public static void GenerateBuiltInMaps() {
        Generate(true, true, false);
    }

    void OnGUI() {
        EditorGUILayout.LabelField("Pizza Map Authoring", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Creates compatible tile groups, a road RuleTile, a reusable Tile Palette, editable blueprints, and built-in map scenes.",
            MessageType.Info);
        rebuildScenes = EditorGUILayout.ToggleLeft("Rebuild built-in scenes", rebuildScenes);
        createPalette = EditorGUILayout.ToggleLeft("Rebuild Tile Palette prefab", createPalette);
        rebuildPrimaryScene = EditorGUILayout.ToggleLeft(
            "Rebuild primary GameScene (destructive)", rebuildPrimaryScene);
        EditorGUILayout.HelpBox(
            "GameScene is the protected primary gameplay scene. Leave this disabled to keep its hand-authored map intact.",
            MessageType.Warning);

        if (GUILayout.Button("Create Tile Kit")) {
            Generate(rebuildScenes, createPalette, rebuildPrimaryScene);
        }

        if (GUILayout.Button("Rebuild Scenes From Existing Blueprints")) {
            RebuildExistingBlueprints(rebuildPrimaryScene);
        }
    }

    static void Generate(bool shouldRebuildScenes, bool shouldCreatePalette, bool includePrimaryScene) {
        try {
            EnsureFolders();
            EnsureDetailTextures();
            EnsureRoadTextures();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureGeneratedTextures();

            PizzaMapTheme cityTheme;
            PizzaMapTheme nightTheme;
            PizzaMapTheme villageTheme;
            Dictionary<string, TileBase> tiles = CreateTiles(out cityTheme, out nightTheme, out villageTheme);

            if (shouldCreatePalette) {
                CreateTilePalette(tiles);
            }

            List<PizzaMapBlueprint> blueprints = CreateBuiltInBlueprints(nightTheme, villageTheme);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (shouldRebuildScenes) {
                RebuildExistingBlueprints(includePrimaryScene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Pizza Map Tile Kit generation completed.");
        }
        catch (Exception exception) {
            Debug.LogError("Pizza Map Tile Kit generation failed: " + exception);
        }
    }

    static void RebuildExistingBlueprints(bool includePrimaryScene) {
        string[] guids = AssetDatabase.FindAssets("t:PizzaMapBlueprint", new[] { BlueprintRoot });
        for (int i = 0; i < guids.Length; i++) {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            PizzaMapBlueprint blueprint = AssetDatabase.LoadAssetAtPath<PizzaMapBlueprint>(path);
            if (blueprint == null || (!includePrimaryScene && blueprint.sceneName == ProtectedPrimarySceneName)) {
                continue;
            }

            if (blueprint.theme == null || blueprint.theme.roadTile == null) {
                Debug.LogError("Skipped blueprint with incomplete tile configuration: " + blueprint.blueprintId);
                continue;
            }

            BuildScene(blueprint);
        }
        AssetDatabase.SaveAssets();
    }

    static void EnsureFolders() {
        EnsureFolder("Assets/TileSets", "PizzaMapKit");
        EnsureFolder(KitRoot, "Textures");
        EnsureFolder(KitRoot, "Tiles");
        EnsureFolder(KitRoot, "Themes");
        EnsureFolder("Assets", "MapBlueprints");
    }

    static void EnsureFolder(string parent, string child) {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    static void EnsureDetailTextures() {
        EnsurePatternTexture("City_GroundAccent", new Color32(116, 123, 133, 255),
            new Color32(94, 101, 112, 255), new Color32(148, 154, 162, 255));
        EnsurePatternTexture("City_RoadEdge", new Color32(64, 72, 80, 255),
            new Color32(91, 101, 108, 255), new Color32(116, 124, 130, 255));
        EnsurePatternTexture("Night_GroundAccent", new Color32(52, 67, 88, 255),
            new Color32(38, 49, 68, 255), new Color32(77, 101, 128, 255));
        EnsurePatternTexture("Night_RoadEdge", new Color32(79, 87, 98, 255),
            new Color32(57, 65, 76, 255), new Color32(111, 125, 139, 255));
        EnsurePatternTexture("Village_GroundAccent", new Color32(158, 174, 79, 255),
            new Color32(134, 150, 62, 255), new Color32(194, 204, 100, 255));
        EnsurePatternTexture("Village_RoadEdge", new Color32(152, 113, 72, 255),
            new Color32(116, 80, 48, 255), new Color32(183, 147, 93, 255));

        EnsureGroundTexture("City_Ground_Rich", new Color32(67, 76, 88, 255), 17);
        EnsureGroundTexture("City_GroundSoft_Rich", new Color32(82, 94, 101, 255), 23);
        EnsureGroundTexture("City_GroundDark_Rich", new Color32(48, 58, 69, 255), 31);
        EnsureGroundTexture("City_GroundAccent_Rich", new Color32(92, 101, 105, 255), 37);
        EnsureGroundTexture("Night_Ground_Rich", new Color32(42, 59, 65, 255), 41);
        EnsureGroundTexture("Night_GroundSoft_Rich", new Color32(54, 77, 78, 255), 47);
        EnsureGroundTexture("Night_GroundDark_Rich", new Color32(29, 45, 53, 255), 53);
        EnsureGroundTexture("Night_GroundAccent_Rich", new Color32(70, 92, 87, 255), 59);
        EnsureGroundTexture("Village_Ground_Rich", new Color32(91, 128, 54, 255), 67);
        EnsureGroundTexture("Village_GroundSoft_Rich", new Color32(116, 151, 63, 255), 71);
        EnsureGroundTexture("Village_GroundDark_Rich", new Color32(67, 102, 48, 255), 79);
        EnsureGroundTexture("Village_GroundAccent_Rich", new Color32(143, 159, 70, 255), 83);

        EnsureSidewalkTexture("City_RoadEdge_Rich", new Color32(91, 101, 108, 255),
            new Color32(65, 72, 80, 255), new Color32(125, 133, 139, 255));
        EnsureSidewalkTexture("Night_RoadEdge_Rich", new Color32(84, 98, 105, 255),
            new Color32(57, 68, 77, 255), new Color32(130, 143, 147, 255));
        EnsureSidewalkTexture("Village_RoadEdge_Rich", new Color32(164, 135, 91, 255),
            new Color32(116, 86, 57, 255), new Color32(204, 171, 111, 255));
    }

    static void EnsureRoadTextures() {
        EnsureRoadTextureSet("City", new Color32(49, 57, 68, 255),
            new Color32(86, 96, 105, 255));
        EnsureRoadTextureSet("Night", new Color32(24, 31, 45, 255),
            new Color32(65, 76, 91, 255));
        EnsureRoadTextureSet("Village", new Color32(67, 72, 73, 255),
            new Color32(104, 96, 77, 255));
        EnsureRoadMarkingTextures();
    }

    static void EnsureRoadTextureSet(string prefix, Color32 asphalt, Color32 edge) {
        string[] variants = { "Single", "Straight", "Corner", "Tee", "Cross", "End" };
        for (int i = 0; i < variants.Length; i++) {
            EnsureRoadTexture(prefix + "_Road_" + variants[i] + "_Rich", variants[i], asphalt, edge);
        }
    }

    static void EnsureRoadTexture(string name, string variant, Color32 asphalt, Color32 edge) {
        string assetPath = TextureRoot + "/" + name + ".png";
        string relativePath = assetPath.Substring("Assets/".Length)
            .Replace("/", Path.DirectorySeparatorChar.ToString());
        string fullPath = Path.Combine(Application.dataPath, relativePath);
        if (File.Exists(fullPath)) {
            return;
        }

        Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < 32; y++) {
            for (int x = 0; x < 32; x++) {
                Color32 color = asphalt;
                if (x == 0 || y == 0 || x == 31 || y == 31) {
                    color = edge;
                } else if ((x * 11 + y * 7) % 43 == 0) {
                    color = new Color32(
                        (byte)Mathf.Min(255, asphalt.r + 9),
                        (byte)Mathf.Min(255, asphalt.g + 9),
                        (byte)Mathf.Min(255, asphalt.b + 9), 255);
                }

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        DestroyImmediate(texture);
    }

    static void EnsureRoadMarkingTextures() {
        Color32 marking = new Color32(238, 218, 142, 255);
        EnsureRoadMarkingTexture("City_RoadMark_Single", marking, false);
        EnsureRoadMarkingTexture("City_RoadMark_Double", marking, true);
        EnsureRoadMarkingTexture("City_RoadMark_Diagonal", marking, false, true);
        EnsureRoadMarkingTexture("Night_RoadMark_Single", new Color32(220, 211, 153, 255), false);
        EnsureRoadMarkingTexture("Night_RoadMark_Double", new Color32(220, 211, 153, 255), true);
        EnsureRoadMarkingTexture("Night_RoadMark_Diagonal", new Color32(220, 211, 153, 255), false, true);
        EnsureRoadMarkingTexture("Village_RoadMark_Single", new Color32(247, 232, 179, 255), false);
        EnsureRoadMarkingTexture("Village_RoadMark_Double", new Color32(247, 232, 179, 255), true);
        EnsureRoadMarkingTexture("Village_RoadMark_Diagonal", new Color32(247, 232, 179, 255), false, true);
    }

    static void EnsureRoadMarkingTexture(string name, Color32 marking, bool doubled, bool diagonal = false) {
        string assetPath = TextureRoot + "/" + name + ".png";
        string relativePath = assetPath.Substring("Assets/".Length)
            .Replace("/", Path.DirectorySeparatorChar.ToString());
        string fullPath = Path.Combine(Application.dataPath, relativePath);
        if (File.Exists(fullPath) && !diagonal) {
            return;
        }

        Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color32[] clear = new Color32[32 * 32];
        for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(0, 0, 0, 0);
        texture.SetPixels32(clear);

        if (diagonal) {
            // Keep a short gap at both cell edges so neighboring diagonal cells read as
            // one continuous dashed lane line instead of a full slash in every cell.
            for (int i = 5; i <= 27; i++) {
                SetPixelSafe(texture, i, i, marking);
                SetPixelSafe(texture, i + 1, i, marking);
            }
        } else {
            int lineOffset = doubled ? 10 : 15;
            for (int y = 0; y < 32; y++) {
                if (y % 10 < 6) {
                    SetPixelSafe(texture, lineOffset, y, marking);
                    SetPixelSafe(texture, lineOffset + 1, y, marking);
                    if (doubled) {
                        SetPixelSafe(texture, lineOffset + 10, y, marking);
                        SetPixelSafe(texture, lineOffset + 11, y, marking);
                    }
                }
            }
        }

        texture.Apply();
        File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        DestroyImmediate(texture);
    }

    static void EnsureGroundTexture(string name, Color32 baseColor, int seed) {
        string assetPath = TextureRoot + "/" + name + ".png";
        string relativePath = assetPath.Substring("Assets/".Length)
            .Replace("/", Path.DirectorySeparatorChar.ToString());
        string fullPath = Path.Combine(Application.dataPath, relativePath);
        if (File.Exists(fullPath)) {
            return;
        }

        Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Repeat;
        Color32 shade = AdjustColor(baseColor, 0.82f);
        Color32 highlight = AdjustColor(baseColor, 1.14f);
        for (int y = 0; y < 32; y++) {
            for (int x = 0; x < 32; x++) {
                int pattern = Mathf.Abs((x * 17 + y * 31 + seed * 13) % 97);
                Color32 color = pattern < 9 ? shade : pattern > 89 ? highlight : baseColor;
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        DestroyImmediate(texture);
    }

    static void EnsureSidewalkTexture(string name, Color32 baseColor, Color32 seam, Color32 highlight) {
        string assetPath = TextureRoot + "/" + name + ".png";
        string relativePath = assetPath.Substring("Assets/".Length)
            .Replace("/", Path.DirectorySeparatorChar.ToString());
        string fullPath = Path.Combine(Application.dataPath, relativePath);
        if (File.Exists(fullPath)) {
            return;
        }

        Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Repeat;
        for (int y = 0; y < 32; y++) {
            for (int x = 0; x < 32; x++) {
                Color32 color = baseColor;
                if (x % 8 == 0 && y % 8 != 0) color = seam;
                if (y % 8 == 0 && x % 8 != 0) color = seam;
                if ((x * 7 + y * 11) % 43 == 0) color = highlight;
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        DestroyImmediate(texture);
    }

    static Color32 AdjustColor(Color32 color, float multiplier) {
        return new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(color.r * multiplier), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * multiplier), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * multiplier), 0, 255), color.a);
    }

    static void SetPixelSafe(Texture2D texture, int x, int y, Color32 color) {
        if (x >= 0 && y >= 0 && x < texture.width && y < texture.height) {
            texture.SetPixel(x, y, color);
        }
    }

    static void EnsurePatternTexture(string name, Color32 baseColor, Color32 lineColor, Color32 detailColor) {
        string assetPath = TextureRoot + "/" + name + ".png";
        string relativePath = assetPath.Substring("Assets/".Length)
            .Replace("/", Path.DirectorySeparatorChar.ToString());
        string fullPath = Path.Combine(Application.dataPath, relativePath);
        if (File.Exists(fullPath)) {
            return;
        }

        Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Repeat;
        for (int y = 0; y < 32; y++) {
            for (int x = 0; x < 32; x++) {
                Color32 color = baseColor;
                if (x % 8 == 0 || y % 8 == 0) {
                    color = lineColor;
                }

                int pattern = (x * 17 + y * 31) % 47;
                if (pattern < 3 || ((x + y * 2) % 19 == 0)) {
                    color = detailColor;
                }

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        DestroyImmediate(texture);
    }

    static void ConfigureGeneratedTextures() {
        string[] paths = AssetDatabase.FindAssets("t:Texture2D", new[] { TextureRoot });
        for (int i = 0; i < paths.Length; i++) {
            string path = AssetDatabase.GUIDToAssetPath(paths[i]);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) {
                continue;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 32f;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }

    static Dictionary<string, TileBase> CreateTiles(
        out PizzaMapTheme cityTheme,
        out PizzaMapTheme nightTheme,
        out PizzaMapTheme villageTheme) {

        Dictionary<string, TileBase> tiles = new Dictionary<string, TileBase>();
        string[] prefixes = { "City", "Night", "Village" };

        for (int i = 0; i < prefixes.Length; i++) {
            string prefix = prefixes[i];
            Sprite single = LoadSprite(prefix + "_Road_Single_Rich");
            Sprite straight = LoadSprite(prefix + "_Road_Straight_Rich");
            Sprite corner = LoadSprite(prefix + "_Road_Corner_Rich");
            Sprite tee = LoadSprite(prefix + "_Road_Tee_Rich");
            Sprite cross = LoadSprite(prefix + "_Road_Cross_Rich");
            Sprite end = LoadSprite(prefix + "_Road_End_Rich");

            tiles[prefix + "_Ground"] = CreateBasicTile(prefix + "_Ground", LoadSprite(prefix + "_Ground_Rich"));
            tiles[prefix + "_GroundAccent"] = CreateBasicTile(
                prefix + "_GroundAccent", LoadSprite(prefix + "_GroundAccent_Rich"));
            tiles[prefix + "_GroundSoft"] = CreateBasicTile(
                prefix + "_GroundSoft", LoadSprite(prefix + "_GroundSoft_Rich"));
            tiles[prefix + "_GroundDark"] = CreateBasicTile(
                prefix + "_GroundDark", LoadSprite(prefix + "_GroundDark_Rich"));
            tiles[prefix + "_Water"] = CreateBasicTile(prefix + "_Water", LoadSprite(prefix + "_Water"));
            tiles[prefix + "_Dirt"] = CreateBasicTile(prefix + "_Dirt", LoadSprite(prefix + "_Dirt"));
            tiles[prefix + "_RoadEdge"] = CreateBasicTile(
                prefix + "_RoadEdge", LoadSprite(prefix + "_RoadEdge_Rich"));
            tiles[prefix + "_RoadMarking"] = CreateBasicTile(
                prefix + "_RoadMarking", LoadSprite(prefix + "_RoadMark_Single"));
            tiles[prefix + "_RoadDoubleMarking"] = CreateBasicTile(
                prefix + "_RoadDoubleMarking", LoadSprite(prefix + "_RoadMark_Double"));
            tiles[prefix + "_DiagonalRoadMarking"] = CreateBasicTile(
                prefix + "_DiagonalRoadMarking", LoadSprite(prefix + "_RoadMark_Diagonal"));
            tiles[prefix + "_Road"] = CreateRoadRuleTile(prefix + "_Road", single, straight, corner, tee, cross, end);
        }

        cityTheme = CreateTheme(
            "CityDay", "City Day", tiles["City_Ground"], tiles["City_GroundAccent"],
            tiles["City_GroundSoft"], tiles["City_GroundDark"], tiles["City_Road"] as PizzaRoadRuleTile,
            tiles["City_RoadEdge"], tiles["City_RoadMarking"], tiles["City_RoadDoubleMarking"],
            tiles["City_DiagonalRoadMarking"],
            tiles["City_Water"], tiles["City_Dirt"], Color.white, 1f);

        nightTheme = CreateTheme(
            "MoonlitTown", "Moonlit Town", tiles["Night_Ground"], tiles["Night_GroundAccent"],
            tiles["Night_GroundSoft"], tiles["Night_GroundDark"], tiles["Night_Road"] as PizzaRoadRuleTile,
            tiles["Night_RoadEdge"], tiles["Night_RoadMarking"], tiles["Night_RoadDoubleMarking"],
            tiles["Night_DiagonalRoadMarking"],
            tiles["Night_Water"], tiles["Night_Dirt"], new Color(0.45f, 0.58f, 0.9f, 1f), 0.85f);

        villageTheme = CreateTheme(
            "MeadowVillage", "Meadow Village", tiles["Village_Ground"], tiles["Village_GroundAccent"],
            tiles["Village_GroundSoft"], tiles["Village_GroundDark"],
            tiles["Village_Road"] as PizzaRoadRuleTile, tiles["Village_RoadEdge"],
            tiles["Village_RoadMarking"], tiles["Village_RoadDoubleMarking"],
            tiles["Village_DiagonalRoadMarking"],
            tiles["Village_Water"], tiles["Village_Dirt"], new Color(1f, 0.94f, 0.78f, 1f), 1.05f);

        return tiles;
    }

    static Sprite LoadSprite(string name) {
        string path = TextureRoot + "/" + name + ".png";
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) {
            throw new InvalidOperationException("Generated sprite was not imported: " + path);
        }
        return sprite;
    }

    static TileBase CreateBasicTile(string name, Sprite sprite) {
        string path = TileRoot + "/" + name + ".asset";
        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (tile == null) {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, path);
        }

        tile.sprite = sprite;
        tile.color = Color.white;
        tile.colliderType = Tile.ColliderType.None;
        EditorUtility.SetDirty(tile);
        return tile;
    }

    static PizzaRoadRuleTile CreateRoadRuleTile(
        string name, Sprite single, Sprite straight, Sprite corner, Sprite tee, Sprite cross, Sprite end) {

        string path = TileRoot + "/" + name + ".asset";
        PizzaRoadRuleTile road = AssetDatabase.LoadAssetAtPath<PizzaRoadRuleTile>(path);
        if (road == null) {
            road = ScriptableObject.CreateInstance<PizzaRoadRuleTile>();
            AssetDatabase.CreateAsset(road, path);
        }

        road.m_DefaultSprite = single;
        road.m_DefaultColliderType = Tile.ColliderType.None;
        road.m_TilingRules.Clear();

        List<Vector3Int> positions = new List<Vector3Int> {
            new Vector3Int(0, 1, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(-1, 0, 0)
        };

        AddRoadRule(road, 0, cross, new[] { 1, 1, 1, 1 },
            UnityEngine.RuleTile.TilingRuleOutput.Transform.Fixed, positions);
        AddRoadRule(road, 1, tee, new[] { 1, 1, 2, 1 },
            UnityEngine.RuleTile.TilingRuleOutput.Transform.Rotated, positions);
        AddRoadRule(road, 2, straight, new[] { 1, 2, 1, 2 },
            UnityEngine.RuleTile.TilingRuleOutput.Transform.Rotated, positions);
        AddRoadRule(road, 3, corner, new[] { 1, 1, 2, 2 },
            UnityEngine.RuleTile.TilingRuleOutput.Transform.Rotated, positions);
        AddRoadRule(road, 4, end, new[] { 1, 2, 2, 2 },
            UnityEngine.RuleTile.TilingRuleOutput.Transform.Rotated, positions);
        AddRoadRule(road, 5, single, new[] { 2, 2, 2, 2 },
            UnityEngine.RuleTile.TilingRuleOutput.Transform.Fixed, positions);

        road.UpdateNeighborPositions();
        EditorUtility.SetDirty(road);
        return road;
    }

    static void AddRoadRule(
        PizzaRoadRuleTile road, int id, Sprite sprite, int[] neighbors,
        UnityEngine.RuleTile.TilingRuleOutput.Transform transform, List<Vector3Int> positions) {

        UnityEngine.RuleTile.TilingRule rule = new UnityEngine.RuleTile.TilingRule();
        rule.m_Id = id;
        rule.m_Sprites = new[] { sprite };
        rule.m_NeighborPositions = new List<Vector3Int>(positions);
        rule.m_Neighbors = new List<int>(neighbors);
        rule.m_RuleTransform = transform;
        rule.m_ColliderType = Tile.ColliderType.None;
        road.m_TilingRules.Add(rule);
    }

    static PizzaMapTheme CreateTheme(
        string id, string label, TileBase ground, TileBase groundAccent, TileBase groundSoft,
        TileBase groundDark, PizzaRoadRuleTile road, TileBase roadEdge, TileBase roadMarking,
        TileBase roadDoubleMarking, TileBase diagonalRoadMarking, TileBase terrain, TileBase secondary,
        Color lightColor, float lightIntensity) {

        string path = ThemeRoot + "/" + id + ".asset";
        PizzaMapTheme theme = AssetDatabase.LoadAssetAtPath<PizzaMapTheme>(path);
        if (theme == null) {
            theme = ScriptableObject.CreateInstance<PizzaMapTheme>();
            AssetDatabase.CreateAsset(theme, path);
        }

        theme.themeId = id;
        theme.displayNameKey = label;
        theme.groundTile = ground;
        theme.groundAccentTile = groundAccent;
        theme.groundSoftTile = groundSoft;
        theme.groundDarkTile = groundDark;
        theme.roadTile = road;
        theme.roadEdgeTile = roadEdge;
        theme.roadMarkingTile = roadMarking;
        theme.roadDoubleMarkingTile = roadDoubleMarking;
        theme.diagonalRoadMarkingTile = diagonalRoadMarking;
        theme.terrainTile = terrain;
        theme.secondaryTerrainTile = secondary;
        theme.globalLightColor = lightColor;
        theme.globalLightIntensity = lightIntensity;
        EditorUtility.SetDirty(theme);
        return theme;
    }

    static void CreateTilePalette(Dictionary<string, TileBase> tiles) {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PalettePath) != null) {
            AssetDatabase.DeleteAsset(PalettePath);
        }

        GameObject palette = new GameObject("PizzaMapTilePalette");
        Grid grid = palette.AddComponent<Grid>();
        grid.cellSize = Vector3.one;

        Tilemap ground = CreatePaletteLayer(palette.transform, "01 Ground Materials");
        Tilemap roads = CreatePaletteLayer(palette.transform, "02 Road Rule Materials");
        Tilemap details = CreatePaletteLayer(palette.transform, "03 Road Details");
        Tilemap terrain = CreatePaletteLayer(palette.transform, "04 Terrain Materials");

        ground.SetTile(new Vector3Int(0, 0, 0), tiles["City_Ground"]);
        ground.SetTile(new Vector3Int(1, 0, 0), tiles["City_GroundAccent"]);
        ground.SetTile(new Vector3Int(2, 0, 0), tiles["City_GroundSoft"]);
        ground.SetTile(new Vector3Int(3, 0, 0), tiles["City_GroundDark"]);
        ground.SetTile(new Vector3Int(4, 0, 0), tiles["Night_Ground"]);
        ground.SetTile(new Vector3Int(5, 0, 0), tiles["Night_GroundAccent"]);
        ground.SetTile(new Vector3Int(6, 0, 0), tiles["Night_GroundSoft"]);
        ground.SetTile(new Vector3Int(7, 0, 0), tiles["Night_GroundDark"]);
        ground.SetTile(new Vector3Int(8, 0, 0), tiles["Village_Ground"]);
        ground.SetTile(new Vector3Int(9, 0, 0), tiles["Village_GroundAccent"]);
        ground.SetTile(new Vector3Int(10, 0, 0), tiles["Village_GroundSoft"]);
        ground.SetTile(new Vector3Int(11, 0, 0), tiles["Village_GroundDark"]);

        roads.SetTile(new Vector3Int(0, 0, 0), tiles["City_Road"]);
        roads.SetTile(new Vector3Int(2, 0, 0), tiles["City_Road"]);
        roads.SetTile(new Vector3Int(4, 0, 0), tiles["City_Road"]);
        roads.SetTile(new Vector3Int(0, 2, 0), tiles["Night_Road"]);
        roads.SetTile(new Vector3Int(2, 2, 0), tiles["Night_Road"]);
        roads.SetTile(new Vector3Int(4, 2, 0), tiles["Night_Road"]);
        roads.SetTile(new Vector3Int(0, 4, 0), tiles["Village_Road"]);
        roads.SetTile(new Vector3Int(2, 4, 0), tiles["Village_Road"]);
        roads.SetTile(new Vector3Int(4, 4, 0), tiles["Village_Road"]);

        details.SetTile(new Vector3Int(0, 0, 0), tiles["City_RoadEdge"]);
        details.SetTile(new Vector3Int(2, 0, 0), tiles["City_RoadMarking"]);
        details.SetTile(new Vector3Int(4, 0, 0), tiles["City_RoadDoubleMarking"]);
        details.SetTile(new Vector3Int(6, 0, 0), tiles["City_DiagonalRoadMarking"]);
        details.SetTile(new Vector3Int(0, 2, 0), tiles["Night_RoadEdge"]);
        details.SetTile(new Vector3Int(2, 2, 0), tiles["Night_RoadMarking"]);
        details.SetTile(new Vector3Int(4, 2, 0), tiles["Night_RoadDoubleMarking"]);
        details.SetTile(new Vector3Int(6, 2, 0), tiles["Night_DiagonalRoadMarking"]);
        details.SetTile(new Vector3Int(0, 4, 0), tiles["Village_RoadEdge"]);
        details.SetTile(new Vector3Int(2, 4, 0), tiles["Village_RoadMarking"]);
        details.SetTile(new Vector3Int(4, 4, 0), tiles["Village_RoadDoubleMarking"]);
        details.SetTile(new Vector3Int(6, 4, 0), tiles["Village_DiagonalRoadMarking"]);

        terrain.SetTile(new Vector3Int(0, 0, 0), tiles["City_Water"]);
        terrain.SetTile(new Vector3Int(2, 0, 0), tiles["City_Dirt"]);
        terrain.SetTile(new Vector3Int(4, 0, 0), tiles["Night_Water"]);
        terrain.SetTile(new Vector3Int(6, 0, 0), tiles["Night_Dirt"]);
        terrain.SetTile(new Vector3Int(8, 0, 0), tiles["Village_Water"]);
        terrain.SetTile(new Vector3Int(10, 0, 0), tiles["Village_Dirt"]);

        PrefabUtility.SaveAsPrefabAsset(palette, PalettePath);
        DestroyImmediate(palette);
    }

    static Tilemap CreatePaletteLayer(Transform parent, string name) {
        GameObject layer = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer));
        layer.transform.SetParent(parent, false);
        layer.GetComponent<TilemapRenderer>().sortingOrder = 0;
        return layer.GetComponent<Tilemap>();
    }

    static List<PizzaMapBlueprint> CreateBuiltInBlueprints(
        PizzaMapTheme nightTheme, PizzaMapTheme villageTheme) {

        PizzaMapDecorationFactory.EnsureAll();
        GameObject house1 = PizzaMapDecorationFactory.Load("MapHouseSmall");
        GameObject house2 = PizzaMapDecorationFactory.Load("MapHouseWide");
        GameObject house3 = PizzaMapDecorationFactory.Load("MapHouseTall");
        GameObject tree1 = PizzaMapDecorationFactory.Load("MapTreeRound");
        GameObject tree2 = PizzaMapDecorationFactory.Load("MapTreeTall");
        GameObject tree3 = PizzaMapDecorationFactory.Load("MapShrub");
        GameObject tree4 = PizzaMapDecorationFactory.Load("MapFlowerBed");
        GameObject fountain = PizzaMapDecorationFactory.Load("MapFountain");
        GameObject bench = PizzaMapDecorationFactory.Load("MapBench");
        GameObject lamp = PizzaMapDecorationFactory.Load("MapLamp");
        GameObject park = PizzaMapDecorationFactory.Load("MapPark");

        return new List<PizzaMapBlueprint> {
            CreateNightTownBlueprint(nightTheme, house1, house2, house3, tree1, tree2, tree3, tree4,
                fountain, bench, lamp, park),
            CreateVillageBlueprint(villageTheme, house1, house2, house3, tree1, tree2, tree3, tree4,
                fountain, bench, lamp, park)
        };
    }

    static PizzaMapBlueprint CreateBlueprint(string id, string sceneName, PizzaMapTheme theme, Vector2Int size) {
        string path = BlueprintRoot + "/MapBlueprint_" + id + ".asset";
        PizzaMapBlueprint blueprint = AssetDatabase.LoadAssetAtPath<PizzaMapBlueprint>(path);
        if (blueprint == null) {
            blueprint = ScriptableObject.CreateInstance<PizzaMapBlueprint>();
            AssetDatabase.CreateAsset(blueprint, path);
        }

        blueprint.blueprintId = id;
        blueprint.sceneName = sceneName;
        blueprint.theme = theme;
        blueprint.mapSize = size;
        blueprint.roadStrokes.Clear();
        blueprint.terrainZones.Clear();
        blueprint.decorations.Clear();
        blueprint.customerPositions.Clear();
        EditorUtility.SetDirty(blueprint);
        return blueprint;
    }

    static PizzaMapBlueprint CreateCityBlueprint(
        PizzaMapTheme theme, GameObject house1, GameObject house2, GameObject house3,
        GameObject tree1, GameObject tree2, GameObject rocks, GameObject car1, GameObject car2) {

        PizzaMapBlueprint blueprint = CreateBlueprint("CityDay", "GameScene", theme, new Vector2Int(60, 40));
        AddRoad(blueprint, 2, new Vector2Int(3, 20), new Vector2Int(56, 20));
        AddRoad(blueprint, 2, new Vector2Int(30, 2), new Vector2Int(30, 38));
        AddRoad(blueprint, 2, new Vector2Int(10, 8), new Vector2Int(10, 32));
        AddRoad(blueprint, 2, new Vector2Int(50, 8), new Vector2Int(50, 32));
        AddRoad(blueprint, 2, new Vector2Int(10, 31), new Vector2Int(50, 31));
        AddRoad(blueprint, 2, new Vector2Int(10, 9), new Vector2Int(50, 9));
        AddRoad(blueprint, 2, new Vector2Int(20, 9), new Vector2Int(20, 20), new Vector2Int(30, 20));
        AddRoad(blueprint, 2, new Vector2Int(40, 20), new Vector2Int(40, 31));
        AddZone(blueprint, new RectInt(0, 0, 8, 7), false);
        AddZone(blueprint, new RectInt(52, 0, 8, 7), false);
        AddZone(blueprint, new RectInt(0, 33, 8, 7), false);
        AddZone(blueprint, new RectInt(52, 33, 8, 7), false);

        blueprint.shopPosition = new Vector2Int(5, 7);
        blueprint.roadsidePosition = new Vector2Int(52, 9);
        blueprint.spawnPosition = new Vector2Int(3, 20);
        blueprint.extractionPosition = new Vector2Int(56, 31);
        AddCustomers(blueprint, new[] {
            new Vector2Int(10, 9), new Vector2Int(20, 9), new Vector2Int(30, 9),
            new Vector2Int(40, 9), new Vector2Int(50, 9), new Vector2Int(10, 20),
            new Vector2Int(30, 20), new Vector2Int(50, 20), new Vector2Int(10, 31),
            new Vector2Int(30, 31), new Vector2Int(50, 31)
        });
        AddStamps(blueprint, new[] {
            Stamp(house1, 4, 4, 0f, 0.9f), Stamp(house2, 14, 4, 90f, 0.9f),
            Stamp(house3, 24, 4, 0f, 0.9f), Stamp(house1, 36, 4, 90f, 0.9f),
            Stamp(house2, 46, 4, 0f, 0.9f), Stamp(house3, 55, 4, 90f, 0.9f),
            Stamp(house2, 4, 35, 180f, 0.9f), Stamp(house3, 16, 35, 270f, 0.9f),
            Stamp(house1, 36, 35, 180f, 0.9f), Stamp(house2, 48, 35, 270f, 0.9f),
            Stamp(tree1, 2, 12, 0f, 0.8f), Stamp(tree2, 6, 27, 0f, 0.8f),
            Stamp(tree1, 54, 13, 0f, 0.8f), Stamp(tree2, 57, 27, 0f, 0.8f),
            Stamp(rocks, 15, 17, 0f, 0.75f), Stamp(car1, 24, 23, 90f, 0.8f),
            Stamp(car2, 45, 17, 270f, 0.8f)
        });
        return blueprint;
    }

    static PizzaMapBlueprint CreateNightTownBlueprint(
        PizzaMapTheme theme, GameObject house1, GameObject house2, GameObject house3,
        GameObject tree1, GameObject tree2, GameObject tree3, GameObject tree4,
        GameObject fountain, GameObject bench, GameObject lamp, GameObject park) {

        PizzaMapBlueprint blueprint = CreateBlueprint("MoonlitTown", "NarrowDistrict", theme, new Vector2Int(128, 88));
        blueprint.cameraReferenceHorizontalWorldSize = 24f;

        AddRoad(blueprint, 2, new Vector2Int(8, 8), new Vector2Int(120, 8), new Vector2Int(120, 80),
            new Vector2Int(8, 80), new Vector2Int(8, 8));
        AddRoad(blueprint, 7, new Vector2Int(64, 5), new Vector2Int(64, 83));
        AddRoad(blueprint, 7, new Vector2Int(5, 44), new Vector2Int(123, 44));
        AddRoad(blueprint, 2, new Vector2Int(16, 18), new Vector2Int(50, 18), new Vector2Int(54, 24),
            new Vector2Int(54, 37), new Vector2Int(16, 37), new Vector2Int(16, 18));
        AddRoad(blueprint, 2, new Vector2Int(76, 18), new Vector2Int(112, 18), new Vector2Int(112, 37),
            new Vector2Int(76, 37), new Vector2Int(76, 18));
        AddRoad(blueprint, 2, new Vector2Int(16, 52), new Vector2Int(50, 52), new Vector2Int(54, 58),
            new Vector2Int(54, 72), new Vector2Int(16, 72), new Vector2Int(16, 52));
        AddRoad(blueprint, 2, new Vector2Int(76, 52), new Vector2Int(112, 52), new Vector2Int(112, 72),
            new Vector2Int(76, 72), new Vector2Int(76, 52));
        AddRoad(blueprint, 2, new Vector2Int(54, 24), new Vector2Int(64, 34));
        AddRoad(blueprint, 2, new Vector2Int(54, 58), new Vector2Int(64, 50));
        AddRoad(blueprint, 2, new Vector2Int(76, 37), new Vector2Int(84, 44));
        AddRoad(blueprint, 2, new Vector2Int(54, 52), new Vector2Int(64, 44));
        AddRoad(blueprint, 2, new Vector2Int(16, 18), new Vector2Int(10, 12));
        AddRoad(blueprint, 2, new Vector2Int(112, 72), new Vector2Int(118, 78));

        AddStyledZone(blueprint, new RectInt(17, 20, 35, 16), PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(77, 20, 34, 16), PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(17, 54, 35, 16), PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(77, 54, 34, 16), PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(28, 23, 22, 11), PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(78, 23, 28, 11), PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(28, 57, 22, 11), PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(78, 57, 28, 11), PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(10, 11, 11, 22), PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(107, 11, 11, 22), PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(10, 55, 11, 21), PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(107, 55, 11, 21), PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(42, 10, 16, 7), PizzaMapBlueprint.TerrainTileStyle.Dirt);
        AddStyledZone(blueprint, new RectInt(70, 71, 18, 7), PizzaMapBlueprint.TerrainTileStyle.Dirt);

        blueprint.shopPosition = new Vector2Int(22, 27);
        blueprint.shopRotationDegrees = 0f;
        blueprint.roadsidePosition = new Vector2Int(116, 76);
        blueprint.roadsideRotationDegrees = 90f;
        blueprint.spawnPosition = new Vector2Int(8, 44);
        blueprint.extractionPosition = new Vector2Int(120, 44);
        AddCustomers(blueprint, new[] {
            new Vector2Int(20, 27), new Vector2Int(34, 27), new Vector2Int(45, 33),
            new Vector2Int(80, 27), new Vector2Int(94, 27), new Vector2Int(107, 33),
            new Vector2Int(20, 61), new Vector2Int(34, 61), new Vector2Int(45, 67),
            new Vector2Int(80, 61), new Vector2Int(94, 61), new Vector2Int(107, 67)
        });
        AddStamps(blueprint, new[] {
            Stamp(house1, 21, 23, 0f, 1f), Stamp(house2, 34, 23, 0f, 0.9f),
            Stamp(house3, 21, 31, 180f, 0.85f), Stamp(house1, 34, 31, 180f, 0.9f),
            Stamp(house2, 80, 23, 0f, 0.9f), Stamp(house3, 94, 23, 0f, 0.85f),
            Stamp(house1, 80, 31, 180f, 0.95f), Stamp(house2, 94, 31, 180f, 0.9f),
            Stamp(house3, 21, 57, 0f, 0.85f), Stamp(house1, 34, 57, 0f, 0.95f),
            Stamp(house2, 21, 65, 180f, 0.9f), Stamp(house3, 34, 65, 180f, 0.85f),
            Stamp(house1, 80, 57, 0f, 0.95f), Stamp(house2, 94, 57, 0f, 0.9f),
            Stamp(house3, 80, 65, 180f, 0.85f), Stamp(house1, 94, 65, 180f, 0.95f),
            Stamp(park, 45, 27, 0f, 0.7f), Stamp(park, 107, 27, 0f, 0.7f),
            Stamp(park, 45, 61, 0f, 0.7f), Stamp(park, 107, 61, 0f, 0.7f),
            Stamp(fountain, 45, 27, 0f, 0.35f), Stamp(fountain, 107, 27, 0f, 0.35f),
            Stamp(fountain, 45, 61, 0f, 0.35f), Stamp(fountain, 107, 61, 0f, 0.35f),
            Stamp(tree1, 12, 20, 0f, 0.95f), Stamp(tree2, 11, 39, 0f, 0.85f),
            Stamp(tree1, 11, 77, 0f, 0.95f), Stamp(tree2, 116, 14, 0f, 0.85f),
            Stamp(tree1, 116, 39, 0f, 0.95f), Stamp(tree2, 116, 68, 0f, 0.85f),
            Stamp(tree1, 28, 15, 0f, 0.75f), Stamp(tree2, 43, 15, 0f, 0.7f),
            Stamp(tree1, 82, 15, 0f, 0.75f), Stamp(tree2, 100, 15, 0f, 0.7f),
            Stamp(tree3, 13, 39, 0f, 0.8f), Stamp(tree3, 116, 34, 0f, 0.8f),
            Stamp(tree4, 29, 39, 0f, 0.8f), Stamp(tree4, 92, 39, 0f, 0.8f),
            Stamp(tree4, 29, 68, 0f, 0.8f), Stamp(tree4, 92, 68, 0f, 0.8f),
            Stamp(bench, 40, 27, 0f, 0.7f), Stamp(bench, 102, 27, 0f, 0.7f),
            Stamp(bench, 40, 61, 0f, 0.7f), Stamp(bench, 102, 61, 0f, 0.7f),
            Stamp(lamp, 107, 14, 0f, 0.5f), Stamp(lamp, 115, 34, 0f, 0.5f),
            Stamp(lamp, 107, 49, 0f, 0.5f), Stamp(lamp, 116, 70, 0f, 0.5f)
        });
        return blueprint;
    }

    static PizzaMapBlueprint CreateVillageBlueprint(
        PizzaMapTheme theme, GameObject house1, GameObject house2, GameObject house3,
        GameObject tree1, GameObject tree2, GameObject tree3, GameObject tree4,
        GameObject fountain, GameObject bench, GameObject lamp, GameObject park) {

        PizzaMapBlueprint blueprint = CreateBlueprint("MeadowVillage", "Expressway", theme, new Vector2Int(144, 96));
        blueprint.cameraReferenceHorizontalWorldSize = 28f;

        AddRoad(blueprint, 2, new Vector2Int(10, 12), new Vector2Int(54, 12), new Vector2Int(62, 20));
        AddRoad(blueprint, 2, new Vector2Int(10, 12), new Vector2Int(10, 42), new Vector2Int(18, 50));
        AddRoad(blueprint, 2, new Vector2Int(134, 12), new Vector2Int(134, 42), new Vector2Int(126, 50));
        AddRoad(blueprint, 7, new Vector2Int(72, 5), new Vector2Int(72, 91));
        AddRoad(blueprint, 7, new Vector2Int(5, 48), new Vector2Int(139, 48));
        AddRoad(blueprint, 2, new Vector2Int(18, 18), new Vector2Int(48, 18), new Vector2Int(58, 28),
            new Vector2Int(58, 38), new Vector2Int(42, 42), new Vector2Int(18, 34), new Vector2Int(18, 18));
        AddRoad(blueprint, 2, new Vector2Int(88, 18), new Vector2Int(118, 18), new Vector2Int(128, 28),
            new Vector2Int(128, 40), new Vector2Int(104, 40), new Vector2Int(88, 32), new Vector2Int(88, 18));
        AddRoad(blueprint, 2, new Vector2Int(18, 58), new Vector2Int(44, 58), new Vector2Int(56, 70),
            new Vector2Int(56, 82), new Vector2Int(18, 82), new Vector2Int(18, 58));
        AddRoad(blueprint, 2, new Vector2Int(88, 58), new Vector2Int(122, 58), new Vector2Int(122, 70),
            new Vector2Int(112, 82), new Vector2Int(88, 82), new Vector2Int(88, 58));
        AddRoad(blueprint, 2, new Vector2Int(58, 38), new Vector2Int(66, 38), new Vector2Int(70, 44));
        AddRoad(blueprint, 2, new Vector2Int(88, 40), new Vector2Int(80, 40), new Vector2Int(74, 44));
        AddRoad(blueprint, 2, new Vector2Int(56, 70), new Vector2Int(64, 58));
        AddRoad(blueprint, 2, new Vector2Int(88, 70), new Vector2Int(80, 58));
        AddRoad(blueprint, 2, new Vector2Int(18, 34), new Vector2Int(10, 42));
        AddRoad(blueprint, 2, new Vector2Int(122, 82), new Vector2Int(134, 86));

        AddStyledZone(blueprint, new RectInt(20, 20, 36, 20), PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(90, 20, 36, 20), PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(20, 60, 35, 20), PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(90, 60, 30, 20), PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(22, 22, 32, 16), PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(92, 22, 32, 16), PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(22, 60, 32, 19), PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(92, 60, 26, 19), PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(12, 12, 38, 5), PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(94, 12, 34, 5), PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(12, 84, 42, 5), PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(92, 84, 34, 5), PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(22, 66, 14, 9), PizzaMapBlueprint.TerrainTileStyle.Water);
        AddStyledZone(blueprint, new RectInt(38, 64, 14, 12), PizzaMapBlueprint.TerrainTileStyle.Dirt);
        AddStyledZone(blueprint, new RectInt(96, 66, 18, 10), PizzaMapBlueprint.TerrainTileStyle.Dirt);

        blueprint.shopPosition = new Vector2Int(24, 28);
        blueprint.shopRotationDegrees = 0f;
        blueprint.roadsidePosition = new Vector2Int(130, 80);
        blueprint.roadsideRotationDegrees = 90f;
        blueprint.spawnPosition = new Vector2Int(10, 48);
        blueprint.extractionPosition = new Vector2Int(134, 48);
        AddCustomers(blueprint, new[] {
            new Vector2Int(28, 24), new Vector2Int(42, 24), new Vector2Int(48, 34),
            new Vector2Int(96, 24), new Vector2Int(110, 24), new Vector2Int(118, 34),
            new Vector2Int(42, 62), new Vector2Int(48, 76),
            new Vector2Int(96, 64), new Vector2Int(108, 64), new Vector2Int(112, 76)
        });
        AddStamps(blueprint, new[] {
            Stamp(house1, 23, 22, 0f, 0.95f), Stamp(house2, 39, 22, 0f, 0.85f),
            Stamp(house3, 23, 32, 180f, 0.8f), Stamp(house1, 39, 32, 180f, 0.9f),
            Stamp(house2, 93, 22, 0f, 0.85f), Stamp(house1, 109, 22, 0f, 0.9f),
            Stamp(house3, 93, 32, 180f, 0.8f), Stamp(house2, 109, 32, 180f, 0.85f),
            Stamp(house2, 40, 61, 0f, 0.85f), Stamp(house1, 30, 61, 0f, 0.8f),
            Stamp(house3, 40, 74, 180f, 0.75f), Stamp(house2, 48, 74, 180f, 0.8f),
            Stamp(house1, 93, 61, 0f, 0.9f), Stamp(house2, 108, 61, 0f, 0.82f),
            Stamp(house3, 93, 74, 180f, 0.75f), Stamp(house1, 108, 74, 180f, 0.9f),
            Stamp(park, 50, 29, 0f, 0.72f), Stamp(park, 120, 29, 0f, 0.68f),
            Stamp(park, 46, 69, 0f, 0.6f), Stamp(park, 116, 70, 0f, 0.65f),
            Stamp(fountain, 50, 29, 0f, 0.35f), Stamp(fountain, 120, 29, 0f, 0.32f),
            Stamp(fountain, 116, 70, 0f, 0.3f),
            Stamp(tree1, 13, 15, 0f, 0.95f), Stamp(tree2, 20, 42, 0f, 0.85f),
            Stamp(tree1, 13, 82, 0f, 0.95f), Stamp(tree2, 130, 15, 0f, 0.85f),
            Stamp(tree1, 130, 40, 0f, 0.95f), Stamp(tree2, 130, 76, 0f, 0.85f),
            Stamp(tree1, 28, 15, 0f, 0.75f), Stamp(tree2, 46, 15, 0f, 0.7f),
            Stamp(tree1, 96, 15, 0f, 0.75f), Stamp(tree2, 116, 15, 0f, 0.7f),
            Stamp(tree3, 18, 43, 0f, 0.8f), Stamp(tree3, 125, 43, 0f, 0.8f),
            Stamp(tree4, 32, 40, 0f, 0.8f), Stamp(tree4, 101, 40, 0f, 0.8f),
            Stamp(tree4, 32, 78, 0f, 0.8f), Stamp(tree4, 101, 78, 0f, 0.8f),
            Stamp(bench, 46, 29, 0f, 0.65f), Stamp(bench, 116, 29, 0f, 0.65f),
            Stamp(bench, 48, 69, 0f, 0.58f), Stamp(bench, 114, 70, 0f, 0.62f),
            Stamp(lamp, 19, 25, 0f, 0.5f), Stamp(lamp, 124, 34, 0f, 0.5f),
            Stamp(lamp, 19, 64, 0f, 0.5f), Stamp(lamp, 124, 64, 0f, 0.5f)
        });
        return blueprint;
    }

    static void AddRoad(PizzaMapBlueprint blueprint, int width, params Vector2Int[] points) {
        PizzaMapBlueprint.RoadStroke stroke = new PizzaMapBlueprint.RoadStroke();
        stroke.width = width;
        stroke.points.AddRange(points);
        blueprint.roadStrokes.Add(stroke);
    }

    static void AddZone(PizzaMapBlueprint blueprint, RectInt area, bool useTerrainTile) {
        PizzaMapBlueprint.TerrainZone zone = new PizzaMapBlueprint.TerrainZone();
        zone.area = area;
        zone.style = useTerrainTile
            ? PizzaMapBlueprint.TerrainTileStyle.Terrain
            : PizzaMapBlueprint.TerrainTileStyle.Secondary;
        zone.softenEdge = false;
        zone.useTerrainTile = useTerrainTile;
        blueprint.terrainZones.Add(zone);
    }

    static void AddAccentZone(PizzaMapBlueprint blueprint, RectInt area) {
        PizzaMapBlueprint.TerrainZone zone = new PizzaMapBlueprint.TerrainZone();
        zone.area = area;
        zone.style = PizzaMapBlueprint.TerrainTileStyle.GroundAccent;
        zone.softenEdge = true;
        zone.useTerrainTile = false;
        zone.useGroundAccentTile = true;
        blueprint.terrainZones.Add(zone);
    }

    static void AddStyledZone(
        PizzaMapBlueprint blueprint, RectInt area, PizzaMapBlueprint.TerrainTileStyle style) {
        PizzaMapBlueprint.TerrainZone zone = new PizzaMapBlueprint.TerrainZone();
        zone.area = area;
        zone.style = style;
        zone.softenEdge = style != PizzaMapBlueprint.TerrainTileStyle.Dirt &&
            style != PizzaMapBlueprint.TerrainTileStyle.Water;
        zone.useTerrainTile = style == PizzaMapBlueprint.TerrainTileStyle.Terrain ||
            style == PizzaMapBlueprint.TerrainTileStyle.Water;
        zone.useGroundAccentTile = style == PizzaMapBlueprint.TerrainTileStyle.GroundAccent;
        blueprint.terrainZones.Add(zone);
    }

    static void AddCustomers(PizzaMapBlueprint blueprint, Vector2Int[] positions) {
        blueprint.customerPositions.AddRange(positions);
    }

    static PizzaMapBlueprint.DecorationStamp Stamp(
        GameObject prefab, int x, int y, float rotationDegrees, float scaleMultiplier) {
        PizzaMapBlueprint.DecorationStamp stamp = new PizzaMapBlueprint.DecorationStamp();
        stamp.prefab = prefab;
        stamp.cell = new Vector2Int(x, y);
        stamp.rotationDegrees = rotationDegrees;
        stamp.scaleMultiplier = scaleMultiplier;
        return stamp;
    }

    static void AddStamps(PizzaMapBlueprint blueprint, params PizzaMapBlueprint.DecorationStamp[][] groups) {
        for (int i = 0; i < groups.Length; i++) {
            if (groups[i] != null) {
                blueprint.decorations.AddRange(groups[i]);
            }
        }
    }

    static PizzaMapSceneRoot GetOrCreateSceneRoot(Scene scene) {
        PizzaMapSceneRoot[] existing = FindSceneComponents<PizzaMapSceneRoot>(scene);
        if (existing.Length > 0) {
            return existing[0];
        }

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++) {
            if (roots[i].GetComponentsInChildren<PizzaCollectPoint>(true).Length > 0) {
                return roots[i].AddComponent<PizzaMapSceneRoot>();
            }
        }

        throw new InvalidOperationException("Could not locate the map root with PizzaCollectPoint children.");
    }

    static T[] FindSceneComponents<T>(Scene scene) where T : Component {
        List<T> found = new List<T>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++) {
            found.AddRange(roots[i].GetComponentsInChildren<T>(true));
        }
        return found.ToArray();
    }

    static void BuildScene(PizzaMapBlueprint blueprint) {
        if (blueprint == null || blueprint.theme == null || blueprint.theme.roadTile == null) {
            throw new InvalidOperationException("Blueprint is missing a theme or road tile.");
        }

        string scenePath = "Assets/Scenes/" + blueprint.sceneName + ".unity";
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        PizzaMapSceneRoot sceneRoot = GetOrCreateSceneRoot(scene);
        List<Transform> services = DetachServices(sceneRoot.transform);

        for (int i = sceneRoot.transform.childCount - 1; i >= 0; i--) {
            Transform child = sceneRoot.transform.GetChild(i);
            if (!services.Contains(child)) {
                DestroyImmediate(child.gameObject);
            }
        }

        GameObject world = new GameObject("Tilemap World");
        world.transform.SetParent(sceneRoot.transform, false);
        world.transform.position = new Vector3(-blueprint.mapSize.x * 0.5f, -blueprint.mapSize.y * 0.5f, 0f);

        Grid grid = world.AddComponent<Grid>();
        grid.cellSize = Vector3.one;

        Tilemap ground = CreateTilemap(world.transform, "Ground Tiles", -30);
        Tilemap terrain = CreateTilemap(world.transform, "Terrain Tiles", -29);
        Tilemap roadEdges = CreateTilemap(world.transform, "Road Edge Details", -28);
        Tilemap roads = CreateTilemap(world.transform, "Road Rule Tiles", -27);
        Tilemap diagonalRoads = CreateTilemap(world.transform, "Diagonal Road Details", -26);
        Tilemap roadMarkings = CreateTilemap(world.transform, "Road Markings", -25);

        PaintRectangle(ground, blueprint.mapSize, blueprint.theme.groundTile);
        PaintTerrain(terrain, blueprint);
        Dictionary<Vector3Int, float> diagonalAngles = new Dictionary<Vector3Int, float>();
        HashSet<Vector3Int> roadCells = PaintRoads(roads, blueprint, diagonalAngles);
        PaintRoadEdges(roadEdges, blueprint, roadCells);
        PaintDiagonalRoadDetails(diagonalRoads, blueprint, diagonalAngles);
        PaintRoadMarkings(roadMarkings, blueprint);

        GameObject decorationsObject = new GameObject("Prefab Decorations");
        decorationsObject.transform.SetParent(sceneRoot.transform, false);
        Transform decorations = decorationsObject.transform;
        PlaceDecorations(decorations, grid, blueprint);
        world.transform.SetSiblingIndex(0);

        sceneRoot.mapGrid = grid;
        sceneRoot.groundTilemap = ground;
        sceneRoot.terrainTilemap = terrain;
        sceneRoot.roadTilemap = roads;
        sceneRoot.roadEdgeTilemap = roadEdges;
        sceneRoot.diagonalRoadTilemap = diagonalRoads;
        sceneRoot.roadMarkingsTilemap = roadMarkings;
        sceneRoot.decorationRoot = decorations;
        sceneRoot.theme = blueprint.theme;
        sceneRoot.blueprintId = blueprint.blueprintId;
        EditorUtility.SetDirty(sceneRoot);

        PlaceGameplayMarkers(scene, grid, blueprint, services);
        ApplyThemeLight(scene, blueprint.theme);
        ApplyMapCamera(scene, blueprint.cameraReferenceHorizontalWorldSize);
        ApplyObstacleBounds(scene, blueprint);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Built tilemap scene " + blueprint.sceneName + " from blueprint " + blueprint.blueprintId + ".");
    }

    static Tilemap CreateTilemap(Transform parent, string name, int sortingOrder) {
        GameObject tilemapObject = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer));
        tilemapObject.transform.SetParent(parent, false);
        tilemapObject.GetComponent<TilemapRenderer>().sortingOrder = sortingOrder;
        return tilemapObject.GetComponent<Tilemap>();
    }

    static void PaintRectangle(Tilemap tilemap, Vector2Int size, TileBase tile) {
        if (tile == null) {
            return;
        }

        for (int y = 0; y < size.y; y++) {
            for (int x = 0; x < size.x; x++) {
                tilemap.SetTile(new Vector3Int(x, y, 0), tile);
            }
        }
    }

    static void PaintTerrain(Tilemap tilemap, PizzaMapBlueprint blueprint) {
        for (int i = 0; i < blueprint.terrainZones.Count; i++) {
            PizzaMapBlueprint.TerrainZone zone = blueprint.terrainZones[i];
            TileBase tile = GetTerrainTile(blueprint.theme, zone);
            if (tile == null) {
                continue;
            }

            for (int y = zone.area.yMin; y < zone.area.yMax; y++) {
                for (int x = zone.area.xMin; x < zone.area.xMax; x++) {
                    if (x >= 0 && y >= 0 && x < blueprint.mapSize.x && y < blueprint.mapSize.y) {
                        if (zone.softenEdge && IsBrokenZoneEdge(zone.area, x, y)) {
                            continue;
                        }

                        tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                    }
                }
            }
        }
    }

    static TileBase GetTerrainTile(PizzaMapTheme theme, PizzaMapBlueprint.TerrainZone zone) {
        switch (zone.style) {
            case PizzaMapBlueprint.TerrainTileStyle.GroundAccent:
                return theme.groundAccentTile;
            case PizzaMapBlueprint.TerrainTileStyle.GroundSoft:
                return theme.groundSoftTile != null ? theme.groundSoftTile : theme.groundTile;
            case PizzaMapBlueprint.TerrainTileStyle.GroundDark:
                return theme.groundDarkTile != null ? theme.groundDarkTile : theme.groundTile;
            case PizzaMapBlueprint.TerrainTileStyle.Water:
                return theme.terrainTile;
            case PizzaMapBlueprint.TerrainTileStyle.Dirt:
                return theme.secondaryTerrainTile;
            case PizzaMapBlueprint.TerrainTileStyle.Secondary:
                return theme.secondaryTerrainTile;
            default:
                return zone.useGroundAccentTile && theme.groundAccentTile != null
                    ? theme.groundAccentTile
                    : zone.useTerrainTile ? theme.terrainTile : theme.secondaryTerrainTile;
        }
    }

    static bool IsBrokenZoneEdge(RectInt area, int x, int y) {
        int edgeDistance = Mathf.Min(Mathf.Min(x - area.xMin, area.xMax - 1 - x),
            Mathf.Min(y - area.yMin, area.yMax - 1 - y));
        if (edgeDistance > 0) {
            return false;
        }

        int pattern = Mathf.Abs((x * 19 + y * 23 + area.xMin * 7 + area.yMin * 11) % 9);
        return pattern > 2;
    }

    static HashSet<Vector3Int> PaintRoads(
        Tilemap tilemap, PizzaMapBlueprint blueprint, Dictionary<Vector3Int, float> diagonalAngles) {
        HashSet<Vector3Int> roadCells = new HashSet<Vector3Int>();
        for (int i = 0; i < blueprint.roadStrokes.Count; i++) {
            PizzaMapBlueprint.RoadStroke stroke = blueprint.roadStrokes[i];
            for (int pointIndex = 0; pointIndex + 1 < stroke.points.Count; pointIndex++) {
                PaintSegment(tilemap, blueprint, stroke.points[pointIndex], stroke.points[pointIndex + 1],
                    stroke.width, roadCells, diagonalAngles);
            }
        }

        return roadCells;
    }

    static void PaintSegment(
        Tilemap tilemap, PizzaMapBlueprint blueprint, Vector2Int start, Vector2Int end, int width,
        HashSet<Vector3Int> roadCells, Dictionary<Vector3Int, float> diagonalAngles) {
        int steps = Mathf.Max(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y));
        bool diagonal = start.x != end.x && start.y != end.y;
        float diagonalAngle = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg - 45f;
        for (int step = 0; step <= steps; step++) {
            float ratio = steps == 0 ? 0f : step / (float)steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(start.x, end.x, ratio));
            int y = Mathf.RoundToInt(Mathf.Lerp(start.y, end.y, ratio));
            int cellWidth = Mathf.Max(1, width);
            int minimumOffset = -(cellWidth / 2);
            int maximumOffset = minimumOffset + cellWidth - 1;
            for (int offsetY = minimumOffset; offsetY <= maximumOffset; offsetY++) {
                for (int offsetX = minimumOffset; offsetX <= maximumOffset; offsetX++) {
                    PaintRoadCell(tilemap, blueprint, x + offsetX, y + offsetY, roadCells);
                }
            }

            if (diagonal && x >= 0 && y >= 0 && x < blueprint.mapSize.x && y < blueprint.mapSize.y) {
                diagonalAngles[new Vector3Int(x, y, 0)] = diagonalAngle;
            }
        }
    }

    static void PaintRoadCell(
        Tilemap tilemap, PizzaMapBlueprint blueprint, int x, int y, HashSet<Vector3Int> roadCells) {
        if (x >= 0 && y >= 0 && x < blueprint.mapSize.x && y < blueprint.mapSize.y) {
            Vector3Int cell = new Vector3Int(x, y, 0);
            tilemap.SetTile(cell, blueprint.theme.roadTile);
            roadCells.Add(cell);
        }
    }

    static void PaintRoadEdges(
        Tilemap tilemap, PizzaMapBlueprint blueprint, HashSet<Vector3Int> roadCells) {
        if (blueprint.theme.roadEdgeTile == null) {
            return;
        }

        HashSet<Vector3Int> edgeCells = new HashSet<Vector3Int>();
        Vector3Int[] directions = {
            Vector3Int.up, Vector3Int.right, Vector3Int.down, Vector3Int.left
        };
        foreach (Vector3Int roadCell in roadCells) {
            for (int i = 0; i < directions.Length; i++) {
                Vector3Int edgeCell = roadCell + directions[i];
                if (edgeCell.x < 0 || edgeCell.y < 0 ||
                    edgeCell.x >= blueprint.mapSize.x || edgeCell.y >= blueprint.mapSize.y ||
                    roadCells.Contains(edgeCell)) {
                    continue;
                }

                edgeCells.Add(edgeCell);
            }
        }

        foreach (Vector3Int edgeCell in edgeCells) {
            tilemap.SetTile(edgeCell, blueprint.theme.roadEdgeTile);
        }
    }

    static void PaintDiagonalRoadDetails(
        Tilemap tilemap, PizzaMapBlueprint blueprint, Dictionary<Vector3Int, float> diagonalAngles) {
        if (blueprint.theme.diagonalRoadMarkingTile == null) {
            return;
        }

        foreach (KeyValuePair<Vector3Int, float> diagonal in diagonalAngles) {
            tilemap.SetTile(diagonal.Key, blueprint.theme.diagonalRoadMarkingTile);
            tilemap.SetTransformMatrix(diagonal.Key, Matrix4x4.TRS(Vector3.zero,
                Quaternion.Euler(0f, 0f, diagonal.Value), Vector3.one));
        }
    }

    static void PaintRoadMarkings(Tilemap tilemap, PizzaMapBlueprint blueprint) {
        for (int i = 0; i < blueprint.roadStrokes.Count; i++) {
            PizzaMapBlueprint.RoadStroke stroke = blueprint.roadStrokes[i];
            TileBase marking = stroke.width >= 5
                ? blueprint.theme.roadDoubleMarkingTile
                : blueprint.theme.roadMarkingTile;
            if (marking == null) {
                continue;
            }

            for (int pointIndex = 0; pointIndex + 1 < stroke.points.Count; pointIndex++) {
                PaintMarkingSegment(tilemap, blueprint, stroke.points[pointIndex],
                    stroke.points[pointIndex + 1], marking);
            }
        }
    }

    static void PaintMarkingSegment(
        Tilemap tilemap, PizzaMapBlueprint blueprint, Vector2Int start, Vector2Int end, TileBase marking) {
        int steps = Mathf.Max(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y));
        if (start.x != end.x && start.y != end.y) {
            return;
        }

        float angle = start.x != end.x ? 90f : 0f;
        for (int step = 0; step <= steps; step++) {
            float ratio = steps == 0 ? 0f : step / (float)steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(start.x, end.x, ratio));
            int y = Mathf.RoundToInt(Mathf.Lerp(start.y, end.y, ratio));
            if (x < 0 || y < 0 || x >= blueprint.mapSize.x || y >= blueprint.mapSize.y) {
                continue;
            }

            Vector3Int cell = new Vector3Int(x, y, 0);
            tilemap.SetTile(cell, marking);
            tilemap.SetTransformMatrix(cell, Matrix4x4.TRS(Vector3.zero,
                Quaternion.Euler(0f, 0f, angle), Vector3.one));
        }
    }

    static void PlaceDecorations(Transform parent, Grid grid, PizzaMapBlueprint blueprint) {
        for (int i = 0; i < blueprint.decorations.Count; i++) {
            PizzaMapBlueprint.DecorationStamp stamp = blueprint.decorations[i];
            if (stamp.prefab == null) {
                continue;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(stamp.prefab) as GameObject;
            if (instance == null) {
                continue;
            }

            GameObject sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            string sourceName = sourcePrefab != null ? sourcePrefab.name : "Decoration";
            instance.name = sourceName + "_" + i.ToString("D2");
            instance.transform.SetParent(parent, true);
            instance.transform.position = GridToWorld(grid, stamp.cell);
            instance.transform.rotation = Quaternion.Euler(0f, 0f, stamp.rotationDegrees);
            instance.transform.localScale *= Mathf.Max(0.01f, stamp.scaleMultiplier);
        }
    }

    static List<Transform> DetachServices(Transform mapRoot) {
        List<Transform> services = new List<Transform>();
        PizzaCollectPoint[] points = mapRoot.GetComponentsInChildren<PizzaCollectPoint>(true);
        for (int i = 0; i < points.Length; i++) {
            Transform service = points[i].transform.parent;
            if (service == null || service == mapRoot || services.Contains(service)) {
                continue;
            }

            services.Add(service);
            service.SetParent(mapRoot, true);
        }
        return services;
    }

    static void PlaceGameplayMarkers(
        Scene scene, Grid grid, PizzaMapBlueprint blueprint, List<Transform> services) {
        for (int i = 0; i < services.Count; i++) {
            Transform service = services[i];
            PizzaCollectPoint point = service.GetComponentInChildren<PizzaCollectPoint>(true);
            if (point == null || point.role == PizzaCollectionPointRole.Custom) {
                continue;
            }

            Vector2Int targetCell = point.role == PizzaCollectionPointRole.Shop
                ? blueprint.shopPosition
                : blueprint.roadsidePosition;
            service.position = GridToWorld(grid, targetCell);
            float rotationDegrees = point.role == PizzaCollectionPointRole.Shop
                ? blueprint.shopRotationDegrees
                : blueprint.roadsideRotationDegrees;
            service.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        }

        PlayerSpawner[] spawners = FindSceneComponents<PlayerSpawner>(scene);
        for (int i = 0; i < spawners.Length; i++) {
            spawners[i].transform.position = GridToWorld(grid, blueprint.spawnPosition);
        }

        ExtractionZone[] extractionZones = FindSceneComponents<ExtractionZone>(scene);
        for (int i = 0; i < extractionZones.Length; i++) {
            extractionZones[i].transform.position = GridToWorld(grid, blueprint.extractionPosition);
        }

        Customer[] customers = FindSceneComponents<Customer>(scene);
        Array.Sort(customers, (first, second) => first.GetInstanceID().CompareTo(second.GetInstanceID()));
        int count = Mathf.Min(customers.Length, blueprint.customerPositions.Count);
        for (int i = 0; i < count; i++) {
            customers[i].transform.position = GridToWorld(grid, blueprint.customerPositions[i]);
        }
    }

    static Vector3 GridToWorld(Grid grid, Vector2Int cell) {
        return grid.transform.TransformPoint(new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f));
    }

    static void ApplyThemeLight(Scene scene, PizzaMapTheme theme) {
        Light2D[] lights = FindSceneComponents<Light2D>(scene);
        for (int i = 0; i < lights.Length; i++) {
            lights[i].color = theme.globalLightColor;
            lights[i].intensity = theme.globalLightIntensity;
            EditorUtility.SetDirty(lights[i]);
        }
    }

    static void ApplyMapCamera(Scene scene, float referenceHorizontalWorldSize) {
        GameplayCameraFraming[] sharedFraming = FindSceneComponents<GameplayCameraFraming>(scene);
        for (int i = 0; i < sharedFraming.Length; i++) {
            sharedFraming[i].enabled = false;
            EditorUtility.SetDirty(sharedFraming[i]);
        }

        CinemachineCamera[] cameras = FindSceneComponents<CinemachineCamera>(scene);
        for (int i = 0; i < cameras.Length; i++) {
            MapCameraFramingOverride framing = cameras[i].GetComponent<MapCameraFramingOverride>();
            if (framing == null) {
                framing = cameras[i].gameObject.AddComponent<MapCameraFramingOverride>();
            }

            SerializedObject serialized = new SerializedObject(framing);
            SerializedProperty reference = serialized.FindProperty("referenceHorizontalWorldSize");
            SerializedProperty minimum = serialized.FindProperty("minimumOrthographicSize");
            if (reference != null) reference.floatValue = referenceHorizontalWorldSize;
            if (minimum != null) minimum.floatValue = 5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(framing);
        }
    }

    static void ApplyObstacleBounds(Scene scene, PizzaMapBlueprint blueprint) {
        ObjectSpawner[] spawners = FindSceneComponents<ObjectSpawner>(scene);
        for (int i = 0; i < spawners.Length; i++) {
            SerializedObject serialized = new SerializedObject(spawners[i]);
            SerializedProperty groups = serialized.FindProperty("obstacleGroups");
            if (groups == null) {
                continue;
            }

            for (int groupIndex = 0; groupIndex < groups.arraySize; groupIndex++) {
                SerializedProperty group = groups.GetArrayElementAtIndex(groupIndex);
                group.FindPropertyRelative("xPos").floatValue = blueprint.mapSize.x * 0.42f;
                group.FindPropertyRelative("yPos").floatValue = blueprint.mapSize.y * 0.42f;
                group.FindPropertyRelative("centerOffsetX").floatValue = 0f;
                group.FindPropertyRelative("centerOffsetY").floatValue = 0f;
                group.FindPropertyRelative("maxObjectLimit").intValue =
                    Mathf.Max(6, blueprint.mapSize.x * blueprint.mapSize.y / 100);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawners[i]);
        }
    }
}
