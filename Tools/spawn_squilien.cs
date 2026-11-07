// Test aid: pull the Squilien (spawning one if needed) in front of the camera.
var d = Game.ActiveScene.GetAllComponents<WaveDirector>().FirstOrDefault();
var s = Game.ActiveScene.GetAllComponents<Squilien>().FirstOrDefault();
if ( s is null && d is not null ) { d.SpawningPaused = false; d.Spawn( WaveDirector.EnemyKind.Squilien ); s = Game.ActiveScene.GetAllComponents<Squilien>().FirstOrDefault(); }
if ( s is null ) return "none";
var cam = Game.ActiveScene.Camera;
s.WorldPosition = cam.WorldPosition + cam.WorldRotation.Forward * 110f;
s.WorldRotation = Rotation.LookAt( cam.WorldPosition - s.WorldPosition );
return "placed " + s.WorldPosition;
