using Godot;

/// <summary>
/// Life and landmarks on the water level's sea floor (models from Tools/blender/sea_props.py): anemone clusters,
/// starfish, shells and urchins scattered between the rocks, a big ship's anchor and a sunken rowboat to navigate by,
/// and a few schools of fish drifting overhead. Everything is placed from one fixed seed, so the level is the same
/// every run, and kept clear of what the level already has (rocks, kelp, buy stations, spawn points).
/// The small things are only for show (no collision); the anchor and the boat are solid.
/// </summary>
public static class SeaDressing
{
	const float Reach = 33f;   // metres from the middle that things are scattered over
	static readonly Dictionary<string, PackedScene> models = new();
	static readonly Dictionary<(string, int), StandardMaterial3D> paints = new();

	static readonly Color[] Bright =
	{
		new( 1f, 0.42f, 0.55f ), new( 1f, 0.62f, 0.2f ), new( 0.6f, 0.9f, 0.3f ), new( 0.3f, 0.85f, 0.9f ), new( 0.72f, 0.5f, 1f ), new( 1f, 0.85f, 0.3f ),
	};

	static Node3D Load( string name )
	{
		if ( !models.TryGetValue( name, out var scene ) )
		{
			var path = $"res://assets/models/sea/{name}.glb";
			models[name] = scene = ResourceLoader.Exists( path ) ? GD.Load<PackedScene>( path ) : null;
		}
		return scene?.Instantiate<Node3D>();
	}

