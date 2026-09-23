using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Targeted Edit Mode coverage for the S10.1 modifier preview contract.</summary>
public class MapModifierSelectionTests {
    readonly List<Object> createdObjects = new List<Object>();

    [TearDown]
    /// <summary>Releases temporary ScriptableObjects created by each test.</summary>
    public void TearDown() {
        GameManager.Instance = null;
        for (int i = 0; i < createdObjects.Count; i++)
            if (createdObjects[i] != null) Object.DestroyImmediate(createdObjects[i]);
        createdObjects.Clear();
    }

    [Test]
    /// <summary>Verifies empty, single, and larger toggle selections remain duplicate-free.</summary>
    public void ToggleSupportsEmptySingleAndManySelectionsWithoutDuplicates() {
        ShiftModifierData[] modifiers = CreateModifiers(4);
        var empty = ModifierSelectionUiHelper.Toggle(null, modifiers[0].modifierId, true);
        var one = ModifierSelectionUiHelper.Toggle(empty, modifiers[1].modifierId, true);
        var many = ModifierSelectionUiHelper.Toggle(one, modifiers[2].modifierId, true);
        many = ModifierSelectionUiHelper.Toggle(many, modifiers[3].modifierId, true);
        var duplicate = ModifierSelectionUiHelper.Toggle(many, modifiers[2].modifierId, true);

        Assert.That(empty, Has.Count.EqualTo(1));
        Assert.That(one, Has.Count.EqualTo(2));
        Assert.That(many, Has.Count.EqualTo(4));
        Assert.That(duplicate, Has.Count.EqualTo(4));
        Assert.That(many, Is.Not.SameAs(duplicate));
    }

    [Test]
    /// <summary>Verifies a preview toggle returns a new request without mutating the applied source.</summary>
    public void PreviewToggleDoesNotMutateAppliedSelection() {
        ShiftModifierData[] modifiers = CreateModifiers(2);
        var applied = new List<string> { modifiers[0].modifierId };

        var preview = ModifierSelectionUiHelper.Toggle(applied, modifiers[1].modifierId, true);

        CollectionAssert.AreEqual(new[] { modifiers[0].modifierId }, applied);
        CollectionAssert.AreEqual(new[] { modifiers[0].modifierId, modifiers[1].modifierId }, preview);
    }

    [Test]
    /// <summary>Verifies a reopened preview can restore the canonical applied id order.</summary>
    public void ReopenCanRestoreCanonicalAppliedSelection() {
        ShiftModifierData[] modifiers = CreateModifiers(3);
        List<string> preview = ModifierSelectionUiHelper.Toggle(null, modifiers[0].modifierId, true);
        preview = ModifierSelectionUiHelper.Toggle(preview, modifiers[1].modifierId, true);
        preview = ModifierSelectionUiHelper.Toggle(preview, modifiers[2].modifierId, true);
        ModifierSelectionResult applied = ModifierSelectionResolver.Resolve(modifiers, preview);
        var reopened = new List<string>();
        for (int i = 0; i < applied.resolvedModifiers.Count; i++)
            reopened.Add(applied.resolvedModifiers[i].modifierId);

        Assert.That(applied.rejectedIds, Is.Empty);
        CollectionAssert.AreEqual(preview, reopened);
    }

    [Test]
    /// <summary>Verifies score products and exclusive-group rejection for combinations.</summary>
    public void ResolverPreservesCombinationMultiplierAndExclusiveGroups() {
        ShiftModifierData[] modifiers = CreateModifiers(4);
        modifiers[0].scoreMultiplier = 0.6f;
        modifiers[1].scoreMultiplier = 0.5f;
        modifiers[1].exclusiveGroup = "traffic-mode";
        modifiers[2].exclusiveGroup = "traffic-mode";
        var request = new[] { modifiers[0].modifierId, modifiers[1].modifierId, modifiers[2].modifierId, modifiers[3].modifierId };

        ModifierSelectionResult result = ModifierSelectionResolver.Resolve(modifiers, request);

        Assert.That(result.resolvedModifiers, Has.Count.EqualTo(3));
        Assert.That(result.rejectedIds, Has.Count.EqualTo(1));
        Assert.That(ModifierSelectionUiHelper.GetPreviewScoreMultiplier(result.resolvedModifiers), Is.EqualTo(0.3f).Within(0.0001f));
    }

