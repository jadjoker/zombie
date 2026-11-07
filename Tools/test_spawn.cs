// Test aid: spawn one enemy on demand via the wave director, like the dev menu button does.
var d = Game.ActiveScene.GetAllComponents<WaveDirector>().FirstOrDefault();
if ( d is null ) return "no WaveDirector in the active scene";
if ( d.SpawningPaused ) d.SpawningPaused = false;
var ok = d.Spawn( WaveDirector.EnemyKind.Crab );
return ok ? "crab spawned" : "could not find a spawn point";
