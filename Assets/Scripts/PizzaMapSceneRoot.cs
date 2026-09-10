using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Serialized bridge between a gameplay scene and its generated tilemap layers.
/// Keeping the references on the map root makes the scene inspectable and allows
/// future editor tools to rebuild or extend a map without relying on hierarchy names.
/// </summary>
[DisallowMultipleComponent]
public class PizzaMapSceneRoot : MonoBehaviour {
    /// <summary>Generated grid containing the map's tilemap layers.</summary>
    public Grid mapGrid;

    /// <summary>Tilemap that fills the complete playable map rectangle.</summary>
    public Tilemap groundTilemap;

    /// <summary>Tilemap that contains water, fields, lots, or other terrain accents.</summary>
    public Tilemap terrainTilemap;

    /// <summary>Tilemap containing the connected drivable road network.</summary>
    public Tilemap roadTilemap;

    /// <summary>Tilemap containing transparent diagonal route details over the road base.</summary>
    public Tilemap diagonalRoadTilemap;

    /// <summary>Tilemap containing single and double lane markings over the road base.</summary>
    public Tilemap roadMarkingsTilemap;

    /// <summary>Tilemap containing sidewalk or road-shoulder detail.</summary>
    public Tilemap roadEdgeTilemap;

    /// <summary>Parent for prefab-based buildings and landscape decoration.</summary>
    public Transform decorationRoot;

    /// <summary>Theme used to build the current map visual layers.</summary>
    public PizzaMapTheme theme;

    /// <summary>Permanent identifier of the blueprint used for this scene.</summary>
    public string blueprintId;
}

