// Spawns the MC61 boss, exactly like the dev menu's "MC61 boss" button does.
var d = Game.ActiveScene.GetAllComponents<WaveDirector>().FirstOrDefault();
if ( d is null ) return "no WaveDirector in the running scene";
if ( !d.SpawnBossWaves ) d.SpawnBossWaves = true; // the button is gated on boss waves being enabled
var ok = d.Spawn( WaveDirector.EnemyKind.Boss );
return ok ? "MC61 boss spawned" : "no spawn point found (too close to everyone)";