using UnityEngine;

/// <summary>Optional pooled visual layer; missing effects never suppress gameplay damage or wreck collisions.</summary>
public sealed class NpcDamagePresentation : MonoBehaviour {
    [SerializeField] SpriteRenderer body;
    [SerializeField] GameObject smoke;
    [SerializeField] GameObject critical;
    [SerializeField] Color wreckColor = Color.gray;
    Color originalColor;
    bool captured;

    /// <summary>Restores authored color and clears both feedback objects before reuse.</summary>
    public void ResetForLife() {
        if (!captured && body != null) { originalColor = body.color; captured = true; }
        if (body != null && captured) body.color = originalColor;
        if (smoke != null) smoke.SetActive(false);
        if (critical != null) critical.SetActive(false);
    }

    /// <summary>Applies an already-resolved feedback tier without mutating health.</summary>
    public void Show(NpcDamageFeedbackLevel level, bool wreck) {
        if (smoke != null) smoke.SetActive(!wreck && level != NpcDamageFeedbackLevel.None);
        if (critical != null) critical.SetActive(!wreck && level == NpcDamageFeedbackLevel.Critical);
        if (wreck && body != null) body.color = wreckColor;
    }
}
