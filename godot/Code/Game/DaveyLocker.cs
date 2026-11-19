using Godot;

/// <summary>
/// Davey's locker room, built far under the map (below the wreck site) when he drags a player down at half health and
/// taken away when they come back up. A smallish room of dark ship's planks with green lanterns, barrels, a sea chest,
/// hanging chains and his locker against the far wall, "Davey's" painted on its door. The player starts at one end
/// facing him at the other. While it stands every other enemy holds still and the waves stop spawning.
/// </summary>
public static class DaveyLocker
{
	public const float Half = 8f;          // half the room's width and depth, metres
	public const float Height = 6.5f;
	public const float Depth = 150f;       // how far under the sea floor

	public static Node3D Room { get; private set; }
	/// <summary> The room stands (a reload of the level frees it along with everything else). </summary>
	public static bool Active => Room is not null && GodotObject.IsInstanceValid( Room );
	public static Vector3 Centre { get; private set; }
	public static float Floor => Centre.Y;
	public static Vector3 PlayerSpot => Centre + new Vector3( 0f, 0.1f, Half - 2.5f );
	public static Vector3 DaveySpot => Centre + new Vector3( 0f, 0f, -Half + 3.5f );
	static PlayerController who;
	static Vector3 returnTo;

	/// <summary> Keeps a point inside the room (a metre and a half off the walls, under the ceiling). </summary>
	public static Vector3 Inside( Vector3 p )
	{
		var limit = Half - 1.5f;
		return new Vector3( Math.Clamp( p.X, Centre.X - limit, Centre.X + limit ), Math.Clamp( p.Y, Floor + 0.5f, Floor + Height - 2.8f ), Math.Clamp( p.Z, Centre.Z - limit, Centre.Z + limit ) );
	}

	/// <summary> Builds the room and puts the player in it; they come back to `back` (on the floor above) when it ends. </summary>
	public static void Enter( PlayerController player, Vector3 back )
	{
		if ( Room is not null ) Leave();
		who = player;
		returnTo = back;
		var top = SeaTerrain.Active ? SeaTerrain.HeightAt( Vector3.Zero ) : 0f;
		Centre = new Vector3( 0f, top - Depth, 0f );
		Room = Build( player.GetTree().CurrentScene );
		player.GlobalPosition = PlayerSpot;
		player.Velocity = Vector3.Zero;
		player.EyeAngles = new Angles( 0f, 0f, 0f );   // facing -Z, at him
		player.ResetPhysicsInterpolation();
		Sfx.Play2D( "dive_impact", 0.8f, 0.7f );
	}

	/// <summary> Back up to where they were grabbed; the room goes. </summary>
	public static void Leave()
	{
		if ( who is not null && GodotObject.IsInstanceValid( who ) )
		{
			who.Scripted = false;
			who.GlobalPosition = returnTo + Vector3.Up * 0.1f;
			who.Velocity = Vector3.Zero;
			who.ResetPhysicsInterpolation();
		}
		if ( Room is not null && GodotObject.IsInstanceValid( Room ) ) Room.QueueFree();
		Room = null;
		who = null;
		Sfx.Play2D( "ghost_appear", 0.8f, 1.2f );
		if ( AutoTest.Requested ) GD.Print( "[autotest] back up from Davey's locker" );
	}

