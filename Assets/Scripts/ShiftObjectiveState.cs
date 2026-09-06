using UnityEngine;

/// <summary>Runtime progress for one optional shift objective.</summary>
[System.Serializable]
public class ShiftObjectiveState {
    /// <summary>The objective's authored type.</summary>
    public ShiftObjectiveType type;
    /// <summary>Localization key for the objective title.</summary>
    public string displayNameKey;
    /// <summary>Localization key for the objective description.</summary>
    public string descriptionKey;
    /// <summary>Progress required for completion. Zero means no violations are allowed.</summary>
    public int target;
    /// <summary>Optional secondary parameter such as a minimum large-order size.</summary>
    public int parameter;
    /// <summary>Currency paid when this objective is completed.</summary>
    public int reward;
    /// <summary>Current progress for a positive target.</summary>
    public int progress;
    /// <summary>Number of violations recorded for a zero-target objective.</summary>
    public int violations;
    /// <summary>True after the objective reward has been granted.</summary>
    public bool isCompleted;
    /// <summary>True when the objective can no longer be completed this shift.</summary>
    public bool isFailed;
    /// <summary>True when the objective reward has already been granted.</summary>
    public bool IsComplete => isCompleted;
    /// <summary>True when the objective has been invalidated by a violation.</summary>
    public bool IsFailed => isFailed;

    /// <summary>Creates a runtime objective from authored tuning and calculated values.</summary>
    /// <param name="tuning">The authored objective entry.</param>
    /// <param name="targetValue">Calculated progress target.</param>
    /// <param name="parameterValue">Calculated secondary parameter.</param>
    /// <param name="rewardValue">Calculated currency reward.</param>
    public ShiftObjectiveState(ShiftObjectiveTuning tuning, int targetValue, int parameterValue, int rewardValue) {
        type = tuning.type;
        displayNameKey = tuning.displayNameKey;
        descriptionKey = tuning.descriptionKey;
        target = Mathf.Max(0, targetValue);
        parameter = Mathf.Max(0, parameterValue);
        reward = Mathf.Max(0, rewardValue);
    }

    /// <summary>Adds progress and reports whether the positive target was reached.</summary>
    /// <param name="amount">Progress to add.</param>
    /// <returns>True when this call reaches the target.</returns>
    public bool AddProgress(int amount) {
        if (isCompleted || isFailed || target <= 0) return false;
        progress = Mathf.Clamp(progress + Mathf.Max(0, amount), 0, target);
        return progress >= target;
    }

    /// <summary>Marks a zero-violation objective as failed.</summary>
    public void RegisterViolation() {
        if (isCompleted || isFailed || target != 0) return;
        violations++;
        isFailed = true;
    }

    /// <summary>Marks this objective complete before its reward is paid.</summary>
    public void MarkCompleted() {
        if (!isFailed) isCompleted = true;
    }
}
