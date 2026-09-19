using System.Collections.Generic;

/// <summary>
/// Outcome of resolving a requested set of modifier ids against a catalog: the accepted
/// modifiers in canonical (modifierId-ordinal-sorted) order, and every id that was rejected for
/// being empty, duplicate, unknown, or in conflict with an already-accepted exclusive group.
/// </summary>
public sealed class ModifierSelectionResult {
    public readonly IReadOnlyList<ShiftModifierData> resolvedModifiers;
    public readonly IReadOnlyList<string> rejectedIds;

    public ModifierSelectionResult(IReadOnlyList<ShiftModifierData> resolvedModifiers, IReadOnlyList<string> rejectedIds) {
        this.resolvedModifiers = resolvedModifiers ?? System.Array.Empty<ShiftModifierData>();
        this.rejectedIds = rejectedIds ?? System.Array.Empty<string>();
    }
}
