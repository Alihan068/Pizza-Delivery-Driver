using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Owns navigation for the dedicated map selection scene.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MapSelectionPanel"/> owns map and modifier preview state. This component owns scene
/// navigation only, which keeps the panel reusable if a future content browser hosts it elsewhere.
/// </para>
/// <para>
/// Button listeners are attached at runtime so scene duplication or reimport cannot leave an old
/// persistent UnityEvent pointing at the former GarageScene workflow.
/// </para>
/// </remarks>
public class MapSelectionSceneManager : MonoBehaviour {

    [Header("Scene References")]
    [Tooltip("Map and modifier preview panel in this scene.")]
    [SerializeField] MapSelectionPanel mapSelectionPanel;

    [Tooltip("Starts a shift on the selected map.")]
    [SerializeField] Button startJobButton;

    [Tooltip("Returns to the garage without starting a shift.")]
    [SerializeField] Button backToGarageButton;

    [Header("Localization Keys")]
    [SerializeField] string startJobKey = "garage.mapSelection.startJob";
    [SerializeField] string backToGarageKey = "garage.mapSelection.backToGarage";

    void OnEnable() {
        if (mapSelectionPanel != null) mapSelectionPanel.SelectionChanged += RefreshUI;
    }

    void OnDisable() {
        if (mapSelectionPanel != null) mapSelectionPanel.SelectionChanged -= RefreshUI;
    }

    void Start() {
        Time.timeScale = 1f;
        if (startJobButton != null) startJobButton.onClick.AddListener(OnClickStartJob);
        if (backToGarageButton != null) backToGarageButton.onClick.AddListener(OnClickBackToGarage);
        RefreshButtonLabels();
        RefreshUI();
    }

    void RefreshButtonLabels() {
        if (mapSelectionPanel == null) SetButtonLabel(startJobButton, startJobKey);
        SetButtonLabel(backToGarageButton, backToGarageKey);
    }

    void SetButtonLabel(Button button, string key) {
        if (button == null) return;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = LocalizationManager.Get(key);
    }

    void RefreshUI() {
        if (startJobButton == null) return;
        var manager = GameManager.Instance;
        var save = manager != null ? manager.GetCurrentVehicleSave() : null;
        bool canStart = manager != null && save != null && save.isUnlocked &&
                        (mapSelectionPanel == null
                            ? manager.currentMap != null && manager.IsMapOwned(manager.currentMap)
                            : mapSelectionPanel.CanApplyPreviewSelection());
        startJobButton.interactable = canStart;
    }

    /// <summary>Starts a shift on the owned map currently applied in the preview panel.</summary>
    public void OnClickStartJob() {
        var manager = GameManager.Instance;
        var save = manager != null ? manager.GetCurrentVehicleSave() : null;
        if (manager == null || save == null || !save.isUnlocked) {
            RefreshUI();
            return;
        }
        var map = manager.currentMap;
        if (mapSelectionPanel != null) {
            if (!mapSelectionPanel.TryApplyPreviewSelection()) {
                RefreshUI();
                return;
            }
            map = manager.currentMap;
        }
        else if (map == null || !manager.IsMapOwned(map)) {
            RefreshUI();
            return;
        }
        map = manager.currentMap;
        if (string.IsNullOrEmpty(map.sceneName)) {
            Debug.LogError("The selected map has no gameplay scene assigned.");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(map.sceneName);
    }

    /// <summary>Returns to the garage without changing the active map selection.</summary>
    public void OnClickBackToGarage() {
        var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
        if (config == null || string.IsNullOrEmpty(config.garageScene)) {
            Debug.LogError("No garage scene is configured in GameConfig.");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(config.garageScene);
    }
}
