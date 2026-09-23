/// <summary>
/// Resolves one destruction event into an authored heat payload. Police victims always increase
/// heat because the arcade director is omniscient and persistent; civilian victims increase heat
/// only when the preserved root instigator is Player. Other victim roles are ignored. This method
/// is stateless and does not spawn, mutate population state, or infer attribution from callback order.
/// </summary>
public static class HeatPayloadResolver {
    /// <summary>
    /// Resolves heat using the victim-role whitelist and explicit killing context. A null rules
    /// object, unsupported victim role, or non-player civilian cause produces no payload safely.
    /// </summary>
    /// <param name="destroyedEvent">Terminal destruction event for one victim life.</param>
    /// <param name="rules">Authored heat values for supported victim roles.</param>
    /// <param name="heatAmount">Resolved positive heat, or zero when the event is ignored.</param>
    /// <returns>True when the event should add heat.</returns>
    public static bool TryResolveHeat(VehicleDestroyedEvent destroyedEvent, HeatAwardRules rules, out float heatAmount) {
        heatAmount = 0f;
        if (rules == null) return false;
        if (destroyedEvent.victimRole == VehicleRole.Civilian &&
            destroyedEvent.killingContext.instigatorKind != InstigatorKind.Player) return false;
        if (destroyedEvent.victimRole != VehicleRole.Civilian && destroyedEvent.victimRole != VehicleRole.Police) return false;

        heatAmount = rules.Resolve(destroyedEvent.victimRole);
        return heatAmount > 0f;
    }
}
