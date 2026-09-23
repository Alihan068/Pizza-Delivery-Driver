using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>S06.1–S06.7 coverage: pure RouteCursor tracking, drivability filter, car-following/junction/recovery/stuck rules, the population planner, and physically driven fixtures (seam continuity, corner braking, edge limits, queues, junction yielding, crash recovery) through the real motor.</summary>
public sealed class CivilianRouteFollowerTests {
    const float Side = 20f;

    // ---- RouteCursor (pure) ----

    [Test]
    public void Cursor_RejectsNullGapAndOpenLoopFailClosed() {
        var cursor = new RouteCursor();
        Assert.IsFalse(cursor.TryBind(null, SquareRoute(), out string issue));
        Assert.IsNotEmpty(issue);
        Assert.IsFalse(cursor.IsBound);

        var graph = new RoadGraphRuntime(SquareDocument());
        var gap = new CivilianRouteRecord { routeId = "gap", loop = true, edgeIds = new List<string> { "e0", "e2" } };
        Assert.IsFalse(cursor.TryBind(graph, gap, out issue));
        StringAssert.Contains("gap", issue);
        Assert.IsFalse(cursor.IsBound);

        var open = new CivilianRouteRecord { routeId = "open", loop = true, edgeIds = new List<string> { "e0", "e1", "e2" } };
        Assert.IsFalse(cursor.TryBind(graph, open, out issue));
        StringAssert.Contains("loop", issue);

        var dangling = new CivilianRouteRecord { routeId = "d", loop = false, edgeIds = new List<string> { "e0", "missing" } };
        Assert.IsFalse(cursor.TryBind(graph, dangling, out issue));
        StringAssert.Contains("dangling", issue);
    }

    [Test]
    public void Cursor_SquareLoopSamplesAreContinuousAcrossEverySeam() {
        var cursor = Bound(SquareDocument(), SquareRoute());
        Assert.AreEqual(4f * Side, cursor.TotalLength, 0.0001f);

        // Walk the whole loop in small arc-length steps; consecutive samples must never jump.
        const float step = 0.5f;
        Vector2 previous = cursor.SamplePoint(0f, out _);
        for (float d = step; d <= 4f * Side + step; d += step) {
            Vector2 point = cursor.SamplePoint(d, out Vector2 tangent);
            Assert.LessOrEqual(Vector2.Distance(previous, point), step + 0.0001f, "seam jump at " + d);
            Assert.AreEqual(1f, tangent.magnitude, 0.0001f);
            previous = point;
        }
        // Full loop wraps back to the origin.
        Assert.AreEqual(0f, Vector2.Distance(cursor.SamplePoint(4f * Side, out _), Vector2.zero), 0.0001f);
        // Sample past the seam lands on e0 again.
        Assert.AreEqual(0f, Vector2.Distance(cursor.SamplePoint(4f * Side + 3f, out _), new Vector2(0f, 3f)), 0.0001f);
    }

    [Test]
    public void Cursor_AdvanceCrossesEdgesAndCountsLapsExactlyOncePerSeam() {
        var cursor = Bound(SquareDocument(), SquareRoute());
        cursor.Reset();
        int lastEdge = 0;
        int edgeChanges = 0;
        // Positions come from an independent parametrisation of the square (not from the cursor),
        // offset 0.3 units sideways so the projection is exercised, never a point exactly on the line.
        for (int i = 1; i <= 500; i++) {
            float s = i * 0.5f;
            cursor.Advance(SquarePointAt(s, out Vector2 tangent) + new Vector2(-tangent.y, tangent.x) * 0.3f, 6f);
            if (cursor.EdgeIndex != lastEdge) {
                Assert.AreEqual((lastEdge + 1) % 4, cursor.EdgeIndex, "edges must be visited in route order");
                lastEdge = cursor.EdgeIndex;
                edgeChanges++;
            }
            float arc = cursor.LapCount * 4f * Side + cursor.EdgeIndex * Side + cursor.DistanceAlongEdge;
            Assert.AreEqual(s, arc, 0.01f, "cursor arc position must track the fed position at s=" + s);
        }
        Assert.AreEqual(3, cursor.LapCount, "250 units on an 80-unit loop is 3 full laps");
        Assert.AreEqual(12, edgeChanges);
    }

    [Test]
    public void Cursor_SkippedNodeStillLandsOnTheRightEdgeWithinWindow() {
        var cursor = Bound(SquareDocument(), SquareRoute());
        cursor.Reset();
        cursor.Advance(new Vector2(Side, Side - 5f), 6f); // that point is on e2 (x = Side), 45 units ahead — beyond the window
        Assert.AreEqual(0, cursor.EdgeIndex, "a bounded window may not leap two edges ahead");

        cursor.Advance(new Vector2(Side, Side - 5f), 100f);
        Assert.AreEqual(2, cursor.EdgeIndex);
        Assert.AreEqual(5f, cursor.DistanceAlongEdge, 0.0001f);
        Assert.AreEqual(0, cursor.LapCount);

        // Skipping from e2 across e3 and the seam onto e0 counts exactly one lap.
        cursor.Advance(new Vector2(0f, 2f), 100f);
        Assert.AreEqual(0, cursor.EdgeIndex);
        Assert.AreEqual(1, cursor.LapCount);
    }

    [Test]
    public void Cursor_OpenRouteClampsAtEndAndReportsIt() {
        var document = SquareDocument();
        var route = new CivilianRouteRecord { routeId = "open", loop = false, edgeIds = new List<string> { "e0", "e1" } };
        var cursor = Bound(document, route);
        Assert.IsFalse(cursor.ReachedEnd);
        Assert.AreEqual(0f, Vector2.Distance(cursor.SamplePoint(500f, out _), new Vector2(Side, Side)), 0.0001f);
        cursor.Advance(new Vector2(Side + 3f, Side + 3f), 500f);
        Assert.IsTrue(cursor.ReachedEnd);
        Assert.AreEqual(1, cursor.EdgeIndex);
        Assert.AreEqual(0, cursor.LapCount);
    }

    [Test]
    public void Cursor_IrregularLoopWithInteriorPointsMeasuresArcLengthAndFindsCorners() {
        var cursor = Bound(IrregularDocument(), IrregularRoute());
        float diagonal = Mathf.Sqrt(50f);
        Assert.AreEqual(10f + 2f * diagonal + 10f + 10f, cursor.TotalLength, 0.001f);
        cursor.Reset();
        // e0: (0,0)->(0,10) straight; node b bends 45° into e1's first diagonal — the node itself is the corner.
        Assert.AreEqual(10f, cursor.DistanceToNextTurn(30f, 100f), 0.001f);
        cursor.Advance(new Vector2(0.1f, 9f), 6f);
        Assert.AreEqual(1f, cursor.DistanceToNextTurn(30f, 100f), 0.001f);
        // The interior control point (5,15) is a 90° corner inside e1, found from a point on e1's first diagonal.
        cursor.Advance(new Vector2(2f, 12f), 20f);
        Assert.AreEqual(1, cursor.EdgeIndex);
        Assert.AreEqual(2f * Mathf.Sqrt(2f), cursor.DistanceAlongEdge, 0.001f);
        Assert.AreEqual(diagonal - 2f * Mathf.Sqrt(2f), cursor.DistanceToNextTurn(30f, 100f), 0.001f);
        Assert.AreEqual(100f, cursor.DistanceToNextTurn(200f, 100f), "no vertex bends 200°; max search returned");
    }

    // ---- Physical loop fixture ----

    /// <summary>The follower drives the real motor around a loop for several laps without any teleport, seam speed/heading jump, or corridor exit.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public void Follower_DrivesLoopForSeveralLapsWithoutSeamJumps(bool irregular) {
        const float deltaTime = 0.02f;
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            var document = irregular ? IrregularDocument() : SquareDocument();
            var route = irregular ? IrregularRoute() : SquareRoute();
            var graph = new RoadGraphRuntime(document);
            var motorSettings = new NpcMotorSettings { cruiseSpeed = 6f, maxSpeed = 9f, acceleration = 4f, turnRate = 120f, minimumTurningRadius = 3f, lateralGrip = 0.9f };
            Vector2 origin = new Vector2(100f, -50f); // non-zero map origin proves the world/local conversion happens once
            var rig = world.CreateMotor(motorSettings, 1f, origin + new Vector2(0f, 2f));
            var follower = world.AddFollower(rig.motor);
            Assert.IsTrue(follower.TryBeginRoute(graph, route, motorSettings, new Vector2(2f, 4f), origin, out string issue), issue);
            Assert.IsTrue(follower.IsFollowing);

