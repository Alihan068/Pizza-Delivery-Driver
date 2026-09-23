using NUnit.Framework;
using UnityEngine;

/// <summary>Focused EditMode coverage for police body provider ownership, reuse, and per-visual caps.</summary>
public sealed class PoliceBodyProviderTests {
    GameObject providerObject;
    GameObject prefabRoot;
    GameObject foreignRoot;
    PoliceVehiclePrefabCatalog catalog;
    NpcVehicleProfile profile;

    [SetUp]
    public void SetUp() {
        providerObject = new GameObject("PoliceBodyProviderTest");
        prefabRoot = new GameObject("PoliceBodyTestPrefab");
        prefabRoot.SetActive(false);
        prefabRoot.AddComponent<PoliceVehicleBody>();

        catalog = ScriptableObject.CreateInstance<PoliceVehiclePrefabCatalog>();
        catalog.entries.Add(new PoliceVehiclePrefabCatalog.Entry { visualCatalogId = "test-police", prefab = prefabRoot.GetComponent<PoliceVehicleBody>() });
        profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profile.visualCatalogId = "test-police";
    }

    [TearDown]
    public void TearDown() {
        if (providerObject != null) Object.DestroyImmediate(providerObject);
        if (prefabRoot != null) Object.DestroyImmediate(prefabRoot);
        if (foreignRoot != null) Object.DestroyImmediate(foreignRoot);
        if (catalog != null) Object.DestroyImmediate(catalog);
        if (profile != null) Object.DestroyImmediate(profile);
    }

    /// <summary>Rejects invalid configuration, enforces the cap, reuses a released body, and blocks reconfiguration after allocation.</summary>
    [Test]
    public void Provider_ValidatesConfigurationCapsAndReusesBodies() {
        var provider = providerObject.AddComponent<PrefabPoliceVehicleBodyProvider>();
        Assert.IsFalse(provider.Configure(null, 1, out _));
        Assert.IsFalse(provider.Configure(catalog, 0, out _));
        Assert.IsTrue(provider.Configure(catalog, 1, out string reason), reason);

        PoliceVehicleBody first = provider.Acquire(profile);
        Assert.IsNotNull(first);
        Assert.IsFalse(first.gameObject.activeSelf);
        Assert.IsNull(provider.Acquire(profile), "A visual must not allocate beyond its configured cap.");
        Assert.IsFalse(provider.Configure(catalog, 1, out _), "Configuration must be locked after the first allocation, even when unchanged.");

        provider.Release(first);
        PoliceVehicleBody reused = provider.Acquire(profile);
        Assert.AreSame(first, reused);
    }

    /// <summary>Duplicate release creates neither a second free entry nor a second body allocation.</summary>
    [Test]
    public void Provider_DuplicateReleaseDoesNotDuplicateFreeBodies() {
        var provider = providerObject.AddComponent<PrefabPoliceVehicleBodyProvider>();
        Assert.IsTrue(provider.Configure(catalog, 1, out string reason), reason);
        PoliceVehicleBody body = provider.Acquire(profile);

        provider.Release(body);
        provider.Release(body);

        Assert.AreSame(body, provider.Acquire(profile));
        Assert.IsNull(provider.Acquire(profile), "Duplicate release must not create a second free-pool slot.");
    }

    /// <summary>Foreign bodies are ignored without being disabled or adopted into the provider pool.</summary>
    [Test]
    public void Provider_ForeignReleaseLeavesForeignBodyActiveAndUnowned() {
        var provider = providerObject.AddComponent<PrefabPoliceVehicleBodyProvider>();
        Assert.IsTrue(provider.Configure(catalog, 1, out string reason), reason);
        PoliceVehicleBody owned = provider.Acquire(profile);

        foreignRoot = new GameObject("ForeignPoliceBody");
        PoliceVehicleBody foreign = foreignRoot.AddComponent<PoliceVehicleBody>();
        provider.Release(foreign);

        Assert.IsTrue(foreign.gameObject.activeSelf);
        provider.Release(owned);
        Assert.AreSame(owned, provider.Acquire(profile));
        Assert.IsNull(provider.Acquire(profile), "A foreign release must not increase the provider's free or created count.");
    }

    /// <summary>Catalog resolution rejects duplicate visual ids before the provider can allocate an ambiguous prefab.</summary>
    [Test]
    public void Provider_DuplicateCatalogIdIsRejected() {
        catalog.entries.Add(new PoliceVehiclePrefabCatalog.Entry { visualCatalogId = "test-police", prefab = prefabRoot.GetComponent<PoliceVehicleBody>() });
        Assert.IsFalse(catalog.TryResolve("test-police", out _));

        var provider = providerObject.AddComponent<PrefabPoliceVehicleBodyProvider>();
        Assert.IsTrue(provider.Configure(catalog, 1, out string reason), reason);
        Assert.IsNull(provider.Acquire(profile));
    }
}
