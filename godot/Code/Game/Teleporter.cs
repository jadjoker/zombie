using Godot;

/// <summary>
/// The teleportation station (issue #35). After wave 10 is cleared it zaps in near where the player landed, on a spot
/// clear of everything, and wave 11 waits until the player steps in and uses it. On the far side is the wreck site
/// (<see cref="AreaTwo"/>). A glowing pad ringed by three pylons with a swirling beam; it arrives in a burst of
/// lightning. Metres.
/// </summary>
public partial class Teleporter : Node3D
{
	public static Teleporter Current { get; private set; }
	/// <summary> How close (metres, across the ground) the player must be to use it. </summary>
	public const float Reach = 2.2f;
	/// <summary> The clear space it needs round it (metres). </summary>
	public const float Clearance = 2.6f;

	public bool Arrival;
	TimeSince born;
	Node3D beam, rings;
	OmniLight3D glow;
	bool taking;

	static readonly Color Green = new( 0.35f, 1f, 0.3f );
	static readonly Color Purple = new( 0.66f, 0.25f, 1f );

	/// <summary> Zaps the station in near a spot, on the clearest ground nearby. </summary>
	public static Teleporter Spawn( Node3D level, Vector3 near )
	{
		var at = FindSpot( level, near );
		var t = new Teleporter { Name = "Teleporter" };
		level.AddChild( t );
		t.GlobalPosition = at;
		Current = t;
		return t;
	}

	/// <summary>
	/// The best spot within 14 m of `near`: on the navmesh, flat, nothing solid in a cylinder over it, and as far as
	/// possible from every prop, plant, station and spawn point (at least the station's clearance).
	/// </summary>
	public static Vector3 FindSpot( Node3D level, Vector3 near )
	{
		var obstacles = Obstacles( level );
		var best = Nav.Closest( level, near );
		var bestScore = float.MinValue;
		for ( var r = 3f; r <= 14f; r += 1.5f )
			for ( var i = 0; i < 20; i++ )
			{
				var a = i / 20f * MathF.Tau + r;
				var want = near + new Vector3( MathF.Cos( a ) * r, 0f, MathF.Sin( a ) * r );
				if ( SeaTerrain.Active ) want.Y = SeaTerrain.HeightAt( want );
				var spot = Nav.Closest( level, want );
				if ( spot.Flat().DistanceTo( want.Flat() ) > 0.6f ) continue;
				var clear = obstacles.Count > 0 ? obstacles.Min( o => o.Flat().DistanceTo( spot.Flat() ) ) : 10f;
				if ( clear < Clearance || !SpaceFree( level, spot ) ) continue;
				var score = MathF.Min( clear, 5f ) - r * 0.08f;
				if ( score > bestScore ) { bestScore = score; best = spot; }
			}
		return best;
	}

	/// <summary> Everything placed on the sea floor that the station must not overlap. </summary>
	public static List<Vector3> Obstacles( Node3D level )
	{
		var list = new List<Vector3>();
		foreach ( var n in level.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>() )
		{
			var parent = n.GetParent();
			if ( parent is null ) continue;
			var kind = Level.Kind( parent );
			if ( kind is "Dressing" or "Buy Stations" or "Zombie Spawns" || parent.Name.ToString() is "Sea Dressing" || n is BuyStation || n.Name.ToString() == "Floater" )
				list.Add( n.GlobalPosition );
		}
		return list;
	}

