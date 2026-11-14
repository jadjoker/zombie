# Risk of Copyright

**The game is now the Godot 4.7.2 C# port in `godot/`.** That is where all current work happens and what ships to
itch.io. The s&box project (`Code/`, `Assets/`, the editor rules below) is the original and is left alone.

## Godot port (current)
- Code in `godot/Code` (Core, Game, Player, Weapons, Creatures, Gore, Dock, Test). Levels are `godot/levels/*.json`,
  converted from the s&box scenes; `Level.cs` builds them at load. Main scene: `scenes/water_level.tscn`.
- Engine: `C:/Users/smitt/Projects/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`.
  Build with `dotnet build` in `godot/`.
- Tests: `<godot> --headless --path godot res://scenes/water_level.tscn -- --autotest <flag>`. Flags: `--creatures`,
  `--boss`, `--robot`, `--float`, `--devmenu`, `--stations`, `--playtest1`, `--dive-edge`, `--devclick` and `--pause` (both need a window), and
  `--waves --waveflow` (the real start of a run: dive in, enemies must come), `--sea` (the sea floor dressing). `--hitframes` (on `test_range.tscn`)
  and `--flash` need a window, so no `--headless`. `--shots <dir>` saves screenshots.
- Under `--autotest` the wave director's spawning is paused unless `--waves` is passed, so tests spawn what they
  need. A test that sets up state the real game doesn't can hide bugs: keep `--waveflow` unassisted.
- Checks that must hold use `Check( ok, what )` in `AutoTest.cs`, which prints `[autotest] FAIL`. The release
  script fails on it.
- Creatures derive from `Creature` (`godot/Code/Creatures/Creature.cs`: health, death, the Killed event, the
  `Creature.Everyone` list, spawn and setup). Ones built from shootable limbs (crab, spider, boss) derive from
  `PartCreature`, which owns the part tree, hit capsules, tint flash, cutting limbs off and loose pieces. A new
  creature should not copy that code: derive and override.
- Art direction: simple, lightly cartoonish cel-shaded shapes in flat colours, not overly cartoonish, nothing
  photoreal. The player is a robot: first person arms and hands are robot parts (`robot_arm` and `robot_hand` in
  `Tools/blender/fps_rifle.py`, shared by the shotgun and axe scripts), never gloves.
- The rifle has four designs (Blaster, the default; Carbine; Marksman; Classic): `RIFLE_VARIANT=<name>
  RIFLE_OUT=godot/assets/models/weapons/rifle_<name>` with `fps_rifle.py`, listed in
  `godot/Code/Weapons/RifleModels.cs`, picked in the dev menu (Player, Appearance, Rifle) and saved with the
  settings. Tests take `--rifle <Name>`; `--rifles` (needs a window) tries them all.
