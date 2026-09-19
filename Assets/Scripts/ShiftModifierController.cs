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
    FrozenModifierRules frozenModifiers;

    void Awake() {
        if (globalLight == null) return;
        baseLightColor = globalLight.color;
        baseLightIntensity = globalLight.intensity;
        capturedBaseLight = true;
    }

    void Start() {
        ApplySelectedModifier();
    }

    /// <summary>Applies the captured session blend, independent of later selection or asset edits.</summary>
    public void ApplySelectedModifier() {
        if (globalLight == null || !capturedBaseLight) return;

        if (frozenModifiers == null) {
            var host = SessionSceneRules.ResolveHost(gameObject.scene);
            if (host != null) {
                host.PrepareSession(GameManager.Instance);
                frozenModifiers = host.Coordinator?.Context.Modifiers;
            }
            else if (GameManager.Instance != null) {
                frozenModifiers = GameManager.Instance.PrepareSession(gameObject.scene).Modifiers;
            }
            frozenModifiers ??= new FrozenModifierRules(null);
        }
        frozenModifiers.ApplyLight(baseLightColor, baseLightIntensity,
            out Color resultColor, out float resultIntensity);
        globalLight.color = resultColor;
        globalLight.intensity = resultIntensity;
    }

    void OnDestroy() {
        if (globalLight == null || !capturedBaseLight) return;
        globalLight.color = baseLightColor;
        globalLight.intensity = baseLightIntensity;
    }
}
