using System;
using System.Collections.Generic;

/// <summary>Detached physical evidence for one authored NPC profile.</summary>
public sealed class SessionNpcProfileEvidence {
    /// <summary>Stable NPC profile identity.</summary>
    public readonly string profileId;
    /// <summary>Detached authored mass.</summary>
    public readonly float baseMass;
    /// <summary>Detached collision resistance.</summary>
    public readonly float collisionArmor;
    /// <summary>Detached explosion resistance.</summary>
    public readonly float explosionResistance;
    /// <summary>Detached authored maximum health.</summary>
    public readonly float maxHealth;
    /// <summary>Stable visual catalog identity.</summary>
    public readonly string visualCatalogId;
    /// <summary>Canonical detached motor values.</summary>
    public readonly string motorFingerprint;
    /// <summary>Canonical detached behavior values.</summary>
    public readonly string behaviorFingerprint;
    /// <summary>Stable collision damage profile identity.</summary>
    public readonly string damageProfileId;
    /// <summary>Stable explosion damage profile identity.</summary>
    public readonly string explosionProfileId;

    /// <summary>Copies the authored identity and physical values into immutable evidence.</summary>
    public SessionNpcProfileEvidence(string profileId, float baseMass, float collisionArmor, float explosionResistance,
        float maxHealth = 0f, string visualCatalogId = null, string motorFingerprint = null,
        string behaviorFingerprint = null, string damageProfileId = null, string explosionProfileId = null) {
        this.profileId = profileId ?? string.Empty;
        this.baseMass = baseMass;
        this.collisionArmor = collisionArmor;
        this.explosionResistance = explosionResistance;
        this.maxHealth = maxHealth;
        this.visualCatalogId = visualCatalogId ?? string.Empty;
        this.motorFingerprint = motorFingerprint ?? string.Empty;
        this.behaviorFingerprint = behaviorFingerprint ?? string.Empty;
        this.damageProfileId = damageProfileId ?? string.Empty;
        this.explosionProfileId = explosionProfileId ?? string.Empty;
    }
}

