using UnityEngine;

/// <summary>
/// Applies and persists display preferences independently of career progress.
/// </summary>
/// <remarks>
/// Resolution, fullscreen mode, vertical sync and target frame rate are machine preferences, so
/// they live in PlayerPrefs rather than in a career slot. The bootstrap component calls
/// <see cref="ApplySaved(GameConfig)"/> before the first rendered frame; UI controls can then call
/// the setter methods to apply a new choice immediately and persist it for the next launch.
/// </remarks>
public static class DisplaySettings {

    const string WidthKey = "settings.display.width";
    const string HeightKey = "settings.display.height";
    const string FullscreenModeKey = "settings.display.fullscreenMode";
    const string VSyncKey = "settings.display.vSync";
    const string TargetFrameRateKey = "settings.display.targetFrameRate";

    /// <summary>
    /// Applies saved display preferences, using the supplied project defaults on first launch.
    /// </summary>
    /// <param name="config">Project display defaults; null keeps the current display values.</param>
    public static void ApplySaved(GameConfig config) {
        if (S12BenchmarkGate.Requested) return;
        int width = PlayerPrefs.GetInt(WidthKey, config != null ? config.defaultResolutionWidth : Screen.width);
        int height = PlayerPrefs.GetInt(HeightKey, config != null ? config.defaultResolutionHeight : Screen.height);
        int modeValue = PlayerPrefs.GetInt(FullscreenModeKey,
            config != null ? (int)config.defaultFullscreenMode : (int)Screen.fullScreenMode);
        FullScreenMode mode = (FullScreenMode)Mathf.Clamp(modeValue, (int)FullScreenMode.ExclusiveFullScreen, (int)FullScreenMode.Windowed);

        if (width > 0 && height > 0) Screen.SetResolution(width, height, mode);
        ApplyFrameRate(config);
    }

    /// <summary>Applies and persists a supported resolution and fullscreen mode.</summary>
    /// <param name="width">Requested pixel width.</param>
    /// <param name="height">Requested pixel height.</param>
    /// <param name="mode">Requested fullscreen mode.</param>
    public static void SetResolution(int width, int height, FullScreenMode mode) {
        if (S12BenchmarkGate.Requested) return;
        if (width <= 0 || height <= 0) return;
        PlayerPrefs.SetInt(WidthKey, width);
        PlayerPrefs.SetInt(HeightKey, height);
        PlayerPrefs.SetInt(FullscreenModeKey, (int)mode);
        PlayerPrefs.Save();
        Screen.SetResolution(width, height, mode);
    }

    /// <summary>Applies and persists only the fullscreen mode at the current resolution.</summary>
    /// <param name="mode">Requested fullscreen mode.</param>
    public static void SetFullscreenMode(FullScreenMode mode) {
        SetResolution(Screen.width, Screen.height, mode);
    }

    /// <summary>Returns the persisted fullscreen mode or the current display mode.</summary>
    /// <returns>The saved mode when valid; otherwise the current Unity mode.</returns>
    public static FullScreenMode GetFullscreenMode() {
        int fallback = (int)Screen.fullScreenMode;
        int value = PlayerPrefs.GetInt(FullscreenModeKey, fallback);
        return (FullScreenMode)Mathf.Clamp(value, (int)FullScreenMode.ExclusiveFullScreen, (int)FullScreenMode.Windowed);
    }

    /// <summary>
    /// Applies and persists vertical sync and target frame rate settings.
    /// </summary>
    /// <param name="vSyncCount">Vertical sync count; zero enables the explicit target cap.</param>
    /// <param name="targetFrameRate">Target frame rate used when vertical sync is disabled.</param>
    public static void SetFrameRate(int vSyncCount, int targetFrameRate) {
        if (S12BenchmarkGate.Requested) return;
        int safeVSync = Mathf.Max(0, vSyncCount);
        int safeTarget = Mathf.Max(0, targetFrameRate);
        PlayerPrefs.SetInt(VSyncKey, safeVSync);
        PlayerPrefs.SetInt(TargetFrameRateKey, safeTarget);
        PlayerPrefs.Save();
        QualitySettings.vSyncCount = safeVSync;
        Application.targetFrameRate = safeTarget;
    }

    /// <summary>Returns the saved vertical sync count, or the configured default.</summary>
    /// <param name="config">Project defaults used when no preference exists.</param>
    /// <returns>A non-negative vertical sync count.</returns>
    public static int GetVSyncCount(GameConfig config) {
        return Mathf.Max(0, PlayerPrefs.GetInt(VSyncKey, config != null ? config.defaultVSyncCount : QualitySettings.vSyncCount));
    }

    /// <summary>Returns the saved target frame rate, or the configured default.</summary>
    /// <param name="config">Project defaults used when no preference exists.</param>
    /// <returns>A non-negative target frame rate; zero means no explicit cap.</returns>
    public static int GetTargetFrameRate(GameConfig config) {
        return Mathf.Max(0, PlayerPrefs.GetInt(TargetFrameRateKey, config != null ? config.defaultTargetFrameRate : Application.targetFrameRate));
    }

    static void ApplyFrameRate(GameConfig config) {
        QualitySettings.vSyncCount = GetVSyncCount(config);
        Application.targetFrameRate = GetTargetFrameRate(config);
    }
}
