/// <summary>
/// Accumulates active session time from explicit <see cref="Tick"/> calls only, never from
/// Time.time/Time.deltaTime directly. Every session timer (respawn deficits, wreck lifetimes, and
/// later heat integrals and arrest holds) is meant to read <see cref="ElapsedActiveSeconds"/> instead
/// of wall-clock time, so pause/loading/end correctly never advance any of them.
/// </summary>
public sealed class SessionClock {
    bool paused;
    bool ended;

    public float ElapsedActiveSeconds { get; private set; }
    public bool IsPaused => paused;
    public bool IsEnded => ended;

    /// <summary>Sets the paused flag. Ignored once the session has ended — pause can never reopen a finished clock.</summary>
    public void SetPaused(bool value) {
        if (!ended) paused = value;
    }

    /// <summary>Advances the clock unless paused or ended. Negative/NaN/Infinity deltas are ignored rather than corrupting the total.</summary>
    public void Tick(float deltaTime) {
        if (paused || ended) return;
        if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f) return;
        ElapsedActiveSeconds += deltaTime;
    }

    /// <summary>Stops the clock permanently. No later Tick/SetPaused call can move it again.</summary>
    public void End() {
        ended = true;
    }
}
