using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates a small coherent top-down decoration family for the generated secondary maps.
/// The factory keeps building, park and street-furniture art in the same point-filtered style as
/// the generated tile kit, so map authors are not forced to reuse unrelated primary-scene assets.
/// </summary>
public static class PizzaMapDecorationFactory {
    const string KitRoot = "Assets/TileSets/PizzaMapKit";
    const string TextureRoot = KitRoot + "/Textures/Decorations";
    const string PrefabRoot = KitRoot + "/Prefabs";
    const string RoguelikeTilesRoot = "Assets/StoreAssets/Kenney/RoguelikeCity/Tiles";
    const string UrbanTilesRoot = "Assets/StoreAssets/Kenney/RPGUrban/Tiles";

    /// <summary>Creates all reusable generated decoration prefabs if they do not already exist.</summary>
    public static void EnsureAll() {
        EnsureFolders();
        EnsureAsset("MapHouseSmall", 160, 128, texture => PaintHouse(texture,
            new Color32(116, 61, 52, 255), new Color32(198, 162, 116, 255)), true, true);
        EnsureAsset("MapHouseWide", 192, 128, texture => PaintHouse(texture,
            new Color32(73, 83, 101, 255), new Color32(169, 179, 183, 255)), true, true);
        EnsureAsset("MapHouseTall", 128, 160, texture => PaintHouse(texture,
            new Color32(145, 88, 54, 255), new Color32(207, 171, 119, 255)), true, true);
        EnsureAsset("MapApartmentBlock", 224, 160, texture => PaintApartmentBlock(texture,
            new Color32(61, 73, 92, 255), new Color32(177, 187, 183, 255)), true, true);
        EnsureAsset("MapMarketBlock", 160, 128, texture => PaintMarketBlock(texture,
            new Color32(87, 57, 47, 255), new Color32(203, 163, 103, 255)), true, true);
        EnsureAsset("MapCivicHall", 256, 176, PaintCivicHall, true, true);
        EnsureAsset("MapWorkshop", 176, 128, PaintWorkshop, true, true);
        EnsureAsset("MapCafe", 144, 112, PaintCafe, true, true);
        EnsureAsset("MapResidentialLot", 192, 128, PaintResidentialLot, true, false, 2);
        EnsureAsset("MapUrbanLot", 192, 128, PaintUrbanLot, true, false, 2);
        EnsureAsset("MapPark", 192, 160, PaintPark);
        EnsureAsset("MapNightPark", 224, 176, PaintNightPark, true, false, 2);
        EnsureAsset("MapTreeRound", 64, 64, texture => PaintTree(texture, false));
        EnsureAsset("MapTreeTall", 64, 80, texture => PaintTree(texture, true));
        EnsureAsset("MapShrub", 48, 48, PaintShrub);
        EnsureAsset("MapBench", 64, 32, PaintBench);
        EnsureAsset("MapLamp", 32, 80, PaintLamp);
        EnsureAsset("MapFlowerBed", 96, 48, PaintFlowerBed);
        EnsureAsset("MapFountain", 64, 64, PaintFountain);
        EnsureExternalPrefabs();
    }