	static Node3D Build( Node parent )
	{
		var room = new Node3D { Name = "Davey's Locker Room" };
		parent.AddChild( room );
		room.GlobalPosition = Centre;
		var plank = Sculpt.Paint( new Color( 0.24f, 0.17f, 0.11f ), 0.85f );
		var plankDark = Sculpt.Paint( new Color( 0.16f, 0.11f, 0.07f ), 0.9f );
		var wall = Sculpt.Paint( new Color( 0.12f, 0.16f, 0.13f ), 0.9f );
		var beam = Sculpt.Paint( new Color( 0.1f, 0.08f, 0.06f ), 0.9f );
		var iron = Sculpt.Paint( new Color( 0.2f, 0.22f, 0.21f ), 0.45f, 0.7f );
		var moss = Sculpt.Paint( new Color( 0.18f, 0.4f, 0.22f ), 0.9f );
		var gold = Sculpt.Paint( new Color( 1f, 0.78f, 0.25f ), 0.3f, 0.85f );

		// Floor, ceiling and walls, solid.
		Solid( room, new Vector3( 0f, -0.25f, 0f ), new Vector3( Half * 2f + 1f, 0.5f, Half * 2f + 1f ), plank );
		Solid( room, new Vector3( 0f, Height + 0.25f, 0f ), new Vector3( Half * 2f + 1f, 0.5f, Half * 2f + 1f ), beam );
		foreach ( var side in new[] { -1f, 1f } )
		{
			Solid( room, new Vector3( side * (Half + 0.25f), Height * 0.5f, 0f ), new Vector3( 0.5f, Height, Half * 2f + 1f ), wall );
			Solid( room, new Vector3( 0f, Height * 0.5f, side * (Half + 0.25f) ), new Vector3( Half * 2f + 1f, Height, 0.5f ), wall );
		}
		// Floor planks, ceiling beams, wall boards and corner posts (looks only).
		for ( var x = -Half + 0.4f; x < Half; x += 0.8f )
			Sculpt.Block( room, new Vector3( x, 0.01f, 0f ), new Vector3( 0.74f, 0.02f, Half * 2f ), ((int)((x + Half) / 0.8f)) % 2 == 0 ? plank : plankDark );
		for ( var z = -Half + 1f; z < Half; z += 2.5f )
			Sculpt.Block( room, new Vector3( 0f, Height - 0.15f, z ), new Vector3( Half * 2f, 0.3f, 0.35f ), beam );
		for ( var y = 0.7f; y < Height; y += 1.1f )
			foreach ( var side in new[] { -1f, 1f } )
			{
				Sculpt.Block( room, new Vector3( side * (Half - 0.02f), y, 0f ), new Vector3( 0.04f, 0.12f, Half * 2f ), beam );
				Sculpt.Block( room, new Vector3( 0f, y, side * (Half - 0.02f) ), new Vector3( Half * 2f, 0.12f, 0.04f ), beam );
			}
		foreach ( var x in new[] { -1f, 1f } )
			foreach ( var z in new[] { -1f, 1f } )
				Sculpt.Block( room, new Vector3( x * (Half - 0.2f), Height * 0.5f, z * (Half - 0.2f) ), new Vector3( 0.4f, Height, 0.4f ), beam );

		// His locker against the far wall: a tall dented steel locker, vents, a handle, a padlock and his name.
		var locker = new Node3D { Name = "Locker" };
		room.AddChild( locker );
		locker.Position = new Vector3( 0f, 0f, -Half + 0.45f );
		var steel = Sculpt.Paint( new Color( 0.32f, 0.42f, 0.38f ), 0.4f, 0.6f );
		Solid( locker, new Vector3( 0f, 1.3f, 0f ), new Vector3( 1.5f, 2.6f, 0.8f ), steel );
		Sculpt.Block( locker, new Vector3( 0f, 1.3f, 0.405f ), new Vector3( 1.36f, 2.46f, 0.02f ), Sculpt.Paint( new Color( 0.27f, 0.36f, 0.33f ), 0.45f, 0.6f ), name: "Door" );
		for ( var i = 0; i < 4; i++ )
			Sculpt.Block( locker, new Vector3( 0f, 2.3f - i * 0.1f, 0.42f ), new Vector3( 0.9f, 0.035f, 0.02f ), beam, name: "Vent" );
		Sculpt.Block( locker, new Vector3( 0.52f, 1.25f, 0.44f ), new Vector3( 0.06f, 0.36f, 0.05f ), iron, name: "Handle" );
		Sculpt.Block( locker, new Vector3( 0.52f, 1.0f, 0.46f ), new Vector3( 0.12f, 0.14f, 0.06f ), gold, name: "Padlock" );
		var name = new Label3D
		{
			Name = "Name",
			Text = "Davey's",
			FontSize = 120,
			PixelSize = 0.0042f,
			OutlineSize = 18,
			Modulate = new Color( 1f, 0.86f, 0.35f ),
			OutlineModulate = new Color( 0.05f, 0.08f, 0.05f ),
			Position = new Vector3( 0f, 1.75f, 0.43f ),
			Shaded = false,
		};
		locker.AddChild( name );
		Sculpt.Blob( locker, new Vector3( -0.5f, 0.3f, 0.42f ), new Vector3( 0.35f, 0.2f, 0.1f ), moss );   // weed growing up it

		// Barrels, a sea chest, chains from the ceiling, barnacles, weed.
		foreach ( var (x, z) in new[] { (-Half + 1f, -Half + 1.2f), (-Half + 1.9f, -Half + 1f), (Half - 1.1f, -Half + 1.1f), (Half - 1f, Half - 1.3f) } )
		{
			Solid( room, new Vector3( x, 0.55f, z ), new Vector3( 0.8f, 1.1f, 0.8f ), null );
			Sculpt.Limb( room, new Vector3( x, 0f, z ), new Vector3( x, 1.1f, z ), 0.4f, 0.4f, plankDark, "Barrel" );
			foreach ( var y in new[] { 0.2f, 0.9f } ) Sculpt.Limb( room, new Vector3( x, y, z ), new Vector3( x, y + 0.06f, z ), 0.42f, 0.42f, iron, "Hoop" );
		}
		Solid( room, new Vector3( -Half + 1.3f, 0.35f, 2f ), new Vector3( 0.9f, 0.7f, 1.5f ), plank );
		Sculpt.Blob( room, new Vector3( -Half + 1.3f, 0.7f, 2f ), new Vector3( 0.9f, 0.35f, 1.5f ), plankDark, name: "Chest Lid" );
		Sculpt.Block( room, new Vector3( -Half + 1.76f, 0.55f, 2f ), new Vector3( 0.04f, 0.18f, 0.16f ), gold, name: "Clasp" );
		for ( var i = 0; i < 5; i++ )
		{
			var top = new Vector3( Rng.Float( -Half + 2f, Half - 2f ), Height, Rng.Float( -Half + 2f, Half - 2f ) );
			var links = Rng.Int( 4, 9 );
			for ( var k = 0; k < links; k++ )
				Sculpt.Limb( room, top + Vector3.Down * (k * 0.22f), top + Vector3.Down * (k * 0.22f + 0.18f), 0.035f, 0.035f, iron, "Chain" );
		}
		for ( var i = 0; i < 40; i++ )
		{
			var side = Rng.Int( 0, 3 );
			var along = Rng.Float( -Half + 0.6f, Half - 0.6f );
			var y = Rng.Float( 0.1f, Height - 0.4f );
			var at = side switch { 0 => new Vector3( -Half, y, along ), 1 => new Vector3( Half, y, along ), 2 => new Vector3( along, y, -Half ), _ => new Vector3( along, y, Half ) };
			Sculpt.Blob( room, at, Vector3.One * Rng.Float( 0.08f, 0.2f ), Rng.Float() < 0.6f ? iron : moss, name: "Barnacle" );
		}
		for ( var i = 0; i < 10; i++ )
		{
			var at = new Vector3( Rng.Float( -Half + 0.5f, Half - 0.5f ), 0f, Rng.Float() < 0.5f ? -Half + 0.4f : Half - 0.4f );
			Sculpt.Cone( room, at, at + new Vector3( Rng.Float( -0.2f, 0.2f ), Rng.Float( 0.6f, 1.4f ), 0f ), 0.08f, moss, 5, "Weed" );
		}

		// Light: green lanterns on the walls, a warm one over the locker.
		var glass = Sculpt.Glow( new Color( 0.4f, 1f, 0.5f ), 3f );
		foreach ( var at in new[] { new Vector3( -Half + 0.3f, 3.2f, -3f ), new Vector3( Half - 0.3f, 3.2f, -3f ), new Vector3( -Half + 0.3f, 3.2f, 3f ), new Vector3( Half - 0.3f, 3.2f, 3f ) } )
		{
			Sculpt.Blob( room, at, new Vector3( 0.22f, 0.32f, 0.22f ), glass, name: "Lantern" );
			var lamp = new OmniLight3D { LightColor = new Color( 0.45f, 1f, 0.55f ), LightEnergy = 2.2f, OmniRange = 10f };
			room.AddChild( lamp );
			lamp.Position = at + new Vector3( -Math.Sign( at.X ) * 0.4f, 0f, 0f );
		}
		var warm = new OmniLight3D { LightColor = new Color( 1f, 0.75f, 0.45f ), LightEnergy = 1.6f, OmniRange = 6f };
		room.AddChild( warm );
		warm.Position = new Vector3( 0f, 3.6f, -Half + 2f );
		return room;
	}

	/// <summary> A solid box on the world layer (drawn too unless `look` is null). </summary>
	static void Solid( Node3D parent, Vector3 at, Vector3 size, Material look )
	{
		var body = new StaticBody3D { Name = "Wall", CollisionLayer = Layers.World, CollisionMask = 0 };
		parent.AddChild( body );
		body.Position = at;
		body.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = size } } );
		if ( look is not null ) body.AddChild( new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = look } );
	}
}
