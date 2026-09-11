# Pizza Map Kit

This folder contains the reusable authoring kit for the built-in and future Pizza Delivery Driver maps.

Use memory-bank/pizza_map_authoring.md for the full workflow and scene contract.

## Manual Tile authoring

The generated `PizzaMapTilePalette.prefab` and project-specific RuleTile assets were removed so RuleTiles can be authored manually from the untouched Kenney source packages.

The raw Kenney 2D sources are now under `Assets/StoreAssets/Kenney/`, grouped into `RPGUrban`, `RPGUrbanPack`, `RoguelikeCity` and `RoguelikeCityPack`.

## Rebuild workflow

Edit a PizzaMapBlueprint under Assets/MapBlueprints, then use:

- PizzaGame > Map Authoring > Open Map Authoring Window
- Rebuild Scenes From Existing Blueprints

The builder writes six scene tilemap layers: Ground Tiles, Terrain Tiles, Road Edge Details, Road Rule Tiles, Diagonal Road Details and Road Markings. It places non-tile decorations from blueprint stamps and preserves the gameplay shell and owner-authored collection-point collider overrides.

The built-in secondary maps use the GreenSedan footprint as their scale reference. Local roads are two cells wide and broad routes are seven cells wide, but both values are ordinary blueprint data and can be changed for future maps. The generated detailed asphalt sprites and theme accent/shoulder textures are point-filtered and remain grouped with their matching material family.

Bulk generation skips the protected primary `GameScene` by default. Map authors should work in NarrowDistrict, Expressway or a new scene/blueprint; rebuilding GameScene requires the explicit destructive Inspector toggle.

## Current secondary map composition — 2026-09-10

NarrowDistrict is 168×112 and Expressway is 176×120. Both use the GreenSedan as the scale reference, center the Grid around world (0,0), and use broad routes with two-cell local streets. NarrowDistrict uses the RPG Urban night family with a seven-cell boulevard and offset mixed-use blocks. Expressway uses the Roguelike City daytime paving family with a nine-cell highway, a seven-cell arterial, asymmetrical neighborhoods, green pocket courtyards, dirt/field accents and four broad diagonal access ramps. The large unexplained default water region was removed.

The generated decoration family contains three related top-down houses, parks, fountains, trees, shrubs, flower beds, benches and lamps. Customer markers sit beside houses or parks, while the PizzaPlace and distant roadside PizzaCar remain off-road service locations with their collection-point prefab children. A final editor check confirms no customer cell or decoration sprite bound overlaps a drivable Road Rule Tile.

PizzaMapDecorationFactory is idempotent: it repairs existing generated prefabs by resolving and assigning the imported Sprite sub-asset before saving. This keeps decorations visible after Unity reimport or domain reload and makes the kit safe to regenerate.

Road and sidewalk RuleTiles are now left for manual authoring. Diagonal Road Details is a separate transparent overlay for diagonal strokes, and Road Markings selects single markings for narrow strokes and double markings for broad strokes. MapCameraFramingOverride widens the view only on the secondary maps. Future city traffic loop routes are recorded but are not implemented by this kit.

## Diagonal marking and map review rules — 2026-09-10

Diagonal Road Details uses a short centered dash texture and a direction-derived tile rotation. Straight horizontal/vertical markings do not process diagonal segments. When a blueprint route changes, review both a zoomed diagonal segment and the full city composition.

The post-commit review scores are NarrowDistrict **8.3/10** and Expressway **8.2/10**. NarrowDistrict passed after adding its offset night park and mixed-use heart and protecting the four-tone night ground family. Expressway passed after a density pass and a second surface pass that replaced the dominant grass sheet with Roguelike City paving while retaining deliberate green courtyards. Before accepting a future map, score its city readability; if it is below 8/10, redesign the blueprint, regenerate, check customers and decoration positions against road cells, validate the scene, and score it again. GameScene remains excluded from this workflow.

The final editor scan found zero building-road overlaps in both scenes. NarrowDistrict has 4,981 road cells, 257 main-road marking cells and 0 diagonal cells; Expressway has 5,052 road cells, 269 main-road marking cells and 46 diagonal ramp cells. The package source is stored under `StoreAssets/Kenney` for manual inspection. Future traffic loop routes remain intentionally deferred.

## Sidewalk RuleTile and ground palette rules — 2026-09-10

The generated map stack remains split into sibling Tilemaps: Ground Tiles, Terrain Tiles, Road Edge Details, Road Rule Tiles, Diagonal Road Details and Road Markings. The road and sidewalk layers no longer contain project-specific RuleTile implementations; assign manually authored RuleTiles when rebuilding a secondary map.

The daytime Roguelike City ground family is 703/704/705/709. Tile 706 is not used as a MeadowVillage ground accent because its beige color reads as an unrelated yellow singleton when mixed into the gray paver surface. Ground variation is clustered in four-cell regions. The two secondary scenes were rebuilt with package sidewalk families and passed Unity scene validation with zero issues.
