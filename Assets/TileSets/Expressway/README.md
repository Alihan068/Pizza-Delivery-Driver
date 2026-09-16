# Expressway

Dense asphalt city with 58 irregular parcels, 269 low city buildings, 60 distant buildings, 7 parking courts and 5 pocket plazas. Main/perimeter roads are 7 cells wide; local links are 3. Four controlled exits continue beyond the visible boundary.

Five Tilemaps contain base surfaces. Buildings, cars, barriers and furniture are independent objects. The western shop is the player spawn and primary pickup; three remote restock cars serve outlying areas. There are 90 delivery locations. Moving traffic is not implemented.

Prefabs/ contains four commercial/warehouse families. Tiles/ contains surface tiles. Art/ holds map-local normalized service artwork. Other artwork reuses the project's existing Kenney sprites and low-building prefabs without editing those source assets. Preview.png is assigned to the map-selection asset.

The scene is authoritative. Keep preserveAuthoredScene enabled on MapBlueprint_MeadowVillage; its data is an overview, not a complete reconstruction of all scene objects.

## Verification Report
- Unity Version: 6000.0.62f1
- Compilation: In-memory authoring and validation passed; no runtime source changes.
- Edit Mode checks: Road connectivity 18325/18325; interaction access 95/95 with radius 0.65 on a 0.5-unit grid; spawn clear.
- Geometry: 269 city buildings; zero building-road/pavement overlap; zero building pairs; zero solid decor-road overlap; zero missing scripts.
- Unity Test Runner: Not Run.
- Play Mode: Not Run.
- Performance / GC: Not Measured.
- Last console check: zero errors; one empty localization-key warning.
- Preservation: NarrowDistrict and GameScene hashes matched their initial snapshots.
- Final save: fifteen Point/16 local-art renderer remaps, detailed parks and refreshed preview saved on 2026-09-12.

Visual composition review: 8/10 after density, surface, lighting, signage and parking corrections. This is not a gameplay test result. Full driving-shift validation remains open.


ParkDetails/ contains five reviewed close-ups. Parks now contain 367 independent sprite pieces, two fountains, picnic furniture, benches, lamps, bins, flower beds, hedges and tree/low-planting groups. Vehicle-clearance access remains 95/95; park sprites do not overlap traffic-road cells.

## Lawn geometry and middle parking rows — 2026-09-12
Corrected all five parks to four simple rectangular lawns with continuous crossing paths. Removed picnic-pad notches and stepped fountain lawn outlines; fountains retain wide surrounding paving. Moved 87 tree/shrub/plant accents back onto grass after repainting.
Added two back-to-back middle parking rows to three sufficiently deep lots (21/23 cells deep): 22 additional bays and 15 additional parked cars. Totals now 80 bays and 60 ordinary parked cars, plus three restock cars. Four shallower lots remain unchanged because two extra rows would remove necessary maneuvering aisles.
Verification Report: Unity 6000.0.62f1, in-memory compilation passed; geometric access 95/95 gameplay targets and 22/22 new bay approaches at radius 0.65 on 0.5-unit samples. Spawn unblocked. Scene and refreshed overview/parking/five park images saved. Play Mode/Test Runner Not Run; performance/GC Not Measured. No runtime source changes or commit.
Lesson: define lawn beds and paths as coherent regions before furniture placement. Do not cut isolated paved squares into beds for each picnic table; use grass seating or an intentional continuous terrace. Place added parking rows only after reserving side access and both drive aisles.

## Expressway alley and courtyard correction — 2026-09-12

Owner requested uncluttered building passages, mixed narrow/wide gaps, a non-asphalt/non-grass courtyard surface, and less repeated market furniture. Applied directly to Expressway only. Moved 124 building instances; all 269 city buildings remain. Removed 585 repeated produce crates, picnic tables and planters, plus 101 remaining street props intersecting reserved passages. Replaced 9068 courtyard/entry cells with gray brick paving (Courtyard_Paving.asset, inspected complete brick fill 0191). Roads, parking and designated parks retain their surfaces.

Inspected City Pack, Gutty Kreum Japanese City, 16-bit top-down World atlas and Admurin General/Miscellaneous sheets. Admurin contains primarily inventory items; suitable small repair/toy items are used on 10 shop displays rather than scattered at world scale. Added 114 wall-adjacent service objects using metal/recycling bins and three vending designs (including 38 Japanese vending variants), 54 roof utility/extractor pieces, and replaced 10 existing bins with hydrants. Map-local atlas/item copies use Point, 16 PPU, no mipmaps and uncompressed import. Original package imports were not changed.

Validation: 217 reserved gap rectangles have zero decoration intersections. Radius-0.65 / 0.5-unit-grid access reaches 95/95 gameplay targets, spawn clear. 63/71 wider-gap centers reachable with this conservative circular clearance; the other eight are reached at radius 0.32 through narrower entries. This is a sampled geometry check, not a claim of swept-car turning validation. Current vehicle widths are approximately 0.656 (sedan) and 0.598 (scooter), so narrow routes cannot honestly be labeled strictly scooter-only without live driving evidence. No vehicle physics/collider changes were made. All 269 buildings have zero building/building and road/pavement overlaps. Missing scripts and enabled missing sprites: zero.

Expressway scene saved and Preview.png/StreetDetail.png refreshed and reviewed. NarrowDistrict hash 85E0EB03419B5C8DF288B35BAC8D8447DF27AE6D42FF997CCC98C0DC6EB0B3C5 and GameScene hash 3A5B73CF12DD6CD74BDD3AACE8C782448CD97C874B2755DB002CDB5FA4D31ACD unchanged. No commit.

Verification Report: Unity 6000.0.62f1. In-memory authoring/validation compilation Passed; runtime code unchanged. Edit Mode Test Runner Not Run for this task (custom geometry checks above). Play Mode Not Run. Console contains 2 error/exception entries and 6 warnings from the existing save-recovery/Test Runner session; none references the map changes. Performance Impact Not Measured. GC Allocation Impact Not Measured. Serialized Asset Integrity Verified by live reference checks and successful scene/asset save. Owner driving/acceptance checks remain open.

Prevention: reserve actual building gaps before placing furniture. Global target access alone does not prove all courtyards are reachable by a car. Keep designated parks separate from service courtyards; avoid repeated produce/table groups behind every building. Use measured vehicle envelopes when classifying narrow routes.
