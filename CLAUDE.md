# Risk of Copyright

**The game is now the Godot 4.7.2 C# port in `godot/`.** That is where all current work happens and what ships to
itch.io. The s&box project (`Code/`, `Assets/`, the editor rules below) is the original and is left alone.

## Godot port (current)
- Code in `godot/Code` (Core, Game, Player, Weapons, Creatures, Gore, Dock, Test). Levels are `godot/levels/*.json`,
  converted from the s&box scenes; `Level.cs` builds them at load. Main scene: `scenes/water_level.tscn`.
- Engine: `C:/Users/smitt/Projects/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`.
  Build with `dotnet build` in `godot/`.
- Tests: `<godot> --headless --path godot res://scenes/water_level.tscn -- --autotest <flag>`. Flags: `--creatures`,
  `--boss`, `--robot`, `--float`, `--devmenu`, `--stations`, `--playtest1`, `--dive-edge`, `--devclick`, and
  `--waves --waveflow` (the real start of a run: dive in, enemies must come). `--hitframes` (on `test_range.tscn`)
  and `--flash` need a window, so no `--headless`. `--shots <dir>` saves screenshots.
- Under `--autotest` the wave director's spawning is paused unless `--waves` is passed, so tests spawn what they
  need. A test that sets up state the real game doesn't can hide bugs: keep `--waveflow` unassisted.
- Checks that must hold use `Check( ok, what )` in `AutoTest.cs`, which prints `[autotest] FAIL`. The release
  script fails on it.
- Creatures derive from `Creature` (`godot/Code/Creatures/Creature.cs`: health, death, the Killed event, the
  `Creature.Everyone` list, spawn and setup). Ones built from shootable limbs (crab, spider, boss) derive from
  `PartCreature`, which owns the part tree, hit capsules, tint flash, cutting limbs off and loose pieces. A new
  creature should not copy that code: derive and override.
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
- In-game dev menu: press `P` (`Code/DevMenu.razor`).

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
