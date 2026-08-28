using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour {
    [SerializeField] Button startButton;
    [SerializeField] Button quitButton;

    void Awake() {
        Time.timeScale = 1f;
        if (startButton != null) startButton.onClick.AddListener(OnClickStart);
        if (quitButton != null) quitButton.onClick.AddListener(OnClickQuit);
    }

    public void OnClickStart() {
        SceneManager.LoadScene("GarageScene");
    }

    public void OnClickQuit() {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
