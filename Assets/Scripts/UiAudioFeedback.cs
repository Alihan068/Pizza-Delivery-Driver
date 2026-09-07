using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a short shared sound when a UI button is activated.
/// </summary>
/// <remarks>
/// The audio source is owned by the persistent GameManager, so scene transitions do not create a
/// source per button and every click is routed through the SFX mixer bus. The source is resolved on
/// demand because Unity does not guarantee that the GameManager prefab awakens before every menu
/// button in every scene.
/// </remarks>
public class UiAudioFeedback : MonoBehaviour {

    [Tooltip("Sound played after this button is activated.")]
    [SerializeField] AudioClip clickClip;

    [Tooltip("Playback volume relative to the shared SFX bus.")]
    [SerializeField] float volume = 0.8f;

    Button button;
    AudioSource audioSource;

    void Awake() {
        button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(PlayClick);
    }

    void OnDestroy() {
        if (button != null) button.onClick.RemoveListener(PlayClick);
    }

    void PlayClick() {
        if (clickClip == null) return;
        if (audioSource == null) {
            GameManager manager = GameManager.Instance;
            if (manager == null) manager = FindFirstObjectByType<GameManager>();
            if (manager != null) audioSource = manager.GetComponent<AudioSource>();
        }

        if (audioSource != null) audioSource.PlayOneShot(clickClip, Mathf.Clamp01(volume));
    }
}