/// <summary>
/// Immutable, detached content identity captured after scene validation. It contains no Unity
/// object or mutable asset reference and is the only content identity eligible for later services.
/// </summary>
public sealed class SessionContentEvidence {
    /// <summary>Shared version tag for the implemented traffic rules contract.</summary>
    public const string ImplementedTrafficRulesetVersion = "S10.4-Traffic-1";
    /// <summary>Frozen session identity.</summary>
    public readonly string sessionId;
    /// <summary>Frozen map identity.</summary>
    public readonly string mapId;
    /// <summary>Frozen vehicle identity.</summary>
    public readonly string vehicleId;
    /// <summary>Frozen difficulty identity.</summary>
    public readonly string difficultyId;
    /// <summary>Frozen selected duration.</summary>
    public readonly int shiftDurationMinutes;
    /// <summary>Frozen freeplay flag.</summary>
    public readonly bool isFreeplay;
    /// <summary>Whether ContentRegistry identified the shipped map.</summary>
    public readonly bool builtInMap;
    /// <summary>Whether ContentRegistry identified the shipped vehicle.</summary>
    public readonly bool builtInVehicle;
    /// <summary>Whether custom vehicle tuning was active.</summary>
    public readonly bool customVehicleTuning;
    /// <summary>Frozen civilian traffic capability.</summary>
    public readonly bool trafficEnabled;
    /// <summary>Frozen police capability.</summary>
    public readonly bool policeEnabled;
    /// <summary>Whether navigation validation completed.</summary>
    public readonly bool navigationValidated;
    /// <summary>Whether required runtime systems validated.</summary>
    public readonly bool requiredSystemsValid;
    /// <summary>Frozen navigation document identity.</summary>
    public readonly string navigationDocumentId;
    /// <summary>Frozen population profile identity.</summary>
    public readonly string populationProfileId;
    /// <summary>Frozen police director identity.</summary>
    public readonly string policeDirectorProfileId;
    /// <summary>Frozen ruleset identity.</summary>
    public readonly string rulesetVersion;
    /// <summary>Creator manifest identity.</summary>
    public readonly string trustedManifestId;
    /// <summary>Actual creator manifest hash, when supplied by a trusted producer.</summary>
    public readonly string actualManifestHash;
    /// <summary>Canonical navigation hash.</summary>
    public readonly string navigationCanonicalHash;
    /// <summary>Canonical resolved modifier values.</summary>
    public readonly string modifierCanonicalFingerprint;
    /// <summary>Canonical population values.</summary>
    public readonly string populationCanonicalFingerprint;
    /// <summary>Canonical police heat-tier values.</summary>
    public readonly string policeHeatTierFingerprint;
    /// <summary>Whether route provenance was trusted by the producer.</summary>
    public readonly bool trustedRouteProvenance;
    /// <summary>Whether NPC provenance was trusted by the producer.</summary>
    public readonly bool trustedNpcProvenance;
    /// <summary>Whether modifier provenance was trusted by the producer.</summary>
    public readonly bool trustedModifierProvenance;
    /// <summary>Frozen civilian population cap.</summary>
    public readonly int populationMaxCivilianMoving;
    /// <summary>Frozen total moving population cap.</summary>
    public readonly int populationMaxTotalMoving;
    /// <summary>Frozen wreck-slot cap.</summary>
    public readonly int populationMaxWreckSlots;
    /// <summary>Frozen total physics-object cap.</summary>
    public readonly int populationMaxTotalPhysicsObjects;
    /// <summary>Frozen player vehicle mass.</summary>
    public readonly float vehicleMass;
    /// <summary>Frozen player vehicle explosion resistance.</summary>
    public readonly float vehicleExplosionResistance;
    /// <summary>Frozen police starting heat.</summary>
    public readonly float baseHeatAtStart;
    /// <summary>Frozen police heat growth rate.</summary>
    public readonly float baseHeatPerActiveSecond;
    /// <summary>Frozen police maximum heat.</summary>
    public readonly float maximumHeat;
    /// <summary>Frozen earliest police activation time.</summary>
    public readonly float earliestPoliceTime;
    /// <summary>Detached resolved modifier values.</summary>
    public readonly FrozenModifierRules modifiers;
    /// <summary>Detached resolved modifier identities.</summary>
    public readonly IReadOnlyList<string> modifierIds;
    /// <summary>Detached civilian profile evidence.</summary>
    public readonly IReadOnlyList<SessionNpcProfileEvidence> civilianProfiles;
    /// <summary>Detached police profile evidence.</summary>
    public readonly IReadOnlyList<SessionNpcProfileEvidence> policeProfiles;

