using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Owns the in-session pause state: the Escape key, the pause menu and the settings screen.
/// </summary>
/// <remarks>
/// This component must NOT live on <see cref="pauseCanvas"/> or any of its children. Hiding the
/// pause menu deactivates that object, and a component on it would stop receiving <c>Update</c>,
/// which silently kills the Escape key after the first hide. <see cref="Start"/> asserts this.
/// </remarks>
public class PauseManager : MonoBehaviour {
    [SerializeField] GameObject pauseCanvas;
    [SerializeField] Button resumeButton;
    [SerializeField] Button garageButton;
    [SerializeField] Button mainMenuButton;
    [Tooltip("Optional authored restart button. When empty, one is created at runtime from the garage button so every gameplay scene gets it without scene edits.")]
    [SerializeField] Button restartButton;

    [Header("Settings")]
    [SerializeField] GameObject pauseMenuRoot;
    [SerializeField] Button settingsButton;
    [SerializeField] SettingsPanel settingsPanel;

    [Header("Cost Transparency")]
    [Tooltip("Shown alongside the pause menu buttons, replacing the old quick-save button.")]
    [SerializeField] PauseCostPanel costPanel;

    ScoreHandler scoreHandler;
    bool isPaused;

    void Start() {
        scoreHandler = FindFirstObjectByType<ScoreHandler>();

        if (pauseCanvas != null && (pauseCanvas == gameObject || transform.IsChildOf(pauseCanvas.transform))) {
            Debug.LogError("PauseManager lives inside the object it hides. Hiding the menu would disable this component and Escape would stop working. Move PauseManager to a separate GameObject.", this);
        }

        if (resumeButton != null) resumeButton.onClick.AddListener(OnClickResume);
        if (garageButton != null) garageButton.onClick.AddListener(OnClickReturnToGarage);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnClickMainMenu);
        // Every gameplay scene carries the SettingsPanel prefab under PauseCanvas, but the field was never
        // wired, so Settings hid the pause menu and showed nothing. Resolve it from this pause canvas only.
        if (settingsPanel == null && pauseCanvas != null) settingsPanel = pauseCanvas.GetComponentInChildren<SettingsPanel>(true);
        if (settingsPanel == null && settingsButton != null)
            Debug.LogWarning("PauseManager has no SettingsPanel under its pause canvas; the Settings button cannot open anything.", this);
        if (restartButton == null) restartButton = CreateRestartButton();
        if (restartButton != null) restartButton.onClick.AddListener(OnClickRestart);
        if (settingsButton != null) settingsButton.onClick.AddListener(OnClickSettings);
        if (settingsPanel != null) {
            settingsPanel.Closed += CloseSettings;
            settingsPanel.ReturnToGarageRequested += OnClickReturnToGarage;
            settingsPanel.ReturnToMainMenuRequested += OnClickMainMenu;
        }

