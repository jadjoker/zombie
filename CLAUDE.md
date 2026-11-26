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
- Tests that place the player or a creature wait for the navmesh (`WaitForNav`) and pick open sand with `OpenLane`
  (stations, rocks and solid plants stand about the basin), never a fixed spot.
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
  float), the Kraken scales 1.2x per visit (health and dome), upgrades cap at 16 (health keeps healing past it),
  slide is a comic head-first dive (lean, fall, bounce, propellers after the landing; `PlayerRig`, `FoldedSlide` is the old pose) with the gun held on the real aim, dock waves and gulls (`DockHouse.DockAmbience`),
  the gun's own bus (`Sfx.GunBus`) filtered underwater. Test: `--round11` (needs a window).
- Controller support (issue #33): `Core/Controls.cs` adds a pad binding to every action at start, tracks the device last
  used (a pad, or keys and mouse) and switches between them, names buttons for the prompts (`Controls.Label`, Xbox or
  PlayStation style) and rumbles (`Controls.Rumble`, also driven by `CameraJuice.Shake`). Right-stick look is in
  `PlayerController._Process`. Aim assist is `Player/AimAssist.cs` (pad only: slows the stick near an enemy and pulls toward it; pause menu slider). Reworked: whole enemies with bubbles sized to them, a sticky target, tracking while you or it move, a pull, a snap onto the sights; the stick has a round dead zone and a turn boost. Gyro aiming (`Controls.GyroRate`: Godot's joypad motion sensors, drift learned while the pad is still; pause menu: Gyro aiming Off/On/While aiming, Gyro sensitivity, Calibrate gyro). A Steam Controller's trackpad and gyro arrive as mouse moves and clicks: with a pad plugged in those don't switch the game to keys and mouse (it was showing "E" and turning aim assist off), and mouse-style look gets the assist too. The dev menu works by pad (focus outline, bumpers change tab, right stick scrolls, B closes, right trigger places an enemy). Tests: `--pad` and `--padmenus` (inject pad events; headless works).
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
  `DockHouse.BlueHoleRadius`), ringed by a low reef after the Great Blue Hole of Belize (`DockHouse.BuildReefRing`, show only).
  `SeaTerrain.Fit` removes the arena's invisible crate and barricade boxes (they were invisible walls). Converted props keep their s&box name in `Level.Kind(node)` (Godot renames duplicates).
- The teleporter and the wreck site (issue #35): after wave 10 `Teleporter.cs` zaps in near the landing spot on clear
  ground (`FindSpot`), and wave 11 waits (`WaveDirector.TeleportAfterWave`, `WaitingForTeleport`) until the player uses
  it. `AreaTwo.Go` rebuilds the ground in place as area 2 (`SeaTerrain.Area`): a flat plain with the sunken liner
  (`ShipWreck.cs`, after the Titanic: heeled 27 degrees, three decks joined by ramps, cabins, a dining saloon, a kitchen,
  broken windows and a breach; boxes merged into a few meshes with box colliders), rebakes the navmesh and starts
  wave 11. Teleporting fades to black behind `LoadingScreen` (a drawn postcard of the dock, the destination, a tip). A game over there reloads the level. Dev menu: World, Waves, Clear wave 10. Test: `--teleport` (shots
  need a window).
- Playtest batch (wreck site and combat): `VegetationColliders.cs` makes kelp (a stem cylinder), coral, boulders, reef and
  cliffs solid from their own meshes after placement (before the navmesh); cliffs and reef rocks are solid all the way
  through (`Wedges`: each skin piece joined to the rock's middle; they used to be hollow shells with gaps on top that
  players and enemies fell into) and tagged `RockMeta`. Swimmers don't collide with rocks: `Sculpt.InBounds` keeps them
  `EdgeClear` (32 m) inside the map edge, short of the rim cliffs, and lifts one that is inside a rock out over its top
  (`Sculpt.OutOfRock`). Test: `--rocks`; `SeaTerrain.Fit` moves most of the basin's
  plants out over the hills; star ferns sit near the sand. Severed pieces (`Gib`) float up under water. `EnemyBars.cs`
  draws the red health bars and the stacking damage numbers. The trident shooter (slot 3, `TridentModel.cs`,
  `TridentBolt.cs`) is the Blaster's rig with its gun stripped and a built-in-code gun on the gun bone; its station and
  the three level stations stand by the arrival pad in area 2 (`AreaTwo.PlaceStations`). A pull starts the charge and letting go fires; damage rises with the hold from `ChargeMinDamage` (75) to
  `Damage` (250) at a full 1 s charge (`PlayerWeapon.ChargeTime`, `ShotDamage`), which is also faster and magical. Every trident goes through the first enemy it hits, then heat seeks the next closest `TridentBolt.SeekNext` (2, plus one per Trident Pierce point) within `SeekRange` (20 m, +2 m per Trident Reach point) at `SeekSpeed` (0.45 of Speed; only enemies stop it while seeking) and goes through them. It steers at the nearest of the target's hit shapes (`AimAt`; aiming at the middle sent it circling open water beside a Rocky Shark). Test: `--tridentseek`. Its sounds (`trident_pierce`,
  `trident_flesh`, `trident_charge`, `bubble_pop`) come from `Tools/godot/make_sounds.py`; trail bubbles pop (`TridentBolt.Pop`). `ShipWreck.Kudzu` grows alien
  vines over the hull, deck and deckhouse.
- Robot classes (`Player/RobotClass.cs`): each robot has its own weapons and mouse 4 move; both swing the axe.
  Ram-Bot: rifle and shotgun, the charged surge (`Jetpack`). Vac-Bot: the Gore Vac (slot 1) and the Bubble Cannon
  (slot 2), and the reverse-thrust blink (`Jetpack.TryBlink`, `RobotClass.Blink`: a tap bursts you the way you move
  for `BlinkCost` power, burning `BlinkGunk` from the tank for `BlinkGunkBoost` more distance and a goo trail; it
  never rams). Picked on the dock at the start of a run (`Game/ClassPicker.cs`, pauses the game; 1 or 2, a click or
  the pad; the pause menu's "Change robot" until the dive), in the dev menu (Player, Robot class), saved as
  `game/robot_class`; `--class <name>` forces one (tests default to Ram-Bot). `RobotClass.Pick` swaps the kit
  (`WeaponInventory.Replace`, damage upgrades kept). No area builds a trident or Gore Vac station any more:
  weapons come with the robot (`RobotClass.Sells` and `BuyStation.Refresh` still hide one if it turns up).
  Trident-Bot (250 health, `Paint` "Trident": teal, #CB4154 shoulders, a plain yellow frame and black metal (the camo
  was dropped, user's choice); in third person the spear's thrust and the surge stab the weapon out along the aim (`PlayerRig.AlongAim`); its surge thrusts the weapon in hand, spear or shooter, forward with a violet glow on its head, `TridentSurge.cs`, `PlayerWeapon.SurgePose`): the trident shooter (slot 1) and the spear
  (slot 2, `PlayerWeapon.Spear`, `SpearModel.cs`): a one-handed thrust (`SpearGrip` bends the right wrist so the fist closes round the shaft; `ThrustCurve`: cock, lunge, overshoot, camera
  jolt) at twice the axe's reach, and the energy shield in the left hand (`ShieldArm`, a `SkeletonModifier3D` that
  bends the left arm to the shield, its forearm through a leather `Strap`; carried edge on at the side, raised to the left of the view with right click to take `BlockShare` (65%) of each hit. Healing in
  third person brings bristlenose plecos with hearts (`HealPlecos.cs`). Vac-Bot's `Paint` is "Vac": green and purple
  camo (`PlayerRig.Camo`, wrapped noise blotches as an object-space triplanar texture) and yellow shoulders. With the
  shield up `ShieldArm` shrinks the left arm away and hides the strap, so no hand shows in first person. Test: `--tridentbot`.
  The Bubble Cannon (`Weapons/BubbleCannon.cs`, model `BubbleCannonModel` on the Blaster rig; the globe's "Loaded"
  bubble regrows after a shot and swells while charging): left click blows a `BubbleShot` (grows out of the bell,
  stretches and wobbles, gentle homing via `GoreBall.NearestToLine`, drifts up, bounces off the world `Bounces` times).
  A tap blows a bubble on release; holding past `PlayerWeapon.TapWindow` charges instead (the Gore Vac's huge shot works the same), and letting go full (`ChargeTime`) throws a Mega Bubble: big and slow, it rolls through and wraps up to `MegaCatches`
  light enemies, splashes heavy ones for triple. A `Pullable` enemy is caught in a `BubbleTrap`: `Creature.Bubbled`
  turns its brain off (`Creature.Frozen` gates every creature's Think), it floats up (`RiseHeight`, `MegaRise`),
  struggles (film bulges, it twists), bubbles push apart. A bubble fired into a trapped enemy inflates it (`Inflate`:
  higher, longer). It pops after `HoldTime`, on a hit of `PopOnHit`+ (bleed ticks don't count), on right click
  (`PopAll`), or in a chain from a pop near it (`ChainDelay`, each link `ChainBonus` harder); the pop splashes within
  `PopRadius` and throws film shards and a fading ring (`BubbleFilm.Shards`, the film shader's `fade` instance
  uniform); the enemy drops and takes `LandDamage` scaled by the height, shoving what is near. In-flight bubble
  splashes spare bubbled enemies. Sounds: `bubble_blow`, `bubble_charge`, `bubble_trap`, `bubble_bounce`,
  `bubble_burst` (`make_sounds.py`). The HUD shows "N BUBBLED". Bubbled crabs and spiders move freely (hits and
  `Pull` skip the navmesh snap), so the Gore Vac can reel a bubble in.
  Test: `--classes` (with `--shots`: the picker, a bubbled crab, a Mega Bubble, a pop, the gun).
- The Gore Vac (slot 4, slot 1 for Vac-Bot, `Weapons/GoreVac.cs` as a `PlayerWeapon` partial with `Vacuum` set, model `GoreVacModel.cs` on
  the Blaster's rig like the trident) is all pushes and pulls. Controls (user's choice): right click sucks, left click
  shoots (hold it to charge the huge shot), reload blows. The suction has no range limit (`VacRange`; the pull weakens
  to half by `FalloffReach`) and shows as a `SuctionCone`: a spiral-shader water cone from the nozzle with silt specks
  whirling in (the old flying white blobs were dropped as broken-looking). It takes gibs (`GoreSystem.Gibs`) and goo
  puddles (`GoreSystem.Slurp`) into the tank (Ammo, 0 to 100; ammo stations do not fill it); drags enemies by
  `Creature.Pull` at `PullStrength / Weight` (Weight is max health; crab and spider override Pull to move their nav
  agent; bosses are not `Pullable`), held `HoldOff` from the nozzle; sucks a light weak one apart; tears off limbs at
  half health or less (`PartCreature.Limbs`); tears up starfish, shells and urchins (`SeaDressing.LooseGroup`) as dry
  gibs; tears the nearest sea mine within `MineReach` off its chain (`SeaMine.TearLoose`) and loads it. Caught gibs
  (`SuckIn`) ease off the floor, swirl in with the vortex, speed up, stop colliding and shrink (`Gib.Suck`) over the
  last `ShrinkReach`, then `Gulp` (pitch climbing with a streak, tank slosh, HUD pop). It takes many pieces to fill:
  `GibGunkMin`/`Max`, `PuddleGunk`, `EnemyGunk` (fractions carry over). Shots (`GoreBall`) are made easy to
  land: aim assist picks an enemy just beside the crosshair (`NearestToLine`), they fly fast and nearly straight, home
  gently, and go off near an enemy (`Proximity`); every burst is a mini explosion (`AlienBlast`, goo, damage, a shove).
  The huge shot throws four bomblets; a loaded mine goes first as a full blast. Shots kick the player back
  (`LastKick`); a full tank slows you by `FullTankSlow` and sways the view. Lumps a burst throws are `Gib.Recycled`
  (worth 1 gunk), so there is no free ammo loop. It is Vac-Bot's only (no station sells it). Dev menu: Cheats, Give Gore Vac.
  Test: `--gorevac` (spawning paused, the level's mines cleared; with `--shots` for pictures).
- Atlantis, area 3: after wave 20 on the wreck site the teleporter comes again (`WaveDirector.NextTeleportWave`,
  `SecondTeleportAfterWave`) and `AreaThree.Go` rebuilds the site as Plato's ring city under a giant glass globe
  (`Atlantis.cs`: island temple of Poseidon with a gold statue of Poseidon, dry canals a storey down (`CanalFloorY`, a lower floor
  dressed with rocks, coral, kelp and ruins; `SeaTerrain` sinks the ground under them), bridges, 8
  avenues, 116 marble/obsidian/crystal buildings, a turning green crystal heart, the globe on a black and gold base).
  Buildings use `MeshKit` (shared with `ShipWreck`). Stations stand behind the arrival pad, spawns on the ring street
  at 65 m, the Kraken comes up in the west plaza (`Atlantis.KrakenCentre`). Test: `--atlantis` (shots need a window).
  The wreck is built at `ShipWreck.Scale` (1.2) through its frame.
  The island statue is Poseidon's own rig in gold (`Poseidon.BuildRig`, `BuildStatue`); the globe glass is a fresnel shader. Districts have their own roof colours, vines, banners, coral gardens and plazas
  (fountains, broken columns); arches span the avenues; `AtlantisFish` schools swim through. The canal floor has two market stands (one sells cheese), barrels and
  sacks of gold (`MarketStand`, `Barrel`, `GoldBag`). Dev menu: World, Waves, Teleport to Atlantis.
- Poseidon (wave 30 boss, `WaveDirector.PoseidonWave`, takes MC61's place that wave) and the seahorse: built in code
  with `Sculpt` helpers (`Code/Creatures/Poseidon.cs`, `Seahorse.cs`). Poseidon floats and swims (tail wave), vanishes
  and reappears through whirlpools near the player inside the map (`Sculpt.InBounds`), throws tridents with the
  player trident's bubbles, throws a whirlpool that carries the player to him for a stab, and summons seahorses
  (`WaveDirector.Adopt` puts them in the wave). Dev menu: Enemies (spawn both, Poseidon attacks), World (Poseidon
  wave). Test: `--poseidon`. Looks: gold armour and collar, veins (`Veins` in `BuildRig`), tail scales baked into meshes with
  `ScaleShader` (hex lines), hands pinned to the chest's colour. His whirlpool sweeps the player up and carries them back
  to him (`Grab` with the vortex); on death he swells and bursts into loose pieces (`Burst`). The held trident is leant
  off the forearm in `Animate` so it never passes through his arm.
  Smooth pass (like the mermaids, on `SmoothKit`): `PoseidonLook.cs` builds him from a lofted torso, round-ended limbs,
  smooth balls and fans; a stern sea god's face (white eyes with glowing green irises and lids, brow ridge, nose, fin
  ears, knitting brows on pivots, moustache, a beard on a `Jaw` that drops when he roars), skinned hair and beard locks,
  a crown with rims, one skinned tail painted by `GodTail` (jewel scales, gold bands). The statue uses the same rig
  (`Rig.Pose` after posing). On the burst `TailPiece` gives each flung tail section its own mesh. `PoseidonMotion.cs`:
  springs for lean, twist, rise, bank, head look; dolphin kick; a coiled throw with an aiming fist and follow-through; a
  rearing, roaring summon; a coiled lunge for the stab; hair and beard flung out in a whirlpool spin; a capped flinch.
  Test: `--poseidongallery` (body, face, three-quarter, side; `--motion` adds throw, summon and swim frames; needs a window).
  `SmoothKit` lofts and skinned tubes wind clockwise from outside (Godot's front faces); before this fix they were
  inside out and the far wall showed through. The sea glows purple, blue and green (`water.gdshader` `alien_mix`, `veins`).
- Davey ("Davey phone home", `Creatures/DaveyJones.cs`, `EnemyKind.Davey`): the wave 20 boss (`WaveDirector.DaveyWave`,
  takes MC61's place, the last wave on the wreck site; health `DaveyHealth` 11000), an alien ghost pirate built in code: smooth lofted alien head with black
  almond eyes and a shut mouth, a man's lofted torso, a full beard, a bicorne (`HatMesh`: teal, gold trim, skull and crossbones, red feather), belt and buckle, one skinned curling tail (`SmoothKit.SkinChain`), a cutlass and a hook, all in
  `GhostShader` (translucent, fresnel glow, `fade` for vanishing). Attacks: vanish (gone 2 to 4 s, back behind the
  player, a swipe), cannons (`DaveyCannon`: three rise round the player, fuses lit one after another with
  `cannon_fuse`, one `Cannonball` each, then fade), sink (the player is slammed in, sinks, 10 a second, mashes jump
  10 to 12 times; `DaveyJones.Sinking`, the HUD's MASH prompt). His health stops at half until he grabs the player and
  takes them to `Game/DaveyLocker.cs` (a plank room 150 m under the map with a locker marked "Davey's"); there spawning
  stops and other creatures are `Frozen`; 6 s after he dies there the player goes back up (`ScreenFade`). Sounds
  `cannon_fuse`, `cannon_fire`, `ghost_vanish`, `ghost_appear` (`make_sounds.py`). Dev menu: Enemies (spawn, attacks,
  to half), World (Davey wave). Test: `--davey` (shots need a window).
- Mermaids (`Code/Creatures/Mermaid.cs`, `EnemyKind.Mermaid`): Atlantis only (`WaveDirector.MermaidChance` of each wave
  there), about player height (`Mermaid.Scale` of Poseidon's units), Poseidon's tail (`Poseidon.BuildTail`) and drifting
  skin, a coral-grown scallop shell mask with glowing eyes and spiky teeth, fin hands. Women have red, blonde or black
  hair and shell tops; men gold helmets. Trident ones swim in and stab behind a honeycomb energy shield held in
  the left fist (part `Mermaid.ShieldIndex`; each hex cell stops shots until it shatters at `CellHealth`, then shots go
  through the hole, `ShotThrough`; head and tail stay open); about a third are archers that keep away and
  shoot `MermaidArrow`s. Slower, 30% more health than a Bloober. Dev menu: Enemies, Spawn an enemy, Mermaid. Test:
  `--mermaids` (shots need a window).
  Rebuilt (the mermaid pass) in `Creatures/MermaidLook.cs`: alien but lightly cartoony. A lofted torso, a tail that is one
  smooth skinned mesh on a `Skeleton3D` (a bone per tail joint, `PoseChains` copies the joints onto the bones each frame;
  `TailPieceFrom` gives the flung piece its own mesh on death) painted by the `TailScales` shader (rows of scales, hip
  colour to tip colour), a two-lobed fluke, hip and ridge fins, a fin frill at the waist; one smooth head with big
  glossy black almond eyes (glowing iris, glint), pointed ear fins, gill slits, a mouth that is a line until it attacks
  and then gapes on needle teeth (`maw`); women have long hair, a shell tiara and shell top, men a gold helmet with a fin
  crest, shoulder plates and belt. Skin is the `BodySkin` shader (one colour, paler front; the head and hands without).
  Smoothing pass: every round part is a smooth mesh (`Loft` through `Smooth`ed spline rings, 32-segment balls, `Tube`s
  with rounded ends); fins and shells are one curved `FanMesh` each (fins use the `FinShade` shader: rays, rim, and a
  ripple driven by `beat`); hair is seven skinned three-joint locks (`SkinChain`, posed by `PoseChains`); meshes are
  cached and shared. Motion is `Creatures/MermaidMotion.cs`: springs (`Spring`) for lean, shoulder twist, bank, head
  look and tail swing; a dolphin-kick tail wave with the body pitching against it; a coiled wind-up, a lunging stab with
  an overshoot, an archer turning side-on to draw and rocking back on release; a hit flinch; flowing hair; gills flare.
  Test: `--mermaidgallery` (the four kinds lined up, faces, side, each one close; `--motion` adds side-on frames of one
  swimming in; needs a window).
- MC-51 is MC61's name in the game (the class and files keep `MC61`). His spin attack launches 5 to 8 `HomingMissile`s
  (`Code/Creatures/HomingMissile.cs`: slim bendy one-eyed rockets, black face, silver, rust and black body, 30 health, slower since playtest (dive 6.6 m/s), climb 6.7 to 10.7 s (as high as before) then dive, 15 damage) instead
  of cards; `AlienBlast` is the green, purple and yellow explosion. Sea mines (`Code/Game/SeaMine.cs`, `SeaMine.Lay`
  after each area is built): `PerArea` (16) at random spots over the whole area every run (kept off the middle,
  spawns, stations, the player and each other), on chains of random length (a `HighShare` high up, to 10 m), drifting;
  shots tink off, touching one costs 45. One that goes off or is torn away is replaced at a new random spot after
  `RespawnMin`/`Max` seconds, rising out of the sand (`QueueRespawn`; `RespawnScale` speeds it up for tests).
  Test: `--missiles` (shots need a window).
  MC-51's seahorse look: the body and tail wear `fx_mosaic.gdshader` (`FxMaterials.Mosaic`: teal, aqua and deep blue
  stained-glass cells in yellow leading; `MC61.Look` is `Surface.Mosaic`), the tail curls tighter and ends in a spiral
  (`BuildCurl`: one swept tube replacing the last segment, so no seam), navy spikes run up his back (`BackSpikes`, placed from the body mesh), the
  yellow leading is bump-shaded as a raised bead (`bead_height`), and the gloved hands are fins (`BuildFin`, flapped by `FlapFins`; the old hand rig code stands aside).
  No suit: the body and head are built in code side on like a seahorse (`SeahorseBody`, `SeahorseHead`: swept
  mosaic tubes and `Lump`s, a long snout, a coronet), the one eye smaller on the brow (`EyeScale`), the hat on the
  coronet, a rippling dorsal fin above the tail (`DorsalFin`), the pectoral fins tucked in at the neck (`FinAnchor`, `Tuck`). Test: `--mc51` (shots need a window).
  Smooth pass: denser lumps and sweeps, 16-sided spikes, the tail sections are code tubes with seahorse ridges
  (`TailTube`, round tops hide the joints; the old tail models are no longer loaded) into the curl, and the top hat is
  built in code (`TopHat`, fitted to the old hat model's bounds) with a gold band. Motion: springs lean him into his
  travel and bank him into turns, his body rocks in a slow S against his head, the fins flutter, the tail uncurls with
  speed and winds tighter to fire, the beam kicks him back as it fires, each missile snaps his snout up, a hit makes him
  flinch (at most once a second), and the hat rides on its own spring. `--mc51 --motion` adds frames of each move.
- Skill trees (user's design, replacing the pickup drops): `Player/Skills.cs` (`SkillId`, `SkillDef`, `Skills.All`, the
  per-player `PlayerSkills` node: ranks, `Tokens`, `Earned`). Each robot has three trees (primary, secondary, core), each
  three rows of two skills and a capstone; a skill takes 5 points (3 for the capstones, Biden Blast, Trident Swarm,
  Clusterfunk, Big Ball Energy, Mother Jelly, Gooquake), and `Skills.UnlockPoints` (3) in a row open the next. Clearing a wave gives a token
  (`WaveDirector`, up to `Skills.MaxTokens` 50 a run); a new run clears them; no refunds (user's choice). Pausing opens
  the trees (`Game/SkillTreePage.cs`; click or A buys a point); the arrow on the right (or a bumper) goes to the menu
  page. Skills that were pickups (`SkillDef.Perk`) feed `PlayerPerks.Count`, so their code stays in PlayerPerks; the
  rest hook in where the weapon works (`Weapons/PlayerWeaponSkills.cs`, `Player/JetpackSkills.cs`, GoreVac, BubbleCannon,
  PlayerMelee, Health for Bubble Prison) with their numbers in `Weapons/SkillFx.cs` (also burn/bleed `Affliction`, the
  Full Choke slug, `ToxicTrail`, the Phase Shift `Decoy` (enemies go for `PlayerSurvivor.WorldPosition`; player-body
  checks use `RealPosition`), `SurgeWhirlpool`, and the `BlackHole`: the huge Gore Vac shot opens it, it drags enemies
  in for `Life` s (bent light via a screen-texture lens, accretion disc, horizon rim, negative light), then bursts; the gore it swallows goes into the Gore Vac's tank, 2 a piece, `PlayerWeapon.HoleGunkPer`; recycled lumps stay at 0.1).
  Weak spots for Headhunter are `Creature.WeakSpot`. Icons: `python Tools/godot/make_skill_icons.py`. Dev menu: Player,
  Skill trees. Test: `--skills` (with `--shots` in a window: the tree page and the black hole).
- Hook-Bot and Coral-Bot (robots 4 and 5, each with three skill trees like the others). Hook-Bot (150 health, `Paint`
  "Hook"; `Weapons/HookBot.cs`, numbers in `HookFx`): the Grab Reel (user's design after a physics gun; model
  `HookModels.GrabGun`) holds left click to lift a `Pullable` enemy on a green beam (`GrabbedBody`, `Creature.Grabbed`:
  brain off, moved freely like `Bubbled`, `Creature.Carried`); swinging the view slams it into the world and bowls
  other enemies over, damage by how fast it really moved (`ImpactDamage`, `ImpactRef`, capped at `ImpactMax`, so
  pressing it into the floor does nothing); letting go flings it, right click throws it (a throw charge from the
  magazine, back every `ChargeRegen` s); it flies, tumbles, bounces off walls and lands stunned. Heavy enemies can't be
  lifted: the beam locks on for `BeamDps`. Catch of the Day: Heavy Hands, Long Arm, Strong Arm, Crusher, Bowling, Deep
  Quiver (throw charges), Depth Charge (a thrown enemy bursts where it hits); the
  Lantern Lure lobs a `LanternLure` that is the player's decoy while it glows (`PlayerSurvivor.Decoy` is a `DecoyBase`,
  shared with Phase Shift), zaps what is near and bursts (Abyssal Bloom: `AbyssJaw`); mouse 4 is the grapple zip
  (`RobotClass.Grapple`, `Player/JetpackMoves.cs`, the blink's surge along a cast line). Coral-Bot (200 health, `Paint`
  "Coral"; `Weapons/CoralBot.cs`, `CoralFx`): the Spine Gun grows `CoralGrowth` on enemies (slows them, shatters at
  `ShatterAt`), Reef Seeds plant a `PolypTurret` (left) or a `CoralWall` (right), `ReefLinks` runs Healing Reef and
  Living Reef's veins; mouse 4 burrows (`RobotClass.Burrow`: untouchable, `PlayerController.EyeDrop`, then an eruption;
  `BurrowCooldown` so it can't be chained). Their guns are built in code on the Blaster's rig with `GunKit`
  (`HookModels`, `CoralModels`). Balance (user's ask: nothing one-shot all game): skill blows go to health through
  `SkillFx.HitPart`, and skill effects scale with half the damage upgrades (`PlayerWeapon.UpgradeScale`). Tests:
  `--hookbot`, `--coralbot` (both print hits-to-kill for crabs by wave; shots need a window, with a third person portrait
  of each). Their suits have their own patterns (`PlayerRig.BuoyTexture`: Hook-Bot's slanted orange and white life
  buoy bands with navy rope lines; `PolypTexture`: Coral-Bot's pink with orange polyp cups) and eye glow (lantern green,
  polyp pink; `CharacterSkins.GlowOf`).
- Goo-Bot, Plunger-Bot and Pinball-Bot (robots 6 to 8, the physics robots; three skill trees each). Balls that
  bounce share `Weapons/BotBall.cs` (a sphere trace a step, bounces off the world, bumpers off enemies, rolls, `Strike`
  lands a hit like a shot). Goo-Bot (replaced Magnet-Bot, user's design; 175 health, `Paint` "Goo": grape purple with
  magenta drips and bubbles, `PlayerRigGoo.cs`: `GooTexture`, a glass goo tank on its back and a goo drop antenna on bone
  attachments; `Weapons/GooBot.cs`, `GooFx`; the old "magnetbot" id loads it): the Goo Gun (60/240, 10 a shot, so 6
  shots; blobs home gently and stick, building a `GooBall` on the enemy: lumps on its nearest parts that swell, jiggle,
  drip and slow it; each blob on a ball hits 10 harder (`BlobDamage`), up to 6; it pops `PopTime` (4 s) after its last
  blob, every blob restarting the clock, at once on the 6th or if the enemy dies, for `PopPerBlob` a blob in
  `PopRadius`), the Jelly Cannon (a `Jelly` that squashes on an enemy and arcs to the nearest one it hasn't hit, `Hops`
  times, smaller each hop; each hit is a blob on that enemy's ball); mouse 4 is the splat jump (`RobotClass.Splat`,
  `Player/JetpackGoo.cs`: bounce up, slam down the way you look at the top, the splat glues and goos the enemies near).
  `GooPuddle` is the puddles (sticky ones slow), `GooBoulder` the Big Ball Energy boulder. The goo look is one shader
  (`GooFx.Goo`, `Jelly`: cel-shaded, translucent, dark core, bright rim, bubbles, per-instance `wobble` and `glow`).
  Plunger-Bot (175, "Plunger"; `PlungerBot.cs`, `PlungerFx`): plungers stick (`StuckPlunger`) and pop off at
  `PopAt` with a SHLORP, right click yanks them back; the Flush Bomb toilet (`FlushBomb`) whirlpools enemies in and
  geysers them up; mouse 4 slings you off a plunger on a bungee (`Sling`) into a belly flop. Pinball-Bot (150,
  "Pinball"; `PinballBot.cs`, `PinballFx`; the deep pass): hold to pull the launcher's plunger (the shot has the Gore Vac's aim
  assist: `GoreBall.NearestToLine` at launch, then `Pinball.Seek` homes gently until the first enemy hit, only when it would miss); a `Pinball` hitting
  an enemy is a pop bumper hit (`BotBall.BounceOffEnemy`): a `BumpPause` beat, a `BumperFlash`, the user's hit sounds
  (`pinball_hit_1..6`, cut from their recordings), a `PinballPopup` (x2, JACKPOT!), and a kick out at `BumperKick`+
  aimed most of the way (`ChainAim`) at the next enemy (`PinballFx.NextTarget`, `AimPoint`), flattened along the floor.
  The Flipper Arms are real flippers (user's choice): a click raises that flipper (the flip), holding keeps it up;
  flips launch light enemies as `HumanPinball`s that bounce off other enemies; a hit out at the tip (`SweetFrom`) is
  PERFECT; a held flipper cradles your balls (`Pinball.Catch`, on the view model's flipper tip) and a flip as one rolls
  off after letting go is a SKILL SHOT. Skills: Rebound (more chain hits), Sweet Spot, Launch Pad (human pinballs),
  Ball Return, Ramp (human pinballs crash down). Models: `Tools/blender/pinball_guns.py` (gun space, exported without
  the Y-up turn) to `pinball_launcher.glb` and `pinball_flippers.glb`, posed by `PinballModels.PoseLauncher` and
  `PoseFlippers` (plunger, spring, ball count lights, flippers on the tilted deck); the plunger at the back pulls on its
  own as the trigger is held (a hand pulling it was tried and dropped, user's choice). Mouse 4 is the roll
  (`JetpackToys.RollStep` through `PlayerController.MoveOverride`): third person while it lasts, a glass pinball shell,
  its own momentum (`JetpackToys.ShellRadius` 1.02 m so the tucked robot stays inside; steer, faster downhill, wall bounces, a hop on jump); enemies take `RollDamagePer` a m/s, light
  ones fly off as `HumanPinball`s, heavy ones bounce it off. Guns built with `GunKit` (`GooModels`, `PlungerModels`,
  `PinballModels`); suits `GooTexture`, `TileTexture`, `PlayfieldTexture`. Balance as for Hook and Coral: skill
  effects scale with `UpgradeScale`; each test prints hits to kill a crab by wave. The picker shows two rows of four.
  Sounds `goo_shot`, `goo_stick`, `goo_pop`, `jelly_fire`, `jelly_boing`, `plunger_pop`, `plunger_shlorp`, `boing`, `flush`, `pinball_ding`, `flipper`.
  Tests: `--goobot`, `--plungerbot`, `--pinballbot`.
- Third person poses for the mouse 4 moves (`Player/PlayerRigMoves.cs`, hooked into `PlayerRig.PlaceFrame` and `Pose`):
  the burrow lies face down in the sand digging, the eruption spins out with the arms up, the grapple reaches along
  the line, the sling and the splat jump fly superman and lands in a
  starfish belly flop, the roll tucks into a ball that rolls along the ground. The Pinball-Bot's third person guns
  flip and squash like the ones in view (`PinballParts`). Test: `--moveposes` (shots need a window; it slows time).
- Online co-op (`Code/Net`, up to 4, ENet on UDP `Net.DefaultPort` 24565): host or join on the robot picker (`ClassPicker.OnlineRow`), or
  `-- --host` / `-- --join <address>`. `Net` lives at /root/Clock/Net and carries every message with the sender's peer id.
  Each machine runs its own robot; others are `PlayerFactory.CreateRemote` copies (`PlayerController.IsRemote`, `PeerId`,
  `Robot`, `SeenFromOutside`) moved by their owner's state (`NetPlayer`, 30 a second) and driven by their owner's button
  presses through `controller.In` (`PlayerInput`: `LocalInput` or `RemoteInput`). Player and weapon code must read
  `controller.In`, never Godot's Input, and gate on this machine's menus only for the local robot (`PlayerWeapon.LocalBlocked`).
  The host runs every creature; joiners get numbered puppets (`NetCreatures`, `Creature.Puppet`: brain off, placed from
  snapshots, `PuppetMoved` for leg animation). Every creature's `ReceiveShot` starts with `NetCreatures.Intercept` and
  creature `Health.OnDamage` goes through `InterceptHealth`: the host sends each hit and joiners replay it (so limbs and
  deaths match); a joiner's own shots never hurt a puppet. Enemy hits on another player's robot go to its owner
  (`NetHits`); shoves and grabs (Scripted) the host's creatures put on it are sent on (`NetPlayer.HostSide`, own moves
  inside `Sfx.Remote` scopes don't count). Remote robots' "own" sounds play at the robot (`Sfx.Remote`). Waves, game
  over, restarts, teleporters and areas follow the host (`NetWorld`, `WaveDirector.ApplyNet`); wave bonuses, tokens and
  respawns for a remote robot are sent to its owner. Bubbles on puppets come from the host (`Net.Bubble`). Online the
  menus don't pause the game. A joiner's own hits on puppets are sent to the host as requests (`ShotRequest`,
  `HealthRequest`), so they land where the shooter saw them; the host's copies of remote robots never deal damage themselves.
  Black holes open where their owner's shot burst (`Net.BlackHoleAt`). Davey's locker takes everyone (`Net.LockerGo`).
  The co-op HUD is `Net/TeamHud.cs`: names and health over teammates, `Pings` (middle mouse, pad left stick click),
  the Tab (pad View) scoreboard, `Spectate` (dead online: watch a teammate, click to switch). Names come from the
  picker's Name box (`GameSettings.PlayerName`). Emotes (`Player/PlayerRigEmotes.cs`, `EmoteWheel`; T or hold the pad's
  D-pad down): the Griddy, Wave hi, Wanker, rig poses in the move-pose style with a camera in front; moving or firing
  ends one; online the others see it (`Net.EmoteNow`). Test: `--emotes` (shots need a window).
  Coral walls hold enemies back by pushing them out of their footprint (`CoralWall.HoldBack`; enemies don't move by physics).
  Enemy attacks online: a creature's `NetWrite`/`NetRead` add its attack state (move, timer, aim, a shot counter) to each
  snapshot so a joiner's puppet draws it; puppet projectiles (mermaid arrows, Squilien ink, Kraken ink, Poseidon's
  tridents, Davey's cannons) are harmless copies (damage 0). Done for the angler, MC-51, crab, spider, Bloober, Rocky
  Shark, Squilien, mermaid, and the bosses (`KrakenNet.cs`, `PoseidonNet.cs`, `DaveyNet.cs`). `--nettest` checks the
  hook, beam, slam, trident and cannons show on the joiner.
  Test: `--nettest` with one copy `--host` and one `--join 127.0.0.1 --class vacbot` (CI runs both).
- Fish swarms (`Creatures/FishSwarm.cs`, user's ask): a regular wave enemy (`EnemyKind.FishSwarm`, `WaveDirector.FishChance`
  of each wave from `FishFromWave` 2, at most `MaxFish` 3 alive, health `FishBaseHealth` + `FishHealthPerWave`); none are
  set about the map. One swarm is one creature (one health bar); its fish die as its health falls (`Thin`, the nearest
  to the blow first). A wave swarm (`SpawnHunter`, `Hunter`) comes from a spawn point already on the nearest player,
  circles the body at arm's length below the eyes and nips (`BiteDamage`, less as it thins), never gives up or comes
  back. (`Spawn` still makes the old idle kind that loops a home and respawns.) Joiners get puppets (`NetCreatures`
  "FishSwarm"). A dead fish is a ragdoll (`KillFish`: head, body and tail as gibs on cone-twist joints) and
  `TearChance` of them come apart, spurting. Looks (`Creatures/FishLooks.cs`, user's ask: less plain): each swarm has a colour (`FishLooks.Colours`: rust, yellow,
  green and more) and a `Theme` (most of its fish that kind, one MultiMesh per kind): plain (back to belly shading, bars on
  some colours), undead (rotted hide, a gash with bare ribs, a milky glowing eye, a brain bulging out of the skull that
  throbs and pops up, `BrainShader`; dead ones lose it as a `Fish Brain` gib) or alien (glowing spots, a lure, back spikes,
  extra glowing eyes, neon fins, green blood). The fish are drawn at `FishSwarm.Small` (0.5) of the decorative schools, with hit capsules a little bigger than them. A swarm is 40 to 60 fish in a tight shoal that circles the player as one knot (`Pack` spaces them so none overlap, with room for the wag and bob; the ring keeps the spacing by arc length); `RagdollShare` of the dead become ragdolls, the rest tear into gore scraps (`GibKit.Chips`). The brain is one lump (folds and the middle groove are drawn by `BrainShader`; `alive` 0 for the loose one). Joiners get the theme and seed. Dev menu: Enemies, Spawn an enemy, Fish swarm. Test: `--fish` (with `--shots`, each kind close up).
- Robot oil and dismemberment (`Player/RobotOil.cs`, `RobotRagdoll.BreakApart`): hits spray black oil and sparks, hard
  hits splatter oil on the visor, a badly hurt robot leaks oil and sparks; on death the ragdoll loses 2 to 4 limbs (the
  head now and then) as robot pieces with chrome joints, oil gushing from both ends.
- Pickups: a dead creature drops one thing at most (`PerkPickup.MaybeDrop`: 30% ammo, then the 0.5% pirate chest, 4000
  points; `PlayerPerks.Drops`). The other old pickups are skills now (above), at per-point numbers in `PlayerPerks`:
  rifle reload -10% and fire rate +8% a point, explosive round `ThrownClip` (heat seeking), electric bullets
  `LightningArc` (25%, +5% a point), the shield through `Health.Absorb` (25, +10), movement +0.3 m/s and +10% stamina,
  Trident Pierce (+1 seek), Reach (+2 m), Swarm (at most 3: 1 point 30% of one small half-damage trident; 2 points 50%
  of one, 30% of two; 3 points 70%, 50%, 30%; `TridentBolt.Splinter`), Shield Bulwark (+3% blocked, 65% up to 80%),
  the Biden Blast on the `blast` action (one a wave per point, up to 3): G (RB on a pad, which fires it instead of the
  axe only while it is ready). `PlayerPerks.Give` still works (dev menu, tests). Icons: `python
  Tools/godot/make_pickup_icons.py`. Dev menu: Player, Pickups. Test: `--pickups`.
- Quality of life batch: `Creatures/Blinker.cs` blinks eyes on creatures with no lids of their own (Bloober, Rocky Shark,
  Kraken, Poseidon, mermaids, seahorses, missiles: it squashes each eye's meshes shut). The Junk Crab's eyes are on ant
  antennas (`BuildAntenna`, `WaveAntennas`: a waving shaft and a springy whip) with the Kraken's eye on the tip (teal bulb, glossy black almond slanted 38 degrees, glowing green rim).
  Atlantis overhangs are solid (roof slabs, porch roofs, shrine roofs, arch caps, the base rim, the market awnings), and
  it has small details on their own seed (`Atlantis.detail`, so the layout never shifts): doorsteps, scallops, planters,
  lanterns, shrine candles and braziers, starfish, clams and pebbles on the avenues, bubble vents in the canals
  (`AtlantisVents`). Test: `--crab` (shots need a window); `--atlantis` checks standing on an arch and a porch roof.
- The clutter pass (visual clarity): `AlienFloaters.cs` is glow bulbs, about 80 in drifting schools (teal, violet; 1 in
  40 gold), a hazard (the star ferns were removed, user's choice). A bulb a player comes within 3.5 m of swells and flashes for
  `PuffTime`, then bursts (20 to players within 3.2 m, once per school thanks to `BlastGrace`; 80 to enemies there); a
  shot bursts it at once (`GlowBulb`, an `IShootable` on the creature layer: 25 points, gold 150 and a pickup). They grow
  back after 45 s. None inside the wreck (too tight to step back).
  Scenery keeps off neon green (that is for enemies, weak points and pickups); `AlienReef.FadeNear` dithers plants away
  within 2 m of the camera. `SeaTerrain.Fit` grows kelp and coral in groves with open sand between and keeps the lanes
  from the spawn points clear. (A warm rim overlay on enemies was tried and removed: they keep their own look.) `Atmosphere.cs` (node "Backdrop"): light shafts
  (`fx_light_shaft.gdshader`) out past the basin and leviathan silhouettes (`fx_silhouette.gdshader`, no fog) circling
  high over the rim. Marine snow is round and fades near the camera. Mood goes far away or high up, never between the
  player and the enemies. Tests: `--bulbs`, `--clarity` (shots need a window: before and after views of each area).
- Gibs (the chunking pass): `Gore/GibKit.cs` builds detailed pieces in code (curved shell shards with a pale underside,
  faceted rocks with fresh broken faces, flesh chunks with skin, fat and meat, smooth jelly lumps, torn ends with a skin
  rim, meat, bone and sinew) and throws them with a hull collider. `Gore/GibRecipes.cs` says what each creature is made
  of and its special pieces (crab claws, legs, gills; jelly tentacle tips and cores; shark crater stones, teeth, fins;
  MC-51 mosaic cells, spikes, fins; Kraken sucker strips and teeth; mermaid scales, fins, shell mask; seahorse rings;
  Poseidon's gold plates). A creature's `Gibs` recipe drives `GibKit.Chips` (shot), `GibKit.Burst` (death) and torn
  stumps (`CreatureGore.AddStump(..., recipe)`). `Gib.Stuff` makes stone, armour and glass sink, shell drift down, jelly
  and flesh float, with their own landing sounds. Each recipe builds a few template variants once (in a hidden node,
  warmed a frame at a time for the shark, MC-51 and Kraken) and copies them; random creature colours are snapped to a
  few shades so recipes are shared. Tests: `--gibs` (each creature blown apart, shots need a window), `--gibgallery`
  (each recipe's pieces up close, and the burst build time).
- UI themes (`Core/HudTheme.cs`, drawn by `Core/UiStyle.cs`): twelve HUD looks, picked in the dev menu (Look tab,
  "HUD theme") and saved in settings.cfg (`game/hud_theme`); `--hudtheme <name or letter>` forces one. Arcade Dive is
  the default (user's choice: chunky outlined Lilita numbers, suit frames, glossy pills). The others are the mockup
  letters A to K: Brass Diving Gear (porthole health), Robot Visor (small sizes and a minimal scanline on purpose,
  user requests), Bubble Pop, Ink Comic, Copyright Stickers (fine print jokes), Sonar (health in a sonar screen with
  real enemy blips), Fish Tank (sloshing tank), Deep Glow (tentacle lights), Dive Computer (LCD), Arcade Dive Juiced,
  Diving-Bell Robot (brass plus the visor). A theme is data: palette, number font, BigLook, FrameLook, BarLook,
  CrossLook, HealthLook, Scale, Tilt, visor Scan/Rim/Glitch. `UiStyle.Theme` setter fires `UiStyle.Changed`;
  GameHud, WeaponHud and HudOverlays rebuild their labels on it, PauseMenu restyles. Sizes in HUD code are Arcade
  Dive's, wrapped in `UiStyle.S()` so each theme scales them. `Game/VisorOverlay.cs` draws the scanline, rim and hit
  glitch only for themes that set them (`Glitch`, `Jitter` shift the HUD sideways on hits of 12+).
  Fonts in `assets/fonts` (SIL OFL, licences beside them): Lilita One (`UiStyle.Display`), Chakra Petch
  (`Label`/`LabelBold`/`Caption`; the project default via `gui/theme/custom_font`), Share Tech Mono (`Mono`);
  `UiStyle.Number` is the theme's number font. Use `UiStyle.Big`/`Tag`/`Text`/`Sub` for labels,
  `DrawBig`/`DrawTag`/`DrawPills`/`DrawFrame` in `_Draw`, `UiStyle.Frame` (plus `DrawFrameExtras` on the panel's
  Draw signal) for panels and `UiStyle.Menu` as a menu's theme; colours are `UiStyle.Teal`, `Gold`, `Ink` and
  friends (theme properties), not inline hex. Test: `--hudshots` (firing, a boss bar, the pause menu; needs a
  window); add `--allthemes` for one shot per theme, switched live.
- The deep sea angler (`Creatures/Angler.cs`, `EnemyKind.Angler`): from the second stage on (`WaveDirector.AnglerChance` of
  each wave on the wreck site and in Atlantis, at most `MaxAnglers` (3) alive at once, base health 370). Tall round body (a `SmoothKit` loft) in
  blue and green scales, about 30% yellow, with purple spots (its own `SkinShader`, after Poseidon's tail), a jutting underbite of huge needle
  teeth on a hinged `Jaw`, two black almond alien eyes on green rims like the other enemies' (the weak spots), and a whole lantern glowing
  green on a jointed stalk off its brow. Attacks: a lantern swing (wind-up, whip; `SwingRange`), a chomp (jaw wide,
  lunge, snap; `ChompRange`; the two take turns), and the hook (`HookThink`: the jaw gapes, a barbed bone claw on a line
  shoots up to `HookRange` 8 m, pierces the player, reels them in slowly with `Scripted` set, then bites; shooting off
  `HookBreak` of its health snaps the line). Dev menu: Enemies, Spawn an enemy, Angler. Test: `--angler` (shots need a window).
  The angler pass: a deep sea hide (`SkinShader`, user's redesign: navy back to teal flanks and a pale belly, fine
  tone-on-tone scales, dark mottling, purple spots with glowing rims, yellow freckles, slowly pulsing green alien veins,
  photophores down each flank), lips, a red mouth, glowing teeth, warts, back spines, short glowing chin barbels, the
  eyes set close into the head. Gore (`AnglerGore.cs`): every hit that
  doesn't kill wounds and oozes (`Bleed`), the hide bloodies as it is hurt (the skin's `bruise`) and badly hurt it
  bleeds as it swims; parts have their own health (`EyeShare`, `LanternShare`, `JawShare`, `FinShare`): an eye bursts,
  the lantern is shot off its stalk (no more swings), the jaw is torn off (no bites or hooks), fins rip, and mouth hits
  knock teeth out (every third, plus `ToothChance`); on death it comes apart (`FallApart`: jaw, lantern, fins, eyes,
  tail and the oozing carcass as loose gibs, with a burst).
- The sting ray (`Creatures/Stingray.cs`, `StingrayLook.cs`, `StingrayGore.cs`, `EnemyKind.Stingray`, user's design): from the second
  stage (`WaveDirector.StingrayChance` of each wave on the wreck site and in Atlantis, at most `MaxStingrays` (4) alive, the angler's
  health). One flat disc mesh built in code; its top draws the sea bed's own sand texture at its world position (camouflage), with a
  slightly darker rim and dim alien spots (sneaky, not invisible); a pale belly with glowing gill slits and a smiling mouth; the
  classic alien eyes on its head; a whip tail with a glowing barb. The wings move in the vertex shader (`DiscShader`: a wave nose to
  tail while gliding, strokes to climb, a droop to dive, a bank). It glides low (`CruiseHeight`), comes round behind the player and
  freezes flat while watched from afar (`Watched`). Zap (`ZapRange`): it rears up charging and zaps, stunning the player
  `StunTime` (0.6 s) through `Electrocuted` (holds `PlayerController.Scripted`, bolts over the robot and the view; online the host's
  hold carries over). Dive (`DiveMin`..`DiveMax`): it flaps up `DiveHeight`, aims, then nose dives spinning in a whirlpool cone,
  taking `DiveDamageScale` (half) damage (also `AreaDamageScale`), and pulls out over the sand. The ground (user's ask): its height
  keeps clear of the highest ground under its nose, wing tips, tail and the spot ahead (rays plus `Sculpt.GroundAt`, the higher),
  it tilts to the slope and is never let under `MinClear`; walls are felt with a sphere. Sounds `ray_zap`, `ray_charge`. Dev menu:
  Enemies, Spawn an enemy, Sting ray. Test: `--stingray` (checks the lowest clearance over the hills; shots need a window).
- King Raymond IV (`Creatures/StingrayKing.cs`, `KingRaymond : Stingray`, `EnemyKind.KingRaymond`, user's design): the wave 15 boss
  (`WaveDirector.KingRaymondWave`, takes MC-51's place; `BossKind` picks each boss wave's boss; health `KingRaymondHealth` 10000), the
  sting ray at `KingSize` (3) times with a jewelled gold crown like Poseidon's (part 7, rings when shot, comes off on death). The
  sting ray's code takes `Size` (heights, reaches, walls) and `King`; the king's acts follow the ray's in `Act`. Five attacks, his
  move names on the HUD in gold (`Line`) and "KING RAYMOND" with a carved "IV" on the boss bar (`HudOverlays.RomanNumeral`): the zap
  (stuns `StunSeconds` 1 s), the nose dive (half damage, a shockwave where it lands, `DiveShock`), the carpet bomb (climbs
  `BombHeight`, marks on the sand follow the player then lock `BombLockBefore` before the run, like the Kraken's strike lines; a
  `RayBomb` per mark), the sand ambush (`Burrow`, `Buried`: only the crown and eyes over the sand, a glowing spot `EruptCue` before
  he bursts up and launches the player with `PlayerController.Jump`), and the shock rings (warning rings, then `RingCount`
  `ShockRing`s across the ground; a player in the air clears them). Cues and rings follow the ground (`GroundRing`). Every copy
  draws the cues, bombs and rings from the synced state (`KingFrame`); only the host's do damage. The dive pulls out by the pose's
  own ground need and `KeepOut` lifts the body after posing, so no part goes into the ground. Dev menu: Enemies, Spawn an enemy,
  King Raymond IV; Enemies, Kraken (the boss attacks); World, Waves, King Raymond wave. Test: `--kingray` (shots need a window).
- Garden eels (`Creatures/GardenEel.cs`, `GardenEelLook.cs`, user's design after spotted garden eels): scenery, not the round
  (never in the wave's `alive` list, no pickups). `EelBed.Lay` (from `AreaTwo.Go` and `AreaThree.Go`, host and offline only)
  puts 4 to 6 beds of 2 to 4 eels packed 0.45 to 0.75 m apart on open sand (Atlantis: the dry canals); a bed grows a new eel
  in a dead one's hole after 45 to 75 s. Hidden in the sand (`IsEnemy` false: no bar, no aim assist) until a player comes within
  `WakeRange`; then it digs out (a hole appears), stands swaying with its head on them, and nips anyone within `Reach` (bed mates never go through each other: each pose is checked against the neighbours' `Spine` and bent round or leant less, `Crowded`)
  (`BiteDamage` 4); a shot sends it ducking into its hole. The hole (`ShowHole`) is laid on the ground in the floor's own sand (`Stingray.Sand`, `SeaTerrain.ShadeAt`; Atlantis `CanalSand`) and fades out at its edge. One skinned spotted tube (`SpotShader`), its skin green, purple or orange (`Skins`, `Tint`, sent to joiners) on a chain built hanging
  down and turned upright; angry head (yellow eyes, slanted brows, a frown, an underbite of teeth on a jaw). Dev menu:
  Enemies, Garden eels. Test: `--eels` (shots need a window).
- The puffer bomber (`Creatures/PufferBomber.cs`, `PufferLook.cs`, `EnemyKind.Puffer`, user's pick): a wave enemy from
  `WaveDirector.PufferFromWave` (3), `PufferChance` of each wave, at most `MaxPuffers` (4), weak on purpose (60 + 8 a wave). A
  spotted pufferfish with the classic alien eyes; within `ArmRange` it lights its fuse (`FuseTime` 1.6 s: swells to `Swell`,
  spines stand up, its skin goes hot red flashing on each `puffer_tick`, a red ring on the sand at `WarnRadius` = `SpineRange`)
  and creeps on, then bursts (`Burst`, also however it dies): a blast and a ring of `PufferSpine`s that hurt players and other
  enemies (`PlayerPerks.HurtCreatures`, `PlayerPerks.Hurt`), so shooting it early by other enemies turns it on them. Hit spheres
  sit on `body` (scaled evenly; the wobble is on `look`, Jolt refuses uneven scale). Test: `--puffer` (shots need a window).
- The hermit crab squatter (`Creatures/HermitCrab.cs`, `HermitShells.cs`, `EnemyKind.HermitCrab`, user's pick): `HermitShare`
  of the crabs from `HermitFromWave` (2), a `JunkCrab` (its `BuildArmour`, `Exposed`, `PanicUntil` hooks) living in one of eight
  shells built in code (`ShellKind`: toilet, diving helmet, garbage can, recycling bin, tea cup, disco ball (`HermitDisco.cs`: spins at `DiscoSpin`, glints twinkle, sparkle particles, coloured spots sweep the sand; off when the shell falls apart),
  milk carton, a classic spiral whelk (`Spiral`, in place of the croissant, user's choice; turned by `SB.Rotate` so its mouth faces down onto the crab's back); the toilet, can, bin, empty cup
  and opened carton are worn upside down over the crab (`SB.UpsideDown`), rims kept above the legs so nothing clips, and
  each crab's shell leans back and to a side a little), drawn `ShellScale` (1.8) up, each cut into thick tiles with jagged edges (`SB.Patch`) plus lids and handles,
  one template per kind. Shots on the shell (part 1) pass `ShellPass` through and knock off the pieces nearest the hit as its
  health falls (`Crumble`); a shot over a hole goes through to the crab; at none the rest falls apart (`ShellFallsApart`) and the
  naked crab (pink tail, soft) panics, then comes back `NakedSpeed` faster. Pieces are gibs of their stuff (porcelain sinks, the
  carton floats). Online: `Shell` is sent. Test: `--hermit` (shots need a window).
- The cleaner wrasse (`Creatures/CleanerWrasse.cs`, `CleanerLook.cs`, `CleanerWard.cs`, `EnemyKind.Cleaner`, user's design): a healer
  from `WaveDirector.CleanerFromWave` (6), `CleanerChance` of each wave, at most `MaxCleaners` (2), weak (50 + 6 a wave). A 0.7 m
  bluestreak-style fish (pale front, electric blue back, a black stripe widening to the tail with a glowing edge, small classic
  alien eyes). It picks a patient (big and hurt first, one wrasse each, `Treats`: no bosses, wrasses, missiles, fish swarms or
  scenery), hides behind it from the nearest player and links with a green beam (bubbles along it): the patient heals
  `HealShare` (7%, user's pick) of its full health a second and wears a `CleanerWard`, a see-through teal bubble with faint
  hexagons fitted round its meshes, that fills to `ShieldMax` (`WaveDirector.CleanerShield`: 100 on wave 6, +15 a wave) in
  `FillTime` (2 s, user's pick), ripples when hit, shatters at nothing, regrows `RecoverTime` after breaking and stays `Linger`
  after the link drops. Every shot and health hit on a creature goes through it first (`NetCreatures.Intercept` and
  `InterceptHealth` take the damage by ref and call `Shielded`, on the host or offline; snapshots carry each creature's shield
  for joiners, `CleanerWard.FromHost`). The enemy bars show it as a teal bar. Aimed at for `AimReact` (0.5 s) or shot, it darts
  aside within `LinkRange` (8 m), drops the link, then comes back; at most once every `DartEvery`. Dev menu: Enemies, Spawn an
  enemy, Cleaner wrasse. Test: `--cleaner` (shots need a window).
- The mimic octopus (`Creatures/MimicOctopus.cs`, `MimicLook.cs`, user's pick): scenery like the garden eels (never in the
  round). Each sea mine (`SeaMine.PlaceRandom`, respawns too) and glow bulb (`AlienFloaters.AddBulb`) laid is a mimic
  `MimicOctopus.Chance` (0.2%) of the time, host and offline only, disguised with the mine's own ball and chain
  (`SeaMine.MakeBall`, `MakeChain`, `LayChain`) or a bulb. Within `RevealRange` (or shot) it gives itself away (colours run
  over it), drops to the sand, crawls after the nearest player and lashes an arm out (`Lash`) to grab them: held
  (`Scripted`), drawn in, squeezed and bitten; the HUD says CUT THE TENTACLE OFF! (`HudOverlays`, `HoldingLocal`). Any melee
  strike by the held player cuts the arm off (`PlayerMelee.Swung`, raised for the axe, the spear and anything striking through
  `PlayerMelee`), frees them and takes `CutShare` of its health; shooting the arm (part 2, `LashHealth`) does too. Eight arms
  (skinned chains with suckers). Dev menu: Enemies, Mimic octopus. Test: `--mimic` (shots need a window).
- The Squilien swims like a jellyfish (user's reference video): each stroke the bell squeezes narrow and tall, then
  relaxes wide and flat on an underdamped spring (the jelly wobble), and the tentacles trail and sway slowly instead of
  whipping (`Squilien.PoseBody`). Test: `--jelly` (with `--shots` in a window, a side-on frame every 0.1 s).
- Sea floor props come from `Tools/blender/sea_props.py` and are placed by `godot/Code/Game/SeaDressing.cs`
  (fixed seed). Dock wood textures: `python Tools/blender/wood_textures.py`.
- Desktop shortcut: run `Tools/godot/make_desktop_shortcut.bat` once; the shortcut runs `play_latest.bat` (pulls main, builds, plays through `play_dev.bat`; it imports assets on every run, since a never-imported model shows up missing).
- Release: `bash Tools/godot/release.sh` builds, smoke-tests and pushes to itch.io (`jadjoker/risk-of-copyright:windows`).
  Every push to `main` also publishes (a push to a `claude/**` branch is built and tested, not uploaded):
  `.github/workflows/itch.yml` builds, exports, runs the headless tests and `butler push`es the Windows build (channel
  `windows`) and the tested Linux build (channel `linux`, for the Steam Machine) (needs the `BUTLER_API_KEY` repo secret; the
  page is `Tools/godot/itch_target.txt` unless the `ITCH_TARGET` repo variable is set).
- Blender scripts for Godot assets write to `godot/assets` (`buy_station.py`, `dock_house.py`, `fps_rifle.py` with
  `RIFLE_OUT` set, and so on).
- Commits reference the migration (#26) or playtest (#27) issue. Chat-only tasks need no issue.
- Merging: once a change passes CI on its `claude/**` branch, merge it to `main` (which publishes) without asking (user's standing instruction).

# The original s&box project

Wave-survival FPS in s&box, published as `jadjoker.risk_of_copyright`. Repo: github.com/jadjoker/zombie, branch `main`.
Alien sea creatures (Bloober, Squilien, Junk Crab, MC61 boss) with part-by-part chunking and gore.

- Performance (`--perf`, a window: frame times idle, in a gore-heavy fight, with the Gore Vac sucking and in a
  shot barrage, plus per-label code times from `Core/Perf.cs`; `--perfsplit` shows what each big subtree and the
  per-frame nodes cost). Patterns kept on purpose: goo drops are one `GooDroplet` system drawn as a MultiMesh (no node
  per drop); `AlienBlast` shards and smoke are MultiMesh batches with shared per-colour materials; `GoreBall` reads
  enemy part positions once per frame (`EnemyParts`); gibs set gravity/drag only on change and use continuous
  collision only while fast; all `Sway` plants are moved by one driver; `UiStyle.DrawFrame` and the HUD widgets reuse
  their style boxes; alien floaters far from the player update every other frame. Don't reintroduce per-frame
  allocations (LINQ `ToList`, new StyleBoxes, group queries) in hot paths.
  The Godot perf pass (Nov 2026): `--perftypes` switches off each code type's per-frame work in turn (paired
  before/after) in idle and in a fight; `--perfcreatures` times ten of each enemy (`Perf.Time` labels in their
  `_Process`); `--perf` logs each hitch frame (its code labels, GC counts and render pipelines built; `--warmup` runs
  the dock warm-up as in play). Kept on purpose: `NavAgent` asks the navmesh's closest point only every `SnapEvery`
  (it cost ~0.5 ms a call) and keeps the feet on the floor by a ray (plus the navmesh's height) between, and plans paths
  at most every `RepathEvery`; `Shapes.Tint` only touches a material when its colour changed; HUD labels go through
  `LabelSet.Show`/`FontColour` (setting a label's text relays it out); far sea mines, swaying plants and alien floaters
  update in turns; gib templates are shared between colour shades (`GibRecipe.Shape`, `GibKit.Recolour`), built
  at most two a frame, prewarmed when each creature spawns and in batches on the dock; `ShaderWarmup` draws the
  common effects once on the dock so their first-draw stalls happen there; `Sfx.PreloadAll` loads every sound on a
  worker thread at start; the Gore Vac catches resting pieces a few a frame. Mass deaths: past `Creature.DeathBudgetMs` a frame the falling
  apart waits for the next frame (`RunLateDeaths`, Killed still fires at once); a killing shot skips the hit's wound
  (`Creature.Lethal`); goo spray rays run in the next physics tick; the goo decal textures and the visor splats are
  loaded at level start and held (a texture no decal uses leaves the decal atlas, rebuilt when it comes back). `--perf`
  kills the dozen inside the measurement and splits each hitch into process, deferred calls, frame end, draw, physics.

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
