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
