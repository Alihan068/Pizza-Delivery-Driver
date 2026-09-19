using UnityEngine;

/// <summary>
/// Resolves which side of one physics contact is at fault, using contracts §6's exact measurable
/// algorithm — never which collider's callback happened to fire first. Ordering the two sides by
/// lifeId (not by argument/callback order) makes the result identical no matter which side Unity
/// calls back first. Static world contacts are handled through <see cref="ContactParticipant.isStaticWorld"/>
/// contributing zero. An equal or ambiguous head-on contact resolves to Environment — nobody is
/// blamed just because a callback fired first.
/// </summary>
public static class DamageAttributionResolver {
    /// <summary>
    /// Resolves fault for one contact.
    /// </summary>
    /// <param name="participantX">Either side, in whatever order the caller happens to have them (e.g. collision callback order — irrelevant here).</param>
    /// <param name="participantY">The other side.</param>
    /// <param name="contactPoint">World-space contact point.</param>
    /// <param name="normalFromXToY">Contact normal as reported, pointing from participantX toward participantY.</param>
    /// <param name="rules">Authored thresholds.</param>
    /// <returns>The at-fault side's InstigatorKind, or Environment when neither clearly qualifies.</returns>
    public static InstigatorKind ResolveFault(ContactParticipant participantX, ContactParticipant participantY,
        Vector2 contactPoint, Vector2 normalFromXToY, DamageAttributionRules rules) {
        return ResolveFault(participantX, participantY, contactPoint, normalFromXToY, rules, out _);
    }

    /// <summary>Resolves both actor kind and life ID; same-role collisions retain the responsible actor's identity.</summary>
    public static InstigatorKind ResolveFault(ContactParticipant participantX, ContactParticipant participantY,
        Vector2 contactPoint, Vector2 normalFromXToY, DamageAttributionRules rules, out int instigatorLifeId) {
        instigatorLifeId = 0;
        if (rules == null) return InstigatorKind.Environment;

        // Stable A/B ordering by lifeId — independent of callback/argument order.
        bool xIsA = participantX.lifeId <= participantY.lifeId;
        ContactParticipant a = xIsA ? participantX : participantY;
        ContactParticipant b = xIsA ? participantY : participantX;
        Vector2 n = xIsA ? normalFromXToY : -normalFromXToY; // re-oriented from A to B

        Vector2 vAPoint = a.linearVelocity + AngularCross(a.angularVelocityRadPerSec, contactPoint - a.centerOfMass);
        Vector2 vBPoint = b.linearVelocity + AngularCross(b.angularVelocityRadPerSec, contactPoint - b.centerOfMass);

        float contributionA = a.isStaticWorld ? 0f : Mathf.Max(0f, Vector2.Dot(vAPoint, n));
        float contributionB = b.isStaticWorld ? 0f : Mathf.Max(0f, Vector2.Dot(vBPoint, -n));

        bool aQualifies = contributionA >= rules.minimumApproachThreshold && contributionA - contributionB >= rules.attributionDeltaTolerance;
        bool bQualifies = contributionB >= rules.minimumApproachThreshold && contributionB - contributionA >= rules.attributionDeltaTolerance;

        if (aQualifies && !bQualifies && !a.isStaticWorld) { instigatorLifeId = a.lifeId; return a.kind; }
        if (bQualifies && !aQualifies && !b.isStaticWorld) { instigatorLifeId = b.lifeId; return b.kind; }
        return InstigatorKind.Environment;
    }

    /// <summary>2D angular-velocity-cross-radius: omega x r = omega * (-r.y, r.x).</summary>
    static Vector2 AngularCross(float angularVelocityRadPerSec, Vector2 r) {
        return angularVelocityRadPerSec * new Vector2(-r.y, r.x);
    }
}
