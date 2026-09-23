using UnityEngine;

/// <summary>
/// Owns every moving/wreck slot counter against an authored <see cref="PopulationBudgetData"/>.
/// A spawn request reserves its moving slot AND its eventual wreck token together, atomically,
/// before anything is created; success commits the reservation, failure/abort/timeout cancels it
/// in full. A death never asks for new wreck capacity — it converts the same NPC's already-reserved
/// token into an occupied wreck, so even every live vehicle dying on the same tick can never push
/// <c>occupiedWrecks + reservedFutureWrecks</c> past <see cref="PopulationBudgetData.maxWreckSlots"/>.
/// This class only counts; it makes no AI or spawn-placement decision.
/// </summary>
public sealed class VehiclePopulationService {
    readonly PopulationBudgetData budget;

    int reservedCivilian, activeCivilian;
    int reservedPolice, activePolice;
    int occupiedWrecks, reservedFutureWrecks;

    public VehiclePopulationService(PopulationBudgetData budget) {
        this.budget = budget;
    }

    public int ActiveCivilianCount => activeCivilian;
    public int ActivePoliceCount => activePolice;
    public int PendingCivilianCount => reservedCivilian;
    public int PendingPoliceCount => reservedPolice;
    public int OccupiedWreckCount => occupiedWrecks;
    public int ReservedFutureWreckCount => reservedFutureWrecks;

    /// <summary>
    /// Reserves one moving slot for the given role plus its future-wreck token, atomically. Rejects
    /// (nothing reserved) if the role cap, the global moving cap, or the wreck-token budget has no room.
    /// </summary>
    public bool TryReserveSpawn(VehicleRole role) {
        if (budget == null || (role != VehicleRole.Civilian && role != VehicleRole.Police)) return false;

        int civilianTotal = reservedCivilian + activeCivilian;
        int policeTotal = reservedPolice + activePolice;
        if (role == VehicleRole.Civilian && civilianTotal >= budget.maxCivilianMoving) return false;
        if (role == VehicleRole.Police && policeTotal >= budget.maxPoliceMoving) return false;
        if (civilianTotal + policeTotal >= budget.maxTotalMoving) return false;
        int physicsCapacity = Mathf.Max(0, Mathf.Min(budget.maxWreckSlots, budget.maxTotalPhysicsObjects));
        if (occupiedWrecks + reservedFutureWrecks >= physicsCapacity) return false;

        if (role == VehicleRole.Civilian) reservedCivilian++;
        else reservedPolice++;
        reservedFutureWrecks++;
        return true;
    }

    /// <summary>Commits a reservation to Active. The wreck token stays reserved unchanged; only the pending-vs-active split moves.</summary>
    public bool CommitSpawn(VehicleRole role) {
        if (role == VehicleRole.Civilian) {
            if (reservedCivilian <= 0) return false;
            reservedCivilian--;
            activeCivilian++;
            return true;
        }
        if (role == VehicleRole.Police) {
            if (reservedPolice <= 0) return false;
            reservedPolice--;
            activePolice++;
            return true;
        }
        return false;
    }

    /// <summary>Cancels a pending (not yet committed) reservation and its wreck token in full — used for a failed/aborted/timed-out spawn attempt.</summary>
    public bool CancelReservation(VehicleRole role) {
        if (role == VehicleRole.Civilian) {
            if (reservedCivilian <= 0) return false;
            reservedCivilian--;
        }
        else if (role == VehicleRole.Police) {
            if (reservedPolice <= 0) return false;
            reservedPolice--;
        }
        else return false;

        if (reservedFutureWrecks > 0) reservedFutureWrecks--;
        return true;
    }

    /// <summary>
    /// An active vehicle dies: its moving slot is released and its already-reserved future-wreck
    /// token becomes an occupied wreck. Requests zero new capacity.
    /// </summary>
    public bool MarkActiveVehicleWrecked(VehicleRole role) {
        if (role == VehicleRole.Civilian) {
            if (activeCivilian <= 0) return false;
            activeCivilian--;
        }
        else if (role == VehicleRole.Police) {
            if (activePolice <= 0) return false;
            activePolice--;
        }
        else return false;

        if (reservedFutureWrecks <= 0) return false; // would indicate a spawn that skipped TryReserveSpawn
        reservedFutureWrecks--;
        occupiedWrecks++;
        return true;
    }

    /// <summary>A wreck finishes its lifetime and is fully recycled: its slot is released entirely.</summary>
    public bool ReleaseWreck() {
        if (occupiedWrecks <= 0) return false;
        occupiedWrecks--;
        return true;
    }

    /// <summary>Clears every counter back to zero. Used at session end so a new session starts clean.</summary>
    public void ResetAll() {
        reservedCivilian = 0;
        activeCivilian = 0;
        reservedPolice = 0;
        activePolice = 0;
        occupiedWrecks = 0;
        reservedFutureWrecks = 0;
    }
}
