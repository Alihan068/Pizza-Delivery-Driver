/// <summary>
/// Selects how the collision shape of a newly created vehicle prefab is determined.
/// </summary>
public enum VehicleColliderMode {
    /// <summary>
    /// Keep the template's collider as it is. Correct when the new sprite has the same
    /// silhouette as the template's; otherwise the vehicle collides with an invisible shape.
    /// </summary>
    InheritFromTemplate,

    /// <summary>
    /// Build a polygon from the sprite's own physics shape. Follows the silhouette exactly
    /// when a Custom Physics Shape has been drawn in the Sprite Editor.
    /// </summary>
    AutoFromSprite,

    /// <summary>
    /// Build a single rectangular path from the sprite bounds.
    /// Simple and predictable; good enough for box shaped vehicles.
    /// </summary>
    RectangleFromSpriteBounds
}
