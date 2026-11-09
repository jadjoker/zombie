# Risk of Copyright (Godot 4 port)

Godot 4.7.2 .NET (C#) port of the s&box game. Plan and progress: `../docs/godot-migration.md`, issue #26.

## Run
- Editor: open this folder with `C:\Users\smitt\Projects\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe`, then F5.
- Build only: `dotnet build` in this folder.
- Movement checks: `Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest --shots <folder>` prints speeds in s&box units and saves screenshots.

## Conventions
- Godot convention everywhere: metres, Y up, -Z forward.
- Ported tunings keep their s&box numbers times `U.In` (inches to metres) so they can be checked against the s&box code.
- `Angles` keeps s&box eye angles (pitch down positive, yaw left positive) and converts with `ToBasis()`.
- Collision layers stand in for s&box tags (`Layers`).

## Ported so far
- `PlayerController`: s&box walk mode (friction 4, accel 10, air accel 10 capped at 30, stop speed 100, gravity 850,
  jump 300, step 18, ground angle 45, coyote 0.12, ducking 72 to 36), eye angles, first and third person camera.
- `PlayerMovement`: run 320 / sprint 470, slide with boost and slope speed, momentum carry, air jump with stamina pips,
  wall kick and wall slide, mantle, speed FOV, slide lean, wall roll, landing dip.
- `CameraJuice`: shake and FOV kick.
- `TestRange`: a code-built movement range (ramps, stairs, ledges, walls, downhill).
- Weapons: rifle and shotgun first-person models loaded straight from the s&box FBX files (animations merged from
  the `name@anim.fbx` files), weapon skins from the .vmat colours, `ViewModel` (draw, fire, reload, shell reload,
  sprint tuck, sway, bob, aim poses, scope tuck), `PlayerWeapon` (hitscan, pellets, spread, recoil, ADS slow-down,
  scope zoom and sensitivity, reloads), `WeaponInventory` (1/2, wheel), `PlayerMelee` (rigged axe, mouse 5, sweep fan),
  `WeaponHud` (centre dot, hitmarker, ammo, scope overlay), `TestTarget` dummies.
- Not yet: sounds (the s&box ones were Facepunch cloud assets), the stock particle effects (stand-ins in `Fx`),
  the third-person world gun (comes with the robot model), bullet bubble trails (with the water port).
- Creatures: `Bloober` (all behaviour, chunking, puffing, venom bites), `CreaturePart`, `Shapes` (creature skins, spindles),
  `Health`, `GoreSystem` / `GooDroplet` / `Gib` / `CreatureGore` (wounds, stumps, overkill, jiggle, twitch).
- Game: `WaveDirector` (waves, float rounds, boss waves, open world, dev tools; kinds plug in via `Spawners`), `GameHud`
  (wave, status, banners, kills, points, health, venom, camera goo, game over), `PlayerSurvivor`, `PlayerAfflictions`.
- Test range keys: B spawns a Bloober, O toggles wave spawning (`--waves` starts them on).
- Water level: `Level` builds `levels/water_level.json` (from `Tools/godot/convert_scene.py`), navmesh baked at load (`Nav`,
  `NavAgent`), `WaterFeel` (blend, gravity, drag, sway, muffle, bubbles), `WaterBubble`, `MarineSnow`, `Jetpack`
  (thrust, stall, dock and launch kick, charged surge, slams), `SeaSpider` (IK legs, eye, lure, climbing, ally brain,
  harpoons), `SpiderStrand`, `AllyCommander` (Z).
- Dock: `DockHouse` (build, physics dive, tumbles, landing ragdoll, impact), `water.gdshader` + `WaterWaves`, `WaveFloater`,
  `WaterSurfaceFx`, `SplashFx`, `DiveHands`, `DiveRush`, `DiveCrater` (ground-hugging mesh, hot core), `DebrisChunk`,
  `SandCloud`, dive HUD in `GameHud`.
- Step 7: `Squilien` + `InkBall`, `JunkCrab`, `MC61` (skinned hands curled by code, painted live eye, beam, card rings,
  worm dance) + `Card` curses, `BuyStation` + `PlayerUse` (E), `HudOverlays` (hurt/venom/burn/slow glows from
  `vignette.gdshader`, ink blots, stamina pips, jet bar, slam flash, surge lines, boss bar and speech, buy prompt),
  `DevMenu` (P: tabs, search, spawning and placing, cheats, skins, noclip, free camera, FOV, sun, time scale, travel,
  stats), `CharacterSkins` (data; applied by the robot in 7b).
- Step 7b: `PlayerRig` (the survivor robot: s&box two-bone IK, slide, swim, surge, jet-foot propellers, axe swing,
  paint jobs from `CharacterSkins`, over-the-shoulder camera from PlayerAppearance). The citizen animation driver
  is replaced by a small procedural one (walk cycle, crouch, lean, aim pitch, hands on the gun). The world gun is our
  first-person gun model with its arm surfaces hidden, playing idle/reload. Test: `--creatures --robot`.
- Zombies are not ported: they need the s&box citizen model (Facepunch's, not ours) and are off in the s&box game too.
  The `Zombie` kind has no spawner, so the dev menu's Zombie row does nothing.
- Autotest flags: `--creatures` (test range: Squilien, crab, MC61; add `--boss`, `--rest-hands`, `--devmenu`),
  `--stations` (water level: buy stations and HUD overlays).
