using UnityEngine;

/// <summary>
/// A rectangular region, in map-local coordinates, where no vehicle may spawn: the player's start,
/// a pickup/restock/extraction approach, or a boundary crossing. Kept as plain data so it still
/// applies after whatever authoring object originally marked it is deleted.
/// </summary>
[System.Serializable]
public class NoSpawnRegion {
    public Rect area;
    public string reason;
    public string anchorId;
}
