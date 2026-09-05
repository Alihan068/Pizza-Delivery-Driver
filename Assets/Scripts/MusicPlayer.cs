using UnityEngine;

/// <summary>
/// Applies the player's music volume setting to the AudioSource on this object.
/// </summary>
/// <remarks>
/// The volume authored in the Inspector is captured once in <c>Awake</c> and treated as the
/// reference level, so the setting acts as a multiplier on top of the existing mix: at 100 the
/// track plays exactly as authored, at 50 at half that. Without this the slider would fight the
/// mix decisions already made on the AudioSource.
/// </remarks>
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour {

    AudioSource source;
    float authoredVolume;

    void Awake() {
        source = GetComponent<AudioSource>();
        authoredVolume = source.volume;
    }

    void OnEnable() {
        GameSettings.Changed += ApplyVolume;
        ApplyVolume();
    }

    void OnDisable() {
        GameSettings.Changed -= ApplyVolume;
    }

    void ApplyVolume() {
        if (source == null) return;
        source.volume = authoredVolume * GameSettings.MusicVolumeNormalized;
    }
}
