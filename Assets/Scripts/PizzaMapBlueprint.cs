using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serializable, editor-friendly description of a playable map layout.
/// Blueprints keep road strokes, terrain zones, service positions and decoration
/// stamps as data so built-in and future maps can be regenerated consistently.
/// </summary>
[CreateAssetMenu(fileName = "PizzaMapBlueprint", menuName = "PizzaGame/Tilemap/Map Blueprint")]
public class PizzaMapBlueprint : ScriptableObject {
    /// <summary>Permanent identifier for this layout.</summary>
    public string blueprintId;

    /// <summary>Scene that receives this layout when the map builder runs.</summary>
    public string sceneName;

    /// <summary>Protects a finished, scene-authored layout from destructive bulk regeneration.</summary>
    [Tooltip("Keep enabled for maps edited directly in the scene. Bulk generation will skip this scene.")]
    public bool preserveAuthoredScene;

    /// <summary>Theme supplying compatible tile assets and lighting.</summary>
    public PizzaMapTheme theme;

    /// <summary>Map width and height in one-unit grid cells.</summary>
    public Vector2Int mapSize = new Vector2Int(60, 40);

    /// <summary>Horizontal world width used by the map-specific gameplay camera framing.</summary>
    [Min(0.1f)] public float cameraReferenceHorizontalWorldSize = 24f;

    /// <summary>Connected road strokes painted in order on the road tilemap.</summary>
    public List<RoadStroke> roadStrokes = new List<RoadStroke>();

    /// <summary>Rectangular terrain accents painted below the road network.</summary>
    public List<TerrainZone> terrainZones = new List<TerrainZone>();

    /// <summary>Prefab placements used after the tilemap has been generated.</summary>
    public List<DecorationStamp> decorations = new List<DecorationStamp>();

    /// <summary>Customer positions expressed in blueprint cell coordinates.</summary>
    public List<Vector2Int> customerPositions = new List<Vector2Int>();

    /// <summary>Cell coordinate for the shop collection service.</summary>
    public Vector2Int shopPosition;

    /// <summary>Rotation in degrees for the shop collection service.</summary>
    public float shopRotationDegrees;

    /// <summary>Cell coordinate for the roadside collection service.</summary>
    public Vector2Int roadsidePosition;

    /// <summary>Rotation in degrees for the roadside collection service.</summary>
    public float roadsideRotationDegrees;

    /// <summary>Cell coordinate for the player spawn marker.</summary>
    public Vector2Int spawnPosition;

    /// <summary>Cell coordinate for the extraction marker.</summary>
    public Vector2Int extractionPosition;

    /// <summary>One connected road polyline with a configurable cell width.</summary>
    [Serializable]
    public class RoadStroke {
        /// <summary>Grid points traversed by this stroke in order.</summary>
        public List<Vector2Int> points = new List<Vector2Int>();

        /// <summary>Number of cells used for the stroke width.</summary>
        [Min(1)] public int width = 2;
    }

    /// <summary>One rectangular terrain region painted with the theme's secondary tile.</summary>
    [Serializable]
    public class TerrainZone {
        /// <summary>Rectangle in blueprint cell coordinates.</summary>
        public RectInt area;

        /// <summary>Visual tile family used by this zone in the authored terrain layer.</summary>
        public TerrainTileStyle style = TerrainTileStyle.Terrain;

        /// <summary>Whether the zone receives a deterministic broken edge instead of a hard rectangle.</summary>
        public bool softenEdge = true;

        /// <summary>When true, use the terrain tile; otherwise use the secondary terrain tile.</summary>
        public bool useTerrainTile = true;

        /// <summary>When true, use the theme's ground accent tile for a district or plaza.</summary>
        public bool useGroundAccentTile;
    }

    /// <summary>One prefab stamp placed at a blueprint cell after terrain generation.</summary>
    [Serializable]
    public class DecorationStamp {
        /// <summary>Prefab used for this placement.</summary>
        public GameObject prefab;

        /// <summary>Grid cell receiving the prefab pivot.</summary>
        public Vector2Int cell;

        /// <summary>Additional local rotation in degrees.</summary>
        public float rotationDegrees;

        /// <summary>Multiplier applied to the prefab's authored local scale.</summary>
        [Min(0.01f)] public float scaleMultiplier = 1f;
    }

    /// <summary>Available visual families for a map-authored terrain zone.</summary>
    public enum TerrainTileStyle {
        /// <summary>Theme terrain such as water or a large natural region.</summary>
        Terrain,
        /// <summary>Theme secondary terrain such as dirt, paths or lots.</summary>
        Secondary,
        /// <summary>Theme accent grass used for plazas and neighborhood greens.</summary>
        GroundAccent,
        /// <summary>Soft light grass variation used to break up large fields.</summary>
        GroundSoft,
        /// <summary>Dark grass variation used for hedges, shade and visual depth.</summary>
        GroundDark,
        /// <summary>Theme water tile used only for authored ponds or narrow waterways.</summary>
        Water,
        /// <summary>Theme dirt tile used for farms, paths and parking shoulders.</summary>
        Dirt
    }
}

