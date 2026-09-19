/// <summary>
/// Propagates one incident's root cause into the next damage event in a chain (e.g. a blast that
/// kills a vehicle whose own death triggers a further explosion). Only eventId/sourceLifeId are
/// fresh; instigatorKind, instigatorLifeId and rootIncidentId carry forward unchanged. The next
/// physical damage kind is explicit, defaulting to Explosion for the existing chain API.
/// </summary>
public static class DamageContextFactory {
    /// <summary>Creates a subsequent explosion event while preserving the original instigator and incident.</summary>
    /// <param name="originatingContext">Killing event whose root attribution must survive the chain.</param>
    /// <param name="newEventId">Fresh stable ID of the new explosion, not the original collision.</param>
    /// <param name="newSourceLifeId">Life of the vehicle emitting this explosion, not its victim.</param>
    /// <returns>An Explosion context with the new source/event and unchanged root attribution.</returns>
    public static DamageContext CreateChainedContext(DamageContext originatingContext, string newEventId, int newSourceLifeId) {
        return CreateChainedContext(originatingContext, newEventId, newSourceLifeId, DamageKind.Explosion);
    }

    /// <summary>Creates a chained event of an explicit physical kind without changing root attribution.</summary>
    /// <param name="originatingContext">Prior event supplying instigator and root incident fields.</param>
    /// <param name="newEventId">Stable ID belonging to the new event.</param>
    /// <param name="newSourceLifeId">Life producing the new event.</param>
    /// <param name="newDamageKind">Physical cause of this new event, independent of the root cause.</param>
    /// <returns>The new event with its supplied kind and preserved instigator/root fields.</returns>
    public static DamageContext CreateChainedContext(DamageContext originatingContext, string newEventId,
        int newSourceLifeId, DamageKind newDamageKind) {
        return new DamageContext(newEventId, newSourceLifeId, originatingContext.instigatorKind,
            originatingContext.instigatorLifeId, newDamageKind, originatingContext.rootIncidentId);
    }
}
