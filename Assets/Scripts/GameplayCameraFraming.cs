using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Keeps the gameplay camera's horizontal world-space framing stable across display aspect ratios.
/// </summary>
/// <remarks>
/// Orthographic cameras normally keep their vertical size fixed, which exposes substantially more
/// horizontal gameplay space on ultrawide displays. This component derives the orthographic size
/// from the shared <see cref="GameConfig"/> so the authored horizontal width remains stable while
/// the configurable minimum prevents overly aggressive zoom on very wide screens. It only writes
/// the Cinemachine lens when the display dimensions change, so it does not compete with the camera
/// follow update loop.
/// </remarks>
[RequireComponent(typeof(CinemachineCamera))]
public class GameplayCameraFraming : MonoBehaviour {

    [Tooltip("Shared data asset containing the authored gameplay camera framing values.")]
    [SerializeField] GameConfig config;

    CinemachineCamera cinemachineCamera;
    int lastScreenWidth = -1;
    int lastScreenHeight = -1;

    void Awake() {
        if (!TryGetComponent(out cinemachineCamera)) {
            Debug.LogError("GameplayCameraFraming requires a CinemachineCamera component.", this);
            enabled = false;
            return;
        }

        if (config == null) {
            Debug.LogError("GameplayCameraFraming requires a GameConfig asset reference.", this);
            enabled = false;
        }
    }

    void OnEnable() {
        ApplyIfDisplayChanged(true);
    }

    void LateUpdate() {
        ApplyIfDisplayChanged(false);
    }

    void ApplyIfDisplayChanged(bool force) {
        if (cinemachineCamera == null || config == null) return;
        if (Screen.width <= 0 || Screen.height <= 0) return;
        if (!force && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight) return;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        float aspect = (float)Screen.width / Screen.height;
        float orthographicSize = config.cameraReferenceHorizontalWorldSize / (2f * aspect);
        orthographicSize = Mathf.Max(config.cameraMinimumOrthographicSize, orthographicSize);

        LensSettings lens = cinemachineCamera.Lens;
        if (Mathf.Approximately(lens.OrthographicSize, orthographicSize)) return;
        lens.OrthographicSize = orthographicSize;
        cinemachineCamera.Lens = lens;
    }
}
