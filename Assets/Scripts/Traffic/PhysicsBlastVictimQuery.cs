using System.Collections.Generic;
using UnityEngine;

/// <summary>Queries the owning physics scene and maps solid colliders to registered, living session receivers.</summary>
public sealed class PhysicsBlastVictimQuery : IBlastVictimQuery {
    readonly PhysicsScene2D scene;
    readonly Dictionary<Rigidbody2D, VehicleDamageReceiver> receivers;
    readonly ContactFilter2D filter;
    Collider2D[] colliders = System.Array.Empty<Collider2D>();

    /// <summary>Creates a query scoped to one physics scene and explicit receiver registry.</summary>
    public PhysicsBlastVictimQuery(PhysicsScene2D scene, Dictionary<Rigidbody2D, VehicleDamageReceiver> receivers, LayerMask layers) {
        this.scene = scene; this.receivers = receivers;
        filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = layers };
    }

    /// <summary>Returns capacity on possible saturation so the service retries instead of dropping unseen candidates.</summary>
    public int FindVictimsInRadius(Vector2 origin, float radius, BlastVictim[] buffer) {
        if (buffer == null || buffer.Length == 0) return 0;
        if (colliders.Length != buffer.Length) colliders = new Collider2D[buffer.Length];
        int count = scene.OverlapCircle(origin, radius, filter, colliders);
        if (count == colliders.Length) return buffer.Length;
        int written = 0;
        for (int i = 0; i < count; i++) {
            var collider = colliders[i];
            if (collider == null || collider.attachedRigidbody == null ||
                !receivers.TryGetValue(collider.attachedRigidbody, out var receiver) || !receiver.CanTakeDamage) continue;
            buffer[written++] = new BlastVictim(receiver.Identity.lifeId, receiver.Identity.role, receiver.Position, receiver.ExplosionResistance);
        }
        return written;
    }
}
