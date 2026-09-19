using UnityEngine;

/// <summary>Authored NPC collision damage tuning, mirroring the player's existing base/factor/exponent shape (Driver.cs) so the two feel consistent.</summary>
[System.Serializable]
public class NpcCollisionDamageProfile {
    [Tooltip("Impact speed below this never causes damage — filters out normal scraping/parked contact.")]
    public float minimumImpactSpeed = 1.5f;

    [Tooltip("Seconds this instance ignores further collision damage after one is applied. Prevents CollisionStay spam from one ongoing contact.")]
    public float damageCooldownSeconds = 0.5f;

    public float damageBase = 3f;
    public float damageFactor = 0.85f;
    public float damageExponent = 2f;
}