- The Kraken event boss (issue #30) is being built in stages: `godot/Code/Creatures/Kraken.cs`, model from
  `Tools/blender/kraken.py` (one glb per part, one mesh per material, painted by mesh name). Stage 1 is in: it
  burrows up, its glass dome soaks shots and shatters, the brain is the weak point, tentacles shoot off, it sinks
  when dead. Stage 2 is in (`KrakenWhirlpool.cs`): a ring of lights at `Kraken.FightRadius`, a 5 second warning
  outside it, then a whirlpool carries the player back in. Spawn it from the dev menu (Enemies, Spawn an enemy,
  Kraken). Stage 3 is in (`KrakenSlam.cs`): it sucks players in for a second, rears up to three tentacles and
  slams them along strike lines that lock just before the hit. Tests: `--kraken` (needs a window for shots),
  `--whirlpool` and `--slam`; they freeze enemies so the Kraken only attacks when the test calls `ForceSlam`.
  Playtest round 1 is in: drawn at `Kraken.Size` (2) times the model, slower, mouth underneath, dry mud-crack
  skin (`python Tools/blender/mudcrack_texture.py`), and `KrakenLife.cs` (springy head and tentacle segments, kept
  above the sea bed). Sizes in the Kraken code are in the model's units unless they say metres: multiply by Size.
  Playtest rounds 2 and 3 are in (`KrakenBurst.cs`): the mouth is its own part shown only during the suck, the
  dome is about 30 panes that crack, pop off and blow out as rigid bodies, and on death it swells and bursts
  into `chunk0..15` plus its tentacles. In `kraken.py`, bake every object's transform before joining (a torus
  left with an offset once threw whole material groups to the origin).
  Playtest round 4 is in: `KrakenGore.cs` (spurting bullet holes, hide tearing off at health steps, craters) and
  a far bigger death burst (46 chunks, teeth, jointed guts, lumps, pieces that burst again, goo rain, a beat
  of slow motion). `kraken.py` writes chunk0..N and the game loads them until one is missing.
  Stage 4 is in (`KrakenSpit.cs`): it bites players at its mouth and spits three globs of ink; both irradiate
  (`PlayerAfflictions.Irradiate`: green glow, slow stamina, and damage over time when poisoned). Test: `--bite`.
  Stage 5 is in (`WaveDirector.RollKraken`, `KrakenSpot`): 10% on wave 7, then odds that climb with the rounds
  since it last came (0, 5, 15, 25, 45, 75, 100%), never on an MC61 wave, coming up near the map centre clear of
  the buy stations. Dev menu (World, waves): Kraken next wave, Wave 7; (Enemies, Wave spawning): Kraken waves.
- Playtest 11/11 (issue #41): the suit malfunction round (`WaveDirector.SuitMalfunctionEvery`, every 4 rounds, you
  float), the Kraken scales 1.2x per visit (health and dome), upgrades cap at 10 (health keeps healing past it),
  slide is a comic head-first dive (lean, fall, bounce, propellers after the landing; `PlayerRig`, `FoldedSlide` is the old pose) with the gun held on the real aim, dock waves and gulls (`DockHouse.DockAmbience`),
  the gun's own bus (`Sfx.GunBus`) filtered underwater. Test: `--round11` (needs a window).
