/// <summary>
/// Owns one session's identity and lifecycle bridge: the raw <see cref="SessionSetupDraft"/> taken
/// at session start, the <see cref="SessionRulesSnapshot"/> frozen once validation completes, its
/// live <see cref="SessionIntegrityState"/>, and the current <see cref="TrafficSessionPhase"/>.
/// A new instance is created per session; nothing here is a persistent singleton.
/// </summary>
public sealed class TrafficSessionContext {
    public string SessionId { get; }
    public SessionSetupDraft Draft { get; }
    public SessionRulesSnapshot Snapshot { get; private set; }
    public SessionIntegrityState Integrity { get; } = new SessionIntegrityState();
    public TrafficSessionPhase Phase { get; private set; } = TrafficSessionPhase.NotStarted;

    /// <summary>Detached modifier values captured before prefab instantiation, even while navigation is pending.</summary>
    public FrozenModifierRules Modifiers { get; }

    /// <summary>Creates one context with captured values; omitted modifiers are neutral for legacy callers.</summary>
    public TrafficSessionContext(SessionSetupDraft draft, FrozenModifierRules modifiers = null) {
        Modifiers = modifiers ?? new FrozenModifierRules(null);
        Draft = draft;
        SessionId = draft != null ? draft.sessionId : null;
    }

    /// <summary>Freezes the final effective rules for this session. Only the first call takes effect.</summary>
    /// <param name="snapshot">Detached, validated snapshot to adopt.</param>
    public void FreezeSnapshot(SessionRulesSnapshot snapshot) {
        if (Snapshot != null || Phase == TrafficSessionPhase.Ended || snapshot == null) return;
        Snapshot = snapshot;
    }

    /// <summary>Advances the session phase. Once <see cref="TrafficSessionPhase.Ended"/> is reached, further changes are ignored.</summary>
    /// <param name="phase">Phase to move to.</param>
    public void SetPhase(TrafficSessionPhase phase) {
        if (Phase == TrafficSessionPhase.Ended) return;
        Phase = phase;
    }
}
