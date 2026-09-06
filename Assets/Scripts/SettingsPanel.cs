using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Settings screen opened from the pause menu: music volume and a guarded progress reset.
/// </summary>
/// <remarks>
/// The reset is deliberately two-step. The first press only reveals a confirmation row; nothing is
/// destroyed until the player presses the confirm button. Opening or closing the panel always
/// returns it to the unconfirmed state, so a stray click can never land on an already armed
/// confirmation.
/// </remarks>
public class SettingsPanel : MonoBehaviour {

    [Header("Music")]
    [SerializeField] Slider musicSlider;
    [SerializeField] TextMeshProUGUI musicValueText;

    [Header("Progress Reset")]
    [SerializeField] Button resetProgressButton;
    [SerializeField] GameObject confirmGroup;
    [SerializeField] Button confirmYesButton;
    [SerializeField] Button confirmNoButton;

    [Header("Navigation")]
    [SerializeField] Button backButton;


    /// <summary>Raised when the player closes this panel with the back button.</summary>
    public event System.Action Closed;

    void Awake() {
        if (musicSlider != null) {
            musicSlider.minValue = 0f;
            musicSlider.maxValue = 100f;
            musicSlider.wholeNumbers = true;
            musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);
        }
        if (resetProgressButton != null) resetProgressButton.onClick.AddListener(OnClickResetProgress);
        if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnClickConfirmReset);
        if (confirmNoButton != null) confirmNoButton.onClick.AddListener(OnClickCancelReset);
        if (backButton != null) backButton.onClick.AddListener(OnClickBack);
    }

    void OnEnable() {
        if (musicSlider != null) musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        UpdateMusicLabel(GameSettings.MusicVolume);
        SetConfirmVisible(false);
    }

    void OnDisable() {
        SetConfirmVisible(false);
    }

    void OnMusicSliderChanged(float value) {
        int volume = Mathf.RoundToInt(value);
        GameSettings.MusicVolume = volume;
        UpdateMusicLabel(volume);
    }

    void UpdateMusicLabel(int volume) {
        if (musicValueText == null) return;
        // SetText writes straight into TMP's char buffer, so dragging the slider allocates nothing.
        musicValueText.SetText("{0}", volume);
    }

    void SetConfirmVisible(bool visible) {
        if (confirmGroup != null) confirmGroup.SetActive(visible);
        if (resetProgressButton != null) resetProgressButton.gameObject.SetActive(!visible);
    }

    void OnClickResetProgress() {
        SetConfirmVisible(true);
    }

    void OnClickCancelReset() {
        SetConfirmVisible(false);
    }

    // The running session is built on progress that no longer exists, so it cannot continue: the
    // player is returned to the main menu rather than dropped back into a stale garage.
    void OnClickConfirmReset() {
        if (GameManager.Instance == null) return;

        GameManager.Instance.ResetProgress();
        SetConfirmVisible(false);

        // The pause menu froze time; the destination scene would otherwise open frozen.
        Time.timeScale = 1f;
        SceneManager.LoadScene(GameManager.Instance.Config.mainMenuScene);
    }

    void OnClickBack() {
        Closed?.Invoke();
    }
}
