/// <summary>Which action the save slot selection screen is performing.</summary>
public enum SaveSlotSelectMode {
    /// <summary>Picking a slot to begin a brand new career in.</summary>
    NewGame,

    /// <summary>Picking an existing career to resume.</summary>
    LoadGame,

    /// <summary>Picking a destination slot to copy the active career into. The active slot is hidden.</summary>
    CopyTarget
}
