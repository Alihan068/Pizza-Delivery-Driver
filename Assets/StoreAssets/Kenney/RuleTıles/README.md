# Kenney RuleTile Sets

These assets use Unity's standard `RuleTile` from `com.unity.2d.tilemap.extras`. They do not use project-specific RuleTile scripts.

The sets are grouped by source package:

## RoguelikeCity

- `RoguelikeCity_RoadBase.asset` — 11 rules using the continuous asphalt body (`tile_0714`). The rules follow the full 3×3 pattern; the asphalt body stays visually continuous because the source provides one road body sprite.
- `RoguelikeCity_RoadMarkings.asset` — optional overlay for horizontal, vertical and intersection marking sprites (`tile_0749`–`tile_0752`). Paint this only on a dedicated marking Tilemap.
- `RoguelikeCity_GrassToDirt.asset` — grass interior variation plus north, east, south, west and corner dirt transition sprites (`tile_0963`–`tile_0978`).
- `RoguelikeCity_GreyPaving.asset` — grey paver interior variation from one cohesive family (`tile_0740`–`tile_0742`) with a lighter edge output (`tile_0748`).

## RPGUrban

- `RPGUrban_PavedGround.asset` — urban paved-ground interior and boundary rules using `tile_0008`–`tile_0015`.
- `RPGUrban_RoundedRoad.asset` — rounded road boundary and corner rules using `tile_0437`–`tile_0441`.
- `RPGUrban_PurpleSidewalk.asset` — purple sidewalk/boundary family using `tile_0081`–`tile_0088`.
- `RPGUrban_ConcreteSidewalk.asset` — concrete sidewalk/boundary family using `tile_0089`–`tile_0096`; the complete raw family also contains `tile_0116`–`tile_0123`, including the wallless interior `tile_0117`.

## Authoring conventions

Every set follows the same 11-rule 3×3 template as `RPGUrban_PurpleSidewalk.asset`: isolated, end, edge, corner, interior and straight cases are expressed with cardinal and diagonal neighbor positions. `This` is serialized as `1` and `NotThis` as `2`. Directional rules use fixed, rotated or mirrored output transforms, and every rule has a unique serialized id. This template is intended for 2×2 and larger painted regions; single-cell strips do not provide enough context for all edge cases.

Use these on separate sibling Tilemaps in this order:

1. Ground or paved ground
2. Road base
3. Sidewalk or road boundary
4. Optional road marking/details overlay

The raw sprites are organized visually inside each package's `Tiles` folder. For manual authoring of the next versions, use these empty source folders:

- `../RPGUrban/Tiles/Sidewalk/Purple/RuleTıles`
- `../RPGUrban/Tiles/Road/Asphalt/RuleTıles`
- `../RoguelikeCity/Tiles/Sidewalk/GreyPaving/RuleTıles`
- `../RoguelikeCity/Tiles/Road/Asphalt/RuleTıles`

The four source packages also contain material-specific `Road`, `Sidewalk`, `Ground` and `Terrain` families, plus `Road/Overlays`, `Terrain/Transitions`, `Nature`, `Props`, `Vehicles`, `Characters` and `Architecture`. The surface folders were assigned by visible color, texture and role from the tilemap previews; the stock `tile_####` filenames were not used as the classification rule. Surface folders have flat interiors suitable for a RuleTile default. `Road/Overlays` and `Terrain/Transitions` are supplemental edge/marking families, not standalone base surfaces. These RuleTiles are ready to inspect, edit and add to a Tile Palette manually. They are not assigned to any scene by this pass.

For RPG Urban, use `Ground/PurplePaving/Lavender`, the four `Ground/RedPaving` folders, `Ground/OrangePaving/OrangeBlueTrim`, `Sidewalk/Purple`, `Sidewalk/Concrete`, `Terrain/Water/Teal` and `Terrain/Water/BlueLavender`. The matching `RPGUrbanPack` paths use the same layout. `RPGUrban_PurpleSidewalk.asset` remains unchanged.