    [Test]
    /// <summary>Verifies traffic and police modifiers are explicitly unsupported on incapable maps.</summary>
    public void CapabilityModifiersAreUnsupportedWithoutMapTrafficData() {
        ShiftModifierData trafficModifier = CreateModifier("traffic", true, false);
        ShiftModifierData policeModifier = CreateModifier("police", false, true);
        MapData map = CreateMap(false);
        MapData capableMap = CreateMap(true);

        Assert.That(ModifierSelectionUiHelper.IsSupported(trafficModifier, map), Is.False);
        Assert.That(ModifierSelectionUiHelper.IsSupported(policeModifier, map), Is.False);
        Assert.That(ModifierSelectionUiHelper.IsSupported(trafficModifier, capableMap), Is.True);
        Assert.That(ModifierSelectionUiHelper.IsSupported(policeModifier, capableMap), Is.True);
    }

    [Test]
    /// <summary>Verifies applied/reopened state, unsupported deselection, and bounded row shrinking through the real panel component.</summary>
    public void PanelRestoresAppliedSelectionAllowsUnsupportedDeselectAndShrinksRows() {
        ShiftModifierData traffic = CreateModifier("traffic", true, false);
        ShiftModifierData normal = CreateModifier("normal", false, false);
        ShiftModifierData extra = CreateModifier("extra", false, false);
        MapData map = CreateMap(false);
        map.mapId = "map";

        GameObject managerObject = new GameObject("S10.1 Test GameManager");
        GameManager manager = managerObject.AddComponent<GameManager>();
        createdObjects.Add(managerObject);
        manager.allModifiers = new[] { traffic, normal, extra };
        manager.currentMap = map;
        manager.ownedMapIds.Add(map.mapId);
        var registry = new ContentRegistry();
        registry.AddProvider(new BuiltInContentProvider(null, new[] { map }));
        registry.Rebuild();
        SetAutoPropertyBackingField(manager, "Content", registry);
        GameManager.Instance = manager;
        manager.SelectModifiers(new[] { traffic.modifierId, normal.modifierId, extra.modifierId });

        GameObject panelObject = new GameObject("S10.1 Test MapSelectionPanel");
        MapSelectionPanel panel = panelObject.AddComponent<MapSelectionPanel>();
        createdObjects.Add(panelObject);
        GameObject contentObject = new GameObject("ModifierContent", typeof(RectTransform));
        createdObjects.Add(contentObject);
        SetField(panel, "modifierContent", contentObject.GetComponent<RectTransform>());
        SetField(panel, "modifierLayoutBuilt", true);
        Invoke(panel, "ResolveInitialSelection");
        Invoke(panel, "RefreshModifierUI", manager, map, null);

        Assert.That(contentObject.transform.childCount, Is.EqualTo(3));
        Invoke(panel, "ResolveInitialSelection");
        Assert.That(InvokeBool(panel, "AreModifierSelectionsApplied", manager), Is.True);

        Toggle trafficToggle = contentObject.transform.GetChild(0).GetComponent<Toggle>();
        Assert.That(trafficToggle.interactable, Is.True, "A selected unsupported modifier must remain removable.");
        trafficToggle.isOn = false;
        Assert.That(manager.SelectedModifierIds, Has.Member(traffic.modifierId), "Preview deselection must not mutate applied GameManager state before Start Job.");
        List<string> previewIds = (List<string>)GetField(panel, "previewModifierIds");
        Assert.That(previewIds, Does.Not.Contain(traffic.modifierId));

        manager.allModifiers = new[] { traffic };
        Invoke(panel, "RefreshModifierUI", manager, map, null);
        Assert.That(contentObject.transform.childCount, Is.EqualTo(1));
        GameManager.Instance = null;
    }

    ShiftModifierData[] CreateModifiers(int count) {
        var result = new ShiftModifierData[count];
        for (int i = 0; i < count; i++) result[i] = CreateModifier("modifier-" + i, false, false);
        return result;
    }

    ShiftModifierData CreateModifier(string id, bool disablesTraffic, bool disablesPolice) {
        var modifier = ScriptableObject.CreateInstance<ShiftModifierData>();
        modifier.modifierId = id;
        modifier.scoreMultiplier = 1f;
        modifier.disablesCivilianTraffic = disablesTraffic;
        modifier.disablesPolice = disablesPolice;
        createdObjects.Add(modifier);
        return modifier;
    }

    MapData CreateMap(bool supportsTraffic) {
        var map = ScriptableObject.CreateInstance<MapData>();
        if (supportsTraffic) {
            var traffic = ScriptableObject.CreateInstance<TrafficMapData>();
            map.trafficMapData = traffic;
            createdObjects.Add(traffic);
        }
        createdObjects.Add(map);
        return map;
    }

    static void SetField(object target, string name, object value) {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    static object GetField(object target, string name) {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    static void SetAutoPropertyBackingField(object target, string propertyName, object value) {
        SetField(target, "<" + propertyName + ">k__BackingField", value);
    }

    static void Invoke(object target, string methodName, params object[] args) {
        target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }

    static bool InvokeBool(object target, string methodName, params object[] args) {
        return (bool)target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}
