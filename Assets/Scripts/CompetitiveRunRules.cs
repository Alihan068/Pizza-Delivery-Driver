/// <summary>
/// Pure competitive eligibility rules shared by runtime settlement and future service adapters.
/// </summary>
/// <remarks>
/// The rules accept facts instead of inspecting Unity objects. This keeps the anti-cheat boundary
/// testable and lets a future server or Steam adapter apply the same decision without depending on
/// scene objects or local save files.
/// </remarks>
public static class CompetitiveRunRules {

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
}
