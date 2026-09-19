/// <summary>One resolved blast-to-victim damage application, ready for a caller to feed into NpcVehicleHealth.ApplyDamage. Carries no VFX/visual data at all — gameplay damage is fully decoupled from any visual-effect budget (S05.6).</summary>
public readonly struct BlastApplication {
    public readonly string blastId;
    public readonly int victimLifeId;
    public readonly VehicleRole victimRole;
    public readonly float damage;
    public readonly DamageContext context;

    public BlastApplication(string blastId, int victimLifeId, VehicleRole victimRole, float damage, DamageContext context) {
        this.blastId = blastId;
        this.victimLifeId = victimLifeId;
        this.victimRole = victimRole;
        this.damage = damage;
        this.context = context;
    }
}
