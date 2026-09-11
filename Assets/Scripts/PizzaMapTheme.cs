using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Shared visual configuration for a family of authored map tilemaps.
/// A theme groups visually compatible ground, road and terrain tiles so a future
/// map editor can offer a coherent palette instead of a flat list of unrelated assets.
/// </summary>
[CreateAssetMenu(fileName = "PizzaMapTheme", menuName = "PizzaGame/Tilemap/Map Theme")]
public class PizzaMapTheme : ScriptableObject {
    /// <summary>Permanent editor-facing identifier for this theme.</summary>
    [Tooltip("Permanent identifier used by map blueprints and future workshop content.")]
    public string themeId;

    /// <summary>Localization key or editor label used to identify the theme.</summary>
    [Tooltip("Localization key or readable label for the theme.")]
    public string displayNameKey;

    /// <summary>Tile used to fill the playable map bounds.</summary>
    [Tooltip("Base tile painted across the complete map rectangle.")]
    public TileBase groundTile;

    /// <summary>Optional tile for large plazas, lots, and other ground variations.</summary>
    [Tooltip("Optional ground variation tile for plazas, lots, and map districts.")]
    public TileBase groundAccentTile;

    /// <summary>Optional light grass variation for large natural areas and soft transitions.</summary>
    [Tooltip("Light grass variation used to break up broad ground regions.")]
    public TileBase groundSoftTile;

    /// <summary>Optional dark grass variation for shade, hedges and neighborhood depth.</summary>
    [Tooltip("Dark grass variation used for shaded or framed ground regions.")]
    public TileBase groundDarkTile;

    /// <summary>Tile used for all connected drivable roads in this theme.</summary>
    [Tooltip("Road tile. The map author may assign a standard Unity RuleTile here.")]
    public TileBase roadTile;

    /// <summary>
    /// Tile painted around drivable roads as the sidewalk or terrain shoulder.
    /// The map author may assign a standard RuleTile when the sidewalk belongs to
    /// the same Tilemap as the road, or author a separate edge layer manually.
    /// </summary>
    [Tooltip("Sidewalk RuleTile painted around drivable roads.")]
    public TileBase roadEdgeTile;

    /// <summary>Tile used for dashed center markings on narrow roads.</summary>
    [Tooltip("Single-line road marking tile used by narrow roads.")]
    public TileBase roadMarkingTile;

    /// <summary>Tile used for paired center markings on broad roads.</summary>
    [Tooltip("Double-line road marking tile used by broad roads.")]
    public TileBase roadDoubleMarkingTile;

    /// <summary>Transparent tile used to draw authored diagonal road markings.</summary>
    [Tooltip("Diagonal road marking tile used by the separate diagonal detail layer.")]
    public TileBase diagonalRoadMarkingTile;

    /// <summary>Optional terrain tile for water, snow, or another map-specific region.</summary>
    [Tooltip("Optional terrain accent tile used by authored zones.")]
    public TileBase terrainTile;

    /// <summary>Optional path tile for lots, fields, or unpaved routes.</summary>
    [Tooltip("Optional secondary terrain tile used by authored zones.")]
    public TileBase secondaryTerrainTile;

    /// <summary>Color applied to the scene's global 2D light when the theme is built.</summary>
    [Tooltip("Global light color applied during map generation.")]
    public Color globalLightColor = Color.white;

    /// <summary>Intensity applied to the scene's global 2D light when the theme is built.</summary>
    [Tooltip("Global light intensity applied during map generation.")]
    [Min(0f)] public float globalLightIntensity = 1f;
}

