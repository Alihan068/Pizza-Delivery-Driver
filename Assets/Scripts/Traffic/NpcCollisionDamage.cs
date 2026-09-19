using UnityEngine;

/// <summary>
/// Pure NPC collision damage calculator. Impact speed is expected to already be the contact-normal
/// closing speed (<see cref="VehicleDrivingMath.CalculateClosingSpeed"/> — the single shared
/// formula the player's own Driver.cs collision damage also uses, so a fast side-scrape and a
/// head-on hit are never confused: a scrape's relative velocity runs mostly parallel to the contact
/// normal and yields a low closing speed, a head-on hit's runs into it and yields a high one).
/// Never itself decides life/death, EndLevel, or score — it only returns how much damage one
/// contact is worth, gated by an authored dead-zone and per-instance cooldown so multiple colliders
/// or a sustained CollisionStay never multiply damage from what is physically one impact.
/// </summary>
public static class NpcCollisionDamage {
    /// <summary>
    /// Computes this contact's damage. Rejected (no damage) when still within the profile's cooldown
    /// since the last applied hit, or when impact speed is below the dead-zone.
    /// </summary>
    /// <param name="profile">Authored tuning.</param>
    /// <param name="impactSpeed">Contact-normal closing speed for this collision.</param>
    /// <param name="armorPercent">Damage reduction in [0, 1].</param>
    /// <param name="lastDamageSessionTime">Session time this instance last actually took collision damage.</param>
    /// <param name="sessionTime">Current session time.</param>
    /// <param name="damageAmount">The computed damage; zero when rejected.</param>
    /// <returns>True when this contact is a real, cooldown-cleared, above-dead-zone hit.</returns>
    public static bool TryCalculateDamage(NpcCollisionDamageProfile profile, float impactSpeed, float armorPercent,
        float lastDamageSessionTime, float sessionTime, out float damageAmount) {
        damageAmount = 0f;
        if (profile == null) return false;
        if (sessionTime - lastDamageSessionTime < profile.damageCooldownSeconds) return false;
        if (impactSpeed < profile.minimumImpactSpeed) return false;

        float rawDamage = profile.damageBase + profile.damageFactor * Mathf.Pow(impactSpeed, profile.damageExponent);
        damageAmount = rawDamage * (1f - Mathf.Clamp01(armorPercent));
        return true;
    }
}
