/// <summary>Final result of the single S09 death/arrest race resolution.</summary>
public enum PoliceArrestRaceResult {
    /// <summary>No terminal request is currently eligible.</summary>
    None,
    /// <summary>Player health reached zero; death always wins over a same-frame arrest request.</summary>
    Wrecked,
    /// <summary>Five-second arrest request is valid while the player remains alive.</summary>
    Arrested
}

/// <summary>Pure terminal race resolver used by the session coordinator's LateUpdate finalize step.</summary>
public static class PoliceArrestRace {
    /// <summary>
    /// Resolves one terminal frame after collision and queued blast damage have been applied.
    /// Already-ended sessions reject both requests.
    /// </summary>
    /// <param name="sessionActive">Whether the coordinator is still active.</param>
    /// <param name="playerAlive">Current player HP state after this frame's damage flush.</param>
    /// <param name="arrestRequested">Whether the contact tracker emitted its one request.</param>
    /// <returns>One terminal result, with Wrecked taking precedence over Arrested.</returns>
    public static PoliceArrestRaceResult Resolve(bool sessionActive, bool playerAlive, bool arrestRequested) {
        if (!sessionActive) return PoliceArrestRaceResult.None;
        if (!playerAlive) return PoliceArrestRaceResult.Wrecked;
        return arrestRequested ? PoliceArrestRaceResult.Arrested : PoliceArrestRaceResult.None;
    }

    /// <summary>Finalizes a pending tracker request using the current post-damage state.</summary>
    public static PoliceArrestRaceResult Resolve(PoliceContactTracker tracker,
        System.Collections.Generic.IReadOnlyCollection<int> livePoliceContacts, UnityEngine.Vector2 playerPosition,
        UnityEngine.Vector2 playerVelocity, bool sessionActive, bool playerAlive) {
        if (!sessionActive) {
            tracker?.CancelArrestRequest();
            return PoliceArrestRaceResult.None;
        }
        if (!playerAlive) {
            tracker?.CancelArrestRequest();
            return PoliceArrestRaceResult.Wrecked;
        }
        if (tracker == null || !tracker.HasPendingArrestRequest) return PoliceArrestRaceResult.None;
        return tracker.TryFinalizeArrestRequest(livePoliceContacts, playerPosition, playerVelocity, true, true)
            ? PoliceArrestRaceResult.Arrested
            : PoliceArrestRaceResult.None;
    }
}
