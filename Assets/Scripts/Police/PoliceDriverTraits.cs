using System;

/// <summary>Detached deterministic driver variation for one police life.</summary>
public sealed class PoliceDriverTraits {
    /// <summary>Multiplier applied to authored reaction timing.</summary>
    public readonly float reactionMultiplier;
    /// <summary>Multiplier applied to authored intercept prediction timing.</summary>
    public readonly float predictionMultiplier;
    /// <summary>Multiplier applied to authored follow gap.</summary>
    public readonly float followGapMultiplier;
    /// <summary>Bounded lateral preference, where negative and positive are opposite sides.</summary>
    public readonly float sidePreference;
    /// <summary>Bounded individual risk bias.</summary>
    public readonly float riskBias;
    /// <summary>Life identity that owns this detached sample.</summary>
    public readonly int lifeId;

    PoliceDriverTraits(int assignedLifeId, float reaction, float prediction, float gap, float side, float risk) {
        lifeId = assignedLifeId;
        reactionMultiplier = reaction;
        predictionMultiplier = prediction;
        followGapMultiplier = gap;
        sidePreference = side;
        riskBias = risk;
    }

    /// <summary>Creates a stable sample from frozen session, spawn, and vehicle identities.</summary>
    /// <param name="sessionSeed">Frozen session seed.</param>
    /// <param name="spawnOrdinal">Stable spawn ordinal within the session.</param>
    /// <param name="stableVehicleId">Stable authored vehicle profile ID.</param>
    /// <param name="assignedLifeId">Life identity receiving the detached sample.</param>
    /// <param name="settings">Authored variation bounds.</param>
    /// <returns>A deterministic, independent per-life trait sample.</returns>
    public static PoliceDriverTraits CreateForLife(int sessionSeed, int spawnOrdinal, string stableVehicleId,
        int assignedLifeId, PoliceUnitVariationSettings settings) {
        PoliceUnitVariationSettings bounds = settings != null ? settings.Clone() : new PoliceUnitVariationSettings();
        uint state = Seed(sessionSeed, spawnOrdinal, stableVehicleId);
        float reaction = 1f + Signed(ref state) * bounds.reactionVariation;
        float prediction = 1f + Signed(ref state) * bounds.predictionVariation;
        float gap = 1f + Signed(ref state) * bounds.followGapVariation;
        float side = Signed(ref state) * bounds.sidePreferenceVariation;
        float risk = Signed(ref state) * bounds.riskBiasVariation;
        return new PoliceDriverTraits(assignedLifeId, reaction, prediction, gap, side, risk);
    }

    /// <summary>Checks that this sample is still owned by the supplied life identity.</summary>
    /// <param name="currentLifeId">Current physical life identity.</param>
    /// <returns>True when the detached sample belongs to that life.</returns>
    public bool IsCurrent(int currentLifeId) => lifeId > 0 && lifeId == currentLifeId;

    /// <summary>Returns a neutral detached sample for reset and pool-reuse boundaries.</summary>
    /// <returns>Neutral traits with no life identity.</returns>
    public static PoliceDriverTraits Reset() => new PoliceDriverTraits(0, 1f, 1f, 1f, 0f, 0f);

    static uint Seed(int sessionSeed, int spawnOrdinal, string stableVehicleId) {
        unchecked {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)sessionSeed) * 16777619u;
            hash = (hash ^ (uint)spawnOrdinal) * 16777619u;
            string value = stableVehicleId ?? string.Empty;
            for (int index = 0; index < value.Length; index++) hash = (hash ^ value[index]) * 16777619u;
            return hash == 0u ? 1u : hash;
        }
    }

    static float Signed(ref uint state) {
        unchecked { state = state * 1664525u + 1013904223u; }
        return (state / (float)uint.MaxValue) * 2f - 1f;
    }
}
