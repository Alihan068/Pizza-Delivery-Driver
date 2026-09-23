# Expressway authoring helper — staging only

This helper is intentionally blocked until Main supplies the world-geometry survey. It does not
open Unity, modify `Assets`, write scenes/assets, or infer world coordinates from Tilemap cells.

The inventory constants are copied from `memory-bank/traffic-police/expressway-road-inventory.md`:
the eight long seven-cell bands and the four-cell outer ring. Grid parent world transform,
road-cell centers, native colliders, safe 90-degree turns, player default target/reachability,
and route closure must come from the owner survey.

The staged gate requires:

- every inventory band covered by exact surveyed navigation edge ids;
- verified local bounds and player-default-to-delivery-target reachability;
- at least one survey-approved safe 90-degree turn with a concrete native-clearance evidence id;
- native collider-clearance evidence, without inventing a clearance result;
- an existing approved Expressway density asset, without copying NarrowDistrict tuning;
- map-specific navigation/population/respawn/damage/police profile paths;
- a loaded `Expressway` scene with typed `SceneTrafficBinding` referencing `Map_Expressway`.

Expected map/scene identity was verified locally:

```text
Map:  Assets/ScriptableObjects/Map_Expressway.asset
ID:   8f350d4ea56b4fe4bccd87dd90ba7cc1
Scene: Assets/Scenes/Expressway.unity
Name: Expressway
```

The future asset paths are explicit inputs and are validated read-only. Missing paths return a
concrete rejection; the helper does not create them or silently substitute NarrowDistrict assets.
Main should review the survey and pilot gate before calling any later application code.
