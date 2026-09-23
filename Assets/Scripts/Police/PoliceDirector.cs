using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-local composition bridge for the pure police director core. It owns session subscription,
/// active-time ticking, identified materialization outcomes, and the player contact bridge. Contact
/// threshold requests are committed only after the prior completed physics step and finalized only
/// after the damage world's existing Update flush.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10000)]
public sealed class PoliceDirector : MonoBehaviour {
    [SerializeField] TrafficSessionHost sessionHost;
    [SerializeField] PoliceDirectorData directorData;
    [SerializeField] bool enablePolice = true;
    [SerializeField] TrafficDamageWorld sharedDamageWorld;

    readonly List<int> liveContactScratch = new List<int>();
    TrafficDamageWorld damageWorld;
    PoliceDirectorRuntime runtime;
    PoliceContactTracker contactTracker;
    PoliceContactBridge contactBridge;
    GameObject boundPlayer;
    bool hostSubscribed;
    bool damageSubscribed;
    bool started;
    bool arrestEmitted;
    bool hasPreviousPhysicsStep;
    float previousPhysicsDelta;

    /// <summary>Current pure director runtime after session activation.</summary>
    public PoliceDirectorRuntime Runtime => runtime;
    /// <summary>Current contact tracker, or null before activation.</summary>
    public PoliceContactTracker ContactTracker => contactTracker;
    /// <summary>The authored profile accepted by the scene composition binding.</summary>
    public PoliceDirectorData DirectorData => directorData;
    /// <summary>Raised when the director has a bounded request for a police composition layer.</summary>
    public event Action<PoliceDirectorRuntime.SpawnRequest> SpawnRequested;
    /// <summary>Raised once when a pending contact request survives post-damage finalization.</summary>
    public event Action ArrestRequested;

    /// <summary>
    /// Binds a validated host and profile before or after Start. Binding is same-scene only and
    /// cannot replace either dependency after a session runtime has been created.
    /// </summary>
    /// <param name="owner">The same-scene traffic session host.</param>
    /// <param name="validatedData">Profile already selected for this scene/session.</param>
    /// <param name="reason">Stable failure reason when binding is rejected.</param>
    /// <returns>True when the binding is accepted or already matches.</returns>
    public bool TryConfigure(TrafficSessionHost owner, PoliceDirectorData validatedData, out string reason) {
        reason = null;
        if (owner == null) return Fail("Police director host is missing.", out reason);
        if (owner.gameObject.scene != gameObject.scene) return Fail("Police director host must be in the same scene.", out reason);
        if (validatedData == null) return Fail("Police director profile is missing.", out reason);
        if (!validatedData.TryValidate(out reason)) return false;
        if (runtime != null && (sessionHost != owner || directorData != validatedData))
            return Fail("Police director dependencies cannot be replaced after activation.", out reason);
        if (sessionHost != null && sessionHost != owner)
            return Fail("Police director is already bound to another host.", out reason);
        if (directorData != null && directorData != validatedData)
            return Fail("Police director is already bound to another profile.", out reason);

        sessionHost = owner;
        directorData = validatedData;
        SubscribeHost();
        if (owner.IsActive) HandleActivated();
        if (owner.Player != null) HandlePlayerReady(owner.Player);
        return true;
    }

    void Start() {
        if (started) return;
        started = true;
        if (sessionHost == null) sessionHost = GetComponent<TrafficSessionHost>();
        if (sessionHost == null) return;
        if (directorData != null) {
            TryConfigure(sessionHost, directorData, out _);
            return;
        }
        SubscribeHost();
    }

    void Update() {
        if (runtime == null || damageWorld == null || !damageWorld.IsActive) return;
        runtime.Tick(damageWorld.SessionTime);
        if (runtime.PendingCount <= 0 || !runtime.TryDequeue(out var request)) return;
        if (SpawnRequested == null) {
            ReportSpawnRejected(request);
            return;
        }
        SpawnRequested.Invoke(request);
    }

