using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-side adapter for solid player-to-police contacts. Each physical player/police collider
/// pair retains the police life captured when observed; a life ID alone is never contact identity.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public sealed class PoliceContactBridge : MonoBehaviour {
    struct ContactPairKey : System.IEquatable<ContactPairKey> {
        public readonly int playerColliderId;
        public readonly int policeColliderId;

        public ContactPairKey(int playerColliderId, int policeColliderId) {
            this.playerColliderId = playerColliderId;
            this.policeColliderId = policeColliderId;
        }

        public bool Equals(ContactPairKey other) =>
            playerColliderId == other.playerColliderId && policeColliderId == other.policeColliderId;

        public override bool Equals(object obj) => obj is ContactPairKey other && Equals(other);

        public override int GetHashCode() => unchecked((playerColliderId * 397) ^ policeColliderId);
    }

    sealed class ContactPair {
        public Collider2D playerCollider;
        public Collider2D policeCollider;
        public VehicleDamageReceiver receiver;
        public int capturedPoliceLifeId;
    }

    readonly Dictionary<ContactPairKey, ContactPair> contactPairs = new Dictionary<ContactPairKey, ContactPair>();
    readonly HashSet<int> contactIds = new HashSet<int>();
    readonly HashSet<int> reportedContactIds = new HashSet<int>();
    readonly List<ContactPairKey> stalePairKeys = new List<ContactPairKey>();
    readonly List<int> contactIdScratch = new List<int>();
    PoliceDirector director;
    PoliceContactTracker tracker;

    /// <summary>Police life IDs in the currently validated solid contact set.</summary>
    public IReadOnlyCollection<int> ContactIds {
        get {
            RefreshLiveContacts();
            return contactIds;
        }
    }

    /// <summary>World-speed threshold supplied to the contact tracker.</summary>
    [Min(0f)] public float stationarySpeedThreshold = 0.05f;
    /// <summary>Anchor drift threshold supplied to the contact tracker.</summary>
    [Min(0f)] public float stationaryDistanceTolerance = 0.05f;
    /// <summary>Active hold required before the tracker emits an arrest request.</summary>
    [Min(0f)] public float arrestHoldSeconds = 5f;
    /// <summary>Read-only authored hold value used by the fixed-step tracker commit.</summary>
    public float ArrestHoldSeconds => arrestHoldSeconds;
    /// <summary>Read-only authored speed threshold used by the fixed-step tracker commit.</summary>
    public float StationarySpeedThreshold => stationarySpeedThreshold;
    /// <summary>Read-only authored drift threshold used by the fixed-step tracker commit.</summary>
    public float StationaryDistanceTolerance => stationaryDistanceTolerance;

    /// <summary>Injects the session-local tracker without requiring a director event owner.</summary>
    /// <param name="contactState">Tracker that receives distinct life enter and exit changes.</param>
    public void Configure(PoliceContactTracker contactState) {
        director = null;
        tracker = contactState;
        ClearContacts();
    }

    /// <summary>Injects the legacy director plus the session-local tracker.</summary>
    /// <param name="owner">Director that owns this player bridge.</param>
    /// <param name="contactState">Tracker that receives distinct life enter and exit changes.</param>
    public void Configure(PoliceDirector owner, PoliceContactTracker contactState) {
        director = owner;
        tracker = contactState;
        ClearContacts();
    }

    void OnCollisionEnter2D(Collision2D collision) => RefreshContact(collision);
    void OnCollisionStay2D(Collision2D collision) => RefreshContact(collision);

    void OnCollisionExit2D(Collision2D collision) {
        if (collision == null || collision.collider == null || collision.otherCollider == null) return;
        Collider2D playerCollider = IsOwnCollider(collision.otherCollider) ? collision.otherCollider : collision.collider;
        Collider2D policeCollider = IsOwnCollider(collision.otherCollider) ? collision.collider : collision.otherCollider;
        if (!IsOwnCollider(playerCollider)) return;
        if (!contactPairs.Remove(new ContactPairKey(playerCollider.GetInstanceID(), policeCollider.GetInstanceID()))) return;
        RefreshContactIdsAndNotify();
    }

    /// <summary>Removes every pair for a wrecked, relocated, or detached police life.</summary>
    /// <param name="policeLifeId">The exact life identity to remove.</param>
    public void RemovePoliceLife(int policeLifeId) {
        stalePairKeys.Clear();
        foreach (var pair in contactPairs) {
            if (pair.Value.capturedPoliceLifeId == policeLifeId) stalePairKeys.Add(pair.Key);
        }
        for (int i = 0; i < stalePairKeys.Count; i++) contactPairs.Remove(stalePairKeys[i]);
        RefreshContactIdsAndNotify();
    }

    /// <summary>Copies the authoritative currently live police life set into a caller-owned buffer.</summary>
    /// <param name="output">Buffer cleared and filled with distinct live life IDs.</param>
    /// <returns>The number of IDs written.</returns>
    public int CollectLivePoliceLifeIds(List<int> output) {
        if (output == null) return 0;
        RefreshLiveContacts();
        output.Clear();
        foreach (int lifeId in contactIds) output.Add(lifeId);
        return output.Count;
    }

    void RefreshContact(Collision2D collision) {
        if (collision == null || collision.collider == null || collision.otherCollider == null) return;
        Collider2D playerCollider = IsOwnCollider(collision.otherCollider) ? collision.otherCollider : collision.collider;
        Collider2D policeCollider = IsOwnCollider(collision.otherCollider) ? collision.collider : collision.otherCollider;
        if (!IsOwnCollider(playerCollider) || !IsSolidSimulated(playerCollider) ||
            !TryGetLivePolice(policeCollider, out var receiver)) return;
        var key = new ContactPairKey(playerCollider.GetInstanceID(), policeCollider.GetInstanceID());
        if (!contactPairs.TryGetValue(key, out var pair)) {
            pair = new ContactPair();
            contactPairs.Add(key, pair);
        }
        pair.playerCollider = playerCollider;
        pair.policeCollider = policeCollider;
        pair.receiver = receiver;
        pair.capturedPoliceLifeId = receiver.Identity.lifeId;
        RefreshContactIdsAndNotify();
    }

    void RefreshLiveContacts() {
        stalePairKeys.Clear();
        foreach (var pair in contactPairs) if (!IsValidPair(pair.Value)) stalePairKeys.Add(pair.Key);
        for (int i = 0; i < stalePairKeys.Count; i++) contactPairs.Remove(stalePairKeys[i]);
        contactIds.Clear();
        foreach (var pair in contactPairs) contactIds.Add(pair.Value.capturedPoliceLifeId);
    }

    void RefreshContactIdsAndNotify() {
        RefreshLiveContacts();
        contactIdScratch.Clear();
        foreach (int lifeId in reportedContactIds) if (!contactIds.Contains(lifeId)) contactIdScratch.Add(lifeId);
        for (int i = 0; i < contactIdScratch.Count; i++) {
            int lifeId = contactIdScratch[i];
            reportedContactIds.Remove(lifeId);
            QueueExit(lifeId);
        }
        contactIdScratch.Clear();
        foreach (int lifeId in contactIds) contactIdScratch.Add(lifeId);
        for (int i = 0; i < contactIdScratch.Count; i++) {
            int lifeId = contactIdScratch[i];
            if (reportedContactIds.Add(lifeId)) {
                QueueEnter(lifeId);
            }
        }
    }

    bool IsOwnCollider(Collider2D collider) => collider != null &&
        (collider.transform == transform || collider.transform.IsChildOf(transform));

    static bool TryGetLivePolice(Collider2D collider, out VehicleDamageReceiver receiver) {
        receiver = collider != null ? collider.GetComponentInParent<VehicleDamageReceiver>() : null;
        return IsSolidSimulated(collider) && receiver != null && receiver.isActiveAndEnabled && receiver.Identity.role == VehicleRole.Police &&
            !receiver.IsWreck && receiver.CanTakeDamage;
    }

    static bool IsValidPair(ContactPair pair) {
        return pair != null && pair.playerCollider != null && pair.policeCollider != null &&
            IsSolidSimulated(pair.playerCollider) && IsSolidSimulated(pair.policeCollider) &&
            pair.receiver != null && pair.receiver.isActiveAndEnabled &&
            pair.receiver.Identity.role == VehicleRole.Police &&
            pair.receiver.Identity.lifeId == pair.capturedPoliceLifeId &&
            !pair.receiver.IsWreck && pair.receiver.CanTakeDamage;
    }

    static bool IsSolidSimulated(Collider2D collider) {
        return collider != null && !collider.isTrigger && collider.enabled && collider.gameObject.activeInHierarchy &&
            (collider.attachedRigidbody == null || collider.attachedRigidbody.simulated);
    }

    void QueueEnter(int policeLifeId) {
        if (tracker != null) tracker.QueueEnter(policeLifeId);
        else director?.QueueContactEnter(policeLifeId);
    }

    void QueueExit(int policeLifeId) {
        if (tracker != null) tracker.QueueExit(policeLifeId);
        else director?.QueueContactExit(policeLifeId);
    }

    /// <summary>Clears all pair records and queues exits before session teardown or rebinding.</summary>
    public void ClearContacts() {
        foreach (int lifeId in reportedContactIds) {
            QueueExit(lifeId);
        }
        contactPairs.Clear();
        contactIds.Clear();
        reportedContactIds.Clear();
    }

    void OnDisable() => ClearContacts();
}