    /// <summary>Loads a generated map decoration prefab by its stable kit name.</summary>
    /// <param name="prefabName">Stable prefab name returned by the authoring blueprint.</param>
    /// <returns>The generated prefab, or null when the factory has not been run yet.</returns>
    public static GameObject Load(string prefabName) {
        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + "/" + prefabName + ".prefab");
    }

    static void EnsureFolders() {
        EnsureFolder("Assets", "TileSets");
        EnsureFolder(KitRoot, "Textures");
        EnsureFolder(TextureRoot.Substring(0, TextureRoot.LastIndexOf('/')), "Decorations");
        EnsureFolder(KitRoot, "Prefabs");
    }

    static void EnsureFolder(string parent, string child) {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    static void EnsureAsset(string name, int width, int height, Action<Texture2D> painter,
        bool rebuildTexture = false, bool addCollider = false, int sortingOrder = 3) {
        string texturePath = TextureRoot + "/" + name + ".png";
        if (rebuildTexture || !File.Exists(ToAbsolutePath(texturePath))) {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            Color32[] clear = new Color32[width * height];
            for (int i = 0; i < clear.Length; i++) {
                clear[i] = new Color32(0, 0, 0, 0);
            }

            texture.SetPixels32(clear);
            painter(texture);
            texture.Apply();
            File.WriteAllBytes(ToAbsolutePath(texturePath), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
        string prefabPath = PrefabRoot + "/" + name + ".prefab";
        Sprite sprite = LoadSpriteAsset(texturePath);
        if (sprite == null) {
            throw new InvalidOperationException("Generated decoration sprite was not imported: " + texturePath);
        }

        GameObject decoration = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        bool existingPrefab = decoration != null;
        GameObject prefabContents = existingPrefab
            ? PrefabUtility.LoadPrefabContents(prefabPath)
            : new GameObject(name);
        SpriteRenderer renderer = prefabContents.GetComponent<SpriteRenderer>();
        if (renderer == null) {
            renderer = prefabContents.AddComponent<SpriteRenderer>();
        }

        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
        if (addCollider) {
            BoxCollider2D collider = prefabContents.GetComponent<BoxCollider2D>();
            if (collider == null) {
                collider = prefabContents.AddComponent<BoxCollider2D>();
            }

            collider.isTrigger = false;
            collider.size = renderer.sprite.bounds.size * 0.78f;
            collider.offset = renderer.sprite.bounds.center;
        }

        PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
        if (existingPrefab) {
            PrefabUtility.UnloadPrefabContents(prefabContents);
        }
        else {
            UnityEngine.Object.DestroyImmediate(prefabContents);
        }
    }

    static Sprite LoadSpriteAsset(string texturePath) {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(texturePath);
        for (int i = 0; i < assets.Length; i++) {
            Sprite sprite = assets[i] as Sprite;
            if (sprite != null) {
                return sprite;
            }
        }

        return null;
    }

    static string ToAbsolutePath(string assetPath) {
        string relative = assetPath.Substring("Assets/".Length)
            .Replace("/", Path.DirectorySeparatorChar.ToString());
        return Path.Combine(Application.dataPath, relative);
    }

    static void EnsureExternalPrefabs() {
        EnsureExternalSpritePrefab("MapKenneyCityTreeA", RoguelikeTilesRoot, 512, 1.45f, false);
        EnsureExternalSpritePrefab("MapKenneyCityTreeB", RoguelikeTilesRoot, 514, 1.45f, false);
        EnsureExternalSpritePrefab("MapKenneyCityTreeC", RoguelikeTilesRoot, 516, 1.35f, false);
        EnsureExternalSpritePrefab("MapKenneyCityLamp", RoguelikeTilesRoot, 500, 1.3f, false);
        EnsureExternalSpritePrefab("MapKenneyCityCarA", RoguelikeTilesRoot, 549, 1.25f, true);
        EnsureExternalSpritePrefab("MapKenneyCityCarB", RoguelikeTilesRoot, 551, 1.25f, true);

        EnsureExternalSpritePrefab("MapKenneyUrbanTreeA", UrbanTilesRoot, 259, 1.45f, false);
        EnsureExternalSpritePrefab("MapKenneyUrbanTreeB", UrbanTilesRoot, 260, 1.45f, false);
        EnsureExternalSpritePrefab("MapKenneyUrbanTreeC", UrbanTilesRoot, 263, 1.3f, false);
        EnsureExternalSpritePrefab("MapKenneyUrbanLamp", UrbanTilesRoot, 165, 1.25f, false);
        EnsureExternalSpritePrefab("MapKenneyUrbanCarA", UrbanTilesRoot, 476, 1.2f, true);
        EnsureExternalSpritePrefab("MapKenneyUrbanCarB", UrbanTilesRoot, 479, 1.2f, true);
    }

    static void EnsureExternalSpritePrefab(
        string name, string tilesRoot, int tileIndex, float baseScale, bool addCollider) {
        string texturePath = tilesRoot + "/tile_" + tileIndex.ToString("D4") + ".png";
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null) {
            throw new InvalidOperationException("External decoration is not a texture: " + texturePath);
        }

        bool changed = importer.textureType != TextureImporterType.Sprite ||
            importer.spriteImportMode != SpriteImportMode.Single ||
            !Mathf.Approximately(importer.spritePixelsPerUnit, 16f) ||
            importer.filterMode != FilterMode.Point ||
            importer.textureCompression != TextureImporterCompression.Uncompressed ||
            importer.mipmapEnabled;
        if (changed) {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16f;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
        if (sprite == null) {
            throw new InvalidOperationException("External decoration sprite was not imported: " + texturePath);
        }

        string prefabPath = PrefabRoot + "/" + name + ".prefab";
        GameObject decoration = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        bool existingPrefab = decoration != null;
        GameObject prefabContents = existingPrefab
            ? PrefabUtility.LoadPrefabContents(prefabPath)
            : new GameObject(name);
        SpriteRenderer renderer = prefabContents.GetComponent<SpriteRenderer>();
        if (renderer == null) {
            renderer = prefabContents.AddComponent<SpriteRenderer>();
        }

        renderer.sprite = sprite;
        renderer.sortingOrder = 3;
        prefabContents.transform.localScale = Vector3.one * baseScale;
        if (addCollider) {
            BoxCollider2D collider = prefabContents.GetComponent<BoxCollider2D>();
            if (collider == null) {
                collider = prefabContents.AddComponent<BoxCollider2D>();
            }

            collider.isTrigger = false;
            collider.size = renderer.sprite.bounds.size * 0.8f;
            collider.offset = renderer.sprite.bounds.center;
        }

        PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
        if (existingPrefab) {
            PrefabUtility.UnloadPrefabContents(prefabContents);
        }
        else {
            UnityEngine.Object.DestroyImmediate(prefabContents);
        }
    }

    static void PaintHouse(Texture2D texture, Color32 roof, Color32 wall) {
        int width = texture.width;
        int height = texture.height;
        Color32 outline = new Color32(34, 31, 34, 255);
        Color32 deepShadow = new Color32(18, 24, 24, 190);
        Color32 roofShadow = Darken(roof, 0.68f);
        Color32 roofHighlight = Lighten(roof, 1.18f);
        Color32 facadeShadow = Darken(wall, 0.76f);
        Color32 facadeHighlight = Lighten(wall, 1.08f);
        Color32 windowFrame = new Color32(57, 48, 44, 255);
        Color32 windowGlass = new Color32(107, 155, 163, 255);

        int left = Mathf.Max(7, width / 12);
        int right = width - left - 1;
        int frontTop = Mathf.Clamp(Mathf.RoundToInt(height * 0.48f), 46, height - 42);
        int bottom = height - Mathf.Max(9, height / 12);

        FillRect(texture, left + 4, 7, right + 2, bottom + 2, deepShadow);
        FillRect(texture, left, 12, right, bottom, new Color32(79, 88, 67, 255));
        DrawRect(texture, left, 12, right, bottom, outline);

        int roofLeft = left + 7;
        int roofRight = right - 7;
        int roofTop = 16;
        int roofBottom = frontTop + 8;
        FillRect(texture, roofLeft + 7, roofTop - 4, roofRight - 7, roofBottom + 4, outline);
        FillRect(texture, roofLeft + 3, roofTop, roofRight - 3, roofBottom, roofShadow);
        FillRect(texture, roofLeft, roofTop + 6, roofRight, roofBottom - 2, roof);
        DrawRect(texture, roofLeft, roofTop + 6, roofRight, roofBottom - 2, outline);
        FillRect(texture, roofLeft + 7, roofTop + 10, roofRight - 7, roofTop + 14, roofHighlight);
        FillRect(texture, roofLeft + 7, roofTop + 25, roofRight - 7, roofTop + 28, roofShadow);
        FillRect(texture, roofLeft + 11, roofBottom - 7, roofRight - 11, roofBottom - 3, outline);

        int chimneyX = Mathf.Clamp(width - width / 4, roofLeft + 10, roofRight - 13);
        FillRect(texture, chimneyX, roofTop - 1, chimneyX + 9, roofTop + 14, outline);
        FillRect(texture, chimneyX + 3, roofTop + 2, chimneyX + 7, roofTop + 12, facadeShadow);
        FillRect(texture, chimneyX - 2, roofTop - 4, chimneyX + 11, roofTop - 1, roofHighlight);

        FillRect(texture, left + 7, frontTop, right - 7, bottom - 4, outline);
        FillRect(texture, left + 11, frontTop + 4, right - 11, bottom - 8, wall);
        FillRect(texture, left + 11, frontTop + 4, right - 11, frontTop + 10, facadeHighlight);
        FillRect(texture, left + 11, bottom - 18, right - 11, bottom - 8, facadeShadow);
        FillRect(texture, left + 7, bottom - 8, right - 7, bottom - 4, outline);

        int windowWidth = Mathf.Clamp(width / 8, 13, 24);
        int windowHeight = Mathf.Clamp(height / 10, 11, 16);
        int windowY = frontTop + Mathf.Clamp(height / 9, 13, 21);
        DrawHouseWindow(texture, width / 4 - windowWidth / 2, windowY,
            windowWidth, windowHeight, windowFrame, windowGlass);
        DrawHouseWindow(texture, width * 3 / 4 - windowWidth / 2, windowY,
            windowWidth, windowHeight, windowFrame, windowGlass);

        int doorWidth = Mathf.Clamp(width / 9, 14, 22);
        int doorHeight = Mathf.Clamp(height / 6, 22, 30);
        int doorX = width / 2 - doorWidth / 2;
        int doorY = bottom - doorHeight - 4;
        FillRect(texture, doorX - 3, doorY - 3, doorX + doorWidth + 2, bottom - 3, outline);
        FillRect(texture, doorX, doorY, doorX + doorWidth - 1, bottom - 5, new Color32(78, 52, 42, 255));
        FillRect(texture, doorX + 3, doorY + 3, doorX + doorWidth - 4, doorY + 6, roofHighlight);
        FillRect(texture, doorX + doorWidth - 5, doorY + doorHeight / 2, doorX + doorWidth - 3,
            doorY + doorHeight / 2 + 2, new Color32(235, 194, 78, 255));

        int pathLeft = doorX + doorWidth / 2 - 5;
        FillRect(texture, pathLeft, bottom - 2, pathLeft + 9, height - 1, new Color32(166, 142, 104, 255));
        FillRect(texture, pathLeft + 2, bottom - 2, pathLeft + 7, height - 1, new Color32(201, 174, 126, 255));
        FillRect(texture, left + 8, bottom - 5, left + 19, bottom - 2, new Color32(65, 95, 54, 255));
        FillRect(texture, right - 18, bottom - 5, right - 7, bottom - 2, new Color32(65, 95, 54, 255));
        SetPixelSafe(texture, left + 12, bottom - 7, new Color32(219, 126, 90, 255));
        SetPixelSafe(texture, right - 13, bottom - 7, new Color32(238, 201, 87, 255));
    }

    static void DrawHouseWindow(
        Texture2D texture, int x, int y, int width, int height, Color32 frame, Color32 glass) {
        FillRect(texture, x - 2, y - 2, x + width + 1, y + height + 1, frame);
        FillRect(texture, x, y, x + width - 1, y + height - 1, glass);
        FillRect(texture, x + width / 2 - 1, y, x + width / 2 + 1, y + height - 1, frame);
        FillRect(texture, x, y + height / 2 - 1, x + width - 1, y + height / 2 + 1, frame);
        FillRect(texture, x + 2, y + 2, x + width / 2 - 2, y + 3, new Color32(183, 218, 208, 255));
    }

    static void PaintApartmentBlock(Texture2D texture, Color32 roof, Color32 wall) {
        int width = texture.width;
        int height = texture.height;
        Color32 outline = new Color32(29, 31, 35, 255);
        Color32 roofShadow = Darken(roof, 0.68f);
        Color32 roofHighlight = Lighten(roof, 1.2f);
        Color32 wallShadow = Darken(wall, 0.74f);
        Color32 wallHighlight = Lighten(wall, 1.1f);

        FillRect(texture, 10, 8, width - 7, height - 6, new Color32(18, 23, 26, 190));
        FillRect(texture, 7, 12, width - 10, height - 11, new Color32(72, 78, 73, 255));
        DrawRect(texture, 7, 12, width - 10, height - 11, outline);

        int roofLeft = 15;
        int roofRight = width - 16;
        int roofTop = 17;
        int roofBottom = 80;
        FillRect(texture, roofLeft + 5, roofTop - 4, roofRight - 5, roofBottom + 8, outline);
        FillRect(texture, roofLeft, roofTop + 2, roofRight, roofBottom, roofShadow);
        FillRect(texture, roofLeft + 5, roofTop + 8, roofRight - 5, roofBottom - 2, roof);
        DrawRect(texture, roofLeft + 5, roofTop + 8, roofRight - 5, roofBottom - 2, outline);
        FillRect(texture, roofLeft + 12, roofTop + 15, roofRight - 12, roofTop + 20, roofHighlight);
        FillRect(texture, roofLeft + 12, roofTop + 40, roofRight - 12, roofTop + 44, roofShadow);

        FillRect(texture, 22, 29, 47, 42, new Color32(84, 91, 96, 255));
        FillRect(texture, width - 49, 29, width - 24, 42, new Color32(84, 91, 96, 255));
        FillRect(texture, 26, 32, 43, 39, new Color32(125, 142, 151, 255));
        FillRect(texture, width - 45, 32, width - 28, 39, new Color32(125, 142, 151, 255));

        int facadeTop = 79;
        FillRect(texture, 14, facadeTop, width - 17, height - 16, outline);
        FillRect(texture, 20, facadeTop + 6, width - 23, height - 22, wall);
        FillRect(texture, 20, facadeTop + 6, width - 23, facadeTop + 15, wallHighlight);
        FillRect(texture, 20, height - 34, width - 23, height - 22, wallShadow);

        int windowWidth = 18;
        int windowHeight = 14;
        int[] windowXs = { 29, 64, width - 82, width - 47 };
        for (int i = 0; i < windowXs.Length; i++) {
            DrawHouseWindow(texture, windowXs[i], facadeTop + 20, windowWidth, windowHeight,
                new Color32(54, 53, 55, 255), new Color32(104, 157, 170, 255));
        }

        int doorX = width / 2 - 13;
        FillRect(texture, doorX - 3, height - 55, doorX + 28, height - 16, outline);
        FillRect(texture, doorX, height - 51, doorX + 24, height - 20, new Color32(71, 55, 48, 255));
        FillRect(texture, doorX + 4, height - 47, doorX + 20, height - 42, roofHighlight);
        FillRect(texture, doorX + 19, height - 36, doorX + 21, height - 33, new Color32(238, 193, 76, 255));
        FillRect(texture, doorX - 9, height - 13, doorX + 30, height - 9, new Color32(175, 148, 106, 255));

        FillRect(texture, width - 48, 24, width - 27, 30, outline);
        FillRect(texture, width - 45, 27, width - 30, 29, new Color32(139, 147, 142, 255));
        FillRect(texture, width - 45, 49, width - 30, 54, outline);
        FillRect(texture, width - 42, 52, width - 33, 100, new Color32(101, 111, 115, 255));
    }

    static void PaintMarketBlock(Texture2D texture, Color32 roof, Color32 wall) {
        int width = texture.width;
        int height = texture.height;
        Color32 outline = new Color32(38, 32, 34, 255);
        Color32 roofShadow = Darken(roof, 0.68f);
        Color32 roofHighlight = Lighten(roof, 1.22f);

        FillRect(texture, 9, 8, width - 8, height - 7, new Color32(18, 22, 22, 180));
        FillRect(texture, 6, 12, width - 11, height - 11, new Color32(92, 83, 65, 255));
        DrawRect(texture, 6, 12, width - 11, height - 11, outline);
        FillRect(texture, 13, 17, width - 18, 61, roofShadow);
        FillRect(texture, 17, 21, width - 22, 63, roof);
        DrawRect(texture, 17, 21, width - 22, 63, outline);
        FillRect(texture, 23, 27, width - 28, 32, roofHighlight);
        FillRect(texture, 23, 48, width - 28, 53, roofShadow);
        FillRect(texture, 13, 76, width - 18, 84, outline);
        FillRect(texture, 19, 80, width - 24, 88, wall);

        int windowY = 94;
        int windowWidth = 25;
        for (int i = 0; i < 3; i++) {
            int x = 18 + i * 39;
            FillRect(texture, x - 2, windowY - 2, x + windowWidth + 2, windowY + 18, outline);
            FillRect(texture, x, windowY, x + windowWidth, windowY + 15, new Color32(101, 157, 164, 255));
            FillRect(texture, x + windowWidth / 2 - 1, windowY, x + windowWidth / 2 + 1, windowY + 15, outline);
        }

        FillRect(texture, 15, 65, width - 20, 73, new Color32(191, 76, 61, 255));
        FillRect(texture, 19, 67, width - 24, 70, new Color32(237, 181, 85, 255));
        FillRect(texture, width / 2 - 10, height - 28, width / 2 + 10, height - 12, outline);
        FillRect(texture, width / 2 - 7, height - 25, width / 2 + 7, height - 14, new Color32(75, 57, 48, 255));
        FillRect(texture, width / 2 + 4, height - 21, width / 2 + 6, height - 19, new Color32(240, 195, 77, 255));
        FillRect(texture, 19, height - 9, width - 24, height - 5, new Color32(173, 146, 105, 255));
    }

    static void PaintCivicHall(Texture2D texture) {
        int width = texture.width;
        int height = texture.height;
        Color32 outline = new Color32(30, 35, 37, 255);
        Color32 roof = new Color32(74, 87, 101, 255);
        Color32 roofHighlight = new Color32(119, 132, 142, 255);
        Color32 wall = new Color32(184, 184, 166, 255);
        Color32 wallShadow = new Color32(125, 132, 124, 255);
        Color32 glass = new Color32(99, 158, 171, 255);

        FillRect(texture, 12, 10, width - 8, height - 7, new Color32(17, 23, 26, 190));
        FillRect(texture, 7, 14, width - 14, height - 13, new Color32(69, 77, 76, 255));
        DrawRect(texture, 7, 14, width - 14, height - 13, outline);
        FillRect(texture, 17, 20, width - 24, 72, roof);
        DrawRect(texture, 17, 20, width - 24, 72, outline);
        FillRect(texture, 24, 27, width - 31, 33, roofHighlight);
        FillRect(texture, 24, 63, width - 31, 69, Darken(roof, 0.72f));
        FillRect(texture, 30, 34, 53, 45, new Color32(52, 61, 68, 255));
        FillRect(texture, width - 54, 34, width - 31, 45, new Color32(52, 61, 68, 255));
        FillRect(texture, 35, 37, 48, 42, glass);
        FillRect(texture, width - 49, 37, width - 36, 42, glass);

        FillRect(texture, 17, 88, width - 24, height - 18, outline);
        FillRect(texture, 24, 96, width - 31, height - 25, wall);
        FillRect(texture, 24, 96, width - 31, 108, Lighten(wall, 1.1f));
        FillRect(texture, 24, height - 39, width - 31, height - 25, wallShadow);
        int[] windows = { 38, 73, width - 91, width - 56 };
        for (int i = 0; i < windows.Length; i++) {
            DrawHouseWindow(texture, windows[i], 119, 20, 15, outline, glass);
        }

        int doorX = width / 2 - 18;
        FillRect(texture, doorX - 4, height - 68, doorX + 40, height - 16, outline);
        FillRect(texture, doorX, height - 63, doorX + 36, height - 20, new Color32(71, 55, 48, 255));
        FillRect(texture, doorX + 5, height - 58, doorX + 31, height - 52, new Color32(222, 195, 111, 255));
        FillRect(texture, doorX + 29, height - 41, doorX + 32, height - 37, new Color32(238, 194, 72, 255));
        FillRect(texture, doorX - 12, height - 14, doorX + 48, height - 9, new Color32(175, 148, 106, 255));
        FillRect(texture, width / 2 - 20, 7, width / 2 + 20, 14, outline);
        FillRect(texture, width / 2 - 16, 9, width / 2 + 16, 11, new Color32(199, 186, 110, 255));
        FillRect(texture, 38, 75, 58, 80, new Color32(41, 49, 55, 255));
        FillRect(texture, width - 59, 75, width - 39, 80, new Color32(41, 49, 55, 255));
    }

    static void PaintWorkshop(Texture2D texture) {
        int width = texture.width;
        int height = texture.height;
        Color32 outline = new Color32(38, 34, 32, 255);
        Color32 roof = new Color32(64, 76, 73, 255);
        Color32 roofHighlight = new Color32(115, 130, 118, 255);
        Color32 wall = new Color32(177, 139, 91, 255);
        Color32 wallShadow = new Color32(124, 92, 64, 255);

        FillRect(texture, 10, 9, width - 7, height - 6, new Color32(19, 23, 23, 190));
        FillRect(texture, 6, 13, width - 12, height - 11, new Color32(78, 76, 66, 255));
        DrawRect(texture, 6, 13, width - 12, height - 11, outline);
        FillRect(texture, 13, 19, width - 19, 63, roof);
        DrawRect(texture, 13, 19, width - 19, 63, outline);
        FillRect(texture, 20, 25, width - 26, 34, roofHighlight);
        FillRect(texture, 20, 65, width - 26, 70, Darken(roof, 0.68f));
        FillRect(texture, 14, 78, width - 20, height - 15, outline);
        FillRect(texture, 20, 84, width - 26, height - 22, wall);
        FillRect(texture, 20, 84, width - 26, 94, Lighten(wall, 1.1f));
        FillRect(texture, 20, height - 39, width - 26, height - 22, wallShadow);

        int garageWidth = 33;
        int garageY = 96;
        for (int i = 0; i < 3; i++) {
            int x = 20 + i * 45;
            FillRect(texture, x - 2, garageY - 2, x + garageWidth + 2, height - 20, outline);
            FillRect(texture, x, garageY, x + garageWidth, height - 23, new Color32(65, 71, 70, 255));
            for (int line = x + 5; line < x + garageWidth; line += 8) {
                FillRect(texture, line, garageY + 4, line + 2, height - 27, new Color32(108, 113, 105, 255));
            }
        }
        FillRect(texture, 24, 68, width - 30, 76, new Color32(216, 165, 64, 255));
        FillRect(texture, 29, 70, width - 35, 73, new Color32(61, 54, 43, 255));
        FillRect(texture, width / 2 - 20, height - 17, width / 2 + 20, height - 11,
            new Color32(173, 146, 105, 255));
    }

    static void PaintCafe(Texture2D texture) {
        int width = texture.width;
        int height = texture.height;
        Color32 outline = new Color32(43, 32, 34, 255);
        Color32 roof = new Color32(122, 64, 57, 255);
        Color32 roofHighlight = new Color32(203, 116, 79, 255);
        Color32 wall = new Color32(204, 171, 111, 255);
        Color32 glass = new Color32(105, 165, 169, 255);

        FillRect(texture, 9, 9, width - 6, height - 6, new Color32(19, 21, 22, 190));
        FillRect(texture, 5, 13, width - 12, height - 11, new Color32(81, 71, 61, 255));
        DrawRect(texture, 5, 13, width - 12, height - 11, outline);
        FillRect(texture, 12, 19, width - 19, 57, roof);
        DrawRect(texture, 12, 19, width - 19, 57, outline);
        FillRect(texture, 18, 26, width - 25, 34, roofHighlight);
        FillRect(texture, 18, 65, width - 25, 70, Darken(roof, 0.68f));
        FillRect(texture, 12, 72, width - 19, height - 15, outline);
        FillRect(texture, 18, 78, width - 25, height - 22, wall);
        FillRect(texture, 18, 78, width - 25, 88, Lighten(wall, 1.1f));
        FillRect(texture, 16, 68, width - 22, 76, new Color32(238, 189, 78, 255));
        for (int i = 0; i < 3; i++) {
            int x = 18 + i * 32;
            FillRect(texture, x, 93, x + 23, 109, outline);
            FillRect(texture, x + 3, 96, x + 20, 106, glass);
            FillRect(texture, x + 10, 96, x + 12, 106, outline);
        }
        FillRect(texture, width / 2 - 10, height - 26, width / 2 + 10, height - 12, outline);
        FillRect(texture, width / 2 - 7, height - 23, width / 2 + 7, height - 14, new Color32(77, 53, 44, 255));
        FillRect(texture, width / 2 + 3, height - 20, width / 2 + 5, height - 18, new Color32(240, 196, 77, 255));
        FillRect(texture, 18, height - 9, width - 24, height - 5, new Color32(175, 148, 106, 255));
    }

    static void PaintResidentialLot(Texture2D texture) {
        int width = texture.width;
        int height = texture.height;
        Color32 border = new Color32(38, 62, 41, 255);
        Color32 yard = new Color32(76, 108, 64, 255);
        Color32 yardHighlight = new Color32(103, 132, 73, 255);
        Color32 path = new Color32(177, 151, 107, 255);
        Color32 pathShadow = new Color32(123, 105, 78, 255);

        FillRect(texture, 5, 5, width - 6, height - 6, border);
        FillRect(texture, 9, 9, width - 10, height - 10, yard);
        DrawRect(texture, 13, 13, width - 14, height - 14, new Color32(58, 86, 49, 255));
        FillRect(texture, 16, 16, width - 17, 20, yardHighlight);
        FillRect(texture, 16, height - 21, width - 17, height - 17, yardHighlight);
        FillRect(texture, width / 2 - 5, 9, width / 2 + 5, height / 2 - 2, pathShadow);
        FillRect(texture, width / 2 - 3, 9, width / 2 + 3, height / 2 - 2, path);
        FillRect(texture, 9, height / 2 - 4, width / 2 - 2, height / 2 + 4, pathShadow);
        FillRect(texture, 9, height / 2 - 2, width / 2 - 2, height / 2 + 2, path);
        FillEllipse(texture, 18, 18, 39, 39, new Color32(39, 82, 46, 255));
        FillEllipse(texture, width - 40, 18, width - 19, 39, new Color32(39, 82, 46, 255));
        FillEllipse(texture, 18, height - 40, 39, height - 19, new Color32(39, 82, 46, 255));
        FillEllipse(texture, width - 40, height - 40, width - 19, height - 19,
            new Color32(39, 82, 46, 255));
        SetPixelSafe(texture, 28, 27, new Color32(213, 139, 87, 255));
        SetPixelSafe(texture, width - 30, 29, new Color32(226, 193, 83, 255));
        SetPixelSafe(texture, 30, height - 29, new Color32(124, 173, 210, 255));
        SetPixelSafe(texture, width - 28, height - 30, new Color32(224, 109, 99, 255));
    }

    static void PaintUrbanLot(Texture2D texture) {
        int width = texture.width;
        int height = texture.height;
        Color32 border = new Color32(43, 47, 70, 255);
        Color32 yard = new Color32(83, 88, 116, 255);
        Color32 yardHighlight = new Color32(111, 116, 145, 255);
        Color32 path = new Color32(176, 177, 170, 255);
        Color32 pathShadow = new Color32(113, 116, 127, 255);

        FillRect(texture, 5, 5, width - 6, height - 6, border);
        FillRect(texture, 9, 9, width - 10, height - 10, yard);
        DrawRect(texture, 13, 13, width - 14, height - 14, new Color32(57, 62, 88, 255));
        FillRect(texture, 16, 16, width - 17, 20, yardHighlight);
        FillRect(texture, 16, height - 21, width - 17, height - 17, yardHighlight);
        FillRect(texture, width / 2 - 5, 9, width / 2 + 5, height / 2 - 2, pathShadow);
        FillRect(texture, width / 2 - 3, 9, width / 2 + 3, height / 2 - 2, path);
        FillRect(texture, 9, height / 2 - 4, width / 2 - 2, height / 2 + 4, pathShadow);
        FillRect(texture, 9, height / 2 - 2, width / 2 - 2, height / 2 + 2, path);
        FillEllipse(texture, 18, 18, 39, 39, new Color32(44, 91, 91, 255));
        FillEllipse(texture, width - 40, 18, width - 19, 39, new Color32(44, 91, 91, 255));
        FillEllipse(texture, 18, height - 40, 39, height - 19, new Color32(44, 91, 91, 255));
        FillEllipse(texture, width - 40, height - 40, width - 19, height - 19,
            new Color32(44, 91, 91, 255));
        SetPixelSafe(texture, 28, 27, new Color32(121, 186, 191, 255));
        SetPixelSafe(texture, width - 30, 29, new Color32(210, 164, 100, 255));
        SetPixelSafe(texture, 30, height - 29, new Color32(157, 128, 192, 255));
        SetPixelSafe(texture, width - 28, height - 30, new Color32(220, 112, 133, 255));
    }

    static void PaintPark(Texture2D texture) {
        FillRect(texture, 5, 5, texture.width - 6, texture.height - 6, new Color32(57, 87, 54, 210));
        DrawRect(texture, 5, 5, texture.width - 6, texture.height - 6, new Color32(31, 49, 38, 255));
        FillRect(texture, 15, texture.height / 2 - 8, texture.width - 16, texture.height / 2 + 8,
            new Color32(178, 155, 115, 255));
        FillRect(texture, texture.width / 2 - 8, 15, texture.width / 2 + 8, texture.height - 16,
            new Color32(178, 155, 115, 255));
        DrawRect(texture, 15, texture.height / 2 - 8, texture.width - 16, texture.height / 2 + 8,
            new Color32(125, 108, 83, 255));
        DrawRect(texture, texture.width / 2 - 8, 15, texture.width / 2 + 8, texture.height - 16,
            new Color32(125, 108, 83, 255));
        FillEllipse(texture, 78, 64, 114, 100, new Color32(72, 122, 73, 255));
        DrawEllipse(texture, 78, 64, 114, 100, new Color32(34, 66, 44, 255));
    }

    static void PaintNightPark(Texture2D texture) {
        int width = texture.width;
        int height = texture.height;
        Color32 border = new Color32(39, 45, 67, 255);
        Color32 lawn = new Color32(48, 78, 77, 255);
        Color32 lawnShadow = new Color32(36, 61, 68, 255);
        Color32 path = new Color32(147, 149, 164, 255);
        Color32 pathShadow = new Color32(93, 102, 128, 255);
        Color32 planter = new Color32(28, 57, 60, 255);
        Color32 planterLight = new Color32(62, 113, 92, 255);

        FillRect(texture, 6, 6, width - 7, height - 7, border);
        FillRect(texture, 11, 11, width - 12, height - 12, lawn);
        DrawRect(texture, 15, 15, width - 16, height - 16, lawnShadow);
        FillRect(texture, 19, height / 2 - 8, width - 20, height / 2 + 8, pathShadow);
        FillRect(texture, 19, height / 2 - 5, width - 20, height / 2 + 5, path);
        FillRect(texture, width / 2 - 8, 19, width / 2 + 8, height - 20, pathShadow);
        FillRect(texture, width / 2 - 5, 19, width / 2 + 5, height - 20, path);

        FillRect(texture, 27, 27, 72, 43, planter);
        FillRect(texture, 31, 31, 68, 39, planterLight);
        FillRect(texture, width - 73, 27, width - 28, 43, planter);
        FillRect(texture, width - 69, 31, width - 32, 39, planterLight);
        FillRect(texture, 27, height - 70, 72, height - 28, planter);
        FillRect(texture, 31, height - 66, 68, height - 32, planterLight);
        FillRect(texture, width - 73, height - 70, width - 28, height - 28, planter);
        FillRect(texture, width - 69, height - 66, width - 32, height - 32, planterLight);

        FillEllipse(texture, width / 2 - 25, height / 2 - 25, width / 2 + 25, height / 2 + 25,
            new Color32(37, 59, 72, 255));
        FillEllipse(texture, width / 2 - 18, height / 2 - 18, width / 2 + 18, height / 2 + 18,
            new Color32(79, 150, 172, 255));
        FillEllipse(texture, width / 2 - 9, height / 2 - 9, width / 2 + 9, height / 2 + 9,
            new Color32(190, 198, 188, 255));
        FillRect(texture, width / 2 - 2, height / 2 - 18, width / 2 + 2, height / 2 - 4,
            new Color32(215, 224, 207, 255));
    }

    static void PaintTree(Texture2D texture, bool tall) {
        int centerX = texture.width / 2;
        int canopyBottom = tall ? 58 : 50;
        FillRect(texture, centerX - 4, canopyBottom - 3, centerX + 5, texture.height - 5,
            new Color32(92, 58, 36, 255));
        FillEllipse(texture, 6, 4, texture.width - 7, canopyBottom, new Color32(26, 65, 38, 255));
        FillEllipse(texture, 12, 8, texture.width - 13, canopyBottom - 7, new Color32(67, 126, 52, 255));
        FillEllipse(texture, 18, 11, texture.width - 22, canopyBottom - 15, new Color32(119, 166, 63, 255));
        SetPixelSafe(texture, centerX - 12, 22, new Color32(178, 205, 82, 255));
        SetPixelSafe(texture, centerX + 10, 32, new Color32(42, 99, 48, 255));
    }

    static void PaintShrub(Texture2D texture) {
        FillEllipse(texture, 4, 6, texture.width - 5, texture.height - 5, new Color32(35, 75, 42, 255));
        FillEllipse(texture, 9, 8, texture.width - 12, texture.height - 11, new Color32(82, 139, 52, 255));
        FillEllipse(texture, 15, 11, texture.width - 17, texture.height - 16, new Color32(138, 176, 66, 255));
    }

    static void PaintBench(Texture2D texture) {
        FillRect(texture, 9, 7, texture.width - 10, 13, new Color32(117, 74, 43, 255));
        FillRect(texture, 9, 18, texture.width - 10, 23, new Color32(76, 50, 34, 255));
        FillRect(texture, 15, 22, 20, texture.height - 5, new Color32(44, 45, 39, 255));
        FillRect(texture, texture.width - 20, 22, texture.width - 15, texture.height - 5,
            new Color32(44, 45, 39, 255));
    }

    static void PaintLamp(Texture2D texture) {
        FillRect(texture, 14, 19, 18, texture.height - 10, new Color32(39, 43, 44, 255));
        FillEllipse(texture, 3, 3, texture.width - 4, 25, new Color32(217, 196, 112, 210));
        FillEllipse(texture, 9, 8, texture.width - 10, 20, new Color32(250, 238, 156, 255));
        FillRect(texture, 7, 25, 25, 29, new Color32(39, 43, 44, 255));
    }

    static void PaintFlowerBed(Texture2D texture) {
        FillRect(texture, 3, 5, texture.width - 4, texture.height - 6, new Color32(54, 93, 48, 255));
        DrawRect(texture, 3, 5, texture.width - 4, texture.height - 6, new Color32(43, 60, 40, 255));
        for (int x = 14; x < texture.width - 8; x += 19) {
            SetPixelSafe(texture, x, 17, new Color32(227, 116, 116, 255));
            SetPixelSafe(texture, x + 1, 18, new Color32(255, 214, 104, 255));
            SetPixelSafe(texture, x + 5, 29, new Color32(123, 167, 223, 255));
        }
    }

    static void PaintFountain(Texture2D texture) {
        FillEllipse(texture, 6, 6, texture.width - 7, texture.height - 7, new Color32(62, 74, 79, 255));
        FillEllipse(texture, 12, 12, texture.width - 13, texture.height - 13, new Color32(87, 164, 193, 255));
        FillEllipse(texture, 22, 22, texture.width - 23, texture.height - 23, new Color32(186, 197, 191, 255));
        FillRect(texture, 30, 17, 34, 32, new Color32(216, 224, 214, 255));
    }

    static void FillRect(Texture2D texture, int minX, int minY, int maxX, int maxY, Color32 color) {
        for (int y = minY; y <= maxY; y++) {
            for (int x = minX; x <= maxX; x++) {
                SetPixelSafe(texture, x, y, color);
            }
        }
    }

    static void DrawRect(Texture2D texture, int minX, int minY, int maxX, int maxY, Color32 color) {
        for (int x = minX; x <= maxX; x++) {
            SetPixelSafe(texture, x, minY, color);
            SetPixelSafe(texture, x, maxY, color);
        }

        for (int y = minY; y <= maxY; y++) {
            SetPixelSafe(texture, minX, y, color);
            SetPixelSafe(texture, maxX, y, color);
        }
    }

    static void FillEllipse(Texture2D texture, int minX, int minY, int maxX, int maxY, Color32 color) {
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;
        float radiusX = Mathf.Max(1f, (maxX - minX) * 0.5f);
        float radiusY = Mathf.Max(1f, (maxY - minY) * 0.5f);
        for (int y = minY; y <= maxY; y++) {
            for (int x = minX; x <= maxX; x++) {
                float dx = (x - centerX) / radiusX;
                float dy = (y - centerY) / radiusY;
                if (dx * dx + dy * dy <= 1f) {
                    SetPixelSafe(texture, x, y, color);
                }
            }
        }
    }

    static void DrawEllipse(Texture2D texture, int minX, int minY, int maxX, int maxY, Color32 color) {
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;
        float radiusX = Mathf.Max(1f, (maxX - minX) * 0.5f);
        float radiusY = Mathf.Max(1f, (maxY - minY) * 0.5f);
        for (int angle = 0; angle < 360; angle += 3) {
            float radians = angle * Mathf.Deg2Rad;
            SetPixelSafe(texture, Mathf.RoundToInt(centerX + Mathf.Cos(radians) * radiusX),
                Mathf.RoundToInt(centerY + Mathf.Sin(radians) * radiusY), color);
        }
    }

    static void DrawLine(Texture2D texture, int startX, int startY, int endX, int endY, Color32 color) {
        int steps = Mathf.Max(Mathf.Abs(endX - startX), Mathf.Abs(endY - startY));
        for (int i = 0; i <= steps; i++) {
            float ratio = steps == 0 ? 0f : i / (float)steps;
            SetPixelSafe(texture, Mathf.RoundToInt(Mathf.Lerp(startX, endX, ratio)),
                Mathf.RoundToInt(Mathf.Lerp(startY, endY, ratio)), color);
        }
    }

    static Color32 Darken(Color32 color, float multiplier) {
        return new Color32((byte)(color.r * multiplier), (byte)(color.g * multiplier),
            (byte)(color.b * multiplier), color.a);
    }

    static Color32 Lighten(Color32 color, float multiplier) {
        return new Color32((byte)Mathf.Clamp(color.r * multiplier, 0f, 255f),
            (byte)Mathf.Clamp(color.g * multiplier, 0f, 255f),
            (byte)Mathf.Clamp(color.b * multiplier, 0f, 255f), color.a);
    }

    static void SetPixelSafe(Texture2D texture, int x, int y, Color32 color) {
        if (x >= 0 && y >= 0 && x < texture.width && y < texture.height) {
            texture.SetPixel(x, y, color);
        }
    }
}
