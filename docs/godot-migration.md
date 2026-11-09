# Moving Risk of Copyright to Godot 4 (C#)

Decision (2026-11-09): port to **Godot 4 with C#**, **solo first**. Co-op comes back later, built on Godot's own
networking. The s&box version stays playable until the Godot build reaches parity.

## Why
- Sell it standalone on Steam on our own terms.
- Engine features we keep faking by hand: real ragdolls, GPU particles, ocean/water shaders, animation blending and IK.
- A stable engine (no hot-reload state loss, no lost material overrides) that the dev already knows.

## What there is to move

| Area | Size | Port effort | Notes |
|---|---|---|---|
| Game code (C#) | ~16,000 lines in 55 files | Medium | Logic ports close to line for line; every engine call changes (see API map). |
| HUD + dev menu (Razor/SCSS) | ~2,500 lines | High | Rebuild as Godot Control scenes. |
| Networking (`[Sync]`, `[Rpc]`) | 15 files, ~100 attributes | Drop | Strip to local calls for solo. Keep the host/owner split in mind for co-op later. |
| Models | 112 FBX (from Blender scripts) | Low | Re-export as glTF from the same scripts. |
| Materials | 99 .vmat, 57 textures | Low/Medium | Become `StandardMaterial3D` or `ShaderMaterial` resources. |
| Shaders | 9 HLSL | Medium | Rewrite in Godot shading language: water, ghost, dissolve, hologram, iridescent, glowbulb, invisible, stylize post, triplanar nature. |
| Scenes | water_level, forest, indoor | Medium | Mostly built from code or editor dressing scripts; re-run those in Godot. |
| s&box citizen model | Zombies, gore segments, and the hidden skeleton driving the robot | High | Not ours to take. Needs our own rig plus animations (see Risks). |
| Sounds | none yet | - | |
| Font | Poppins | Low | OFL licence, ship the .ttf. |

## The big gotchas
1. **Units.** s&box units are inches (39.37 per metre); Godot uses metres. Every distance, speed and force in the
   code is in inches. Convert tunings to metres as they're ported (divide by 39.37) and don't mix the two.
2. **Axes.** s&box is Z-up, X-forward. Godot is Y-up, -Z-forward. Use one conversion helper at import time and keep
   all ported maths in Godot's convention, rather than converting per call.
3. **Animation.** The third-person robot is posed by copying a hidden s&box citizen's animated skeleton. In Godot the
   robot needs its own skeleton and animation set (idle, run, jump, slide, swim, dive/superman, swing). This is the
   largest single art job.
4. **Gore and chunking.** Each shootable part is its own model pivoted at its joint; that maps well to Godot nodes.
   `CreatureGore` and `BodySegments` lean on s&box mesh building and the citizen; rebuild on `ArrayMesh`.

## API map (s&box to Godot)

| s&box | Godot 4 |
|---|---|
| `GameObject` + `Component` | `Node3D` with a C# script (or child nodes) |
| `OnStart` / `OnUpdate` / `OnFixedUpdate` | `_Ready` / `_Process` / `_PhysicsProcess` |
| `Scene.Trace.Ray/Sphere` | `PhysicsDirectSpaceState3D.IntersectRay` / `IntersectShape` (wrap in one `Trace` helper) |
| `ModelRenderer` + `Shapes.Box/Sphere` + `Tint` | `MeshInstance3D` with `BoxMesh`/`SphereMesh`, per-instance colour via instance shader parameter |
| `SkinnedModelRenderer`, `SetBoneTransform` | `Skeleton3D` + `AnimationPlayer`/`AnimationTree`, bone pose overrides |
| `Rigidbody`, `BoxCollider` | `RigidBody3D`, `CollisionShape3D` |
| `PlayerController` + move modes | `CharacterBody3D` with our own movement (port `PlayerMovement.cs`) |
| `CameraComponent`, FOV, post-process | `Camera3D`, `WorldEnvironment` + a full-screen stylize shader |
| `PointLight` | `OmniLight3D` |
| Razor panels + SCSS | `Control` scenes, a shared `Theme` |
| `Input.Down("Duck")`, `ProjectSettings/Input.config` | `Input.IsActionPressed("duck")`, InputMap in `project.godot` |
| `TimeSince` / `TimeUntil` | small ported structs |
| `Game.Random`, `MathX`, `Vector3.Random` | a `Rng` helper and `Mathf` |
| `Scene.TimeScale` (hit-stop) | `Engine.TimeScale` |
| `[Sync]`, `[Rpc.Broadcast]` | removed for solo; `MultiplayerSynchronizer` / RPCs for co-op later |
| `Material.Load` + `Attributes.Set` | `ResourceLoader.Load<Material>`, `SetShaderParameter` / instance uniforms |

## Getting ready now (in s&box)
- [ ] Commit and push everything, then tag it `sbox-final-baseline`. (115 changed files are uncommitted today.)
- [ ] Record a reference capture of each feature (short clips or screenshots) for parity checks.
- [ ] Pull tuning numbers (weapon stats, creature stats, wave tables, dive/jetpack values) out of code into one data
      file per system, so they move as data rather than being hunted through scripts.
- [ ] Add glTF export next to FBX in `Tools/blender/creature_helpers.py` and the weapon scripts.
- [ ] Decide what not to port: the zombies (the game is now sea creatures) and the forest/indoor maps are candidates.
- [ ] Write the feature parity checklist (below) into the GitHub issue.

## Progress (keep this current: the auto-continue job reads it)
- [x] 1. Skeleton project, player movement, camera juice (`367cd92`)
- [x] 3. Rifle, shotgun, axe, weapon HUD (weapons step)
- [x] 4. Bloober end to end + WaveDirector + Health + basic HUD (gore detail still to do: mesh dents, s&box GooJet look, decals art)
- [x] 5. Water level: seabed map, WaterFeel, Jetpack/surge, Sea Spider ally (jet power bar and surge HUD come with the full HUD in 7)
- [x] 6. Dock, dive, crater, surface waves (water shader ported to water.gdshader)
- [ ] 7. Squilien, Junk Crab, MC61, zombies (if kept), skins, dev menu, buy stations, afflictions, full HUD
- [ ] 7b. Robot third-person model + animation set, world gun, PlayerRig poses
- [ ] 7c. Shaders (water, ghost, dissolve, hologram, iridescent, glowbulb, stylize looks), sounds hooks
- [ ] 8. Upgrades: real ragdolls, GPU particles, ocean shader
- [ ] 9. Parity pass against the checklist below, then co-op later

## Port order (Godot)
1. **Skeleton project.** Godot 4 .NET project, folders mirroring `Code/`, InputMap from `Input.config`, units/axes
   helpers, `Trace` helper, stylize post-process.
2. **Player.** Movement (walk, sprint, slide, jump, float), first-person camera and camera juice, health.
3. **Shooting.** Rifle and shotgun view models with their animations, ADS and scope, hit detection, axe melee.
4. **One creature end to end.** Bloober with part chunking, gore and goo, plus `WaveDirector` and a basic HUD.
5. **Water level.** Seabed, `WaterFeel`, jetpack and surge, ally sea spider.
6. **Dock and dive.** Dock house, water surface and waves, the dive, crater and debris.
7. **The rest.** Squilien, Junk Crab, MC61 boss, skins, dev menu, buy stations, afflictions.
8. **Upgrades the move unlocks.** Real ragdolls, GPU particles for sand/bubbles/gore, proper ocean shader.
9. **Co-op** (later).

## Feature parity checklist
Player: walk, sprint, slide (ground and air), jump, float/swim, third-person camera, robot model and skins.
Weapons: rifle (scope), shotgun (shell reload), axe melee (mouse 5), weapon skins, bubble trails.
Movement extras: jetpack thrust and stall, surge charge, slam and ram-through (mouse 4), propeller feet.
Creatures: Bloober, Squilien, Junk Crab, MC61, Sea Spider ally (climb, dock, harpoon, beacon, launch kick).
Systems: waves, health and afflictions, gore and gibs, buy stations, dev menu with search.
Water level: water feel underwater, dock safe house, dive (physics fall, tumbles, saw hands, crushing Bloobers),
crater landing, surface waves and floating barrels.
Look: stylize looks (Comic, Anime, Sketch, Watercolor, Riso, Flat), water shader.

## Risks
- **Robot animation set.** Without the citizen animations we need our own. Options: animate in Blender (scripted
  like the weapon rigs), or retarget a licensed humanoid pack onto the robot skeleton.
- **Feel drift.** Movement and dive feel come from many tuned numbers in inches. Port with the reference captures side by side.
- **UI rebuild time.** The HUD and dev menu are big; port only what the current phase needs.
