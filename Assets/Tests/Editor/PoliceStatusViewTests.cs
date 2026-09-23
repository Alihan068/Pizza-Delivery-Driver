using NUnit.Framework;
using UnityEngine;

public sealed class PoliceStatusViewTests {
    [Test]
    public void InactiveRuntime_HidesPolicePresentation() {
        PoliceDirectorData data = CreateData(3);
        PoliceStatusView.PoliceStatusPresentationModel model = PoliceStatusView.BuildModel(
            data, false, 2, 4f, 1, 2f, 5f, true);

        Assert.That(model.visible, Is.False);
        Assert.That(model.pursuitActive, Is.False);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void PeacefulRuntime_HidesPolicePresentationEvenWithNoActivePursuit() {
        PoliceDirectorData data = CreateData(4);
        PoliceStatusView.PoliceStatusPresentationModel model = PoliceStatusView.BuildModel(
            data, false, 0, 7f, 2, 0f, 5f, false);

        Assert.That(model.visible, Is.False);
        Assert.That(model.captureVisible, Is.False);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void HeatStarCount_ComesFromAuthoredTierList() {
        PoliceDirectorData data = CreateData(7);
        PoliceStatusView.PoliceStatusPresentationModel model = PoliceStatusView.BuildModel(
            data, true, 0, 3f, 2, 0f, 5f, false);

        Assert.That(model.heatTierCount, Is.EqualTo(7));
        Assert.That(model.filledHeatTierCount, Is.EqualTo(3));
        Assert.That(model.pursuitActive, Is.False);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void CaptureProgress_ClampsToAuthoredThreshold() {
        PoliceDirectorData data = CreateData(3);
        PoliceStatusView.PoliceStatusPresentationModel model = PoliceStatusView.BuildModel(
            data, true, 1, 3f, 1, 8f, 5f, true);

        Assert.That(model.captureVisible, Is.True);
        Assert.That(model.captureProgress01, Is.EqualTo(1f));
        Object.DestroyImmediate(data);
    }

    [Test]
    public void Model_UsesExactQuantizedValuesForStableEquality() {
        PoliceDirectorData data = CreateData(3);
        PoliceStatusView.PoliceStatusPresentationModel first = PoliceStatusView.BuildModel(
            data, true, 1, 1.04f, 1, 1.234f, 5f, true);
        PoliceStatusView.PoliceStatusPresentationModel second = PoliceStatusView.BuildModel(
            data, true, 1, 1.049f, 1, 1.236f, 5f, true);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        Object.DestroyImmediate(data);
    }

    [Test]
    public void FinalScorePresentation_UsesFrozenResultValue() {
        SessionResult result = new SessionResult { rawScore = 100, scoreMultiplier = 0.15f, finalScore = 15 };

        Assert.That(SessionResultPanel.GetDisplayedFinalScore(result), Is.EqualTo(15));
    }

    [Test]
    public void DelayedRuntimeEnable_KeepsObserverGameObjectActive() {
        GameObject viewObject = new GameObject("PoliceStatusViewFixture");
        PoliceStatusView view = viewObject.AddComponent<PoliceStatusView>();

        view.SetSessionActive(false);
        Assert.That(viewObject.activeSelf, Is.True);
        view.SetSessionActive(true);
        Assert.That(viewObject.activeSelf, Is.True);

        Object.DestroyImmediate(viewObject);
    }

    static PoliceDirectorData CreateData(int tierCount) {
        PoliceDirectorData data = ScriptableObject.CreateInstance<PoliceDirectorData>();
        data.profileId = "test";
        data.maximumHeat = tierCount;
        data.heatTiers = new System.Collections.Generic.List<PoliceHeatTier>();
        for (int i = 0; i < tierCount; i++) {
            data.heatTiers.Add(new PoliceHeatTier {
                tierId = "tier_" + i,
                minimumHeat = i,
                targetCount = i + 1,
                compositions = new System.Collections.Generic.List<PoliceCompositionEntry> {
                    new PoliceCompositionEntry { vehicleProfileId = "vehicle", behaviorProfileId = "behavior", weight = 1f }
                }
            });
        }
        return data;
    }
}
