using UnityEngine;

/// <summary>
/// One NPC's runtime health for its current life. Never mutates the authored ScriptableObject's
/// maxHealth — every value here is a per-instance runtime copy. Guarantees exactly one
/// Alive→Wreck transition (and exactly one <see cref="VehicleDestroyedEvent"/>) per life: a second
/// lethal hit the same frame, a stale event from a life this instance already left behind (pool
/// reuse), or any damage after the session has ended are all rejected as no-ops rather than
/// re-killing or double-counting.
/// </summary>
public sealed class NpcVehicleHealth {
    float currentHealth;
    float maxHealth;
    bool destroyedThisLife;
    bool sessionActive = true;
    bool initialized;
    int lifeId;

    /// <summary>Remaining finite health for the current life, or zero before initialization/release.</summary>
    public float CurrentHealth => currentHealth;
    /// <summary>Finite positive authored health copied for this life; zero without an initialized life.</summary>
    public float MaxHealth => maxHealth;
    /// <summary>Whether this initialized life has already emitted its single destruction event.</summary>
    public bool IsDestroyed => destroyedThisLife;
    /// <summary>Whether a valid life has been initialized and has not been released to the pool.</summary>
    public bool IsInitialized => initialized;
    /// <summary>Session gate; false is terminal and requires a new health instance for a new session.</summary>
    public bool IsSessionActive => sessionActive;
    /// <summary>Most recently accepted life ID; it remains reserved after release to reject stale reuse.</summary>
    public int LifeId => lifeId;

    /// <summary>
    /// Compatibility initializer. Invalid health, non-increasing life IDs and ended sessions are
    /// no-ops; use TryInitializeForNewLife when the caller needs an acceptance result.
    /// </summary>
    /// <param name="authoredMaxHealth">The ScriptableObject's authored max health for this profile — read, never written.</param>
    /// <param name="newLifeId">This life's lifeId, used to reject any later stale damage from a previous life.</param>
    public void InitializeForNewLife(float authoredMaxHealth, int newLifeId) {
        TryInitializeForNewLife(authoredMaxHealth, newLifeId);
    }

    /// <summary>
    /// Starts a full-health life only for finite positive health and a fresh, increasing ID from
    /// the session registry. Rejected requests preserve all existing state, including session end.
    /// </summary>
    /// <returns>True when a fresh life was accepted; false for invalid, stale or ended-session requests.</returns>
    public bool TryInitializeForNewLife(float authoredMaxHealth, int newLifeId) {
        if (!sessionActive || !IsFinite(authoredMaxHealth) || authoredMaxHealth <= 0f || newLifeId <= lifeId) return false;
        maxHealth = authoredMaxHealth;
        currentHealth = maxHealth;
        destroyedThisLife = false;
        lifeId = newLifeId;
        initialized = true;
        return true;
    }

    /// <summary>Ends damage acceptance permanently when false; true never reopens an ended session.</summary>
    public void SetSessionActive(bool active) {
        sessionActive &= active;
    }

    /// <summary>Invalidates a released receiver without ending the session or permitting reuse of its old ID.</summary>
    public void InvalidateLife() {
        initialized = false;
        currentHealth = 0f;
        maxHealth = 0f;
        destroyedThisLife = false;
    }

    /// <summary>Checks session, initialization, death and target life immediately before receiver dispatch.</summary>
    /// <param name="victimLifeId">Target identity from the queued application, never the source identity.</param>
    public bool CanReceiveDamage(int victimLifeId) {
        return sessionActive && initialized && !destroyedThisLife && victimLifeId == lifeId;
    }

    /// <summary>
    /// Applies damage for the given life. Rejected outright (no state change) when the session is
    /// inactive, this life is already destroyed, or callerLifeId does not match this instance's
    /// current life (a stale reference from before a pool reuse).
    /// </summary>
    /// <param name="callerLifeId">The lifeId the caller believes it is damaging.</param>
    /// <param name="amount">Damage to apply; non-positive or non-finite amounts are ignored.</param>
    /// <param name="context">Origin of this damage.</param>
    /// <param name="victimRole">This vehicle's role, carried into the destroyed event.</param>
    /// <param name="sessionTime">Finite non-negative session time; invalid timestamps reject the whole hit.</param>
    /// <param name="destroyedEvent">Populated only when this call is the one that brings health to zero.</param>
    /// <returns>True exactly once per life, on the hit that kills it.</returns>
    public bool ApplyDamage(int callerLifeId, float amount, DamageContext context, VehicleRole victimRole,
        float sessionTime, out VehicleDestroyedEvent destroyedEvent) {
        return TryApplyDamage(callerLifeId, amount, context, victimRole, sessionTime, out destroyedEvent) && destroyedThisLife;
    }

    /// <summary>
    /// Receiver API returning true for any accepted hit, including nonlethal damage. Check
    /// IsDestroyed after acceptance before consuming destroyedEvent; rejected hits always output
    /// default. The legacy ApplyDamage method continues returning true only for the lethal hit.
    /// </summary>
    /// <param name="victimLifeId">Current target life, distinct from the source and instigator in context.</param>
    /// <param name="amount">Finite positive damage; invalid values cause no state changes.</param>
    /// <param name="context">Source event and root attribution copied only into a lethal event.</param>
    /// <param name="victimRole">Target role carried into the destruction event.</param>
    /// <param name="sessionTime">Finite non-negative active-session timestamp.</param>
    /// <param name="destroyedEvent">Populated only for the first lethal hit of this life.</param>
    /// <returns>True only when health changed for an eligible target life.</returns>
    public bool TryApplyDamage(int victimLifeId, float amount, DamageContext context, VehicleRole victimRole,
        float sessionTime, out VehicleDestroyedEvent destroyedEvent) {
        destroyedEvent = default;
        if (!CanReceiveDamage(victimLifeId) || !IsFinite(amount) || amount <= 0f ||
            !IsFinite(sessionTime) || sessionTime < 0f) return false;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        if (currentHealth > 0f) return true;

        destroyedThisLife = true;
        destroyedEvent = new VehicleDestroyedEvent(victimLifeId, victimRole, context, sessionTime);
        return true;
    }

    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
