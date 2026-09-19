using UnityEngine;

/// <summary>One candidate placement <see cref="VehicleSpawnPolicy"/> evaluates: where, which way it would face, which road-graph node it attaches to, and its collider footprint.</summary>
public sealed class SpawnCandidate {
    public Vector2 position;
    public float headingDegrees;
    public string graphNodeId;
    public Vector2 footprint = new Vector2(2f, 4f);
}
