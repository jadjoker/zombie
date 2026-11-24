using Godot;

/// <summary>
/// Sea mines (after the old horned naval mines): a riveted steel ball studded with pegs, chained to the sea floor and
/// drifting slowly on its chain, a little up and down and side to side. Eight per area, out on the edges, never by
/// the landing spot, the arrival pads or the enemy spawn points. Bullets ring off them (a tink, no harm). Swim or
/// walk into one and it goes off (an alien blast) for 45 damage, and it is gone.
/// </summary>
public partial class SeaMine : Node3D
{
	/// <summary> How many float in each area at once. </summary>
	public const int PerArea = 16;
	/// <summary> A mine that went off or was torn away is replaced, somewhere new, after this long (s). </summary>
	public const float RespawnMin = 12f, RespawnMax = 22f;
	/// <summary> Chains this long (m): most low, a good share high up in the water. </summary>
	public const float LowMin = 1.2f, LowMax = 3.2f, HighMin = 5f, HighMax = 10f, HighShare = 0.35f;
	/// <summary> Kept this far from the middle of the area, a spawn point, a station, the player and each other (m). </summary>
	const float KeepMiddle = 14f, KeepSpawn = 9f, KeepStation = 12f, KeepPlayer = 14f, KeepApart = 9f;
	public static int Respawned;
	/// <summary> For tests: the respawn wait is scaled by this. </summary>
	public static float RespawnScale = 1f;
	/// <summary> Bumped by every Lay, so a respawn queued in an earlier area never lands in this one. </summary>
	static int generation;
	static Node3D level;
	static readonly Random random = new();
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

	/// <summary>
	/// Clears any mines and lays this area's lot at random spots (new ones every run) and random heights, some high up.
	/// Call after the ground and the stations are in place.
	/// </summary>
	public static void Lay( Node3D level )
	{
		foreach ( var old in All.ToList() ) { old.GetParent()?.RemoveChild( old ); old.Free(); }
		generation++;
		SeaMine.level = level;
		var laid = 0;
		for ( var k = 0; k < PerArea; k++ ) if ( PlaceRandom( level ) is not null ) laid++;
		GD.Print( $"[mines] {laid} sea mines laid in area {SeaTerrain.Area}" );
	}

