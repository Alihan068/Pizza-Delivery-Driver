/// <summary>
/// Decides whether one VehicleDestroyedEvent awards heat, per contracts §6's truth table: only a
/// kill whose killingContext.instigatorKind is Player ever awards heat — a Police-caused kill (its
/// own mistake or a deliberate takedown), a civilian's own accident, or an Environment-attributed
/// contact never do, regardless of any earlier, non-lethal contact the victim may have had with the
/// player. Never spawns anything and never reaches into population/pool state — a caller reports
/// only the resulting population deficit to S03's TrafficRespawnScheduler, never a new instance here.
/// </summary>
public static class HeatPayloadResolver {
    public static bool TryResolveHeat(VehicleDestroyedEvent destroyedEvent, HeatAwardRules rules, out float heatAmount) {
        heatAmount = 0f;
        if (rules == null) return false;
        if (destroyedEvent.killingContext.instigatorKind != InstigatorKind.Player) return false;

        heatAmount = rules.Resolve(destroyedEvent.victimRole);
        return heatAmount > 0f;
    }
}