    /// <summary>Creates detached evidence and copies every supplied collection.</summary>
    public SessionContentEvidence(string sessionId, string mapId, string vehicleId, string difficultyId,
        int shiftDurationMinutes, bool isFreeplay, bool builtInMap, bool builtInVehicle,
        bool customVehicleTuning, bool trafficEnabled, bool policeEnabled, bool navigationValidated,
        bool requiredSystemsValid, string navigationDocumentId, string populationProfileId,
        string policeDirectorProfileId, string rulesetVersion, string trustedManifestId,
        string actualManifestHash, float vehicleMass, float vehicleExplosionResistance,
        float baseHeatAtStart, float baseHeatPerActiveSecond, float maximumHeat,
        float earliestPoliceTime, FrozenModifierRules modifiers, IReadOnlyList<string> modifierIds,
        IReadOnlyList<SessionNpcProfileEvidence> civilianProfiles,
        IReadOnlyList<SessionNpcProfileEvidence> policeProfiles, string navigationCanonicalHash = null,
        string modifierCanonicalFingerprint = null, string populationCanonicalFingerprint = null,
        string policeHeatTierFingerprint = null,
        bool trustedRouteProvenance = false, bool trustedNpcProvenance = false,
        bool trustedModifierProvenance = false, int populationMaxCivilianMoving = 0,
        int populationMaxTotalMoving = 0, int populationMaxWreckSlots = 0,
        int populationMaxTotalPhysicsObjects = 0) {
        this.sessionId = sessionId ?? string.Empty;
        this.mapId = mapId ?? string.Empty;
        this.vehicleId = vehicleId ?? string.Empty;
        this.difficultyId = difficultyId ?? string.Empty;
        this.shiftDurationMinutes = shiftDurationMinutes;
        this.isFreeplay = isFreeplay;
        this.builtInMap = builtInMap;
        this.builtInVehicle = builtInVehicle;
        this.customVehicleTuning = customVehicleTuning;
        this.trafficEnabled = trafficEnabled;
        this.policeEnabled = policeEnabled;
        this.navigationValidated = navigationValidated;
        this.requiredSystemsValid = requiredSystemsValid;
        this.navigationDocumentId = navigationDocumentId ?? string.Empty;
        this.populationProfileId = populationProfileId ?? string.Empty;
        this.policeDirectorProfileId = policeDirectorProfileId ?? string.Empty;
        this.rulesetVersion = rulesetVersion ?? string.Empty;
        this.trustedManifestId = trustedManifestId ?? string.Empty;
        this.actualManifestHash = actualManifestHash ?? string.Empty;
        this.navigationCanonicalHash = navigationCanonicalHash ?? string.Empty;
        this.modifierCanonicalFingerprint = modifierCanonicalFingerprint ?? string.Empty;
        this.populationCanonicalFingerprint = populationCanonicalFingerprint ?? string.Empty;
        this.policeHeatTierFingerprint = policeHeatTierFingerprint ?? string.Empty;
        this.trustedRouteProvenance = trustedRouteProvenance;
        this.trustedNpcProvenance = trustedNpcProvenance;
        this.trustedModifierProvenance = trustedModifierProvenance;
        this.populationMaxCivilianMoving = populationMaxCivilianMoving;
        this.populationMaxTotalMoving = populationMaxTotalMoving;
        this.populationMaxWreckSlots = populationMaxWreckSlots;
        this.populationMaxTotalPhysicsObjects = populationMaxTotalPhysicsObjects;
        this.vehicleMass = vehicleMass;
        this.vehicleExplosionResistance = vehicleExplosionResistance;
        this.baseHeatAtStart = baseHeatAtStart;
        this.baseHeatPerActiveSecond = baseHeatPerActiveSecond;
        this.maximumHeat = maximumHeat;
        this.earliestPoliceTime = earliestPoliceTime;
        this.modifiers = modifiers ?? new FrozenModifierRules(null);
        this.modifierIds = CopyStrings(modifierIds);
        this.civilianProfiles = CopyProfiles(civilianProfiles);
        this.policeProfiles = CopyProfiles(policeProfiles);
    }

    /// <summary>Creates the deliberately incomplete fallback used before an explicit binding exists.</summary>
    public static SessionContentEvidence CreateMinimal(SessionSetupDraft draft, FrozenModifierRules modifiers) {
        return new SessionContentEvidence(draft != null ? draft.sessionId : string.Empty,
            draft != null ? draft.mapId : string.Empty, draft != null ? draft.vehicleId : string.Empty,
            draft != null ? draft.difficultyId : string.Empty, draft != null ? draft.shiftDurationMinutes : 0,
            draft != null && draft.isFreeplay, false, false, false, false, false, false, false,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0f, 0f,
            0f, 0f, 0f, 0f, modifiers, modifiers != null ? modifiers.Ids : null, null, null);
    }

