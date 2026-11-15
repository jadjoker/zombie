using Godot;

/// <summary>
/// Sea mines (after the old horned naval mines): a riveted steel ball studded with pegs, chained to the sea floor and
/// drifting slowly on its chain, a little up and down and side to side. Eight per area, out on the edges, never by
/// the landing spot, the arrival pads or the enemy spawn points. Bullets ring off them (a tink, no harm). Swim or
/// walk into one and it goes off (an alien blast) for 45 damage, and it is gone.
/// </summary>
public partial class SeaMine : Node3D
{
	public const int PerArea = 8;
	public const float Damage = 45f;
	public const float Radius = 0.55f;
	/// <summary> How near a player's body (feet to head) must come to the ball's middle to set it off. </summary>
	public const float Trigger = 1.05f;
	public static readonly List<SeaMine> All = new();
	/// <summary> Tests: how many have gone off on a player, and how many shots have rung off one. </summary>
	public static int Exploded, Tinks;

	Vector3 anchor;
	float height;
	Node3D ball;
	readonly List<Node3D> links = new();
	Vector3 phases, rates, sway;
	float time;
	bool gone;

	public override void _EnterTree() => All.Add( this );
	public override void _ExitTree() => All.Remove( this );

	/// <summary> Clears any mines and lays this area's eight. Call after the ground and the stations are in place. </summary>
	public static void Lay( Node3D level )
	{
		foreach ( var old in All.ToList() ) { old.GetParent()?.RemoveChild( old ); old.Free(); }
		var random = new Random( 5151 + SeaTerrain.Area );
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var spots = new List<Vector3>();
		if ( SeaTerrain.Area == 3 )
		{
			// Atlantis: down in the outer dry canal, between the bridges and ramps, clear of the market stands.
			foreach ( var degrees in new[] { 11.25f, 56.25f, 78.75f, 146.25f, 191.25f, 236.25f, 281.25f, 326.25f } )
				spots.Add( Atlantis.Polar( R( 48.5f, 51.5f ), degrees + R( -2f, 2f ), Atlantis.CanalFloorY ) );
		}
		else
		{
			var keepOff = level.GetTree().GetNodesInGroup( "spawn_point" ).OfType<Node3D>().Select( n => (n.GlobalPosition, 8f) ).ToList();
			keepOff.AddRange( level.GetTree().GetNodesInGroup( "buy_station" ).OfType<Node3D>().Select( n => (n.GlobalPosition, 12f) ) );
			if ( SeaTerrain.Area == 2 ) keepOff.Add( (AreaTwo.ArrivalFlat, 18f) );
			for ( var k = 0; k < PerArea; k++ )
			{
				for ( var tries = 0; tries < 60; tries++ )
				{
					// Out on the edges: past the middle basin, each in its own slice of the circle.
					var angle = (k + R( 0.15f, 0.85f )) / PerArea * MathF.Tau;
					var radius = R( 38f, 68f );
					var at = new Vector3( MathF.Cos( angle ) * radius, 0f, MathF.Sin( angle ) * radius );
					if ( keepOff.Any( o => o.Item1.Flat().DistanceTo( at ) < o.Item2 ) ) continue;
					if ( SeaTerrain.Area == 2 && AreaTwo.OnPlain( at, 5f ) ) continue;   // not on the wreck
					if ( spots.Any( s => s.Flat().DistanceTo( at ) < 12f ) ) continue;
					spots.Add( at with { Y = SeaTerrain.Active ? SeaTerrain.HeightAt( at ) : 0f } );
					break;
				}
			}
		}
		foreach ( var spot in spots )
		{
			var mine = new SeaMine { Name = "Sea Mine", anchor = spot, height = R( 1.5f, 2.3f ) };
			mine.phases = new Vector3( R( 0f, 6f ), R( 0f, 6f ), R( 0f, 6f ) );
			mine.rates = new Vector3( R( 0.25f, 0.5f ), R( 0.3f, 0.6f ), R( 0.2f, 0.45f ) );
			mine.sway = new Vector3( R( 0.25f, 0.5f ), R( 0.12f, 0.28f ), R( 0.25f, 0.5f ) );
			level.AddChild( mine );
			mine.GlobalPosition = spot;
		}
		GD.Print( $"[mines] {spots.Count} sea mines laid in area {SeaTerrain.Area}" );
	}

