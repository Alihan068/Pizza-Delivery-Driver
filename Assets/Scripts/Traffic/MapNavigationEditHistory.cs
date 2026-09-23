using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bounded undo/redo history for portable navigation DTOs. Snapshots are detached JSON strings,
/// intentionally outside hot paths; restoring overwrites the supplied document in place so graph
/// owners keep their reference. Call <see cref="Record"/> once after a successful command.
/// </summary>
public sealed class MapNavigationEditHistory {
    readonly int capacity;
    readonly List<string> undoSnapshots = new List<string>();
    readonly List<string> redoSnapshots = new List<string>();

    /// <summary>Creates a history with an initial detached snapshot and a positive maximum depth.</summary>
    public MapNavigationEditHistory(MapNavigationDocument document, int maximumDepth = 64) {
        capacity = (maximumDepth < 1 ? 1 : maximumDepth) + 1;
        if (document != null) undoSnapshots.Add(JsonUtility.ToJson(document));
    }

    /// <summary>Returns whether an earlier snapshot is available for restoration.</summary>
    public bool CanUndo => undoSnapshots.Count > 1;

    /// <summary>Returns whether a later snapshot is available for restoration.</summary>
    public bool CanRedo => redoSnapshots.Count > 0;

    /// <summary>Number of retained undo snapshots, including the current state.</summary>
    public int UndoCount => undoSnapshots.Count > 0 ? undoSnapshots.Count - 1 : 0;

    /// <summary>Number of retained redo snapshots.</summary>
    public int RedoCount => redoSnapshots.Count;

    /// <summary>
    /// Records the current DTO after a successful command. Equal consecutive snapshots are ignored;
    /// a new edit after undo clears the redo branch.
    /// </summary>
    public bool Record(MapNavigationDocument document) {
        if (document == null) return false;
        string snapshot = JsonUtility.ToJson(document);
        if (undoSnapshots.Count > 0 && undoSnapshots[undoSnapshots.Count - 1] == snapshot) return false;
        if (undoSnapshots.Count == 0) undoSnapshots.Add(snapshot);
        else undoSnapshots.Add(snapshot);
        while (undoSnapshots.Count > capacity) undoSnapshots.RemoveAt(0);
        redoSnapshots.Clear();
        return true;
    }

    /// <summary>Restores the previous snapshot in place and returns false when no undo is available.</summary>
    public bool TryUndo(MapNavigationDocument document) {
        if (document == null || !CanUndo) return false;
        string current = undoSnapshots[undoSnapshots.Count - 1];
        undoSnapshots.RemoveAt(undoSnapshots.Count - 1);
        redoSnapshots.Add(current);
        Restore(document, undoSnapshots[undoSnapshots.Count - 1]);
        return true;
    }

    /// <summary>Restores the next snapshot in place and returns false when no redo is available.</summary>
    public bool TryRedo(MapNavigationDocument document) {
        if (document == null || !CanRedo) return false;
        string snapshot = redoSnapshots[redoSnapshots.Count - 1];
        redoSnapshots.RemoveAt(redoSnapshots.Count - 1);
        undoSnapshots.Add(snapshot);
        Restore(document, snapshot);
        return true;
    }

    static void Restore(MapNavigationDocument document, string snapshot) {
        JsonUtility.FromJsonOverwrite(snapshot, document);
        NavigationDocumentRevision.Changed(document);
    }
}
