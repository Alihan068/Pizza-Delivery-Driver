/// <summary>One legal edge-to-edge movement through a <see cref="JunctionRecord"/>, with an authored yield priority.</summary>
[System.Serializable]
public class JunctionTransition {
    public string fromEdgeId;
    public string toEdgeId;

    /// <summary>Authored yield order; lower values yield to higher values. Data only — S06/S08 apply the actual yielding behavior.</summary>
    public int priority;
}
