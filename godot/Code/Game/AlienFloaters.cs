using Godot;

/// <summary>
/// The glow bulbs (the clutter pass): small glowing alien critters that drift about in schools, bobbing and turning.
/// They shy away from a player who comes within <see cref="FleeRadius"/>, leaving a short trail of light, and drift
/// back home once left alone. A shot pops one for <see cref="BulbPoints"/>; about one in <see cref="GoldEvery"/> is gold,
/// worth more, and drops a pickup. A popped bulb grows back after <see cref="RegrowAfter"/>. Star ferns sit still on
/// the sand. Colours keep off the enemies' neon green: teal and violet, gold for the rare ones. Metres.
/// </summary>
public partial class AlienFloaters : Node3D
{
	public const float FleeRadius = 6f, FleeSpeed = 7f, MaxScare = 9f, CalmAfter = 2.5f, RegrowAfter = 45f;
	public const int BulbPoints = 25, GoldPoints = 150, GoldEvery = 40;
	/// <summary> Tests: bulbs popped (gold among them), and the furthest any has fled. </summary>
	public static int Popped, GoldPopped;

	internal sealed class School
	{
		public Vector3 Home;
		public float Phase, Rate, Wander;
	}

	internal sealed class Floater
	{
		public Node3D Node;
		public School School;
		public Vector3 Offset, Scare;
		public float Phase, Bob, Rate, Spin, Size;
		public bool Fern, Gold, IsPopped;
		public TimeSince SinceScared = 99f, SincePopped, SinceTrail;
		public GlowBulb Body;
		public MeshInstance3D Bulb;
		public OmniLight3D Light;
		public Vector3 Last;
	}

	readonly List<Floater> all = new();
	public int Count => all.Count;
	/// <summary> Tests: the bulbs (not the ferns) that are out, where they are. </summary>
	public IEnumerable<Node3D> Bulbs => all.Where( f => !f.Fern && !f.IsPopped ).Select( f => f.Node );

