using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>Coverage for the Shadow role: goal selection over a fake street map, opening claims and the Interceptor assets.</summary>
public sealed class PoliceShadowTacticsTests {
    const string InterceptorPath = "Assets/ScriptableObjects/Police/PoliceInterceptor.asset";
    const string ShadowBehaviorPath = "Assets/ScriptableObjects/Police/PoliceShadowBehavior.asset";
    const string SportPath = "Assets/ScriptableObjects/Police/PoliceSport.asset";
    const string InterceptorPrefabPath = "Assets/Prefabs/Police/PoliceInterceptor.prefab";
    const string CatalogPath = "Assets/ScriptableObjects/Police/PoliceVehiclePrefabCatalog.asset";

    static readonly PoliceShadowTactics.Settings Settings = new PoliceShadowTactics.Settings();

    /// <summary>A street running north along x in [-2, 2] with solid blocks on both sides, plus optional gaps.</summary>
    sealed class StreetMap {
        readonly List<Rect> walls = new List<Rect>();

        public StreetMap(params Rect[] extraOpenings) {
            walls.Add(Rect.MinMaxRect(-30f, -50f, -2f, 50f));
            walls.Add(Rect.MinMaxRect(2f, -50f, 30f, 50f));
            foreach (Rect opening in extraOpenings) Carve(opening);
        }

        public StreetMap WithWall(Rect wall) {
            walls.Add(wall);
            return this;
        }

        public static StreetMap Open() {
            var map = new StreetMap();
            map.walls.Clear();
            return map;
        }

        void Carve(Rect opening) {
            var carved = new List<Rect>();
            foreach (Rect wall in walls) {
                if (!wall.Overlaps(opening)) { carved.Add(wall); continue; }
                if (opening.yMin > wall.yMin) carved.Add(Rect.MinMaxRect(wall.xMin, wall.yMin, wall.xMax, opening.yMin));
                if (opening.yMax < wall.yMax) carved.Add(Rect.MinMaxRect(wall.xMin, opening.yMax, wall.xMax, wall.yMax));
                if (opening.xMin > wall.xMin) carved.Add(Rect.MinMaxRect(wall.xMin, Mathf.Max(wall.yMin, opening.yMin), opening.xMin, Mathf.Min(wall.yMax, opening.yMax)));
                if (opening.xMax < wall.xMax) carved.Add(Rect.MinMaxRect(opening.xMax, Mathf.Max(wall.yMin, opening.yMin), wall.xMax, Mathf.Min(wall.yMax, opening.yMax)));
            }
            walls.Clear();
            walls.AddRange(carved);
        }

