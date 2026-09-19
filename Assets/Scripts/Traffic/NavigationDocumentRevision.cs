using System.Runtime.CompilerServices;

/// <summary>
/// Tracks successful authoring transactions without adding runtime cache state to the portable DTO.
/// Weak keys let discarded drafts and their revisions be collected. Use edit commands for mutations;
/// callers that replace document fields directly must explicitly rebuild their runtime graph.
/// </summary>
internal static class NavigationDocumentRevision {
    sealed class Revision {
        public long value;
    }

    static readonly ConditionalWeakTable<MapNavigationDocument, Revision> revisions =
        new ConditionalWeakTable<MapNavigationDocument, Revision>();

    internal static long Get(MapNavigationDocument document) {
        return document != null ? revisions.GetValue(document, _ => new Revision()).value : 0;
    }

    internal static void Changed(MapNavigationDocument document) {
        if (document != null) revisions.GetValue(document, _ => new Revision()).value++;
    }
}