    void FixedUpdate() {
        if (contactTracker == null || contactBridge == null || damageWorld == null || !damageWorld.IsActive) return;
        Rigidbody2D playerBody = contactBridge.GetComponent<Rigidbody2D>();
        if (playerBody == null) return;

        bool paused = Time.timeScale <= 0f;
        if (!hasPreviousPhysicsStep) {
            CapturePreviousPhysicsStep();
            return;
        }

        contactTracker.CommitStep(contactBridge.ContactIds, playerBody.position, playerBody.linearVelocity,
            previousPhysicsDelta, contactBridge.StationarySpeedThreshold, contactBridge.StationaryDistanceTolerance,
            contactBridge.ArrestHoldSeconds, paused);
        previousPhysicsDelta = Time.fixedDeltaTime;
    }

    void LateUpdate() {
        if (contactTracker == null || contactBridge == null || damageWorld == null || sessionHost == null ||
            !damageWorld.IsActive || !sessionHost.IsActive || Time.timeScale <= 0f ||
            !contactTracker.HasPendingArrestRequest) return;
        Rigidbody2D playerBody = contactBridge.GetComponent<Rigidbody2D>();
        if (playerBody == null) return;

        liveContactScratch.Clear();
        contactBridge.CollectLivePoliceLifeIds(liveContactScratch);
        Driver driver = contactBridge.GetComponent<Driver>();
        VehicleDamageReceiver playerReceiver = contactBridge.GetComponent<VehicleDamageReceiver>();
        bool playerAlive = driver != null ? !driver.IsDisabled && driver.currentHealth > 0f :
            playerReceiver != null && playerReceiver.CurrentHealth > 0f;
        PoliceArrestRaceResult result = PoliceArrestRace.Resolve(contactTracker, liveContactScratch,
            playerBody.position, playerBody.linearVelocity, sessionHost.IsActive && damageWorld.IsActive, playerAlive);
        if (result == PoliceArrestRaceResult.Arrested && !arrestEmitted) {
            arrestEmitted = true;
            ArrestRequested?.Invoke();
        }
    }

    /// <summary>Records one damage-world destruction event without changing the damage truth table.</summary>
    /// <param name="destroyedEvent">Published destruction event.</param>
    public void RecordDestroyedEvent(VehicleDestroyedEvent destroyedEvent) {
        if (runtime == null || damageWorld == null) return;
        runtime.RecordIncident(destroyedEvent, damageWorld.Settings != null ? damageWorld.Settings.heat : null);
    }

    void HandleVehicleDestroyed(VehicleDestroyedEvent destroyedEvent, float resolvedHeat) => RecordDestroyedEvent(destroyedEvent);

    /// <summary>Queues a police contact enter from the player bridge.</summary>
    /// <param name="policeLifeId">Live police life identity.</param>
    public void QueueContactEnter(int policeLifeId) => contactTracker?.QueueEnter(policeLifeId);

    /// <summary>Queues a police contact exit from the player bridge.</summary>
    /// <param name="policeLifeId">Police life identity leaving contact.</param>
    public void QueueContactExit(int policeLifeId) => contactTracker?.QueueExit(policeLifeId);

    /// <summary>Commits an identified materialization to a genuine positive police life ID.</summary>
    /// <param name="request">Request returned by the director queue.</param>
    /// <param name="genuineLifeId">Identity assigned by the shared population service.</param>
    /// <returns>True only when the identified in-flight reservation was committed.</returns>
    public bool ReportSpawnCommitted(PoliceDirectorRuntime.SpawnRequest request, int genuineLifeId) =>
        runtime != null && runtime.Commit(request, genuineLifeId);

    /// <summary>Rejects one identified materialization request and releases its reservation.</summary>
    /// <param name="request">Request returned by the director queue.</param>
    /// <returns>True only when the identified reservation was released.</returns>
    public bool ReportSpawnRejected(PoliceDirectorRuntime.SpawnRequest request) =>
        runtime != null && runtime.Reject(request);

    /// <summary>Destroys one identified police life and removes its player contact pairs.</summary>
    /// <param name="policeLifeId">Genuine committed police life identity.</param>
    /// <returns>True when the runtime released the committed life.</returns>
    public bool ReportPoliceDestroyed(int policeLifeId) {
        bool destroyed = runtime != null && runtime.Destroyed(policeLifeId);
        RemovePoliceContacts(policeLifeId);
        return destroyed;
    }