	/// <summary> Nothing solid (other than the ground) in the station's space. </summary>
	static bool SpaceFree( Node3D level, Vector3 at )
	{
		var space = level.GetWorld3D().DirectSpaceState;
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = new CylinderShape3D { Radius = Clearance - 0.4f, Height = 2.8f },
			Transform = new Transform3D( Basis.Identity, at + Vector3.Up * 1.7f ),
			CollisionMask = Layers.World,
		};
		return space.IntersectShape( query, 1 ).Count == 0;
	}

	public override void _Ready()
	{
		born = 0f;
		Build();
		if ( !Arrival )
		{
			Sfx.Play( "beam_charge", GlobalPosition, 1f );
			Sfx.Play( "beam_fire", GlobalPosition, 1f, 0.8f );
			Lightning();
		}
	}

	public override void _ExitTree() { if ( Current == this ) Current = null; }

	static StandardMaterial3D Glow( Color c, float e, float alpha = 1f ) => new()
	{
		AlbedoColor = c with { A = alpha }, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = e, Roughness = 0.3f,
		Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
		ShadingMode = alpha < 1f ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};

	void Build()
	{
		var iron = new StandardMaterial3D { AlbedoColor = new Color( 0.12f, 0.12f, 0.14f ), Metallic = 0.7f, Roughness = 0.35f };
		void Add( Mesh mesh, Material m, Vector3 at, Node3D parent = null )
		{
			var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = m, Position = at, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			(parent ?? this).AddChild( mi );
		}
		Add( new CylinderMesh { TopRadius = 1.7f, BottomRadius = 1.9f, Height = 0.3f, RadialSegments = 32 }, iron, new Vector3( 0f, 0.15f, 0f ) );
		Add( new TorusMesh { InnerRadius = 1.3f, OuterRadius = 1.5f, Rings = 40, RingSegments = 8 }, Glow( Green, 3f ), new Vector3( 0f, 0.32f, 0f ) );
		Add( new CylinderMesh { TopRadius = 1.2f, BottomRadius = 1.2f, Height = 0.04f, RadialSegments = 32 }, Glow( Purple, 1.5f ), new Vector3( 0f, 0.31f, 0f ) );
		for ( var i = 0; i < 3; i++ )
		{
			var a = i / 3f * MathF.Tau;
			var at = new Vector3( MathF.Cos( a ) * 1.75f, 0f, MathF.Sin( a ) * 1.75f );
			Add( new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.18f, Height = 3f, RadialSegments = 8 }, iron, at + new Vector3( 0f, 1.5f, 0f ) );
			Add( new SphereMesh { Radius = 0.18f, Height = 0.36f }, Glow( Green, 4f ), at + new Vector3( 0f, 3.1f, 0f ) );
		}
		beam = new Node3D { Name = "Beam" };
		AddChild( beam );
		Add( new CylinderMesh { TopRadius = 1.05f, BottomRadius = 1.15f, Height = 3.2f, RadialSegments = 24, CapTop = false, CapBottom = false }, Glow( Green, 2f, 0.22f ), new Vector3( 0f, 1.9f, 0f ), beam );
		rings = new Node3D { Name = "Rings" };
		AddChild( rings );
		for ( var i = 0; i < 3; i++ )
			Add( new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1.05f, Rings = 32, RingSegments = 6 }, Glow( i % 2 == 0 ? Green : Purple, 3f ), new Vector3( 0f, 0.4f + i, 0f ), rings );
		glow = new OmniLight3D { LightColor = Green, LightEnergy = 1.5f, OmniRange = 7f, ShadowEnabled = false, Position = new Vector3( 0f, 1.5f, 0f ) };
		AddChild( glow );
		var label = new Label3D { Text = Arrival ? "" : "TELEPORTER", Modulate = Green, FontSize = 48, PixelSize = 0.01f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3( 0f, 3.8f, 0f ), OutlineSize = 8, OutlineModulate = new Color( 0f, 0f, 0f, 0.7f ) };
		AddChild( label );
		// Solid pylons, so nothing walks through them; the pad itself is walked onto.
		var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
		AddChild( body );
		for ( var i = 0; i < 3; i++ )
		{
			var a = i / 3f * MathF.Tau;
			body.AddChild( new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.2f, Height = 3f }, Position = new Vector3( MathF.Cos( a ) * 1.75f, 1.5f, MathF.Sin( a ) * 1.75f ) } );
		}
	}

	/// <summary> The arrival: bolts of lightning from above and a flash. </summary>
	void Lightning()
	{
		var bolt = Glow( new Color( 0.75f, 1f, 0.8f ), 6f );
		for ( var b = 0; b < 5; b++ )
		{
			var node = new Node3D { Name = "Bolt" };
			AddChild( node );
			var p = new Vector3( Rng.Float( -1.5f, 1.5f ), 14f, Rng.Float( -1.5f, 1.5f ) );
			for ( var k = 0; k < 8; k++ )
			{
				var next = new Vector3( Rng.Float( -1.4f, 1.4f ), p.Y - Rng.Float( 1.4f, 2.2f ), Rng.Float( -1.4f, 1.4f ) ) * new Vector3( 0.6f, 1f, 0.6f );
				if ( k == 7 ) next = new Vector3( 0f, 0.3f, 0f );
				var mid = (p + next) * 0.5f;
				var len = p.DistanceTo( next );
				var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3( 0.06f, len, 0.06f ) }, MaterialOverride = bolt, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
				node.AddChild( mi );
				mi.Transform = new Transform3D( new Basis( new Quaternion( Vector3.Up, (next - p).Normalized() ) ), mid );
				p = next;
			}
			var tween = node.CreateTween();
			tween.TweenInterval( 0.08f + b * 0.07f );
			tween.TweenCallback( Callable.From( node.QueueFree ) );
		}
		glow.LightEnergy = 12f;
		Scale = Vector3.One * 0.05f;
	}

	public override void _Process( double delta )
	{
		var t = (float)born;
		// Grow in with a little overshoot, then hum.
		if ( !Arrival && t < 1.2f ) Scale = Vector3.One * MathF.Max( 0.05f, 1f + MathF.Sin( MathF.Min( t / 0.6f, 1f ) * MathF.PI * 1.5f ) * 0.15f * MathF.Max( 0f, 1f - t ) - MathF.Max( 0f, 1f - t / 0.5f ) );
		else Scale = Vector3.One;
		beam.Rotation = new Vector3( 0f, t * 1.6f, 0f );
		var i = 0;
		foreach ( var ring in rings.GetChildren().OfType<Node3D>() )
		{
			var phase = (t * 0.6f + i / 3f) % 1f;
			ring.Position = new Vector3( 0f, 0.4f + phase * 2.8f, 0f );
			ring.Scale = Vector3.One * (1f - phase * 0.3f);
			i++;
		}
		glow.LightEnergy = MathX.Lerp( glow.LightEnergy, 1.5f + MathF.Sin( t * 5f ) * 0.4f, 1f - MathF.Exp( -3f * (float)delta ) );
	}

	/// <summary> True when the player is standing at the pad (the prompt shows "Teleport"). </summary>
	public bool InReach( PlayerController controller ) =>
		!Arrival && !taking && (float)born > 1f && controller.GlobalPosition.Flat().DistanceTo( GlobalPosition.Flat() ) < Reach && MathF.Abs( controller.GlobalPosition.Y - GlobalPosition.Y ) < 2.5f;

	/// <summary> Into the beam: a flash, and out the other side at the wreck site, where wave 11 begins. </summary>
	public void Take( PlayerController controller )
	{
		if ( taking ) return;
		taking = true;
		Sfx.Play2D( "surge", 1f );
		Sfx.Play2D( "beam_fire", 0.8f, 1.3f );
		var fade = new ColorRect { Color = new Color( 0.5f, 1f, 0.55f, 0f ), MouseFilter = Control.MouseFilterEnum.Ignore };
		fade.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		var layer = new CanvasLayer { Layer = 50, Name = "Teleport Flash" };
		GetTree().CurrentScene.AddChild( layer );
		layer.AddChild( fade );
		controller.UseInputControls = false;
		var tween = layer.CreateTween();
		tween.TweenProperty( fade, "color:a", 1f, 0.35f );
		tween.TweenCallback( Callable.From( () =>
		{
			var level = GetTree().CurrentScene as Node3D;
			AreaTwo.Go( level, controller );
			fade.Color = new Color( 0.02f, 0.06f, 0.1f, 1f );
			QueueFree();
		} ) );
		tween.TweenInterval( 0.3f );
		tween.TweenProperty( fade, "color:a", 0f, 0.8f );
		tween.TweenCallback( Callable.From( () =>
		{
			controller.UseInputControls = true;
			layer.QueueFree();
		} ) );
	}
}
