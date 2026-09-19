using System.Collections.Generic;

/// <summary>
/// Live, monotonic record of whether a running session is still eligible to be treated as valid.
/// Can only move from valid to invalid, never back — a session that degrades stays degraded for
/// its whole lifetime. This never rewrites the frozen <see cref="SessionRulesSnapshot"/> itself.
/// </summary>
public sealed class SessionIntegrityState {
    readonly List<string> invalidationReasons = new List<string>();

    public bool IsValid { get; private set; } = true;
    readonly System.Collections.ObjectModel.ReadOnlyCollection<string> reasonsView;
    /// <summary>Creates an isolated integrity record with a non-mutable diagnostics view.</summary>
    public SessionIntegrityState() { reasonsView = invalidationReasons.AsReadOnly(); }
    /// <summary>Live read-only diagnostic reasons; callers cannot remove an invalidation.</summary>
    public IReadOnlyList<string> InvalidationReasons => reasonsView;

    /// <summary>Marks the session permanently invalid. Subsequent calls only append additional reasons.</summary>
    /// <param name="reason">Human-readable cause, recorded for diagnostics.</param>
    public void MarkInvalid(string reason) {
        IsValid = false;
        if (!string.IsNullOrEmpty(reason)) invalidationReasons.Add(reason);
    }
}
