using System.Collections.Generic;

/// <summary>
/// Frozen, detached effective rules for one session, produced once map/content validation
/// completes. Every field is a primitive/string/list copy — never a live reference into a shared
/// ScriptableObject or into the <see cref="SessionSetupDraft"/> that preceded it — so nothing can
/// mutate an already-frozen snapshot out from under a running session.
/// </summary>
public sealed class SessionRulesSnapshot {
    public readonly string sessionId;
    public readonly string mapId;
    public readonly string vehicleId;
    public readonly string difficultyId;
    public readonly int shiftDurationMinutes;
    public readonly bool isFreeplay;
    public readonly int seed;

    /// <summary>Modifier IDs after resolution/validation. Empty typed slot until S01 wires real multi-modifier resolution.</summary>
    public readonly IReadOnlyList<string> resolvedModifierIds;

    /// <summary>Product of all resolved modifier score multipliers. Neutral (1) until S01 computes a real value.</summary>
    public readonly float combinedScoreMultiplier;

    /// <summary>Actual detached modifier effects used by gameplay and presentation.</summary>
    public FrozenModifierRules Modifiers { get; }

    /// <summary>Whether civilian traffic is actually enabled for this session (map support + modifiers).</summary>
    public readonly bool trafficEnabled;

    /// <summary>Whether police pursuit is actually enabled for this session (map support + modifiers).</summary>
    public readonly bool policeEnabled;

    /// <summary>Identifier of the bound map navigation document. Empty typed slot until S02 defines real navigation data.</summary>
    public readonly string navigationDocumentId;

    public SessionRulesSnapshot(string sessionId, string mapId, string vehicleId, string difficultyId,
        int shiftDurationMinutes, bool isFreeplay, int seed, IReadOnlyList<string> resolvedModifierIds,
        float combinedScoreMultiplier, bool trafficEnabled, bool policeEnabled, string navigationDocumentId, FrozenModifierRules modifiers = null) {
        this.sessionId = sessionId;
        this.mapId = mapId;
        this.vehicleId = vehicleId;
        this.difficultyId = difficultyId;
        this.shiftDurationMinutes = shiftDurationMinutes;
        this.isFreeplay = isFreeplay;
        this.seed = seed;
        this.resolvedModifierIds = CopySafe(resolvedModifierIds);
        Modifiers = modifiers ?? new FrozenModifierRules(null);
        this.combinedScoreMultiplier = modifiers != null ? modifiers.ScoreMultiplier : combinedScoreMultiplier;
        this.trafficEnabled = trafficEnabled;
        this.policeEnabled = policeEnabled;
        this.navigationDocumentId = navigationDocumentId ?? string.Empty;
    }

    static IReadOnlyList<string> CopySafe(IReadOnlyList<string> source) {
        if (source == null || source.Count == 0) return System.Array.Empty<string>();
        var copy = new string[source.Count];
        for (int i = 0; i < source.Count; i++) copy[i] = source[i];
        return System.Array.AsReadOnly(copy);
    }
}