        public bool Clear(Vector2 from, Vector2 to) {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / 0.1f));
            for (int i = 0; i <= steps; i++) {
                Vector2 point = Vector2.Lerp(from, to, i / (float)steps);
                foreach (Rect wall in walls) if (wall.Contains(point)) return false;
            }
            return true;
        }
    }

    static readonly Vector2 Player = Vector2.zero;
    static readonly Vector2 NorthFast = new Vector2(0f, 6f);
    static readonly Vector2 PoliceBehindLeft = new Vector2(-1f, -3f);

    /// <summary>A side street ahead on the right is blocked: the goal is its mouth, not the player.</summary>
    [Test]
    public void SideStreetAhead_GoalIsItsMouth() {
        var map = new StreetMap(Rect.MinMaxRect(2f, 8f, 30f, 11f));
        var goal = PoliceShadowTactics.Select(Player, NorthFast, PoliceBehindLeft, Settings, map.Clear);
        Assert.IsTrue(goal.blocking, "an open side street ahead is cut off");
        Assert.AreEqual(2f, goal.point.x, 0.01f, "parks at the mouth, mouthDepth into the side street");
        Assert.That(goal.point.y, Is.InRange(8f, 11f), "inside the side street's width");
    }

    /// <summary>No side street: the unit shadows slightly ahead of the player on its own side.</summary>
    [Test]
    public void StraightStreet_ShadowsAheadOnItsSide() {
        var map = new StreetMap();
        var goal = PoliceShadowTactics.Select(Player, NorthFast, PoliceBehindLeft, Settings, map.Clear);
        Assert.IsFalse(goal.blocking);
        Assert.AreEqual(6f * Settings.leadSeconds, goal.point.y, 0.01f, "leads by speed x lead seconds");
        Assert.AreEqual(-Settings.sideOffset, goal.point.x, 0.01f, "on the police unit's side (left)");
    }

    /// <summary>Open ground has no walls, so nothing counts as a side street.</summary>
    [Test]
    public void OpenGround_IsNeverTreatedAsASideStreet() {
        var goal = PoliceShadowTactics.Select(Player, NorthFast, PoliceBehindLeft, Settings, StreetMap.Open().Clear);
        Assert.IsFalse(goal.blocking);
        Assert.Greater(goal.point.y, 3f, "still leads the player");
    }

    /// <summary>A mouth another unit holds is skipped for the next free one.</summary>
    [Test]
    public void ClaimedMouth_IsSkippedForTheNextOpening() {
        var map = new StreetMap(Rect.MinMaxRect(-30f, 4f, -2f, 6f), Rect.MinMaxRect(2f, 8f, 30f, 11f));
        var free = PoliceShadowTactics.Select(Player, NorthFast, PoliceBehindLeft, Settings, map.Clear);
        Assert.IsTrue(free.blocking);
        Assert.Less(free.point.x, 0f, "nearest opening (left) is taken first");

        var other = PoliceShadowTactics.Select(Player, NorthFast, PoliceBehindLeft, Settings, map.Clear, mouth => mouth.x > 0f);
        Assert.IsTrue(other.blocking);
        Assert.Greater(other.point.x, 0f, "the left mouth is held, so the right one is chosen");

        var none = PoliceShadowTactics.Select(Player, NorthFast, PoliceBehindLeft, Settings, map.Clear, mouth => false);
        Assert.IsFalse(none.blocking, "all mouths held: fall back to shadowing");
    }

    /// <summary>A dead end ahead limits the search to the openings before it.</summary>
    [Test]
    public void DeadEnd_OnlyOpeningsBeforeTheWallCount() {
        var map = new StreetMap(Rect.MinMaxRect(2f, 3.5f, 30f, 5.5f)).WithWall(Rect.MinMaxRect(-2f, 6f, 2f, 7f));
        var goal = PoliceShadowTactics.Select(Player, NorthFast, PoliceBehindLeft, Settings, map.Clear);
        Assert.IsTrue(goal.blocking);
        Assert.That(goal.point.y, Is.InRange(3.5f, 5.5f));
    }

    /// <summary>A stopped player gets a standoff on the unit's side, not a goal on top of it.</summary>
    [Test]
    public void SlowPlayer_KeepsAStandoff() {
        var goal = PoliceShadowTactics.Select(Player, new Vector2(0f, 0.2f), new Vector2(0f, -10f), Settings, StreetMap.Open().Clear);
        Assert.IsFalse(goal.blocking);
        Assert.AreEqual(new Vector2(0f, -Settings.slowStandoff), goal.point);
    }

    /// <summary>Invalid input never produces a non-finite goal.</summary>
    [Test]
    public void InvalidInput_FailsClosedToThePlayerPosition() {
        var goal = PoliceShadowTactics.Select(Player, new Vector2(float.NaN, 1f), PoliceBehindLeft, Settings, StreetMap.Open().Clear);
        Assert.IsFalse(goal.blocking);
        Assert.AreEqual(Player, goal.point);
        Assert.AreEqual(Player, PoliceShadowTactics.Select(Player, NorthFast, PoliceBehindLeft, Settings, null).point);
    }

    /// <summary>Two Shadow units never hold the same mouth; claims expire and can be released.</summary>
    [Test]
    public void Coordinator_OpeningClaimsAreExclusiveAndExpire() {
        var coordinator = new PoliceTacticsCoordinator(null);
        var target = new PoliceTacticsCoordinator.TargetFacts(99, 0);
        Assert.IsTrue(coordinator.BeginLife(1, target, null));
        Assert.IsTrue(coordinator.BeginLife(2, target, null));
        Assert.IsTrue(coordinator.TryClaimOpening(1, new Vector2(2f, 9f), 3f, 10f, 0.6f));
        Assert.IsFalse(coordinator.IsOpeningFree(2, new Vector2(2.5f, 10f), 3f, 10.1f), "same mouth is held by unit 1");
        Assert.IsFalse(coordinator.TryClaimOpening(2, new Vector2(2.5f, 10f), 3f, 10.1f, 0.6f));
        Assert.IsTrue(coordinator.IsOpeningFree(1, new Vector2(2.5f, 10f), 3f, 10.1f), "a unit's own claim never blocks it");
        Assert.IsTrue(coordinator.IsOpeningFree(2, new Vector2(-8f, 20f), 3f, 10.1f), "a different mouth is free");
        Assert.IsTrue(coordinator.IsOpeningFree(2, new Vector2(2.5f, 10f), 3f, 10.7f), "expired claim");
        coordinator.ReleaseOpening(1);
        Assert.IsTrue(coordinator.TryClaimOpening(2, new Vector2(2.5f, 10f), 3f, 10.2f, 0.6f), "released claim");
    }

    /// <summary>The Interceptor assets are Shadow-only, faster than Sport, and pass police preflight.</summary>
    [Test]
    public void InterceptorAssets_AreShadowOnlyFastAndValid() {
        var interceptor = AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>(InterceptorPath);
        var sport = AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>(SportPath);
        var behavior = AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>(ShadowBehaviorPath);
        Assert.IsNotNull(interceptor);
        Assert.IsNotNull(behavior);
        Assert.AreEqual(PoliceTacticalRole.Shadow, behavior.tacticalRole);
        Assert.AreEqual("builtin:police-shadow", behavior.behaviorProfileId);
        Assert.IsTrue(interceptor.SupportsRole(PoliceTacticalRole.Shadow));
        Assert.IsFalse(interceptor.SupportsRole(PoliceTacticalRole.Pursue), "no Pursue: the coordinator can never reassign it to ramming");
        Assert.IsFalse(interceptor.SupportsRole(PoliceTacticalRole.Ram));
        Assert.AreEqual("builtin:police-interceptor", interceptor.sharedNpc.vehicleProfileId);
        Assert.Greater(interceptor.sharedNpc.motorSettings.maxSpeed, sport.sharedNpc.motorSettings.maxSpeed);
        Assert.Less(interceptor.sharedNpc.motorSettings.minimumTurningRadius, sport.sharedNpc.motorSettings.minimumTurningRadius);

        var catalog = new BuiltInTrafficProfileCatalog(new[] { interceptor.sharedNpc });
        Assert.IsTrue(PolicePreflight.ValidateVehicleProfile(interceptor, catalog, out string vehicleReason), vehicleReason);
        Assert.IsTrue(PolicePreflight.ValidateBehaviorProfile(behavior, out string behaviorReason), behaviorReason);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InterceptorPrefabPath);
        Assert.IsNotNull(prefab);
        Assert.IsTrue(PolicePreflight.ValidatePrefab(prefab.GetComponent<PoliceVehicleBody>(), interceptor.sharedNpc, out string prefabReason), prefabReason);
        var prefabCatalog = AssetDatabase.LoadAssetAtPath<PoliceVehiclePrefabCatalog>(CatalogPath);
        Assert.IsTrue(PolicePreflight.ValidatePrefabCatalog(prefabCatalog, out string catalogReason), catalogReason);
        Assert.IsTrue(prefabCatalog.entries.Exists(e => e.visualCatalogId == interceptor.sharedNpc.visualCatalogId && e.prefab == prefab));
    }
}
