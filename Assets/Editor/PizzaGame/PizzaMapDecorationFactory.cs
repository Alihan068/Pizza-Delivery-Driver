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

    /// <summary>Creates all reusable generated decoration prefabs if they do not already exist.</summary>
    public static void EnsureAll() {
        EnsureFolders();
        EnsureAsset("MapHouseSmall", 160, 128, texture => PaintHouse(texture,
            new Color32(104, 55, 48, 255), new Color32(202, 169, 119, 255)));
        EnsureAsset("MapHouseWide", 192, 128, texture => PaintHouse(texture,
            new Color32(71, 76, 91, 255), new Color32(176, 182, 178, 255)));
        EnsureAsset("MapHouseTall", 128, 160, texture => PaintHouse(texture,
            new Color32(123, 70, 49, 255), new Color32(213, 181, 126, 255)));
        EnsureAsset("MapPark", 192, 160, PaintPark);
        EnsureAsset("MapTreeRound", 64, 64, texture => PaintTree(texture, false));
        EnsureAsset("MapTreeTall", 64, 80, texture => PaintTree(texture, true));
        EnsureAsset("MapShrub", 48, 48, PaintShrub);
        EnsureAsset("MapBench", 64, 32, PaintBench);
        EnsureAsset("MapLamp", 32, 80, PaintLamp);
        EnsureAsset("MapFlowerBed", 96, 48, PaintFlowerBed);
        EnsureAsset("MapFountain", 64, 64, PaintFountain);
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

    static void EnsureAsset(string name, int width, int height, Action<Texture2D> painter) {
        string texturePath = TextureRoot + "/" + name + ".png";
        if (!File.Exists(ToAbsolutePath(texturePath))) {
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
        renderer.sortingOrder = 1;
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

    static void PaintHouse(Texture2D texture, Color32 roof, Color32 wall) {
        int width = texture.width;
        int height = texture.height;
        Color32 shadow = new Color32(13, 16, 21, 95);
        FillRect(texture, 10, 8, width - 12, height - 8, shadow);
        FillRect(texture, 14, 10, width - 18, height - 14, new Color32(57, 72, 61, 255));
        DrawRect(texture, 14, 10, width - 18, height - 14, new Color32(35, 43, 39, 255));
        FillRect(texture, 20, 18, width - 30, height - 32, wall);
        DrawRect(texture, 20, 18, width - 30, height - 32, new Color32(45, 38, 36, 255));
        FillRect(texture, 25, 23, width - 40, height - 43, roof);
        DrawRect(texture, 25, 23, width - 40, height - 43, new Color32(39, 31, 34, 255));

        int ridgeStart = 30;
        int ridgeEnd = width - 45;
        int roofTop = 28;
        int roofBottom = height - 48;
        for (int x = ridgeStart; x < ridgeEnd; x += 12) {
            DrawLine(texture, x, roofTop, x + 18, roofBottom, Darken(roof, 0.72f));
        }

        int doorX = width / 2 - 9;
        FillRect(texture, doorX, height - 44, doorX + 18, height - 22, new Color32(56, 36, 28, 255));
        FillRect(texture, 29, height - 40, 46, height - 28, new Color32(190, 216, 215, 255));
        FillRect(texture, width - 47, height - 40, width - 30, height - 28, new Color32(190, 216, 215, 255));
        FillRect(texture, 31, height - 38, 34, height - 36, new Color32(42, 63, 70, 255));
        FillRect(texture, width - 45, height - 38, width - 34, height - 36, new Color32(42, 63, 70, 255));
        FillRect(texture, doorX + 12, height - 34, doorX + 14, height - 32, new Color32(228, 186, 73, 255));
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

    static void SetPixelSafe(Texture2D texture, int x, int y, Color32 color) {
        if (x >= 0 && y >= 0 && x < texture.width && y < texture.height) {
            texture.SetPixel(x, y, color);
        }
    }
}