	static StandardMaterial3D Glow( Color c, float energy )
	{
		var m = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, Roughness = 0.3f, RimEnabled = true, Rim = 0.5f };
		m.SetMeta( "no_caustics", true );
		return m;
	}
	static StandardMaterial3D Frond( Color c ) => new()
	{
		AlbedoColor = c, Roughness = 0.45f, RimEnabled = true, Rim = 0.6f, RimTint = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		// Out of the way up close: anything within reach of the camera dithers away.
		DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelDither, DistanceFadeMinDistance = 0.6f, DistanceFadeMaxDistance = 2f,
	};

	static readonly StandardMaterial3D[] BulbGlow = { Glow( new Color( 0.25f, 0.85f, 0.85f ), 1.5f ), Glow( new Color( 0.62f, 0.38f, 1f ), 1.5f ), Glow( new Color( 0.35f, 0.6f, 1f ), 1.4f ) };
	static readonly StandardMaterial3D GoldGlow = Glow( new Color( 1f, 0.78f, 0.25f ), 2.6f );
	static readonly StandardMaterial3D[] Fronds = { Frond( new Color( 0.03f, 0.03f, 0.05f ) ), Frond( new Color( 0.2f, 0.06f, 0.3f ) ), Frond( new Color( 0.04f, 0.1f, 0.12f ) ) };
	static readonly StandardMaterial3D TrailGlow = Glow( new Color( 0.6f, 0.9f, 1f ), 2.2f );

	MeshInstance3D Part( Node3D node, Mesh mesh, Material material, Vector3 position, Basis basis )
	{
		var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		node.AddChild( m );
		m.Transform = new Transform3D( basis, position );
		return m;
	}

	/// <summary> A school of bulbs drifting together round a spot. </summary>
	public void SpawnSchool( Random random, Vector3 centre, int count )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var school = new School { Home = centre, Phase = R( 0f, MathF.Tau ), Rate = R( 0.04f, 0.09f ), Wander = R( 1.5f, 3.5f ) };
		for ( var i = 0; i < count; i++ )
			AddBulb( random, school, new Vector3( R( -2.5f, 2.5f ), R( -0.8f, 1.2f ), R( -2.5f, 2.5f ) ) );
	}

	/// <summary> One bulb at a spot (a school of one). Kept for the wreck's rooms and older callers. </summary>
	public void Spawn( Random random, Vector3 at, float? ground = null ) =>
		AddBulb( random, new School { Home = at, Phase = (float)random.NextDouble() * MathF.Tau, Rate = 0.06f, Wander = 0.6f }, Vector3.Zero );

	void AddBulb( Random random, School school, Vector3 offset )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var node = new Node3D { Name = "Floater" };
		AddChild( node );
		node.GlobalPosition = school.Home + offset;
		var size = R( 0.16f, 0.28f );
		var frond = Fronds[random.Next( Fronds.Length )];
		// A glowing bulb under a dark cap, with a few short wisps trailing (no long legs: they read as enemies).
		var bulb = Part( node, new SphereMesh { Radius = size, Height = size * 2.2f, RadialSegments = 12, Rings = 7 }, BulbGlow[random.Next( BulbGlow.Length )], Vector3.Zero, Basis.Identity );
		Part( node, new SphereMesh { Radius = size * 1.15f, Height = size * 1.1f, RadialSegments = 12, Rings = 5, IsHemisphere = true }, frond, Vector3.Up * size * 0.35f, Basis.Identity );
		for ( var i = random.Next( 3, 5 ); i > 0; i-- )
		{
			var len = R( 0.25f, 0.6f ) * (size / 0.22f);
			var a = R( 0f, MathF.Tau );
			var tilt = new Basis( new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ), R( 0.05f, 0.3f ) );
			Part( node, new CylinderMesh { TopRadius = size * 0.1f, BottomRadius = 0.006f, Height = len, RadialSegments = 4, Rings = 1 }, frond, tilt * (Vector3.Down * (size * 0.6f + len * 0.5f)), tilt );
		}
		var body = new GlowBulb { Name = "Bulb Body", Owner2 = this, CollisionLayer = Layers.Creature, CollisionMask = 0 };
		body.AddChild( new CollisionShape3D { Shape = new SphereShape3D { Radius = size * 1.4f } } );
		node.AddChild( body );
		var f = new Floater
		{
			Node = node, School = school, Offset = offset, Phase = R( 0f, MathF.Tau ), Bob = R( 0.15f, 0.4f ), Rate = R( 0.3f, 0.6f ),
			Spin = R( -0.4f, 0.4f ), Size = size, Body = body, Bulb = bulb, Last = node.GlobalPosition,
		};
		body.Floater = f;
		Gild( f, random.Next( GoldEvery ) == 0 );
		all.Add( f );
	}

	/// <summary> Tests: the nearest bulb that is out, made gold or plain. </summary>
	public Node3D NearestBulb( Vector3 to, bool? gold = null )
	{
		var f = all.Where( x => !x.Fern && !x.IsPopped ).OrderBy( x => x.Node.GlobalPosition.DistanceSquaredTo( to ) ).FirstOrDefault();
		if ( f is null ) return null;
		if ( gold is { } g ) Gild( f, g );
		return f.Node;
	}

	/// <summary> Makes a bulb gold (a light of its own, worth more, drops a pickup) or plain again. </summary>
	void Gild( Floater f, bool gold )
	{
		f.Gold = gold;
		if ( gold )
		{
			f.Bulb.MaterialOverride = GoldGlow;
			f.Light ??= new OmniLight3D { LightColor = new Color( 1f, 0.8f, 0.3f ), LightEnergy = 0.9f, OmniRange = 2.5f, ShadowEnabled = false };
			if ( f.Light.GetParent() is null ) f.Node.AddChild( f.Light );
		}
		else if ( f.Light is not null ) { f.Light.QueueFree(); f.Light = null; }
		if ( !gold && f.Bulb.MaterialOverride == GoldGlow ) f.Bulb.MaterialOverride = BulbGlow[0];
	}

	/// <summary> A spiky star fern resting on the sand, barely moving. </summary>
	public void SpawnFern( Random random, Vector3 at, float ground )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var node = new Node3D { Name = "Floater" };
		AddChild( node );
		node.GlobalPosition = at with { Y = ground + R( 0.25f, 0.45f ) };
		var frond = Fronds[random.Next( Fronds.Length )];
		var tip = BulbGlow[random.Next( BulbGlow.Length )];
		for ( var i = random.Next( 5, 8 ); i > 0; i-- )
		{
			var dir = new Vector3( R( -1f, 1f ), R( 0.25f, 1f ), R( -1f, 1f ) ).Normalized();
			var len = R( 0.35f, 0.9f );
			var basis = new Basis( new Quaternion( Vector3.Up, dir ) );
			Part( node, new BoxMesh { Size = new Vector3( 0.14f, len, 0.02f ) }, frond, dir * len * 0.5f, basis );
			Part( node, new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 6, Rings = 3 }, tip, dir * len, Basis.Identity );
		}
		all.Add( new Floater { Node = node, School = new School { Home = node.GlobalPosition }, Fern = true, Phase = R( 0f, MathF.Tau ), Rate = R( 0.2f, 0.4f ) } );
	}

	/// <summary>
	/// After the ground changes (the wreck site, Atlantis): drops the ones where `remove` says, sets the rest at the
	/// same height above the new ground.
	/// </summary>
	public void Resettle( Func<Vector3, bool> remove )
	{
		var schools = new Dictionary<School, Vector3>();
		foreach ( var f in all.ToList() )
		{
			if ( remove( f.School.Home ) ) { f.Node.QueueFree(); all.Remove( f ); continue; }
			if ( schools.ContainsKey( f.School ) ) continue;
			var lift = f.School.Home.Y - SeaTerrain.HeightAt( f.School.Home );
			schools[f.School] = f.School.Home with { Y = SeaTerrain.HeightAt( f.School.Home ) + lift };
		}
		foreach ( var (school, home) in schools ) school.Home = home;
	}

	/// <summary> A shot found a bulb: it pops in a burst of light, for points (and a pickup if it was gold). </summary>
	internal void Pop( GlowBulb body, Node attacker )
	{
		var f = body.Floater;
		if ( f is null || f.IsPopped ) return;
		f.IsPopped = true;
		f.SincePopped = 0f;
		f.Node.Visible = false;
		body.CollisionLayer = 0;
		Popped++;
		var at = f.Node.GlobalPosition;
		Burst( at, f.Gold ? GoldGlow : (StandardMaterial3D)f.Bulb.MaterialOverride, f.Gold ? 16 : 10 );
		Sfx.Play( "bubble_pop", at, 0.9f, Rng.Float( 1.1f, 1.4f ) );
		Sfx.Play( "glass_hit", at, 0.3f, Rng.Float( 1.6f, 2f ) );
		var survivor = (attacker as PlayerController)?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) ?? attacker as PlayerSurvivor;
		survivor?.RewardPoints( f.Gold ? GoldPoints : BulbPoints );
		if ( f.Gold )
		{
			GoldPopped++;
			Sfx.Play2D( "buy", 0.6f, 1.5f );
			PerkPickup.DropAt( GetTree().CurrentScene, f.Node, at, RandomPerk() );
		}
	}

	static PerkKind RandomPerk()
	{
		var total = PlayerPerks.Table.Sum( e => e.Chance );
		var roll = Rng.Float() * total;
		foreach ( var e in PlayerPerks.Table )
		{
			if ( roll < e.Chance ) return e.Kind;
			roll -= e.Chance;
		}
		return PlayerPerks.Table[0].Kind;
	}

	/// <summary> Little glowing motes flying out from a pop, shrinking away. </summary>
	void Burst( Vector3 at, Material material, int count )
	{
		var root = GetTree().CurrentScene;
		for ( var i = 0; i < count; i++ )
		{
			var mote = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 6, Rings = 3 }, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			root.AddChild( mote );
			mote.GlobalPosition = at;
			var tween = mote.CreateTween().SetParallel();
			tween.TweenProperty( mote, "global_position", at + Rng.OnSphere() * Rng.Float( 0.6f, 1.4f ), 0.5f ).SetEase( Tween.EaseType.Out ).SetTrans( Tween.TransitionType.Cubic );
			tween.TweenProperty( mote, "scale", Vector3.One * 0.05f, 0.55f );
			tween.Chain().TweenCallback( Callable.From( mote.QueueFree ) );
		}
	}

	/// <summary> A fading dot of light left behind a fleeing bulb. </summary>
	void Trail( Vector3 at )
	{
		var dot = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 6, Rings = 3 }, MaterialOverride = TrailGlow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		GetTree().CurrentScene.AddChild( dot );
		dot.GlobalPosition = at;
		var tween = dot.CreateTween();
		tween.TweenProperty( dot, "scale", Vector3.One * 0.05f, 0.6f );
		tween.TweenCallback( Callable.From( dot.QueueFree ) );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var t = (float)Clock.Now;
		var players = PlayerSurvivor.All.Where( p => p.IsAlive ).Select( p => p.Controller.GlobalPosition + Vector3.Up * 1f ).ToList();
		foreach ( var f in all )
		{
			if ( f.Fern )
			{
				f.Node.Rotation = new Vector3( MathF.Sin( t * f.Rate + f.Phase ) * 0.05f, f.Node.Rotation.Y, MathF.Cos( t * f.Rate * 0.8f + f.Phase ) * 0.05f );
				continue;
			}
			if ( f.IsPopped )
			{
				if ( f.SincePopped < RegrowAfter ) continue;
				// Grows back at home, calm, and maybe gold this time.
				f.IsPopped = false;
				f.Scare = Vector3.Zero;
				f.Node.Visible = true;
				f.Node.Scale = Vector3.One * 0.05f;
				f.Node.CreateTween().TweenProperty( f.Node, "scale", Vector3.One, 1.2f );
				f.Body.CollisionLayer = Layers.Creature;
				Gild( f, Rng.Int( 0, GoldEvery - 1 ) == 0 );
			}

			var s = f.School;
			var a = t * s.Rate + s.Phase;
			var drift = new Vector3( MathF.Cos( a ) * s.Wander, MathF.Sin( a * 1.7f ) * 0.4f, MathF.Sin( a * 0.8f ) * s.Wander );
			var b = t * f.Rate + f.Phase;
			var bob = new Vector3( MathF.Cos( b ) * 0.3f, MathF.Sin( b * 2.1f ) * f.Bob, MathF.Sin( b * 0.9f ) * 0.3f );
			var at = s.Home + drift + f.Offset + bob + f.Scare;

			// Shy: a player close by pushes it away (and a little up); left alone, it drifts home.
			var scared = false;
			foreach ( var p in players )
			{
				var away = at - p;
				var d = away.Length();
				if ( d > FleeRadius ) continue;
				var dir = (away.Flat().SafeNormal() + Vector3.Up * 0.35f).Normalized();
				f.Scare += dir * FleeSpeed * (1.2f - d / FleeRadius) * dt;
				scared = true;
			}
			if ( scared ) { f.SinceScared = 0f; if ( f.Scare.Length() > MaxScare ) f.Scare = f.Scare.Normalized() * MaxScare; }
			else if ( f.SinceScared > CalmAfter ) f.Scare = f.Scare.Lerp( Vector3.Zero, 1f - MathF.Exp( -0.35f * dt ) );
			at = s.Home + drift + f.Offset + bob + f.Scare;

			// A trail of light while it darts off.
			var speed = (at - f.Last).Length() / MathF.Max( dt, 0.001f );
			if ( speed > 2.5f && f.SinceTrail > 0.07f ) { f.SinceTrail = 0f; Trail( f.Last ); }
			f.Last = at;

			var lean = new Basis( Vector3.Right, MathF.Sin( b * 1.7f ) * 0.15f ) * new Basis( Vector3.Forward, MathF.Cos( b * 1.3f ) * 0.12f );
			f.Node.GlobalTransform = new Transform3D( new Basis( Vector3.Up, t * f.Spin + f.Phase ) * lean, at );
		}
	}
}

/// <summary> The shootable body of a glow bulb: one hit pops it. </summary>
public partial class GlowBulb : StaticBody3D, IShootable
{
	internal AlienFloaters Owner2;
	internal AlienFloaters.Floater Floater;
	public Node ShotOwner => this;
	public bool IsDead => CollisionLayer == 0;
	public void ReceiveShot( float damage, Node3D hitPart, Vector3 direction, Vector3 position, Vector3 origin, Node attacker, bool scoreHit )
	{
		if ( IsDead || Owner2 is null ) return;
		Owner2.Pop( this, attacker );
	}
}
