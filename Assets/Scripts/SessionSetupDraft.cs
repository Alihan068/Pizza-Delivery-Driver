using System.Collections.Generic;

/// <summary>
/// Detached copy of the player's raw selection taken the moment a session starts, before any map
/// or content validation runs. Immutable after construction so later mutation of the caller's
/// source collections (or of a later-frozen <see cref="SessionRulesSnapshot"/>) can never leak
/// back into an already-taken draft.
/// </summary>
public sealed class SessionSetupDraft {
    public readonly string sessionId;
    public readonly string mapId;
    public readonly string vehicleId;
    public readonly string difficultyId;
    public readonly int shiftDurationMinutes;
    public readonly bool isFreeplay;

    /// <summary>Raw modifier IDs as selected, before resolution/validation. Empty until a modifier selection UI feeds it.</summary>
    public readonly IReadOnlyList<string> selectedModifierIds;

    public SessionSetupDraft(string sessionId, string mapId, string vehicleId, string difficultyId,
        int shiftDurationMinutes, bool isFreeplay, IReadOnlyList<string> selectedModifierIds) {
        this.sessionId = sessionId;
        this.mapId = mapId;
        this.vehicleId = vehicleId;
        this.difficultyId = difficultyId;
        this.shiftDurationMinutes = shiftDurationMinutes;
        this.isFreeplay = isFreeplay;
        this.selectedModifierIds = CopySafe(selectedModifierIds);
    }

    static IReadOnlyList<string> CopySafe(IReadOnlyList<string> source) {
        if (source == null || source.Count == 0) return System.Array.Empty<string>();
        var copy = new string[source.Count];
        for (int i = 0; i < source.Count; i++) copy[i] = source[i];
        return System.Array.AsReadOnly(copy);
    }
}
