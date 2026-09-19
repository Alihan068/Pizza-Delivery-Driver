/// <summary>What physically caused the damage. Explosion damage never re-applies collision armor a second time (see contracts §5).</summary>
public enum DamageKind {
    Collision,
    Explosion
}
