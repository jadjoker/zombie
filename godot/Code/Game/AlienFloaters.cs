using Godot;

/// <summary>
/// The glow bulbs and star ferns: living hazards on the sea floor (the clutter pass).
/// - Glow bulbs drift about in small schools. A player who comes within <see cref="PuffRadius"/> sets one off: it puffs
///   up, flashing faster and faster, and after <see cref="PuffTime"/> it bursts, hurting players within
///   <see cref="BlastRadius"/> and any enemies there. A shot bursts it at once (no warning), for <see cref="BulbPoints"/>,
///   so shooting one beside a crowd of enemies is a trick worth knowing. About one in <see cref="GoldEvery"/> is gold:
///   worth more and drops a pickup. A burst bulb grows back after <see cref="RegrowAfter"/>.
/// - Star ferns sit on the sand. A player within <see cref="FernReach"/> makes one shudder, then its spikes shoot out
///   and stick anyone still in reach for <see cref="FernDamage"/>; it pulls them back and rests for
///   <see cref="FernRest"/>.
/// Colours keep off the enemies' neon green: teal and violet, gold for the rare bulbs. Metres.
/// </summary>
public partial class AlienFloaters : Node3D
{
	public const float PuffRadius = 3.5f, PuffTime = 0.9f, BlastRadius = 3.2f, BlastDamage = 20f, BlastCreatureDamage = 80f, RegrowAfter = 45f;
	public const float FernReach = 2.2f, FernTell = 0.4f, FernDamage = 12f, FernRest = 3f;
	public const int BulbPoints = 25, GoldPoints = 150, GoldEvery = 40;
	/// <summary> Seconds after a burst hurts a player in which no other burst can (a school going off hurts once). </summary>
	public const float BlastGrace = 0.8f;
	readonly Dictionary<PlayerSurvivor, double> lastBlastHit = new();
	/// <summary> Tests: bulbs burst (shot, gold among them, set off by walking up) and fern stabs that landed. </summary>
	public static int Popped, GoldPopped, Puffed, Stabs;

	internal sealed class School
	{
		public Vector3 Home;
		public float Phase, Rate, Wander;
	}

	internal enum FernState { Rest, Tell, Out }

	internal sealed class Floater
	{
		public Node3D Node;
		public School School;
		public Vector3 Offset;
		public float Phase, Bob, Rate, Spin, Size;
		public bool Fern, Gold, IsPopped, Puffing;
		public TimeSince SincePopped, SincePuff;
		public GlowBulb Body;
		public MeshInstance3D Bulb;
		public Material BulbLook;
		public OmniLight3D Light;
		// Ferns: the blades (pivots that stretch out to stab), and what the fern is doing.
		public List<Node3D> Blades;
		public FernState State;
		public TimeSince SinceState = 99f;
		public bool Struck;
		/// <summary> Where it was put last frame (read back from here, not from the engine, every frame). </summary>
		public Vector3? At;
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
	/// <summary> The warning flash of a bulb about to burst, and a fern about to strike. </summary>
	static readonly StandardMaterial3D Hot = Glow( new Color( 1f, 0.92f, 0.85f ), 4f );
	static readonly StandardMaterial3D[] Fronds = { Frond( new Color( 0.03f, 0.03f, 0.05f ) ), Frond( new Color( 0.2f, 0.06f, 0.3f ) ), Frond( new Color( 0.04f, 0.1f, 0.12f ) ) };

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
		var look = BulbGlow[random.Next( BulbGlow.Length )];
		var bulb = Part( node, new SphereMesh { Radius = size, Height = size * 2.2f, RadialSegments = 12, Rings = 7 }, look, Vector3.Zero, Basis.Identity );
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
			Spin = R( -0.4f, 0.4f ), Size = size, Body = body, Bulb = bulb, BulbLook = look,
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

