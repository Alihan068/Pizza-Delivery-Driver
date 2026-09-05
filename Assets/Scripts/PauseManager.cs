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

    [Header("Settings")]
    [SerializeField] GameObject pauseMenuRoot;
    [SerializeField] Button settingsButton;
    [SerializeField] SettingsPanel settingsPanel;

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
        if (settingsButton != null) settingsButton.onClick.AddListener(OnClickSettings);
        if (settingsPanel != null) settingsPanel.Closed += CloseSettings;

        if (pauseCanvas != null) pauseCanvas.SetActive(false);
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
    }

    void OnDestroy() {
        if (settingsPanel != null) settingsPanel.Closed -= CloseSettings;
    }

    void Update() {
        if (scoreHandler != null && !scoreHandler.IsGameActive) return;
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;

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
        if (pauseCanvas != null) pauseCanvas.SetActive(false);
        if (scoreHandler != null) scoreHandler.EndLevel(EndReason.Abandoned, "GarageScene");
    }

    /// <summary>Abandons the session and returns to the main menu.</summary>
    public void OnClickMainMenu() {
        if (pauseCanvas != null) pauseCanvas.SetActive(false);
        if (scoreHandler != null) scoreHandler.EndLevel(EndReason.Abandoned, "MainMenu");
    }
}
