using UnityEngine;

/// <summary>
/// Identifies the dedicated trigger volume that accepts pizza deliveries for a customer.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public sealed class CustomerDeliveryZone : MonoBehaviour {
    Customer owner;

    /// <summary>Customer that owns this delivery trigger.</summary>
    public Customer Owner => owner;

    void Awake() {
        owner = GetComponentInParent<Customer>();
    }
}
