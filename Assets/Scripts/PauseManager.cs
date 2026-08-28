using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PauseManager : MonoBehaviour {
    [SerializeField] GameObject pauseCanvas;
    [SerializeField] Button resumeButton;
    [SerializeField] Button garageButton;
    [SerializeField] Button mainMenuButton;

    ScoreHandler scoreHandler;
    bool isPaused;

    void Start() {
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        if (pauseCanvas != null) pauseCanvas.SetActive(false);

        if (resumeButton != null) resumeButton.onClick.AddListener(OnClickResume);
        if (garageButton != null) garageButton.onClick.AddListener(OnClickReturnToGarage);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnClickMainMenu);
    }

    void Update() {
        if (scoreHandler != null && !scoreHandler.IsGameActive) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) {
            TogglePause();
        }
    }

    public void TogglePause() {
        if (isPaused) Resume();
        else Pause();
    }

    void Pause() {
        isPaused = true;
        Time.timeScale = 0f;
        if (pauseCanvas != null) pauseCanvas.SetActive(true);
    }

    void Resume() {
        isPaused = false;
        Time.timeScale = 1f;
        if (pauseCanvas != null) pauseCanvas.SetActive(false);
    }

    public void OnClickResume() {
        Resume();
    }

    // Leaving from Pause is abandoning the session - sessionEarnings burns, but
    // there's no death penalty (repair is still costed off the real current health).
    public void OnClickReturnToGarage() {
        if (pauseCanvas != null) pauseCanvas.SetActive(false);
        if (scoreHandler != null) scoreHandler.EndLevel(EndReason.Abandoned, "GarageScene");
    }

    public void OnClickMainMenu() {
        if (pauseCanvas != null) pauseCanvas.SetActive(false);
        if (scoreHandler != null) scoreHandler.EndLevel(EndReason.Abandoned, "MainMenu");
    }
}
