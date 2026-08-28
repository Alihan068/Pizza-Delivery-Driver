using UnityEngine;

// Constructed once per customer activation. totalPizzas and waitTime are set at
// construction and never mutated afterward, so the old "waitTime creeps across
// respawns" bug is structurally impossible - only deliveredPizzas changes, and only
// through RegisterDelivery.
public class CustomerOrder {
    public readonly int totalPizzas;
    public readonly float waitTime;
    public int deliveredPizzas { get; private set; }

    public CustomerOrder(int totalPizzas, float waitTime) {
        this.totalPizzas = totalPizzas;
        this.waitTime = waitTime;
        deliveredPizzas = 0;
    }

    public int RemainingPizzas => totalPizzas - deliveredPizzas;
    public bool IsComplete => deliveredPizzas >= totalPizzas;

    public void RegisterDelivery(int count) {
        deliveredPizzas = Mathf.Min(totalPizzas, deliveredPizzas + count);
    }
}
