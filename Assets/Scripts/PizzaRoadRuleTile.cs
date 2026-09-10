using UnityEngine;

#if UNITY_EDITOR
using UnityEngine.Tilemaps;
#endif

/// <summary>
/// Designer-facing road RuleTile used by the Pizza Delivery Driver map kit.
/// Its serialized rules select straight, corner, T-junction, crossroad and dead-end
/// sprites from the same tile asset as neighboring road cells are painted.
/// </summary>
[CreateAssetMenu(fileName = "PizzaRoadRuleTile", menuName = "PizzaGame/Tilemap/Pizza Road Rule Tile")]
public class PizzaRoadRuleTile : UnityEngine.RuleTile {
    /// <summary>
    /// Keeps road connections aligned to the four cardinal directions used by the
    /// built-in palette and by the map authoring tool.
    /// </summary>
    public override int m_RotationAngle => 90;
}