	static float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);

	/// <summary> A random free spot on the floor for a mine in the current area, or null if none was found. </summary>
	static Vector3? RandomSpot( Node3D level )
	{
		var keepOff = level.GetTree().GetNodesInGroup( "spawn_point" ).OfType<Node3D>().Select( n => (n.GlobalPosition, KeepSpawn) ).ToList();
		keepOff.AddRange( level.GetTree().GetNodesInGroup( "buy_station" ).OfType<Node3D>().Select( n => (n.GlobalPosition, KeepStation) ) );
		keepOff.AddRange( PlayerSurvivor.All.Where( p => p.IsAlive ).Select( p => (p.RealPosition, KeepPlayer) ) );
		keepOff.AddRange( All.Where( m => !m.gone ).Select( m => (m.GlobalPosition, KeepApart) ) );
		if ( SeaTerrain.Area == 2 ) keepOff.Add( (AreaTwo.ArrivalFlat, 18f) );
		for ( var tries = 0; tries < 80; tries++ )
		{
			Vector3 at;
			if ( SeaTerrain.Area == 3 )
			{
				// Atlantis: down in the dry canals (inner or outer).
				at = Atlantis.Polar( random.NextDouble() < 0.35 ? R( 20.5f, 23.5f ) : R( 47.5f, 52.5f ), R( 0f, 360f ), Atlantis.CanalFloorY );
			}
			else
			{
				// Anywhere over the sea floor, out of the middle basin, inside the edge (spread evenly over the area).
				var angle = R( 0f, MathF.Tau );
				var radius = MathF.Sqrt( R( KeepMiddle * KeepMiddle, 88f * 88f ) );
				at = new Vector3( MathF.Cos( angle ) * radius, 0f, MathF.Sin( angle ) * radius );
				if ( SeaTerrain.Area == 2 && AreaTwo.OnPlain( at, 5f ) ) continue;   // not on the wreck
				at.Y = SeaTerrain.Active ? SeaTerrain.HeightAt( at ) : 0f;
			}
			if ( keepOff.Any( o => o.Item1.Flat().DistanceTo( at.Flat() ) < o.Item2 ) ) continue;
			return at;
		}
		return null;
	}

	/// <summary> A mine at a random free spot, on a chain of a random length (some high up). </summary>
	static SeaMine PlaceRandom( Node3D level )
	{
		if ( level is null || !IsInstanceValid( level ) || RandomSpot( level ) is not { } spot ) return null;
		var height = random.NextDouble() < HighShare ? R( HighMin, HighMax ) : R( LowMin, LowMax );
		var mine = new SeaMine { Name = "Sea Mine", anchor = spot, height = height };
		mine.phases = new Vector3( R( 0f, 6f ), R( 0f, 6f ), R( 0f, 6f ) );
		mine.rates = new Vector3( R( 0.25f, 0.5f ), R( 0.3f, 0.6f ), R( 0.2f, 0.45f ) );
		// Long chains sway a little wider.
		var loose = 1f + (height - LowMin) * 0.06f;
		mine.sway = new Vector3( R( 0.25f, 0.5f ), R( 0.12f, 0.28f ), R( 0.25f, 0.5f ) ) * loose;
		level.AddChild( mine );
		mine.GlobalPosition = spot;
		return mine;
	}

	/// <summary> This mine is gone (went off, or torn away): another comes up somewhere new a little later. </summary>
	void QueueRespawn()
	{
		var gen = generation;
		GetTree().CreateTimer( R( RespawnMin, RespawnMax ) * RespawnScale, false ).Timeout += () =>
		{
			if ( gen != generation || level is null || !IsInstanceValid( level ) || All.Count( m => !m.gone ) >= PerArea ) return;
			if ( PlaceRandom( level ) is { } fresh ) { Respawned++; fresh.rising = 0f; }
		};
	}

	/// <summary> 0 to 1 while a fresh mine rises out of the sand on its chain (1: fully up). </summary>
	float rising = 1f;

	public override void _Ready()
	{
		var steel = Sculpt.Paint( new Color( 0.26f, 0.52f, 0.56f ), 0.5f, 0.35f );
		var dark = Sculpt.Paint( new Color( 0.13f, 0.27f, 0.32f ), 0.55f, 0.4f );
		var peg = Sculpt.Paint( new Color( 0.42f, 0.66f, 0.66f ), 0.45f, 0.35f );
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
		if ( rising < 1f ) rising = MathF.Min( 1f, rising + dt / 2.5f );
		var up = height * (1f - (1f - rising) * (1f - rising));
		ball.Position = Vector3.Up * up + offset * rising;
		ball.Rotation = new Vector3( offset.Z * 0.25f, time * 0.08f, -offset.X * 0.25f );
		// The chain hangs in a slight curve from the floor to the ball's underside.
		var top = ball.Position + Vector3.Down * (Radius + 0.08f);
		top.Y = MathF.Max( top.Y, 0.12f );
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

	float poseWait, poseDt;

	public override void _Process( double delta )
	{
		if ( gone ) return;
		// The chain is up to 60-odd links: a far mine's slow sway is laid out a few times a second, not every frame.
		poseDt += (float)delta;
		poseWait -= (float)delta;
		if ( poseWait <= 0f || rising < 1f )
		{
			var camera = GetViewport().GetCamera3D();
			var far = camera is null ? 0f : camera.GlobalPosition.DistanceTo( GlobalPosition );
			poseWait = far < 15f ? 0f : far < 40f ? 1f / 20f : 1f / 8f;
			Pose( poseDt );
			poseDt = 0f;
		}
		var centre = ball.GlobalPosition;
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var feet = player.RealPosition;
			if ( Geometry3D.GetClosestPointToSegment( centre, feet + Vector3.Up * 0.2f, feet + Vector3.Up * 1.7f ).DistanceTo( centre ) > Trigger ) continue;
			gone = true;
			Exploded++;
			QueueRespawn();
			AlienBlast.Spawn( GetTree().CurrentScene, centre, 1.8f );
			foreach ( var near in PlayerSurvivor.All )
			{
				if ( !near.IsAlive || (near.RealPosition + Vector3.Up * 1f).DistanceTo( centre ) > 3.2f ) continue;
				near.Health?.OnDamage( new DamageInfo { Damage = Damage, Attacker = this, Position = centre, Origin = centre } );
				if ( near.Controller is { } c ) c.Velocity += ((c.GlobalPosition - centre).Flat().SafeNormal() + Vector3.Up * 0.5f) * 7f;
			}
			QueueFree();
			return;
		}
	}

	/// <summary> Where the ball is (the chain sways it about). </summary>
	public Vector3 BallPosition => ball is not null && IsInstanceValid( ball ) ? ball.GlobalPosition : GlobalPosition;
	public bool Gone => gone;
	/// <summary> How long its chain is (m): how high the ball floats over the floor. </summary>
	public float Height => height;

	/// <summary> A mine laid at a spot (tests; the areas use Lay). </summary>
	public static SeaMine PlaceAt( Node parent, Vector3 floor, float height = 1.8f )
	{
		var mine = new SeaMine { Name = "Sea Mine", anchor = floor, height = height, phases = new Vector3( 1f, 2f, 3f ), rates = new Vector3( 0.3f, 0.4f, 0.35f ), sway = new Vector3( 0.3f, 0.2f, 0.3f ) };
		parent.AddChild( mine );
		mine.GlobalPosition = floor;
		return mine;
	}

	/// <summary>
	/// The Gore Vac tore it off its chain: the ball comes loose (disarmed, it no longer goes off when walked into, and
	/// shots no longer ring off it), the chain and anchor are gone. Returns the ball, now a node of its own in the scene.
	/// </summary>
	public Node3D TearLoose()
	{
		if ( gone || ball is null ) return null;
		gone = true;
		QueueRespawn();
		foreach ( var shell in ball.FindChildren( "*", "", true, false ).OfType<SeaMineShell>() ) shell.QueueFree();
		var loose = ball;
		loose.Reparent( GetTree().CurrentScene, true );
		ball = null;
		Sfx.Play( "mine_tink", loose.GlobalPosition, 1f, 0.7f );
		QueueFree();
		return loose;
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
