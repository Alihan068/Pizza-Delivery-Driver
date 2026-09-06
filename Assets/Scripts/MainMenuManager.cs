using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour {
    [Header("Buttons")]
    [SerializeField] Button startButton;
    [SerializeField] Button quitButton;

    [Header("Build Identity")]
    [SerializeField] TextMeshProUGUI versionText;

    [Tooltip("Localization key whose format receives the build version from Player Settings.")]
    [SerializeField] string versionKey = "menu.version";

    void OnEnable() { LocalizationManager.LanguageChanged += ShowVersion; }
    void OnDisable() { LocalizationManager.LanguageChanged -= ShowVersion; }

    void Awake() {
        Time.timeScale = 1f;
        if (startButton != null) startButton.onClick.AddListener(OnClickStart);
        if (quitButton != null) quitButton.onClick.AddListener(OnClickQuit);
        ShowVersion();
    }

    // The version is never written into the scene or the code: it is read from Player Settings at
    // runtime, so a build can never disagree with the number shown on its own main menu.
    // string.Format rather than TMP's SetText because the argument is a string, and SetText only
    // has numeric format overloads. This runs once, so the allocation is irrelevant.
    void ShowVersion() {
        if (versionText == null) return;
        versionText.text = LocalizationManager.Get(versionKey, Application.version);
    }

    /// <summary>Leaves the menu and opens the garage.</summary>
    public void OnClickStart() {
        var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
        if (config == null) {
            Debug.LogError("No GameConfig is assigned, so the garage scene name is unknown.");
            return;
        }
        SceneManager.LoadScene(config.garageScene);
    }

    /// <summary>Closes the game, and stops Play Mode when running inside the editor.</summary>
    public void OnClickQuit() {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
