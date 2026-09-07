using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Applies the selected shift modifier's presentation to the scene-local global light.
/// </summary>
/// <remarks>
/// This component belongs to each gameplay scene because the light is a scene object. The selected
/// modifier itself lives on <see cref="GameManager"/> only for the next shift and is never saved as
/// permanent career progress.
/// </remarks>
public class ShiftModifierController : MonoBehaviour {
    [Header("Scene Reference")]
    [Tooltip("The scene's global Light2D. Assign it in each gameplay scene.")]
    [SerializeField] Light2D globalLight;

    Color baseLightColor = Color.white;
    float baseLightIntensity = 1f;
    bool capturedBaseLight;

    void Awake() {
        if (globalLight == null) return;
        baseLightColor = globalLight.color;
        baseLightIntensity = globalLight.intensity;
        capturedBaseLight = true;
    }

    void Start() {
        ApplySelectedModifier();
    }

    /// <summary>Applies the selected modifier or restores the scene's authored lighting.</summary>
    public void ApplySelectedModifier() {
        if (globalLight == null || !capturedBaseLight) return;

        ShiftModifierData modifier = GameManager.Instance != null ? GameManager.Instance.CurrentModifier : null;
        if (modifier == null) {
            globalLight.color = baseLightColor;
            globalLight.intensity = baseLightIntensity;
            return;
        }

        globalLight.color = Color.Lerp(baseLightColor, modifier.lightColor, Mathf.Clamp01(modifier.lightBlend));
        globalLight.intensity = baseLightIntensity * Mathf.Max(0.01f, modifier.lightIntensityMultiplier);
    }

    void OnDestroy() {
        if (globalLight == null || !capturedBaseLight) return;
        globalLight.color = baseLightColor;
        globalLight.intensity = baseLightIntensity;
    }
}
