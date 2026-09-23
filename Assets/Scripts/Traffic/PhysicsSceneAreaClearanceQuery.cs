using UnityEngine;

/// <summary>Allocation-free oriented footprint query in one physics scene; a full buffer fails closed.</summary>
public sealed class PhysicsSceneAreaClearanceQuery : IAreaClearanceQuery {
    readonly PhysicsScene2D scene;
    readonly Vector2 origin;
    readonly bool staticOnly;
    readonly Collider2D[] results;
    readonly ContactFilter2D filter = new ContactFilter2D { useTriggers = false };

    /// <summary>Creates an adapter; origin is zero for world queries and the map origin for local navigation queries.</summary>
    public PhysicsSceneAreaClearanceQuery(PhysicsScene2D scene, Vector2 origin, bool staticOnly, int capacity) {
        this.scene = scene; this.origin = origin; this.staticOnly = staticOnly;
        results = new Collider2D[Mathf.Max(1, capacity)];
    }

    /// <summary>Rejects solid occupancy, invalid dimensions and saturation. Static-only checks ignore moving actors, including self.</summary>
    public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) {
        if (!scene.IsValid() || !Finite(center.x) || !Finite(center.y) || !Finite(headingDegrees) ||
            !Finite(footprint.x) || !Finite(footprint.y) || footprint.x <= 0f || footprint.y <= 0f) return false;
        int count = scene.OverlapBox(center + origin, footprint, headingDegrees, filter, results);
        if (count == results.Length) return false;
        for (int i = 0; i < count; i++) {
            var collider = results[i];
            if (collider == null) continue;
            if (!staticOnly || collider.attachedRigidbody == null || collider.attachedRigidbody.bodyType == RigidbodyType2D.Static) return false;
        }
        return true;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