    /// <summary>Returns true only when the detached identity has all required competitive facts.</summary>
    public bool HasCompleteIdentity {
        get {
            return !string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(mapId) &&
                !string.IsNullOrEmpty(vehicleId) && !string.IsNullOrEmpty(difficultyId) &&
                shiftDurationMinutes > 0 && navigationValidated && requiredSystemsValid &&
                !string.IsNullOrEmpty(navigationDocumentId) && !string.IsNullOrEmpty(rulesetVersion) &&
                !string.IsNullOrEmpty(navigationCanonicalHash) && !string.IsNullOrEmpty(modifierCanonicalFingerprint) &&
                !string.IsNullOrEmpty(populationCanonicalFingerprint) &&
                IsFinitePositive(vehicleMass) && IsFiniteRange(vehicleExplosionResistance) &&
                ModifierIdsMatch() && ModifierValuesValid() &&
                (!trafficEnabled || (!string.IsNullOrEmpty(populationProfileId) && ProfilesValid(civilianProfiles, false))) &&
                (!policeEnabled || (!string.IsNullOrEmpty(policeDirectorProfileId) && ProfilesValid(policeProfiles, true) &&
                    !string.IsNullOrEmpty(policeHeatTierFingerprint)));
        }
    }

    /// <summary>Checks that detached identity still belongs to the captured draft and modifiers.</summary>
    public bool MatchesSessionIdentity(SessionSetupDraft draft, FrozenModifierRules resolvedModifiers) {
        return draft != null && ReferenceEquals(modifiers, resolvedModifiers) &&
            string.Equals(sessionId, draft.sessionId, StringComparison.Ordinal) &&
            string.Equals(mapId, draft.mapId, StringComparison.Ordinal) &&
            string.Equals(vehicleId, draft.vehicleId, StringComparison.Ordinal) &&
            string.Equals(difficultyId, draft.difficultyId, StringComparison.Ordinal) &&
            shiftDurationMinutes == draft.shiftDurationMinutes && isFreeplay == draft.isFreeplay &&
            ModifierIdsMatch();
    }

    bool ModifierIdsMatch() {
        if (modifiers == null || modifierIds.Count != modifiers.Ids.Count) return false;
        for (int i = 0; i < modifierIds.Count; i++)
            if (!string.Equals(modifierIds[i], modifiers.Ids[i], StringComparison.Ordinal)) return false;
        return true;
    }

    bool ModifierValuesValid() {
        if (modifiers == null || !IsFinitePositive(modifiers.ScoreMultiplier)) return false;
        foreach (VehicleStatId stat in Enum.GetValues(typeof(VehicleStatId))) {
            float delta = modifiers.GetStatDelta(stat);
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return false;
        }
        return true;
    }

    static bool ProfilesValid(IReadOnlyList<SessionNpcProfileEvidence> profiles, bool requireBehavior) {
        if (profiles == null || profiles.Count == 0) return false;
        for (int i = 0; i < profiles.Count; i++) {
            var profile = profiles[i];
            if (profile == null || string.IsNullOrEmpty(profile.profileId) ||
                !IsFinitePositive(profile.baseMass) || !IsFinitePositive(profile.maxHealth) ||
                !IsFiniteRange(profile.collisionArmor) || !IsFiniteRange(profile.explosionResistance) ||
                string.IsNullOrEmpty(profile.visualCatalogId) || string.IsNullOrEmpty(profile.motorFingerprint) ||
                string.IsNullOrEmpty(profile.damageProfileId) || string.IsNullOrEmpty(profile.explosionProfileId) ||
                (requireBehavior && string.IsNullOrEmpty(profile.behaviorFingerprint))) return false;
        }
        return true;
    }

    static IReadOnlyList<string> CopyStrings(IReadOnlyList<string> source) {
        if (source == null || source.Count == 0) return Array.Empty<string>();
        var copy = new string[source.Count];
        for (int i = 0; i < source.Count; i++) copy[i] = source[i] ?? string.Empty;
        return Array.AsReadOnly(copy);
    }

    static IReadOnlyList<SessionNpcProfileEvidence> CopyProfiles(IReadOnlyList<SessionNpcProfileEvidence> source) {
        if (source == null || source.Count == 0) return Array.Empty<SessionNpcProfileEvidence>();
        var copy = new SessionNpcProfileEvidence[source.Count];
        for (int i = 0; i < source.Count; i++) copy[i] = source[i];
        return Array.AsReadOnly(copy);
    }

