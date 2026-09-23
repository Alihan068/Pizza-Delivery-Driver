using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Pure and isolated coverage for the bounded straight-road clearance certificate.</summary>
public sealed class PoliceStraightRoadClearanceTests {
    /// <summary>Checks empty-role straight admission and global cumulative interior geometry.</summary>
    [Test]
    public void Evaluate_PlainNodesAndEmptyRolesAreUsable_AndInteriorArcIsGlobal() {
        using (var fixture = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, new Vector2(0f, 5f), new Vector2(5f, 5f) }, null, "e0");
            fixture.SetPolicePosition(new Vector2(2f, 5f));
            fixture.SetPoliceRotation(-90f);
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 7f, 10f));
            var helper = CreateHelper(fixture, graph);
            int work = 512;
            Vector2 reconstructedAim = fixture.PolicePosition + fixture.PoliceForward * 3f;
            Debug.Log("INTERIOR_ARC aimExcess=" + (reconstructedAim.x - 5f).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            var result = helper.Evaluate(cursor, graph.Version, fixture.PolicePosition,
                new Vector2(5f, 5f), 4f, 0f, 0.02f, ref work, out float throttle);
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, result);
            Assert.Greater(throttle, 0f);
            Assert.LessOrEqual(throttle, 1f);
        }
    }

    /// <summary>Checks explicit Police admission while unresolved junction references fail closed.</summary>
    [Test]
    public void Evaluate_ExplicitPoliceRoleAndUnresolvedJunctionAreDistinct() {
        using (var allowed = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 30f },
                new List<VehicleRole> { VehicleRole.Police }, "e0");
            allowed.SetPolicePosition(new Vector2(0f, 2f));
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 2f, 30f));
            var helper = CreateHelper(allowed, graph);
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, helper.Evaluate(cursor, graph.Version,
                allowed.PolicePosition, allowed.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }

        using (var unresolved = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 30f }, null, "e0",
                startJunction: "missing-junction");
            unresolved.SetPolicePosition(new Vector2(0f, 2f));
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 2f, 30f));
            var helper = CreateHelper(unresolved, graph);
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                unresolved.PolicePosition, unresolved.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }
    }

    /// <summary>Checks stale, misaligned, reversing, angular, and exhausted-work inputs.</summary>
    [Test]
    public void Evaluate_RejectsStaleWrongAnchorHeadingVelocityAndWork() {
        using (var fixture = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 40f }, null, "e0");
            fixture.SetPolicePosition(new Vector2(0f, 2f));
            fixture.SetPoliceRotation(0f);
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 2f, 40f));
            var helper = CreateHelper(fixture, graph);
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.NotApplicable, helper.Evaluate(cursor, graph.Version,
                fixture.PolicePosition, fixture.PolicePosition + Vector2.up * 4f, 4f, 0.01f, 0.02f, ref work, out _));

            work = 512;
            fixture.Binding.body.Body.linearVelocity = -Vector2.up;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                fixture.PolicePosition, fixture.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));

            work = 512;
            fixture.Binding.body.Body.linearVelocity = Vector2.zero;
            fixture.Binding.body.Body.angularVelocity = 1f;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                fixture.PolicePosition, fixture.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));

            work = 0;
            fixture.Binding.body.Body.angularVelocity = 0f;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                fixture.PolicePosition, fixture.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));

            work = 512;
            graph.Rebuild();
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version - 1,
                fixture.PolicePosition, fixture.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }
    }

    /// <summary>Checks width, static, lateral-grip, and dynamic sensor saturation refusals.</summary>
    [Test]
    public void Evaluate_RejectsWidthEndpointStaticAndLateralFailures() {
        using (var narrow = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 30f }, null, "e0", width: 1f);
            narrow.SetPolicePosition(new Vector2(0f, 2f));
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 2f, 30f));
            var helper = CreateHelper(narrow, graph);
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                narrow.PolicePosition, narrow.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }

        using (var blocked = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 30f }, null, "e0");
            blocked.SetPolicePosition(new Vector2(0f, 2f));
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 2f, 30f));
            var helper = CreateHelper(blocked, graph, new AlwaysClear(false));
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                blocked.PolicePosition, blocked.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }

        using (var lateral = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 30f }, null, "e0");
            lateral.Profile.motorSettings.lateralGrip = 0f;
            lateral.SetPolicePosition(new Vector2(0f, 2f));
            lateral.Binding.body.Body.linearVelocity = Vector2.right;
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 2f, 30f));
            var helper = CreateHelper(lateral, graph);
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                lateral.PolicePosition, lateral.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }

        using (var saturated = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 40f }, null, "e0");
            saturated.SetPolicePosition(new Vector2(0f, 2f));
            saturated.CreateBlocker(new Vector2(0f, 8f), false, Vector2.one);
            saturated.CreateBlocker(new Vector2(0f, 10f), false, Vector2.one);
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 2f, 40f));
            var helper = CreateHelper(saturated, graph, sensorCapacity: 1);
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                saturated.PolicePosition, saturated.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }
    }

    /// <summary>Checks the force-limited positive-root path produces a fractional throttle.</summary>
    [Test]
    public void Evaluate_ForceLimitedStepRecomputesFiniteFractionalThrottle() {
        using (var fixture = new PursuitFixture()) {
            fixture.ReplacePlayer(new Vector2(0f, 90f));
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 100f }, null, "e0");
            fixture.SetPolicePosition(new Vector2(0f, 2f));
            fixture.Binding.body.Body.mass = 10f;
            fixture.Profile.motorSettings.maxBrakeForce = 8f;
            fixture.Binding.body.Body.linearVelocity = Vector2.up * 2.8f;
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 2f, 100f));
            var helper = CreateHelper(fixture, graph);
            int work = 512;
            var result = helper.Evaluate(cursor, graph.Version, fixture.PolicePosition,
                fixture.PolicePosition + Vector2.up * 8f, 5f, 0f, 0.5f, ref work, out float throttle);
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, result);
            Assert.Greater(throttle, 0f);
            Assert.Less(throttle, 1f);
            Assert.Greater(RequiredRange(fixture, 1f, 0.5f), fixture.ControllerSettings.maxSweepDistance,
                "The fixture must independently demonstrate why full throttle is unsafe.");
            Assert.LessOrEqual(RequiredRange(fixture, throttle, 0.5f), fixture.ControllerSettings.maxSweepDistance);
            work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, helper.Evaluate(cursor, graph.Version, fixture.PolicePosition,
                fixture.PolicePosition + Vector2.up * 8f, 5f, 0f, 0.25f, ref work, out float smallerStepThrottle));
            Assert.Greater(smallerStepThrottle, throttle, "A new step must obtain its own finite stopping allowance.");
            Assert.LessOrEqual(RequiredRange(fixture, smallerStepThrottle, 0.25f), fixture.ControllerSettings.maxSweepDistance);
        }
    }

    /// <summary>Checks nonfinite speed, timestep, and motor-force inputs fail closed.</summary>
    [Test]
    public void Evaluate_RejectsNonfiniteAndOverflowInputsFailClosed() {
        using (var fixture = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 40f }, null, "e0");
            fixture.SetPolicePosition(new Vector2(0f, 2f));
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 2f, 40f));
            var helper = CreateHelper(fixture, graph);
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                fixture.PolicePosition, fixture.PolicePosition + Vector2.up * 4f, float.NaN, 0f, 0.02f, ref work, out _));
            work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                fixture.PolicePosition, fixture.PolicePosition + Vector2.up * 4f, 4f, 0f, float.PositiveInfinity, ref work, out _));
            work = 512;
            fixture.Profile.motorSettings.maxBrakeForce = float.PositiveInfinity;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                fixture.PolicePosition, fixture.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }
    }

    /// <summary>Checks the extracted discrete-step envelope consumes shared work and static proof.</summary>
    [Test]
    public void TryValidateStepEnvelope_UsesFiniteSharedWorkAndStaticProof() {
        using (var fixture = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 40f }, null, "e0");
            var helper = CreateHelper(fixture, graph);
            int work = 8;
            Assert.IsTrue(helper.TryValidateStepEnvelope(Vector2.zero, 0.02f, fixture.Profile.colliderSize + Vector2.one, ref work));
            work = 0;
            Assert.IsFalse(helper.TryValidateStepEnvelope(Vector2.zero, 0.02f, fixture.Profile.colliderSize + Vector2.one, ref work));
        }
    }

    /// <summary>Checks the allocation-free double hull enclosure at nonzero heading, offset, origin, and float rounding.</summary>
    [Test]
    public void Evaluate_DoubleHullEnclosesRotatedOffsetOriginAndRoundingBoundary() {
        using (var fixture = new PursuitFixture(0.03f)) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 100f }, null, "e0");
            Vector2 origin = new Vector2(16384.3f, -8192.7f);
            Vector2 localPosition = new Vector2(0f, 4f);
            fixture.SetPolicePosition(origin + localPosition);
            fixture.SetPoliceRotation(-3f);
            fixture.SetPoliceColliderOffset(new Vector2(0.17f, -0.11f));
            fixture.Profile.motorSettings.lateralGrip = 0.1f;
            Vector2 forward = fixture.PoliceForward;
            fixture.Binding.body.Body.linearVelocity = forward * 0.5f + new Vector2(forward.y, -forward.x) * 0.2f;
            fixture.ReplacePlayer(origin + new Vector2(0f, 90f));
            var capture = new RecordingClearance(true);
            var helper = CreateHelper(fixture, graph, capture, origin: origin);
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 4f, 100f));
            int work = 512;
            var result = helper.Evaluate(cursor, graph.Version, localPosition,
                localPosition + fixture.PoliceForward * 8f, 4f, 0f, fixture.StepDelta, ref work, out float throttle);
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, result);
            Assert.Greater(throttle, 0f);
            Assert.GreaterOrEqual(capture.Samples.Count, 1);
            HullBounds(fixture, origin, throttle, out double lowX, out double lowY, out double highX, out double highY);
            var sample = capture.Samples[0];
            Assert.AreEqual(0f, sample.heading);
            Assert.LessOrEqual((double)sample.center.x - sample.size.x * 0.5f, lowX);
            Assert.GreaterOrEqual((double)sample.center.x + sample.size.x * 0.5f, highX);
            Assert.LessOrEqual((double)sample.center.y - sample.size.y * 0.5f, lowY);
            Assert.GreaterOrEqual((double)sample.center.y + sample.size.y * 0.5f, highY);
            Vector2 submittedWorldCenter = sample.center + origin;
            Assert.LessOrEqual((double)submittedWorldCenter.x - sample.size.x * 0.5f, lowX + origin.x);
            Assert.GreaterOrEqual((double)submittedWorldCenter.x + sample.size.x * 0.5f, highX + origin.x);
            Assert.LessOrEqual((double)submittedWorldCenter.y - sample.size.y * 0.5f, lowY + origin.y);
            Assert.GreaterOrEqual((double)submittedWorldCenter.y + sample.size.y * 0.5f, highY + origin.y);
            Assert.IsFalse(float.IsNaN(sample.center.x) || float.IsInfinity(sample.center.x) ||
                float.IsNaN(sample.center.y) || float.IsInfinity(sample.center.y));
            Assert.IsFalse(float.IsNaN(sample.size.x) || float.IsInfinity(sample.size.x) ||
                float.IsNaN(sample.size.y) || float.IsInfinity(sample.size.y));
        }
    }

    /// <summary>Checks valid mass-one continuation, mass-three force limitation, and a finite stopping-root refusal.</summary>
    [Test]
    public void Evaluate_ForceMassAndLargeDtRootRemainFiniteAndBounded() {
        using (var light = new PursuitFixture()) {
            ConfigureLongStraight(light, 100f, new Vector2(0f, 2f), new Vector2(0f, 90f));
            var helper = CreateHelper(light, light.Binding.graph);
            var cursor = BindCursor(light.Binding.graph, new RoadPathQuery.PathSpan("e0", 2f, 100f));
            int work = 512;
            var result = helper.Evaluate(cursor, light.Binding.graph.Version, light.PolicePosition,
                light.PolicePosition + Vector2.up * 8f, 4f, 0f, 0.02f, ref work, out float lightThrottle);
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, result);
            Assert.Greater(lightThrottle, 0f);
            Assert.LessOrEqual(lightThrottle, 1f);
        }

        using (var heavy = new PursuitFixture()) {
            ConfigureLongStraight(heavy, 20f, new Vector2(0f, 2f), new Vector2(0f, 18f));
            heavy.Binding.body.Body.mass = 3f;
            heavy.Profile.motorSettings.maxBrakeForce = 1f;
            heavy.Binding.body.Body.linearVelocity = Vector2.up * 4f;
            var helper = CreateHelper(heavy, heavy.Binding.graph);
            var cursor = BindCursor(heavy.Binding.graph, new RoadPathQuery.PathSpan("e0", 2f, 20f));
            int work = 512;
            var result = helper.Evaluate(cursor, heavy.Binding.graph.Version, heavy.PolicePosition,
                heavy.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.5f, ref work, out float throttle);
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, result);
            Assert.AreEqual(0f, throttle);
        }
    }

    /// <summary>Checks zero-grip at rest and symmetric finite low-grip reserves without forbidding small valid drift.</summary>
    [Test]
    public void Evaluate_GripZeroAndLowGripAllowBoundedDriftBothDirections() {
        using (var zeroClear = new PursuitFixture()) {
            ConfigureLongStraight(zeroClear, 40f, new Vector2(0f, 2f), new Vector2(0f, 30f));
            zeroClear.Profile.motorSettings.lateralGrip = 0f;
            var helper = CreateHelper(zeroClear, zeroClear.Binding.graph);
            var cursor = BindCursor(zeroClear.Binding.graph, new RoadPathQuery.PathSpan("e0", 2f, 40f));
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, helper.Evaluate(cursor, zeroClear.Binding.graph.Version,
                zeroClear.PolicePosition, zeroClear.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }

        float previousWidth = 0f;
        foreach (float lateralVelocity in new[] { -0.2f, 0.2f }) {
            using (var lateral = new PursuitFixture()) {
                ConfigureLongStraight(lateral, 40f, new Vector2(0f, 2f), new Vector2(0f, 30f));
                lateral.Profile.motorSettings.lateralGrip = 0.25f;
                lateral.Binding.body.Body.linearVelocity = Vector2.up * 2f + Vector2.right * lateralVelocity;
                var capture = new RecordingClearance(true);
                var helper = CreateHelper(lateral, lateral.Binding.graph, capture);
                var cursor = BindCursor(lateral.Binding.graph, new RoadPathQuery.PathSpan("e0", 2f, 40f));
                int work = 512;
                Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, helper.Evaluate(cursor, lateral.Binding.graph.Version,
                    lateral.PolicePosition, lateral.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
                double reserve = Math.Abs((double)lateralVelocity) * (1d - 0.25f) * 0.02f / 0.25f;
                double paddedWidth = (double)lateral.Profile.colliderSize.x + lateral.Binding.navigationSettings.clearanceMargin;
                Assert.LessOrEqual(paddedWidth + 2d * reserve, (double)capture.Samples[0].size.x);
                if (previousWidth > 0f) Assert.AreEqual(previousWidth, capture.Samples[0].size.x, "Lateral sign must not shrink the finite reserve.");
                previousWidth = capture.Samples[0].size.x;
            }
        }
    }

    /// <summary>Checks behind-A refusal, terminal fractional throttle, outside-terminal refusal, stale version, and work classification.</summary>
    [Test]
    public void Evaluate_BehindAAndTerminalFractionalClassification() {
        using (var behind = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 20f }, null, "e0");
            behind.SetPolicePosition(new Vector2(0f, -1f));
            var helper = CreateHelper(behind, graph);
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 0f, 20f));
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                new Vector2(0f, -1f), Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }

        using (var terminal = new PursuitFixture()) {
            var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * 10f }, null, "e0");
            terminal.SetPolicePosition(new Vector2(0f, 7.4f));
            var helper = CreateHelper(terminal, graph);
            var cursor = BindCursor(graph, new RoadPathQuery.PathSpan("e0", 7.4f, 10f));
            int work = 512;
            var result = helper.Evaluate(cursor, graph.Version,
                terminal.PolicePosition, new Vector2(0f, 9.5f), 4f, 0f, 0.2f, ref work, out float throttle);
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, result);
            Assert.Greater(throttle, 0f);
            Assert.Less(throttle, 1f);

            terminal.SetPolicePosition(new Vector2(0f, 9.5f));
            work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                terminal.PolicePosition, terminal.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
            work = 512;
            graph.Rebuild();
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version - 1,
                terminal.PolicePosition, terminal.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
            work = 0;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, graph.Version,
                terminal.PolicePosition, terminal.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }
    }

    /// <summary>Checks that a real unobstructed sensor returning an infinite clear gap remains eligible.</summary>
    [Test]
    public void Evaluate_ClearSensorInfiniteGapRemainsEligible() {
        using (var fixture = new PursuitFixture()) {
            ConfigureLongStraight(fixture, 100f, new Vector2(0f, 2f), new Vector2(0f, 90f));
            var capture = new RecordingClearance(true);
            var helper = CreateHelper(fixture, fixture.Binding.graph, capture);
            var cursor = BindCursor(fixture.Binding.graph, new RoadPathQuery.PathSpan("e0", 2f, 100f));
            int work = 512;
            var result = helper.Evaluate(cursor, fixture.Binding.graph.Version, fixture.PolicePosition,
                fixture.PolicePosition + Vector2.up * 8f, 4f, 0f, 0.02f, ref work, out float throttle);
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, result);
            Assert.Greater(throttle, 0f);
            Assert.GreaterOrEqual(capture.Samples.Count, 1);
        }
    }

    /// <summary>Checks exact one-short work refusal and a static-query refusal without asserting sensor cast internals.</summary>
    [Test]
    public void Evaluate_OneShortWorkAndStaticQueryFailureStopFailClosed() {
        int consumed;
        using (var measured = new PursuitFixture()) {
            ConfigureLongStraight(measured, 100f, new Vector2(0f, 2f), new Vector2(0f, 90f));
            var helper = CreateHelper(measured, measured.Binding.graph, new RecordingClearance(true));
            var cursor = BindCursor(measured.Binding.graph, new RoadPathQuery.PathSpan("e0", 2f, 100f));
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Clear, helper.Evaluate(cursor, measured.Binding.graph.Version,
                measured.PolicePosition, measured.PolicePosition + Vector2.up * 8f, 4f, 0f, 0.02f, ref work, out _));
            consumed = 512 - work;
            Assert.Greater(consumed, 0);
        }

        using (var shortWork = new PursuitFixture()) {
            ConfigureLongStraight(shortWork, 100f, new Vector2(0f, 2f), new Vector2(0f, 90f));
            var helper = CreateHelper(shortWork, shortWork.Binding.graph);
            var cursor = BindCursor(shortWork.Binding.graph, new RoadPathQuery.PathSpan("e0", 2f, 100f));
            int work = consumed - 1;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, shortWork.Binding.graph.Version,
                shortWork.PolicePosition, shortWork.PolicePosition + Vector2.up * 8f, 4f, 0f, 0.02f, ref work, out _));
        }

        using (var blocked = new PursuitFixture()) {
            ConfigureLongStraight(blocked, 100f, new Vector2(0f, 2f), new Vector2(0f, 90f));
            var helper = CreateHelper(blocked, blocked.Binding.graph, new RecordingClearance(false));
            var cursor = BindCursor(blocked.Binding.graph, new RoadPathQuery.PathSpan("e0", 2f, 100f));
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, blocked.Binding.graph.Version,
                blocked.PolicePosition, blocked.PolicePosition + Vector2.up * 8f, 4f, 0f, 0.02f, ref work, out _));
        }
    }

    /// <summary>Checks a real configured sensor buffer saturation refuses the command, without stubbing the cast.</summary>
    [Test]
    public void Evaluate_RealSensorSaturationStopsWithoutCastInspection() {
        using (var fixture = new PursuitFixture()) {
            ConfigureLongStraight(fixture, 40f, new Vector2(0f, 2f), new Vector2(0f, 35f));
            fixture.CreateBlocker(new Vector2(0f, 8f), false, Vector2.one);
            fixture.CreateBlocker(new Vector2(0f, 10f), false, Vector2.one);
            var helper = CreateHelper(fixture, fixture.Binding.graph, sensorCapacity: 1);
            var cursor = BindCursor(fixture.Binding.graph, new RoadPathQuery.PathSpan("e0", 2f, 40f));
            int work = 512;
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, helper.Evaluate(cursor, fixture.Binding.graph.Version,
                fixture.PolicePosition, fixture.PolicePosition + Vector2.up * 4f, 4f, 0f, 0.02f, ref work, out _));
        }
    }

    /// <summary>Checks a finite positive root that rounds below the motor's usable throttle fails closed.</summary>
    [Test]
    public void Evaluate_MotorIgnoredTinyPositiveThrottleStops() {
        using (var fixture = new PursuitFixture()) {
            ConfigureLongStraight(fixture, 40f, new Vector2(0f, 2f), new Vector2(0f, 35f));
            fixture.Profile.motorSettings.acceleration = 1e38f;
            fixture.Profile.motorSettings.maxEngineForce = 1e38f;
            var helper = CreateHelper(fixture, fixture.Binding.graph, sensorCapacity: 8);
            var cursor = BindCursor(fixture.Binding.graph, new RoadPathQuery.PathSpan("e0", 2f, 40f));
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked,
                fixture.Sensor.QuerySweep(Vector2.up, fixture.ControllerSettings.maxSweepDistance, out float gap, out _));
            double response = fixture.Profile.motorSettings.reactionTime + 2d * 10000f;
            double brake = 8d * fixture.ControllerSettings.comfort * fixture.Binding.body.Motor.crashModeForceMultiplier;
            double room = gap - (double)fixture.ControllerSettings.stopGap;
            double root = 2d * room / (Math.Sqrt(response * response + 2d * room / brake) + response);
            float roundedFraction = (float)(root / ((double)fixture.Profile.motorSettings.acceleration * 10000f) * (1d - 4d / 8388608d));
            Assert.Greater(roundedFraction, 0f, "Exercise a positive subnormal, not a negative or underflowed root.");
            Assert.IsTrue(Mathf.Approximately(roundedFraction, 0f), "The actual motor must ignore the otherwise positive fraction.");
            int work = 512;
            var result = helper.Evaluate(cursor, fixture.Binding.graph.Version, fixture.PolicePosition,
                fixture.PolicePosition + Vector2.up * 4f, 4f, 0f, 10000f, ref work, out float throttle);
            Assert.AreEqual(PoliceStraightRoadClearance.Result.Stop, result);
            Assert.AreEqual(0f, throttle);
        }
    }

    static PoliceStraightRoadClearance CreateHelper(PursuitFixture fixture, RoadGraphRuntime graph,
        IAreaClearanceQuery clearance = null, int sensorCapacity = 16, Vector2 origin = default) {
        var body = fixture.Binding.body;
        Vector2 scale = new Vector2(body.transform.lossyScale.x, body.transform.lossyScale.y);
        body.Sensor.Configure(fixture.Profile.motorSettings, sensorCapacity);
        return new PoliceStraightRoadClearance(body, graph, graphDocumentBounds(graph), origin,
            clearance ?? new AlwaysClear(true), fixture.ControllerSettings, fixture.Profile.motorSettings,
            body.WorldFootprint, Vector2.Scale(body.MainCollider.offset, scale), body.Motor.crashModeForceMultiplier,
            fixture.Binding.navigationSettings.clearanceMargin);
    }

    static Rect graphDocumentBounds(RoadGraphRuntime graph) => new Rect(-40f, -10f, 80f, 100f);

    static void ConfigureLongStraight(PursuitFixture fixture, float length, Vector2 policePosition, Vector2 playerPosition) {
        var graph = DocumentGraph(new[] { Vector2.zero, Vector2.up * length }, null, "e0");
        fixture.Binding.graph = graph;
        fixture.SetPolicePosition(policePosition);
        fixture.ReplacePlayer(playerPosition);
        fixture.Binding.body.Body.linearVelocity = Vector2.zero;
        fixture.Binding.body.Body.angularVelocity = 0f;
    }

    static double RequiredRange(PursuitFixture fixture, float q, float step) {
        var motor = fixture.Profile.motorSettings;
        var body = fixture.Binding.body;
        double acceleration = Math.Min((double)motor.acceleration, (double)motor.maxEngineForce / body.Body.mass) * Math.Max(1d, body.Motor.crashModeForceMultiplier);
        double braking = Math.Min((double)motor.brakeDeceleration, (double)motor.maxBrakeForce / body.Body.mass) * fixture.ControllerSettings.comfort * Math.Min(1d, body.Motor.crashModeForceMultiplier);
        double end = body.Body.linearVelocity.y + acceleration * q * step;
        return end * (motor.reactionTime + 2d * step) + end * end / (2d * braking) + fixture.ControllerSettings.stopGap;
    }

    static void HullBounds(PursuitFixture fixture, Vector2 origin, float q,
        out double lowX, out double lowY, out double highX, out double highY) {
        var body = fixture.Binding.body;
        var motor = fixture.Profile.motorSettings;
        Vector2 forward = body.transform.up.normalized;
        Vector2 right = new Vector2(forward.y, -forward.x);
        float radians = body.Body.rotation * Mathf.Deg2Rad;
        Vector2 bodyRight = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 bodyForward = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
        double mass = body.Body.mass;
        double crash = body.Motor.crashModeForceMultiplier;
        double acceleration = Math.Min(motor.acceleration, motor.maxEngineForce / mass) * Math.Max(1d, crash);
        double braking = Math.Min(motor.brakeDeceleration, motor.maxBrakeForce / mass) * fixture.ControllerSettings.comfort * Math.Min(1d, crash);
        double forwardSpeed = (double)body.Body.linearVelocity.x * forward.x + (double)body.Body.linearVelocity.y * forward.y;
        double lateralSpeed = (double)body.Body.linearVelocity.x * right.x + (double)body.Body.linearVelocity.y * right.y;
        double lateralReserve = motor.lateralGrip == 0f ? 0d : Math.Abs(lateralSpeed) * (1d - motor.lateralGrip) * fixture.StepDelta / motor.lateralGrip;
        double endSpeed = forwardSpeed + acceleration * q * fixture.StepDelta;
        double travel = lateralReserve + endSpeed * (motor.reactionTime + 2d * fixture.StepDelta) + endSpeed * endSpeed / (2d * braking);
        double halfWidth = ((double)body.WorldFootprint.x + fixture.Binding.navigationSettings.clearanceMargin) * 0.5d;
        double halfLength = ((double)body.WorldFootprint.y + fixture.Binding.navigationSettings.clearanceMargin) * 0.5d;
        Vector2 offset = Vector2.Scale(body.MainCollider.offset, new Vector2(body.transform.lossyScale.x, body.transform.lossyScale.y));
        double cx = (double)body.Body.position.x - origin.x + (double)bodyRight.x * offset.x + (double)bodyForward.x * offset.y;
        double cy = (double)body.Body.position.y - origin.y + (double)bodyRight.y * offset.x + (double)bodyForward.y * offset.y;
        double hx = Math.Abs(bodyRight.x) * halfWidth + Math.Abs(bodyForward.x) * halfLength + Math.Abs(right.x) * lateralReserve;
        double hy = Math.Abs(bodyRight.y) * halfWidth + Math.Abs(bodyForward.y) * halfLength + Math.Abs(right.y) * lateralReserve;
        lowX = Math.Min(cx, cx + forward.x * travel) - hx;
        highX = Math.Max(cx, cx + forward.x * travel) + hx;
        lowY = Math.Min(cy, cy + forward.y * travel) - hy;
        highY = Math.Max(cy, cy + forward.y * travel) + hy;
    }

    static PolicePathCursor BindCursor(RoadGraphRuntime graph, RoadPathQuery.PathSpan span) {
        var cursor = new PolicePathCursor();
        Assert.IsTrue(cursor.TryBind(graph, graph.Version, new[] { span },
            new PolicePathCursor.TrackingLimits(20f, 2f, 1.5f, 0.05f), new RoadPathQuery.SearchBudget(512)));
        return cursor;
    }

    static RoadGraphRuntime DocumentGraph(Vector2[] points, List<VehicleRole> roles, string edgeId,
        float width = 6f, string startJunction = "", string endJunction = "") {
        var document = new MapNavigationDocument { localBounds = new Rect(-40f, -10f, 80f, 100f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = points[0].x, y = points[0].y });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = points[points.Length - 1].x, y = points[points.Length - 1].y });
        var edge = new RoadEdgeRecord { edgeId = edgeId, fromNodeId = "a", toNodeId = "b", usableWidth = width,
            speedLimit = 5f, allowedRoles = roles, startJunctionId = startJunction, endJunctionId = endJunction };
        for (int index = 1; index < points.Length - 1; index++) edge.orderedPoints.Add(points[index]);
        document.edges.Add(edge);
        return new RoadGraphRuntime(document);
    }

    sealed class AlwaysClear : IAreaClearanceQuery {
        readonly bool clear;
        /// <summary>Creates a deterministic static-clearance adapter for pure proof cases.</summary>
        public AlwaysClear(bool clear) { this.clear = clear; }
        /// <summary>Returns the configured static-clearance outcome.</summary>
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) => clear;
    }

    sealed class RecordingClearance : IAreaClearanceQuery {
        internal readonly List<Sample> Samples = new List<Sample>();
        readonly bool clear;
        internal RecordingClearance(bool clear) { this.clear = clear; }
        /// <summary>Records the submitted rectangle and returns the configured static outcome.</summary>
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) {
            Samples.Add(new Sample { center = center, size = footprint, heading = headingDegrees });
            return clear;
        }
    }

    struct Sample {
        internal Vector2 center;
        internal Vector2 size;
        internal float heading;
    }
}
