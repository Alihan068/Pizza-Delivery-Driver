/// <summary>Bounded size limits <see cref="TrafficMapValidator"/> enforces so a malicious or corrupt document cannot force an unbounded import.</summary>
[System.Serializable]
public class TrafficValidationLimits {
    public int maxNodes = 5000;
    public int maxEdges = 5000;
    public int maxPointsPerEdge = 64;
    public int maxRoutes = 200;
}