- Controller support (issue #33): `Core/Controls.cs` adds a pad binding to every action at start, tracks the device last
  used (a pad, or keys and mouse) and switches between them, names buttons for the prompts (`Controls.Label`, Xbox or
  PlayStation style) and rumbles (`Controls.Rumble`, also driven by `CameraJuice.Shake`). Right-stick look is in
  `PlayerController._Process`. Aim assist is `Player/AimAssist.cs` (pad only: slows the stick near an enemy and pulls toward it; pause menu slider). The dev menu works by pad (focus outline, bumpers change tab, right stick scrolls, B closes, right trigger places an enemy). Tests: `--pad` and `--padmenus` (inject pad events; headless works).
- The dock radio (issue #42): `Core/Radio.cs` (autoloaded through `Clock`) shuffles the songs in `godot/assets/music`
  (drop more .mp3 or .ogg files in and they join), on its own `Music` bus with the pause menu's Music volume slider; the
  dock builds the radio model (`Tools/blender/dock_props.py`, `DOCK_ONLY=radio`) on the hut table and starts it. Test: `--radio`.
- A dead player has no gun, HUD or input: `PlayerSurvivor.LocalDead` gates them, and `PlayerRig.DeathCamera` (through
  `PlayerController.CameraOverride`) watches the ragdoll. Test for board items #31 to #40: `--board1` (needs a window).
- Rocky Shark (issue #29): a stone-plated serpent in `Code/Creatures/RockyShark.cs`, model from
  `Tools/blender/rocky_shark.py` (parts in `godot/assets/models/creatures/rockyshark`). It swims like the Bloober
  serpent with a livelier tail; the green radiation holes and eyes are weak points, the rock plates turn most shots,
  a section shot off drops the tail behind it, and a bite can irradiate. Spawns from wave 2 (18% of the mix); dev menu
  Enemies, Spawn an enemy, Rocky Shark. Test: `--shark`.
- The sea floor is terrain (issue #35): `Code/Game/SeaTerrain.cs` builds a 224 m height map (the old arena was 76 m) with
  hills, valleys and shelves, a flat basin in the middle (radius `FlatRadius` 30 m: stations, dive crater, Kraken ring)
  and a cliff rim at the edge. `SeaTerrain.Fit` removes the old floor and walls, sets the level's props on the ground,
  moves the cliffs to the rim and spreads copies of the sea life over the hills, and moves the spawn points out to
  44-58 m. Place anything new with `SeaTerrain.HeightAt(x, z)`. Test: `--terrain` (shots need a window);
  `--flat-floor` loads the old flat arena for comparison. Slides survive a moment of air over a crest.
  `AlienReef.cs` adds a coral reef ring round the basin (radius 23-33 m) and patches inside it, recolours about 45% of the
  kelp and coral alien (neon green, black, purple, violet), and 230 glowing floating alien plants (`AlienFloaters`). The
  sea surface has a dark blue hole off the end of the diving board (`water.gdshader` hole uniforms,
  `DockHouse.BlueHoleRadius`). Converted props keep their s&box name in `Level.Kind(node)` (Godot renames duplicates).
- The teleporter and the wreck site (issue #35): after wave 10 `Teleporter.cs` zaps in near the landing spot on clear
  ground (`FindSpot`), and wave 11 waits (`WaveDirector.TeleportAfterWave`, `WaitingForTeleport`) until the player uses
  it. `AreaTwo.Go` rebuilds the ground in place as area 2 (`SeaTerrain.Area`): a flat plain with the sunken liner
  (`ShipWreck.cs`, after the Titanic: heeled 27 degrees, three decks joined by ramps, cabins, a dining saloon, a kitchen,
  broken windows and a breach; boxes merged into a few meshes with box colliders), rebakes the navmesh and starts
  wave 11. A game over there reloads the level. Dev menu: World, Waves, Clear wave 10. Test: `--teleport` (shots
  need a window).
- Playtest batch (wreck site and combat): `VegetationColliders.cs` makes kelp (a stem cylinder), coral, boulders, reef and
  cliffs solid from their own meshes after placement (before the navmesh); `SeaTerrain.Fit` moves most of the basin's
  plants out over the hills; star ferns sit near the sand. Severed pieces (`Gib`) float up under water. `EnemyBars.cs`
  draws the red health bars and the stacking damage numbers. The trident shooter (slot 3, `TridentModel.cs`,
  `TridentBolt.cs`) is the Blaster's rig with its gun stripped and a built-in-code gun on the gun bone; its station and
  the three level stations stand by the arrival pad in area 2 (`AreaTwo.PlaceStations`). `ShipWreck.Kudzu` grows alien
  vines over the hull, deck and deckhouse.
- Atlantis, area 3: after wave 20 on the wreck site the teleporter comes again (`WaveDirector.NextTeleportWave`,
  `SecondTeleportAfterWave`) and `AreaThree.Go` rebuilds the site as Plato's ring city under a giant glass globe
  (`Atlantis.cs`: island temple of Poseidon with a gold statue holding a giant trident, glowing canals, bridges, 8
  avenues, 116 marble/obsidian/crystal buildings, a turning green crystal heart, the globe on a black and gold base).
  Buildings use `MeshKit` (shared with `ShipWreck`). Stations stand behind the arrival pad, spawns on the ring street
  at 65 m, the Kraken comes up in the west plaza (`Atlantis.KrakenCentre`). Test: `--atlantis` (shots need a window).
  The wreck is built at `ShipWreck.Scale` (1.2) through its frame.
- Sea floor props come from `Tools/blender/sea_props.py` and are placed by `godot/Code/Game/SeaDressing.cs`
  (fixed seed). Dock wood textures: `python Tools/blender/wood_textures.py`.
- Release: `bash Tools/godot/release.sh` builds, smoke-tests and pushes to itch.io (`jadjoker/risk-of-copyright:windows`).
- Blender scripts for Godot assets write to `godot/assets` (`buy_station.py`, `dock_house.py`, `fps_rifle.py` with
  `RIFLE_OUT` set, and so on).
- Commits reference the migration (#26) or playtest (#27) issue. Chat-only tasks need no issue.

# The original s&box project

Wave-survival FPS in s&box, published as `jadjoker.risk_of_copyright`. Repo: github.com/jadjoker/zombie, branch `main`.
Alien sea creatures (Bloober, Squilien, Junk Crab, MC61 boss) with part-by-part chunking and gore.

## Rules
- The user often plays in the editor while agents work. Before saving code, editing the scene, or play_start,
  check `sbox-cli --call editor_status` and wait until `IsPlaying` is false (saving code hotloads into their game).
- One agent per set of files. Don't edit files another agent is working on.
- Ask before downloading packages. The user does the publishing.
- Writing style: no em-dashes, keep it short.

## Layout
- `Code/` game code. Enemies in `Code/Enemies` (shared ICreature, CreaturePart, CreatureGore), movement in `Code/Movement`.
- `Assets/` models, materials, scenes. Scenes: `scenes/water_level.scene` (underwater), `scenes/forest.scene` (outdoor clearing), `scenes/indoor.scene` (interior, the startup scene).
- `Tools/blender/` Blender scripts: `creature_helpers.py`, one script per creature, `arena.py`, `forest.py`, `indoor.py`, `mkvmdl.sh` / `mkvmdl_any.sh` write .vmdl files.
- Editor dressing scripts (not compiled into the game): `Tools/arena_dressing.cs`, `Tools/forest_dressing.cs`, `Tools/indoor_dressing.cs`.
- In-game dev menu (s&box): press `P` (`Code/DevMenu.razor`). In the Godot port it is opened from the pause menu (Esc).

## Editor tooling
- sbox-cli: `C:/Program Files (x86)/Steam/steamapps/common/sbox/bin/managed/sbox-cli.exe`
  (`--call editor_status`, `compile_status`, `play_start`, `play_stop`, `run_csharp '{"path":"C:/..."}'`, `camera_screenshot`).
- Editor scene edits: `Editor.SceneEditorSession.Active`, create objects with `scene.CreateObject()`, then `HasUnsavedChanges = true; Save(false)`.

## Blender pipeline
- Blender 5.2: `C:/Program Files (x86)/Steam/steamapps/common/Blender/blender.exe`. Launching it starts the MCP connector (port 9876).
- 1 Blender unit = 1 s&box unit. Export FBX via `export()` in creature_helpers.py; the .vmdl uses import_rotation [0, -90, 0]
  and a default material so nothing shows missing-texture checkers.
- Each shootable part is its own model pivoted at its joint, so chunking code doesn't change.

## Backlog
- Work items live in GitHub Issues (github.com/jadjoker/zombie/issues), tracked on the project board "Risk of Copyright Backlog" (github.com/users/jadjoker/projects/1).
- Every issue goes on the board (the board auto-adds new issues into Workshop; `.github/workflows/presort.yml` moves `bug` and `playtest` issues to Todo, using the PROJECT_TOKEN secret).
- Columns: Workshop (idea still being fleshed out) -> Todo (ready to build) -> In Progress (optional) -> Done.
- Reworks of finished items: a new issue titled "Rework: ..." with the `rework` label, linking the original ("Refs #12"), placed in Todo (or Workshop if unclear). The original stays Done.
- Check open issues before starting work, and note the issue number in commits ("Fixes #12").
- CLI: `"C:/Program Files/GitHub CLI/gh.exe"` (`issue list`, `issue create`). Labels: bug, playtest, idea, workshop, rework, enemy, map, weapon, movement, art, needs-testing.
