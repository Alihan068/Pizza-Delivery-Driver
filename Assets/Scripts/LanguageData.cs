using UnityEngine;

/// <summary>Immutable authored translations and language identity; runtime lookup caches live in the manager.</summary>
[CreateAssetMenu(menuName = "PizzaGame/Localization/Language")]
public class LanguageData : ScriptableObject {
    /// <summary>Persistent culture code, such as en or tr. Changing it invalidates a saved preference.</summary>
    public string languageCode;
    /// <summary>Language name in its own language, used in the language selector.</summary>
    public string nativeName;
    /// <summary>System languages which select this table on first launch. New languages require no code.</summary>
    public SystemLanguage[] systemLanguages;
    /// <summary>Translations authored in the Inspector. Duplicate and missing keys are reported by the validator.</summary>
    public LocalizationEntry[] entries;
}
