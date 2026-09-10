using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Applies map-specific orthographic framing without changing the shared gameplay camera settings.
/// Secondary maps can therefore show more road ahead while the protected primary scene keeps its
/// existing camera contract and aspect-ratio behavior.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public class MapCameraFramingOverride : MonoBehaviour {
    [Tooltip("Horizontal world width that should remain visible on the reference aspect ratio.")]
    [Min(0.1f)] [SerializeField] float referenceHorizontalWorldSize = 24f;

    [Tooltip("Smallest orthographic size allowed on wide displays.")]
    [Min(0.1f)] [SerializeField] float minimumOrthographicSize = 5f;

    CinemachineCamera cinemachineCamera;
    int lastScreenWidth = -1;
    int lastScreenHeight = -1;

    void Awake() {
        if (!TryGetComponent(out cinemachineCamera)) {
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
        if (cinemachineCamera == null || Screen.width <= 0 || Screen.height <= 0) {
            return;
        }

        if (!force && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight) {
            return;
        }

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        float aspect = (float)Screen.width / Screen.height;
        float orthographicSize = referenceHorizontalWorldSize / (2f * aspect);
        orthographicSize = Mathf.Max(minimumOrthographicSize, orthographicSize);

        LensSettings lens = cinemachineCamera.Lens;
        if (Mathf.Approximately(lens.OrthographicSize, orthographicSize)) {
            return;
        }

        lens.OrthographicSize = orthographicSize;
        cinemachineCamera.Lens = lens;
    }
}
