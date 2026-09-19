using UnityEngine;

/// <summary>
/// The one place that defines and applies the <see cref="MapNavigationDocument"/> coordinate
/// contract: map-local world units relative to one map origin, +X right, +Y up, +Y is a vehicle's
/// forward direction, heading is degrees counter-clockwise from +Y. A Unity authoring adapter
/// applies the scene-origin translation exactly once when it reads or writes node/spawn positions;
/// nothing downstream may add a second grid-cell-center offset on top of an already-placed value.
/// <see cref="PizzaMapBlueprint.RoadStroke"/> paints Tilemap visuals during map authoring and is
/// unrelated to this navigation graph — a painted road stroke is not a traffic lane or edge.
/// </summary>
public static class MapNavigationCoordinates {
    /// <summary>Converts a Unity world position to map-local navigation coordinates.</summary>
    /// <param name="worldPosition">Position in Unity world space.</param>
    /// <param name="mapOriginWorld">This map's origin in Unity world space.</param>
    public static Vector2 WorldToLocal(Vector2 worldPosition, Vector2 mapOriginWorld) {
        return worldPosition - mapOriginWorld;
    }

    /// <summary>Converts map-local navigation coordinates to a Unity world position.</summary>
    /// <param name="localPosition">Position in map-local navigation space.</param>
    /// <param name="mapOriginWorld">This map's origin in Unity world space.</param>
    public static Vector2 LocalToWorld(Vector2 localPosition, Vector2 mapOriginWorld) {
        return localPosition + mapOriginWorld;
    }

    /// <summary>Converts a heading (degrees, counter-clockwise from +Y) to a unit direction vector.</summary>
    /// <param name="headingDegrees">Heading in degrees; 0 is +Y (a vehicle's authored forward).</param>
    public static Vector2 HeadingDegreesToDirection(float headingDegrees) {
        float radians = headingDegrees * Mathf.Deg2Rad;
        return new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
    }

    /// <summary>Converts a unit direction vector back to a heading in degrees (counter-clockwise from +Y).</summary>
    /// <param name="direction">Direction vector; does not need to be pre-normalized.</param>
    public static float DirectionToHeadingDegrees(Vector2 direction) {
        return Mathf.Atan2(-direction.x, direction.y) * Mathf.Rad2Deg;
    }
}
