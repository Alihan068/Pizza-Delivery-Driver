using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Applies the persisted master, music and sound-effect settings to the shared audio mixer.
/// </summary>
/// <remarks>
/// The component is placed on the persistent GameManager prefab. Each exposed parameter name is
/// serialized so the mixer asset can be replaced or extended without changing runtime code. Volume
/// values use the standard logarithmic decibel conversion; zero volume is clamped to the authored
/// silence floor because logarithm of zero is undefined.
/// </remarks>
public class AudioMixerSettings : MonoBehaviour {

    [Header("Mixer")]
    [Tooltip("Shared mixer that owns the Master, Music and SFX buses.")]
    [SerializeField] AudioMixer mixer;
    [Tooltip("Exposed master attenuation parameter on the mixer.")]
    [SerializeField] string masterParameter;
    [Tooltip("Exposed music attenuation parameter on the mixer.")]
    [SerializeField] string musicParameter;
    [Tooltip("Exposed sound-effect attenuation parameter on the mixer.")]
    [SerializeField] string sfxParameter;

    [Header("Conversion")]
    [Tooltip("Lowest mixer attenuation used for a zero-volume slider value.")]
    [SerializeField] float silenceDecibels = -80f;
    [Tooltip("Decibel change represented by one tenfold change in linear volume.")]
    [SerializeField] float decibelsPerLogStep = 20f;

    void OnEnable() {
        GameSettings.Changed += Apply;
        Apply();
    }

    void OnDisable() {
        GameSettings.Changed -= Apply;
    }

    void Apply() {
        if (mixer == null) return;
        SetVolume(masterParameter, GameSettings.MasterVolumeNormalized);
        SetVolume(musicParameter, GameSettings.MusicVolumeNormalized);
        SetVolume(sfxParameter, GameSettings.SfxVolumeNormalized);
    }

    void SetVolume(string parameter, float normalized) {
        if (string.IsNullOrEmpty(parameter)) return;
        float safe = Mathf.Clamp01(normalized);
        float decibels = safe > Mathf.Epsilon ? Mathf.Log10(safe) * decibelsPerLogStep : silenceDecibels;
        mixer.SetFloat(parameter, decibels);
    }
}
