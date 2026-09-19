using System.Collections.Generic;

/// <summary>Tracks collider pairs so a compound body's contact stays open until its last pair exits.</summary>
public sealed class VehicleContactTracker {
    readonly Dictionary<(int self, int other), int> pairs = new Dictionary<(int, int), int>();

    /// <summary>Records a pair and returns true only on the first contact with the other physical body.</summary>
    public bool TryEnter(int selfColliderId, int otherColliderId, int otherBodyId) {
        var key = (selfColliderId, otherColliderId);
        if (pairs.ContainsKey(key)) return false;
        bool touching = pairs.ContainsValue(otherBodyId);
        pairs.Add(key, otherBodyId);
        return !touching;
    }

    /// <summary>Ends one collider pair without releasing other pairs with the same body.</summary>
    public void Exit(int selfColliderId, int otherColliderId) {
        pairs.Remove((selfColliderId, otherColliderId));
    }

    /// <summary>Discards contacts when a body is detached, recycled or its session ends.</summary>
    public void Clear() { pairs.Clear(); }
}
