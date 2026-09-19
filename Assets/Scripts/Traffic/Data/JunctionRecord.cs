using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An intersection: a conflict zone plus the explicit set of edge-to-edge movements a vehicle may
/// legally take through it. A junction is always explicitly authored data — it is never inferred
/// from two polylines crossing in space, so two disconnected intersecting road strokes never become
/// a junction by accident.
/// </summary>
[System.Serializable]
public class JunctionRecord {
    public string junctionId;
    public Rect conflictZone;
    public List<JunctionTransition> allowedTransitions = new List<JunctionTransition>();
}