	public override void _Ready()
	{
		var steel = Sculpt.Paint( new Color( 0.16f, 0.36f, 0.4f ), 0.45f, 0.75f );
		var dark = Sculpt.Paint( new Color( 0.08f, 0.16f, 0.2f ), 0.5f, 0.8f );
		var peg = Sculpt.Paint( new Color( 0.3f, 0.5f, 0.52f ), 0.4f, 0.7f );
		var glow = Sculpt.Glow( new Color( 0.35f, 1f, 0.3f ), 2.2f );
		var warn = Sculpt.Glow( new Color( 0.66f, 0.22f, 1f ), 2f );

		ball = new Node3D { Name = "Ball" };
		AddChild( ball );
		Sculpt.Blob( ball, Vector3.Zero, Vector3.One * Radius * 2f, steel );
		// The riveted seam round its middle.
		Sculpt.Limb( ball, new Vector3( 0f, -0.03f, 0f ), new Vector3( 0f, 0.03f, 0f ), Radius * 1.02f, Radius * 1.02f, dark, "Seam" );
		for ( var k = 0; k < 20; k++ )
		{
			var a = k / 20f * MathF.Tau;
			Sculpt.Blob( ball, new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * Radius * 1.03f, Vector3.One * 0.045f, peg );
		}
		// The pegs (its horns): a round flange bolted on, a stub out of it, spread over the ball.
		const int pegs = 16;
		for ( var k = 0; k < pegs; k++ )
		{
			var y = 1f - (k + 0.5f) / pegs * 2f;
			var ring = MathF.Sqrt( 1f - y * y );
			var a = k * 2.399963f;   // the golden angle: evenly spread
			var dir = new Vector3( MathF.Cos( a ) * ring, y, MathF.Sin( a ) * ring );
			if ( dir.Y < -0.85f ) continue;   // the chain's eye is underneath
			Sculpt.Limb( ball, dir * (Radius - 0.02f), dir * (Radius + 0.04f), 0.13f, 0.12f, dark, "Flange" );
			Sculpt.Limb( ball, dir * (Radius + 0.03f), dir * (Radius + 0.2f), 0.065f, 0.055f, peg, "Peg" );
			Sculpt.Blob( ball, dir * (Radius + 0.2f), Vector3.One * 0.1f, k % 4 == 0 ? (k % 8 == 0 ? glow : warn) : peg );
		}
		Sculpt.Limb( ball, Vector3.Down * (Radius - 0.02f), Vector3.Down * (Radius + 0.1f), 0.1f, 0.07f, dark, "Chain Eye" );
		ball.AddChild( new OmniLight3D { LightColor = new Color( 0.4f, 1f, 0.5f ), LightEnergy = 0.7f, OmniRange = 3.5f, ShadowEnabled = false } );

		// What bullets hit: they ring off it.
		var shell = new SeaMineShell { Name = "Shell", CollisionLayer = Layers.Creature, CollisionMask = 0 };
		shell.AddChild( new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius + 0.12f } } );
		ball.AddChild( shell );

		// The chain: links from the anchor ring on the floor up to the ball, laid out again each frame.
		Sculpt.Limb( this, Vector3.Zero, Vector3.Up * 0.12f, 0.28f, 0.2f, dark, "Anchor" );
		var linkMesh = new TorusMesh { InnerRadius = 0.05f, OuterRadius = 0.09f, Rings = 10, RingSegments = 5 };
		var count = Math.Max( 3, (int)(height / 0.15f) );
		for ( var i = 0; i < count; i++ )
		{
			var link = new MeshInstance3D { Name = "Link", Mesh = linkMesh, MaterialOverride = dark, Scale = new Vector3( 0.75f, 1f, 1.25f ) };
			AddChild( link );
			links.Add( link );
		}
		Pose( 0f );
	}

	void Pose( float dt )
	{
		time += dt;
		var offset = new Vector3( MathF.Sin( time * rates.X + phases.X ) * sway.X, MathF.Sin( time * rates.Y + phases.Y ) * sway.Y, MathF.Sin( time * rates.Z + phases.Z ) * sway.Z );
		ball.Position = Vector3.Up * height + offset;
		ball.Rotation = new Vector3( offset.Z * 0.25f, time * 0.08f, -offset.X * 0.25f );
		// The chain hangs in a slight curve from the floor to the ball's underside.
		var top = ball.Position + Vector3.Down * (Radius + 0.08f);
		for ( var i = 0; i < links.Count; i++ )
		{
			var u = (i + 0.5f) / links.Count;
			var at = new Vector3( top.X * u * u, MathX.Lerp( 0.1f, top.Y, u ), top.Z * u * u );
			var next = new Vector3( top.X * (u + 0.05f) * (u + 0.05f), MathX.Lerp( 0.1f, top.Y, u + 0.05f ), top.Z * (u + 0.05f) * (u + 0.05f) );
			var along = (next - at).SafeNormal();
			var turn = Shapes.LookAt( along == Vector3.Zero ? Vector3.Up : along ) * new Basis( Vector3.Forward, i % 2 == 0 ? 0f : MathF.PI * 0.5f );
			links[i].Transform = new Transform3D( turn.Scaled( new Vector3( 0.75f, 1f, 1.25f ) ), at );
		}
	}

	public override void _Process( double delta )
	{
		if ( gone ) return;
		Pose( (float)delta );
		var centre = ball.GlobalPosition;
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var feet = player.WorldPosition;
			if ( Geometry3D.GetClosestPointToSegment( centre, feet + Vector3.Up * 0.2f, feet + Vector3.Up * 1.7f ).DistanceTo( centre ) > Trigger ) continue;
			gone = true;
			Exploded++;
			AlienBlast.Spawn( GetTree().CurrentScene, centre, 1.8f );
			foreach ( var near in PlayerSurvivor.All )
			{
				if ( !near.IsAlive || (near.WorldPosition + Vector3.Up * 1f).DistanceTo( centre ) > 3.2f ) continue;
				near.Health?.OnDamage( new DamageInfo { Damage = Damage, Attacker = this, Position = centre, Origin = centre } );
				if ( near.Controller is { } c ) c.Velocity += ((c.GlobalPosition - centre).Flat().SafeNormal() + Vector3.Up * 0.5f) * 7f;
			}
			QueueFree();
			return;
		}
	}

	/// <summary> A shot rang off the ball: sparks and a tink. </summary>
	public static void Tink( Vector3 at, Vector3 normal )
	{
		Tinks++;
		Fx.ImpactWorld( at, normal );
		Sfx.Play( "mine_tink", at, 0.8f, Rng.Float( 0.92f, 1.12f ) );
	}
}

/// <summary> The body of a sea mine that shots hit (it only rings). </summary>
public partial class SeaMineShell : StaticBody3D { }
