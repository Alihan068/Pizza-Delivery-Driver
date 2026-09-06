using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Corner indicator that shows the active slot was written: dim "Saved" at rest, briefly bright
/// with a checkmark right after a write. Clicking it also forces an immediate save.
/// </summary>
/// <remarks>
/// Listens to <see cref="GameManager.GameSaved"/> rather than polling, so it reacts to every write
/// path (settlement, upgrades, vehicle changes) and not just the ones this component triggers.
/// </remarks>
public class SaveStatusChip : MonoBehaviour {

    [Header("Text")]
    [SerializeField] TextMeshProUGUI label;
    [SerializeField] string idleKey = "save.chip.idle";
    [SerializeField] string savedKey = "save.chip.saved";

    [Header("Flash")]
    [SerializeField] Color idleColor = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField] Color savedColor = Color.white;
    [SerializeField] float flashSeconds = 1.2f;

    [SerializeField] Button saveButton;

    float flashTimer;

    void Awake() {
        if (saveButton != null) saveButton.onClick.AddListener(OnClickSave);
    }

    void OnEnable() {
        LocalizationManager.LanguageChanged += RefreshText;
        GameManager.GameSaved += OnGameSaved;
        flashTimer = 0f;
        RefreshText();
    }

    void OnDisable() {
        LocalizationManager.LanguageChanged -= RefreshText;
        GameManager.GameSaved -= OnGameSaved;
    }

    void Update() {
        if (flashTimer <= 0f) return;
        flashTimer -= Time.unscaledDeltaTime;
        if (flashTimer <= 0f) RefreshText();
    }

    void OnGameSaved(bool success) {
        if (!success) return;
        flashTimer = flashSeconds;
        RefreshText();
    }

    void RefreshText() {
        bool flashing = flashTimer > 0f;
        if (label != null) {
            label.text = LocalizationManager.Get(flashing ? savedKey : idleKey);
            label.color = flashing ? savedColor : idleColor;
        }
    }

    void OnClickSave() {
        GameManager.Instance?.SaveGame();
    }
}
