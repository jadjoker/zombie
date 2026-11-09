# Sounds

Placeholder sounds made from code by `Tools/godot/make_sounds.py` (no downloads, nothing to license). Rerun it after
tweaking a recipe. To use a real recording instead, drop a file here with the same name (`.ogg`, `.wav` or `.mp3`;
remove the generated `.wav` of that name). Missing names are silent.

| Name | When |
| --- | --- |
| `rifle_shoot`, `shotgun_shoot` | a shot (the weapon's ModelName + `_shoot`) |
| `mag_out`, `mag_in` | reload start and finish (also an ammo purchase) |
| `dry_fire` | trigger with an empty gun and no reserve |
| `weapon_switch` | switching weapons |
| `hit_world`, `hit_flesh` | bullet impacts |
| `axe_swing`, `axe_hit` | melee swing, and a melee hit on a creature or gib |
| `footstep`, `jump`, `land` | walking and running, jumping, landing |
| `dash`, `slide` | dash and slide start |
| `jet_thrust`, `surge` | jetpack thrust (repeats while held) and the surge |
| `hurt`, `death`, `low_health` | player damage, death, heartbeat under 25% health |
| `splat`, `gib` | gore squelches, a piece tearing loose |
| `creature_death` | any creature dying |
| `creature_attack`, `crab_snap` | Bloober bite and spider stab, crab pinch |
| `ink_throw`, `harpoon` | Squilien ink ball, ally spider harpoon |
| `boss_spawn`, `beam_charge`, `beam_fire`, `card_throw` | MC61 |
| `splash`, `swim` | hitting the water surface, swimming |
| `dive_whoosh`, `dive_impact` | the opening dive and its landing |
| `wave_start`, `wave_cleared` | wave banners |
| `buy`, `deny`, `heal` | buy stations |
| `ui_click` | dev menu buttons |
| `bubbles` | spare (not used yet) |

Creatures make wet and mechanical noises only, no voices.
