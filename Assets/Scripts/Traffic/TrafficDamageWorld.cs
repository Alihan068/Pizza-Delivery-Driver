using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-local damage composition: physical receivers, queued chain reactions and lifecycle gates; never chooses traffic routes.</summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class TrafficDamageWorld : MonoBehaviour {
    readonly Dictionary<int, VehicleDamageReceiver> byLife = new Dictionary<int, VehicleDamageReceiver>();
    readonly Dictionary<Rigidbody2D, VehicleDamageReceiver> byBody = new Dictionary<Rigidbody2D, VehicleDamageReceiver>();
    readonly List<VehicleDamageReceiver> receivers = new List<VehicleDamageReceiver>();
    readonly List<BlastApplication> applications = new List<BlastApplication>();
    readonly HashSet<int> destroyedLives = new HashSet<int>();
    VehicleExplosionService explosions;
    TrafficSessionHost host;
    int nextEventId;
    bool closed;

    /// <summary>Single authority for readiness, identity allocation and permanent session end.</summary>
    public TrafficSessionCoordinator Coordinator { get; private set; }
    /// <summary>Detached per-session tuning copied from the authored asset.</summary>
    public TrafficDamageSettings Settings { get; private set; }
    /// <summary>Elapsed active seconds; pause and ended sessions do not advance this clock.</summary>
    public float SessionTime { get; private set; }
    /// <summary>Whether receivers may accept or publish gameplay damage.</summary>
    public bool IsActive => !closed && Coordinator != null && Coordinator.IsActive;
    /// <summary>Number of blasts awaiting bounded processing.</summary>
    public int PendingBlasts => explosions != null ? explosions.PendingCount : 0;
    /// <summary>Emitted once per destroyed life for population diagnostics or later heat subscribers.</summary>
    public event Action<VehicleDestroyedEvent, float> VehicleDestroyed;
    /// <summary>Optional visual request; gameplay processing never depends on whether an effect can be shown.</summary>
    public event Action<Vector2> ExplosionVisualRequested;

    /// <summary>Initializes a single world; another session requires a new component, never reopening the old one.</summary>
    public void Configure(TrafficSessionCoordinator coordinator, TrafficDamageSettings settings) {
        if (closed || Coordinator != null || coordinator == null || settings == null) return;
        if (!settings.IsValid()) {
            coordinator.Context.Integrity.MarkInvalid("Invalid damage budgets or tuning.");
            closed = true;
            Debug.LogError("Traffic damage settings contain invalid budgets or tuning.", this);
            return;
        }
        Coordinator = coordinator; Settings = Instantiate(settings);
        var query = new PhysicsBlastVictimQuery(gameObject.scene.GetPhysicsScene2D(), byBody, Settings.receiverLayers);
        explosions = new VehicleExplosionService(query, Settings.roles, Settings.blastsPerTick,
            Settings.initialQueryCapacity, Settings.maxQueryCapacity, Settings.queriesPerTick, Settings.maxSessionBlasts);
        Coordinator.Ended += EndSession;
        host = GetComponent<TrafficSessionHost>();
        if (host != null) {
            host.PlayerReady += HandlePlayerReady;
            if (host.Player != null && Coordinator.PlayerIdentity.HasValue) HandlePlayerReady(host.Player);
        }
    }

    void HandlePlayerReady(GameObject player) {
        if (!Coordinator.PlayerIdentity.HasValue) return;
        var data = GameManager.Instance != null ? GameManager.Instance.currentVehicle : null;
        RegisterPlayer(player, Coordinator.PlayerIdentity.Value, data != null ? data.explosionResistance : 0f);
    }

    /// <summary>Connects the spawned player's existing Driver to blast queries with the shared player identity.</summary>
    public void RegisterPlayer(GameObject player, VehicleIdentity identity, float explosionResistance) {
        if (closed || player == null || player.scene != gameObject.scene) return;
        var driver = player.GetComponent<Driver>();
        if (driver == null) return;
        var receiver = player.GetComponent<VehicleDamageReceiver>();
        if (receiver == null) receiver = player.AddComponent<VehicleDamageReceiver>();
        receiver.BindPlayer(this, driver, identity, explosionResistance);
    }

    /// <summary>Registers one bound body; conflicting life IDs are configuration errors and invalidate this session.</summary>
    public void Register(VehicleDamageReceiver receiver) {
        if (closed || receiver == null) return;
        if (byLife.TryGetValue(receiver.Identity.lifeId, out var existing) && existing != receiver) {
            Coordinator.Context.Integrity.MarkInvalid("Duplicate vehicle life ID.");
            throw new InvalidOperationException("Two vehicle bodies cannot share one life ID.");
        }
        if (byLife.ContainsKey(receiver.Identity.lifeId)) return;
        byLife.Add(receiver.Identity.lifeId, receiver);
        byBody[receiver.GetComponent<Rigidbody2D>()] = receiver;
        receivers.Add(receiver);
    }

    /// <summary>Removes all lookup entries for a released or destroyed physical body.</summary>
    public void Unregister(VehicleDamageReceiver receiver) {
        if (receiver == null) return;
        if (byLife.TryGetValue(receiver.Identity.lifeId, out var current) && current == receiver) byLife.Remove(receiver.Identity.lifeId);
        var body = receiver.GetComponent<Rigidbody2D>();
        if (body != null) byBody.Remove(body);
        receivers.Remove(receiver);
    }

    void FixedUpdate() { BeginPhysicsStep(); }
    void Update() { Tick(Time.deltaTime); }

    /// <summary>Samples every receiver after driving commands and before the physics solver; also usable by isolated tests.</summary>
    public void BeginPhysicsStep() {
        if (!IsActive) return;
        for (int i = 0; i < receivers.Count; i++) receivers[i].CaptureBeforePhysics();
    }

    /// <summary>Advances queued damage, recovery and wreck cleanup once for a positive active delta.</summary>
    public void Tick(float deltaTime) {
        if (!IsActive || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
        SessionTime += deltaTime;
        applications.Clear();
        explosions.ProcessTick(applications);
        if (explosions.LastProcessStatus == VehicleExplosionService.ProcessingStatus.VictimCapacityReached ||
            explosions.LastProcessStatus == VehicleExplosionService.ProcessingStatus.InvalidQueryResult) {
            if (Coordinator.Context.Integrity.IsValid) {
                Coordinator.Context.Integrity.MarkInvalid("Explosion query could not complete within authored capacity.");
                Debug.LogError("Explosion query is incomplete. Increase the authored capacity before enabling this map.", this);
            }
        }
        for (int i = 0; i < applications.Count && IsActive; i++) {
            var application = applications[i];
            if (byLife.TryGetValue(application.victimLifeId, out var receiver)) receiver.ApplyBlast(application);
        }
        for (int i = receivers.Count - 1; i >= 0 && IsActive; i--) receivers[i].Tick();
    }

    /// <summary>Resolves one contact's fault from pre-step samples and applies its scalar damage to the NPC.</summary>
    public void ApplyCollision(VehicleDamageReceiver target, ContactParticipant other, Vector2 point, Vector2 normal, float impactSpeed) {
        if (!IsActive || target == null || !target.CanTakeDamage) return;
        InstigatorKind kind = DamageAttributionResolver.ResolveFault(target.Sample, other, point, normal, Settings.attribution, out int instigator);
        string eventId = NewEventId();
        target.ApplyCollision(impactSpeed, new DamageContext(eventId, other.lifeId, kind, instigator, DamageKind.Collision, eventId));
    }

    /// <summary>Publishes death once, preserving chain origin and queuing a new Explosion event independently of VFX.</summary>
    public void PublishDeath(VehicleDamageReceiver victim, VehicleDestroyedEvent death, TrafficDamageProfile tuning) {
        if (!IsActive || !destroyedLives.Add(death.victimLifeId)) return;
        if (tuning.blastRadius > 0f && tuning.blastDamage > 0f) {
            string blastId = NewEventId();
            var context = DamageContextFactory.CreateChainedContext(death.killingContext, blastId, death.victimLifeId);
            try {
                explosions.EnqueueBlast(new PendingBlast(blastId, victim.Position, tuning.blastDamage, tuning.blastRadius, context));
            } catch (InvalidOperationException exception) {
                Coordinator.Context.Integrity.MarkInvalid(exception.Message);
                Debug.LogError("Explosion session budget exhausted; increase maxSessionBlasts. " + exception.Message, this);
            }
        }
        HeatPayloadResolver.TryResolveHeat(death, Settings.heat, out float heat);
        VehicleDestroyed?.Invoke(death, heat);
        if (IsActive) ExplosionVisualRequested?.Invoke(victim.Position);
    }

    string NewEventId() => (++nextEventId).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Permanently closes this world and discards queued or already-resolved applications before settlement.</summary>
    public void EndSession() {
        if (closed) return;
        closed = true; explosions?.SetSessionActive(false); applications.Clear();
        for (int i = 0; i < receivers.Count; i++) receivers[i].Suspend();
        if (Coordinator != null) Coordinator.Ended -= EndSession;
        if (host != null) host.PlayerReady -= HandlePlayerReady;
    }

    void OnDestroy() {
        EndSession();
        if (Settings != null) {
            if (Application.isPlaying) Destroy(Settings); else DestroyImmediate(Settings);
        }
    }
}