    /// <summary>Clears contact pairs for a live police relocation without changing runtime counts.</summary>
    /// <param name="policeLifeId">The unchanged life identity being relocated.</param>
    public void RemovePoliceContacts(int policeLifeId) => contactBridge?.RemovePoliceLife(policeLifeId);

    /// <summary>Legacy commit overload retained for callers not yet carrying a life identity.</summary>
    public void ReportSpawnCommitted(PoliceDirectorRuntime.SpawnRequest request) => runtime?.ReportSpawnCommitted(request);

    /// <summary>Legacy anonymous rejection overload retained for compatibility.</summary>
    public void ReportSpawnRejected() => runtime?.ReportSpawnRejected();

    /// <summary>Legacy request-based destruction overload retained for compatibility.</summary>
    public void ReportPoliceDestroyed(PoliceDirectorRuntime.SpawnRequest request) => runtime?.ReportPoliceDestroyed(request);

    void HandleActivated() {
        if (runtime != null || sessionHost == null || sessionHost.Coordinator == null || directorData == null) return;
        // The director may live on PoliceManager while the shared world remains on the host.
        damageWorld = sharedDamageWorld != null ? sharedDamageWorld : sessionHost.GetComponent<TrafficDamageWorld>();
        if (damageWorld == null || damageWorld.gameObject.scene != gameObject.scene) return;
        bool policeEnabled = enablePolice;
        var snapshot = sessionHost.Coordinator.Context.Snapshot;
        if (snapshot != null && snapshot.Modifiers != null && snapshot.Modifiers.DisablesPolice) policeEnabled = false;
        int seed = snapshot != null ? snapshot.seed : 0;
        runtime = new PoliceDirectorRuntime(directorData, policeEnabled, seed);
        contactTracker = new PoliceContactTracker();
        arrestEmitted = false;
        hasPreviousPhysicsStep = false;
        if (sessionHost.Player != null) HandlePlayerReady(sessionHost.Player);
        SubscribeDamageWorld();
    }

    void HandlePlayerReady(GameObject player) {
        if (player == null || contactTracker == null || player.scene != gameObject.scene) return;
        if (boundPlayer == player && contactBridge != null) return;
        boundPlayer = player;
        contactBridge = player.GetComponent<PoliceContactBridge>();
        if (contactBridge == null) contactBridge = player.AddComponent<PoliceContactBridge>();
        contactBridge.Configure(this, contactTracker);
        contactTracker.Begin(player.transform.position);
        hasPreviousPhysicsStep = false;
    }

    void CapturePreviousPhysicsStep() {
        previousPhysicsDelta = Time.fixedDeltaTime;
        hasPreviousPhysicsStep = true;
    }

    void SubscribeHost() {
        if (sessionHost == null || hostSubscribed) return;
        sessionHost.Activated += HandleActivated;
        sessionHost.Ended += HandleEnded;
        sessionHost.PlayerReady += HandlePlayerReady;
        hostSubscribed = true;
    }

    void SubscribeDamageWorld() {
        if (damageWorld == null || damageSubscribed) return;
        damageWorld.VehicleDestroyed += HandleVehicleDestroyed;
        damageSubscribed = true;
    }

    void HandleEnded() {
        ClearSessionState();
        UnsubscribeHost();
    }

    void ClearSessionState() {
        if (damageSubscribed && damageWorld != null) damageWorld.VehicleDestroyed -= HandleVehicleDestroyed;
        damageSubscribed = false;
        contactBridge?.ClearContacts();
        contactTracker?.End();
        runtime?.End();
        runtime = null;
        contactTracker = null;
        boundPlayer = null;
        arrestEmitted = false;
        hasPreviousPhysicsStep = false;
    }

    void UnsubscribeHost() {
        if (!hostSubscribed || sessionHost == null) return;
        sessionHost.Activated -= HandleActivated;
        sessionHost.Ended -= HandleEnded;
        sessionHost.PlayerReady -= HandlePlayerReady;
        hostSubscribed = false;
    }

    void OnDisable() {
        ClearSessionState();
        UnsubscribeHost();
    }

    void OnDestroy() {
        ClearSessionState();
        UnsubscribeHost();
    }

    static bool Fail(string message, out string reason) {
        reason = message;
        return false;
    }
}
