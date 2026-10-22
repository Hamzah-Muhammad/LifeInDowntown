# Life In Downtown: Design

The source of truth for what we build and in what order. Each session updates the **Status** line of the task it touches.

## 1. Concept

- Third-person RPG set in real downtown Toronto. GTA-style open world, choice-driven story.
- The story carries over from LifeInTheCityRPG (Godot): Malik, 18, Pakistani/Arab, rises from small-time dealing to a King West penthouse, or ends in Prison, Dead or Out Clean. Four hidden stats decide it: **REP / HEAT / LOYALTY / CASH** (start cash $40). Ty is the friend who pulls him in. The tone is The Wire / Top Boy, with real Toronto slang and TTC references.
- Unlike the Godot game, this one starts **downtown**. Where Malik's family lives downtown (for example a St. James Town or Regent Park tower) is an open story decision (section 9).
- **Goal: as close to real life as possible.** That means real street layout and building heights from map data, photoreal materials, and realistic night lighting.

## 2. Locked-in decisions (2026-10-22)

| Topic | Decision |
|---|---|
| Engine | Unity **6000.3.26f1** (6.3 LTS) |
| Render pipeline | **HDRP** 17.3 (template `3d-high-end`) |
| City source | **OpenStreetMap** footprints, heights and roads, generated into meshes by our own tools, skinned with photoreal materials |
| Rejected | Google Photorealistic 3D Tiles (one melted scan you can't enter or edit, needs an API key, and the terms restrict shipping it) |
| Character (milestone 1) | Unity **Starter Assets: Third Person** as a placeholder; a realistic Malik comes later |
| Licensing | **CC0 art only** (Poly Haven, ambientCG, and similar), plus the required **OSM ODbL credit** (CREDITS.md and the in-game credits) |
| Input | Unity Input System (new) |
| Repo | `C:\Apps\Games\LifeInDowntown`, public GitHub `Hamzah-Muhammad/LifeInDowntown`, Git LFS for binaries |
| Verification | Rendered screenshots from batch mode (section 6), then Hamzah eyeballs the editor or game |

## 3. Milestone 1: walk real downtown blocks at night

**Done when:** a player walks a third-person character around the real blocks near **Yonge & Dundas** at night, with realistic lighting, collision on buildings and curbs, and no falling through the world, and the screenshot tool shows it.

### 3.1 Map area
- Bounding box (about 550 m × 450 m): **lat 43.6535 to 43.6585, lon −79.3845 to −79.3775**. That covers Yonge St from Shuter to Gerrard, Dundas from Bay to Church, Yonge-Dundas Square and the Eaton Centre's north end.
- World origin (0,0,0) is the **Yonge & Dundas** intersection node (found by street name in the data, not hard-coded).
- Projection is local equirectangular: `x = (lon − lon0) · 111320 · cos(lat0)`, `z = (lat − lat0) · 110540`, in metres. Unity +X is east and +Z is north. The error is under 0.1% at this size.

### 3.2 Data pipeline (`Tools/osm/`)
1. `fetch_osm.py` (Python) queries the **Overpass API** once for buildings, `building:part`, highways, footways and landuse in the bounding box, and writes the raw result to `Tools/osm/cache/` (gitignored).
2. `build_city_json.py` projects the data to metres, picks a height for each building, classifies roads, and writes **`Assets/City/Data/downtown_yonge_dundas.json`** (committed, so builds never need the network).
   - Height order: `height` tag, then `building:levels × 3.5 m`, then a 12 m default. It logs how many buildings used each source.
   - Road width by `highway` type (primary 14 m, secondary 12 m, tertiary 10 m, residential 8 m, service 5 m), or the `width`/`lanes` tags when present.
3. Unity editor tool `CityGenerator` (C#, `Assets/City/Editor/`) reads the JSON and builds:
   - Buildings: extruded footprints (walls plus a flat roof), triangulated with ear clipping, one GameObject per building, a MeshCollider, and a facade material chosen by height (tower glass, mid-rise brick, low-rise storefront).
   - Roads: ribbon meshes along each road's centre line at road width, plus 0.15 m sidewalks with curbs on both sides.
   - Ground: a fill plane under everything.
   - Output goes into the scene `Assets/Scenes/Downtown_YongeDundas.unity` under a `City` root, regenerated on demand, never hand-edited.

### 3.3 Look (HDRP)
- Night: HDRI sky (Poly Haven night city or dusk HDRI), physically based exposure, volumetric fog, a moonlight directional light.
- Streetlights: one every ~35 m along each road side, an HDRP point/spot light with a CC0 pole model or a simple modelled pole.
- Materials: CC0 PBR sets from Poly Haven / ambientCG for asphalt, concrete sidewalk, brick, curtain-wall glass and storefront. Every source is listed in CREDITS.md.
- Lit windows: the facade shader adds random emissive window cells at night.

### 3.4 Player
- Starter Assets Third Person controller and camera (Cinemachine) on the sidewalk outside Yonge-Dundas Square.
- The Asset Store download needs "Add to My Assets", which accepts Unity's Asset Store terms. Hamzah approves that step himself.

## 4. Later milestones (not scheduled)
1. Port the Godot game's core systems to C#: main menu with character creation, dialogue engine with `{name}`/pronoun templating, save/load, pause and options, the GameState stats.
2. Realistic Malik and NPCs, plus pedestrians and traffic.
3. More downtown: Union / Financial District, King West, Kensington / Chinatown, TTC Line 1 stations.
4. Missions, combat, endings.

## 5. Project layout
```
Assets/City/Data/         generated city JSON (committed)
Assets/City/Editor/       CityGenerator and related editor tools
Assets/City/Materials/    facade, road, sidewalk materials
Assets/City/Textures/     CC0 texture sets (LFS)
Assets/Scenes/            Downtown_YongeDundas.unity
Assets/Scripts/           runtime game code
Assets/Editor/            batch-mode tools (screenshots)
Tools/osm/                Python map pipeline
docs/                     this file
```

## 6. Screenshot verification
- `Assets/Editor/ShotTool.cs`: a static `ShotTool.Run`, called with `-batchmode -executeMethod ShotTool.Run` **without** `-nographics`, so HDRP renders on the GPU. It opens the scene, renders a list of named camera poses to `Screenshots/<pose>.png` (gitignored), and quits.
- The poses cover a street-level view up Yonge, the Square, an overhead map view, and the player spawn.
- Every visual change is checked with these shots before Hamzah looks.

## 7. Commands
- Open or create: `"C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe" -projectPath C:\Apps\Games\LifeInDowntown`
- Generate the city: `-batchmode -quit -projectPath <proj> -executeMethod CityGenerator.GenerateFromCommandLine -logFile <log>`
- Screenshots: `-batchmode -quit -projectPath <proj> -executeMethod ShotTool.Run -logFile <log>`
- Map data: `python Tools/osm/fetch_osm.py`, then `python Tools/osm/build_city_json.py`

## 8. Task list for milestone 1
| # | Task | Status |
|---|---|---|
| 1 | Project created from the HDRP template, repo, LFS, docs | done 2026-10-22 |
| 2 | OSM fetch + city JSON (`Tools/osm/`) | done 2026-10-22: 290 buildings (43 height tags, 219 from levels, 56 default 12 m), 113 roads, 173 paths, 9 plazas |
| 3 | CityGenerator: buildings, roads, sidewalks, ground | done 2026-10-22 (grey-box; sidewalks are 2 m slabs, gaps at bends) |
| 4 | ShotTool + first screenshots (grey materials) | done 2026-10-22: 4 poses render in batch mode on the GPU |
| 5 | CC0 materials + HDRI + night lighting + streetlights | todo (asset list needs Hamzah's OK) |
| 6 | Starter Assets player in the scene | todo (Hamzah adds it to his Unity account) |
| 7 | Playtest pass with Hamzah | todo |

## 9. Open questions
- Where in downtown does Malik's family live?
- Do we keep the name Malik for this game?
