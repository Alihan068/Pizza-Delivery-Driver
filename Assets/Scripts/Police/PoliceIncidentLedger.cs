using System.Collections.Generic;

/// <summary>
/// Session-local deduplication ledger for vehicle destruction. It protects the heat system from
/// repeated delivery of one life's death event while preserving a fresh lifeId after pool reuse.
/// </summary>
public sealed class PoliceIncidentLedger {
    readonly HashSet<int> recordedLifeIds = new HashSet<int>();
    float incidentHeat;

    /// <summary>Total accepted incident heat in this session.</summary>
    public float IncidentHeat => incidentHeat;

    /// <summary>Number of unique vehicle lives whose destruction event has been consumed.</summary>
    public int RecordedIncidentCount => recordedLifeIds.Count;

    /// <summary>
    /// Records at most one destruction per victim life. Police deaths are always eligible for heat;
    /// civilian deaths follow the explicit root-instigator rule in the resolver. A life is consumed
    /// even when its authored event produces no heat, preventing later duplicate attribution.
    /// </summary>
    /// <param name="destroyedEvent">Published destruction event.</param>
    /// <param name="rules">Authored victim-role heat values.</param>
    /// <param name="awardedHeat">New heat added by this call.</param>
    /// <returns>True when this event added heat.</returns>
    public bool TryRecord(VehicleDestroyedEvent destroyedEvent, HeatAwardRules rules, out float awardedHeat) {
        awardedHeat = 0f;
        if (recordedLifeIds.Contains(destroyedEvent.victimLifeId)) return false;
        recordedLifeIds.Add(destroyedEvent.victimLifeId);
        if (!HeatPayloadResolver.TryResolveHeat(destroyedEvent, rules, out awardedHeat)) return false;
        incidentHeat += awardedHeat;
        return true;
    }

    /// <summary>Clears the ledger for a new session without retaining pool-life identities.</summary>
    public void Reset() {
        recordedLifeIds.Clear();
        incidentHeat = 0f;
    }
}
