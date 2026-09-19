using System;

/// <summary>
/// Pure, Unity-lifecycle-independent brain behind one session's start/stop bridge. Initializes
/// exactly once, in whichever order map-ready and player-ready notifications arrive, and never
/// re-opens once the session has ended. A thin MonoBehaviour wires real scene events
/// (<see cref="PlayerSpawner.PlayerSpawned"/>, map/navigation validation) into this class; this
/// class itself takes no scene or Unity API dependency so it can be exercised by EditMode tests.
/// </summary>
public sealed class TrafficSessionCoordinator {
    /// <summary>Single identity authority to inject into every player, civilian and police pool in this session.</summary>
    public VehicleIdentityRegistry IdentityRegistry { get; }

    bool mapReady;
    bool playerReady;
    bool initialized;
    bool ended;

    public TrafficSessionContext Context { get; }
    public VehicleIdentity? PlayerIdentity { get; private set; }
    public bool IsInitialized => initialized;

    /// <summary>Raised exactly once, when both map and player have reported ready and the session becomes active.</summary>
    public event Action Initialized;

    /// <summary>Raised exactly once, when the session ends. Listeners (population/pool/scheduler gates) use this to cancel pending work and detach their own subscriptions — never do that reactively on every frame.</summary>
    public event Action Ended;

    /// <summary>True only between successful initialization and session end — the single "session clock active" flag every acquire/damage/spawn entry point must gate on.</summary>
    public bool IsActive => initialized && !ended && Context.Phase == TrafficSessionPhase.Active;

    /// <summary>Creates a bridge with an injected registry, or a fresh shared registry exposed through IdentityRegistry.</summary>
    public TrafficSessionCoordinator(TrafficSessionContext context, VehicleIdentityRegistry identityRegistry = null) {
        IdentityRegistry = identityRegistry ?? new VehicleIdentityRegistry();
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Reports that the map/navigation side is ready. Safe to call before or after player-ready, and safe to call more than once.</summary>
    public void NotifyMapReady() {
        if (ended) return;
        mapReady = true;
        TryInitialize();
    }

    /// <summary>Reports that the player vehicle exists and assigns it its session-scoped identity. Ignored if already reported or the session has ended.</summary>
    public void NotifyPlayerReady() {
        if (playerReady || ended) return;
        playerReady = true;
        PlayerIdentity = IdentityRegistry.Assign(VehicleRole.Player);
        if (!initialized) Context.SetPhase(TrafficSessionPhase.PlayerReady);
        TryInitialize();
    }

    /// <summary>Closes the session bridge exactly once. No later notification can reopen or (re)initialize it, and a repeated call never re-fires Ended.</summary>
    public void NotifySessionEnded() {
        if (ended) return;
        ended = true;
        Context.SetPhase(TrafficSessionPhase.Ended);
        Ended?.Invoke();
    }

    void TryInitialize() {
        if (initialized || ended || Context.Phase == TrafficSessionPhase.Ended) return;
        if (!mapReady || !playerReady) return;
        initialized = true;
        Context.SetPhase(TrafficSessionPhase.Active);
        Initialized?.Invoke();
    }
}
