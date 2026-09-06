using TMPro;
using UnityEngine;

/// <summary>Binds static scene or prefab text to a key. Do not attach to a label owned by a dynamic presenter.</summary>
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour {
    [SerializeField] string localizationKey;
    TMP_Text target;

    void Awake() { target = GetComponent<TMP_Text>(); }
    void OnEnable() { LocalizationManager.LanguageChanged += Refresh; Refresh(); }
    void OnDisable() { LocalizationManager.LanguageChanged -= Refresh; }
    void Refresh() { if (target != null) target.text = LocalizationManager.Get(localizationKey); }
}
