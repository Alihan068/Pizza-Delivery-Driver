using NUnit.Framework;
using UnityEngine;

/// <summary>Pure coverage for the player collision damage dead zone and fade-in.</summary>
public sealed class CollisionDamageTests {
    const float DeadZone = 1.5f, FullSpeed = 3f, Base = 3f, Factor = 0.85f, Exponent = 2f;

    static float Damage(float speed) =>
        VehicleDrivingMath.CollisionDamage(speed, DeadZone, FullSpeed, Base, Factor, Exponent);

    static float Original(float speed) => Base + Factor * Mathf.Pow(speed, Exponent);

    /// <summary>Touches and scrapes at or below the dead zone deal nothing.</summary>
    [Test]
    public void LightContacts_DealNoDamage() {
        Assert.AreEqual(0f, Damage(0f));
        Assert.AreEqual(0f, Damage(0.3f));
        Assert.AreEqual(0f, Damage(1.5f));
        Assert.AreEqual(0f, Damage(float.NaN));
    }

    /// <summary>Damage fades in continuously above the dead zone, with no jump at the threshold.</summary>
    [Test]
    public void DamageFadesInWithoutAJump() {
        Assert.Less(Damage(1.51f), 0.1f, "just above the dead zone the damage is near zero");
        Assert.AreEqual(0.5f * Original(2.25f), Damage(2.25f), 0.001f, "half-way through the fade");
        Assert.Greater(Damage(2.5f), Damage(2f), "rises with speed");
    }

    /// <summary>Real crashes hurt exactly as they did before the dead zone existed.</summary>
    [Test]
    public void RealCrashes_KeepTheOriginalDamage() {
        Assert.AreEqual(Original(3f), Damage(3f), 0.001f);
        Assert.AreEqual(Original(6f), Damage(6f), 0.001f);
        Assert.AreEqual(Original(12f), Damage(12f), 0.001f);
    }
}
