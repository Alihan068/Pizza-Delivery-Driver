using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional bounded one-shot presentation pool. Exhausted or missing effects never gate gameplay explosions.</summary>
public sealed class TrafficExplosionVisualPool : MonoBehaviour {
    [SerializeField] TrafficDamageWorld world;
    [SerializeField] ParticleSystem effectPrefab;
    [SerializeField, Min(0)] int capacity = 16;
    readonly List<ParticleSystem> effects = new List<ParticleSystem>();
    bool subscribed;

    void Start() { Initialize(world, effectPrefab, capacity); }

    /// <summary>Prewarms authored effects once and subscribes only to visual requests.</summary>
    public void Initialize(TrafficDamageWorld owner, ParticleSystem prefab, int limit) {
        if (subscribed || owner == null) return;
        world = owner;
        if (prefab != null) {
            for (int i = 0; i < Mathf.Max(0, limit); i++) {
                var effect = Instantiate(prefab, transform);
                effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                effects.Add(effect);
            }
        }
        world.ExplosionVisualRequested += Show;
        if (world.Coordinator != null) world.Coordinator.Ended += StopEffects;
        subscribed = true;
    }

    void Show(Vector2 position) {
        for (int i = 0; i < effects.Count; i++) {
            var effect = effects[i];
            if (effect == null || effect.IsAlive(true)) continue;
            effect.transform.position = position;
            effect.Play(true);
            return;
        }
    }

    void StopEffects() {
        foreach (var effect in effects)
            if (effect != null) effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void OnDestroy() {
        if (subscribed && world != null) world.ExplosionVisualRequested -= Show;
        if (world != null && world.Coordinator != null) world.Coordinator.Ended -= StopEffects;
    }
}
