using UnityEngine;

/// <summary>
/// Applies machine display preferences before the first frame of the persistent game session.
/// </summary>
/// <remarks>
/// This component belongs on the persistent GameManager prefab. Keeping the bootstrap there gives
/// every scene the same startup behavior and avoids each menu or gameplay scene applying a different
/// resolution after its first frame has already been rendered.
/// </remarks>
[DefaultExecutionOrder(-1100)]
public class DisplaySettingsBootstrap : MonoBehaviour {

    [Tooltip("Project defaults used when this machine has no saved display preferences.")]
    [SerializeField] GameConfig config;

    void Awake() {
        DisplaySettings.ApplySaved(config);
    }
}