    static bool IsFinitePositive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    static bool IsFiniteRange(float value) => value >= 0f && value <= 1f && !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>Creator-owned expected-hash evidence source. Runtime production has no default provider.</summary>
public interface ITrustedContentManifestProvider {
    /// <summary>Returns the expected hash installed by the trusted creator pipeline.</summary>
    bool TryGetExpectedHash(string manifestId, out string expectedHash);
}

/// <summary>Common immutable payload for future competitive score and achievement adapters.</summary>
public sealed class CompetitiveSubmissionRecord {
    /// <summary>Frozen content evidence used for the decision.</summary>
    public readonly SessionContentEvidence contentEvidence;
    /// <summary>Eligibility computed by the settlement factory.</summary>
    public readonly CompetitiveEligibilityStatus eligibility;
    /// <summary>Authoritative settlement reason.</summary>
    public readonly EndReason reason;
    /// <summary>Single settled final score.</summary>
    public readonly int finalScore;
    /// <summary>Integrity validity captured at settlement.</summary>
    public readonly bool sessionIntegrityValid;
    /// <summary>First captured integrity failure reason, when invalid.</summary>
    public readonly string sessionIntegrityReason;

    CompetitiveSubmissionRecord(SessionContentEvidence contentEvidence,
        CompetitiveEligibilityStatus eligibility, EndReason reason, int finalScore,
        bool sessionIntegrityValid, string sessionIntegrityReason) {
        this.contentEvidence = contentEvidence;
        this.eligibility = eligibility;
        this.reason = reason;
        this.finalScore = finalScore;
        this.sessionIntegrityValid = sessionIntegrityValid;
        this.sessionIntegrityReason = sessionIntegrityReason ?? string.Empty;
    }

    /// <summary>Creates the record from settlement facts and computes eligibility exactly once.</summary>
    public static CompetitiveSubmissionRecord CreateFromSettlement(SessionContentEvidence contentEvidence,
        EndReason reason, int finalScore, int standardDurationMinutes,
        ITrustedContentManifestProvider manifestProvider, bool sessionIntegrityValid = false,
        string sessionIntegrityReason = null) {
        CompetitiveEligibilityStatus eligibility = CompetitiveRunRules.Evaluate(contentEvidence, reason,
            standardDurationMinutes, manifestProvider);
        if (!sessionIntegrityValid) eligibility = CompetitiveEligibilityStatus.ContentIntegrityFailure;
        return new CompetitiveSubmissionRecord(contentEvidence, eligibility, reason, finalScore,
            sessionIntegrityValid, sessionIntegrityReason);
    }
}

/// <summary>Future sink contract for services that consume the same immutable score result.</summary>
public interface ICompetitiveResultSink {
    /// <summary>Attempts submission and returns false when the record is not acceptable.</summary>
    bool TrySubmit(CompetitiveSubmissionRecord record);
}

/// <summary>Bridges one settled result to a future score or achievement sink without recomputing it.</summary>
public sealed class CompetitiveResultSubmissionAdapter {
    readonly ICompetitiveResultSink sink;

    /// <summary>Creates an adapter for an explicitly supplied service sink.</summary>
    public CompetitiveResultSubmissionAdapter(ICompetitiveResultSink sink) {
        this.sink = sink;
    }

    /// <summary>Submits only the immutable record produced by settlement.</summary>
    public bool TrySubmit(SessionResult result) {
        CompetitiveSubmissionRecord record = result.competitiveSubmission;
        if (sink == null || record == null || !record.sessionIntegrityValid ||
            record.eligibility != CompetitiveEligibilityStatus.Eligible ||
            (record.reason != EndReason.TimeUp && record.reason != EndReason.Extracted) ||
            record.contentEvidence == null || !record.contentEvidence.HasCompleteIdentity ||
            !record.contentEvidence.trustedRouteProvenance || !record.contentEvidence.trustedNpcProvenance ||
            !record.contentEvidence.trustedModifierProvenance) return false;
        return sink.TrySubmit(record);
    }
}