	/// <summary> A flat, softly rimmed colour for a named surface (one shared material per name and palette entry). </summary>
	static StandardMaterial3D Paint( string surface, int variant, Color color )
	{
		if ( paints.TryGetValue( (surface, variant), out var m ) ) return m;
		m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.65f, RimEnabled = true, Rim = 0.35f, RimTint = 0.6f, ResourceName = surface };
		paints[(surface, variant)] = m;
		return m;
	}

	/// <summary> Repaints a prop's surfaces by their material names. </summary>
	static void Recolour( Node3D prop, int variant, Func<string, Color?> colour )
	{
		foreach ( var mesh in prop.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
			{
				var name = mesh.Mesh.SurfaceGetMaterial( i )?.ResourceName ?? "";
				if ( colour( name ) is { } c ) mesh.SetSurfaceOverrideMaterial( i, Paint( name, variant, c ) );
			}
		}
	}

	public static void Add( Node3D level, List<(Vector3 Position, float Radius)> taken )
	{
		var random = new Random( 4711 );
		float Range( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var root = new Node3D { Name = "Sea Dressing" };
		level.AddChild( root );
		// Keep the middle and the spot under the diving board (where the player lands) open.
		taken.Add( (Vector3.Zero, 4f) );
		taken.Add( (new Vector3( 8.9f, 0f, -8.9f ), 4f) );

		float Clearance( Vector3 at )
		{
			var best = float.MaxValue;
			foreach ( var (p, r) in taken ) best = MathF.Min( best, at.Flat().DistanceTo( p.Flat() ) - r );
			return best;
		}
		Vector3 FreeSpot( float need, float reach )
		{
			var best = Vector3.Zero;
			var bestClear = float.MinValue;
			for ( var i = 0; i < 80; i++ )
			{
				var at = new Vector3( Range( -reach, reach ), 0f, Range( -reach, reach ) );
				var clear = Clearance( at );
				if ( clear > bestClear ) { bestClear = clear; best = at; }
				if ( clear >= need ) break;
			}
			return best;
		}

		// ---- Landmarks: the anchor (the one that keeps snapping) and a sunken rowboat. Solid, and kept clear of. ----
		if ( Load( "anchor" ) is { } anchor )
		{
			var at = FreeSpot( 5f, 24f );
			root.AddChild( anchor );
			anchor.Name = "Anchor";
			// Laid over on its side, one fluke dug into the sand.
			anchor.Transform = new Transform3D( new Basis( Vector3.Up, Range( 0f, MathF.Tau ) ) * new Basis( Vector3.Forward, MathX.Rad( 96f ) ) * Basis.FromScale( Vector3.One * 1.5f ), at + Vector3.Up * 0.75f );
			Solid( anchor, new Vector3( 0f, 1.45f, 0f ), new Vector3( 0.6f, 2.9f, 0.6f ) );
			Solid( anchor, new Vector3( 0f, 0.6f, 0f ), new Vector3( 2.2f, 0.9f, 0.4f ) );
			taken.Add( (at, 4.5f) );
		}
		if ( Load( "rowboat" ) is { } boat )
		{
			var at = FreeSpot( 5f, 26f );
			root.AddChild( boat );
			boat.Name = "Rowboat";
			// Settled into the sand, heeled over.
			boat.Transform = new Transform3D( new Basis( Vector3.Up, Range( 0f, MathF.Tau ) ) * new Basis( Vector3.Right, MathX.Rad( 17f ) ) * Basis.FromScale( Vector3.One * 1.25f ), at + Vector3.Down * 0.12f );
			Solid( boat, new Vector3( 0f, 0.4f, 0f ), new Vector3( 4.6f, 0.8f, 1.5f ) );
			taken.Add( (at, 4.5f) );
		}

		// ---- Small life between the rocks. ----
		void Scatter( int count, float spacing, Action<Vector3> place )
		{
			for ( var n = 0; n < count; n++ )
				for ( var tries = 0; tries < 12; tries++ )
				{
					var at = new Vector3( Range( -Reach, Reach ), 0f, Range( -Reach, Reach ) );
					if ( Clearance( at ) < 0.5f ) continue;
					place( at );
					taken.Add( (at, spacing) );
					break;
				}
		}
		Node3D Put( string model, Vector3 at, float scale, float sink = 0.01f )
		{
			var prop = Load( model );
			if ( prop is null ) return null;
			root.AddChild( prop );
			prop.Transform = new Transform3D( new Basis( Vector3.Up, Range( 0f, MathF.Tau ) ) * Basis.FromScale( Vector3.One * scale ), at + Vector3.Down * sink );
			return prop;
		}

		// Anemones grow in little groups of one colour, swaying.
		Scatter( 16, 1.6f, centre =>
		{
			var variant = random.Next( Bright.Length );
			var arm = Bright[variant];
			for ( var k = 0; k < random.Next( 2, 5 ); k++ )
			{
				var at = centre + new Vector3( Range( -0.7f, 0.7f ), 0f, Range( -0.7f, 0.7f ) );
				if ( Put( "anemone", at, Range( 1.2f, 2.2f ) ) is not { } a ) continue;
				Recolour( a, variant, n => n == "AnemoneArm" ? arm : n == "AnemoneBase" ? arm.Darkened( 0.45f ) : null );
				a.AddChild( new Sway { Amount = 5f, Period = 3.5f } );
			}
		} );
		Scatter( 22, 0.6f, at =>
		{
			var variant = random.Next( Bright.Length );
			if ( Put( "starfish", at, Range( 1.3f, 2.4f ) ) is { } s )
				Recolour( s, variant, n => n == "Starfish" ? Bright[variant] : n == "StarfishDot" ? Bright[variant].Lightened( 0.5f ) : null );
		} );
		Scatter( 18, 0.5f, at =>
		{
			if ( Put( "shell", at, Range( 1.2f, 2.2f ), 0.03f ) is { } s )
				Recolour( s, 0, n => n == "Shell" ? new Color( 0.95f, 0.84f, 0.72f ) : n == "ShellLip" ? new Color( 1f, 0.62f, 0.62f ) : null );
		} );
		Scatter( 12, 0.6f, at =>
		{
			if ( Put( "urchin", at, Range( 1.2f, 2f ) ) is { } u )
				Recolour( u, 0, n => n == "Urchin" ? new Color( 0.2f, 0.1f, 0.28f ) : n == "UrchinSpine" ? new Color( 0.42f, 0.24f, 0.55f ) : null );
		} );

		// ---- Fish overhead. ----
		for ( var s = 0; s < 4; s++ )
		{
			var school = FishSchool.Create( Bright[(s * 2 + 1) % Bright.Length], 12 + random.Next( 8 ), random );
			if ( school is null ) break;
			root.AddChild( school );
			school.Home = new Vector3( Range( -16f, 16f ), Range( 2.6f, 5.5f ), Range( -16f, 16f ) );
			school.Radius = Range( 7f, 14f );
			school.Speed = Range( 0.1f, 0.18f ) * (s % 2 == 0 ? 1f : -1f);
			school.Phase = Range( 0f, MathF.Tau );
		}
	}

	/// <summary> A solid box on a prop, in the prop's own space (so it tilts and scales with it). </summary>
	static void Solid( Node3D prop, Vector3 centre, Vector3 size )
	{
		var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
		prop.AddChild( body );
		body.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = centre } );
	}
}

