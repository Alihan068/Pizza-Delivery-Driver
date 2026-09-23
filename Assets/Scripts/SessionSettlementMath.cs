using UnityEngine;

/// <summary>Pure settlement calculations shared by the session manager and EditMode tests.</summary>
/// <remarks>
/// Keeping these calculations free of scene, save-file and singleton state makes the four session
/// endings testable without touching the player's active profile. <see cref="GameManager"/> remains
/// the owner of the transaction and persistence side effects.
/// </remarks>
public static class SessionSettlementMath {

    /// <summary>Calculates the unbanked earnings that survive a session ending.</summary>
    /// <param name="sessionEarnings">Unbanked earnings accumulated during the session.</param>
    /// <param name="reason">Ending reason that determines the retention rule.</param>
    /// <param name="deathEarningsKeep">Fraction retained after a wreck.</param>
    /// <returns>A non-negative amount to add to the bank before repairs.</returns>
    public static int CalculateKeptEarnings(int sessionEarnings, EndReason reason, float deathEarningsKeep) {
        return CalculateKeptEarnings(sessionEarnings, reason, deathEarningsKeep, 0f);
    }

    /// <summary>Calculates retained earnings using separate wreck and arrest policies.</summary>
    /// <param name="sessionEarnings">Unbanked earnings accumulated during the session.</param>
    /// <param name="reason">Ending reason that determines the retention rule.</param>
    /// <param name="deathEarningsKeep">Authored fraction retained after a wreck.</param>
    /// <param name="arrestEarningsKeep">Authored fraction retained after an arrest.</param>
    /// <returns>A non-negative amount to add to the bank before repairs.</returns>
    public static int CalculateKeptEarnings(int sessionEarnings, EndReason reason, float deathEarningsKeep, float arrestEarningsKeep) {
        int safeEarnings = Mathf.Max(0, sessionEarnings);
        switch (reason) {
            case EndReason.Wrecked:
                return Mathf.FloorToInt(safeEarnings * Mathf.Clamp01(deathEarningsKeep));
            case EndReason.Arrested:
                return Mathf.FloorToInt(safeEarnings * Mathf.Clamp01(arrestEarningsKeep));
            case EndReason.Abandoned:
            case EndReason.Interrupted:
                return 0;
            default:
                return safeEarnings;
        }
    }

    /// <summary>Calculates the linear repair bill before the bank safety clamp.</summary>
    /// <param name="currentHealth">Health remaining when the session ended.</param>
    /// <param name="maxHealth">Maximum health of the active vehicle.</param>
    /// <param name="died">Whether the full health bar must be billed.</param>
    /// <param name="vehicleValue">Price plus vehicle-specific upgrade investment.</param>
    /// <param name="repairCostPerHP">Charge per lost health point.</param>
    /// <param name="repairValueRate">Vehicle value charge scaled by damage ratio.</param>
    /// <returns>The repair cost before affordability protection.</returns>
    public static int CalculateRepairCost(float currentHealth, float maxHealth, bool died, int vehicleValue, float repairCostPerHP, float repairValueRate) {
        if (maxHealth <= 0f) return 0;
        float hpLost = died ? maxHealth : Mathf.Clamp(maxHealth - currentHealth, 0f, maxHealth);
        float damageRatio = hpLost / maxHealth;
        return Mathf.RoundToInt(hpLost * repairCostPerHP + vehicleValue * repairValueRate * damageRatio);
    }

    /// <summary>Limits the repair charge so a bad session cannot consume the protected bank reserve.</summary>
    /// <param name="repairBeforeClamp">Repair bill calculated from damage and vehicle value.</param>
    /// <param name="keptEarnings">Earnings surviving the ending reason.</param>
    /// <param name="bankBefore">Bank balance before settlement.</param>
    /// <param name="bankSafetyRate">Fraction of the pre-session bank protected from repairs.</param>
    /// <returns>The affordable repair amount.</returns>
    public static int ClampRepairCost(int repairBeforeClamp, int keptEarnings, int bankBefore, float bankSafetyRate) {
        int maxCharge = Mathf.Max(0, keptEarnings) + Mathf.FloorToInt(bankBefore * Mathf.Max(0f, bankSafetyRate));
        return Mathf.Clamp(repairBeforeClamp, 0, maxCharge);
    }

    /// <summary>Applies kept earnings and repairs while enforcing the non-negative bank floor.</summary>
    /// <param name="bankBefore">Bank balance before settlement.</param>
    /// <param name="keptEarnings">Earnings surviving the ending reason.</param>
    /// <param name="repairCost">Repair amount charged after the safety clamp.</param>
    /// <returns>The settled bank balance, never below zero.</returns>
    public static int CalculateBankAfter(int bankBefore, int keptEarnings, int repairCost) {
        return Mathf.Max(0, bankBefore + keptEarnings - repairCost);
    }
}