            var cursor = follower.Cursor;
            float lapLength = cursor.TotalLength;
            int steps = Mathf.CeilToInt(3.2f * lapLength / 3.5f / deltaTime); // generous budget at a conservative average speed (corners slow the car)
            float previousSpeed = 0f;
            float previousHeading = rig.rb.rotation;
            Vector2 previousPosition = rig.rb.position;
            float maxDeviation = 0f;
            float maxSpeed = 0f;
            for (int i = 0; i < steps; i++) {
                world.Step();
                float speed = rig.rb.linearVelocity.magnitude;
                maxSpeed = Mathf.Max(maxSpeed, speed);
                Assert.LessOrEqual(Vector2.Distance(previousPosition, rig.rb.position), (Mathf.Max(speed, previousSpeed) + 0.5f) * deltaTime + 0.001f,
                    "position must move continuously — no teleport at step " + i);
                Assert.LessOrEqual(Mathf.Abs(speed - previousSpeed), (motorSettings.brakeDeceleration + motorSettings.acceleration) * deltaTime + 0.01f,
                    "speed must not jump at step " + i);
                Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(previousHeading, rig.rb.rotation)), motorSettings.turnRate * deltaTime + 0.01f,
                    "heading must not jump at step " + i);
                previousSpeed = speed;
                previousHeading = rig.rb.rotation;
                previousPosition = rig.rb.position;

                Vector2 local = MapNavigationCoordinates.WorldToLocal(rig.rb.position, origin);
                maxDeviation = Mathf.Max(maxDeviation, DistanceToRoute(document, route, local));
                if (cursor.LapCount >= 3) break;
            }

            Assert.GreaterOrEqual(cursor.LapCount, 3, "vehicle must complete three laps by driving; max speed " + maxSpeed + ", max deviation " + maxDeviation);
            Assert.LessOrEqual(maxDeviation, 3f, "vehicle must stay inside the road corridor around corners");
            Assert.GreaterOrEqual(maxSpeed, motorSettings.cruiseSpeed * 0.9f, "vehicle must reach cruise on straights");
            Assert.LessOrEqual(maxSpeed, motorSettings.cruiseSpeed + motorSettings.acceleration * deltaTime + 0.01f, "governor holds cruise within one step's acceleration");
            Assert.IsFalse(follower.LastCommand.reverseAllowed, "route following never asks for reverse");
        }
    }

    // ---- S06.2: turn and speed target ----

    /// <summary>On the square loop the car cruises on straights, brakes before each 90° corner down to the corner speed, and accelerates again after it.</summary>
    [Test]
    public void Follower_BrakesBeforeCornersAndRegainsCruiseAfter() {
        const float deltaTime = 0.02f;
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            var document = SquareDocument();
            var graph = new RoadGraphRuntime(document);
            var motorSettings = new NpcMotorSettings { cruiseSpeed = 6f, maxSpeed = 9f, acceleration = 4f, brakeDeceleration = 8f, turnRate = 120f, minimumTurningRadius = 3f, lateralGrip = 0.9f };
            var followerSettings = new CivilianFollowerSettings();
            var rig = world.CreateMotor(motorSettings, 1f, new Vector2(0f, 2f));
            var follower = world.AddFollower(rig.motor, followerSettings);
            Assert.IsTrue(follower.TryBeginRoute(graph, SquareRoute(), motorSettings, new Vector2(2f, 4f), Vector2.zero, out string issue), issue);

            float cornerSpeed = Mathf.Sqrt(followerSettings.cornerLateralAcceleration * motorSettings.minimumTurningRadius);
            var cursor = follower.Cursor;
            int lastEdge = cursor.EdgeIndex;
            int cornersPassed = 0;
            bool brakedThisEdge = false;
            float peakThisEdge = 0f;
            float minAfterCruise = float.PositiveInfinity;
            float maxDeviation = 0f;
            int budget = Mathf.CeilToInt(2.2f * cursor.TotalLength / 3.5f / deltaTime);
            for (int i = 0; i < budget && cornersPassed < 6; i++) {
                world.Step();
                float forwardSpeed = Vector2.Dot(rig.rb.linearVelocity, rig.go.transform.up);
                if (follower.LastCommand.brake > 0f) brakedThisEdge = true;
                peakThisEdge = Mathf.Max(peakThisEdge, forwardSpeed);
                maxDeviation = Mathf.Max(maxDeviation, DistanceToRoute(document, SquareRoute(), rig.rb.position));
                if (i > 50) Assert.LessOrEqual(forwardSpeed, follower.LastPlannedSpeed + 0.75f, "speed must track the plan at step " + i);
                if (peakThisEdge >= motorSettings.cruiseSpeed - 0.2f) minAfterCruise = Mathf.Min(minAfterCruise, forwardSpeed);
                if (cursor.EdgeIndex != lastEdge) {
                    cornersPassed++;
                    if (cornersPassed > 1) {
                        Assert.GreaterOrEqual(peakThisEdge, motorSettings.cruiseSpeed - 0.2f, "cruise must be regained on the straight before corner " + cornersPassed);
                        Assert.IsTrue(brakedThisEdge, "a real brake command must precede corner " + cornersPassed);
                        Assert.LessOrEqual(minAfterCruise, cornerSpeed + 1.5f, "after cruising, the car must slow markedly for corner " + cornersPassed);
                    }
                    lastEdge = cursor.EdgeIndex;
                    brakedThisEdge = false;
                    peakThisEdge = 0f;
                    minAfterCruise = float.PositiveInfinity;
                }
            }
            Assert.AreEqual(6, cornersPassed, "six corners within the step budget");
            Assert.LessOrEqual(maxDeviation, 2f, "slowing for corners keeps the car tighter to the road than S06.1's 3-unit bound");
        }
    }

    /// <summary>A lower speed limit on the upcoming edge is reached by braking before the edge boundary, not after entering it.</summary>
    [Test]
    public void Follower_BrakesForUpcomingEdgeSpeedLimit() {
        const float deltaTime = 0.02f;
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            // Long straight so the limit change is not confused with corner braking: e0 unlimited, e1 limited to 2.
            var document = new MapNavigationDocument();
            document.nodes.Add(Node("a", 0f, 0f));
            document.nodes.Add(Node("b", 0f, 40f));
            document.nodes.Add(Node("c", 0f, 80f));
            document.edges.Add(Edge("e0", "a", "b"));
            var slow = Edge("e1", "b", "c");
            slow.speedLimit = 2f;
            document.edges.Add(slow);
            var route = new CivilianRouteRecord { routeId = "straight", loop = false, edgeIds = new List<string> { "e0", "e1" } };
            var graph = new RoadGraphRuntime(document);
            var motorSettings = new NpcMotorSettings { cruiseSpeed = 6f, brakeDeceleration = 8f, acceleration = 4f };
            var rig = world.CreateMotor(motorSettings, 1f, new Vector2(0f, 1f));
            var follower = world.AddFollower(rig.motor);
            Assert.IsTrue(follower.TryBeginRoute(graph, route, motorSettings, new Vector2(2f, 4f), Vector2.zero, out string issue), issue);

            bool reachedCruise = false;
            bool brakedOnFirstEdge = false;
            for (int i = 0; i < 1500 && follower.Cursor.EdgeIndex == 0; i++) {
                world.Step();
                float speed = rig.rb.linearVelocity.y;
                if (speed >= 5.9f) reachedCruise = true;
                if (follower.LastCommand.brake > 0f) brakedOnFirstEdge = true;
            }
            Assert.AreEqual(1, follower.Cursor.EdgeIndex, "must reach the limited edge");
            Assert.IsTrue(reachedCruise, "must cruise on the unlimited straight first");
            Assert.IsTrue(brakedOnFirstEdge, "braking for the upcoming limit happens on the edge before it");
            Assert.LessOrEqual(rig.rb.linearVelocity.y, 2f + 0.3f, "arrives at the limited edge already near its limit");
            Assert.AreEqual(2f, follower.LastCommand.targetSpeed, 0.0001f);
        }
    }

    /// <summary>Routes a wide or large-radius vehicle cannot drive are refused at bind time, and the follower stays idle.</summary>
    [Test]
    public void Follower_RefusesRoutesTheVehicleCannotPhysicallyDrive() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var graph = new RoadGraphRuntime(SquareDocument());
            var motorSettings = new NpcMotorSettings { minimumTurningRadius = 3f };
            var rig = world.CreateMotor(motorSettings, 1f, Vector2.zero);
            var follower = world.AddFollower(rig.motor);

            Assert.IsFalse(follower.TryBeginRoute(graph, SquareRoute(), motorSettings, new Vector2(6f, 4f), Vector2.zero, out string issue), "6 wide on a 6 wide road with a safety margin");
            StringAssert.Contains("narrow", issue);
            Assert.IsFalse(follower.IsFollowing);
            Assert.IsFalse(follower.Cursor.IsBound, "a refused route leaves nothing bound");

            var heavy = new NpcMotorSettings { minimumTurningRadius = 25f };
            Assert.IsFalse(follower.TryBeginRoute(graph, SquareRoute(), heavy, new Vector2(2f, 4f), Vector2.zero, out issue), "radius 25 needs 25 units of straight for a 90° corner on 20-unit sides");
            StringAssert.Contains("transition", issue);

            Assert.IsTrue(follower.TryBeginRoute(graph, SquareRoute(), motorSettings, new Vector2(2f, 4f), Vector2.zero, out issue), issue);
            Assert.IsTrue(follower.IsFollowing);
        }
    }

    // ---- S06.3: braking for obstacles and pulling away again ----

    /// <summary>Three cars queue behind a static blocker (a stopped player, wreck or wall are all the same to the sensor): all stop, none reverses, none rams the one ahead; when the blocker leaves, the whole queue pulls away and the lead regains cruise.</summary>
    [Test]
    public void Follower_QueueBehindBlockerStopsWithoutReverseAndResumesWhenClear() {
        const float deltaTime = 0.02f;
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            var document = StraightDocument(120f);
            var route = new CivilianRouteRecord { routeId = "straight", loop = false, edgeIds = new List<string> { "e0" } };
            var graph = new RoadGraphRuntime(document);
            var motorSettings = new NpcMotorSettings { cruiseSpeed = 6f, acceleration = 4f, brakeDeceleration = 8f, minimumGap = 2f, sensorInterval = 0.2f, reactionTime = 0.3f };
            var blocker = world.CreateBody("Blocker", new Vector2(0f, 40f), new Vector2(2f, 2f), bodyType: RigidbodyType2D.Static);

            var cars = new List<(GameObject go, NpcVehicleMotor motor, Rigidbody2D rb)>();
            var followers = new List<CivilianRouteFollower>();
            for (int i = 0; i < 3; i++) {
                var rig = world.CreateMotor(motorSettings, 1f, new Vector2(0f, 20f - i * 7f));
                rig.rb.GetComponent<BoxCollider2D>().size = new Vector2(1.5f, 3f);
                world.AddSensor(rig.rb, motorSettings);
                var follower = world.AddFollower(rig.motor);
                Assert.IsTrue(follower.TryBeginRoute(graph, route, motorSettings, new Vector2(1.5f, 3f), Vector2.zero, out string issue), issue);
                cars.Add(rig);
                followers.Add(follower);
            }

            bool allStopped = false;
            int stoppedAtStep = -1;
            for (int i = 0; i < 1200; i++) {
                world.Step();
                for (int c = 0; c < cars.Count; c++) {
                    Assert.GreaterOrEqual(cars[c].rb.linearVelocity.y, -0.01f, "car " + c + " must never reverse (step " + i + ")");
                    Assert.IsFalse(followers[c].LastCommand.reverseAllowed);
                    Assert.IsFalse(followers[c].LastCommand.throttle < 0f);
                }
                bool stoppedNow = true;
                foreach (var car in cars) stoppedNow &= car.rb.linearVelocity.magnitude <= 0.05f;
                if (stoppedNow && i > 100) {
                    allStopped = true;
                    stoppedAtStep = i;
                    break;
                }
            }
            Assert.IsTrue(allStopped, "the whole queue must come to rest behind the blocker");

            float leadGap = 40f - 1f - (cars[0].rb.position.y + 1.5f);
            Assert.GreaterOrEqual(leadGap, 0.3f, "lead car must not touch the blocker");
            Assert.LessOrEqual(leadGap, motorSettings.minimumGap + 1.5f, "lead car must stop close behind the blocker, not far away");
            for (int c = 1; c < cars.Count; c++) {
                float gap = (cars[c - 1].rb.position.y - 1.5f) - (cars[c].rb.position.y + 1.5f);
                Assert.GreaterOrEqual(gap, 0.3f, "car " + c + " must not ram the car ahead");
                Assert.LessOrEqual(gap, motorSettings.minimumGap + 2f, "car " + c + " queues close behind");
            }
            Assert.IsTrue(followers[0].IsBlockedByObstacle, "lead is held in the blocked state");

            // Hold the queue for a while: nobody creeps or reverses while blocked.
            Vector2[] held = new Vector2[3];
            for (int c = 0; c < 3; c++) held[c] = cars[c].rb.position;
            for (int i = 0; i < 100; i++) world.Step();
            for (int c = 0; c < 3; c++) Assert.LessOrEqual(Vector2.Distance(held[c], cars[c].rb.position), 0.05f, "car " + c + " must stay put while blocked");

            // Blocker leaves: the queue must dissolve and the lead must regain cruise.
            Object.DestroyImmediate(blocker.gameObject);
            float leadPeak = 0f;
            for (int i = 0; i < 700; i++) {
                world.Step();
                leadPeak = Mathf.Max(leadPeak, cars[0].rb.linearVelocity.y);
                for (int c = 0; c < cars.Count; c++) Assert.GreaterOrEqual(cars[c].rb.linearVelocity.y, -0.01f, "car " + c + " must never reverse while pulling away");
            }
            Assert.GreaterOrEqual(leadPeak, motorSettings.cruiseSpeed - 0.3f, "lead regains cruise once clear; stopped at step " + stoppedAtStep);
            for (int c = 0; c < cars.Count; c++) {
                Assert.Greater(cars[c].rb.linearVelocity.y, 1f, "car " + c + " must be moving again, no permanent stop");
                Assert.IsFalse(followers[c].IsBlockedByObstacle, "car " + c + " left the blocked state");
            }
        }
    }

    /// <summary>Behind a slower vehicle the follower settles to that vehicle's speed and keeps at least the minimum gap instead of ramming it.</summary>
    [Test]
    public void Follower_MatchesSlowerVehicleAheadWithoutRamming() {
        const float deltaTime = 0.02f;
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            var document = StraightDocument(200f);
            var route = new CivilianRouteRecord { routeId = "straight", loop = false, edgeIds = new List<string> { "e0" } };
            var graph = new RoadGraphRuntime(document);
            var motorSettings = new NpcMotorSettings { cruiseSpeed = 6f, acceleration = 4f, brakeDeceleration = 8f, minimumGap = 2f, sensorInterval = 0.2f };
            var slow = world.CreateBody("Slow", new Vector2(0f, 12f), new Vector2(1.5f, 3f), bodyType: RigidbodyType2D.Kinematic);
            slow.linearVelocity = new Vector2(0f, 2f);

            var rig = world.CreateMotor(motorSettings, 1f, new Vector2(0f, 1f));
            rig.rb.GetComponent<BoxCollider2D>().size = new Vector2(1.5f, 3f);
            world.AddSensor(rig.rb, motorSettings);
            var follower = world.AddFollower(rig.motor);
            Assert.IsTrue(follower.TryBeginRoute(graph, route, motorSettings, new Vector2(1.5f, 3f), Vector2.zero, out string issue), issue);

            float minGap = float.PositiveInfinity;
            for (int i = 0; i < 1000; i++) {
                world.Step();
                float gap = (slow.position.y - 1.5f) - (rig.rb.position.y + 1.5f);
                minGap = Mathf.Min(minGap, gap);
                Assert.GreaterOrEqual(gap, 0.2f, "must never ram the slower vehicle (step " + i + ")");
            }
            float finalGap = (slow.position.y - 1.5f) - (rig.rb.position.y + 1.5f);
            Assert.AreEqual(2f, rig.rb.linearVelocity.y, 0.6f, "settles to the leader's speed");
            Assert.GreaterOrEqual(finalGap, motorSettings.minimumGap * 0.75f, "keeps roughly the minimum gap");
            Assert.LessOrEqual(finalGap, motorSettings.minimumGap + 3f, "does not hang back needlessly");
            Assert.IsFalse(follower.IsBlockedByObstacle, "a moving leader is followed, not treated as a wall");
        }
    }

    /// <summary>Pure car-following rules: clear road, matching a leader, emergency stop, and the blocked/resume hysteresis.</summary>
    [Test]
    public void ObstacleRules_MatchEmergencyAndHysteresis() {
        var p = new ObstacleFollowingRules.Parameters(minimumGap: 2f, emergencyGapFraction: 0.5f, resumeGapHysteresis: 1f, deceleration: 5.6f, stoppedSpeedThreshold: 0.1f);
        bool blocked = true;
        Assert.IsTrue(float.IsPositiveInfinity(ObstacleFollowingRules.AllowedSpeed(false, 0f, 0f, 6f, p, ref blocked, out bool emergency)));
        Assert.IsFalse(blocked, "a clear road clears the blocked flag");
        Assert.IsFalse(emergency);

        Assert.AreEqual(Mathf.Sqrt(2f * 5.6f * 2f), ObstacleFollowingRules.AllowedSpeed(true, 4f, 0f, 6f, p, ref blocked, out _), 0.0001f, "brake down to a stopped leader over gap minus minimum gap");
        Assert.AreEqual(2f, ObstacleFollowingRules.AllowedSpeed(true, 2f, 2f, 3f, p, ref blocked, out _), 0.0001f, "at the minimum gap, match the leader's speed");
        Assert.AreEqual(2f, ObstacleFollowingRules.AllowedSpeed(true, 1.5f, 2f, 3f, p, ref blocked, out emergency), 0.0001f, "inside the gap but above emergency: match, do not stop dead");
        Assert.IsFalse(emergency);
        Assert.IsFalse(blocked);

        Assert.AreEqual(0f, ObstacleFollowingRules.AllowedSpeed(true, 0.9f, 0f, 3f, p, ref blocked, out emergency), "inside the emergency fraction");
        Assert.IsTrue(emergency);
        Assert.IsTrue(blocked);

        // Held: a small re-opening is not enough; the hysteresis gap is.
        Assert.AreEqual(0f, ObstacleFollowingRules.AllowedSpeed(true, 2.5f, 0f, 0f, p, ref blocked, out emergency));
        Assert.IsFalse(emergency);
        Assert.IsTrue(blocked);
        Assert.AreEqual(Mathf.Sqrt(2f * 5.6f * 1f), ObstacleFollowingRules.AllowedSpeed(true, 3f, 0f, 0f, p, ref blocked, out _), 0.0001f);
        Assert.IsFalse(blocked, "released once gap >= minimum + hysteresis");

        // Coming to rest naturally (allowed ~0, speed ~0) enters the blocked state without an emergency.
        blocked = false;
        Assert.AreEqual(0f, ObstacleFollowingRules.AllowedSpeed(true, 2f, 0f, 0.05f, p, ref blocked, out emergency), 0.0001f);
        Assert.IsTrue(blocked);
        Assert.IsFalse(emergency);

        // NaN gap fails safe as a full stop; extrapolation never credits a leader pulling away.
        blocked = false;
        Assert.AreEqual(0f, ObstacleFollowingRules.AllowedSpeed(true, float.NaN, 0f, 3f, p, ref blocked, out emergency));
        Assert.IsTrue(emergency);
        Assert.AreEqual(4f, ObstacleFollowingRules.ExtrapolateGap(4f, 2f, 6f, 0.2f), 0.0001f);
        Assert.AreEqual(4f - 0.8f, ObstacleFollowingRules.ExtrapolateGap(4f, 6f, 2f, 0.2f), 0.0001f);
    }

    // ---- S06.5: crash states and safe recovery ----

    static CrashRecoveryPolicy.Parameters RecoveryParameters() => new CrashRecoveryPolicy.Parameters(
        lightImpactSpeed: 1f, heavyImpactSpeed: 3f, lightHoldSeconds: 0.6f, settleSpeed: 1f, settleTimeoutSeconds: 2f,
        rejoinRetrySeconds: 1f, reverseMaxSeconds: 1.5f, maxRejoinDistance: 8f);

    [Test]
    public void RecoveryPolicy_LightHoldsHeavySettlesAndContinuedContactDoesNotRestart() {
        var policy = new CrashRecoveryPolicy(RecoveryParameters());
        var clear = new CrashRecoveryPolicy.RejoinObservation(0f, true, false, true);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, policy.Current);

        policy.NotifyImpact(0.5f);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, policy.Current, "below the light threshold nothing happens");

        policy.NotifyImpact(1.5f);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.HoldingAfterContact, policy.Current);
        policy.Tick(0.5f, 0f, clear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.HoldingAfterContact, policy.Current);
        policy.Tick(0.2f, 0f, clear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, policy.Current, "light hold expires");

        policy.NotifyImpact(5f);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Settling, policy.Current);
        policy.Tick(1f, 4f, clear);
        policy.NotifyImpact(5f); // pinned against a wall: continued contact must not restart settling
        policy.Tick(1.1f, 4f, clear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, policy.Current, "settle timeout counted from the first impact, then a clear rejoin");
        Assert.AreEqual(0, policy.RefusedRejoinAttempts);

        policy.NotifyImpact(5f);
        policy.NotifyImpact(1.5f);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Settling, policy.Current, "a light touch never downgrades a heavy recovery");
        policy.Tick(0.1f, 0.5f, clear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, policy.Current, "momentum settled early: rejoin immediately");

        policy.NotifyImpact(1.5f);
        policy.NotifyImpact(5f);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Settling, policy.Current, "a heavy hit escalates a light hold");
        policy.Reset();
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, policy.Current);
        Assert.IsFalse(policy.IsRecovering);
    }

    [Test]
    public void RecoveryPolicy_MinimumCrashWaitKeepsStoppedHeavyCrashSettling() {
        var policy = new CrashRecoveryPolicy(new CrashRecoveryPolicy.Parameters(
            lightImpactSpeed: 1f, heavyImpactSpeed: 3f, lightHoldSeconds: 0.6f, settleSpeed: 1f,
            settleTimeoutSeconds: 1f, rejoinRetrySeconds: 1f, reverseMaxSeconds: 0f,
            maxRejoinDistance: 8f, minimumSettleSeconds: 3f));
        var clear = new CrashRecoveryPolicy.RejoinObservation(0f, true, false, true);

        policy.NotifyImpact(5f);
        policy.Tick(2.9f, 0f, clear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Settling, policy.Current);
        policy.Tick(0.1f, 0f, clear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, policy.Current);
    }

    [Test]
    public void RecoveryPolicy_BlockedRejoinWaitsRetriesAndReversesOnlyWithRearClear() {
        var policy = new CrashRecoveryPolicy(RecoveryParameters());
        var blockedPath = new CrashRecoveryPolicy.RejoinObservation(2f, false, false, true);
        var tooFar = new CrashRecoveryPolicy.RejoinObservation(20f, true, false, true);
        var blockedAheadRearBlocked = new CrashRecoveryPolicy.RejoinObservation(2f, true, true, false);
        var blockedAheadRearClear = new CrashRecoveryPolicy.RejoinObservation(2f, true, true, true);
        var clear = new CrashRecoveryPolicy.RejoinObservation(2f, true, false, false);

        policy.NotifyImpact(5f);
        policy.Tick(0.1f, 0f, blockedPath);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, policy.Current, "blocked path: wait, do not cut through buildings");
        Assert.AreEqual(1, policy.RefusedRejoinAttempts);
        policy.Tick(0.5f, 0f, clear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, policy.Current, "retries only on the interval, not every step");
        policy.Tick(0.6f, 0f, tooFar);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, policy.Current, "route too far: keep waiting for stuck handling");
        Assert.AreEqual(2, policy.RefusedRejoinAttempts);
        policy.Tick(1.1f, 0f, clear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, policy.Current);
        Assert.AreEqual(0, policy.RefusedRejoinAttempts, "a successful rejoin clears the refusal count");

        // Blocked ahead with a blocked rear: wait, never reverse.
        policy.NotifyImpact(5f);
        policy.Tick(0.1f, 0f, blockedAheadRearBlocked);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, policy.Current);
        // Blocked ahead with a clear rear: one bounded reverse, then re-evaluate.
        policy.Tick(1.1f, 0f, blockedAheadRearClear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Reversing, policy.Current);
        policy.Tick(0.5f, 0f, blockedAheadRearClear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Reversing, policy.Current);
        policy.Tick(1.1f, 0f, blockedAheadRearClear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, policy.Current, "reverse is bounded; still blocked → wait");
        policy.Tick(1.1f, 0f, blockedAheadRearClear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Reversing, policy.Current, "may reverse again after a wait interval");
        policy.Tick(0.2f, 0f, blockedAheadRearBlocked);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, policy.Current, "rear closes: stop reversing at once");
        policy.Tick(1.1f, 0f, clear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, policy.Current);

        // Reversing disabled by data: blocked ahead simply waits.
        var noReverse = new CrashRecoveryPolicy(new CrashRecoveryPolicy.Parameters(1f, 3f, 0.6f, 1f, 2f, 1f, 0f, 8f));
        noReverse.NotifyImpact(5f);
        noReverse.Tick(0.1f, 0f, blockedAheadRearClear);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, noReverse.Current);
    }

    sealed class FakeClearance : IAreaClearanceQuery {
        public bool clear = true;
        public int calls;
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) { calls++; return clear; }
    }

    /// <summary>A heavy impact (reported and physically applied by the test) cuts motor pressure, momentum settles, and the car drives back onto its loop and keeps lapping — with continuous motion throughout, never a teleport.</summary>
    [Test]
    public void Follower_HeavyImpactSettlesThenRejoinsByDriving() {
        const float deltaTime = 0.02f;
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            var document = SquareDocument();
            var graph = new RoadGraphRuntime(document);
            var motorSettings = new NpcMotorSettings { cruiseSpeed = 6f, acceleration = 4f, brakeDeceleration = 8f, turnRate = 120f, minimumTurningRadius = 3f, lateralGrip = 0.9f };
            var rig = world.CreateMotor(motorSettings, 1f, new Vector2(0f, 2f));
            var follower = world.AddFollower(rig.motor);
            var clearance = new FakeClearance();
            follower.SetRejoinClearance(clearance, new Rect(-50f, -50f, 120f, 120f));
            Assert.IsTrue(follower.TryBeginRoute(graph, SquareRoute(), motorSettings, new Vector2(2f, 4f), Vector2.zero, out string issue), issue);

            for (int i = 0; i < 100; i++) world.Step();
            Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, follower.RecoveryPhase);

            // Side impact: the test writes the physics, the follower never does.
            rig.rb.linearVelocity += new Vector2(4f, 0f);
            follower.NotifyImpact(5f);
            world.Step();
            Assert.AreEqual(CrashRecoveryPolicy.Phase.Settling, follower.RecoveryPhase);
            Assert.AreEqual(0f, follower.LastCommand.throttle, "motor pressure is cut while settling");
            Assert.IsFalse(follower.LastCommand.reverseAllowed);

            Vector2 previousPosition = rig.rb.position;
            int lapsBefore = follower.Cursor.LapCount;
            bool sawDrivingAgain = false;
            for (int i = 0; i < 3000; i++) {
                world.Step();
                float step = Vector2.Distance(previousPosition, rig.rb.position);
                Assert.LessOrEqual(step, (rig.rb.linearVelocity.magnitude + 1f) * deltaTime + 0.05f, "no teleport during recovery at step " + i);
                previousPosition = rig.rb.position;
                if (follower.RecoveryPhase == CrashRecoveryPolicy.Phase.Driving) sawDrivingAgain = true;
                if (follower.Cursor.LapCount >= lapsBefore + 1) break;
            }
            Assert.IsTrue(sawDrivingAgain, "the car must return to normal driving");
            Assert.GreaterOrEqual(follower.Cursor.LapCount, lapsBefore + 1, "and complete another lap by driving back onto the route");
            Assert.Greater(clearance.calls, 0, "the rejoin path was actually checked");
            Assert.LessOrEqual(DistanceToRoute(document, SquareRoute(), rig.rb.position), 3f, "back inside the corridor");
        }
    }

    /// <summary>When the way back is reported blocked (a building between the car and the road) the car waits calmly with the brake on, then rejoins once the path is reported clear.</summary>
    [Test]
    public void Follower_BlockedRejoinWaitsCalmlyThenResumesWhenClear() {
        const float deltaTime = 0.02f;
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            var document = SquareDocument();
            var graph = new RoadGraphRuntime(document);
            var motorSettings = new NpcMotorSettings { cruiseSpeed = 6f, acceleration = 4f, brakeDeceleration = 8f, turnRate = 120f, minimumTurningRadius = 3f };
            var rig = world.CreateMotor(motorSettings, 1f, new Vector2(0f, 2f));
            var follower = world.AddFollower(rig.motor);
            var clearance = new FakeClearance { clear = false };
            follower.SetRejoinClearance(clearance, new Rect(-50f, -50f, 120f, 120f));
            Assert.IsTrue(follower.TryBeginRoute(graph, SquareRoute(), motorSettings, new Vector2(2f, 4f), Vector2.zero, out string issue), issue);
            for (int i = 0; i < 100; i++) world.Step();

            follower.NotifyImpact(5f);
            for (int i = 0; i < 200; i++) world.Step(); // settle (car slows under the settle brake) and first refused rejoin
            Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, follower.RecoveryPhase);
            Assert.GreaterOrEqual(follower.RefusedRejoinAttempts, 1);

            Vector2 held = rig.rb.position;
            for (int i = 0; i < 150; i++) {
                world.Step();
                Assert.AreEqual(1f, follower.LastCommand.brake, "brake held while waiting");
                Assert.AreEqual(0f, follower.LastCommand.throttle);
            }
            Assert.LessOrEqual(Vector2.Distance(held, rig.rb.position), 0.5f, "waits in place, does not creep through the blocked path");
            Assert.LessOrEqual(rig.rb.linearVelocity.magnitude, 0.1f);

            clearance.clear = true;
            int lapsBefore = follower.Cursor.LapCount;
            for (int i = 0; i < 2500; i++) {
                world.Step();
                if (follower.Cursor.LapCount >= lapsBefore + 1) break;
            }
            Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, follower.RecoveryPhase);
            Assert.GreaterOrEqual(follower.Cursor.LapCount, lapsBefore + 1, "resumes and laps once the path is clear");
        }
    }

    // ---- S06.6: long stuck handling ----

    [Test]
    public void StuckMonitor_CountsNoProgressAndRecyclesOnlyInvisibleFarAndCooledDown() {
        var monitor = new StuckMonitor(new StuckMonitor.Parameters(progressSpeedThreshold: 0.2f, stuckSeconds: 8f, maxStuckSeconds: 30f, recycleCooldownSeconds: 5f, minPlayerDistance: 25f));
        for (int i = 0; i < 100; i++) monitor.Tick(0.02f, 0.1f); // moving: 5 units/s
        Assert.AreEqual(0f, monitor.StuckSeconds);
        Assert.IsFalse(monitor.IsStuck);
        Assert.AreEqual(StuckMonitor.Decision.None, monitor.Evaluate(false, 100f, 10f));

        for (int i = 0; i < 350; i++) monitor.Tick(0.02f, 0.001f); // 7 s of crawling below the threshold
        Assert.AreEqual(7f, monitor.StuckSeconds, 0.01f);
        Assert.IsFalse(monitor.IsStuck);
        Assert.IsFalse(monitor.TryTriggerLocalRecovery(), "not stuck yet");
        for (int i = 0; i < 60; i++) monitor.Tick(0.02f, 0f);
        Assert.IsTrue(monitor.IsStuck);
        Assert.IsTrue(monitor.TryTriggerLocalRecovery(), "one local recovery per episode");
        Assert.IsFalse(monitor.TryTriggerLocalRecovery());

        // In view, or near the player: never a recycle.
        Assert.AreEqual(StuckMonitor.Decision.WaitInPlace, monitor.Evaluate(true, 100f, 20f));
        Assert.AreEqual(StuckMonitor.Decision.WaitInPlace, monitor.Evaluate(false, 10f, 20f));
        Assert.IsFalse(monitor.RecycleRequested);

        // Cooldown from a fleet recycle still applies; then the recycle is requested exactly once.
        monitor.NoteFleetRecycle(18f);
        Assert.AreEqual(StuckMonitor.Decision.WaitInPlace, monitor.Evaluate(false, 100f, 20f));
        Assert.AreEqual(StuckMonitor.Decision.RequestRecycle, monitor.Evaluate(false, 100f, 23.5f));
        Assert.IsTrue(monitor.RecycleRequested);
        Assert.AreEqual(StuckMonitor.Decision.WaitInPlace, monitor.Evaluate(false, 100f, 60f), "never twice per life");

        // Progress resets the episode; a new life resets everything.
        monitor.Tick(0.02f, 0.5f);
        Assert.AreEqual(0f, monitor.StuckSeconds);
        Assert.IsFalse(monitor.IsStuck);
        monitor.Reset();
        Assert.IsFalse(monitor.RecycleRequested);

        // Beyond the hard cap the cooldown is waived but visibility still rules.
        var capped = new StuckMonitor(new StuckMonitor.Parameters(0.2f, 8f, 30f, 5f, 25f));
        for (int i = 0; i < 1600; i++) capped.Tick(0.02f, 0f); // 32 s
        Assert.IsTrue(capped.IsBeyondMaxDuration);
        capped.NoteFleetRecycle(100f);
        Assert.AreEqual(StuckMonitor.Decision.WaitInPlace, capped.Evaluate(true, 100f, 101f));
        Assert.AreEqual(StuckMonitor.Decision.RequestRecycle, capped.Evaluate(false, 100f, 101f), "past the cap the cooldown is waived");

        // Negative/NaN progress never counts as movement.
        var odd = new StuckMonitor(new StuckMonitor.Parameters(0.2f, 1f, 30f, 5f, 25f));
        for (int i = 0; i < 60; i++) odd.Tick(0.02f, i % 2 == 0 ? -5f : float.NaN);
        Assert.IsTrue(odd.IsStuck);
    }

    /// <summary>A car queued behind the player-shaped blocker in view is reported stuck after the authored time but is never recycled while visible; off screen and far it is recycled once, and it keeps its lifecycle intact meanwhile.</summary>
    [Test]
    public void Follower_StuckInViewWaitsAndIsOnlyRecycledWhenOffScreen() {
        const float deltaTime = 0.02f;
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            var graph = new RoadGraphRuntime(StraightDocument(120f));
            var route = new CivilianRouteRecord { routeId = "straight", loop = false, edgeIds = new List<string> { "e0" } };
            var motorSettings = new NpcMotorSettings { cruiseSpeed = 6f, acceleration = 4f, brakeDeceleration = 8f, minimumGap = 2f, sensorInterval = 0.2f };
            var followerSettings = new CivilianFollowerSettings { stuckSeconds = 3f, maxStuckSeconds = 10f, stuckRecycleCooldownSeconds = 1f, stuckRecycleMinPlayerDistance = 25f };
            world.CreateBody("Player", new Vector2(0f, 30f), new Vector2(2f, 3f), bodyType: RigidbodyType2D.Static);
            var rig = world.CreateMotor(motorSettings, 1f, new Vector2(0f, 20f));
            rig.rb.GetComponent<BoxCollider2D>().size = new Vector2(1.5f, 3f);
            world.AddSensor(rig.rb, motorSettings);
            var follower = world.AddFollower(rig.motor, followerSettings);
            Assert.IsTrue(follower.TryBeginRoute(graph, route, motorSettings, new Vector2(1.5f, 3f), Vector2.zero, out string issue), issue);

            float now = 0f;
            for (int i = 0; i < 500; i++) { world.Step(); now += deltaTime; } // 10 s: reach the blocker, queue, go stuck
            Assert.IsTrue(follower.IsBlockedByObstacle);
            Assert.IsTrue(follower.IsStuck, "stuck after " + follower.StuckSeconds + " s");
            Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, follower.RecoveryPhase, "a queued car never starts a reverse/rejoin recovery");
            Assert.GreaterOrEqual(rig.rb.linearVelocity.y, -0.01f);

            Assert.AreEqual(StuckMonitor.Decision.WaitInPlace, follower.EvaluateStuck(true, 5f, now), "in view: wait, never despawn in front of the player");
            Vector2 held = rig.rb.position;
            for (int i = 0; i < 100; i++) { world.Step(); now += deltaTime; }
            Assert.LessOrEqual(Vector2.Distance(held, rig.rb.position), 0.05f, "still waiting in place");
            Assert.IsTrue(follower.IsFollowing, "no lifecycle transition happened on its own");

            Assert.AreEqual(StuckMonitor.Decision.RequestRecycle, follower.EvaluateStuck(false, 60f, now), "off screen and far: one recycle request");
            Assert.AreEqual(StuckMonitor.Decision.WaitInPlace, follower.EvaluateStuck(false, 60f, now + 100f), "and never a second one for this life");
        }
    }

    // ---- S06.7: route population, weighted start and refill ----

    /// <summary>Two disjoint loops with two pools; the planner is exercised without a catalog (null = skip catalog validation, covered by the S02 resolver tests) so it stays runnable as a pure test.</summary>
    static MapNavigationDocument TwoRouteTwoPoolDocument() {
        var document = SquareDocument();
        // Second, disjoint loop to the east.
        document.nodes.Add(Node("p", 40f, 0f)); document.nodes.Add(Node("q", 40f, 20f)); document.nodes.Add(Node("r", 60f, 20f)); document.nodes.Add(Node("s", 60f, 0f));
        document.edges.Add(Edge("f0", "p", "q")); document.edges.Add(Edge("f1", "q", "r")); document.edges.Add(Edge("f2", "r", "s")); document.edges.Add(Edge("f3", "s", "p"));
        document.vehiclePools.Add(new VehiclePoolRecord { poolId = "mixed", entries = new List<VehiclePoolEntry> {
            new VehiclePoolEntry { vehicleProfileId = "sedan", weight = 3f }, new VehiclePoolEntry { vehicleProfileId = "van", weight = 1f }, new VehiclePoolEntry { vehicleProfileId = "never", weight = 0f } } });
        document.vehiclePools.Add(new VehiclePoolRecord { poolId = "sedans", entries = new List<VehiclePoolEntry> { new VehiclePoolEntry { vehicleProfileId = "sedan", weight = 1f } } });
        document.civilianRoutes.Add(new CivilianRouteRecord { routeId = "west", loop = true, vehiclePoolId = "mixed", targetCount = 3, edgeIds = new List<string> { "e0", "e1", "e2", "e3" } });
        document.civilianRoutes.Add(new CivilianRouteRecord { routeId = "east", loop = true, vehiclePoolId = "sedans", targetCount = 4, edgeIds = new List<string> { "f0", "f1", "f2", "f3" } });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "w1", edgeId = "e0", distanceAlongEdge = 5f, routeId = "west", role = VehicleRole.Civilian, clearanceWidth = 2f, clearanceLength = 4f });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "w2", edgeId = "e1", distanceAlongEdge = 10f, routeId = "west", role = VehicleRole.Civilian, clearanceWidth = 2f, clearanceLength = 4f });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "w3", edgeId = "e2", distanceAlongEdge = 15f, routeId = "west", role = VehicleRole.Civilian, clearanceWidth = 2f, clearanceLength = 4f });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "x1", edgeId = "f0", distanceAlongEdge = 5f, routeId = "east", role = VehicleRole.Civilian, clearanceWidth = 2f, clearanceLength = 4f });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "x2", edgeId = "f2", distanceAlongEdge = 5f, routeId = "east", role = VehicleRole.Civilian, clearanceWidth = 2f, clearanceLength = 4f });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "police", edgeId = "f1", distanceAlongEdge = 5f, routeId = string.Empty, role = VehicleRole.Police, clearanceWidth = 2f, clearanceLength = 4f });
        return document;
    }

    [Test]
    public void Planner_SameSeedSameStartDifferentSeedVariesZeroWeightNeverPickedAndGlobalCapHolds() {
        var document = TwoRouteTwoPoolDocument();
        ITrafficProfileCatalog catalog = null;
        {
            var graph = new RoadGraphRuntime(document);
            var a = new CivilianPopulationPlanner(document, graph, catalog, new System.Random(42), true, 10);
            var b = new CivilianPopulationPlanner(document, graph, catalog, new System.Random(42), true, 10);
            Assert.AreEqual(3, a.TargetOf("west"));
            Assert.AreEqual(4, a.TargetOf("east"));
            Assert.AreEqual(7, a.TotalTarget);

            var requestsA = new List<CivilianPopulationPlanner.SpawnRequest>();
            var requestsB = new List<CivilianPopulationPlanner.SpawnRequest>();
            Vector2 player = new Vector2(0f, 0f);
            Assert.AreEqual(7, a.PlanDeficits(player, requestsA));
            Assert.AreEqual(7, b.PlanDeficits(player, requestsB));
            for (int i = 0; i < 7; i++) {
                Assert.AreEqual(requestsA[i].vehicleProfileId, requestsB[i].vehicleProfileId, "same seed → same profiles");
                Assert.AreEqual(requestsA[i].spawnId, requestsB[i].spawnId, "same seed → same spawn points");
                Assert.AreNotEqual("never", requestsA[i].vehicleProfileId, "weight 0 is never picked");
                Assert.AreNotEqual("police", requestsA[i].spawnId, "police spawn points are not civilian spawns");
            }
            Assert.AreEqual(3, a.PendingOn("west"));
            Assert.AreEqual(4, a.PendingOn("east"));
            Assert.AreEqual(0, a.PlanDeficits(player, requestsA), "nothing more while pending");

            // Farthest-first spread: with the player at the origin, west's spawns are w2 (10,20) → w3 (20,5) → w1 (0,5).
            Assert.AreEqual("w2", requestsA[0].spawnId);
            Assert.AreEqual("w3", requestsA[1].spawnId);
            Assert.AreEqual("w1", requestsA[2].spawnId);
            Assert.AreEqual(new Vector2(10f, Side), requestsA[0].position);
            Assert.AreEqual(90f, Mathf.Abs(requestsA[0].headingDegrees), 0.01f, "heading follows e1 (rightwards)");

            // Different seeds vary the mixed pool over enough draws.
            var distinct = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++) {
                var c = new CivilianPopulationPlanner(document, graph, catalog, new System.Random(seed), true, 10);
                var r = new List<CivilianPopulationPlanner.SpawnRequest>();
                c.PlanDeficits(player, r);
                foreach (var request in r) if (request.routeId == "west") distinct.Add(request.vehicleProfileId);
            }
            CollectionAssert.AreEquivalent(new[] { "sedan", "van" }, distinct);

            // Global cap trims routes in authoring order: 5 → west 3, east 2.
            var capped = new CivilianPopulationPlanner(document, graph, catalog, new System.Random(1), true, 5);
            Assert.AreEqual(3, capped.TargetOf("west"));
            Assert.AreEqual(2, capped.TargetOf("east"));
            Assert.AreEqual(5, capped.TotalTarget);

            // Commit/fail/lost bookkeeping and a fresh profile per new life.
            a.NoteSpawnCommitted("west"); a.NoteSpawnCommitted("west"); a.NoteSpawnFailed("west");
            Assert.AreEqual(2, a.AliveOn("west"));
            Assert.AreEqual(0, a.PendingOn("west"));
            Assert.IsTrue(a.TryPlanReplacement("west", player, out var replacement));
            Assert.AreEqual("west", replacement.routeId);
            Assert.AreEqual(1, a.PendingOn("west"));
            Assert.IsFalse(a.TryPlanReplacement("west", player, out _), "target reached: no more");
            a.NoteSpawnCommitted("west");
            a.NoteVehicleLost("west");
            Assert.AreEqual(2, a.AliveOn("west"));
            Assert.IsTrue(a.TryPlanReplacement("west", player, out _));
            a.ReduceTarget("west", 1);
            Assert.AreEqual(1, a.TargetOf("west"));
            Assert.IsFalse(a.TryPlanReplacement("west", player, out _));
            Assert.IsFalse(a.TryPlanReplacement("nope", player, out _));
        }
    }

    [Test]
    public void Planner_NoTrafficSnapshotOpensNoRequestsAndInvalidRoutesGetZeroTarget() {
        var document = TwoRouteTwoPoolDocument();
        ITrafficProfileCatalog catalog = null;
        {
            var graph = new RoadGraphRuntime(document);
            var disabled = new CivilianPopulationPlanner(document, graph, catalog, new System.Random(3), false, 10);
            Assert.IsFalse(disabled.IsEnabled);
            var requests = new List<CivilianPopulationPlanner.SpawnRequest>();
            Assert.AreEqual(0, disabled.PlanDeficits(Vector2.zero, requests));
            Assert.IsFalse(disabled.TryPlanReplacement("west", Vector2.zero, out _));
            Assert.AreEqual(0, requests.Count);

            // A route with an unknown pool or no spawn points gets target 0 and a diagnostic, without blocking the others.
            document.civilianRoutes.Add(new CivilianRouteRecord { routeId = "broken", loop = true, vehiclePoolId = "missing", targetCount = 2, edgeIds = new List<string> { "e0", "e1", "e2", "e3" } });
            document.civilianRoutes.Add(new CivilianRouteRecord { routeId = "nospawn", loop = true, vehiclePoolId = "sedans", targetCount = 2, edgeIds = new List<string> { "e0", "e1", "e2", "e3" } });
            var planner = new CivilianPopulationPlanner(document, graph, catalog, new System.Random(3), true, 10);
            Assert.AreEqual(0, planner.TargetOf("broken"));
            Assert.AreEqual(0, planner.TargetOf("nospawn"));
            Assert.AreEqual(7, planner.TotalTarget);
            var issues = new List<string>();
            planner.CollectIssues(issues);
            Assert.IsTrue(issues.Exists(i => i.Contains("broken")));
            Assert.IsTrue(issues.Exists(i => i.Contains("nospawn")));
        }
    }

    [Test]
    public void SpawnPoseLocator_FollowsPolylineAndClampsPastTheEnd() {
        var graph = new RoadGraphRuntime(IrregularDocument());
        Assert.IsTrue(SpawnPoseLocator.TryLocate(graph, new VehicleSpawnRecord { edgeId = "e1", distanceAlongEdge = Mathf.Sqrt(50f) + Mathf.Sqrt(2f) }, out Vector2 p, out float heading));
        Assert.AreEqual(0f, Vector2.Distance(new Vector2(6f, 14f), p), 0.001f, "one unit diagonal past the apex on the second leg");
        Assert.AreEqual(MapNavigationCoordinates.DirectionToHeadingDegrees(new Vector2(5f, -5f)), heading, 0.01f);
        Assert.IsTrue(SpawnPoseLocator.TryLocate(graph, new VehicleSpawnRecord { edgeId = "e0", distanceAlongEdge = 999f }, out p, out heading));
        Assert.AreEqual(new Vector2(0f, 10f), p);
        Assert.AreEqual(0f, heading, 0.001f);
        Assert.IsFalse(SpawnPoseLocator.TryLocate(graph, new VehicleSpawnRecord { edgeId = "missing", distanceAlongEdge = 1f }, out _, out _));
        Assert.IsFalse(SpawnPoseLocator.TryLocate(graph, new VehicleSpawnRecord { edgeId = "e0", distanceAlongEdge = -1f }, out _, out _));
        Assert.IsFalse(SpawnPoseLocator.TryLocate(null, new VehicleSpawnRecord { edgeId = "e0", distanceAlongEdge = 1f }, out _, out _));
    }

    // ---- S06.4: junction right of way ----

    /// <summary>Deterministic arbiter rules: one grant per conflicting movement, stable arrival order, platooning on identical movements, release/expiry/owner-death handover, external occupancy and refusal of unauthored movements.</summary>
    [Test]
    public void JunctionService_GrantsOneConflictingMovementAtATimeWithStableOrderAndTimeout() {
        var clock = new SessionClock();
        var graph = new RoadGraphRuntime(CrossDocument());
        var service = new JunctionReservationService(graph, new[] { "J", "missing" }, clock, holdTimeoutSeconds: 5f);
        Assert.AreEqual(1, service.JunctionCount);

        // Two cars on the same movement platoon through together; cross traffic that arrives next waits,
        // and a same-movement car arriving after the cross traffic queues behind it (no starvation).
        Assert.AreEqual(JunctionReservationService.Outcome.Granted, service.Request("J", 1, "h_in", "h_out"));
        Assert.AreEqual(JunctionReservationService.Outcome.Granted, service.Request("J", 3, "h_in", "h_out"), "same movement platoons while nobody conflicting is queued");
        clock.Tick(0.1f);
        Assert.AreEqual(JunctionReservationService.Outcome.Waiting, service.Request("J", 2, "v_in", "v_out"));
        Assert.AreEqual(JunctionReservationService.Outcome.Waiting, service.Request("J", 4, "h_in", "h_out"), "arrived after queued cross traffic: no jumping the queue");
        Assert.AreEqual(2, service.HolderCount("J"));
        Assert.IsTrue(service.Holds("J", 1));
        Assert.IsFalse(service.Holds("J", 2));

        // A later same-movement arrival must not jump the queued cross traffic once the zone frees up.
        service.Release("J", 1);
        service.Release("J", 3);
        Assert.AreEqual(0, service.HolderCount("J"));
        clock.Tick(0.1f);
        Assert.AreEqual(JunctionReservationService.Outcome.Waiting, service.Request("J", 4, "h_in", "h_out"), "queued vehicle 2 is ahead");
        Assert.AreEqual(JunctionReservationService.Outcome.Granted, service.Request("J", 2, "v_in", "v_out"));
        Assert.AreEqual(JunctionReservationService.Outcome.Waiting, service.Request("J", 4, "h_in", "h_out"));

        // Owner dies without releasing: ReleaseAll hands the zone to the next in line.
        service.ReleaseAll(2);
        Assert.AreEqual(JunctionReservationService.Outcome.Granted, service.Request("J", 4, "h_in", "h_out"));

        // Bounded timeout: a holder that never releases loses the zone after the hold timeout.
        Assert.AreEqual(JunctionReservationService.Outcome.Waiting, service.Request("J", 5, "v_in", "v_out"));
        clock.Tick(4.9f);
        Assert.AreEqual(JunctionReservationService.Outcome.Waiting, service.Request("J", 5, "v_in", "v_out"));
        clock.Tick(0.2f);
        Assert.AreEqual(JunctionReservationService.Outcome.Granted, service.Request("J", 5, "v_in", "v_out"), "stale grant expired");
        Assert.IsFalse(service.Holds("J", 4));

        // Re-requesting refreshes the holder's expiry; paused clock does not expire anything.
        clock.Tick(4f);
        Assert.AreEqual(JunctionReservationService.Outcome.Granted, service.Request("J", 5, "v_in", "v_out"));
        clock.SetPaused(true);
        clock.Tick(100f);
        Assert.IsTrue(service.Holds("J", 5), "pause freezes junction timers");
        clock.SetPaused(false);
        service.Release("J", 5);

        // External occupancy (player parked in the zone) withholds new grants.
        service.SetExternalOccupancy("J", true);
        Assert.AreEqual(JunctionReservationService.Outcome.Waiting, service.Request("J", 6, "h_in", "h_out"));
        service.SetExternalOccupancy("J", false);
        Assert.AreEqual(JunctionReservationService.Outcome.Granted, service.Request("J", 6, "h_in", "h_out"));

        // Unauthored movement and unknown junction are refused, never granted.
        Assert.AreEqual(JunctionReservationService.Outcome.Refused, service.Request("J", 7, "h_in", "v_out"));
        Assert.AreEqual(JunctionReservationService.Outcome.Refused, service.Request("nope", 7, "h_in", "h_out"));
        Assert.AreEqual(JunctionReservationService.Outcome.Refused, service.Request("J", 7, null, "h_out"));

        // Same arrival time: authored priority decides, then life id.
        service.ReleaseAll(6);
        var tie = new JunctionReservationService(graph, new[] { "J" }, clock, 5f);
        Assert.AreEqual(JunctionReservationService.Outcome.Granted, tie.Request("J", 9, "h_in", "h_out"));
        Assert.AreEqual(JunctionReservationService.Outcome.Waiting, tie.Request("J", 10, "v_in", "v_out")); // priority 2 (authored higher)
        Assert.AreEqual(JunctionReservationService.Outcome.Waiting, tie.Request("J", 8, "d_in", "d_out")); // priority 0, lower id
        tie.Release("J", 9);
        Assert.AreEqual(JunctionReservationService.Outcome.Waiting, tie.Request("J", 8, "d_in", "d_out"), "higher authored priority goes first on a tie");
        Assert.AreEqual(JunctionReservationService.Outcome.Granted, tie.Request("J", 10, "v_in", "v_out"));
    }

    [Test]
    public void JunctionGeometry_StopLineAndExitOffsetsComeFromTheZone() {
        var zone = new Rect(-3f, -3f, 6f, 6f);
        Assert.AreEqual(3f, JunctionGeometry.DistanceToZoneBoundary(zone, Vector2.zero, Vector2.left), 0.0001f, "backwards along a horizontal approach");
        Assert.AreEqual(3f, JunctionGeometry.DistanceToZoneBoundary(zone, Vector2.zero, Vector2.up), 0.0001f);
        Assert.AreEqual(3f * Mathf.Sqrt(2f), JunctionGeometry.DistanceToZoneBoundary(zone, Vector2.zero, new Vector2(1f, 1f)), 0.001f, "diagonal exits at the corner");
        Assert.AreEqual(1f, JunctionGeometry.DistanceToZoneBoundary(zone, new Vector2(2f, 0f), Vector2.right), 0.0001f);
        Assert.AreEqual(0f, JunctionGeometry.DistanceToZoneBoundary(zone, new Vector2(10f, 0f), Vector2.right), "outside the zone");
        Assert.AreEqual(0f, JunctionGeometry.DistanceToZoneBoundary(zone, Vector2.zero, Vector2.zero), "degenerate direction");
    }

    /// <summary>Two cars arrive at a real crossing at the same time from two directions: exactly one takes the zone, the other waits at the stop line and crosses after; they never overlap and both finish. The back of a queue never enters the zone on its own.</summary>
    [Test]
    public void Follower_CrossTrafficYieldsAtJunctionAndBothComplete() {
        const float deltaTime = 0.02f;
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            var document = CrossDocument();
            var graph = new RoadGraphRuntime(document);
            var clock = new SessionClock();
            var service = new JunctionReservationService(graph, new[] { "J" }, clock, 10f);
            var motorSettings = new NpcMotorSettings { cruiseSpeed = 6f, acceleration = 4f, brakeDeceleration = 8f, minimumGap = 2f, turnRate = 120f, minimumTurningRadius = 3f };
            var horizontal = new CivilianRouteRecord { routeId = "h", loop = false, edgeIds = new List<string> { "h_in", "h_out" } };
            var vertical = new CivilianRouteRecord { routeId = "v", loop = false, edgeIds = new List<string> { "v_in", "v_out" } };

            var carH = world.CreateMotor(motorSettings, 1f, new Vector2(-24f, 0f));
            carH.go.transform.rotation = Quaternion.Euler(0f, 0f, -90f); // forward = +X
            carH.rb.GetComponent<BoxCollider2D>().size = new Vector2(1.5f, 3f);
            world.AddSensor(carH.rb, motorSettings);
            var followerH = world.AddFollower(carH.motor);
            followerH.SetJunctionArbiter(service, 1);
            Assert.IsTrue(followerH.TryBeginRoute(graph, horizontal, motorSettings, new Vector2(1.5f, 3f), Vector2.zero, out string issue), issue);

            var carV = world.CreateMotor(motorSettings, 1f, new Vector2(0f, -24f));
            carV.rb.GetComponent<BoxCollider2D>().size = new Vector2(1.5f, 3f);
            world.AddSensor(carV.rb, motorSettings);
            var followerV = world.AddFollower(carV.motor);
            followerV.SetJunctionArbiter(service, 2);
            Assert.IsTrue(followerV.TryBeginRoute(graph, vertical, motorSettings, new Vector2(1.5f, 3f), Vector2.zero, out issue), issue);

            float minSeparation = float.PositiveInfinity;
            bool someoneWaited = false;
            bool bothHeldAtOnce = false;
            for (int i = 0; i < 1500; i++) {
                world.Step();
                clock.Tick(deltaTime);
                minSeparation = Mathf.Min(minSeparation, Vector2.Distance(carH.rb.position, carV.rb.position));
                someoneWaited |= followerH.IsWaitingAtJunction || followerV.IsWaitingAtJunction;
                bothHeldAtOnce |= followerH.HeldJunctionId != null && followerV.HeldJunctionId != null;
                if (followerH.Cursor.ReachedEnd && followerV.Cursor.ReachedEnd) break;
            }
            Assert.IsTrue(followerH.Cursor.ReachedEnd && followerV.Cursor.ReachedEnd, "both cars must get through");
            Assert.IsTrue(someoneWaited, "one car must have yielded at the stop line");
            Assert.IsFalse(bothHeldAtOnce, "conflicting movements are never granted together");
            Assert.GreaterOrEqual(minSeparation, 3f, "cars must never overlap in the conflict zone");
            Assert.AreEqual(0, service.HolderCount("J"), "grants are released after traversal");
            Assert.IsNull(followerH.HeldJunctionId);
            Assert.IsNull(followerV.HeldJunctionId);
        }
    }

    /// <summary>Cross intersection at the origin: h_in (-30,0)->(0,0), h_out (0,0)->(30,0), v_in (0,-30)->(0,0), v_out (0,0)->(0,30), plus a diagonal d_in/d_out pair used only by the pure priority test. Zone is a 6x6 square around the node.</summary>
    static MapNavigationDocument CrossDocument() {
        var document = new MapNavigationDocument();
        document.nodes.Add(Node("c", 0f, 0f));
        document.nodes.Add(Node("w", -30f, 0f));
        document.nodes.Add(Node("e", 30f, 0f));
        document.nodes.Add(Node("s", 0f, -30f));
        document.nodes.Add(Node("n", 0f, 30f));
        document.nodes.Add(Node("sw", -30f, -30f));
        document.nodes.Add(Node("ne", 30f, 30f));
        var hIn = Edge("h_in", "w", "c"); hIn.endJunctionId = "J";
        var hOut = Edge("h_out", "c", "e"); hOut.startJunctionId = "J";
        var vIn = Edge("v_in", "s", "c"); vIn.endJunctionId = "J";
        var vOut = Edge("v_out", "c", "n"); vOut.startJunctionId = "J";
        var dIn = Edge("d_in", "sw", "c"); dIn.endJunctionId = "J";
        var dOut = Edge("d_out", "c", "ne"); dOut.startJunctionId = "J";
        document.edges.Add(hIn); document.edges.Add(hOut); document.edges.Add(vIn); document.edges.Add(vOut); document.edges.Add(dIn); document.edges.Add(dOut);
        var junction = new JunctionRecord { junctionId = "J", conflictZone = new Rect(-3f, -3f, 6f, 6f) };
        junction.allowedTransitions.Add(new JunctionTransition { fromEdgeId = "h_in", toEdgeId = "h_out", priority = 1 });
        junction.allowedTransitions.Add(new JunctionTransition { fromEdgeId = "v_in", toEdgeId = "v_out", priority = 2 });
        junction.allowedTransitions.Add(new JunctionTransition { fromEdgeId = "d_in", toEdgeId = "d_out", priority = 0 });
        document.junctions.Add(junction);
        return document;
    }

    static MapNavigationDocument StraightDocument(float length) {
        var document = new MapNavigationDocument();
        document.nodes.Add(Node("a", 0f, 0f));
        document.nodes.Add(Node("b", 0f, length));
        document.edges.Add(Edge("e0", "a", "b"));
        return document;
    }

    // ---- RouteTransitionFilter (pure) ----

    [Test]
    public void TransitionFilter_RejectsUTurnShortConnectorAndInteriorCornerButAcceptsFixtures() {
        var constraints = new RouteTransitionFilter.VehicleConstraints(2f, 0.5f, 3f, 135f);
        Assert.IsTrue(RouteTransitionFilter.IsDrivable(Bound(SquareDocument(), SquareRoute()), constraints, out string issue), issue);
        Assert.IsTrue(RouteTransitionFilter.IsDrivable(Bound(IrregularDocument(), IrregularRoute()), constraints, out issue), issue);

        // U-turn: up then straight back down through the same node.
        var uTurn = new MapNavigationDocument();
        uTurn.nodes.Add(Node("a", 0f, 0f));
        uTurn.nodes.Add(Node("b", 0f, 10f));
        uTurn.edges.Add(Edge("e0", "a", "b"));
        uTurn.edges.Add(Edge("e1", "b", "a"));
        var uRoute = new CivilianRouteRecord { routeId = "u", loop = true, edgeIds = new List<string> { "e0", "e1" } };
        Assert.IsFalse(RouteTransitionFilter.IsDrivable(Bound(uTurn, uRoute), constraints, out issue));
        StringAssert.Contains("180", issue);

        // Short connector: a 2-unit edge between two 90° turns cannot host a radius-3 arc.
        var connector = new MapNavigationDocument();
        connector.nodes.Add(Node("a", 0f, 0f));
        connector.nodes.Add(Node("b", 0f, 20f));
        connector.nodes.Add(Node("c", 2f, 20f));
        connector.nodes.Add(Node("d", 2f, 0f));
        connector.edges.Add(Edge("e0", "a", "b"));
        connector.edges.Add(Edge("e1", "b", "c"));
        connector.edges.Add(Edge("e2", "c", "d"));
        connector.edges.Add(Edge("e3", "d", "a"));
        var cRoute = new CivilianRouteRecord { routeId = "c", loop = true, edgeIds = new List<string> { "e0", "e1", "e2", "e3" } };
        Assert.IsFalse(RouteTransitionFilter.IsDrivable(Bound(connector, cRoute), constraints, out issue));
        StringAssert.Contains("e0 -> e1", issue);
        Assert.IsTrue(RouteTransitionFilter.IsDrivable(Bound(connector, cRoute), new RouteTransitionFilter.VehicleConstraints(1f, 0f, 1.5f, 135f), out issue), "a nimbler vehicle (r=1.5) fits the same connector: " + issue);

        // Interior corner tighter than the radius, inside one edge.
        var interior = SquareDocument();
        interior.edges[1].orderedPoints.Add(new Vector2(0.5f, Side + 1f)); // (0,20)->(0.5,21)->(20,20): a 1.1-unit jog whose 66° bend needs ~2 units of straight
        Assert.IsFalse(RouteTransitionFilter.IsDrivable(Bound(interior, SquareRoute()), constraints, out issue));
        StringAssert.Contains("interior corner", issue);

        // Invalid constraints fail closed.
        Assert.IsFalse(RouteTransitionFilter.IsDrivable(Bound(SquareDocument(), SquareRoute()), new RouteTransitionFilter.VehicleConstraints(float.NaN, 0f, 3f, 135f), out issue));
        Assert.IsFalse(RouteTransitionFilter.IsDrivable(new RouteCursor(), constraints, out issue));
    }

    [Test]
    public void Cursor_TryGetNextTurnReportsAngleAndNonLoopEndAsStop() {
        var cursor = Bound(IrregularDocument(), IrregularRoute());
        cursor.Reset();
        Assert.IsTrue(cursor.TryGetNextTurn(30f, 100f, out float distance, out float angle));
        Assert.AreEqual(10f, distance, 0.001f);
        Assert.AreEqual(45f, angle, 0.01f);
        Assert.IsFalse(cursor.TryGetNextTurn(30f, 5f, out distance, out angle), "no turn within 5 units");
        Assert.AreEqual(5f, distance);
        Assert.AreEqual(0f, angle);
        Assert.AreEqual(10f, cursor.DistanceToEdgeEnd, 0.001f);
        Assert.AreEqual("e1", cursor.NextEdge.edgeId);

        var open = Bound(SquareDocument(), new CivilianRouteRecord { routeId = "open", loop = false, edgeIds = new List<string> { "e0" } });
        open.Reset();
        Assert.IsTrue(open.TryGetNextTurn(30f, 100f, out distance, out angle));
        Assert.AreEqual(Side, distance, 0.001f);
        Assert.AreEqual(180f, angle);
        Assert.IsNull(open.NextEdge);
    }

    /// <summary>An edge speed limit below cruise governs the target speed on that edge only.</summary>
    [Test]
    public void Follower_HonoursEdgeSpeedLimitBelowCruise() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var document = SquareDocument();
            document.edges[0].speedLimit = 2f;
            var graph = new RoadGraphRuntime(document);
            var motorSettings = new NpcMotorSettings { cruiseSpeed = 6f };
            var rig = world.CreateMotor(motorSettings, 1f, new Vector2(0f, 1f));
            var follower = world.AddFollower(rig.motor);
            Assert.IsTrue(follower.TryBeginRoute(graph, SquareRoute(), motorSettings, new Vector2(2f, 4f), Vector2.zero, out _));
            world.Step();
            Assert.AreEqual(2f, follower.LastCommand.targetSpeed, 0.0001f);
            Assert.AreEqual(0, follower.Cursor.EdgeIndex);
        }
    }

    /// <summary>Stopping/reset never touches the rigidbody through the follower and clears state for pool reuse.</summary>
    [Test]
    public void Follower_StopAndResetLeaveMotorStoppedAndCursorUnbound() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var graph = new RoadGraphRuntime(SquareDocument());
            var motorSettings = new NpcMotorSettings();
            var rig = world.CreateMotor(motorSettings, 1f, Vector2.zero);
            var follower = world.AddFollower(rig.motor);
            Assert.IsTrue(follower.TryBeginRoute(graph, SquareRoute(), motorSettings, new Vector2(2f, 4f), Vector2.zero, out _));
            for (int i = 0; i < 20; i++) world.Step();
            Assert.Greater(rig.rb.linearVelocity.magnitude, 0.5f);

            follower.StopFollowing();
            Assert.IsFalse(follower.IsFollowing);
            float speedAtStop = rig.rb.linearVelocity.magnitude;
            world.Step();
            Assert.Less(rig.rb.linearVelocity.magnitude, speedAtStop, "motor received a stopped (braking) command, not a velocity write");
            Assert.Greater(rig.rb.linearVelocity.magnitude, 0f, "no instant velocity zeroing by the follower");

            follower.ResetForNewLife();
            Assert.IsFalse(follower.Cursor.IsBound);
            Assert.IsFalse(follower.IsFollowing);
            Assert.IsFalse(follower.TryBeginRoute(graph, null, motorSettings, new Vector2(2f, 4f), Vector2.zero, out string issue));
            Assert.IsNotEmpty(issue);
        }
    }

    // ---- fixtures ----

    static RouteCursor Bound(MapNavigationDocument document, CivilianRouteRecord route) {
        var cursor = new RouteCursor();
        Assert.IsTrue(cursor.TryBind(new RoadGraphRuntime(document), route, out string issue), issue);
        return cursor;
    }

    /// <summary>Independent arc-length parametrisation of the square loop (no cursor involved).</summary>
    static Vector2 SquarePointAt(float s, out Vector2 tangent) {
        s %= 4f * Side;
        int edge = Mathf.FloorToInt(s / Side);
        float d = s - edge * Side;
        switch (edge) {
            case 0: tangent = Vector2.up; return new Vector2(0f, d);
            case 1: tangent = Vector2.right; return new Vector2(d, Side);
            case 2: tangent = Vector2.down; return new Vector2(Side, Side - d);
            default: tangent = Vector2.left; return new Vector2(Side - d, 0f);
        }
    }

    /// <summary>Counter-clockwise square: (0,0)->(0,S)->(S,S)->(S,0)->(0,0).</summary>
    static MapNavigationDocument SquareDocument() {
        var document = new MapNavigationDocument();
        document.nodes.Add(Node("a", 0f, 0f));
        document.nodes.Add(Node("b", 0f, Side));
        document.nodes.Add(Node("c", Side, Side));
        document.nodes.Add(Node("d", Side, 0f));
        document.edges.Add(Edge("e0", "a", "b"));
        document.edges.Add(Edge("e1", "b", "c"));
        document.edges.Add(Edge("e2", "c", "d"));
        document.edges.Add(Edge("e3", "d", "a"));
        return document;
    }

    static CivilianRouteRecord SquareRoute() => new CivilianRouteRecord { routeId = "square", loop = true, edgeIds = new List<string> { "e0", "e1", "e2", "e3" } };

    /// <summary>Irregular closed loop a->b->c->d->a: e1 is a roof-shaped detour with an interior 90° control point, so corners of 45° and 90° exist both at nodes and inside an edge.</summary>
    static MapNavigationDocument IrregularDocument() {
        var document = new MapNavigationDocument();
        document.nodes.Add(Node("a", 0f, 0f));
        document.nodes.Add(Node("b", 0f, 10f));
        document.nodes.Add(Node("c", 10f, 10f));
        document.nodes.Add(Node("d", 10f, 0f));
        document.edges.Add(Edge("e0", "a", "b"));                       // 10 straight up
        var bend = Edge("e1", "b", "c");                                 // (0,10)->(5,15)->(10,10): two diagonals of sqrt(50), 45° in, 90° at the apex, 45° out
        bend.orderedPoints.Add(new Vector2(5f, 15f));
        document.edges.Add(bend);
        document.edges.Add(Edge("e2", "c", "d"));                       // 10 straight down
        document.edges.Add(Edge("e3", "d", "a"));                       // 10 left
        return document;
    }

    static CivilianRouteRecord IrregularRoute() => new CivilianRouteRecord { routeId = "irregular", loop = true, edgeIds = new List<string> { "e0", "e1", "e2", "e3" } };

    static float DistanceToRoute(MapNavigationDocument document, CivilianRouteRecord route, Vector2 local) {
        float best = float.PositiveInfinity;
        foreach (var edgeId in route.edgeIds) {
            var edge = document.edges.Find(e => e.edgeId == edgeId);
            var from = document.nodes.Find(n => n.nodeId == edge.fromNodeId);
            var to = document.nodes.Find(n => n.nodeId == edge.toNodeId);
            var points = new List<Vector2> { new Vector2(from.x, from.y) };
            points.AddRange(edge.orderedPoints);
            points.Add(new Vector2(to.x, to.y));
            for (int i = 0; i < points.Count - 1; i++) {
                Vector2 ab = points[i + 1] - points[i];
                float t = Mathf.Clamp01(Vector2.Dot(local - points[i], ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(points[i] + ab * t, local));
            }
        }
        return best;
    }

    static RoadNodeRecord Node(string id, float x, float y) => new RoadNodeRecord { nodeId = id, x = x, y = y };
    static RoadEdgeRecord Edge(string id, string from, string to) => new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = 6f, speedLimit = 10f };
}
