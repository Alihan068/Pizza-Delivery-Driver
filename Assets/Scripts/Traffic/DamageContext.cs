/// <summary>
/// Carries a damage event's origin: a stable eventId, the victim's lifeId, who/what caused it, what
/// kind of damage it was, and the rootIncidentId a chain (e.g. an explosion cascading into further
/// explosions) shares. Never inferred from callback order or a name/tag.
/// </summary>
public readonly struct DamageContext {
    public readonly string eventId;
    public readonly int sourceLifeId;
    public readonly InstigatorKind instigatorKind;
    public readonly int instigatorLifeId;
    public readonly DamageKind damageKind;
    public readonly string rootIncidentId;

    public DamageContext(string eventId, int sourceLifeId, InstigatorKind instigatorKind, int instigatorLifeId,
        DamageKind damageKind, string rootIncidentId) {
        this.eventId = eventId;
        this.sourceLifeId = sourceLifeId;
        this.instigatorKind = instigatorKind;
        this.instigatorLifeId = instigatorLifeId;
        this.damageKind = damageKind;
        this.rootIncidentId = rootIncidentId;
    }
}
