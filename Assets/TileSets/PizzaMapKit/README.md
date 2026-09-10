# Pizza Map Kit

This folder contains the reusable authoring kit for the built-in and future Pizza Delivery Driver maps.

Use memory-bank/pizza_map_authoring.md for the full workflow and scene contract.

## Palette groups

PizzaMapTilePalette.prefab is organized into four adjacent material groups:

- 01 Ground Materials
- 02 Road Rule Materials
- 03 Road Details
- 04 Terrain Materials

City, night, and village materials are kept next to their related theme. The road groups use PizzaRoadRuleTile, which derives its straight, corner, tee, cross, end, and single-cell output from neighboring road cells.

## Rebuild workflow

Edit a PizzaMapBlueprint under Assets/MapBlueprints, then use:

- PizzaGame > Map Authoring > Open Map Authoring Window
- Rebuild Scenes From Existing Blueprints

The builder writes six scene tilemap layers: Ground Tiles, Terrain Tiles, Road Edge Details, Road Rule Tiles, Diagonal Road Details and Road Markings. It places non-tile decorations from blueprint stamps and preserves the gameplay shell and owner-authored collection-point collider overrides.

The built-in secondary maps use the GreenSedan footprint as their scale reference. Local roads are two cells wide and broad routes are seven cells wide, but both values are ordinary blueprint data and can be changed for future maps. The generated detailed asphalt sprites and theme accent/shoulder textures are point-filtered and remain grouped with their matching material family.

Bulk generation skips the protected primary `GameScene` by default. Map authors should work in NarrowDistrict, Expressway or a new scene/blueprint; rebuilding GameScene requires the explicit destructive Inspector toggle.

## Current secondary map composition — 2026-09-10

NarrowDistrict is 128×88 and Expressway is 144×96. Both use the GreenSedan as the scale reference, center the Grid around world (0,0), and use broad seven-cell routes with two-cell local streets. The current blueprints use four grass/ground families, dirt accents and irregular softened zone edges; Expressway also has one small authored pond beside a field. The large unexplained default water region was removed.

The generated decoration family contains three related top-down houses, parks, fountains, trees, shrubs, flower beds, benches and lamps. Customer markers sit beside houses or parks, while the PizzaPlace and distant roadside PizzaCar remain off-road service locations with their collection-point prefab children. A final editor check confirms no customer cell or decoration sprite bound overlaps a drivable Road Rule Tile.

PizzaMapDecorationFactory is idempotent: it repairs existing generated prefabs by resolving and assigning the imported Sprite sub-asset before saving. This keeps decorations visible after Unity reimport or domain reload and makes the kit safe to regenerate.

PizzaRoadRuleTile remains the route topology layer. Diagonal Road Details is a separate transparent overlay for diagonal strokes, and Road Markings selects single markings for narrow strokes and double markings for broad strokes. MapCameraFramingOverride widens the view only on the secondary maps. Future city traffic loop routes are recorded but are not implemented by this kit.

## Diagonal marking and map review rules — 2026-09-10

Diagonal Road Details uses a short centered dash texture and a direction-derived tile rotation. Straight horizontal/vertical markings do not process diagonal segments. When a blueprint route changes, review both a zoomed diagonal segment and the full city composition.

The current review scores are NarrowDistrict 8.2/10 and Expressway 8.4/10. Expressway reached the passing score after replacing its repeated block silhouette with winding asymmetrical village roads, pond/field and dirt regions, and a separate decoration layout. Before accepting a future map, score its city readability; if it is below 8/10, redesign the blueprint, regenerate, check customers and decoration positions against road cells, validate the scene, and score it again. GameScene remains excluded from this workflow.