	/// <summary> Tests: the nearest fern. </summary>
	public Node3D NearestFern( Vector3 to ) =>
		all.Where( x => x.Fern ).OrderBy( x => x.Node.GlobalPosition.DistanceSquaredTo( to ) ).FirstOrDefault()?.Node;

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
		else
		{
			if ( f.Light is not null ) { f.Light.QueueFree(); f.Light = null; }
			f.Bulb.MaterialOverride = f.BulbLook;
		}
	}

	/// <summary> A spiky star fern resting on the sand. </summary>
	public void SpawnFern( Random random, Vector3 at, float ground )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var node = new Node3D { Name = "Floater" };
		AddChild( node );
		node.GlobalPosition = at with { Y = ground + R( 0.25f, 0.45f ) };
		var frond = Fronds[random.Next( Fronds.Length )];
		var tip = BulbGlow[random.Next( BulbGlow.Length )];
		var blades = new List<Node3D>();
		for ( var i = random.Next( 5, 8 ); i > 0; i-- )
		{
			// Each blade on its own pivot at the centre, pointing out along its local up, so it can shoot out.
			var dir = new Vector3( R( -1f, 1f ), R( 0.25f, 1f ), R( -1f, 1f ) ).Normalized();
			var len = R( 0.35f, 0.9f );
			var pivot = new Node3D { Name = "Blade" };
			node.AddChild( pivot );
			pivot.Basis = new Basis( new Quaternion( Vector3.Up, dir ) );
			Part( pivot, new BoxMesh { Size = new Vector3( 0.14f, len, 0.02f ) }, frond, Vector3.Up * len * 0.5f, Basis.Identity );
			Part( pivot, new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 6, Rings = 3 }, tip, Vector3.Up * len, Basis.Identity ).Name = "Tip";
			blades.Add( pivot );
		}
		all.Add( new Floater { Node = node, School = new School { Home = node.GlobalPosition }, Fern = true, Phase = R( 0f, MathF.Tau ), Rate = R( 0.2f, 0.4f ), Blades = blades, BulbLook = tip } );
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
		foreach ( var f in all.Where( f => f.Fern ) ) f.Node.GlobalPosition = f.School.Home; foreach ( var f in all ) f.At = null;   // moved: read again
	}

	/// <summary> A shot found a bulb: it bursts at once, for points (and a pickup if it was gold). </summary>
	internal void Pop( GlowBulb body, Node attacker ) => Burst( body.Floater, attacker as PlayerController ?? (attacker as PlayerSurvivor)?.Controller );

	/// <summary>
	/// A bulb bursts: a blast of light that hurts players and enemies close by. Shot, the shooter gets the points and the
	/// credit for any enemies it hurts; set off by walking up to it, nobody does.
	/// </summary>
	void Burst( Floater f, PlayerController shooter )
	{
		if ( f is null || f.IsPopped ) return;
		f.IsPopped = true;
		f.Puffing = false;
		f.SincePopped = 0f;
		f.Node.Visible = false;
		f.Body.CollisionLayer = 0;
		var at = f.Node.GlobalPosition;
		AlienBlast.Spawn( GetTree().CurrentScene, at, 0.8f );
		Motes( at, f.Gold ? GoldGlow : f.BulbLook, f.Gold ? 16 : 10 );
		Sfx.Play( "bubble_pop", at, 1f, Rng.Float( 0.7f, 0.9f ) );
		Sfx.Play( "missile_boom", at, 0.45f, Rng.Float( 1.5f, 1.8f ) );
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || (p.RealPosition + Vector3.Up * 1f).DistanceTo( at ) > BlastRadius ) continue;
			// A school going off together hurts once, not once per bulb.
			if ( lastBlastHit.TryGetValue( p, out var when ) && Clock.Now - when < BlastGrace ) continue;
			lastBlastHit[p] = Clock.Now;
			p.Health?.OnDamage( new DamageInfo { Damage = BlastDamage, Attacker = this, Position = at, Origin = at } );
			if ( p.Controller is { } c ) c.Velocity += ((c.GlobalPosition - at).Flat().SafeNormal() + Vector3.Up * 0.4f) * 4f;
		}
		PlayerPerks.HurtCreatures( at, BlastRadius, BlastCreatureDamage, shooter );
		if ( shooter is null ) { Puffed++; return; }
		Popped++;
		shooter.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.RewardPoints( f.Gold ? GoldPoints : BulbPoints );
		if ( f.Gold )
		{
			GoldPopped++;
			Sfx.Play2D( "buy", 0.6f, 1.5f );
			// A pickup the robot being played can have; ammo if there is none (Vac-Bot).
			if ( RandomPerk() is { } kind ) PerkPickup.DropAt( GetTree().CurrentScene, f.Node, at, kind );
			else AmmoPickup.Drop( f.Node );
		}
	}

	static PerkKind? RandomPerk()
	{
		var total = PlayerPerks.Table.Where( e => PlayerPerks.Drops( e.Kind ) ).Sum( e => e.Chance );
		if ( total <= 0f ) return null;
		var roll = Rng.Float() * total;
		foreach ( var e in PlayerPerks.Table )
		{
			if ( !PlayerPerks.Drops( e.Kind ) ) continue;
			if ( roll < e.Chance ) return e.Kind;
			roll -= e.Chance;
		}
		return null;
	}

	/// <summary> Little glowing motes flying out from a burst, shrinking away. </summary>
	void Motes( Vector3 at, Material material, int count )
	{
		var root = GetTree().CurrentScene;
		for ( var i = 0; i < count; i++ )
		{
			var mote = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 6, Rings = 3 }, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			root.AddChild( mote );
			mote.GlobalPosition = at;
			var tween = mote.CreateTween().SetParallel();
			tween.TweenProperty( mote, "global_position", at + Rng.OnSphere() * Rng.Float( 0.8f, 2f ), 0.5f ).SetEase( Tween.EaseType.Out ).SetTrans( Tween.TransitionType.Cubic );
			tween.TweenProperty( mote, "scale", Vector3.One * 0.05f, 0.55f );
			tween.Chain().TweenCallback( Callable.From( mote.QueueFree ) );
		}
	}

	static float Nearest( List<Vector3> players, Vector3 at )
	{
		var best = float.MaxValue;
		foreach ( var p in players ) best = MathF.Min( best, p.DistanceTo( at ) );
		return best;
	}

	// Reused every frame (they were built afresh each frame).
	readonly List<Vector3> chests = new(), feet = new();
	/// <summary> Bulbs this far from every player move every other frame (their drift is slow; nobody can tell). </summary>
	const float FarAway = 45f;
	ulong frames;

	public override void _Process( double delta )
	{
		var t = (float)Clock.Now;
		frames++;
		chests.Clear();
		feet.Clear();
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive ) continue;
			var at = p.Controller.GlobalPosition;
			chests.Add( at + Vector3.Up * 1f );
			feet.Add( at + Vector3.Up * 0.4f );
		}
		for ( var i = 0; i < all.Count; i++ )
		{
			var f = all[i];
			if ( f.Fern ) { UpdateFern( f, feet, t ); continue; }
			if ( f.IsPopped )
			{
				if ( f.SincePopped < RegrowAfter ) continue;
				// Grows back at home, and maybe gold this time.
				f.IsPopped = false;
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
			var at = s.Home + drift + f.Offset + bob;

			// A player too close sets it off: it swells and flashes, faster and faster, then bursts.
			f.At ??= f.Node.GlobalPosition;
			var nearest = Nearest( chests, f.At.Value );
			if ( !f.Puffing && nearest > FarAway && ((frames + (ulong)i) & 1) == 1 ) continue;
			if ( !f.Puffing && nearest < PuffRadius )
			{
				f.Puffing = true;
				f.SincePuff = 0f;
				Sfx.Play( "trident_charge", f.Node.GlobalPosition, 0.5f, 1.6f );
			}
			var scale = 1f;
			if ( f.Puffing )
			{
				var k = MathX.Clamp( (float)f.SincePuff / PuffTime, 0f, 1f );
				scale = 1f + 1.1f * k * k + MathF.Sin( (float)f.SincePuff * 40f ) * 0.08f * k;
				var flashRate = MathX.Lerp( 6f, 26f, k );
				f.Bulb.MaterialOverride = MathF.Sin( (float)f.SincePuff * flashRate ) > 0f ? Hot : (f.Gold ? GoldGlow : f.BulbLook);
				if ( k >= 1f ) { f.Bulb.MaterialOverride = f.Gold ? GoldGlow : f.BulbLook; f.Node.Scale = Vector3.One; Burst( f, null ); continue; }
				at = f.At.Value;   // it stops drifting while it swells
			}

			var lean = new Basis( Vector3.Right, MathF.Sin( b * 1.7f ) * 0.15f ) * new Basis( Vector3.Forward, MathF.Cos( b * 1.3f ) * 0.12f );
			f.Node.GlobalTransform = new Transform3D( new Basis( Vector3.Up, t * f.Spin + f.Phase ) * lean * Basis.FromScale( Vector3.One * scale ), at );
			f.At = at;
		}
	}

	/// <summary> A fern: resting (a slight sway), shuddering as a warning, then its spikes shoot out and draw back. </summary>
	void UpdateFern( Floater f, List<Vector3> feet, float t )
	{
		f.At ??= f.Node.GlobalPosition;   // ferns stay put: read once
		var centre = f.At.Value;
		switch ( f.State )
		{
			case FernState.Rest:
				f.Node.Rotation = new Vector3( MathF.Sin( t * f.Rate + f.Phase ) * 0.05f, f.Node.Rotation.Y, MathF.Cos( t * f.Rate * 0.8f + f.Phase ) * 0.05f );
				if ( f.SinceState > FernRest && Nearest( feet, centre ) < FernReach )
				{
					f.State = FernState.Tell;
					f.SinceState = 0f;
					Sfx.Play( "crab_snap", centre, 0.5f, 0.7f );
					SetTips( f, Hot );
				}
				break;
			case FernState.Tell:
				// A shudder: the warning to step back.
				f.Node.Rotation = new Vector3( Rng.Float( -0.12f, 0.12f ), f.Node.Rotation.Y, Rng.Float( -0.12f, 0.12f ) );
				if ( f.SinceState > FernTell ) { f.State = FernState.Out; f.SinceState = 0f; f.Struck = false; Sfx.Play( "stab_squish_0", centre, 0.7f, 1.2f ); }
				break;
			case FernState.Out:
			{
				// Out fast, then back slowly.
				var s = (float)f.SinceState;
				var reach = s < 0.08f ? MathX.Lerp( 1f, 2.1f, s / 0.08f ) : MathX.Lerp( 2.1f, 1f, MathX.Clamp( (s - 0.25f) / 0.6f, 0f, 1f ) );
				// Thinner as they stretch: needles, not planks.
				var thin = 1f / MathF.Sqrt( reach );
				foreach ( var blade in f.Blades ) blade.Scale = new Vector3( thin, reach, thin );
				if ( !f.Struck && s >= 0.08f )
				{
					f.Struck = true;
					foreach ( var p in PlayerSurvivor.All )
					{
						if ( !p.IsAlive || (p.RealPosition + Vector3.Up * 0.4f).DistanceTo( centre ) > FernReach * 1.3f ) continue;
						p.Health?.OnDamage( new DamageInfo { Damage = FernDamage, Attacker = this, Position = centre, Origin = centre } );
						Stabs++;
						Sfx.Play( "hit_flesh", p.RealPosition + Vector3.Up, 0.7f );
					}
				}
				if ( s > 0.9f ) { f.State = FernState.Rest; f.SinceState = 0f; SetTips( f, f.BulbLook ); foreach ( var blade in f.Blades ) blade.Scale = Vector3.One; }
				break;
			}
		}
	}

	static void SetTips( Floater f, Material material )
	{
		foreach ( var blade in f.Blades )
			if ( blade.GetNodeOrNull<MeshInstance3D>( "Tip" ) is { } tip ) tip.MaterialOverride = material;
	}
}

/// <summary> The shootable body of a glow bulb: one hit bursts it. </summary>
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
