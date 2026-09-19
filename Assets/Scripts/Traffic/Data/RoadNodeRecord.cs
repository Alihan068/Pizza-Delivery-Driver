/// <summary>
/// One point in the road graph, in map-local navigation coordinates (see
/// <see cref="MapNavigationCoordinates"/>). Node positions are the single geometry authority: an
/// edge's endpoints are always derived from its from/to node, never stored redundantly.
/// </summary>
[System.Serializable]
public class RoadNodeRecord {
    public string nodeId;
    public float x;
    public float y;
}
