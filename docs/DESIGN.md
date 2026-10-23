# Life In Downtown: Design

The source of truth for what we build and in what order. Each session updates the **Status** line of the task it touches.

## 1. Concept

- Third-person RPG set in real downtown Toronto. GTA-style open world, choice-driven story.
- **New game, new story (Hamzah 2026-10-22):** unrelated to the Godot game's Malik story. The protagonist is **Tarik**, who lives alone in his own 1-bedroom condo downtown (section 3E). Tarik's story is still to be written (section 9).
- Kept from the earlier plan until the story says otherwise: the four hidden stats **REP / HEAT / LOYALTY / CASH** (start cash $40), a grimy street-level tone, real Toronto slang and TTC references.
- The phone's current cast (Ty, Ma, Baba) and the three starter missions came from the old story. They are **placeholders**: they make the systems testable and get rewritten once Tarik's story exists.
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
| Whose camera | **The protagonist's chest-mounted action cam** (now Tarik's). He films or livestreams his life, so the bodycam view exists in the story. |
| Perspective | First person only. Replaces the Starter Assets third-person placeholder (no Asset Store step needed). |
| NPCs | **Microsoft Rocketbox** (MIT, 115 rigged realistic humans) for now; hero characters (main cast, Tarik's hands) upgraded later |
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

## 3D. Phone, calls, missions (2026-10-22)

Hamzah: "pressing the phone button brings up an iPhone that you can use to call people in the game, and get calls from people to do missions."

- **Phone:** **Up Arrow or P** toggles it; slides up bottom-right in an iPhone-style body (rounded, camera pill, status bar, app grid). **No Apple name or logo.** While it's up the mouse is freed for tapping and mouse-look pauses; WASD still walks.
- **Apps:** Phone (contacts → call), Messages (texts from contacts, newest first), Jobs (active mission + objective, completed list).
- **Calls:** outgoing calls play a contact's dialogue for the current story state; **incoming calls** ring (the phone pops up, Answer/Decline). Dialogue = speaker + subtitle line + up to 3 reply choices (click or keys 1-3; Space continues).
- **Missions** (`Assets/Resources/Phone/missions.json`): trigger (incoming call after N seconds, required/blocked flags), the call's dialogue, ordered objectives (go to a **real named place**, radius, optional arrival text), rewards (cash/REP/HEAT/LOYALTY/flags). A waypoint diamond with distance is shown on screen for the active objective.
- **Places:** named OSM features (squares, subway entrances, malls, theatres, garages) are exported by `build_city_json.py` into `Assets/Resources/City/places.json`; missions reference them by name.
- **Saves:** active mission, objective, messages and call log live in `GameState`, so they're in every save slot.
- **First missions:** "Link Up" (Ty: meet at Sankofa Square, then Trinity Square), "Ma's Errand" (Ma: pick something up at the Atrium on Bay), "The Package" (Ty: drop a bag at the 300 Victoria Street garage, which you can refuse).
- Tone and story follow section 1 (The Wire / Top Boy, real Toronto slang). Places are real; nothing here points at a real shelter, church or school as a crime location.

## 3F. Map and minimap (built 2026-10-22)

Hamzah: "Make a map Grand Theft Auto style, and a circular on screen map on the bottom left."
- **Map image:** `Tools/osm/render_map.py` draws `Assets/Resources/City/map.png` (4 px/m, north up) from the city JSON: dark ground, light roads (major roads warmer), slate buildings (towers lighter), plazas, parks, street names along each street. `map_meta.json` holds its world bounds. Re-run it after the city data changes.
- **Minimap:** bottom-left circle, ~300 m across, **rotates with the camera** (heading up), centre arrow, N marker on the rim, job blip clamped to the rim when off the disc.
- **Full map (M):** pauses the game; drag to pan, scroll to zoom around the cursor, Space re-centres, M or Esc closes (Esc doesn't also open the pause menu). Shows the player arrow, the job blip, the active objective and a legend.
- The objective line moved up above the minimap.

## 3G. Store signs and building names (built 2026-10-22)

- `Tools/assets/make_signs.py` reads the real OSM places and buildings and writes `Assets/City/Data/signs.json` plus a 4096×8192 atlas (`Assets/City/Signs/signs_albedo.png` / `signs_emissive.png`, 256 slots).
- **Store signs (116):** every named shop, café, restaurant, bar, bank, pharmacy, cinema or arcade within 200 m of Yonge & Dundas gets a neon sign on the building wall nearest its OSM point, 0.85 m tall, above the shopfront, coloured by type (food orange, cafés cyan, bars magenta, shops blue, pharmacies red, banks green, entertainment purple, theatres gold). Each casts a small coloured light onto the wet street.
- **Building names (22 plates + 4 tower crowns):** named buildings get a backlit plate on the side facing the nearest road; towers over 40 m also get a lit crown sign near the top. One sign per name. "homeless shelter" is dropped from the Bond Place Hotel's OSM name.
- Long names shrink to fit instead of being clipped. The full map also shows building names.
- Real business names come from OSM; fine for a personal project, a commercial release needs parody names (section 9).

## 3H. Tarik's condo: Pantages Tower, unit 3801 (2026-10-22)

- **Where:** inside the real Pantages Tower (157.5 m, Victoria & Shuter), against its long south wall, floor 38 (y = 120 m). The tower's facade renders only from outside, so the condo's floor-to-ceiling glass looks out over the generated city. Built by `CityGenerator.Condo.cs` (`BuildCondo`), which checks every room corner lies inside the real footprint.
- **Layout (10 m × 7.5 m, 2.75 m ceilings):** open living/kitchen along the glass, kitchen with island on the back wall, bedroom (right, by the window), bathroom (right, at the back) with vanity, toilet and a glass shower, front door on the back wall.
- **View:** south over the Financial District. A seeded ring of ~300 distant lit towers (denser and taller to the south) and a floodlit **CN Tower at its real position** (~0.5 km west, 1.5 km south) with the pod ring and red beacon. Inside, a local volume turns the rain fog off (it washed the skyline into flat slabs) and sets an interior exposure (EV 4.2).
- **Getting in/out:** a lit glass entrance on Shuter St; **[E]** at it rides the "elevator" up (fade to black), and **[E]** at the condo's front door rides down (`World.DoorLink` + `World.Interactor`). No rain indoors. **New Game starts in the condo**, facing the window; saves store the position, so loading puts you back inside.
- **Gotcha:** HDRP tiles the emission map separately from the base map; with metre UVs it must get the same scale or windows glow as a flat sheet.
- **Furniture (2026-10-22, approved):** 12 Poly Haven CC0 models + oak laminate, marble, tile, black oak and fabric textures. The download was **215 MB, not the 93 MB quoted**: the estimate used glTF sizes, the FBX bundles carry EXR normal maps. `CondoFurnitureImport` packs HDRP masks in Unity (it reads the EXR maps), folds leaf opacity into the colour map, builds HDRP materials per slot, and saves `Assets/Condo/Prefabs/`. `CityGenerator.Condo.Furniture` places each piece from its measured bounds (floor / ceiling / wall height). All seating faces +Z; `ReportFacing` measures that from the highest backrest vertex.

## 3I. Sound (2026-10-22)

Hamzah: "Let's add sound. I want to build the world more before we start making specific missions." **Working order: world-building first, specific missions later.**

- **Sources:** **all sound is generated in code for now** (Hamzah 2026-10-22: "let's just generate our own sounds from code rn"): `Tools/assets/make_sounds.py` writes every clip, ours, CC0. Freesound CC0 recordings stay a later option (the key file `Tools/assets/freesound_key.txt` is already gitignored); a recording named like a synth clip replaces it with no code change.
- **Ambience** (`Game/AudioDirector`): outdoor = rain bed + city hum (traffic swells) + occasional streetcar bells and distant sirens placed in 3D around the player; indoor (condo) = muffled rain on glass + room tone with a 60 Hz fridge/HVAC hum. Crossfades in about 1 s on the elevator ride.
- **Player:** footsteps on each step (BodycamPlayer.Footstep), wet concrete outside / wood inside, random pitch and volume; heavier when sprinting. Synth placeholder until the Freesound set lands.
- **Phone:** original ringtone and message ping (not any real phone's sounds); elevator chime on rides.
- **Body-mic sound** (`Player/BodyMicFilter` on the listener): the whole mix goes through a chest-mic chain: high-pass ~150 Hz, low-pass ~8 kHz, compressor, light saturation, and wind buffet that grows with speed outdoors. Strength is a setting (later).

## 3J. City detail: windows, light, balconies, streets (2026-10-22)

Hamzah: "more details in the streets, the skyscrapers need more details. We need windows, light, balconies."
- **Real-scale windows:** facade tiles now use real sizes (1.5 m panes, 3.4–3.6 m floors; `Tools/assets/make_facades.py` TILES). The old uniform 14–30 m tiles made "floors" 1.4 m tall.
- **Lit windows:** every office facade gets an emission map: Facade001/005 light whole grid cells, Facade006 its detected ribbon panes, ~22% lit (60% warm, 30% cool white, 10% TV blue, some with blinds). U runs continuously round each building from a per-building offset, so no two towers light the same.
- **Brick with windows:** the blank brick sets became `brick_windows_red/buff` (1.2 × 1.6 m openings, stone lintels and sills, 8 windows × 4 floors per 24 × 14 m tile, ~30% lit).
- **Storefronts:** every street-facing wall of a ground-level building gets a 4.5 m lit storefront band (`storefront` set: display glass, mullions, door, sign band, bulkhead). 483 walls.
- **Balconies:** residential towers (OSM apartments/residential/hotel/dormitory, or Condo in the name) get stacked balconies in every other 4.5 m bay from the 3rd floor up: concrete slab, glass railing, metal rail. 558 balconies.
- **Road paint:** double yellow centre lines on two-way streets ≥ 7.5 m, dashed white lane lines on ≥ 12 m streets, ladder crosswalks + stop bars on every arm of every intersection (OSM nodes shared by 2+ roads: 17 junctions, 59 crossings). Paint at 0.075 m (above the road-area polygons).
- **Traffic signals** (`World/TrafficSignal`): at 4-arm main-street junctions (3), mast-arm heads per approach, axes alternate green 22 s / amber 4 s / all-red 2 s, staggered between junctions.
- **Street props:** litter bins, hydrants, benches, newspaper boxes at the kerb every ~18–40 m on main streets, plus Bike Share docks (kiosk + bikes posts) at the real OSM bicycle_rental points. 53 props.
- Brightness tuned from renders: windows 18 nits, storefronts 12 nits (higher blew out at EV 1.5).

## 3K. Rabba stores (walk-in, 2026-10-22)

Hamzah: "make a couple rabba stores, make them enterable." Rabba is a real Toronto chain (personal project OK; commercial release needs a parody name, section 9).
- **Two stores** (`CityGenerator.Shops.cs`): **under Tarik's condo** on Pantages Tower's Shuter St face beside the lobby, and **on Yonge north of Dundas** (west side). The INS Market spot was first choice for the second, but no building there is deep enough for a 10 m store.
- **Walk-in, no loading screen:** the host wall gets a real opening (any building wall on the store front line is cut: OSM has overlapping outlines), a glass front with a 2.2 m open doorway (1.6 m was easy to clip), and an 8 × 10 m room fitted inside the footprint (rejected if another building's footprint reaches into it).
- **Inside:** tile floor level with the sidewalk, fluorescent panels + 2 cool 4300 K lights, two double-sided aisles stocked with products, a lit drinks-fridge wall, the red checkout counter with the cigarette wall behind, a Rocketbox clerk; backlit RABBA FINE FOODS sign outside (`make_rabba_sign.py`).
- **Indoor zones** (`World/IndoorZone`): no rain, indoor ambience and no wind inside the stores (the condo keeps the height rule). Interior volume: fog off, EV 5.5.
- **Test:** `PlayerSimTest` walks in from the sidewalk at both stores (6+ m inside). The wall test now runs from both the post-speed-run spot and the street spawn; the plane check applies to near walls only.

## 3L. Street crowd: walkers, corner crews, homeless (2026-10-22)

Hamzah: "there should be normal people walking around, drug dealers at corners, homeless people. Realistic downtown toronto." The first NPC pass (3B.2) only placed 32 people within 170 m of Yonge & Dundas, and Tarik's condo is ~200 m out, so his streets were empty.
- **Walkers follow the player** (`NPC/CrowdDirector`): a pool of **90**, kept on the real sidewalk lines of the **whole map** (baked by `NpcSetup`: 109 lines, 1,906 points). New walkers appear 35-140 m away and outside the camera view; anyone past 165 m is recycled. Street weights: Yonge 3, primary 2, secondary 1.5, others 1. Upstairs in the condo (y > 60) the crowd sleeps; back on the street it refills at once.
- **Placed people** (seeded, `NpcSetup`), switched off past 150 m:
  - **Corner crews (10):** ones, twos (talking + listening) or threes (lookout, talker, someone on the phone) on the inside of junction corners, one per few blocks. The lookout (`Pedestrian.watchPlayer`) turns to watch Tarik within 5 m.
  - **Crosswalks (9 people):** waiting or on the phone, facing the crossing.
  - **Homeless (16 spots):** against walls outside real OSM shops, 35 m apart, at least 12 m from the lobby and Rabba doors. Variants: on a milk crate with cardboard, cup and backpack; crouched on cardboard with a cup; asleep in a sleeping bag on flattened boxes (no lying animation exists, so the body is a shaped mound); standing (drunk, coughing, shaking off the cold). Their outfits use a faded, dirtier copy of the body material.
- **Animations:** 14 more Rocketbox in-place clips (60 MB, approved 2026-10-22). The female controller uses the male clip where no female file exists.
- **Limits:** Rocketbox has no hoodies (one male model has a hood) and no lying-down clip; the sleeping-bag mound is primitives. Edit-mode renders preview the pool with `ShotTool.PreviewCrowd`; runtime frame rate is logged every 15 s as `CROWD walkers=.. statics=.. fps=..` in Player.log.

## 3M. Inventory, shop, body stats, weapons (2026-10-22)

Hamzah: "Create an inventory system as well, where you can carry in game items, food, guns." Design approved 2026-10-22 ("go, build the inventory as designed").
- **Backpack** (`Items/Inventory.cs`, rules in one place): 20 slots, per-item stacks, carried weight. Over **25 kg** Tarik walks at 65% and can't sprint; nothing more goes in past **35 kg**. Key items (phone, condo keys) can't be dropped or used up. Start kit: phone, keys, lighter, **$60.00** (cash is now dollars and cents).
- **Items** (`Resources/Items/items.json`): 18 items in 8 categories (food, drink, medical, weapon, ammo, key, tool, misc) with Toronto convenience-store prices, tax in. Missions can hand out items (`item:<id>:<n>`).
- **Screen** (`Items/InventoryUI.cs`, `UI/Inventory.uxml/.uss`): **Tab** opens, Tab/Esc closes. 5 × 4 grid; cash, carry weight and health/food/water bars on top; details and Eat/Drink/Use, Equip, Draw, Drop on the right. Tarik stands still while it's open.
- **Shops** (`World/ShopCounter`): **E** at the till of either Rabba opens the stock beside the backpack, with Buy buttons greyed out when you can't afford it or have no room.
- **Pickups** (`World/WorldPickup`, placed by `EditorTools/ItemSetup`): water and ketchup chips on the coffee table; a 9mm pistol and 30 rounds on the bedroom nightstand. Taken pickups stay gone (`picked:<id>` flags). Dropped items land as a bag in front of Tarik.
- **Body** (`Items/PlayerStats.cs`), real time while playing: hunger empties in ~2 h, thirst in ~1.5 h, 2.5× faster sprinting. Empty hunger or thirst drains 6 health a minute; both above half heals 3 a minute. Low stats wash the colour out and close the vignette in (a runtime volume over the bodycam look); warnings show as phone banners. At zero health Tarik **collapses and wakes up at home** with 35 health and 15% of his cash gone. Stats show on the phone's home screen.
- **Weapons** (`Items/WeaponHolder.cs`), carry and draw only: equip from the backpack (loads the magazine from loose rounds), **Q** draws/holsters, **R** reloads. Drawn, the pistol comes up into the bottom of the chest-cam frame, canted so the slide's side shows. **No firing yet**: combat is its own milestone. The pistol is **Rocketbox `Police_Male_01`'s duty pistol** (MIT, approved 2026-10-22, 86 MB download): `EditorTools/PistolImport` cuts its material slot out of the officer's skinned mesh and orients it from its shape (muzzle +Z, slide +Y); it measures 29 × 136 × 205 mm, Glock-17 size. The box stand-in remains as a fallback.
- **Saves:** version 2; backpack, stats, cash and the equipped weapon ride in `GameState`. Older saves load with the phone and keys added back.
- **Test:** `InventoryTest` (24 checks): start kit, stacking, slot/weight limits, buying (cash, not sold, full), eating caps, the body clock, starving and collapsing, equip and reload, unequip on removal, save round trip, old-save repair, and the scene wiring (2 counters, 4 condo pickups, the components).
- **Police** (same download): `CharacterImport` matches extra texture sets by slot name (his equipment belt, his pistol). Two officers are posted on a corner of Yonge & Dundas (one watches Tarik), and 2 of the 90 pooled walkers are officers on the beat. Their uniform is Rocketbox's generic grey-blue, not TPS navy.
- **Limits:** the condo food and ammo pickups are placeholder shapes; no hands are drawn holding the pistol.

## 3N. TTC subway: Line 1, Queen - TMU - College (designed and built 2026-10-22, branch `feature/ttc-subway`)

Hamzah: "build the TTC subway system ... one you can physically ride to other stations around the map ... as accurate as possible to real life TTC subway stations." His answers: **Queen, TMU, College** first; the subway is **quick travel** between spots in the city; **free for now** (gates open); **real streets** above the new stations.

### 3N.1 Real-world facts used (checked 2026-10-22)
- Dundas station has been **TMU station** since 7 Dec 2025 (TTC; TMU paid for the change). Sankofa Square (ex Yonge-Dundas Square) is the square above it.
- Queen, TMU and College all opened 30 Mar 1954 on the original Union-Eglinton stretch: **cut-and-cover under Yonge, two tracks, side platforms**. Queen's platform art is John Boyle's mural *Our Nell*; College's is Charles Pachter's hockey murals (both copyrighted: not reproduced; see 3N.9).
- Line 1 runs **Toronto Rocket** trains (Bombardier): 6 cars permanently coupled with open gangways; end cars 23.19 m, middle cars 22.86 m (train ~138 m), 3.137 m wide, 3.645 m tall; stainless steel, rivet-free sides; **4 doors per side per car**, 1.5 m wide; inside: white walls, silver dividers, grey floor (seat colour unverified). Third-rail power.
- Adult PRESTO fare $3.30 (frozen for 2026), cash $3.35. Not used yet (free for now).
- Directions: northbound = **Finch**; southbound = **Vaughan Metropolitan Centre via Union**.

### 3N.2 Geometry from OpenStreetMap (`Tools/osm/fetch_ttc.py`, cache `raw_ttc_line1.json`)
Game coordinates (same projection as the city):

| Station | Centre (x, z) | Platforms (OSM) | Gap to next (along the line) |
|---|---|---|---|
| Queen | (113, -365) | 2 side, ~144 m traced | 412 m to TMU |
| TMU | (-10, 28) | 2 side, ~146 m traced | 499 m to College |
| College | (-160, 504) | 2 side, ~172 m traced | (Wellesley off-map) |

- **Built (task 2):** `fetch_ttc.py` writes `Assets/City/Data/ttc_line1.json`. Station centres are the midpoint of the two OSM side platforms, not the station node (the node sits 5 m off at TMU, 15 m at Queen, 27 m at College). The line is one centreline (midway between the tracks, smoothed, a point every 2 m, 1,952 m from 520 m south of Queen to 520 m north of College); trains and tunnels follow it, with the two tracks 4.1 m either side of centre. Measured track gap: 4.46 m Queen, 4.07 m TMU, 4.49 m College. Each entrance carries its offset in the station's frame (u along the line, v across it).
- Queen St at Yonge is tagged `highway=construction` in OSM (Ontario Line works, real), so it was missing from the map; the city build now draws `construction=<road type>` ways as that road.

- Both tracks are in OSM (`railway=subway`, `tunnel=yes`, `layer=-2`), **4.1 m apart** at TMU; they follow Yonge's bend. Platform centrelines ~12 m apart, so each platform is ~3.5 m wide.
- **Platforms are built 152 m long** (the TTC standard; OSM traces are approximate), centred on the OSM platform ways.
- **Entrances: 24 real ones in OSM, 19 at these three stations**, with names, level and wheelchair flags:
  - **TMU, street:** Sankofa Square Entrance, Sankofa Square Elevator, Yonge / Dundas NW corner.
  - **TMU, PATH:** Atrium on Bay, Eaton Centre L1 and L2, The Tenor.
  - **Queen, street:** Yonge / Queen NW, NE and SW corners, 2 Queen St E, Yonge / Shuter SE.
  - **Queen, PATH:** One Queen St E, Eaton Centre North and South, Hudson's Bay. The Bay closed in 2025, so its door is shuttered.
  - **College, street:** Yonge / Carlton NE and SE.
  - **College, PATH:** College Park Shops.

### 3N.3 Map extension (task 1)
- The OSM box grows from 43.6535-43.6585 N to **43.6505-43.6625 N** (same west/east). That is ~1.33 km of Yonge, one block past Queen (Richmond) and past College/Carlton.
- Everything regenerates: buildings, roads, signs, map.png, places.json, the sidewalk lines and the crowd.
- **Risks:** roughly 2.4× the buildings with ray tracing on, and the Yonge Rabba site is re-picked from the new data, so it may move.
- **Built 2026-10-22.** Box now x -289..275, z -645..681 m. Measured: 837 buildings (was 290), 232 roads, 535 paths, 12 plazas, 579 streetlights, 13,406 sidewalk pieces, 135 signs (was 142: the sign picker re-ranks shops), 1,032 meshes. Both Rabbas stayed put (same buildings and doors) and the condo still generates. map.png is now 2735×5785 px (import max raised to 8192). All 4 suites green. FPS with 2.9× the buildings and ray tracing is **not measured** (no exe launch); check `Player.log` after a play.

### 3N.4 Levels (all three stations, shallow 1954 box)

| Level | y (m) | What |
|---|---|---|
| Street | 0 | stair openings with railings, the red TTC sign on a post; Sankofa elevator kiosk |
| Concourse | -4.2 floor / -1.2 ceiling | fare line (PRESTO gates, open), PRESTO machines, wayfinding, washrooms (closed), PATH doors |
| Platform | -8.55 floor / -4.6 ceiling | 2 side platforms, 152 m |
| Rail top | -9.6 | 2 tracks 4.1 m apart, third rail on the outside |

- **Stairs:** ~25 risers concourse to platform at each end, plus escalators. The elevator uses the existing fade ride (like the condo).
- **Concourse layout:** the fare line runs across the middle, with signs to "Northbound - Finch" and "Southbound - Vaughan Metropolitan Centre via Union" over the stairs down.
- **Built (task 3), how it is laid out:** `plan_ttc_stations.py` plans each station in its own frame and writes `ttc_stations.json`; `CityGenerator.Subway.cs` builds it.
  - **Concourses:** one over each end of the platforms (34 m × 14.4 m), fare line across each (12 gate cabinets, open, PRESTO readers and 2 machines). Stairs down sit at the platform ends (|u| 69-76): a 2.3 m stair (25 risers) beside a 1.2 m escalator that carries you up at 0.5 m/s.
  - **Street stairs:** each OSM entrance gets a 2.6 m × 7.6 m opening (24 risers) placed on open pavement as close to its node as fits (not on a road, not under any building or low building part), then a 3 m passage to the nearer concourse's unpaid side. Moved from the OSM point: 4.5-5 m at Queen's corners, 16.6 m Shuter, 3 m / 18 m at TMU, 1 m / 18 m at Carlton. The ground, road, sidewalk and plaza meshes are cut around each opening.
  - **Not built:** "Entrance by 2 Queen Street East" is a door inside the tower (every metre around it is building in OSM), so it has no sidewalk stair; SubwayTest lists it. College Park Shops' door is below street level, so it counts as a closed PATH-style door.
  - **Elevator:** Sankofa Square Elevator is a glass kiosk; E rides it down to a landing joined to the concourse (the condo's DoorLink fade).
  - **Light:** HDRP caps how many lights reach one screen cell, and the 602 streetlights (30 m range) reach below the pavement, so underground they crowded out the station lights. `LightBudget` switches city lights more than 30 m away off while the camera is underground, and station lights more than 35 m away off while it is on the street. A local volume over each station and its passages sets exposure EV 6.0 and no fog. Rain stops below y = -1.5 (IndoorZone).
  - **Measured:** all 18 entrance-to-platform walks pass (9 street stairs + the elevator, to both platforms), 18-40 s at a jog.

### 3N.5 Station finish (accuracy checklist)
- **Platform edge:** yellow tactile strip with raised domes (width ~60 cm, approximate).
- **Platform furniture:**
  - The **Designated Waiting Area** (blue sign, intercom pillar, camera, bench, brighter light).
  - Next-train screens hanging over the platform, with minutes and the destination.
  - Emergency power cut and emergency alarm stations, bins and benches.
- **Walls:** the station name repeated along the track wall in the TTC subway lettering style (our own render; the Toronto Subway typeface itself is not free to embed), with Line 1 yellow (~#F8C300, approximate) roundels "1".
  - **TMU:** new 2025 TMU name panels.
  - **Queen and College:** their own names.
  - **Wall colours per station:** unverified; needs reference photos, see 3N.9.
- **Ceiling:** low, with fluorescent light strips over the platform edge.
- **Track bed:** concrete with drainage; cable trays and blue emergency lights on the tunnel walls.
- **Built (task 4):** `CityGenerator.SubwayTrack.cs` sweeps a double-track box along the centreline between the stations (8.5 m inside, floor -9.8, roof -5.4): 1,496 m of tunnel on a 1,952 m line, closed at both ends. Through the whole length: two tracks at TTC gauge (1,495 mm) on 5,206 concrete ties, a third rail on the outside of each track, two cable trays per wall, a blue emergency light every 25 m. Tunnels sit outside the station volume, so they render at the night exposure: dark, as intended.

### 3N.6 The train (procedural, no download)
- **Build:** a Toronto Rocket set generated in code from the real dimensions in 3N.1. The body is an extruded stainless profile with a rounded roof and a black window band; the ends have the TR cab shape with headlights and a route sign reading "1 FINCH" or "1 VMC".
- **Inside:** open gangways between cars; seats along the walls with grab poles; LED ceiling strips; a strip map above each door with "next station" lights.
- **Riders:** Rocketbox people sitting (Sit clip), standing on their phones, and waiting on the platforms.

### 3N.7 Riding (quick travel)
- **Schedule:** one train per direction shuttles Queen ↔ College. When Tarik reaches a platform, the next train is **at most ~20 s away** (it is held in the tunnel out of sight, then sent in).
- **Ride time:** a hop takes ~30 s: 2.2 m/s² up to 25 m/s, cruise, brake to the stop mark. That is faster than the real Line 1, on purpose. **Enter "zones out"**: a fade, and Tarik is at the next station.
- **Doors:** a 12 s dwell. The door chime is code-made, followed by on-screen announcements in the real wording: "The next station is College. College station." / "Doors closing, please stand clear of the doors." Doors open on the right, on the platform side.
- **Physics:** Tarik physically stands in the car. The train is a moving platform that carries the CharacterController by its delta each frame. He can walk the length of the train through the gangways.
- **End stations:** "This train is short turning. All customers must exit at Queen / College." This is real TTC wording. The train empties and goes back into the tunnel.
- **Map:** Line 1 is drawn in yellow with roundels on the full map. The minimap shows the line while underground.
- **Built (task 5):** `CityGenerator.SubwayTrain.cs` builds two trains (Train_NB "1 FINCH", Train_SB "1 VMC"), 6 cars each, 137.82 m (23.19 + 4 × 22.86 + 23.19), 3.137 m wide, 3.645 m tall, 4 doors a side per car. The right-side leaves slide; left-side doors are part of the body. `TrainController` runs each train: hold point 300 m behind the first stop, sent in at cruising speed from 300 m out when Tarik waits on its platform, 2.2 m/s² up to 25 m/s, 2.0 m/s² braking onto the stop mark (the train centred on the platform), 12 s dwell, chime and "Doors closing", short turn at the end (doors stay open while Tarik is aboard there), then out to 300 m past the end and back to the hold point. Cars carry whoever stands in them (position and turn). Enter zones out to the next stop. Announcements are subtitles (`SubwayAnnouncer`, OnGUI).
  - **Measured (SubwayTest):** train at TMU with doors open 20.6 s after Tarik reached the platform; TMU → College hop 31.7 s; Tarik's height stayed on the car floor the whole ride; zone-out TMU → Queen; an unattended loop runs Queen → TMU → College → short turn → hold; doors opened only when stopped on a platform, on the platform side (0 violations in 7,212 open-door steps).
  - **Not seen yet:** the subtitle and next-train lines are OnGUI, which ShotTool renders don't show.

### 3N.8 Sound (code-generated, like 3I)
- **Station:** ambience hum.
- **Train:**
  - an approach rumble and wind push down the tunnel;
  - the TR's rising motor whine as it pulls away;
  - wheel noise;
  - the door chime;
  - brake hiss.
- **In the car:** a muffled interior bed.
- **Built (task 6):** nine `subway_*.wav` clips from `make_sounds.py`, mixed by `SubwayAudio`: station hum below the street, 3D rumble/wheels/whine on each train's front car (the whine's pitch rises with speed), wind before an arriving train, chime/doors/brake hiss from the train's own cues, the interior bed while riding. Underground no longer plays the condo's rain-on-glass and room tone, and footsteps there are concrete. **Never listened to by me**: Hamzah to judge.
- **Riders (task 6):** 9 people per train (seated on the side benches with the chair-sit clip, or standing by a door on the phone) and 3 waiting on each platform, Rocketbox. `SubwayRiders` switches them on only while the camera is underground.
- **Map (task 6):** `render_map.py` draws Line 1 in yellow with a "1" roundel and name at each station and red squares at the street entrances. The minimap uses the same image, so it shows the line everywhere, not only underground (simpler than the design said).

### 3N.9 Open items
- Per-station wall colours and the TMU 2025 panel look: needs reference photos (Hamzah supplies them, or I search images) before the finish pass.
- The Pachter and Boyle murals are copyrighted: leave those walls plain or use our own art.
- TR seat colour: unverified.
- The PATH (Eaton Centre etc.) is not built, so PATH doors stay closed.
- Wall tiles, floor tiles and seat fabric are neutral placeholders; the TMU panels are plain "TMU" lettering for now.
- Not checked by eye in the running game (Hamzah forbade taking over his screen): the subtitle and next-train lines (OnGUI never shows in ShotTool renders), the zone-out fade, how the trains look moving, all the sounds, and frame rate with 837 buildings plus the subway.
- "Entrance by 2 Queen Street East" is inside the tower in OSM, so it is not built.
- Saving while on a train and loading puts Tarik back at the saved spot, but the trains restart at their hold points, so he lands on the track bed (untested edge case).

### 3N.10 Build order (one branch, one commit per task)
1. Map extension (3N.3), regenerate, all tests and renders green.
2. `fetch_ttc.py` → `Assets/City/Data/ttc_line1.json` (tracks, stations, entrances, platforms, local coords).
3. Station builder: box, platforms, concourse, stairs, escalators, elevator, street entrances, signage textures (`make_ttc_signs.py`).
4. Tunnels and track: box tunnel, rails, ties, third rail, tunnel lights.
5. Train mesh + `TrainController` (schedule, doors, moving-platform ride, announcements, zone-out, short turn).
6. Sound, riders, map overlay.
7. `SubwayTest` (all green 2026-10-22, plus the 4 older suites; exe rebuilt):
   - every street entrance reaches its platform (PlayerSimTest-style walk);
   - a simulated ride TMU → College arrives;
   - doors open only at platforms;
   - a hop takes ≤ 35 s;
   - plus renders and a rebuild.

## 3O. Ty, the lobby, the garage, cars, shooting (built 2026-10-22, `feature/ty-lobby-garage-cars`)

Hamzah: "make Ty the 2nd character in the game, a person who always calls me to give me missions. Lets also add cars ... Give [Tarik] his car. Also make a garage system for the condo. Build the condo lobby properly with a concierge. Add more details ... the condo lobby, the subway, the streets, the cars." Then: "give tarik an old bmw" and "add a Pistol to Tariks inventory, and add a proper shooting system."
He skipped the scope questions except the car, so the recommended defaults apply: Tarik drives, parked cars line the streets but there is **no moving traffic yet**; the garage is underground P1 with his own spot; Ty is an old friend.

### 3O.1 Ty (the second character)
- **Who:** Tarik's friend from Regent Park, plugged in downtown. He sets up the jobs and keeps asking how Tarik affords Pantages. This is a placeholder story (section 9).
- **Look:** Rocketbox **Male_Adult_10** (red zip track jacket, black track pants), taken **out of the street crowd** so there is only one Ty.
- **Calls:** Ty rings on his own between jobs. When no mission is active, the next Ty job calls after 3-6 minutes of play. He also sends texts.
- **In person:** an objective can say `"npc": "ty"`. Ty then waits at the meeting place, turns to Tarik, and talks in **subtitles** (the same dialogue files as calls, shown at the bottom of the screen with reply keys 1-3).
- **Jobs:** the existing three are reworked around Ty, plus jobs that use the new pieces:
  - drive the BMW somewhere;
  - take Line 1 to College for a pickup.

### 3O.2 The condo lobby (Pantages Tower, Shuter St)
- **Walk-in:** a real ground-floor room cut into the tower like the Rabbas (`ShopSites` gains a `kind`). It is 8 × 10 m with a 4.2 m ceiling, beside the Pantages Rabba. The old street elevator prompt goes.
- **Inside:**
  - a glass vestibule with a fob reader;
  - stone floor, a marble feature wall, warm downlights and an LED cove;
  - a lounge with a sofa, chairs, table and plants (the condo's furniture sets);
  - a wall of mailboxes, and a closed parcel-room door;
  - **two elevators** with steel doors, call buttons and floor displays: the left goes "up to 3801", the right "down to P1 Parking".
- **Concierge:** a Rocketbox person (Female_Adult_06) behind a desk with CCTV monitors. They watch Tarik, greet him on the way in or out ("Evening, Mr. Tarik." / "Late one?"), and E talks to them (a few lines, plus hints such as where his car is parked).

### 3O.3 P1 garage
- **Space:** under Pantages at floor y = -3.6 (well clear of Line 1). About 40 × 24 m, a 2.6 m ceiling, columns on an 8 m grid with yellow-black bands, painted bays 2.6 × 5.4 m numbered P1-01 to P1-24, fluorescent tubes, pipes, an EXIT sign and a fire door.
- **Tarik's spot P1-17** holds his BMW. Residents' cars fill some other bays.
- **Getting in and out:**
  - **On foot:** the elevator from the lobby, or from the condo (via the lobby elevator).
  - **By car:** drive into the **roll-up garage door on Shuter St**: a fade, and you're in the P1 lane. The reverse puts you on Shuter facing out. The same fade idea as the elevator.
- **Remembering:** the garage records where Tarik's car is (in P1-17 or out on the street, with its position). This is saved.

### 3O.4 Tarik's car: an old BMW (E46 3 Series sedan, black)
- **Model:** built in code from E46 dimensions: 4.47 m long, 1.74 m wide, 1.42 m tall, 2.73 m wheelbase.
  - **Body:** lofted from side and plan profiles, with the E46 cues: twin round headlights under glass, kidney grille, Hofmeister kink on the rear window, L-shaped taillights.
  - **Wheels:** 5-spoke.
  - **Inside:** a dash with round gauges, steering wheel, seats and a mirror.
  - **Badges:** unbadged look (section 9: real names need a parody for any release).
- **No good free licence exists:** the CC0 cars found are low-poly or generic. If the procedural body doesn't read as an E46, the fallback is a CC BY model that Hamzah downloads himself, credited in CREDITS.
- **Driving:** a Rigidbody with WheelColliders, rear-wheel drive, ~1,450 kg.
  - **Controls:** W/S throttle and brake (reverse when stopped), A/D steer, Space handbrake, **F** to get in or out (beside the driver door).
  - **View:** from the driver's seat (the chest cam sits at a seated chest height, so the dash fills the bottom of the frame).
  - **Feel:** a code-made engine sound (inline-6) and headlights.
  - **Collisions:** with buildings, parked cars and poles. Pedestrians step aside; there's no running people over yet.

### 3O.5 Parked cars
- **Where:** along the curbs of side streets (residential, tertiary, unclassified and some secondary), not Yonge. They're kept out of junctions, crosswalks, the garage door and the subway entrances.
- **Spacing:** one car every 6.5-8 m, with some gaps.
- **Variety:** procedural body types (sedan, hatchback, SUV, minivan, a white delivery van) in real-world colour mixes: lots of grey, black, white and silver, a few blue and red. Wet paint, dark windows, a box collider each.
- **Performance budget:** about 250 cars, static, with shadows only near the player.

### 3O.6 Pistol and shooting
- **Start kit:** a new game now includes the **9mm pistol and 30 rounds** (the nightstand pickup stays as spare ammo).
- **Controls:** Q draws or holsters, **right mouse aims** (it comes up to the sights, FOV narrows a little), **left mouse fires**, R reloads.
- **Feel:** semi-auto with a fire-rate cap; recoil kicks the view and the rig; muzzle flash and a light flash; a code-made gunshot with a tunnel or street echo; spent casings; dry clicks when empty.
- **Hits:** a raycast from the barrel.
  - **On walls:** bullet-hole decals and sparks or dust by surface.
  - **On NPCs:** the hit person goes ragdoll (Rocketbox humanoid → ragdoll in code).
- **Reaction:**
  - **The crowd:** anyone within 35 m of a shot panics and runs away. Corner crews scatter.
  - **Police:** officers within range draw their attention to Tarik (they look and walk toward him; police combat is a later milestone).
- **Heat:** +1 per shot fired near people, +3 per person hit.
- **Rules:** you can't fire indoors in the condo, lobby or subway trains (the safety is on with a "Not here" toast). That keeps missions sane until police AI exists.

### 3O.7 Detail pass
- **Lobby, garage, cars:** as above.
- **Subway:** the wall colour placeholders stay until reference photos arrive. Add a train headlight glow in the tunnel, platform-edge gap fillers and more wayfinding.
- **Streets:** hydrants, newspaper and Bike Share docks, parking machines on the parked-car streets, more litter.

### 3O.7b Added mid-build (Hamzah's asks)
- **Volume:** the whole mix plays at **5%** of its old level (`GameSettings.MixScale`); the slider keeps its range.
- **Toronto street talk** (`NPC/NpcBarks`): walk right past someone and now and then they say a line, shown above their head (no voices). The lines depend on their street role: walkers ("TTC's delayed again, I swear.", "Tim's or nah?"), corner crews ("Wagwan."), the homeless ("Spare some change, bro?"), police, people at crosswalks ("Is this where the 505 stops?"), clerks.
- **Dealers** (`World/DealerNPC`): each corner crew's lookout (10 of them, named: Dre, Junior, Smokes ...). E opens his stock in the shop screen ("Loud (1g)" $15, "Pills" $20, cigarettes; cash only); each purchase is +1 HEAT. He won't deal with police within 25 m or with a gun out.

### 3O.9 Built: what was measured
- **Ty:** meetups snap him to the nearest sidewalk, and the meeting radius follows where he stands. College Run is a Line 1 job; he texts every 5 min between jobs. He's kept out of the crowd and the train riders.
- **Lobby:** walk-in on Shuter beside the Rabba (the wall cutter now handles several openings on one edge). Two elevators: condo ↔ lobby ↔ P1.
- **Garage:** 24 bays, a 7 m aisle and a roll-up door; drive-in and out through fade gates; the concierge knows where the car is.
- **E46:** about 4.3k triangles. In DriveTest it did 0-46 km/h in 3 s (a real 330i is about 6.5 s to 100), stopped from 46 km/h in 1.3 s, reversed after a beat at a standstill, and steered 46°.
- **Parked cars:** about 50 in all (Hamzah: 250 was excess): 43 on side-street kerbs (from 1,541 valid spots) plus 11 in P1. One renderer each (wheels baked in), about 5k triangles; lamps off.
- **Shooting** (ShootingTest): a wall shot leaves a hole; a person shot ragdolls (12 bodies); HEAT +1 for firing near people, +3 for a hit; bystanders run and police walk over; no firing in indoor zones; an empty mag clicks; R reloads.
- **Street extras:** 5 Green P pay-and-display machines (one per 9 parked cars), 3 Bike Share Toronto docks in the curb lane, 224 bits of litter.
- **Tests:** 7 suites green (player, phone, saves, inventory, drive, shooting, subway).
- **Not checked in the running game:**
  - the driving feel;
  - the engine and gunshot sounds;
  - the subtitles, the street talk and the inventory screen (UI isn't in batch renders);
  - frame rate on the bigger map.
- **Limits:**
  - The E46's side windows show stair-step edges where glass meets trim.
  - Parked cars are generic shapes.
  - There are no hands on the pistol, no running animation (fleeing people speed up the walk clip), and no police combat yet.
  - The sleeping bags and bikes are primitives.

### 3O.8 Build order (one branch `feature/ty-lobby-garage-cars`, one commit per task)
1. Ty
2. Lobby + concierge
3. P1 garage
4. BMW + driving
5. Parked cars
6. Pistol + shooting
7. Detail pass, `GarageCarTest` + `ShootingTest`, renders, rebuild

## 3P. Combat: hands, gunfights, fist fights (design 2026-10-22; task 1 built, rest not built)

Hamzah: "Create a proper shooting mechanism, with hands. Shooting and fighting is a big part of this game and its upcoming missions." His answers:
- **Hands:** Tarik's own Rocketbox body with IK.
- **Combat:** enemies that shoot back, plus fist fights.
- **Guns:** pistol only for now; the system takes more guns later.
- **Death:** wake at home (the collapse rule in 3M: cash lost).

Builds on 3M and 3O.6: WeaponHolder, Inventory, PlayerStats, Pedestrian, Ragdoll, CrowdDirector.

### 3P.1 Tarik's body and hands
- **His body:** Rocketbox **Male_Adult_18** (grey jacket, black pants) becomes Tarik. Like Ty, he's taken out of the crowd, the train riders and the clerks.
  - The body is parented to the player at his feet.
  - **First person at eye level** (Hamzah: "its supposed to be FIRST PERSON VIEW"). The camera sits at his eyes: `BodycamRig.mountHeight` 1.62 m, 0.10 m forward, near clip 0.03 m. It was a chest mount at 1.35 m before.
  - **The head is cut from the mesh.** `PlayerSetup.HideHead` saves a headless copy of his mesh (`Tarik_FirstPerson_*.asset`), dropping every triangle skinned mostly to the head bone. Two earlier tries failed:
    - Scaling the head and neck bones collapsed the arms (Biped clavicles hang off the neck) and stretched the collar.
    - Hiding the head material slots also hid the hands, because Rocketbox shares those slots.
  - **No shadows from the first-person view:** his own body and the held gun cast none. Shadows that close to the lens broke into blotches.
- **Arms:** idle and walk run on the animator's base layer (the legs mostly stay out of view). `TarikBody` poses the arms after that each frame with its own two-bone IK (law of cosines, elbow hints), a hand frame taken from the finger bones, and finger curl. Pure transform maths, so editor renders show exactly what play shows. Measured pose values:
  - **Aim:** the gun is 0.45 m ahead of the eye with the sights 6.5 cm below it.
  - **Ready:** `(0.10, -0.15, 0.42)`, so both hands are in frame.
  - **Reload:** pulled in to `(0.03, -0.12, 0.36)`, muzzle up, canted 30°.
  - **Rack:** an overhand grip on the rear of the slide.
  - **Pistol:** the right hand holds the grip and the left supports it. Grip points are children of the gun; elbow hints are children of the camera. Recoil, aiming and the draw all move the arms.
  - **Unarmed:** a fighting stance with fists in front of the chest when raised. Hands hang out of view when walking around calm.
  - Weights blend in over 0.15 s, so draws and holsters don't snap.
- **Procedural gun actions:**
  - **Draw:** the right hand goes down to the hip holster and comes up with the gun.
  - **Holster:** the reverse.
  - **Reload:** 1.6 s, cancelled by sprinting. The gun tilts in and the mag drops out (a real object falls); the left hand goes to the belt, brings a new mag, slaps it in and racks the slide on an empty reload.
  - **Inspect:** hold R when full.

### 3P.2 Damage model (built 2026-10-22)
Built as follows. Punch damage exists as `PlayerStats.PunchDamage()` but nothing throws punches until task 3. Near misses are `PlayerStats.NearMiss()`, called by the hostiles in task 4.
- **Tarik:** `PlayerStats.TakeDamage(amount, from)` takes damage, `BulletDamage(distance)` scales it with range (30 at 5 m, 18 at 40 m), and `HurtFeedback` on the eye camera draws the effects. The tuning is in the range (`range_eye_hurt`).
- **NPCs:** `Pedestrian.Damage()`. Body shots do **50-55**, not 45-55: with the wider range, two shots could leave someone standing on 6 health.
- **HEAT per hit:** +2 for a wound, +3 for a kill, +10 for killing police (plus the +1 for firing near people).

The design as written:
- **Tarik:** 100 health (PlayerStats).
  - Bullets do 18-30 by range, punches 6-10.
  - **Feedback:** a red vignette pulse, a bodycam glitch (chromatic aberration plus a noise burst), a short tinnitus ring after a near miss, and a heartbeat under 30 health.
  - **Regeneration:** slow regen (existing) only after 8 s without damage.
  - **At 0:** collapse, wake at home, cash lost (3M), weapons kept.
- **NPCs:** 100 health.
  - **Bullets:** a hit above 1.5 m (head) drops them at once; body shots do 45-55, so two drop them.
  - **Fists:** a punch knocks them back (a short stagger). A knockdown is a temporary ragdoll that gets back up after 3 s, unless their health is gone.
  - **Death:** ragdoll (existing).
- **HEAT:** as in 3O.6. Killing police is +10, though police combat stays a later milestone.

### 3P.3 Fist fights (built 2026-10-22)
Built as follows:
- **`Player/Fists`:** jab and cross do 8-12, the hook 18-24. The hook comes on holding left mouse for 0.25 s.
- **`NPC/NpcMelee`:** added on someone's first punch.
  - **Walkers:** after one punch they stagger and stare; the second makes them fight.
  - **Corner crews:** they fight at the first punch.
  - **Police:** they walk up and watch. Clerks and story people run.
  - **Fighters:** their swings are aimed IK arms (a `TarikBody` of their own). They give up and run below 35 health.
  - **Knockdown:** a ragdoll, then `Ragdoll.Recover` stands them up where the hips landed. There's no get-up clip yet, so the pose snaps.
- **"Shoved twice" became "punched twice":** the game has no shove.
- **Finger-curl fix:** the curl direction was being judged at every joint, so past ~90° the middle joint bent backward (a claw). It's now judged once per finger. That also fixed the loose support hand on the pistol. Thumbs fold in too.
- **Range:** `range_fists_*` (guard, close-ups, jab, cross, hook, block) and `range_brawl_*` (a fighter landing a swing).

The design as written:
- **Controls:**
  - **Fists up:** holstered and in a fight stance (V toggles, or it comes up automatically when an enemy is in melee range).
  - **Left mouse:** jab, then cross alternating. **Right mouse:** block (fists to the face, 70% less damage).
  - **Hold left mouse:** a heavy hook (slower; knocks people down).
- **Hit check:** a sphere cast 0.9 m ahead on the fist's peak. Hits get code-made punch sounds and a small view kick.
- **NPC fighters:** corner crews and anyone shoved twice fight back with fists. They step in, swing (IK-driven arm swings like Tarik's), get staggered, and back off when hurt.

### 3P.4 Enemies that shoot back (`NPC/Hostile`; built 2026-10-22, no mission yet)
Built as follows. "Wrong Corner" is **not** written: Hamzah said "Dont build the first mission yet".
- **The spawner and the objective field are built:**
  - `HostileSpawner` sits in the city scene on the crowd root, with the crowd's men and Tarik's pistol model.
  - An objective's `"enemies": [{"place": "", "count": 3, "spread": 8}]` spawns them on the sidewalk when Tarik is within 90 m. The objective is done when all are down or have run.
  - Spawned enemies aren't saved; loading mid-fight puts them back.
- **Cover is found, not placed:** points on 1 m rings within 15 m where something solid within 2.5 m blocks the line from Tarik's chest. So parked cars, corners and columns all work without tagging.
- **Shooting:** they shoot from cover when they have a line, otherwise they peek out 0.8-1.4 m to the side and duck back after each burst.
- **Accuracy per shot:** 0.6 × (1 at 5 m, falling to 0.2 at 40 m) × 0.6 if Tarik is moving × 0.4 if only his head shows. Misses within 1.2 m of him count as near misses (the tinnitus ring).
- **When they shoot:** their shots panic the street and alert other enemies.
- **Bullets and punches on an enemy:** both do damage. Neither makes them flee or brawl.
- **Range:** `range_hostile_*` (aimed, from Tarik's eyes, low ready).

The design as written:
- **States:** Idle → Alert (heard a shot, saw a gun, was shoved) → Combat → Dead. Each one can be stepped with `Tick(dt)` for tests.
- **Perception:**
  - **Sight:** 40 m, a 120° view, a line-of-sight raycast to Tarik's chest.
  - **Hearing:** gunshots within 60 m (the Panic hook in WeaponHolder).
- **In combat:**
  - **Cover:** they pick cover. Candidates are points beside parked cars, building corners and columns within 15 m, where a ray from Tarik to the point is blocked.
  - **Movement:** they run there (sped-up walk), peek, and fire 2-3 shot bursts with their arms IK-aimed at Tarik.
  - **Accuracy:** it falls off with distance, with Tarik moving and with Tarik being in cover. Shots have a muzzle flash, a 3D gunshot and a faint tracer.
  - **Reload:** after 12 rounds.
  - **Fleeing:** under 25 health, 50% chance.
  - **Bodies:** dead ones drop their pistol, which can be picked up as ammo.
- **Who:**
  - **Mission enemies:** spawned by an objective field `"enemies": [{"place": ..., "count": 3}]`; the objective completes when all are down.
  - **Corner crews and dealers:** they turn hostile if Tarik shoots at them or hits them. One crew member in each group is armed.
  - **Police:** react as now. Police combat comes later.
- **First mission:** "Wrong Corner" (Ty).

  > "Mans jumped my guy behind the Victoria St garage."
  
  Three hostiles; the reward is $120, REP +2, HEAT +3.

### 3P.5 Sound (code-made, like 3I)
- Punch impacts: jab and hook.
- A body hit.
- Enemy gunshots: the same shot, 3D with distance filtering.
- Bullet cracks passing near.
- A heartbeat.
- A tinnitus ring.
- The mag drop clatter.

### 3P.6 Tests (`CombatTest`, edit mode; the damage checks are built, 18 of them)
- **Body:** Tarik's body exists with the headless first-person mesh. The IK targets sit on the gun's grip, follow the drawn and aimed poses, and stay within arm's reach of the shoulders.
- **Reload:** the reload timeline ends with a full mag and one dropped mag object.
- **Damage:** headshots drop at once; body shots take two; punches stagger; heavy punches knock down and NPCs get back up; Tarik takes damage, regenerates after 8 s, and wakes at home at 0.
- **Hostiles, stepped:** they hear a shot, go alert, pick blocked cover, fire, reload after 12, and flee under 25.
- **Missions:** the mission spawns 3 and completes when they're down.
- **Plus:** renders of the pistol held, aimed and mid-reload, and of fists up; all 7 existing suites stay green; the exe is rebuilt.

### 3P.7 Build order (one branch `feature/combat`, one commit per task)
1. Tarik's body and IK hands with the pistol: draw, holster, aim, recoil, reload. **Done.**
2. The damage model and feedback. **Done.**
3. Fist fights. **Done.**
4. Hostile AI and the spawner. **Done.** "Wrong Corner" waits for Hamzah's go.
5. Corner crews turning hostile.
6. Sounds, tests, renders, rebuild.

### 3P.8 Open items and limits
- **No custom animations:** everything arm-related is IK and procedural; legs use the walk clip. No strafing or crouch-walk clips exist in Rocketbox.
- **The look:** Male_Adult_18 as Tarik is a placeholder until Tarik's look is decided.
- **Police combat** is a later milestone.
- **Support hand:** it wraps properly since the curl fix (3P.3). It still sits slightly low. Tune `WeaponHolder.GripLPos/GripLRot` in the range (3Q).
- **Fist guard:** it sits a little low and wide in the frame. A get-up animation after a knockdown would replace the snap.

## 3Q. Development range (built 2026-10-22)
Hamzah: "you need to create a 'development range' where you have put models and make them correctly before dropping them in the game."
- **What it is:** `Assets/DevRange/DevRange.unity`, built by `EditorTools.DevRange`. A grey studio with:
  - one shadowed key light and a shadowless fill, at fixed exposure;
  - a shooting lane with targets at 5, 10 and 20 m;
  - the pistol on a plinth, Tarik's E46, and a line-up of five people (Police_Male_01, Male_Adult_04, Female_Adult_04, Ty and Tarik).
- **The player is the game's player:** `PlayerSetup.BuildPlayer` makes it for both the city and the range. Anything that looks right in the range looks the same in the game. On its first run the range showed the bug the city renders had hidden: the hands weren't drawn at all.
- **Rule:** every new or changed model, pose or held item is built and judged in the range first, from its renders, and only then goes into the city.
- **Run:** `-executeMethod LifeInDowntown.EditorTools.DevRange.Render` (without `-nographics`; `-shots a,b` to pick shots). It writes `Screenshots/range_*.png` and logs how far each hand ended up from its grip:
  - first-person views: ready, aim, reload, rack;
  - close-ups of both hands, from the side and the front;
  - the pistol turntable, two views of the car, the line-up and the lane.
  
  `DevRange.Build` only rebuilds and saves the scene.
- **Edit-mode gotcha:** skinned meshes don't re-skin between renders without a player loop. The range and ShotTool set `forceMatrixRecalculationPerRender`, or the arms drawn lag one pose behind the bones.

## 3R. Movement for stealth and crime (built 2026-10-23)
Hamzah: "include the ability to run, walk, sneak, peak around corners. This will be needed for this game, based on stealth, crime." He approved the plan: walk by default, Shift run, C sneak toggle, hold Alt + A/D to peek, with noise and visibility hooked into the hostiles.

- **Paces (`BodycamPlayer`):**

  | Pace | How | Speed |
  |---|---|---|
  | Walk | the default | 1.6 m/s |
  | Run | Shift | 4.0 m/s |
  | Sprint | Shift held for 1.2 s going forward | 5.5 m/s |
  | Sneak | C toggles it | 1.0 m/s, capsule 1.15 m, eyes about 1.06 m up |

  Shift or jump stands him out of a sneak, and he won't stand up into a low ceiling. Alt-walk is gone: walking is the default now.
- **Peek:** hold Alt + A or D.
  - The feet stay put and the head eases out up to 0.4 m. A sphere cast stops it 12 cm short of a wall.
  - The view tilts up to 12° and dips 5 cm.
  - The spine bends into the lean (`TarikBody.Stance`).
- **Body while sneaking:** the crowd's crouch clip is a deep forward kneel, which put his hunched shoulder in the lowered view. Instead the body sinks with the capsule. TODO: a Mixamo crouch-walk clip on Hamzah's OK.
- **What the hostiles get:**
  - **Hearing:** `NoiseRadius` is 0 standing still, 1.5 m sneaking, 6 m walking, 15 m running, 18 m sprinting. Idle or alert hostiles inside it come to look.
  - **Sight:** sneaking cuts how far off he's spotted to 20 m (from 40), and spotting him at close range drops from 20 m to 6 m.
  - **Visible points:** `HeadPoint` and `ChestPoint` replace fixed heights. Crouching lowers both; peeking moves the head out past the corner while the chest stays behind it. A peeking Tarik is seen by his head (and his drawn gun), and is hit at 40% of the normal chance.
- **HUD:** "SNEAKING" and "< PEEK" / "PEEK >" at the bottom left.
- **Range:** `range_sneak_eye`, `range_sneak_side`, `range_peek_behind`, `range_peek_right`, `range_peek_third` at a corner block, `PeekCorner`.
- **Tests:**
  - `PlayerSimTest` checks walk, run, sprint and sneak speeds, height and noise. Its wall test now checks that no frame-to-frame step crosses a building; the old infinite-plane test failed on irregular buildings.
  - `CombatTest` checks footsteps by pace, sneak sight, peek visibility, and the lean wall clamp.
- **Fixes the range found:**
  - Fists never handed the arms back when lowered.
  - Holstering mid-reload left the left hand on the reload.

## 3S. MinniGameFramework (built 2026-10-23, branch `feature/minni-game-framework`)
Hamzah: "Lets call this game's framework MinniGameFramework", and "make MinniGameFramework its own folder, assembly and namespace inside LifeInDowntown."
- **Layout:** `Assets/MinniGameFramework/` holds `MinniGameFramework.asmdef` and the namespaces `MinniGameFramework.Core`, `.Player`, `.Combat` and `.NPC`. The folder's `README.md` has the file list and how a game plugs in.
- **What moved in:** 13 scripts, moved with `git mv` together with their `.meta` files, so every scene and prefab link survived. The city scene regenerates clean.
  - `Player`: BodycamPlayer, BodycamRig, TarikBody, Fists, HurtFeedback, BodycamOverlay, PlayerStats.
  - `Combat`: WeaponHolder.
  - `NPC`: Pedestrian, Ragdoll, NpcMelee, Hostile, HostileSpawner.
- **No game references:** every place the framework touched game code is now a `FrameworkHooks` call. That covers the live state (`IPlayerState`, implemented by `GameState` without changing the save format), the backpack as `IArmory`, the game flow, input blocking (backpack, map, phone), driving, the phone banner, wake at home, money text, overloaded, escalator carry, crowd sidewalks, indoor no-fire zones, and dropped loot.
- **The bridge:** `Game/FrameworkBridge` installs the hooks before the first scene loads in a build. `Editor/FrameworkBridgeEditor` installs them when the editor loads, for tests and renders.
- **Stays in the game:** the city generator, phone, missions and story, inventory and shops, crowd, transit, cars, world interactables (`Interactor`, `WorldPickup`, `IndoorZone`), `BodyMicFilter`, `RainFollow`, and the development range and tests (they build from game content).
- **Verified:** the framework builds as its own `MinniGameFramework.dll`, and all 8 suites pass. The range renders look unchanged. A Windows build succeeds with the DLL in it. The exe hasn't been played live.
- **Structure review:**
  - The class names `TarikBody` and `Bodycam*`, and the "Tarik needs to eat" strings in PlayerStats, are game-flavoured inside a generic framework. Renaming them needs Hamzah's "rename".
  - The range and the combat and stealth tests could move into a `MinniGameFramework/Editor` assembly once they take their content (characters, pistol, car) as inputs, not from game paths.

## 4. Later milestones (not scheduled)
1. Port the Godot game's core systems to C#: main menu with character creation, dialogue engine with `{name}`/pronoun templating, save/load, pause and options, the GameState stats.
2. Realistic Tarik (hands/reflection) and NPCs, plus pedestrians and traffic.
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
| 7 | Rocketbox NPCs (3B.2) | done 2026-10-22: 10 adults + 8 clips (831 MB raw, 79 MB converted), `CharacterImport` (humanoid, gendered controllers, HDRP materials), `NpcSetup` 24 walkers on 27 real sidewalk lines + 8 standers outside shops, walk speeds measured from the clips (1.16 / 0.85 m/s). Clip root settings matter: keeping the original root or baking XZ displaced bodies; see `NpcDiagnostic` |
| 8 | Audio + body-mic mix (3B.3) | todo (download list needs OK) |
| 9 | Playtest pass with Hamzah | todo |
| 5d/5e | Remaining look passes (neon, lit windows, trim, props) | after 6-8 |

## 9. Open questions
- Tarik's story: who he is, what he wants, the main cast, how it ends. That replaces the placeholder phone cast and missions.
- Which real condo tower is Tarik's home (section 3E)?
- Real business names on signs (Shoppers, Tim Hortons...) are fine for a personal project; a commercial release would need parody names like GTA uses.
