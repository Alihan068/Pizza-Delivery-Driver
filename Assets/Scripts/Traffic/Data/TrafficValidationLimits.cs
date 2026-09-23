/// <summary>Bounded size limits <see cref="TrafficMapValidator"/> enforces so a malicious or corrupt document cannot force an unbounded import.</summary>
[System.Serializable]
public class TrafficValidationLimits {
    /// <summary>Maximum number of road nodes.</summary>
    public int maxNodes = 5000;
    /// <summary>Maximum number of directed road edges.</summary>
    public int maxEdges = 5000;
    /// <summary>Maximum number of interior points on one edge.</summary>
    public int maxPointsPerEdge = 64;
    /// <summary>Maximum number of civilian routes.</summary>
    public int maxRoutes = 200;
    /// <summary>Maximum encoded UTF-8 package size.</summary>
    public int maxPackageBytes = 2097152;
    /// <summary>Maximum length of one decoded string field.</summary>
    public int maxStringLength = 256;
    /// <summary>Maximum aggregate count across top-level navigation records.</summary>
    public int maxTotalRecords = 20000;
    /// <summary>Maximum size of a nested collection such as points or route edges.</summary>
    public int maxNestedCollectionItems = 5000;
    /// <summary>Maximum JSON nesting depth accepted before materialization.</summary>
    public int maxJsonDepth = 32;
    /// <summary>Maximum JSON token count accepted before materialization.</summary>
    public int maxTotalJsonTokens = 100000;
}
