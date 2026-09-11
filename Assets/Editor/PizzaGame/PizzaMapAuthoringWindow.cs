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
    const string ProtectedPrimarySceneName = "GameScene";

    bool rebuildScenes = true;
    bool rebuildPrimaryScene;

    /// <summary>Opens the map authoring window from the PizzaGame menu.</summary>
    [MenuItem("PizzaGame/Map Authoring/Open Map Authoring Window")]
    public static void OpenWindow() {
        GetWindow<PizzaMapAuthoringWindow>("Pizza Map Authoring");
    }

    /// <summary>Runs the complete tile kit and built-in map generation workflow.</summary>
    [MenuItem("PizzaGame/Map Authoring/Generate Tile Kit and Built-in Maps")]
    public static void GenerateBuiltInMaps() {
        Generate(true, false);
    }

    void OnGUI() {
        EditorGUILayout.LabelField("Pizza Map Authoring", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Creates compatible tile groups, editable blueprints, and built-in secondary map scenes.",
            MessageType.Info);
        rebuildScenes = EditorGUILayout.ToggleLeft("Rebuild built-in scenes", rebuildScenes);
        rebuildPrimaryScene = EditorGUILayout.ToggleLeft(
            "Rebuild primary GameScene (destructive)", rebuildPrimaryScene);
        EditorGUILayout.HelpBox(
            "GameScene is the protected primary gameplay scene. Leave this disabled to keep its hand-authored map intact.",
            MessageType.Warning);

        if (GUILayout.Button("Create Tile Kit")) {
            Generate(rebuildScenes, rebuildPrimaryScene);
        }

        if (GUILayout.Button("Rebuild Scenes From Existing Blueprints")) {
            RebuildExistingBlueprints(rebuildPrimaryScene);
        }
    }

    static void Generate(bool shouldRebuildScenes, bool includePrimaryScene) {
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
            tiles[prefix + "_Road"] = CreateBasicTile(
                prefix + "_Road", LoadSprite(prefix + "_Road_Straight_Rich"));
        }

        cityTheme = CreateTheme(
            "CityDay", "City Day", tiles["City_Ground"], tiles["City_GroundAccent"],
            tiles["City_GroundSoft"], tiles["City_GroundDark"], tiles["City_Road"],
            tiles["City_RoadEdge"], tiles["City_RoadMarking"], tiles["City_RoadDoubleMarking"],
            tiles["City_DiagonalRoadMarking"],
            tiles["City_Water"], tiles["City_Dirt"], Color.white, 1f);

        nightTheme = CreateTheme(
            "MoonlitTown", "Moonlit Town", tiles["Night_Ground"], tiles["Night_GroundAccent"],
            tiles["Night_GroundSoft"], tiles["Night_GroundDark"], tiles["Night_Road"],
            tiles["Night_RoadEdge"], tiles["Night_RoadMarking"], tiles["Night_RoadDoubleMarking"],
            tiles["Night_DiagonalRoadMarking"],
            tiles["Night_Water"], tiles["Night_Dirt"], new Color(0.45f, 0.58f, 0.9f, 1f), 0.85f);

        villageTheme = CreateTheme(
            "MeadowVillage", "Meadow Village", tiles["Village_Ground"], tiles["Village_GroundAccent"],
            tiles["Village_GroundSoft"], tiles["Village_GroundDark"],
            tiles["Village_Road"], tiles["Village_RoadEdge"],
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

    static PizzaMapTheme CreateTheme(
        string id, string label, TileBase ground, TileBase groundAccent, TileBase groundSoft,
        TileBase groundDark, TileBase road, TileBase roadEdge, TileBase roadMarking,
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
        GameObject cityTree1 = PizzaMapDecorationFactory.Load("MapKenneyCityTreeA");
        GameObject cityTree2 = PizzaMapDecorationFactory.Load("MapKenneyCityTreeB");
        GameObject cityTree3 = PizzaMapDecorationFactory.Load("MapKenneyCityTreeC");
        GameObject cityLamp = PizzaMapDecorationFactory.Load("MapKenneyCityLamp");
        GameObject cityCar1 = PizzaMapDecorationFactory.Load("MapKenneyCityCarA");
        GameObject cityCar2 = PizzaMapDecorationFactory.Load("MapKenneyCityCarB");
        GameObject urbanTree1 = PizzaMapDecorationFactory.Load("MapKenneyUrbanTreeA");
        GameObject urbanTree2 = PizzaMapDecorationFactory.Load("MapKenneyUrbanTreeB");
        GameObject urbanTree3 = PizzaMapDecorationFactory.Load("MapKenneyUrbanTreeC");
        GameObject urbanLamp = PizzaMapDecorationFactory.Load("MapKenneyUrbanLamp");
        GameObject urbanCar1 = PizzaMapDecorationFactory.Load("MapKenneyUrbanCarA");
        GameObject urbanCar2 = PizzaMapDecorationFactory.Load("MapKenneyUrbanCarB");
        GameObject apartmentBlock = PizzaMapDecorationFactory.Load("MapApartmentBlock");
        GameObject marketBlock = PizzaMapDecorationFactory.Load("MapMarketBlock");
        GameObject civicHall = PizzaMapDecorationFactory.Load("MapCivicHall");
        GameObject workshop = PizzaMapDecorationFactory.Load("MapWorkshop");
        GameObject cafe = PizzaMapDecorationFactory.Load("MapCafe");
        GameObject residentialLot = PizzaMapDecorationFactory.Load("MapResidentialLot");
        GameObject urbanLot = PizzaMapDecorationFactory.Load("MapUrbanLot");
        GameObject nightPark = PizzaMapDecorationFactory.Load("MapNightPark");

        return new List<PizzaMapBlueprint> {
            CreateNightTownBlueprint(nightTheme, house1, house2, house3, urbanTree1, urbanTree2, urbanTree3,
                tree4, fountain, bench, urbanLamp, park, urbanCar1, urbanCar2, apartmentBlock, marketBlock,
                civicHall, workshop, cafe, residentialLot, urbanLot, nightPark),
            CreateVillageBlueprint(villageTheme, house1, house2, house3, cityTree1, cityTree2, cityTree3,
                tree4, fountain, bench, cityLamp, park, cityCar1, cityCar2, apartmentBlock, marketBlock,
                civicHall, workshop, cafe, residentialLot)
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
        GameObject fountain, GameObject bench, GameObject lamp, GameObject park,
        GameObject parkedCar1, GameObject parkedCar2,
        GameObject apartmentBlock, GameObject marketBlock,
        GameObject civicHall, GameObject workshop, GameObject cafe, GameObject residentialLot,
        GameObject urbanLot, GameObject nightPark) {

        PizzaMapBlueprint blueprint = CreateBlueprint(
            "MoonlitTown", "NarrowDistrict", theme, new Vector2Int(168, 112));
        blueprint.cameraReferenceHorizontalWorldSize = 32f;

        AddRoad(blueprint, 7, new Vector2Int(6, 56), new Vector2Int(162, 56));
        AddRoad(blueprint, 7, new Vector2Int(84, 6), new Vector2Int(84, 106));
        AddRoad(blueprint, 3, new Vector2Int(14, 14), new Vector2Int(154, 14),
            new Vector2Int(154, 98), new Vector2Int(14, 98), new Vector2Int(14, 14));
        AddRoad(blueprint, 3, new Vector2Int(14, 28), new Vector2Int(154, 28));
        AddRoad(blueprint, 3, new Vector2Int(14, 84), new Vector2Int(154, 84));
        AddRoad(blueprint, 3, new Vector2Int(36, 14), new Vector2Int(36, 98));
        AddRoad(blueprint, 3, new Vector2Int(132, 14), new Vector2Int(132, 98));
        AddRoad(blueprint, 2, new Vector2Int(58, 40), new Vector2Int(110, 40),
            new Vector2Int(110, 72), new Vector2Int(58, 72), new Vector2Int(58, 40));
        AddRoad(blueprint, 2, new Vector2Int(18, 36), new Vector2Int(30, 36),
            new Vector2Int(30, 48), new Vector2Int(18, 48), new Vector2Int(18, 36));
        AddRoad(blueprint, 2, new Vector2Int(30, 36), new Vector2Int(36, 36));
        AddRoad(blueprint, 2, new Vector2Int(138, 36), new Vector2Int(150, 36),
            new Vector2Int(150, 48), new Vector2Int(138, 48), new Vector2Int(138, 36));
        AddRoad(blueprint, 2, new Vector2Int(138, 36), new Vector2Int(132, 36));
        AddRoad(blueprint, 2, new Vector2Int(18, 64), new Vector2Int(30, 64),
            new Vector2Int(30, 76), new Vector2Int(18, 76), new Vector2Int(18, 64));
        AddRoad(blueprint, 2, new Vector2Int(30, 76), new Vector2Int(36, 76));
        AddRoad(blueprint, 2, new Vector2Int(138, 64), new Vector2Int(150, 64),
            new Vector2Int(150, 76), new Vector2Int(138, 76), new Vector2Int(138, 64));
        AddRoad(blueprint, 2, new Vector2Int(138, 76), new Vector2Int(132, 76));

        AddStyledZone(blueprint, new RectInt(18, 18, 48, 32),
            PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(102, 18, 48, 32),
            PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(18, 62, 48, 30),
            PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(102, 62, 48, 30),
            PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(44, 32, 22, 14),
            PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(102, 32, 22, 14),
            PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(44, 66, 22, 14),
            PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(102, 66, 22, 14),
            PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(18, 18, 10, 30),
            PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(140, 18, 10, 30),
            PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(18, 64, 10, 26),
            PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(140, 64, 10, 26),
            PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(42, 18, 20, 7),
            PizzaMapBlueprint.TerrainTileStyle.Dirt);
        AddStyledZone(blueprint, new RectInt(106, 87, 20, 7),
            PizzaMapBlueprint.TerrainTileStyle.Dirt);

        blueprint.shopPosition = new Vector2Int(48, 20);
        blueprint.shopRotationDegrees = 0f;
        blueprint.roadsidePosition = new Vector2Int(144, 92);
        blueprint.roadsideRotationDegrees = 90f;
        blueprint.spawnPosition = new Vector2Int(6, 56);
        blueprint.extractionPosition = new Vector2Int(162, 56);
        AddCustomers(blueprint, new[] {
            new Vector2Int(24, 34), new Vector2Int(48, 34), new Vector2Int(54, 44),
            new Vector2Int(24, 52), new Vector2Int(48, 52), new Vector2Int(112, 34),
            new Vector2Int(144, 34), new Vector2Int(120, 44), new Vector2Int(112, 52),
            new Vector2Int(144, 52), new Vector2Int(24, 62), new Vector2Int(48, 62),
            new Vector2Int(54, 70), new Vector2Int(24, 80), new Vector2Int(48, 80),
            new Vector2Int(112, 62), new Vector2Int(144, 62), new Vector2Int(120, 70),
            new Vector2Int(112, 80), new Vector2Int(144, 80), new Vector2Int(54, 90),
            new Vector2Int(120, 90), new Vector2Int(70, 22), new Vector2Int(98, 90)
        });
        AddStamps(blueprint, new[] {
            Stamp(marketBlock, 24, 20, 0f, 0.62f), Stamp(apartmentBlock, 48, 20, 0f, 0.55f),
            Stamp(house1, 24, 38, 0f, 0.75f), Stamp(house2, 48, 38, 0f, 0.72f),
            Stamp(house3, 24, 44, 180f, 0.72f), Stamp(house1, 48, 48, 180f, 0.76f),
            Stamp(marketBlock, 144, 20, 0f, 0.62f), Stamp(apartmentBlock, 112, 20, 0f, 0.55f),
            Stamp(house2, 112, 38, 0f, 0.72f), Stamp(house3, 144, 38, 0f, 0.7f),
            Stamp(house1, 112, 48, 180f, 0.76f), Stamp(house2, 144, 44, 180f, 0.72f),
            Stamp(marketBlock, 24, 90, 180f, 0.62f), Stamp(apartmentBlock, 48, 90, 180f, 0.55f),
            Stamp(house3, 24, 66, 0f, 0.72f), Stamp(house1, 48, 66, 0f, 0.76f),
            Stamp(house2, 24, 70, 180f, 0.7f), Stamp(house3, 48, 76, 180f, 0.74f),
            Stamp(marketBlock, 112, 90, 180f, 0.62f), Stamp(apartmentBlock, 144, 90, 180f, 0.55f),
            Stamp(house1, 112, 66, 0f, 0.76f), Stamp(house2, 144, 66, 0f, 0.72f),
            Stamp(house3, 112, 76, 180f, 0.72f), Stamp(house1, 144, 70, 180f, 0.76f),
            Stamp(nightPark, 52, 46, 0f, 0.48f), Stamp(nightPark, 116, 46, 0f, 0.48f),
            Stamp(nightPark, 52, 66, 0f, 0.48f), Stamp(nightPark, 116, 66, 0f, 0.48f),
            Stamp(fountain, 52, 46, 0f, 0.24f), Stamp(fountain, 116, 46, 0f, 0.24f),
            Stamp(fountain, 52, 66, 0f, 0.24f), Stamp(fountain, 116, 66, 0f, 0.24f),
            Stamp(tree1, 18, 22, 0f, 0.92f), Stamp(tree2, 18, 52, 0f, 0.92f),
            Stamp(tree1, 70, 22, 0f, 0.92f), Stamp(tree2, 70, 52, 0f, 0.92f),
            Stamp(tree1, 98, 22, 0f, 0.92f), Stamp(tree2, 98, 52, 0f, 0.92f),
            Stamp(tree1, 150, 22, 0f, 0.92f), Stamp(tree2, 150, 52, 0f, 0.92f),
            Stamp(tree1, 18, 82, 0f, 0.92f), Stamp(tree2, 70, 82, 0f, 0.92f),
            Stamp(tree1, 98, 82, 0f, 0.92f), Stamp(tree2, 150, 82, 0f, 0.92f),
            Stamp(tree3, 48, 32, 0f, 1f), Stamp(tree3, 120, 32, 0f, 1f),
            Stamp(tree3, 48, 80, 0f, 1f), Stamp(tree3, 120, 80, 0f, 1f),
            Stamp(tree4, 42, 32, 0f, 0.62f), Stamp(tree4, 126, 32, 0f, 0.62f),
            Stamp(tree4, 42, 80, 0f, 0.62f), Stamp(tree4, 126, 80, 0f, 0.62f),
            Stamp(bench, 48, 46, 0f, 0.48f), Stamp(bench, 112, 46, 0f, 0.48f),
            Stamp(bench, 48, 66, 0f, 0.48f), Stamp(bench, 112, 66, 0f, 0.48f),
            Stamp(lamp, 78, 20, 0f, 1.12f), Stamp(lamp, 90, 20, 0f, 1.12f),
            Stamp(lamp, 78, 92, 0f, 1.12f), Stamp(lamp, 90, 92, 0f, 1.12f),
            Stamp(parkedCar1, 20, 22, 90f, 0.95f), Stamp(parkedCar2, 148, 22, 270f, 0.95f),
            Stamp(parkedCar1, 20, 90, 90f, 0.95f), Stamp(parkedCar2, 148, 90, 270f, 0.95f),
            Stamp(parkedCar1, 60, 22, 90f, 0.86f), Stamp(parkedCar2, 108, 22, 270f, 0.86f),
            Stamp(parkedCar1, 60, 90, 90f, 0.86f), Stamp(parkedCar2, 108, 90, 270f, 0.86f),
            Stamp(tree1, 40, 30, 0f, 0.95f), Stamp(tree2, 60, 30, 0f, 0.95f),
            Stamp(tree1, 108, 30, 0f, 0.95f), Stamp(tree2, 128, 30, 0f, 0.95f),
            Stamp(tree1, 40, 82, 0f, 0.95f), Stamp(tree2, 60, 82, 0f, 0.95f),
            Stamp(tree1, 108, 82, 0f, 0.95f), Stamp(tree2, 128, 82, 0f, 0.95f),
            Stamp(lamp, 40, 34, 0f, 0.92f), Stamp(lamp, 128, 34, 0f, 0.92f),
            Stamp(lamp, 40, 78, 0f, 0.92f), Stamp(lamp, 128, 78, 0f, 0.92f)
        });
        AddStamps(blueprint, new[] {
            Stamp(civicHall, 72, 34, 0f, 0.46f), Stamp(workshop, 70, 22, 0f, 0.62f),
            Stamp(cafe, 98, 22, 0f, 0.7f), Stamp(workshop, 70, 78, 180f, 0.62f),
            Stamp(cafe, 98, 78, 180f, 0.7f), Stamp(civicHall, 72, 78, 180f, 0.46f),
            Stamp(workshop, 24, 52, 90f, 0.58f), Stamp(workshop, 152, 52, 270f, 0.58f),
            Stamp(cafe, 42, 30, 0f, 0.62f), Stamp(cafe, 142, 30, 0f, 0.62f),
            Stamp(cafe, 42, 90, 180f, 0.62f), Stamp(cafe, 142, 90, 180f, 0.62f)
        });
        AddStamps(blueprint, new[] {
            Stamp(house1, 42, 34, 0f, 0.56f), Stamp(house2, 54, 34, 0f, 0.52f),
            Stamp(house3, 42, 44, 180f, 0.54f), Stamp(house1, 54, 44, 180f, 0.56f),
            Stamp(house2, 114, 34, 0f, 0.54f), Stamp(house3, 126, 34, 0f, 0.52f),
            Stamp(house1, 114, 44, 180f, 0.56f), Stamp(house2, 126, 44, 180f, 0.54f),
            Stamp(house3, 42, 68, 0f, 0.54f), Stamp(house1, 54, 68, 0f, 0.52f),
            Stamp(house2, 42, 78, 180f, 0.54f), Stamp(house3, 54, 78, 180f, 0.52f),
            Stamp(house1, 114, 68, 0f, 0.56f), Stamp(house2, 126, 68, 0f, 0.52f),
            Stamp(house3, 114, 78, 180f, 0.54f), Stamp(house1, 126, 78, 180f, 0.56f)
        });
        AddStamps(blueprint, new[] {
            Stamp(urbanLot, 24, 20, 0f, 0.98f), Stamp(urbanLot, 48, 20, 0f, 0.98f),
            Stamp(urbanLot, 144, 20, 0f, 0.98f), Stamp(urbanLot, 112, 20, 0f, 0.98f),
            Stamp(urbanLot, 24, 90, 180f, 0.98f), Stamp(urbanLot, 48, 90, 180f, 0.98f),
            Stamp(urbanLot, 112, 90, 180f, 0.98f), Stamp(urbanLot, 144, 90, 180f, 0.98f),
            Stamp(urbanLot, 42, 34, 0f, 0.68f), Stamp(urbanLot, 54, 34, 0f, 0.68f),
            Stamp(urbanLot, 42, 44, 180f, 0.68f), Stamp(urbanLot, 54, 44, 180f, 0.68f),
            Stamp(urbanLot, 114, 34, 0f, 0.68f), Stamp(urbanLot, 126, 34, 0f, 0.68f),
            Stamp(urbanLot, 114, 44, 180f, 0.68f), Stamp(urbanLot, 126, 44, 180f, 0.68f),
            Stamp(urbanLot, 42, 68, 0f, 0.68f), Stamp(urbanLot, 54, 68, 0f, 0.68f),
            Stamp(urbanLot, 42, 78, 180f, 0.68f), Stamp(urbanLot, 54, 78, 180f, 0.68f),
            Stamp(urbanLot, 114, 68, 0f, 0.68f), Stamp(urbanLot, 126, 68, 0f, 0.68f),
            Stamp(urbanLot, 114, 78, 180f, 0.68f), Stamp(urbanLot, 126, 78, 180f, 0.68f)
        });
        AddStamps(blueprint, new[] {
            Stamp(urbanLot, 72, 34, 0f, 0.82f), Stamp(urbanLot, 70, 22, 0f, 0.82f),
            Stamp(urbanLot, 98, 22, 0f, 0.82f), Stamp(urbanLot, 72, 78, 180f, 0.82f),
            Stamp(urbanLot, 98, 78, 180f, 0.82f),
            Stamp(urbanLot, 24, 52, 90f, 0.74f), Stamp(urbanLot, 152, 52, 270f, 0.74f),
            Stamp(urbanLot, 42, 30, 0f, 0.72f), Stamp(urbanLot, 142, 30, 0f, 0.72f),
            Stamp(urbanLot, 42, 90, 180f, 0.72f), Stamp(urbanLot, 142, 90, 180f, 0.72f)
        });
        AddStamps(blueprint, new[] {
            Stamp(civicHall, 72, 48, 0f, 0.36f), Stamp(marketBlock, 96, 48, 0f, 0.55f),
            Stamp(cafe, 72, 64, 180f, 0.6f), Stamp(workshop, 96, 64, 180f, 0.6f),
            Stamp(urbanLot, 72, 48, 0f, 0.7f), Stamp(urbanLot, 96, 48, 0f, 0.7f),
            Stamp(urbanLot, 72, 64, 180f, 0.7f), Stamp(urbanLot, 96, 64, 180f, 0.7f),
            Stamp(tree1, 64, 48, 0f, 0.8f), Stamp(tree2, 104, 48, 0f, 0.8f),
            Stamp(tree1, 64, 64, 0f, 0.8f), Stamp(tree2, 104, 64, 0f, 0.8f),
            Stamp(lamp, 64, 52, 0f, 0.82f), Stamp(lamp, 104, 52, 0f, 0.82f),
            Stamp(lamp, 64, 60, 0f, 0.82f), Stamp(lamp, 104, 60, 0f, 0.82f)
        });
        // Add a readable mixed-use heart just off the central traffic spine. The
        // square is offset from both main roads so its buildings remain on lots
        // and the intersection stays clear for driving.
        AddStamps(blueprint, new[] {
            Stamp(nightPark, 70, 34, 0f, 0.66f), Stamp(fountain, 70, 34, 0f, 0.3f),
            Stamp(bench, 64, 34, 0f, 0.55f), Stamp(bench, 76, 34, 180f, 0.55f),
            Stamp(lamp, 64, 30, 0f, 0.82f), Stamp(lamp, 76, 30, 0f, 0.82f),
            Stamp(marketBlock, 48, 34, 0f, 0.68f), Stamp(cafe, 48, 44, 180f, 0.66f),
            Stamp(apartmentBlock, 118, 34, 0f, 0.62f), Stamp(workshop, 118, 44, 180f, 0.66f),
            Stamp(urbanLot, 48, 34, 0f, 0.78f), Stamp(urbanLot, 48, 44, 180f, 0.78f),
            Stamp(urbanLot, 118, 34, 0f, 0.78f), Stamp(urbanLot, 118, 44, 180f, 0.78f),
            Stamp(tree3, 40, 34, 0f, 0.88f), Stamp(tree2, 126, 34, 0f, 0.88f),
            Stamp(tree1, 40, 44, 0f, 0.88f), Stamp(tree3, 126, 44, 0f, 0.88f),
            Stamp(parkedCar1, 56, 30, 90f, 0.78f), Stamp(parkedCar2, 110, 30, 270f, 0.78f)
        });
        // Fill the long residential blocks with varied house sizes and small
        // yards. These positions sit between the local streets instead of on
        // the main road cells, which keeps the town dense without blocking play.
        AddStamps(blueprint, new[] {
            Stamp(residentialLot, 30, 22, 0f, 0.72f), Stamp(house1, 30, 22, 0f, 0.68f),
            Stamp(residentialLot, 42, 22, 0f, 0.7f), Stamp(house2, 42, 22, 0f, 0.66f),
            Stamp(residentialLot, 70, 22, 0f, 0.72f), Stamp(house3, 70, 22, 0f, 0.68f),
            Stamp(residentialLot, 76, 48, 180f, 0.7f), Stamp(house1, 76, 48, 180f, 0.66f),
            Stamp(residentialLot, 100, 22, 0f, 0.72f), Stamp(house2, 100, 22, 0f, 0.68f),
            Stamp(residentialLot, 138, 22, 0f, 0.7f), Stamp(house3, 138, 22, 0f, 0.66f),
            Stamp(residentialLot, 100, 48, 180f, 0.72f), Stamp(house1, 100, 48, 180f, 0.68f),
            Stamp(residentialLot, 138, 48, 180f, 0.7f), Stamp(house2, 138, 48, 180f, 0.66f),
            Stamp(residentialLot, 30, 70, 0f, 0.72f), Stamp(house3, 30, 70, 0f, 0.68f),
            Stamp(residentialLot, 42, 70, 0f, 0.7f), Stamp(house1, 42, 70, 0f, 0.66f),
            Stamp(residentialLot, 76, 70, 0f, 0.72f), Stamp(house2, 76, 70, 0f, 0.68f),
            Stamp(residentialLot, 76, 98, 180f, 0.7f), Stamp(house3, 76, 98, 180f, 0.66f),
            Stamp(residentialLot, 100, 70, 0f, 0.72f), Stamp(house1, 100, 70, 0f, 0.68f),
            Stamp(residentialLot, 138, 70, 0f, 0.7f), Stamp(house2, 138, 70, 0f, 0.66f),
            Stamp(residentialLot, 100, 98, 180f, 0.72f), Stamp(house3, 100, 98, 180f, 0.68f),
            Stamp(residentialLot, 138, 98, 180f, 0.7f), Stamp(house1, 138, 98, 180f, 0.66f)
        });
        return blueprint;
    }
    static PizzaMapBlueprint CreateVillageBlueprint(
        PizzaMapTheme theme, GameObject house1, GameObject house2, GameObject house3,
        GameObject tree1, GameObject tree2, GameObject tree3, GameObject tree4,
        GameObject fountain, GameObject bench, GameObject lamp, GameObject park,
        GameObject parkedCar1, GameObject parkedCar2,
        GameObject apartmentBlock, GameObject marketBlock,
        GameObject civicHall, GameObject workshop, GameObject cafe, GameObject residentialLot) {

        PizzaMapBlueprint blueprint = CreateBlueprint(
            "MeadowVillage", "Expressway", theme, new Vector2Int(176, 120));
        blueprint.cameraReferenceHorizontalWorldSize = 34f;

        AddRoad(blueprint, 9, new Vector2Int(8, 60), new Vector2Int(168, 60));
        AddRoad(blueprint, 7, new Vector2Int(88, 6), new Vector2Int(88, 114));
        AddRoad(blueprint, 3, new Vector2Int(16, 16), new Vector2Int(160, 16),
            new Vector2Int(160, 104), new Vector2Int(16, 104), new Vector2Int(16, 16));
        // Four broad ramps tie the perimeter expressway to the neighborhood
        // loops. They are part of the main route, so their diagonal lane
        // details remain visible while local two-cell streets stay unmarked.
        AddRoad(blueprint, 5, new Vector2Int(16, 16), new Vector2Int(26, 26));
        AddRoad(blueprint, 5, new Vector2Int(160, 16), new Vector2Int(148, 24));
        AddRoad(blueprint, 5, new Vector2Int(16, 104), new Vector2Int(26, 94));
        AddRoad(blueprint, 5, new Vector2Int(160, 104), new Vector2Int(154, 94));
        AddRoad(blueprint, 2, new Vector2Int(26, 26), new Vector2Int(62, 26),
            new Vector2Int(62, 48), new Vector2Int(46, 48), new Vector2Int(46, 38),
            new Vector2Int(26, 38), new Vector2Int(26, 26));
        AddRoad(blueprint, 2, new Vector2Int(108, 24), new Vector2Int(148, 24),
            new Vector2Int(148, 44), new Vector2Int(126, 44), new Vector2Int(126, 34),
            new Vector2Int(108, 34), new Vector2Int(108, 24));
        AddRoad(blueprint, 2, new Vector2Int(26, 74), new Vector2Int(46, 74),
            new Vector2Int(46, 94), new Vector2Int(70, 94), new Vector2Int(70, 78),
            new Vector2Int(26, 78), new Vector2Int(26, 74));
        AddRoad(blueprint, 2, new Vector2Int(106, 76), new Vector2Int(134, 76),
            new Vector2Int(134, 94), new Vector2Int(154, 94), new Vector2Int(154, 72),
            new Vector2Int(106, 72), new Vector2Int(106, 76));
        AddRoad(blueprint, 2, new Vector2Int(26, 26), new Vector2Int(26, 16));
        AddRoad(blueprint, 2, new Vector2Int(62, 26), new Vector2Int(62, 16));
        AddRoad(blueprint, 2, new Vector2Int(62, 38), new Vector2Int(88, 38));
        AddRoad(blueprint, 2, new Vector2Int(108, 24), new Vector2Int(108, 16));
        AddRoad(blueprint, 2, new Vector2Int(148, 24), new Vector2Int(148, 16));
        AddRoad(blueprint, 2, new Vector2Int(108, 34), new Vector2Int(88, 34));
        AddRoad(blueprint, 2, new Vector2Int(26, 78), new Vector2Int(26, 104));
        AddRoad(blueprint, 2, new Vector2Int(70, 78), new Vector2Int(70, 60));
        AddRoad(blueprint, 2, new Vector2Int(106, 76), new Vector2Int(106, 60));
        AddRoad(blueprint, 2, new Vector2Int(154, 72), new Vector2Int(154, 104));

        AddStyledZone(blueprint, new RectInt(20, 20, 48, 26),
            PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(108, 20, 48, 26),
            PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(20, 66, 48, 26),
            PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(108, 66, 48, 26),
            PizzaMapBlueprint.TerrainTileStyle.GroundSoft);
        AddStyledZone(blueprint, new RectInt(34, 31, 30, 14),
            PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(114, 31, 30, 14),
            PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(34, 75, 30, 14),
            PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(114, 75, 30, 14),
            PizzaMapBlueprint.TerrainTileStyle.GroundAccent);
        AddStyledZone(blueprint, new RectInt(18, 18, 10, 28),
            PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(148, 18, 10, 28),
            PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(18, 74, 10, 26),
            PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(148, 74, 10, 26),
            PizzaMapBlueprint.TerrainTileStyle.GroundDark);
        AddStyledZone(blueprint, new RectInt(34, 18, 24, 7),
            PizzaMapBlueprint.TerrainTileStyle.Dirt);
        AddStyledZone(blueprint, new RectInt(120, 95, 24, 7),
            PizzaMapBlueprint.TerrainTileStyle.Dirt);
        // Small planted courtyards break up the paved blocks. They sit under
        // the four neighborhood parks and remain well clear of the main and
        // local roads, giving the daytime town a deliberate green rhythm.
        AddStyledZone(blueprint, new RectInt(48, 30, 20, 14),
            PizzaMapBlueprint.TerrainTileStyle.Terrain);
        AddStyledZone(blueprint, new RectInt(128, 30, 20, 14),
            PizzaMapBlueprint.TerrainTileStyle.Terrain);
        AddStyledZone(blueprint, new RectInt(48, 78, 20, 14),
            PizzaMapBlueprint.TerrainTileStyle.Terrain);
        AddStyledZone(blueprint, new RectInt(128, 78, 20, 14),
            PizzaMapBlueprint.TerrainTileStyle.Terrain);
        AddStyledZone(blueprint, new RectInt(60, 42, 20, 16),
            PizzaMapBlueprint.TerrainTileStyle.Terrain);

        blueprint.shopPosition = new Vector2Int(36, 22);
        blueprint.shopRotationDegrees = 0f;
        blueprint.roadsidePosition = new Vector2Int(154, 110);
        blueprint.roadsideRotationDegrees = 90f;
        blueprint.spawnPosition = new Vector2Int(8, 60);
        blueprint.extractionPosition = new Vector2Int(168, 60);
        AddCustomers(blueprint, new[] {
            new Vector2Int(34, 22), new Vector2Int(52, 22), new Vector2Int(68, 36),
            new Vector2Int(68, 44), new Vector2Int(34, 52), new Vector2Int(52, 52),
            new Vector2Int(114, 18), new Vector2Int(134, 18), new Vector2Int(152, 34),
            new Vector2Int(152, 44), new Vector2Int(114, 52), new Vector2Int(134, 52),
            new Vector2Int(34, 68), new Vector2Int(52, 68), new Vector2Int(74, 82),
            new Vector2Int(34, 98), new Vector2Int(52, 98), new Vector2Int(74, 88),
            new Vector2Int(114, 68), new Vector2Int(132, 68), new Vector2Int(156, 82),
            new Vector2Int(114, 98), new Vector2Int(132, 98), new Vector2Int(156, 88)
        });
        AddStamps(blueprint, new[] {
            Stamp(house1, 34, 34, 0f, 0.82f), Stamp(house2, 54, 34, 0f, 0.76f),
            Stamp(house3, 34, 42, 180f, 0.76f), Stamp(house1, 54, 42, 180f, 0.8f),
            Stamp(house2, 20, 24, 0f, 0.7f), Stamp(house3, 20, 42, 180f, 0.7f),
            Stamp(house2, 114, 30, 0f, 0.76f), Stamp(house3, 134, 34, 0f, 0.74f),
            Stamp(house1, 114, 42, 180f, 0.8f), Stamp(house2, 134, 42, 180f, 0.76f),
            Stamp(house3, 150, 24, 0f, 0.68f), Stamp(house1, 150, 42, 180f, 0.72f),
            Stamp(house3, 34, 82, 0f, 0.76f), Stamp(house1, 54, 82, 0f, 0.8f),
            Stamp(house2, 34, 86, 180f, 0.74f), Stamp(house3, 54, 86, 180f, 0.78f),
            Stamp(house1, 20, 76, 0f, 0.7f), Stamp(house2, 20, 94, 180f, 0.68f),
            Stamp(house1, 114, 78, 0f, 0.8f), Stamp(house2, 130, 82, 0f, 0.76f),
            Stamp(house3, 114, 86, 180f, 0.76f), Stamp(house1, 128, 86, 180f, 0.8f),
            Stamp(house2, 150, 76, 0f, 0.68f), Stamp(house3, 152, 88, 180f, 0.72f),
            Stamp(park, 54, 38, 0f, 0.55f), Stamp(park, 138, 38, 0f, 0.55f),
            Stamp(park, 58, 88, 0f, 0.55f), Stamp(park, 142, 88, 0f, 0.55f),
            Stamp(fountain, 54, 38, 0f, 0.28f), Stamp(fountain, 138, 38, 0f, 0.28f),
            Stamp(fountain, 58, 88, 0f, 0.28f), Stamp(fountain, 142, 88, 0f, 0.28f),
            Stamp(tree1, 20, 20, 0f, 1.05f), Stamp(tree2, 24, 52, 0f, 1.05f),
            Stamp(tree1, 20, 84, 0f, 1.05f), Stamp(tree2, 152, 20, 0f, 1.05f),
            Stamp(tree1, 152, 52, 0f, 1.05f), Stamp(tree2, 152, 84, 0f, 1.05f),
            Stamp(tree3, 76, 22, 0f, 1.1f), Stamp(tree3, 100, 22, 0f, 1.1f),
            Stamp(tree3, 76, 98, 0f, 1.1f), Stamp(tree3, 100, 98, 0f, 1.1f),
            Stamp(tree4, 46, 29, 0f, 0.7f), Stamp(tree4, 126, 29, 0f, 0.7f),
            Stamp(tree4, 58, 70, 0f, 0.7f), Stamp(tree4, 126, 73, 0f, 0.7f),
            Stamp(bench, 42, 42, 0f, 0.55f), Stamp(bench, 122, 38, 0f, 0.55f),
            Stamp(bench, 42, 82, 0f, 0.55f), Stamp(bench, 122, 82, 0f, 0.55f),
            Stamp(lamp, 80, 22, 0f, 1.2f), Stamp(lamp, 96, 22, 0f, 1.2f),
            Stamp(lamp, 80, 98, 0f, 1.2f), Stamp(lamp, 96, 98, 0f, 1.2f),
            Stamp(parkedCar1, 24, 22, 90f, 1f), Stamp(parkedCar2, 152, 22, 270f, 1f),
            Stamp(parkedCar1, 24, 98, 90f, 1f), Stamp(parkedCar2, 152, 98, 270f, 1f),
            Stamp(marketBlock, 56, 20, 0f, 0.68f), Stamp(apartmentBlock, 116, 20, 0f, 0.58f),
            Stamp(marketBlock, 56, 100, 180f, 0.68f), Stamp(apartmentBlock, 116, 100, 180f, 0.58f),
            Stamp(house1, 42, 22, 0f, 0.56f), Stamp(house2, 128, 22, 0f, 0.54f),
            Stamp(house1, 42, 98, 180f, 0.56f), Stamp(house2, 128, 98, 180f, 0.54f),
            Stamp(tree1, 24, 30, 0f, 0.8f), Stamp(tree2, 24, 44, 0f, 0.8f),
            Stamp(tree1, 72, 34, 0f, 0.75f), Stamp(tree2, 72, 42, 0f, 0.75f),
            Stamp(tree1, 104, 30, 0f, 0.8f), Stamp(tree2, 104, 44, 0f, 0.8f),
            Stamp(tree1, 152, 34, 0f, 0.75f), Stamp(tree2, 152, 42, 0f, 0.75f),
            Stamp(tree1, 24, 74, 0f, 0.8f), Stamp(tree2, 24, 88, 0f, 0.8f),
            Stamp(tree1, 72, 78, 0f, 0.75f), Stamp(tree2, 72, 86, 0f, 0.75f),
            Stamp(tree1, 104, 74, 0f, 0.8f), Stamp(tree2, 104, 88, 0f, 0.8f),
            Stamp(tree1, 152, 78, 0f, 0.75f), Stamp(tree2, 152, 86, 0f, 0.75f)
        });
        AddStamps(blueprint, new[] {
            Stamp(civicHall, 72, 30, 0f, 0.46f), Stamp(workshop, 72, 22, 0f, 0.62f),
            Stamp(workshop, 72, 90, 180f, 0.62f), Stamp(civicHall, 72, 82, 180f, 0.46f),
            Stamp(workshop, 24, 52, 90f, 0.56f), Stamp(workshop, 152, 52, 270f, 0.56f),
            Stamp(cafe, 40, 30, 0f, 0.6f), Stamp(cafe, 136, 30, 0f, 0.6f),
            Stamp(cafe, 40, 90, 180f, 0.6f), Stamp(cafe, 136, 90, 180f, 0.6f)
        });
        AddStamps(blueprint, new[] {
            Stamp(house3, 30, 30, 0f, 0.56f), Stamp(house1, 40, 30, 0f, 0.54f),
            Stamp(house2, 30, 44, 180f, 0.54f), Stamp(house3, 40, 44, 180f, 0.56f),
            Stamp(house1, 116, 30, 0f, 0.55f), Stamp(house2, 124, 30, 0f, 0.52f),
            Stamp(house3, 136, 30, 0f, 0.54f), Stamp(house1, 116, 40, 180f, 0.54f),
            Stamp(house2, 136, 40, 180f, 0.54f),
            Stamp(house1, 30, 82, 0f, 0.54f), Stamp(house2, 40, 82, 0f, 0.52f),
            Stamp(house3, 54, 82, 0f, 0.54f), Stamp(house1, 64, 82, 0f, 0.52f),
            Stamp(house2, 30, 90, 180f, 0.54f), Stamp(house3, 40, 90, 180f, 0.52f),
            Stamp(house1, 54, 90, 180f, 0.54f), Stamp(house2, 64, 90, 180f, 0.52f),
            Stamp(house3, 112, 82, 0f, 0.55f), Stamp(house1, 122, 82, 0f, 0.52f),
            Stamp(house2, 136, 82, 0f, 0.54f), Stamp(house3, 146, 82, 0f, 0.52f),
            Stamp(house1, 112, 90, 180f, 0.54f), Stamp(house2, 122, 90, 180f, 0.52f),
            Stamp(house3, 136, 90, 180f, 0.54f), Stamp(house1, 146, 90, 180f, 0.52f)
        });
        AddStamps(blueprint, new[] {
            Stamp(residentialLot, 34, 34, 0f, 0.76f), Stamp(residentialLot, 54, 34, 0f, 0.76f),
            Stamp(residentialLot, 34, 42, 180f, 0.76f), Stamp(residentialLot, 54, 42, 180f, 0.76f),
            Stamp(residentialLot, 114, 30, 0f, 0.76f), Stamp(residentialLot, 134, 34, 0f, 0.76f),
            Stamp(residentialLot, 114, 42, 180f, 0.76f), Stamp(residentialLot, 134, 42, 180f, 0.76f),
            Stamp(residentialLot, 34, 82, 0f, 0.76f), Stamp(residentialLot, 54, 82, 0f, 0.76f),
            Stamp(residentialLot, 34, 86, 180f, 0.76f), Stamp(residentialLot, 54, 86, 180f, 0.76f),
            Stamp(residentialLot, 114, 78, 0f, 0.76f), Stamp(residentialLot, 130, 82, 0f, 0.76f),
            Stamp(residentialLot, 114, 86, 180f, 0.76f), Stamp(residentialLot, 128, 86, 180f, 0.76f),
            Stamp(residentialLot, 40, 30, 0f, 0.68f), Stamp(residentialLot, 40, 44, 180f, 0.68f),
            Stamp(residentialLot, 124, 30, 0f, 0.68f), Stamp(residentialLot, 136, 40, 180f, 0.68f),
            Stamp(residentialLot, 40, 82, 0f, 0.68f), Stamp(residentialLot, 64, 82, 0f, 0.68f),
            Stamp(residentialLot, 40, 90, 180f, 0.68f), Stamp(residentialLot, 64, 90, 180f, 0.68f),
            Stamp(residentialLot, 122, 82, 0f, 0.68f), Stamp(residentialLot, 146, 82, 0f, 0.68f),
            Stamp(residentialLot, 122, 90, 180f, 0.68f), Stamp(residentialLot, 146, 90, 180f, 0.68f),
            Stamp(park, 58, 34, 0f, 0.42f), Stamp(fountain, 58, 34, 0f, 0.2f),
            Stamp(park, 142, 34, 0f, 0.42f), Stamp(fountain, 142, 34, 0f, 0.2f),
            Stamp(park, 58, 86, 180f, 0.42f), Stamp(fountain, 58, 86, 180f, 0.2f),
            Stamp(park, 142, 86, 180f, 0.42f), Stamp(fountain, 142, 86, 180f, 0.2f),
            Stamp(tree1, 38, 34, 0f, 0.72f), Stamp(tree2, 54, 38, 0f, 0.72f),
            Stamp(tree1, 118, 38, 0f, 0.72f), Stamp(tree2, 134, 38, 0f, 0.72f),
            Stamp(tree1, 38, 86, 0f, 0.72f), Stamp(tree2, 54, 86, 0f, 0.72f),
            Stamp(tree1, 118, 86, 0f, 0.72f), Stamp(tree2, 146, 86, 0f, 0.72f),
            Stamp(lamp, 42, 34, 0f, 0.8f), Stamp(lamp, 50, 42, 180f, 0.8f),
            Stamp(lamp, 120, 30, 0f, 0.8f), Stamp(lamp, 130, 42, 180f, 0.8f),
            Stamp(lamp, 42, 82, 0f, 0.8f), Stamp(lamp, 50, 90, 180f, 0.8f),
            Stamp(lamp, 122, 82, 0f, 0.8f), Stamp(lamp, 130, 90, 180f, 0.8f)
        });
        // A village square sits beside the highway spine instead of in its
        // junction. It gives the large green quadrants a recognizable civic
        // destination while preserving a clear route through the map.
        AddStamps(blueprint, new[] {
            Stamp(park, 70, 50, 0f, 0.86f), Stamp(fountain, 70, 50, 0f, 0.34f),
            Stamp(bench, 64, 50, 0f, 0.62f), Stamp(bench, 76, 50, 180f, 0.62f),
            Stamp(lamp, 64, 46, 0f, 0.9f), Stamp(lamp, 76, 46, 0f, 0.9f),
            Stamp(civicHall, 52, 50, 0f, 0.54f), Stamp(cafe, 52, 40, 180f, 0.66f),
            Stamp(workshop, 120, 50, 0f, 0.66f), Stamp(marketBlock, 120, 40, 0f, 0.72f),
            Stamp(residentialLot, 52, 50, 0f, 0.8f), Stamp(residentialLot, 52, 40, 180f, 0.74f),
            Stamp(residentialLot, 120, 50, 0f, 0.8f), Stamp(residentialLot, 120, 40, 0f, 0.74f),
            Stamp(tree3, 42, 50, 0f, 0.9f), Stamp(tree1, 82, 50, 0f, 0.9f),
            Stamp(tree2, 110, 50, 0f, 0.9f), Stamp(tree3, 132, 50, 0f, 0.9f),
            Stamp(parkedCar1, 58, 46, 90f, 0.78f), Stamp(parkedCar2, 114, 46, 270f, 0.78f)
        });
        // Add a second, irregular row of homes in every quadrant. The spacing
        // leaves the local loop roads and the main cross completely open.
        AddStamps(blueprint, new[] {
            Stamp(residentialLot, 30, 22, 0f, 0.72f), Stamp(house1, 30, 22, 0f, 0.68f),
            Stamp(residentialLot, 44, 22, 0f, 0.7f), Stamp(house2, 44, 22, 0f, 0.66f),
            Stamp(residentialLot, 70, 22, 0f, 0.72f), Stamp(house3, 70, 22, 0f, 0.68f),
            Stamp(residentialLot, 34, 48, 180f, 0.7f), Stamp(house1, 34, 48, 180f, 0.66f),
            Stamp(residentialLot, 48, 48, 180f, 0.72f), Stamp(house3, 48, 48, 180f, 0.68f),
            Stamp(residentialLot, 104, 22, 0f, 0.72f), Stamp(house2, 104, 22, 0f, 0.68f),
            Stamp(residentialLot, 122, 22, 0f, 0.7f), Stamp(house1, 122, 22, 0f, 0.66f),
            Stamp(residentialLot, 148, 22, 0f, 0.72f), Stamp(house3, 148, 22, 0f, 0.68f),
            Stamp(residentialLot, 104, 48, 180f, 0.7f), Stamp(house2, 104, 48, 180f, 0.66f),
            Stamp(residentialLot, 140, 48, 180f, 0.72f), Stamp(house1, 140, 48, 180f, 0.68f),
            Stamp(residentialLot, 30, 72, 0f, 0.72f), Stamp(house2, 30, 72, 0f, 0.68f),
            Stamp(residentialLot, 44, 72, 0f, 0.7f), Stamp(house3, 44, 72, 0f, 0.66f),
            Stamp(residentialLot, 70, 72, 0f, 0.72f), Stamp(house1, 70, 72, 0f, 0.68f),
            Stamp(residentialLot, 34, 98, 180f, 0.7f), Stamp(house2, 34, 98, 180f, 0.66f),
            Stamp(residentialLot, 48, 98, 180f, 0.72f), Stamp(house1, 48, 98, 180f, 0.68f),
            Stamp(residentialLot, 104, 72, 0f, 0.72f), Stamp(house3, 104, 72, 0f, 0.68f),
            Stamp(residentialLot, 122, 72, 0f, 0.7f), Stamp(house1, 122, 72, 0f, 0.66f),
            Stamp(residentialLot, 148, 72, 0f, 0.72f), Stamp(house2, 148, 72, 0f, 0.68f),
            Stamp(residentialLot, 104, 98, 180f, 0.7f), Stamp(house3, 104, 98, 180f, 0.66f),
            Stamp(residentialLot, 122, 98, 180f, 0.72f), Stamp(house2, 122, 98, 180f, 0.68f),
            Stamp(residentialLot, 148, 98, 180f, 0.7f), Stamp(house1, 148, 98, 180f, 0.66f)
        });
        AddStamps(blueprint, new[] {
            Stamp(tree1, 22, 30, 0f, 0.86f), Stamp(tree2, 58, 30, 0f, 0.86f),
            Stamp(tree3, 22, 48, 0f, 0.86f), Stamp(tree1, 58, 48, 0f, 0.86f),
            Stamp(tree2, 100, 30, 0f, 0.86f), Stamp(tree3, 156, 30, 0f, 0.86f),
            Stamp(tree1, 100, 48, 0f, 0.86f), Stamp(tree2, 156, 48, 0f, 0.86f),
            Stamp(tree3, 22, 78, 0f, 0.86f), Stamp(tree1, 58, 78, 0f, 0.86f),
            Stamp(tree2, 22, 100, 0f, 0.86f), Stamp(tree3, 58, 100, 0f, 0.86f),
            Stamp(tree1, 100, 78, 0f, 0.86f), Stamp(tree2, 156, 78, 0f, 0.86f),
            Stamp(tree3, 100, 100, 0f, 0.86f), Stamp(tree1, 156, 100, 0f, 0.86f),
            Stamp(lamp, 62, 24, 0f, 0.86f), Stamp(lamp, 114, 24, 0f, 0.86f),
            Stamp(lamp, 62, 96, 0f, 0.86f), Stamp(lamp, 114, 96, 0f, 0.86f)
        });
        // Use the remaining safe strips inside each loop for a continuous town
        // fabric. Every house is paired with a yard and placed clear of the
        // local roads, so density increases without creating visual blockers.
        AddStamps(blueprint, new[] {
            Stamp(residentialLot, 30, 22, 0f, 0.66f), Stamp(house2, 30, 22, 0f, 0.62f),
            Stamp(residentialLot, 42, 22, 0f, 0.68f), Stamp(house3, 42, 22, 0f, 0.64f),
            Stamp(residentialLot, 54, 22, 0f, 0.66f), Stamp(house1, 54, 22, 0f, 0.62f),
            Stamp(residentialLot, 20, 32, 90f, 0.64f), Stamp(house2, 20, 32, 90f, 0.6f),
            Stamp(residentialLot, 20, 44, 90f, 0.64f), Stamp(house3, 20, 44, 90f, 0.6f),
            Stamp(residentialLot, 72, 32, 270f, 0.66f), Stamp(house1, 72, 32, 270f, 0.62f),
            Stamp(residentialLot, 72, 46, 270f, 0.66f), Stamp(house2, 72, 46, 270f, 0.62f),
            Stamp(residentialLot, 30, 52, 180f, 0.66f), Stamp(house3, 30, 52, 180f, 0.62f),
            Stamp(residentialLot, 42, 52, 180f, 0.68f), Stamp(house1, 42, 52, 180f, 0.64f),
            Stamp(residentialLot, 54, 52, 180f, 0.66f), Stamp(house2, 54, 52, 180f, 0.62f),
            Stamp(residentialLot, 66, 52, 180f, 0.68f), Stamp(house3, 66, 52, 180f, 0.64f),
            Stamp(residentialLot, 100, 20, 0f, 0.66f), Stamp(house1, 100, 20, 0f, 0.62f),
            Stamp(residentialLot, 112, 20, 0f, 0.68f), Stamp(house2, 112, 20, 0f, 0.64f),
            Stamp(residentialLot, 124, 20, 0f, 0.66f), Stamp(house3, 124, 20, 0f, 0.62f),
            Stamp(residentialLot, 100, 50, 0f, 0.64f), Stamp(house1, 100, 50, 0f, 0.6f),
            Stamp(residentialLot, 112, 50, 0f, 0.66f), Stamp(house2, 112, 50, 0f, 0.62f),
            Stamp(residentialLot, 124, 50, 0f, 0.68f), Stamp(house3, 124, 50, 0f, 0.64f),
            Stamp(residentialLot, 136, 50, 0f, 0.66f), Stamp(house1, 136, 50, 0f, 0.62f),
            Stamp(residentialLot, 100, 30, 90f, 0.64f), Stamp(house2, 100, 30, 90f, 0.6f),
            Stamp(residentialLot, 100, 42, 90f, 0.64f), Stamp(house3, 100, 42, 90f, 0.6f),
            Stamp(residentialLot, 154, 30, 270f, 0.64f), Stamp(house1, 154, 30, 270f, 0.6f),
            Stamp(residentialLot, 154, 42, 270f, 0.64f), Stamp(house2, 154, 42, 270f, 0.6f),
            Stamp(residentialLot, 30, 68, 0f, 0.66f), Stamp(house1, 30, 68, 0f, 0.62f),
            Stamp(residentialLot, 42, 68, 0f, 0.68f), Stamp(house2, 42, 68, 0f, 0.64f),
            Stamp(residentialLot, 54, 68, 0f, 0.66f), Stamp(house3, 54, 68, 0f, 0.62f),
            Stamp(residentialLot, 20, 78, 90f, 0.64f), Stamp(house1, 20, 78, 90f, 0.6f),
            Stamp(residentialLot, 20, 90, 90f, 0.64f), Stamp(house2, 20, 90, 90f, 0.6f),
            Stamp(residentialLot, 72, 78, 270f, 0.66f), Stamp(house3, 72, 78, 270f, 0.62f),
            Stamp(residentialLot, 72, 90, 270f, 0.66f), Stamp(house1, 72, 90, 270f, 0.62f),
            Stamp(residentialLot, 30, 100, 180f, 0.66f), Stamp(house2, 30, 100, 180f, 0.62f),
            Stamp(residentialLot, 42, 100, 180f, 0.68f), Stamp(house3, 42, 100, 180f, 0.64f),
            Stamp(residentialLot, 54, 100, 180f, 0.66f), Stamp(house1, 54, 100, 180f, 0.62f),
            Stamp(residentialLot, 100, 68, 0f, 0.66f), Stamp(house2, 100, 68, 0f, 0.62f),
            Stamp(residentialLot, 112, 68, 0f, 0.68f), Stamp(house3, 112, 68, 0f, 0.64f),
            Stamp(residentialLot, 124, 68, 0f, 0.66f), Stamp(house1, 124, 68, 0f, 0.62f),
            Stamp(residentialLot, 136, 68, 0f, 0.68f), Stamp(house2, 136, 68, 0f, 0.64f),
            Stamp(residentialLot, 100, 100, 180f, 0.66f), Stamp(house3, 100, 100, 180f, 0.62f),
            Stamp(residentialLot, 112, 100, 180f, 0.68f), Stamp(house1, 112, 100, 180f, 0.64f),
            Stamp(residentialLot, 124, 100, 180f, 0.66f), Stamp(house2, 124, 100, 180f, 0.62f),
            Stamp(residentialLot, 136, 100, 180f, 0.68f), Stamp(house3, 136, 100, 180f, 0.64f),
            Stamp(residentialLot, 100, 78, 90f, 0.64f), Stamp(house1, 100, 78, 90f, 0.6f),
            Stamp(residentialLot, 100, 90, 90f, 0.64f), Stamp(house2, 100, 90, 90f, 0.6f),
            Stamp(residentialLot, 154, 78, 270f, 0.64f), Stamp(house3, 154, 78, 270f, 0.6f),
            Stamp(residentialLot, 154, 90, 270f, 0.64f), Stamp(house1, 154, 90, 270f, 0.6f)
        });
        // The unbuilt perimeter is treated as a planted town edge rather than
        // an accidental empty border. These trees frame the settlement and
        // keep the play area readable at the larger camera scale.
        AddStamps(blueprint, new[] {
            Stamp(tree1, 24, 8, 0f, 0.86f), Stamp(tree2, 40, 8, 0f, 0.86f),
            Stamp(tree3, 56, 8, 0f, 0.86f), Stamp(tree1, 72, 8, 0f, 0.86f),
            Stamp(tree2, 104, 8, 0f, 0.86f), Stamp(tree3, 120, 8, 0f, 0.86f),
            Stamp(tree1, 136, 8, 0f, 0.86f), Stamp(tree2, 152, 8, 0f, 0.86f),
            Stamp(tree3, 24, 112, 0f, 0.86f), Stamp(tree1, 40, 112, 0f, 0.86f),
            Stamp(tree2, 56, 112, 0f, 0.86f), Stamp(tree3, 72, 112, 0f, 0.86f),
            Stamp(tree1, 104, 112, 0f, 0.86f), Stamp(tree2, 120, 112, 0f, 0.86f),
            Stamp(tree3, 136, 112, 0f, 0.86f), Stamp(tree1, 152, 112, 0f, 0.86f),
            Stamp(tree2, 8, 24, 90f, 0.86f), Stamp(tree3, 8, 40, 90f, 0.86f),
            Stamp(tree1, 8, 80, 90f, 0.86f), Stamp(tree2, 8, 96, 90f, 0.86f),
            Stamp(tree3, 168, 24, 270f, 0.86f), Stamp(tree1, 168, 40, 270f, 0.86f),
            Stamp(tree2, 168, 80, 270f, 0.86f), Stamp(tree3, 168, 96, 270f, 0.86f)
        });
        AddStamps(blueprint, new[] {
            Stamp(apartmentBlock, 80, 22, 0f, 0.66f), Stamp(marketBlock, 96, 98, 180f, 0.76f),
            Stamp(residentialLot, 80, 22, 0f, 0.8f), Stamp(residentialLot, 96, 98, 180f, 0.84f),
            Stamp(lamp, 80, 30, 0f, 0.9f), Stamp(lamp, 96, 90, 0f, 0.9f)
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
        if (blueprint != null && blueprint.preserveAuthoredScene) {
            Debug.Log("Preserved scene-authored map: " + blueprint.sceneName);
            return;
        }

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

        sceneRoot.mapGrid = grid;
        sceneRoot.groundTilemap = ground;
        sceneRoot.terrainTilemap = terrain;
        sceneRoot.roadTilemap = roads;
        sceneRoot.roadEdgeTilemap = roadEdges;
        sceneRoot.diagonalRoadTilemap = diagonalRoads;
        sceneRoot.roadMarkingsTilemap = roadMarkings;

        PaintGround(ground, blueprint);
        PaintTerrain(terrain, blueprint);
        Dictionary<Vector3Int, float> diagonalAngles = new Dictionary<Vector3Int, float>();
        HashSet<Vector3Int> roadCells = PaintRoads(roads, blueprint, diagonalAngles);
        PaintRoadEdges(roadEdges, blueprint, roadCells);
        PaintDiagonalRoadDetails(diagonalRoads, blueprint, diagonalAngles);
        PaintRoadMarkings(roadMarkings, blueprint);

        GameObject decorationsObject = new GameObject("Prefab Decorations");
        decorationsObject.transform.SetParent(sceneRoot.transform, false);
        Transform decorations = decorationsObject.transform;
        PlaceDecorations(decorations, grid, blueprint, roadCells);
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

    static void PaintGround(Tilemap tilemap, PizzaMapBlueprint blueprint) {
        TileBase ground = blueprint.theme.groundTile;
        TileBase soft = blueprint.theme.groundSoftTile;
        TileBase dark = blueprint.theme.groundDarkTile;
        TileBase accent = blueprint.theme.groundAccentTile;
        for (int y = 0; y < blueprint.mapSize.y; y++) {
            for (int x = 0; x < blueprint.mapSize.x; x++) {
                int clusterX = x / 4;
                int clusterY = y / 4;
                int pattern = Mathf.Abs((clusterX * 37 + clusterY * 53 + clusterX * clusterY * 3) % 100);
                TileBase tile = ground;
                if (soft != null && pattern < 18) {
                    tile = soft;
                }
                else if (dark != null && pattern >= 18 && pattern < 25) {
                    tile = dark;
                }
                else if (accent != null && pattern >= 25 && pattern < 31) {
                    tile = accent;
                }

                if (tile != null) {
                    tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
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

        tilemap.RefreshAllTiles();
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
            if (stroke.width < 5) {
                continue;
            }

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

    static void PlaceDecorations(
        Transform parent, Grid grid, PizzaMapBlueprint blueprint, HashSet<Vector3Int> roadCells) {
        int movedBuildings = 0;
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
            if (IsBuildingDecoration(sourceName) && MoveBuildingOffRoad(
                instance, grid, blueprint.mapSize, stamp.cell, roadCells)) {
                movedBuildings++;
            }
        }

        if (movedBuildings > 0) {
            Debug.Log("Moved " + movedBuildings + " building stamps away from drivable road cells.");
        }
    }

    static bool IsBuildingDecoration(string sourceName) {
        return sourceName.StartsWith("MapHouse") || sourceName.StartsWith("MapApartment") ||
            sourceName.StartsWith("MapMarket") || sourceName.StartsWith("MapCivic") ||
            sourceName.StartsWith("MapWorkshop") || sourceName.StartsWith("MapCafe");
    }

    static bool MoveBuildingOffRoad(
        GameObject instance, Grid grid, Vector2Int mapSize, Vector2Int authoredCell,
        HashSet<Vector3Int> roadCells) {
        Physics2D.SyncTransforms();
        if (!BuildingIntersectsRoad(instance, grid, roadCells)) {
            return false;
        }

        for (int radius = 1; radius <= 6; radius++) {
            for (int offsetY = -radius; offsetY <= radius; offsetY++) {
                for (int offsetX = -radius; offsetX <= radius; offsetX++) {
                    if (Mathf.Abs(offsetX) + Mathf.Abs(offsetY) != radius) {
                        continue;
                    }

                    Vector2Int candidate = authoredCell + new Vector2Int(offsetX, offsetY);
                    if (candidate.x < 0 || candidate.y < 0 ||
                        candidate.x >= mapSize.x || candidate.y >= mapSize.y) {
                        continue;
                    }

                    instance.transform.position = GridToWorld(grid, candidate);
                    Physics2D.SyncTransforms();
                    if (!BuildingIntersectsRoad(instance, grid, roadCells)) {
                        return true;
                    }
                }
            }
        }

        instance.transform.position = GridToWorld(grid, authoredCell);
        return false;
    }

    static bool BuildingIntersectsRoad(GameObject instance, Grid grid, HashSet<Vector3Int> roadCells) {
        BoxCollider2D[] colliders = instance.GetComponentsInChildren<BoxCollider2D>(true);
        for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++) {
            Bounds colliderBounds = colliders[colliderIndex].bounds;
            foreach (Vector3Int roadCell in roadCells) {
                Bounds roadBounds = new Bounds(
                    GridToWorld(grid, new Vector2Int(roadCell.x, roadCell.y)),
                    Vector3.one);
                if (colliderBounds.Intersects(roadBounds)) {
                    return true;
                }
            }
        }

        return false;
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
