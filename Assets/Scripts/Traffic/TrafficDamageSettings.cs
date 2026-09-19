using UnityEngine;

/// <summary>Scene-authored damage catalog, query limits and lifecycle timings; does not spawn traffic.</summary>
[CreateAssetMenu(menuName = "PizzaGame/Traffic/Damage Settings")]
public sealed class TrafficDamageSettings : ScriptableObject {
    /// <summary>Entries resolved by stable profile IDs, never asset names.</summary>
    public TrafficDamageProfile[] profiles = System.Array.Empty<TrafficDamageProfile>();
    /// <summary>Attribution thresholds sampled with the session.</summary>
    public DamageAttributionRules attribution = new DamageAttributionRules();
    /// <summary>Separate player, civilian and police blast coefficients.</summary>
    public BlastRoleMultipliers roles = new BlastRoleMultipliers();
    /// <summary>Heat payload coefficients consumed once per destroyed life.</summary>
    public HeatAwardRules heat = new HeatAwardRules();
    /// <summary>Maximum distinct blasts resolved per frame.</summary>
    [Min(1)] public int blastsPerTick = 8;
    /// <summary>Initial reusable overlap capacity; saturation grows it without dropping victims.</summary>
    [Min(1)] public int initialQueryCapacity = 32;
    /// <summary>Hard query capacity including one spare entry; saturation preserves queued work and invalidates the run.</summary>
    [Min(1)] public int maxQueryCapacity = 4096;
    /// <summary>Query retry budget per frame, including buffer growth.</summary>
    [Min(1)] public int queriesPerTick = 32;
    /// <summary>Whole-session unique explosion limit retained for idempotence.</summary>
    [Min(1)] public int maxSessionBlasts = 65536;
    /// <summary>Minimum active lifetime of a solid wreck before recycling.</summary>
    [Min(0f)] public float wreckLifetimeSeconds = 8f;
    /// <summary>Active time spent in crash recovery before ordinary driving can resume.</summary>
    [Min(0f)] public float recoverySeconds = 1f;
    /// <summary>Physical layers containing registered damage receivers.</summary>
    public LayerMask receiverLayers = ~0;

    /// <summary>Rejects invalid service budgets and timing values before constructing a runtime world.</summary>
    public bool IsValid() {
        return blastsPerTick > 0 && initialQueryCapacity > 0 && maxQueryCapacity >= initialQueryCapacity &&
            queriesPerTick > 0 && maxSessionBlasts > 0 && Valid(wreckLifetimeSeconds) && Valid(recoverySeconds) &&
            roles != null && Valid(roles.playerMultiplier) && Valid(roles.civilianMultiplier) && Valid(roles.policeMultiplier) &&
            attribution != null && Valid(attribution.minimumApproachThreshold) && Valid(attribution.attributionDeltaTolerance) &&
            heat != null && Valid(heat.civilianVictimHeat) && Valid(heat.policeVictimHeat);
    }

    static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

    /// <summary>Returns a detached valid tuning entry; missing/ambiguous IDs fail closed.</summary>
    public bool TryResolve(string id, out TrafficDamageProfile profile) {
        profile = null;
        if (string.IsNullOrEmpty(id) || profiles == null) return false;
        foreach (var entry in profiles) {
            if (entry == null || entry.profileId != id) continue;
            if (profile != null || !entry.IsValid()) { profile = null; return false; }
            profile = entry.Copy();
        }
        return profile != null;
    }
}
