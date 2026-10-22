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
| Character (milestone 1) | ~~Starter Assets third person~~, replaced 2026-10-22 by the first-person bodycam (3B) |
| Licensing | ~~CC0 art only~~, widened 2026-10-22 (3B): CC0 art + MIT (Rocketbox) + royalty-free audio + the **OSM ODbL credit** |
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

## 3A. Look pass: real Toronto, Cyberpunk 2077-style rendering (locked 2026-10-22)

**Goal:** present-day Toronto with real streets and buildings, no invented sci-fi, rendered the way Cyberpunk 2077 renders Night City: a rainy night, wet reflective streets, neon, lit windows, fog, ray tracing. It must not read as blocky.

| # | Layer | What | How (HDRP) |
|---|---|---|---|
| L1 | PBR materials | Approved CC0 sets (3A.1) on roads, sidewalks, plaza, brick and tower facades | HDRP Lit: BaseColor + Normal + packed MaskMap (metallic/AO/smoothness), world-metre UVs, pixel displacement on brick and paving |
| L2 | Night sky | `kloppenheim_07_puresky` (overcast, city-glow sky) | HDRI Sky, fixed exposure tuned for night, no sun; a dim cool moon directional light |
| L3 | Wet streets | Rain-soaked asphalt and pavement with puddles | High smoothness, darker albedo, puddle mask in the material, rain particles and splash decals, streak normals on glass |
| L4 | Ray tracing | Reflections that mirror the neon in puddles and glass | DX12, HDRP ray-traced reflections; RT shadows/GI optional by cost; screen-space fallback for non-RT GPUs |
| L5 | Lit windows | Window grids glow at night (random lit/dark/coloured) | Emissive mask from the facade texture's window areas plus per-building random seed |
| L6 | Neon and signage | Glowing sign bands on shopfronts; the real Yonge-Dundas Square LED billboards as huge emissive screens | Emissive meshes generated at ground-floor height on storefront facades; billboard quads placed at the square's real billboard spots |
| L7 | Less boxy buildings | Floor ledges, cornices, parapets, rooftop units, setbacks from OSM parts | CityGenerator adds trim geometry by height class |
| L8 | Atmosphere | Volumetric fog, bloom, ACES tonemapping, teal/magenta grade, film grain, lens flares, light shafts | Global Volume profile `NightRain.asset` |
| L9 | Street life props | Streetlights, traffic lights, Dundas streetcar poles and overhead wires, signs | Generated along roads; CC0 models where they exist, simple modelled meshes otherwise |

Realistic expectation: CDPR built Night City with hundreds of artists. We aim for the same mood (rain, neon, reflections, fog), not the same density of detail.

### 3A.1 Approved CC0 assets (Hamzah 2026-10-22)
| Use | Asset | Source | Size |
|---|---|---|---|
| Road | `asphalt_02` | Poly Haven 2K | 14.0 MB |
| Sidewalk | `brushed_concrete_03` | Poly Haven 2K | 17.4 MB |
| Yonge-Dundas Square paving | `PavingStones138` | ambientCG 2K | 14.5 MB |
| Low-rise brick | `brick_wall_001`, `brown_brick_02` | Poly Haven 2K | 12.0 MB |
| Tower facades | `Facade001`, `Facade005`, `Facade006`, `Facade009` | ambientCG 2K | 30.5 MB |
| Night sky | `kloppenheim_07_puresky` (backup `kloppenheim_02_puresky`) | Poly Haven 4K HDR | 39.5 MB |

Downloads go through `Tools/assets/fetch_cc0.py` (raw zips cached in `Tools/assets/cache/`, gitignored); textures land in `Assets/City/Textures/<asset>/` (LFS).

## 3B. Bodycam presentation, realistic NPCs, audio (locked 2026-10-22)

Hamzah's brief: "BodyCam style, extremely realistic with great audio... a grimy RPG." The look reference is bodycam-shooter footage: first person, raw and unstable, as if recorded.

