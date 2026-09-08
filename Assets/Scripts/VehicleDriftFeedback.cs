using UnityEngine;

/// <summary>
/// Presents the actual drift state without changing vehicle physics or gameplay economy.
/// </summary>
/// <remarks>
/// All visual and audio references are optional. Existing vehicles therefore receive a visible
/// sprite tint and automatically generated tire marks immediately, while authored TrailRenderer,
/// ParticleSystem and AudioSource assets can still override the optional feedback content.
/// </remarks>
[RequireComponent(typeof(VehicleMovement))]
public class VehicleDriftFeedback : MonoBehaviour {

    [Header("Visual feedback")]
    [SerializeField] Color driftColor = new Color(1f, 0.78f, 0.2f, 1f);
    [SerializeField] float minimumFeedbackSpeed = 0.2f;
    [SerializeField] TrailRenderer[] driftTrails;
    [SerializeField] ParticleSystem driftParticles;

    [Header("Automatic tire marks")]
    [SerializeField] Material tireMarkMaterial;
    [Range(1, 2)] [SerializeField] int automaticTireMarkCount = 2;
    [Min(0.05f)] [SerializeField] float tireMarkTime = 1.4f;
    [Min(0.001f)] [SerializeField] float tireMarkWidth = 0.12f;
    [Range(0f, 1f)] [SerializeField] float tireMarkAlpha = 0.55f;
    [Min(0.001f)] [SerializeField] float tireMarkMinVertexDistance = 0.04f;
    [Range(0f, 1f)] [SerializeField] float tireMarkRearOffset = 0.55f;
    [Range(0f, 1f)] [SerializeField] float tireMarkSideOffset = 0.68f;
    [SerializeField] int tireMarkSortingOffset = -1;

    [Header("Optional drift audio")]
    [SerializeField] AudioSource driftAudioSource;
    [SerializeField] AudioClip driftLoopClip;
    [Range(0f, 1f)] [SerializeField] float minimumDriftAudioVolume = 0.15f;
    [Min(1f)] [SerializeField] float slipAngleForMaxAudio = 45f;
    [SerializeField] float driftAudioVolume = 0.35f;

    VehicleMovement movement;
    SpriteRenderer bodyRenderer;
    Material runtimeTireMarkMaterial;
    Color baseColor = Color.white;
    bool feedbackActive;

    void Awake() {
        movement = GetComponent<VehicleMovement>();
        bodyRenderer = GetComponent<SpriteRenderer>();
        if (bodyRenderer != null) baseColor = bodyRenderer.color;
        CreateAutomaticTireMarksIfNeeded();
        ConfigureAudio();
        SetFeedbackActive(false);
    }

    void CreateAutomaticTireMarksIfNeeded() {
        if (driftTrails != null && driftTrails.Length > 0) return;
        if (bodyRenderer == null || bodyRenderer.sprite == null) return;

        Vector2 spriteSize = bodyRenderer.sprite.bounds.size;
        float halfWidth = Mathf.Max(0.01f, spriteSize.x * tireMarkSideOffset * 0.5f);
        float rearPosition = -Mathf.Max(0.01f, spriteSize.y * tireMarkRearOffset * 0.5f);
        Material material = tireMarkMaterial;
        if (material == null) {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) {
                runtimeTireMarkMaterial = new Material(shader);
                material = runtimeTireMarkMaterial;
            }
        }

        if (material == null) return;
        int trailCount = Mathf.Clamp(automaticTireMarkCount, 1, 2);
        driftTrails = new TrailRenderer[trailCount];
        if (trailCount == 1) {
            driftTrails[0] = CreateTireMarkTrail(0f, rearPosition, material);
            return;
        }

        driftTrails[0] = CreateTireMarkTrail(-halfWidth, rearPosition, material);
        driftTrails[1] = CreateTireMarkTrail(halfWidth, rearPosition, material);
    }

    TrailRenderer CreateTireMarkTrail(float localX, float localY, Material material) {
        GameObject trailObject = new GameObject();
        trailObject.transform.SetParent(transform, false);
        trailObject.transform.localPosition = new Vector3(localX, localY, 0f);

        TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
        trail.time = Mathf.Max(0.05f, tireMarkTime);
        trail.startWidth = Mathf.Max(0.001f, tireMarkWidth);
        trail.endWidth = Mathf.Max(0.001f, tireMarkWidth * 0.75f);
        trail.minVertexDistance = Mathf.Max(0.001f, tireMarkMinVertexDistance);
        trail.autodestruct = false;
        trail.emitting = false;
        trail.sharedMaterial = material;
        trail.sortingLayerID = bodyRenderer.sortingLayerID;
        trail.sortingOrder = bodyRenderer.sortingOrder + tireMarkSortingOffset;

        float alpha = Mathf.Clamp01(tireMarkAlpha);
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha * 0.15f, 1f) });
        trail.colorGradient = gradient;
        return trail;
    }

    void Update() {
        bool shouldBeActive = Time.timeScale > 0f && movement != null && movement.IsDrifting &&
                              movement.NormalizedSpeed >= Mathf.Max(0f, minimumFeedbackSpeed);
        if (shouldBeActive == feedbackActive) {
            UpdateAudioVolume(shouldBeActive);
            return;
        }
        SetFeedbackActive(shouldBeActive);
    }

    void ConfigureAudio() {
        if (driftAudioSource == null || driftLoopClip == null) return;
        driftAudioSource.clip = driftLoopClip;
        driftAudioSource.loop = true;
        driftAudioSource.playOnAwake = false;
        driftAudioSource.volume = 0f;
    }

    void SetFeedbackActive(bool active) {
        feedbackActive = active;
        if (bodyRenderer != null) bodyRenderer.color = active ? driftColor : baseColor;

        if (driftTrails != null) {
            foreach (TrailRenderer trail in driftTrails) {
                if (trail != null) trail.emitting = active;
            }
        }

        if (driftParticles != null) {
            if (active) driftParticles.Play();
            else driftParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        if (driftAudioSource == null || driftLoopClip == null) return;
        if (active) {
            UpdateAudioVolume(true);
            if (!driftAudioSource.isPlaying) driftAudioSource.Play();
        }
        else if (driftAudioSource.isPlaying) {
            driftAudioSource.Stop();
        }
    }

    void UpdateAudioVolume(bool active) {
        if (driftAudioSource == null || driftLoopClip == null) return;
        float intensity = 0f;
        if (active && movement != null) {
            float speedIntensity = Mathf.InverseLerp(minimumFeedbackSpeed, 1f, movement.NormalizedSpeed);
            float angleIntensity = Mathf.InverseLerp(0f, Mathf.Max(1f, slipAngleForMaxAudio), movement.SlipAngle);
            intensity = Mathf.Max(speedIntensity, angleIntensity);
        }

        float minimumVolume = Mathf.Clamp01(minimumDriftAudioVolume);
        float targetVolume = active
            ? Mathf.Clamp01(driftAudioVolume) * Mathf.Lerp(minimumVolume, 1f, intensity)
            : 0f;
        driftAudioSource.volume = Mathf.MoveTowards(driftAudioSource.volume, targetVolume, Time.unscaledDeltaTime * 4f);
    }

    /// <summary>Stops drift feedback immediately when the vehicle is disabled or a session ends.</summary>
    public void StopFeedback() {
        SetFeedbackActive(false);
    }

    void OnDisable() {
        SetFeedbackActive(false);
    }

    void OnDestroy() {
        if (runtimeTireMarkMaterial != null) Destroy(runtimeTireMarkMaterial);
    }
}
