using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Resolves translations before scene UI enables. Lives beside the persistent GameManager;
/// duplicate scene instances are discarded without replacing the active language.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class LocalizationManager : MonoBehaviour {
    [SerializeField] LocalizationCatalog catalog;
    readonly Dictionary<string, string> translations = new Dictionary<string, string>(StringComparer.Ordinal);
    readonly Dictionary<string, string> missing = new Dictionary<string, string>(StringComparer.Ordinal);
    CultureInfo culture = CultureInfo.InvariantCulture;

    /// <summary>Active service. Reset explicitly at Play Mode entry, including with domain reload disabled.</summary>
    public static LocalizationManager Instance { get; private set; }
    /// <summary>Notifies enabled UI once after a table change; consumers must unsubscribe when disabled.</summary>
    public static event Action LanguageChanged;
    /// <summary>Read-only configuration reference used by selectors and editor validation.</summary>
    public LocalizationCatalog Catalog => catalog;
    /// <summary>Selected table, or null if the catalog is invalid.</summary>
    public LanguageData CurrentLanguage { get; private set; }
    /// <summary>Changes only when the language changes, allowing hot-path UI to invalidate cached text.</summary>
    public int Revision { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() {
        Instance = null;
        LanguageChanged = null;
    }

    void Awake() {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (catalog == null || catalog.languages == null || catalog.defaultLanguage == null) {
            Debug.LogError("Localization catalog or fallback language is not assigned.", this);
            return;
        }
        string saved = GameSettings.GetLanguagePreference(catalog.preferenceKey);
        ApplyLanguage(catalog.ResolveLanguage(saved, Application.systemLanguage));
    }

    void OnDestroy() {
        if (Instance == this) Instance = null;
    }

    /// <summary>Selects an installed language and persists its code outside career saves. Unknown codes are rejected.</summary>
    /// <param name="code">Exact language code from the catalog.</param>
    /// <returns>True if the language exists, including when it was already selected.</returns>
    public bool SelectLanguage(string code) {
        if (catalog == null || catalog.languages == null) return false;
        foreach (var language in catalog.languages) {
            if (language == null || language.languageCode != code) continue;
            GameSettings.SetLanguagePreference(catalog.preferenceKey, code);
            if (CurrentLanguage != language) ApplyLanguage(language);
            return true;
        }
        return false;
    }

    void ApplyLanguage(LanguageData language) {
        CurrentLanguage = language;
        try { culture = CultureInfo.GetCultureInfo(language.languageCode); }
        catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
        translations.Clear();
        missing.Clear();
        if (language.entries != null) foreach (var entry in language.entries) {
            if (entry == null || string.IsNullOrWhiteSpace(entry.key)) continue;
            if (!translations.TryAdd(entry.key, entry.text ?? string.Empty))
                Debug.LogWarning("Duplicate localization key: " + entry.key, language);
        }
        Revision++;
        LanguageChanged?.Invoke();
    }

    /// <summary>Returns a translation without allocation on a cache hit. Missing keys stay visible and log once per language.</summary>
    /// <param name="key">Stable translation identifier, not player-facing prose.</param>
    /// <returns>Translated format, or a bracketed missing-key marker.</returns>
    public static string Get(string key) {
        key = key ?? string.Empty;
        if (Instance == null) return "[" + key + "]";
        if (Instance.translations.TryGetValue(key, out string value)) return value;
        if (Instance.missing.TryGetValue(key, out value)) return value;
        value = "[" + key + "]";
        Instance.missing.Add(key, value);
        Debug.LogWarning("Missing localization key: " + key, Instance);
        return value;
    }

    /// <summary>Formats infrequent UI containing strings. This overload allocates; use SetText for frequent numeric displays.</summary>
    /// <param name="key">Translation key containing composite-format placeholders.</param>
    /// <param name="args">Values in the order declared by the translation.</param>
    /// <returns>Formatted text, or the visible key on malformed formatting.</returns>
    public static string Get(string key, params object[] args) {
        try { return string.Format(Instance != null ? Instance.culture : CultureInfo.InvariantCulture, Get(key), args); }
        catch (FormatException) { Debug.LogWarning("Invalid localization format: " + key); return "[" + key + "]"; }
    }

    /// <summary>Writes up to three numeric values directly into TMP's buffer without boxing or a temporary formatted string.</summary>
    /// <param name="target">Destination; null is allowed for optional UI.</param>
    /// <param name="key">TMP numeric format key. Use indexed placeholders with supported decimal precision.</param>
    /// <param name="a">Placeholder zero.</param>
    /// <param name="b">Placeholder one.</param>
    /// <param name="c">Placeholder two.</param>
    public static void SetText(TMP_Text target, string key, float a = 0, float b = 0, float c = 0) {
        if (target != null) target.SetText(Get(key), a, b, c);
    }
}
