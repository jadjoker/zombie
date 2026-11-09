/// <summary> TEMPORARY test harness: a side-view camera that follows the player, who can be kept moving. Delete when done. </summary>
public sealed class RigTest : Component
{
	public float Speed { get; set; } = 0f;
	public Vector3 CamOffset { get; set; } = new( 30f, 120f, 25f );   // forward, left, up in the player's frame
	public bool Keep { get; set; }
	GameObject cam;
	public float Apex, AirTime, Ground;
	bool wasAir;

	protected override void OnUpdate()
	{
		var pc = GetComponent<PlayerController>();
		if ( !pc.IsValid() ) return;
		if ( !cam.IsValid() )
		{
			cam = new GameObject( true, "RigTestCam" );
			var c = cam.AddComponent<CameraComponent>();
			c.Priority = 100;
			c.FieldOfView = 50;
		}
		if ( !pc.IsOnGround )
		{
			if ( !wasAir ) { Apex = 0; AirTime = 0; }
			wasAir = true;
			AirTime += Time.Delta;
			Apex = MathF.Max( Apex, pc.WorldPosition.z - Ground );
		}
		else { wasAir = false; Ground = pc.WorldPosition.z; }
		var yaw = Rotation.FromYaw( pc.EyeAngles.yaw );
		if ( Keep ) pc.Body.Velocity = (yaw.Forward * Speed).WithZ( pc.Body.Velocity.z );
		var target = pc.WorldPosition + Vector3.Up * 32f;
		cam.WorldPosition = pc.WorldPosition + yaw * CamOffset;
		cam.WorldRotation = Rotation.LookAt( target - cam.WorldPosition );
	}
}
