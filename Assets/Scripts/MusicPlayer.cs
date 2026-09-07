using UnityEngine;

/// <summary>
/// Keeps an authored music AudioSource looping while the shared audio mixer controls its volume.
/// </summary>
/// <remarks>
/// The source is routed to the Music mixer group. Volume preferences are applied centrally by
/// <see cref="AudioMixerSettings"/>, so this component does not multiply the source volume again.
/// </remarks>
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour {

    AudioSource source;
    void Awake() {
        source = GetComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = true;
    }

    void OnEnable() {
        if (source != null && source.clip != null && source.playOnAwake && !source.isPlaying) source.Play();
    }
}
