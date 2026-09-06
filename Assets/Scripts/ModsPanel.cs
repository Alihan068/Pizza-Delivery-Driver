using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Read-only overlay listing every installed vehicle and map, which source supplied it, and any id
/// conflicts <see cref="ContentRegistry"/> found. No Workshop connection exists yet, but the game
/// already reads its content through <see cref="IContentProvider"/>, so this screen is the entry
/// point a future content browser replaces rather than something built from scratch later.
/// </summary>
public class ModsPanel : MonoBehaviour {

    [SerializeField] TextMeshProUGUI contentText;
    [SerializeField] Button backButton;

    [Header("Localization Keys")]
    [SerializeField] string vehiclesHeaderKey = "mods.vehiclesHeader";
    [SerializeField] string mapsHeaderKey = "mods.mapsHeader";
    [SerializeField] string entryKey = "mods.entry";
    [SerializeField] string conflictsHeaderKey = "mods.conflictsHeader";
    [SerializeField] string noConflictsKey = "mods.noConflicts";

    void Awake() {
        if (backButton != null) backButton.onClick.AddListener(Close);
    }

    /// <summary>Shows the panel and populates it from the current content registry.</summary>
    public void Open() {
        gameObject.SetActive(true);
        Refresh();
    }

    void Close() {
        gameObject.SetActive(false);
    }

    void Refresh() {
        if (contentText == null) return;
        var registry = GameManager.Instance != null ? GameManager.Instance.Content : null;
        if (registry == null) { contentText.text = string.Empty; return; }

        var sb = new StringBuilder();
        sb.AppendLine(LocalizationManager.Get(vehiclesHeaderKey));
        foreach (var vehicle in registry.Vehicles) {
            sb.AppendLine(LocalizationManager.Get(entryKey, vehicle.GetDisplayName(), registry.GetVehicleProviderId(vehicle)));
        }

        sb.AppendLine();
        sb.AppendLine(LocalizationManager.Get(mapsHeaderKey));
        foreach (var map in registry.Maps) {
            sb.AppendLine(LocalizationManager.Get(entryKey, map.GetDisplayName(), registry.GetMapProviderId(map)));
        }

        sb.AppendLine();
        sb.AppendLine(LocalizationManager.Get(conflictsHeaderKey));
        if (registry.Conflicts.Count == 0) {
            sb.AppendLine(LocalizationManager.Get(noConflictsKey));
        }
        else {
            foreach (var conflict in registry.Conflicts) sb.AppendLine(conflict);
        }

        contentText.text = sb.ToString();
    }
}
