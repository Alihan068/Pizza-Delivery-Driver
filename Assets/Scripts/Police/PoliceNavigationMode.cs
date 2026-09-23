/// <summary>Navigation composition selected for one police session.</summary>
public enum PoliceNavigationMode {
    /// <summary>Use the existing road and connector pursuit composition.</summary>
    LegacyRoad = 0,
    /// <summary>Use the later road-independent free-drive composition.</summary>
    FreeDrive = 1
}
