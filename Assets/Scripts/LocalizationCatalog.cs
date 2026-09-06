using UnityEngine;

/// <summary>Available languages, fallback and preference storage configured independently of career saves.</summary>
[CreateAssetMenu(menuName = "PizzaGame/Localization/Catalog")]
public class LocalizationCatalog : ScriptableObject {
    /// <summary>Ordered language list. Add another LanguageData here to expose it in the selector.</summary>
    public LanguageData[] languages;
    /// <summary>Language used when no stored or system language matches; assign English for this project.</summary>
    public LanguageData defaultLanguage;
    /// <summary>PlayerPrefs key owned by this catalog. Must remain stable across releases.</summary>
    public string preferenceKey = "settings.language";

    /// <summary>Resolves a stored choice, then the system language, then the authored fallback without changing any data.</summary>
    /// <param name="preferredCode">Persisted code, or empty for a first launch. Uninstalled codes are ignored.</param>
    /// <param name="systemLanguage">Platform language used only when no installed preference matches.</param>
    /// <returns>Selected table, or null when no usable language/fallback is configured.</returns>
    public LanguageData ResolveLanguage(string preferredCode, SystemLanguage systemLanguage) {
        if (languages != null) {
            if (!string.IsNullOrEmpty(preferredCode)) foreach (var language in languages) {
                if (language != null && language.languageCode == preferredCode) return language;
            }
            foreach (var language in languages) {
                if (language != null && language.systemLanguages != null &&
                    System.Array.IndexOf(language.systemLanguages, systemLanguage) >= 0) return language;
            }
        }
        return defaultLanguage;
    }
}
