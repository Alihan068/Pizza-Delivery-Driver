using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour {
    [Header("Buttons")]
    [SerializeField] Button newGameButton;
    [SerializeField] Button loadGameButton;
    [SerializeField] Button modsButton;
    [SerializeField] Button quitButton;

    [Header("Slot Selection")]
    [Tooltip("Shared New Game / Load Game screen. Opened in the mode matching whichever button was pressed.")]
    [SerializeField] SaveSlotSelectPanel slotSelectPanel;

    [Header("Mods")]
    [SerializeField] ModsPanel modsPanel;

    [Header("Build Identity")]
    [SerializeField] TextMeshProUGUI versionText;

    [Tooltip("Localization key whose format receives the build version from Player Settings.")]
    [SerializeField] string versionKey = "menu.version";

    void OnEnable() { LocalizationManager.LanguageChanged += ShowVersion; }
    void OnDisable() { LocalizationManager.LanguageChanged -= ShowVersion; }

    void Awake() {
        Time.timeScale = 1f;
        if (newGameButton != null) newGameButton.onClick.AddListener(OnClickNewGame);
        if (loadGameButton != null) loadGameButton.onClick.AddListener(OnClickLoadGame);
        if (modsButton != null) modsButton.onClick.AddListener(OnClickMods);
        if (quitButton != null) quitButton.onClick.AddListener(OnClickQuit);
        ShowVersion();
        RefreshLoadButtonAvailability();
    }

    // The version is never written into the scene or the code: it is read from Player Settings at
    // runtime, so a build can never disagree with the number shown on its own main menu.
    void ShowVersion() {
        if (versionText == null) return;
        versionText.text = LocalizationManager.Get(versionKey, Application.version);
    }

    // Loading is only offered once at least one slot holds a readable career; otherwise the player
    // would land on a Load Game screen full of empty cards with nothing to actually load.
    void RefreshLoadButtonAvailability() {
        if (loadGameButton == null) return;
        var manager = GameManager.Instance;
        bool anyCareer = false;
        if (manager != null) {
            for (int i = 0; i < manager.Saves.SlotCount; i++) {
                if (manager.Saves.Peek(i, manager.Content).HasCareer) { anyCareer = true; break; }
            }
        }
        loadGameButton.interactable = anyCareer;
    }

    /// <summary>Opens the slot picker to start a brand new career.</summary>
    public void OnClickNewGame() {
        if (slotSelectPanel != null) slotSelectPanel.Open(SaveSlotSelectMode.NewGame);
    }

    /// <summary>Opens the slot picker to resume an existing career.</summary>
    public void OnClickLoadGame() {
        if (slotSelectPanel != null) slotSelectPanel.Open(SaveSlotSelectMode.LoadGame);
    }

    /// <summary>Opens the installed-content listing.</summary>
    public void OnClickMods() {
        if (modsPanel != null) modsPanel.Open();
    }

    /// <summary>Closes the game, and stops Play Mode when running inside the editor.</summary>
    public void OnClickQuit() {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
