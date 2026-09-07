/// <summary>
/// Identifies the screen that opened a shared settings panel so the panel can expose only the
/// actions that are valid in that context.
/// </summary>
public enum SettingsContext {
    /// <summary>Main menu settings contain application preferences and progress reset only.</summary>
    MainMenu,

    /// <summary>Garage settings contain career save actions and a route back to the main menu.</summary>
    Garage,

    /// <summary>Pause settings contain in-session escape routes handled by <see cref="PauseManager"/>.</summary>
    Pause
}
