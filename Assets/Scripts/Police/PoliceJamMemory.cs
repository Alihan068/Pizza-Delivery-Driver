using UnityEngine;

/// <summary>
/// Session-scoped shared memory of map-local spots where a police car physically jammed against
/// geometry. Every recorded spot is a small, expiring disc placed in front of the jammed car — never
/// on the car itself — so the jammed unit can still reverse and replan out of it while every other
/// unit's bounded path search treats the pocket as blocked and routes around it. Entries are a fixed
/// ring buffer and always expire, so a session can never accumulate an unreachable map. The exact
/// player position is never fenced in; that check belongs to the recorder.
/// </summary>
public sealed class PoliceJamMemory {
    /// <summary>Maximum number of simultaneously remembered jam spots.</summary>
    public const int Capacity = 16;

    struct Entry {
        public Vector2 center;
        public float radius;
        public float expiresAt;
    }

    readonly Entry[] entries = new Entry[Capacity];
    readonly float lifetimeSeconds;
    int next;
    float clock;

    /// <summary>Creates a memory whose entries expire after the authored lifetime.</summary>
    /// <param name="lifetimeSeconds">Active seconds a recorded spot stays blocked; non-finite or non-positive falls back to 25.</param>
    public PoliceJamMemory(float lifetimeSeconds = 25f) {
        this.lifetimeSeconds = Finite(lifetimeSeconds) && lifetimeSeconds > 0f ? lifetimeSeconds : 25f;
    }

    /// <summary>Active seconds the memory currently uses for expiry decisions.</summary>
    public float Clock => clock;

    /// <summary>Number of entries that have not expired at the current clock.</summary>
    public int ActiveCount {
        get {
            int count = 0;
            for (int i = 0; i < entries.Length; i++) if (entries[i].radius > 0f && entries[i].expiresAt > clock) count++;
            return count;
        }
    }

    /// <summary>Advances the shared expiry clock. Callers pass the active session time; time never moves backwards.</summary>
    public void SetClock(float activeSeconds) {
        if (Finite(activeSeconds) && activeSeconds >= clock) clock = activeSeconds;
    }

    /// <summary>
    /// Records (or refreshes) one jam spot in map-local coordinates. A spot close to an existing live
    /// entry refreshes that entry instead of consuming a new slot, so repeated jams in one pocket
    /// cannot flush the rest of the memory.
    /// </summary>
    /// <param name="localCenter">Jam spot in map-local navigation coordinates.</param>
    /// <param name="radius">Disc radius; non-finite or non-positive values are rejected.</param>
    /// <returns>True when the memory now holds this spot.</returns>
    public bool Record(Vector2 localCenter, float radius) {
        if (!Finite(localCenter) || !Finite(radius) || radius <= 0f) return false;
        for (int i = 0; i < entries.Length; i++) {
            if (entries[i].radius <= 0f || entries[i].expiresAt <= clock) continue;
            if (Vector2.Distance(entries[i].center, localCenter) > entries[i].radius * 0.5f) continue;
            entries[i].radius = Mathf.Max(entries[i].radius, radius);
            entries[i].expiresAt = clock + lifetimeSeconds;
            return true;
        }
        entries[next] = new Entry { center = localCenter, radius = radius, expiresAt = clock + lifetimeSeconds };
        next = (next + 1) % entries.Length;
        return true;
    }

    /// <summary>Whether a map-local point currently sits inside a remembered jam spot.</summary>
    public bool Contains(Vector2 localPoint) {
        if (!Finite(localPoint)) return false;
        for (int i = 0; i < entries.Length; i++) {
            if (entries[i].radius <= 0f || entries[i].expiresAt <= clock) continue;
            if (Vector2.Distance(entries[i].center, localPoint) <= entries[i].radius) return true;
        }
        return false;
    }

    /// <summary>
    /// Whether an oriented clearance envelope overlaps a remembered jam spot. The envelope is tested
    /// by its inscribed circle so a long sweep passing nearby is not blocked wholesale.
    /// </summary>
    /// <param name="center">Envelope centre in map-local coordinates.</param>
    /// <param name="size">Envelope size.</param>
    public bool Intersects(Vector2 center, Vector2 size) {
        if (!Finite(center) || !Finite(size)) return false;
        float inscribed = 0.5f * Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.y));
        for (int i = 0; i < entries.Length; i++) {
            if (entries[i].radius <= 0f || entries[i].expiresAt <= clock) continue;
            if (Vector2.Distance(entries[i].center, center) <= entries[i].radius + inscribed) return true;
        }
        return false;
    }

    /// <summary>Drops every remembered spot, for a new session or an explicit reset.</summary>
    public void Clear() {
        for (int i = 0; i < entries.Length; i++) entries[i] = default;
        next = 0;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
}
