using UnityEngine;

/// <summary>
/// Unity-side asset wrapper holding one map's authored <see cref="MapNavigationDocument"/>. The
/// wrapper is a normal ScriptableObject so it can be authored and referenced like any other content
/// asset; <see cref="ResolveDocument"/> always hands back a fully detached copy, never the asset's
/// own serialized instance, so nothing a caller does to the result can corrupt the saved asset.
/// </summary>
[CreateAssetMenu(fileName = "NewTrafficMapData", menuName = "PizzaGame/Traffic/Traffic Map Data")]
public class TrafficMapData : ScriptableObject {
    [SerializeField] MapNavigationDocument document = new MapNavigationDocument();

    /// <summary>Resolves a detached runtime copy of this asset's navigation document via a JSON round-trip.</summary>
    /// <returns>A copy safe to mutate; the asset's own data is never touched by the caller.</returns>
    public MapNavigationDocument ResolveDocument() {
        string json = JsonUtility.ToJson(document);
        return JsonUtility.FromJson<MapNavigationDocument>(json);
    }
}