        if (pauseCanvas != null) pauseCanvas.SetActive(false);
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
    }

    void OnDestroy() {
        if (resumeButton != null) resumeButton.onClick.RemoveListener(OnClickResume);
        if (garageButton != null) garageButton.onClick.RemoveListener(OnClickReturnToGarage);
        if (mainMenuButton != null) mainMenuButton.onClick.RemoveListener(OnClickMainMenu);
        if (restartButton != null) restartButton.onClick.RemoveListener(OnClickRestart);
        if (settingsButton != null) settingsButton.onClick.RemoveListener(OnClickSettings);
        if (settingsPanel != null) {
            settingsPanel.Closed -= CloseSettings;
            settingsPanel.ReturnToGarageRequested -= OnClickReturnToGarage;
            settingsPanel.ReturnToMainMenuRequested -= OnClickMainMenu;
        }
    }

    void Update() {
        if (S12BenchmarkGate.Requested) return;
        if (scoreHandler != null && !scoreHandler.IsGameActive) return;
        bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool gamepadPausePressed = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
        if (!escapePressed && !gamepadPausePressed) return;

        // While the settings screen is open, Escape steps back to the pause menu instead of
        // unpausing, so the player never resumes the game straight out of a submenu.
        if (isPaused && settingsPanel != null && settingsPanel.gameObject.activeSelf) {
            CloseSettings();
            return;
        }
        TogglePause();
    }

    /// <summary>Pauses the game when it is running, resumes it when it is paused.</summary>
    public void TogglePause() {
        if (isPaused) Resume();
        else Pause();
    }

    void Pause() {
        isPaused = true;
        Time.timeScale = 0f;
        if (pauseCanvas != null) pauseCanvas.SetActive(true);
        ShowPauseMenu();
    }

    void Resume() {
        isPaused = false;
        Time.timeScale = 1f;
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
        if (pauseCanvas != null) pauseCanvas.SetActive(false);
    }

    void ShowPauseMenu() {
        if (pauseMenuRoot != null) pauseMenuRoot.SetActive(true);
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
        if (costPanel != null) costPanel.Refresh();
    }

    void OpenSettings() {
        if (pauseMenuRoot != null) pauseMenuRoot.SetActive(false);
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(true);
    }

    void CloseSettings() {
        ShowPauseMenu();
    }

    /// <summary>Closes the pause menu and lets the session continue.</summary>
    public void OnClickResume() {
        Resume();
    }

    /// <summary>Opens the settings screen in place of the pause menu.</summary>
    public void OnClickSettings() {
        OpenSettings();
    }

    // Leaving from Pause is abandoning the session - sessionEarnings burns, but
    // there's no death penalty (repair is still costed off the real current health).
    /// <summary>Abandons the session and returns to the garage.</summary>
    public void OnClickReturnToGarage() {
        AbandonTo(GameManager.Instance != null ? GameManager.Instance.Config.garageScene : null);
    }

    /// <summary>Abandons the session and returns to the main menu.</summary>
    public void OnClickMainMenu() {
        AbandonTo(GameManager.Instance != null ? GameManager.Instance.Config.mainMenuScene : null);
    }

    /// <summary>
    /// Restarts the shift on the same map with the same vehicle, difficulty, duration and modifiers.
    /// The running shift is settled exactly like leaving from pause (unbanked earnings are lost,
    /// repair is charged on the real remaining health), so a restart is never a free escape from a
    /// bad run. If the replay cannot start, the normal result panel stays up as the fallback.
    /// </summary>
    public void OnClickRestart() {
        GameManager manager = GameManager.Instance;
        if (manager == null || scoreHandler == null) return;
        AbandonTo(manager.Config.garageScene);
        if (!manager.ReplayLastSession()) Debug.LogWarning("Restart could not replay the last shift; showing the result panel instead.", this);
    }

    /// <summary>Clones the garage button into a restart button directly under Resume, relabelled through localization.</summary>
    Button CreateRestartButton() {
        if (garageButton == null) return null;
        Button clone = Instantiate(garageButton, garageButton.transform.parent);
        clone.name = "RestartButton";
        clone.onClick = new Button.ButtonClickedEvent(); // drop any persistent calls copied from the garage button
        int index = resumeButton != null && resumeButton.transform.parent == clone.transform.parent
            ? resumeButton.transform.GetSiblingIndex() + 1 : garageButton.transform.GetSiblingIndex();
        clone.transform.SetSiblingIndex(index);
        LocalizedText label = clone.GetComponentInChildren<LocalizedText>(true);
        if (label != null) label.SetKey("pause.restart");
        else {
            TMPro.TMP_Text text = clone.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (text != null) text.text = LocalizationManager.Get("pause.restart");
        }
        return clone;
    }

    void AbandonTo(string destinationScene) {
        if (string.IsNullOrEmpty(destinationScene)) {
            Debug.LogError("No GameConfig is assigned, so the destination scene name is unknown.");
            return;
        }
        if (pauseCanvas != null) pauseCanvas.SetActive(false);
        if (scoreHandler != null) scoreHandler.EndLevel(EndReason.Abandoned, destinationScene);
    }
}
