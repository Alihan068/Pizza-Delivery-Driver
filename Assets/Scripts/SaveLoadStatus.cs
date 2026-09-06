/// <summary>
/// How an attempt to read a career profile turned out.
/// </summary>
/// <remarks>
/// The caller has to be able to tell "there is nothing here yet" apart from "there was something
/// here and it is broken". Collapsing those two into a silent fresh start is how a player loses a
/// career without ever being told.
/// </remarks>
public enum SaveLoadStatus {
    /// <summary>No file for this slot. A new career can be started here.</summary>
    Empty,

    /// <summary>The profile was read normally.</summary>
    Loaded,

    /// <summary>The main file was unreadable and the backup was used instead.</summary>
    RecoveredFromBackup,

    /// <summary>Neither the file nor its backup could be read. The files were set aside, not deleted.</summary>
    Corrupt
}
