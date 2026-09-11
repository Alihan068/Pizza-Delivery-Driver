# Kenney 2D Source Assets

This folder contains the imported Kenney source packages. The package folders remain separated so each source set can be inspected and turned into project-specific Tilemaps or RuleTiles manually.

- `RPGUrban` — RPG Urban city tiles and source sample files.
- `RPGUrbanPack` — the companion RPG Urban package files.
- `RoguelikeCity` — Roguelike City tiles and source sample files.
- `RoguelikeCityPack` — the companion Roguelike City package files.

The source files in this folder are kept separate from generated gameplay tiles and map scenes. No project-specific RuleTile definitions are stored here.

## Visual tile organization

The PNGs inside each package's `Tiles` folder are grouped by visible palette, texture and gameplay role. The stock `tile_####` names remain unchanged; the folders provide the authoring context. Surface folders contain one coherent material family and include a flat interior sprite where the source provides one.

- `Road/Asphalt` contains the complete asphalt surface family. `Road/Overlays/Edges` and `Road/Overlays/Markings` contain edge and marking overlays that are intentionally painted on a separate Tilemap and are not standalone ground RuleTiles.
- `Sidewalk` contains material-specific paving families. Roguelike City separates `CoolGray`, `WarmBeige`, `PaleBlue` and `SmoothGray`; RPG Urban keeps its purple and concrete families together with their matching trim variants so each has flat interiors.
- `Ground` contains separate visual families such as RPG Urban's lavender, four red palettes and orange paving, plus Roguelike City's red smooth surface, red brick, gray brick, beige brick and terrain-colored dirt.
- `Terrain` contains flat and transitional terrain families. Grass-to-concrete transitions are explicitly kept under `Terrain/Transitions`; they are edge sets rather than standalone terrain fills.
- `Vehicles`, `Characters`, `Nature`, `Props` and `Architecture` are decoration and structural source families, not RuleTile surface folders.

The same visual grouping is applied to all four source packages, including the Pack variants. `Architecture` is the fallback for structural or facade pieces that do not form a reusable surface family.

## RuleTile authoring folders

These are the four source locations selected for manual RuleTile authoring:

- `RPGUrban/Tiles/Sidewalk/Purple/RuleTıles`
- `RPGUrban/Tiles/Road/Asphalt/RuleTıles`
- `RoguelikeCity/Tiles/Sidewalk/GreyPaving/RuleTıles`
- `RoguelikeCity/Tiles/Road/Asphalt/RuleTıles`

They are empty authoring folders. The existing standard RuleTiles remain under `RuleTıles/`; the owner's manually corrected `RPGUrban_PurpleSidewalk.asset` was not changed.

## RPG Urban visual subfamilies

The manually useful RPG Urban families are now grouped as follows:

- `Ground/PurplePaving/Lavender` — the three compatible lavender rows, with flat interiors from each row.
- `Ground/RedPaving/RedBrickDark`, `RedBrickWarm`, `RedCoral` and `RedBrickMuted` — the four red palettes, each with its own flat interior.
- `Ground/OrangePaving/OrangeBlueTrim` — the orange rows, including the former plain top-trim row, with flat orange interiors.
- `Sidewalk/Purple` and `Sidewalk/Concrete` — each contains its base pieces and matching trim pieces, including wallless interiors.
- `Terrain/Water/Teal` and `Terrain/Water/BlueLavender` — the two water palettes, each separated by visible color and border material and each containing flat water sprites.

The same separation is present in `RPGUrbanPack`. The matching `.meta` file moves preserve the original asset GUIDs and leave the stock filenames unchanged.

## RuleTile source check

The four manual RuleTile folders remain inside the matching surface folders. For quick authoring, use the surface folder's flat interior sprite as the default output, then add its matching edge and corner pieces from that same folder. Do not use `Road/Overlays` or `Terrain/Transitions` as standalone base RuleTiles; they are supplemental topology layers.
