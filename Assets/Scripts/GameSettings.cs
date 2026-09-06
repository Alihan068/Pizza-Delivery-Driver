using UnityEngine;

/// <summary>
/// Player preferences that are not part of game progress: currently music volume.
/// </summary>
/// <remarks>
/// Stored in <see cref="PlayerPrefs"/> rather than in <c>save.json</c> on purpose. Settings and
/// progress have different lifetimes: resetting progress must not silently throw away the player's
/// audio preference, and a corrupt save file must not cost them their settings either.
/// <para>
/// Values are read lazily on first access instead of in a static constructor, so the class behaves
/// correctly when domain reload is disabled and static state survives between play sessions.
/// </para>
/// </remarks>
public static class GameSettings {

    /// <summary>Reads a language preference independently of profile slots. Empty means use the system language.</summary>
    /// <param name="key">Stable PlayerPrefs key supplied by the localization catalog.</param>
    /// <returns>Previously selected language code, or empty on first launch.</returns>
    public static string GetLanguagePreference(string key) => PlayerPrefs.GetString(key, string.Empty);

    /// <summary>Persists a language choice without modifying game progress or audio settings.</summary>
    /// <param name="key">Stable PlayerPrefs key supplied by the localization catalog.</param>
    /// <param name="code">Installed language code selected by the player.</param>
    public static void SetLanguagePreference(string key, string code) {
        PlayerPrefs.SetString(key, code);
        PlayerPrefs.Save();
    }

    const string MusicVolumeKey = "settings.musicVolume";

    /// <summary>Music volume used when the player has never changed it, on a 0-100 scale.</summary>
    public const int DefaultMusicVolume = 100;

    static int musicVolume;
    static bool loaded;

    /// <summary>
    /// Raised whenever a setting changes, so live objects can re-apply it without polling.
    /// </summary>
    public static event System.Action Changed;

    /// <summary>
    /// Music volume on a 0-100 scale. Assigning clamps the value, persists it and notifies listeners.
    /// </summary>
    /// <remarks>
    /// This is a scale factor, not an absolute level: 100 means "as loud as the audio was authored"
    /// and 50 means half of that. See <see cref="MusicPlayer"/>.
    /// </remarks>
    public static int MusicVolume {
        get {
            Load();
            return musicVolume;
        }
        set {
            Load();
            int clamped = Mathf.Clamp(value, 0, 100);
            if (clamped == musicVolume) return;
            musicVolume = clamped;
            PlayerPrefs.SetInt(MusicVolumeKey, clamped);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Music volume as a 0-1 multiplier, ready to be applied to an AudioSource.</summary>
    public static float MusicVolumeNormalized => MusicVolume / 100f;

    static void Load() {
        if (loaded) return;
        loaded = true;
        musicVolume = Mathf.Clamp(PlayerPrefs.GetInt(MusicVolumeKey, DefaultMusicVolume), 0, 100);
    }
}
