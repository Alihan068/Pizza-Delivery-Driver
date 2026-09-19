using UnityEngine;

/// <summary>Authored thresholds for <see cref="DamageAttributionResolver"/>. Never a fixed five-number table — always read from here.</summary>
[System.Serializable]
public class DamageAttributionRules {
    [Tooltip("Minimum contact-point closing contribution required before an actor can be blamed at all.")]
    public float minimumApproachThreshold = 0.5f;

    [Tooltip("An actor's contribution must exceed the other side's by at least this much to be blamed; otherwise the contact is ruled Environment (ambiguous or equal head-on).")]
    public float attributionDeltaTolerance = 0.5f;
}
