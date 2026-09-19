using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One directed road segment from <see cref="fromNodeId"/> to <see cref="toNodeId"/>. A two-way
/// street is authored as two separate edges (A to B and B to A), never as one bidirectional record.
/// <see cref="orderedPoints"/> stores only the interior control points between the endpoints; the
/// endpoints themselves always come from the referenced nodes, never a second time from here.
/// </summary>
[System.Serializable]
public class RoadEdgeRecord {
    public string edgeId;
    public string fromNodeId;
    public string toNodeId;
    public List<Vector2> orderedPoints = new List<Vector2>();
    public float usableWidth;
    public float speedLimit;
    public List<VehicleRole> allowedRoles = new List<VehicleRole>();

    /// <summary>Junction this edge enters/exits through at its start, or empty when it starts at a plain node.</summary>
    public string startJunctionId = string.Empty;

    /// <summary>Junction this edge enters/exits through at its end, or empty when it ends at a plain node.</summary>
    public string endJunctionId = string.Empty;
}
