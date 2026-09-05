using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Regenerates the collision shape of a vehicle prefab from its sprite.
/// </summary>
/// <remarks>
/// Both existing vehicles use a <see cref="PolygonCollider2D"/>, so generated shapes are written
/// as polygon paths too. That way the component type never has to change and the component order
/// on the prefab stays intact.
/// </remarks>
public static class VehicleColliderFitter {

    /// <summary>
    /// Applies a collision shape to the given root according to the selected mode.
    /// </summary>
    /// <param name="root">Prefab root to edit; must have been opened with LoadPrefabContents.</param>
    /// <param name="sprite">Sprite the shape is derived from. Nothing happens when null.</param>
    /// <param name="mode">The collider mode to apply.</param>
    /// <param name="message">Human readable summary of what was done.</param>
    /// <returns>True when the collider was actually changed.</returns>
    public static bool Apply(GameObject root, Sprite sprite, VehicleColliderMode mode, out string message) {
        message = string.Empty;
        if (root == null) return false;

        if (mode == VehicleColliderMode.InheritFromTemplate) {
            message = "Collider inherited from the template.";
            return false;
        }
        if (sprite == null) {
            message = "No sprite given, so the collider was inherited from the template.";
            return false;
        }

        var poly = EnsurePolygon(root, out bool replaced);
        if (poly == null) {
            message = "Could not create a PolygonCollider2D; the collider was left unchanged.";
            return false;
        }

        List<Vector2[]> paths = mode == VehicleColliderMode.AutoFromSprite
            ? BuildPhysicsShapePaths(sprite)
            : null;

        bool usedFallback = false;
        if (paths == null || paths.Count == 0) {
            usedFallback = mode == VehicleColliderMode.AutoFromSprite;
            paths = new List<Vector2[]> { BuildRectanglePath(sprite) };
        }

        poly.pathCount = paths.Count;
        for (int i = 0; i < paths.Count; i++) {
            poly.SetPath(i, paths[i]);
        }
        poly.isTrigger = false;
        poly.offset = Vector2.zero;

        if (usedFallback) {
            message = "The sprite has no Custom Physics Shape, so a rectangle was generated from its bounds. " +
                      "Draw one under Sprite Editor > Custom Physics Shape for a tighter fit.";
        }
        else if (mode == VehicleColliderMode.AutoFromSprite) {
            message = "Generated a polygon with " + paths.Count + " path(s) from the sprite physics shape.";
        }
        else {
            message = "Generated a rectangular collider from the sprite bounds.";
        }
        if (replaced) {
            message += " (The existing collider was replaced with a PolygonCollider2D.)";
        }
        return true;
    }

    static PolygonCollider2D EnsurePolygon(GameObject root, out bool replaced) {
        replaced = false;
        var existing = root.GetComponent<PolygonCollider2D>();
        if (existing != null) return existing;

        var other = root.GetComponent<Collider2D>();
        if (other != null) {
            Object.DestroyImmediate(other, true);
            replaced = true;
        }
        return root.AddComponent<PolygonCollider2D>();
    }

    static List<Vector2[]> BuildPhysicsShapePaths(Sprite sprite) {
        int shapeCount = sprite.GetPhysicsShapeCount();
        if (shapeCount <= 0) return null;

        var paths = new List<Vector2[]>(shapeCount);
        var buffer = new List<Vector2>();
        for (int i = 0; i < shapeCount; i++) {
            buffer.Clear();
            sprite.GetPhysicsShape(i, buffer);
            if (buffer.Count >= 3) paths.Add(buffer.ToArray());
        }
        return paths;
    }

    static Vector2[] BuildRectanglePath(Sprite sprite) {
        Bounds b = sprite.bounds;
        Vector2 min = b.min;
        Vector2 max = b.max;
        return new[] {
            new Vector2(min.x, min.y),
            new Vector2(max.x, min.y),
            new Vector2(max.x, max.y),
            new Vector2(min.x, max.y)
        };
    }
}
