/// <summary>
/// Pure competitive eligibility rules shared by runtime settlement and future service adapters.
/// </summary>
/// <remarks>
/// The rules accept facts instead of inspecting Unity objects. This keeps the anti-cheat boundary
/// testable and lets a future server or Steam adapter apply the same decision without depending on
/// scene objects or local save files.
/// </remarks>
public static class CompetitiveRunRules {

    /// <summary>
    /// Evaluates detached identity and creator-installed manifest evidence. A null provider is an
    /// explicit missing-manifest state; local labels or self-generated hashes never become trusted.
    /// </summary>
    public static CompetitiveEligibilityStatus Evaluate(SessionContentEvidence evidence,
        EndReason reason, int standardDurationMinutes, ITrustedContentManifestProvider manifestProvider) {
        if (evidence == null || !evidence.HasCompleteIdentity) return CompetitiveEligibilityStatus.ContentIntegrityFailure;
        if (evidence.isFreeplay) return CompetitiveEligibilityStatus.Freeplay;
        if (reason != EndReason.TimeUp && reason != EndReason.Extracted) return CompetitiveEligibilityStatus.Unfinished;
        if (!evidence.builtInMap) return CompetitiveEligibilityStatus.ExternalMap;
        if (!evidence.builtInVehicle) return CompetitiveEligibilityStatus.ExternalVehicle;
        if (evidence.customVehicleTuning) return CompetitiveEligibilityStatus.CustomVehicleTuning;
        if (evidence.shiftDurationMinutes != standardDurationMinutes) return CompetitiveEligibilityStatus.NonStandardDuration;
        if (manifestProvider == null || string.IsNullOrEmpty(evidence.trustedManifestId) ||
            string.IsNullOrEmpty(evidence.actualManifestHash))
            return CompetitiveEligibilityStatus.MissingTrustedManifest;
        if (!manifestProvider.TryGetExpectedHash(evidence.trustedManifestId, out string expectedHash) ||
            string.IsNullOrEmpty(expectedHash)) return CompetitiveEligibilityStatus.MissingTrustedManifest;
        if (!string.Equals(expectedHash, evidence.actualManifestHash, System.StringComparison.Ordinal))
            return CompetitiveEligibilityStatus.ContentIntegrityFailure;
        return evidence.trustedRouteProvenance && evidence.trustedNpcProvenance && evidence.trustedModifierProvenance
            ? CompetitiveEligibilityStatus.Eligible
            : CompetitiveEligibilityStatus.ContentIntegrityFailure;
    }

    /// <summary>Evaluates whether a settled run qualifies for the current standard board.</summary>
    /// <param name="isFreeplay">Whether the run used an endless session.</param>
    /// <param name="builtInMap">Whether the map came from the shipped content provider.</param>
    /// <param name="builtInVehicle">Whether the vehicle came from the shipped content provider.</param>
    /// <param name="hasCustomVehicleTuning">Whether temporary custom performance tuning was used.</param>
    /// <param name="durationMinutes">Duration selected for the run.</param>
    /// <param name="standardDurationMinutes">Duration accepted by the current board.</param>
    /// <param name="completed">Whether the run ended through a valid completed-session reason.</param>
    /// <returns>The first rule that disqualifies the run, or <see cref="CompetitiveEligibilityStatus.Eligible"/>.</returns>
    public static CompetitiveEligibilityStatus Evaluate(bool isFreeplay, bool builtInMap,
        bool builtInVehicle, bool hasCustomVehicleTuning, int durationMinutes,
        int standardDurationMinutes, bool completed) {
        if (isFreeplay) return CompetitiveEligibilityStatus.Freeplay;
        if (!completed) return CompetitiveEligibilityStatus.Unfinished;
        if (!builtInMap) return CompetitiveEligibilityStatus.ExternalMap;
        if (!builtInVehicle) return CompetitiveEligibilityStatus.ExternalVehicle;
        if (hasCustomVehicleTuning) return CompetitiveEligibilityStatus.CustomVehicleTuning;
        if (durationMinutes != standardDurationMinutes) return CompetitiveEligibilityStatus.NonStandardDuration;
        return CompetitiveEligibilityStatus.Eligible;
    }

    /// <summary>Evaluates eligibility using the authoritative session ending reason.</summary>
    /// <param name="isFreeplay">Whether the run used an endless session.</param>
    /// <param name="builtInMap">Whether the map came from the shipped content provider.</param>
    /// <param name="builtInVehicle">Whether the vehicle came from the shipped content provider.</param>
    /// <param name="hasCustomVehicleTuning">Whether temporary custom performance tuning was used.</param>
    /// <param name="durationMinutes">Duration selected for the run.</param>
    /// <param name="standardDurationMinutes">Duration accepted by the current board.</param>
    /// <param name="reason">Terminal reason produced by the single session settlement.</param>
    /// <returns>A status that rejects every non-completion ending, including arrest.</returns>
    public static CompetitiveEligibilityStatus Evaluate(bool isFreeplay, bool builtInMap,
        bool builtInVehicle, bool hasCustomVehicleTuning, int durationMinutes,
        int standardDurationMinutes, EndReason reason) {
        return Evaluate(isFreeplay, builtInMap, builtInVehicle, hasCustomVehicleTuning,
            durationMinutes, standardDurationMinutes, reason == EndReason.TimeUp || reason == EndReason.Extracted);
    }
}
