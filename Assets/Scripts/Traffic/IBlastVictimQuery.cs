using UnityEngine;

/// <summary>
/// Reusable-overlap adapter that finds blast damage-receiver candidates. Kept as an interface
/// rather than a direct Physics2D call — mirroring <see cref="IAreaClearanceQuery"/> — so
/// <see cref="VehicleExplosionService"/> stays testable without a physics world. The adapter must
/// also grow/retry any internal collider buffer; it must never hide truncation from this contract.
/// </summary>
public interface IBlastVictimQuery {
    /// <summary>
    /// Writes candidates into a non-empty buffer and returns a count in [0, buffer.Length]. A
    /// count strictly below capacity certifies a complete result; equality means potentially
    /// truncated and requires a retry with more capacity, even for an exact fit. Retries must
    /// have no gameplay side effects. Duplicate colliders must report the same receiver life,
    /// role, position and resistance. Only currently eligible receiver lives may be reported.
    /// </summary>
    /// <param name="origin">Finite world-space blast center.</param>
    /// <param name="radius">Finite, positive query radius.</param>
    /// <param name="buffer">Caller-owned reusable storage; the adapter must not retain it.</param>
    /// <returns>Written candidate count, with capacity equality signaling an incomplete result.</returns>
    int FindVictimsInRadius(Vector2 origin, float radius, BlastVictim[] buffer);
}