| Topic | Decision |
|---|---|
| Whose camera | **Malik's chest-mounted action cam.** He films or livestreams his life, so the bodycam view exists in the story. Story unchanged. |
| Perspective | First person only. Replaces the Starter Assets third-person placeholder (no Asset Store step needed). |
| NPCs | **Microsoft Rocketbox** (MIT, 115 rigged realistic humans) for now; hero characters (Ty, Ma, Malik's hands) upgraded later |
| Audio | **Royalty-free allowed**: Sonniss GDC bundles (free, commercial use) + Freesound CC0. Every file is listed in CREDITS.md. |
| Licensing rule (updated) | CC0 art, plus MIT (Rocketbox), plus royalty-free audio, plus the OSM ODbL credit. No CC-BY-NC, no "personal use only". |

### 3B.1 Bodycam camera (task 6)
- **Controller:** CharacterController, Input System actions from the template (`InputSystem_Actions`): walk 1.4 m/s, jog 3.2, sprint 5.5, crouch.
- **Mount:** chest height 1.35 m, 0.15 m in front of the body. The camera *lags* the look input (spring-damped yaw/pitch), so turns feel heavy and the frame drifts like a strapped-on camera.
- **Motion:** footstep-phase bob (vertical 3–5 cm, lateral 2 cm), roll sway 1–2°, landing and stop impulses, micro-jitter. Every amount is a tunable field.
- **Lens:** wide FOV (~100° horizontal), barrel distortion (HDRP Lens Distortion), stronger vignette, chromatic aberration and film grain than the free-camera look, motion blur on.
- **Exposure:** automatic histogram with fast adaptation, so highlights bloom out and dark alleys crush the way a cheap sensor does.
- **Recording artefacts:** an HDRP Custom Post Process with light block compression, rolling noise, and occasional frame hitches.
- **Overlay:** date/time timestamp, REC dot, a generic device ID. **No real camera or police brand names.**
- **Rain follows the camera:** a runtime script moves the rain mesh with the player and scrolls it downward (wraps every `RainBoxHeight`).

### 3B.2 NPCs (task 7)
- Source: `microsoft/Microsoft-Rocketbox` on GitHub (MIT). The download list (which avatars, which animations, sizes) needs Hamzah's OK first.
- First pass: about 10 pedestrians on the Yonge-Dundas sidewalks with NavMesh walking, idle loops, umbrellas or hoods for the rain, and LOD + distance culling.
- Later: reactions to the player (look-at, step aside, flinch at running), dialogue hooks into the ported dialogue system.

### 3B.3 Audio (task 8)
- Ambience: layered rain (on pavement, on awnings, in gutters), distant traffic, streetcar bells and squeal on Dundas, sirens, crowd walla near the square.
- Player: footsteps by surface and wetness, clothing rustle, breathing when sprinting, camera-handling bumps.
- **Body-mic sound:** an AudioMixer chain (compressor, band-pass ~150 Hz–8 kHz, light saturation, wind-buffet noise) so the whole mix sounds recorded on a chest mic.
- Spatial: HDRP scene reverb zones (street canyon vs alley vs indoors).
- Download list needs Hamzah's OK first (same rule as textures).

## 3C. Menu, settings, save files (built 2026-10-22)

- **Flow** (`Assets/Scripts/Game/GameFlow.cs`): the game opens on the **main menu** over a slow camera drift up rainy Yonge (`MenuCamera`); **Esc** in game opens the **pause menu** (time stops); Esc/Back steps out of sub-screens.
- **Main menu:** title LIFE IN / DOWNTOWN / TORONTO; Continue (newest save, greyed when none), New Game, Load Game, Settings, Quit.
- **Pause menu:** Resume, Save Game, Load Game, Settings, Quit to Main Menu, Quit to Desktop.
- **Saves** (`SaveSystem.cs`): 3 manual slots + 1 autosave (written when quitting from the pause menu), JSON in `%USERPROFILE%\AppData\LocalLow\Hamzah Muhammad\Life In Downtown\saves\`; write-then-rename so a crash never leaves half a file; a corrupt file reads as empty. Each save holds position, look, playtime and `GameState` (name, REP/HEAT/LOYALTY/CASH, flags).
- **Settings** (`GameSettings.cs`, `settings.json` beside the saves, applied live): graphics quality (the 3 HDRP presets), resolution, display mode, V-Sync, ray-traced reflections, motion blur, field of view (80-110° horizontal), camera shake (0-1.5), mouse sensitivity, master volume.
- **UI:** UI Toolkit, `Assets/UI/Menu.uxml` + `Menu.uss` (palette from the icon), panel scales by screen height.
- **Overlay change (Hamzah):** top-left device ID and REC dot removed; only the timestamp stays, hidden in menus. **Camera shake** cut roughly in half (bob 4.0 → 2.5 cm, jitter and roll halved) plus the settings slider.
- Tests: `SaveSettingsTest.Run` (9 checks, temp folder only), `PlayerSimTest.Run` (7 checks).

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
- Windows build: `-batchmode -quit -projectPath <proj> -executeMethod LifeInDowntown.EditorTools.BuildTool.BuildWindows -logFile <log>` writes `Builds/Windows/LifeInDowntown.exe` (gitignored, ~310 MB, ~4 min). Desktop shortcut `Life In Downtown.lnk` points at it. The exe is a snapshot, so rebuild after changes. Esc quits.
- Player test: `-batchmode -nographics -quit -projectPath <proj> -executeMethod LifeInDowntown.EditorTools.PlayerSimTest.Run -logFile <log>` (SIM_PASS/SIM_FAIL lines, exit 1 on failure)
- Map data: `python Tools/osm/fetch_osm.py`, then `python Tools/osm/build_city_json.py`

## 8. Task list for milestone 1
| # | Task | Status |
|---|---|---|
| 1 | Project created from the HDRP template, repo, LFS, docs | done 2026-10-22 |
| 2 | OSM fetch + city JSON (`Tools/osm/`) | done 2026-10-22: 290 buildings (43 height tags, 219 from levels, 56 default 12 m), 113 roads, 173 paths, 9 plazas |
| 3 | CityGenerator: buildings, roads, sidewalks, ground | done 2026-10-22 (grey-box; sidewalks are 2 m slabs, gaps at bends) |
| 4 | ShotTool + first screenshots (grey materials) | done 2026-10-22: 4 poses render in batch mode on the GPU |
| 5 | Look pass L1 (PBR materials) | done 2026-10-22: 9 CC0 sets, packed MaskMaps (`Tools/assets/pack_masks.py`), walls/roof submeshes, facade picked by height + OSM id, `area:highway` road polygons render as asphalt. Open: PavingStones138 is mossy cobblestone, so the square uses concrete until a granite-slab set is approved; pixel displacement not on yet |
| 5b | Look pass L2 + L8 (night sky, exposure, fog, post) + streetlights | done 2026-10-22: `NightLook.cs` builds `Assets/City/Look/NightRain.asset` (HDRI sky, fixed EV 1.5, volumetric fog, SSAO, bloom, ACES, blue/magenta split tone, grain, vignette, CA); template sun is now a dim 8500 K moon; 234 staggered 8 m poles with 7000 lm 3200 K spots. Open: a dark seam runs along Yonge between the road ribbon and a road-area polygon; window glow waits for 5d |
| 5c | Look pass L3 + L4 (rain, wet streets, ray-traced reflections) | done 2026-10-22: `RainLook.cs` turns on ray tracing + SSR in all 4 HDRP quality assets (renders confirmed `Direct3D12 rayTracing=True`), RT reflections in the profile, wet smoothness remaps on street materials, 180 puddle decals, and a 9,000-streak rain mesh (a ParticleSystem simulated but never drew in batch renders). Open: rain mesh must follow + scroll with the player camera at runtime (task 6); no splashes yet |
| 5d | Look pass L5 + L6 (lit windows, neon, square billboards) | todo |
| 5e | Look pass L7 + L9 (building trim, street props) | todo |
| 6 | Bodycam first-person player (3B.1) + rain follow | done 2026-10-22: `Assets/Scripts/Player/` (BodycamPlayer, BodycamRig, RainFollow, BodycamOverlay), `Assets/Editor/PlayerSetup.cs` (spawn on the Yonge sidewalk south of Dundas, `Bodycam.asset` volume: barrel lens, motion blur, heavy vignette/CA/grain/bloom), `PlayerSimTest` 7/7 pass (grounded, jog 3.20, sprint 5.50, bob 4.0 cm, never crosses a wall). Not yet: live play-mode feel check by Hamzah, compression-artefact post process, auto exposure (fixed EV kept so shots stay comparable) |
| 7 | Rocketbox NPCs (3B.2) | todo (download list needs OK) |
| 8 | Audio + body-mic mix (3B.3) | todo (download list needs OK) |
| 9 | Playtest pass with Hamzah | todo |
| 5d/5e | Remaining look passes (neon, lit windows, trim, props) | after 6-8 |

## 9. Open questions
- Where in downtown does Malik's family live?
- Do we keep the name Malik for this game?
