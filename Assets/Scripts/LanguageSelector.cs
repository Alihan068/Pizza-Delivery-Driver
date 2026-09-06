using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Cycles through catalog languages from a button; available languages and labels are entirely data-driven.</summary>
[RequireComponent(typeof(Button))]
public class LanguageSelector : MonoBehaviour {
    [SerializeField] TMP_Text label;
    [SerializeField] string labelKey = "settings.language";
    void Awake() { GetComponent<Button>().onClick.AddListener(SelectNext); }
    void OnEnable() { LocalizationManager.LanguageChanged += Refresh; Refresh(); }
    void OnDisable() { LocalizationManager.LanguageChanged -= Refresh; }
    void Refresh() {
        var manager = LocalizationManager.Instance;
        if (label != null && manager != null && manager.CurrentLanguage != null)
            label.text = LocalizationManager.Get(labelKey, manager.CurrentLanguage.nativeName);
    }
    void SelectNext() {
        var manager = LocalizationManager.Instance;
        if (manager == null || manager.Catalog == null || manager.Catalog.languages == null) return;
        var languages = manager.Catalog.languages;
        int index = System.Array.IndexOf(languages, manager.CurrentLanguage);
        for (int offset = 1; offset <= languages.Length; offset++) {
            var next = languages[(index + offset) % languages.Length];
            if (next != null) { manager.SelectLanguage(next.languageCode); return; }
        }
    }
}