/// <summary>
/// A school of small fish: one multimesh, the school circling a home point on a lazy loop, each fish holding its
/// own place in the group, bobbing a little and wagging as it swims. Only for show.
/// </summary>
public partial class FishSchool : MultiMeshInstance3D
{
	public Vector3 Home;
	public float Radius = 10f;
	public float Speed = 0.14f;   // turns of the loop per second, in radians
	public float Phase;

	Vector3[] offsets;
	float[] phases, sizes;
	static Mesh fishMesh;

	public static FishSchool Create( Color colour, int count, Random random )
	{
		if ( fishMesh is null )
		{
			var path = "res://assets/models/sea/fish.glb";
			if ( !ResourceLoader.Exists( path ) ) return null;
			var scene = GD.Load<PackedScene>( path ).Instantiate<Node3D>();
			fishMesh = scene.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().FirstOrDefault()?.Mesh;
			scene.QueueFree();
			if ( fishMesh is null ) return null;
		}
		// This school's own colours on a copy of the mesh.
		var mesh = (Mesh)fishMesh.Duplicate();
		for ( var i = 0; i < mesh.GetSurfaceCount(); i++ )
		{
			var name = mesh.SurfaceGetMaterial( i )?.ResourceName ?? "";
			var c = name switch { "Fish" => colour, "FishFin" => colour.Lightened( 0.45f ), _ => new Color( 0.03f, 0.03f, 0.04f ) };
			mesh.SurfaceSetMaterial( i, new StandardMaterial3D { AlbedoColor = c, Roughness = 0.5f, RimEnabled = name != "FishEye", Rim = 0.4f, RimTint = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled } );
		}
		var school = new FishSchool
		{
			Name = "Fish School",
			CastShadow = ShadowCastingSetting.Off,
			Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = count },
			CustomAabb = new Aabb( -Vector3.One * 60f, Vector3.One * 120f ),
			offsets = new Vector3[count], phases = new float[count], sizes = new float[count],
		};
		for ( var i = 0; i < count; i++ )
		{
			school.offsets[i] = new Vector3( (float)random.NextDouble() * 2.4f - 1.2f, (float)random.NextDouble() * 1f - 0.5f, (float)random.NextDouble() * 3.2f - 1.6f );
			school.phases[i] = (float)random.NextDouble() * MathF.Tau;
			school.sizes[i] = 0.8f + (float)random.NextDouble() * 0.7f;
		}
		return school;
	}

	public override void _Process( double delta )
	{
		var t = (float)Clock.Now;
		var angle = Phase + t * Speed;
		Vector3 At( float a ) => Home + new Vector3( MathF.Cos( a ) * Radius, MathF.Sin( a * 2f ) * 0.7f, MathF.Sin( a ) * Radius * 0.7f );
		var centre = At( angle );
		var heading = (At( angle + 0.02f * MathF.Sign( Speed ) ) - centre).SafeNormal();
		if ( heading == Vector3.Zero ) heading = Vector3.Forward;
		// The model's nose points down +Z: turn it to face the way it swims.
		var facing = Shapes.LookAt( heading ) * new Basis( Vector3.Up, MathF.PI );
		for ( var i = 0; i < offsets.Length; i++ )
		{
			var bob = new Vector3( 0f, MathF.Sin( t * 1.3f + phases[i] ) * 0.12f, MathF.Sin( t * 0.7f + phases[i] * 2f ) * 0.25f );
			var wag = new Basis( Vector3.Up, MathF.Sin( t * 9f + phases[i] ) * 0.22f );
			var basis = facing * wag * Basis.FromScale( Vector3.One * sizes[i] );
			Multimesh.SetInstanceTransform( i, new Transform3D( basis, centre + facing * (offsets[i] + bob) ) );
		}
	}
}
