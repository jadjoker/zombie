using Godot;

/// <summary>
/// The Bubble Cannon (Vac-Bot's second gun).
///
/// Left click blows a soap bubble: fast, curving a little onto an enemy near its path, drifting up, and bouncing once
/// off the world before it pops. Keep holding after a shot (ChargeTime) to blow a Mega Bubble: big and slow, it rolls
/// through enemies wrapping up every light one it touches (up to `BubbleShot.MegaCatches`) and blows up on a heavy one.
///
/// A light enemy caught (`Creature.Pullable`) is held in a BubbleTrap: its brain is off (`Creature.Bubbled`), it floats
/// up struggling against the film, and the bubble pops after `BubbleTrap.HoldTime`, on a real hit, or on right click
/// (pops all of yours). A pop hurts everything near it and sets off other bubbles near it in a chain (each link hits
/// harder); the enemy then drops and lands harder the higher it was. Another bubble fired into a trapped enemy inflates
/// its bubble (higher, longer). Heavy enemies and bosses just take a splash.
/// </summary>
public partial class PlayerWeapon
{
	/// <summary> Fires soap bubbles (BubbleShot) that trap light enemies; right click pops them. </summary>
	public bool Bubbles;

	public static PlayerWeapon BubbleCannon( int slot = 5 ) => new()
	{
		Name = "BubbleCannon", DisplayName = "Bubble Cannon", Slot = slot, ModelName = "bubble", Bubbles = true,
		Damage = 45f, FireRate = 2.6f, Range = 2400f * U.In, SpreadDegrees = 0.4f, Recoil = 1.2f,
		MagazineSize = 6, StartingReserve = 48, ReloadTime = 1.7f, Automatic = false, ChargeTime = 0.7f,
		MuzzleFlashScale = 0f, ShakePerShot = 0.12f, FovKickPerShot = 1.5f, ShootSound = "bubble_blow",
	};

	void BlowBubble( Vector3 from, Vector3 start, Vector3 forward, float spread, bool mega )
	{
		var direction = (forward + Rng.InSphere( 1f ) * spread).Normalized();
		// Aim help: an enemy just beside the crosshair is what the bubble heads for.
		var exclude = new Godot.Collections.Array<Rid> { controller.GetRid() };
		var aim = Trace.Ray( controller, start, start + direction * Range, Layers.World | Layers.Creature, exclude );
		Vector3 target = aim.Hit ? aim.HitPosition : start + direction * 30f;
		if ( Shootables.Find( aim.Node )?.ShotOwner is not Creature && GoreBall.NearestToLine( start, direction, 30f, mega ? 2f : 1.2f ) is { } assisted ) target = assisted;
		BubbleShot.Launch( GetTree().CurrentScene, from, (target - from).SafeNormal(), CurrentDamage, controller, mega );
		// A puff of little bubbles out of the bell.
		for ( var i = 0; i < (mega ? 14 : 6); i++ )
			WaterBubble.Create( from + direction * 0.1f + Rng.InSphere( 0.08f ), Rng.Float( 0.3f, 0.8f ), i * 0.01f, Rng.Float( 0.3f, 0.6f ) );
		if ( mega )
		{
			Sfx.Play2D( "bubble_trap", 0.7f, 0.7f, bus: Sfx.GunBus );
			juice?.Shake( 0.3f );
			juice?.KickFov( 5f );
		}
	}

	/// <summary> The bubble in the gun's glass globe: gone at a shot, it grows back, and swells while a Mega Bubble is blown. </summary>
	void ShowBubbleLoaded()
	{
		if ( loaded is null || !IsInstanceValid( loaded ) ) loaded = ViewModel.FindChild( "Loaded", true, false ) as Node3D;
		if ( loaded is null ) return;
		var regrow = MathX.Clamp( ((float)TimeSinceShot - 0.08f) / 0.35f, 0f, 1f );
		var size = Ammo > 0 ? regrow * regrow * (3f - 2f * regrow) : 0f;
		loaded.Visible = size > 0.02f;
		loaded.Scale = Vector3.One * MathF.Max( 0.02f, size * (1f + 0.4f * ChargeAmount) );
	}
}

/// <summary> A soap bubble in flight from the Bubble Cannon (a Mega Bubble when charged). </summary>
public partial class BubbleShot : Node3D
{
	public const float Speed = 18f, Life = 2.6f, Radius = 0.38f, HomingTurn = 2.4f, Rise = 0.7f;
	public const float MegaSpeed = 11f, MegaLife = 3.4f, MegaRadius = 0.95f;
	/// <summary> Enemies one Mega Bubble can wrap up as it rolls through. </summary>
	public const int MegaCatches = 4;
	/// <summary> World hits it bounces off before the next one pops it. </summary>
	public const int Bounces = 1;
	/// <summary> Test counters. </summary>
	public static int Fired, Splashed, Bounced, MegaFired;

	public bool Mega { get; private set; }
	Vector3 velocity;
	float damage, age, radius;
	int bounces, catches;
	PlayerController owner;
	MeshInstance3D film;
	TimeSince sinceTrail, sinceBounce = 9f;
	Godot.Collections.Array<Rid> exclude;
	readonly HashSet<Creature> caught = new();

	public static BubbleShot Launch( Node parent, Vector3 from, Vector3 direction, float damage, PlayerController owner, bool mega = false )
	{
		var shot = new BubbleShot { Name = mega ? "Mega Bubble" : "Bubble Shot", velocity = direction * (mega ? MegaSpeed : Speed), damage = damage, owner = owner, Mega = mega };
		shot.radius = mega ? MegaRadius : Radius;
		parent.AddChild( shot );
		shot.GlobalPosition = from;
		shot.film = BubbleFilm.Ball( shot, shot.radius );
		shot.film.Scale = Vector3.One * 0.05f;
		shot.exclude = new Godot.Collections.Array<Rid> { owner.GetRid() };
		Fired++;
		if ( mega ) MegaFired++;
		return shot;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		age += dt;
		var at = GlobalPosition;
		if ( age > (Mega ? MegaLife : Life) || !IsInstanceValid( owner ) ) { Burst( at, null ); return; }

		// Curve gently onto an enemy close to the path (a Mega Bubble keeps its line), and drift up like a bubble does.
		var dir = velocity.SafeNormal();
		if ( !Mega && GoreBall.NearestToLine( at, dir, 20f, 2.2f ) is { } aim )
		{
			var want = (aim - at).SafeNormal();
			dir = dir.Slerp( want, MathF.Min( 1f, HomingTurn * dt ) ).SafeNormal();
			velocity = dir * velocity.Length();
		}
		velocity += Vector3.Up * Rise * dt;
		var step = velocity * dt;

		var tr = Trace.Sphere( owner, radius * (Mega ? 0.8f : 1f), at, at + step, Layers.World | Layers.Creature, exclude );
		if ( tr.Hit && Hit( tr, at, step ) ) return;
		GlobalPosition += step;

		// It blows up out of the bell, stretches along its flight, wobbles, and squashes flat for a moment on a bounce.
		var grow = MathX.Clamp( age / 0.12f, 0f, 1f );
		grow = 1f - (1f - grow) * (1f - grow);
		var t = age * (Mega ? 6f : 9f);
		var squash = MathX.Clamp( 1f - (float)sinceBounce / 0.18f, 0f, 1f );
		var stretch = 1f + 0.12f * (velocity.Length() / Speed) - 0.35f * squash;
		var wobble = new Vector3( 1f + 0.07f * MathF.Sin( t ) + 0.2f * squash, 1f + 0.07f * MathF.Sin( t + 2.1f ) + 0.2f * squash, stretch );
		film.Basis = Shapes.LookAt( dir ).Scaled( wobble * radius * grow );
		if ( sinceTrail > (Mega ? 0.02f : 0.035f) ) { sinceTrail = 0f; WaterBubble.Create( at + Rng.InSphere( radius * 0.6f ), Rng.Float( 0.3f, Mega ? 1.2f : 0.7f ), 0f, Rng.Float( 0.4f, 0.8f ) ); }
	}

	/// <summary> True when the bubble is done (popped or caught something); false to carry on through. </summary>
	bool Hit( TraceResult tr, Vector3 at, Vector3 step )
	{
		var shootable = Shootables.Find( tr.Node );
		if ( shootable?.ShotOwner is Creature { IsDead: false } creature && creature.IsEnemy )
		{
			// Into an enemy already in a bubble: that bubble swells instead (higher and longer).
			if ( creature.Bubbled && BubbleTrap.Of( creature ) is { } trap )
			{
				trap.Inflate( Mega ? 2 : 1 );
				if ( !Mega ) { QueueFree(); return true; }
				Pass( creature );
				return false;
			}
			if ( creature.Pullable )
			{
				// Caught: a little hit, and into a bubble it goes.
				shootable.ReceiveShot( damage * 0.3f, tr.Node as Node3D, velocity.SafeNormal(), tr.HitPosition, at, owner, true );
				owner.GetChildren().OfType<PlayerWeapon>().FirstOrDefault( w => w.Bubbles )?.MarkHit();
				if ( !creature.IsDead ) BubbleTrap.Create( creature, owner, damage, Mega );
				if ( !Mega ) { QueueFree(); return true; }
				// A Mega Bubble rolls on through, until it has wrapped up all it can hold.
				Pass( creature );
				velocity *= 0.85f;
				if ( ++catches >= MegaCatches + Skills.Rank( owner, SkillId.BigGulp ) ) { Burst( tr.HitPosition, null ); return true; }
				return false;
			}
		}
		else if ( shootable is null && tr.Node is not null && bounces < Bounces + Skills.Rank( owner, SkillId.Bouncy ) && sinceBounce > 0.1f )
		{
			// The world: bounce off it once, rubbery.
			bounces++;
			Bounced++;
			sinceBounce = 0f;
			var n = tr.Normal == Vector3.Zero ? -velocity.SafeNormal() : tr.Normal;
			velocity = velocity.Bounce( n ) * 0.75f;
			GlobalPosition = tr.HitPosition + n * (radius + 0.02f);
			Sfx.Play( "bubble_bounce", tr.HitPosition, 0.8f, Rng.Float( 0.9f, 1.1f ) * (Mega ? 0.7f : 1f) );
			return true;
		}
		Burst( tr.HitPosition, shootable );
		return true;
	}

	/// <summary> A Mega Bubble rolls on through this one: its parts are skipped from now on. </summary>
	void Pass( Creature creature )
	{
		if ( !caught.Add( creature ) ) return;
		foreach ( var p in CreaturePart.All ) if ( IsInstanceValid( p ) && p.Creature == creature ) exclude.Add( p.GetRid() );
	}

	/// <summary> Pops in flight: a splash that hurts what it touched and anything near it. </summary>
	void Burst( Vector3 at, IShootable direct )
	{
		Splashed++;
		var scale = Mega ? 3f : 1f;
		if ( direct is not null && !direct.IsDead )
		{
			direct.ReceiveShot( damage * 1.5f * scale, null, velocity.SafeNormal(), at, at - velocity.SafeNormal(), owner, true );
			owner?.GetChildren().OfType<PlayerWeapon>().FirstOrDefault( w => w.Bubbles )?.MarkHit();
		}
		BubbleTrap.Splash( at, Mega ? 3.2f : 1.6f, damage * 0.5f * scale, owner, direct?.ShotOwner, spareBubbled: true );   // not the ones it just caught
		BubbleFilm.PopFx( at, radius * 2f );
		Sfx.Play( Mega ? "bubble_burst" : "bubble_pop", at, Mega ? 1f : 0.7f, Rng.Float( 0.9f, 1.15f ) );
		QueueFree();
	}
}

/// <summary>
/// An enemy caught in a bubble: it stops thinking (`Creature.Bubbled`) and floats up inside the film, struggling. The
/// bubble pops after `HoldTime`, on a hit of `PopOnHit` or more (a bleed tick doesn't), on the player's right click, or
/// in a chain when a bubble near it pops; the pop hurts everything within `PopRadius`, and the enemy drops back to the
/// floor and takes `LandDamage` scaled by how high it fell, shoving what is near where it lands.
/// </summary>
public partial class BubbleTrap : Node3D
{
	public const float RiseHeight = 2.4f, MegaRise = 3.4f, InflateRise = 1.2f, RiseSpeed = 1.6f, HoldTime = 5f, InflateTime = 2f;
	public const float PopRadius = 3.4f, PopScale = 2.2f, ChainBonus = 0.25f, ChainDelay = 0.1f, LandDamage = 45f, LandRadius = 2f, PopOnHit = 10f;
	/// <summary> Test counters. </summary>
	public static int Trapped, Popped, Landed, Inflated, Chained;
	static readonly List<BubbleTrap> live = new();
	public static IReadOnlyList<BubbleTrap> Live => live;

	public Creature Victim { get; private set; }
	public float Risen => risen;
	public float TargetRise => rise;
	PlayerController owner;
	float damage, age, risen, fall, size, rise, hold, dropFrom, chainIn = -1f;
	int chain;
	bool popped, shot;
	Vector3 centreOffset;
	Basis restBasis;
	MeshInstance3D film;
	Vector3 bulge;
	TimeSince sinceStruggle;
	float nextStruggle = 0.4f;

	public static BubbleTrap Of( Creature creature ) => live.FirstOrDefault( t => t.Victim == creature && !t.popped );
	public static int CountFor( PlayerController owner )
	{
		var n = 0;
		foreach ( var t in live ) if ( t.owner == owner && !t.popped ) n++;   // read by the HUD every frame: no LINQ
		return n;
	}

	public static BubbleTrap Create( Creature victim, PlayerController owner, float damage, bool mega = false )
	{
		// Online: a joiner's copy of the host's creature is only bubbled when the host's is (NetBubbles).
		if ( victim.Puppet && !NetCreatures.Applying ) return null;
		var parts = CreaturePart.All.Where( p => IsInstanceValid( p ) && p.Creature == victim ).Select( p => p.GlobalPosition ).ToList();
		var centre = parts.Count > 0 ? parts.Aggregate( Vector3.Zero, ( a, p ) => a + p ) / parts.Count : victim.GlobalPosition + Vector3.Up * 0.5f;
		var reach = parts.Count > 0 ? parts.Max( p => p.DistanceTo( centre ) ) : 0.6f;
		var trap = new BubbleTrap { Name = "Bubble Trap", Victim = victim, owner = owner, damage = damage, size = MathX.Clamp( reach + 0.55f, 0.8f, 2.4f ) };
		trap.rise = (mega ? MegaRise : RiseHeight) * (1f + SkillFx.LiftPer * Skills.Rank( owner, SkillId.HighLift ));
		trap.hold = HoldTime + SkillFx.LastingPer * Skills.Rank( owner, SkillId.LongLasting );
		trap.centreOffset = centre - victim.GlobalPosition;
		trap.restBasis = victim.GlobalBasis;
		victim.GetTree().CurrentScene.AddChild( trap );
		trap.GlobalPosition = centre;
		trap.film = BubbleFilm.Ball( trap, trap.size );
		trap.film.Scale = Vector3.One * 0.1f;
		victim.Bubbled = true;
		if ( victim.Health is { } hp ) hp.Damaged += trap.OnDamaged;
		live.Add( trap );
		Trapped++;
		if ( Net.IsHost && victim.NetId > 0 ) Net.Instance?.SendBubble( victim.NetId, mega, owner?.NetPeer ?? 0, damage );
		BubbleFilm.PopFx( centre, trap.size * 0.6f );
		Sfx.Play( "bubble_trap", centre, 0.9f, Rng.Float( 0.9f, 1.1f ) );
		return trap;
	}

	/// <summary> Another bubble fired into this one: it swells, rises higher and holds longer. </summary>
	public void Inflate( int times = 1 )
	{
		if ( popped || (Victim.Puppet && !NetCreatures.Applying) ) return;
		if ( Net.IsHost && Victim.NetId > 0 ) Net.Instance?.SendBubbleInflate( Victim.NetId, times );
		Inflated++;
		rise += InflateRise * times;
		hold = MathF.Max( hold, age + InflateTime * times );
		size = MathF.Min( size * (1f + 0.12f * times), 3f );
		bulge += Vector3.One * 0.25f;
		Sfx.Play( "bubble_blow", GlobalPosition, 0.8f, 0.7f );
		BubbleFilm.PopFx( GlobalPosition, size * 0.4f );
	}

	/// <summary> A real hit pops it (a bleed tick, a few points a second, doesn't). </summary>
	void OnDamaged( DamageInfo damage )
	{
		if ( damage.Damage >= PopOnHit ) shot = true;
	}

	/// <summary> Pops every bubble this player has an enemy in (the Bubble Cannon's right click). </summary>
	public static void PopAll( PlayerController owner, bool sound = false )
	{
		var any = false;
		foreach ( var trap in live.ToList() )
			if ( trap.owner == owner && !trap.popped ) { trap.Pop(); any = true; }
		if ( sound && !any ) Sfx.Play2D( "dry_fire", 0.5f );
	}

	public override void _ExitTree()
	{
		live.Remove( this );
		if ( Victim is not null && IsInstanceValid( Victim ) )
		{
			Victim.Bubbled = false;
			if ( Victim.Health is { } hp ) hp.Damaged -= OnDamaged;
		}
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( Victim is null || !IsInstanceValid( Victim ) ) { QueueFree(); return; }
		age += dt;
		if ( !popped )
		{
			if ( chainIn >= 0f && (chainIn -= dt) < 0f ) { Pop(); return; }
			if ( Victim.IsDead || shot || age > hold ) { Pop(); return; }
			Float( dt );
			return;
		}
		// Popped: the enemy drops back to where it was caught.
		if ( Victim.IsDead ) { QueueFree(); return; }
		fall += 16f * dt;
		var drop = MathF.Min( fall * dt, risen );
		risen -= drop;
		if ( !Victim.Puppet ) Victim.GlobalPosition += Vector3.Down * drop;   // a joiner's copy drops with the host's
		if ( risen > 0.001f ) return;
		Land();
	}

	void Float( float dt )
	{
		// Up, swaying, kept clear of other bubbles.
		var lift = MathF.Min( RiseSpeed * dt * (1f - risen / MathF.Max( 0.1f, rise ) * 0.7f), rise - risen );
		risen += lift;
		var push = new Vector3( MathF.Sin( age * 1.7f ), 0f, MathF.Cos( age * 1.3f ) ) * 0.12f * dt;
		foreach ( var other in live )
		{
			if ( other == this || other.popped ) continue;
			var away = (GlobalPosition - other.GlobalPosition).Flat();
			var gap = size + other.size - away.Length();
			if ( gap > 0f ) push += away.SafeNormal() * MathF.Min( gap, 1f ) * 2f * dt;
		}
		if ( !Victim.Puppet ) Victim.GlobalPosition += Vector3.Up * lift + push;   // a joiner's copy rises with the host's

		// It struggles: a kick every so often bulges the film and twists the enemy inside.
		if ( sinceStruggle > nextStruggle )
		{
			sinceStruggle = 0f;
			nextStruggle = Rng.Float( 0.35f, 0.8f );
			var kick = Rng.OnSphere();
			bulge += new Vector3( MathF.Abs( kick.X ), MathF.Abs( kick.Y ), MathF.Abs( kick.Z ) ) * 0.18f;
			if ( Rng.Float() < 0.5f ) Sfx.Play( "bubble_pop", GlobalPosition, 0.25f, Rng.Float( 0.4f, 0.6f ) );
		}
		bulge = bulge.Lerp( Vector3.Zero, MathF.Min( 1f, dt * 6f ) );
		var twist = MathF.Sin( age * 7f ) * 0.18f * MathF.Min( 1f, bulge.Length() * 4f + 0.3f );
		if ( !Victim.Puppet ) Victim.GlobalBasis = new Basis( Vector3.Up, twist ) * restBasis;
		GlobalPosition = Victim.GlobalPosition + centreOffset;

		var grow = MathX.Clamp( age / 0.2f, 0f, 1f );
		var t = age * 4f;
		var warn = age > hold - 1f ? 1f + 0.08f * MathF.Sin( age * 40f ) : 1f;   // shivers just before it goes
		var wobble = new Vector3( 1f + 0.05f * MathF.Sin( t ), 1f + 0.05f * MathF.Sin( t + 2f ), 1f + 0.05f * MathF.Sin( t + 4f ) ) + bulge;
		film.Scale = wobble * size * warn * (1f - (1f - grow) * (1f - grow));
	}

	/// <summary> Online, a joiner: the host's bubble round this creature popped. </summary>
	public static void PopNet( Creature victim ) { if ( Of( victim ) is { } trap ) trap.Pop(); }

	void Pop()
	{
		if ( popped ) return;
		popped = true;
		if ( Net.IsHost && Victim.NetId > 0 ) Net.Instance?.SendBubblePop( Victim.NetId );
		Popped++;
		film.Visible = false;
		dropFrom = risen;
		var at = GlobalPosition;
		BubbleFilm.PopFx( at, size );
		BubbleFilm.Shards( at, size );
		Jetpack.SlamBubbles( at, Vector3.Up, 0.6f );
		Sfx.Play( "bubble_burst", at, 1f, Rng.Float( 0.9f, 1.1f ) * (1f + 0.08f * chain) );
		var hurt = damage * PopScale * (1f + (ChainBonus + SkillFx.ChainPer * Skills.Rank( owner, SkillId.ChainReaction )) * chain);
		Splash( at, PopRadius, hurt, owner, null );
		Fizz( at );
		// Bubbles near it go off a moment later, one link further down the chain.
		foreach ( var other in live )
		{
			if ( other == this || other.popped || other.chainIn >= 0f ) continue;
			if ( other.GlobalPosition.DistanceTo( at ) > PopRadius + other.size ) continue;
			other.chain = chain + 1;
			other.chainIn = ChainDelay;
			Chained++;
		}
		Victim.Bubbled = false;
		Victim.GlobalBasis = restBasis;
		if ( risen <= 0.01f ) QueueFree();
	}

	/// <summary> Fizz: a pop shocks the enemies nearest it (one per point). </summary>
	void Fizz( Vector3 at )
	{
		var n = owner is not null && IsInstanceValid( owner ) ? Skills.Rank( owner, SkillId.Fizz ) : 0;
		if ( n <= 0 ) return;
		var near = Creature.Everyone.Where( c => IsInstanceValid( c ) && c.IsEnemy && !c.IsDead && c != Victim && c.GlobalPosition.DistanceTo( at ) < PopRadius * 1.6f )
			.OrderBy( c => c.GlobalPosition.DistanceSquaredTo( at ) ).Take( n ).ToList();
		foreach ( var c in near )
		{
			SkillFx.Fizzes++;
			var to = SkillFx.NearestPart( c, at );
			SkillFx.Zap( at, to );
			PlayerPerks.Hurt( c, SkillFx.FizzDamage( n ), to, at, owner );
		}
		if ( near.Count > 0 ) Sfx.Play( "beam_fire", at, 0.3f, 2.4f );
	}

	void Land()
	{
		Landed++;
		var height = MathF.Max( 0.3f, dropFrom ) / RiseHeight;
		var at = Victim.GlobalPosition;
		var part = CreaturePart.All.Where( p => IsInstanceValid( p ) && p.Creature == Victim ).OrderBy( p => p.GlobalPosition.Y ).FirstOrDefault();
		part?.ReceiveShot( LandDamage * height, part, Vector3.Down, part.GlobalPosition, part.GlobalPosition + Vector3.Up, owner, false );
		Splash( at, LandRadius, LandDamage * 0.4f * height, owner, Victim );
		Jetpack.SlamBubbles( at, Vector3.Up, 0.35f * MathF.Min( 2f, height ) );
		SeaStir.SiltRing( at with { Y = SeaStir.GroundBelow( at ) }, 0.5f + 0.3f * MathF.Min( 2f, height ) );
		SeaStir.Push( at, 4f, 0.6f );
		Sfx.Play( "land", at, 0.8f, 0.8f );
		QueueFree();
	}

	/// <summary>
	/// Hurts every living enemy within `radius` (more near the middle). `skip` is spared (already hit), and with
	/// `spareBubbled` so is any enemy in a bubble.
	/// </summary>
	public static void Splash( Vector3 at, float radius, float damage, PlayerController owner, Node skip, bool spareBubbled = false )
	{
		foreach ( var creature in CreatureRegistry.Alive().ToList() )
		{
			if ( creature == skip || !IsInstanceValid( creature ) ) continue;
			if ( spareBubbled && creature is Creature { Bubbled: true } ) continue;
			CreaturePart nearest = null;
			var best = float.MaxValue;
			foreach ( var p in CreaturePart.All )
			{
				if ( !IsInstanceValid( p ) || p.Creature != creature || p.IsDead ) continue;
				var d = p.GlobalPosition.DistanceTo( at );
				if ( d < best ) { best = d; nearest = p; }
			}
			if ( nearest is null || best > radius ) continue;
			var falloff = 1f - best / radius;
			nearest.ReceiveShot( damage * (0.4f + 0.6f * falloff), nearest, (nearest.GlobalPosition - at).SafeNormal(), nearest.GlobalPosition, at, owner, true );
		}
	}
}

/// <summary> The soap film look shared by bubbles in flight, traps, pop shards and the gun's globe. </summary>
public static partial class BubbleFilm
{
	static ShaderMaterial material;
	static SphereMesh sphere;
	static QuadMesh shard;

	/// <summary> Soap film: rainbow bands sliding over a fresnel rim. `fade` (per instance) fades a piece out. </summary>
	public static ShaderMaterial Material => material ??= new ShaderMaterial
	{
		Shader = new Shader
		{
			Code = @"
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, specular_schlick_ggx;
instance uniform float fade = 1.0;
void vertex() {
	VERTEX += NORMAL * sin(TIME * 5.0 + VERTEX.y * 7.0 + VERTEX.x * 4.0) * 0.025;
}
void fragment() {
	float facing = abs(dot(NORMAL, VIEW));
	float f = pow(1.0 - clamp(facing, 0.0, 1.0), 1.8);
	float swirl = sin(UV.x * 18.0 + UV.y * 9.0 + TIME * 1.3) * 0.15;
	float band = f * 2.5 + TIME * 0.25 + UV.y * 1.5 + swirl;
	vec3 rainbow = 0.5 + 0.5 * cos(6.2831 * (band + vec3(0.0, 0.33, 0.67)));
	ALBEDO = mix(vec3(0.75, 0.95, 1.0), rainbow, 0.75);
	EMISSION = rainbow * f * 0.7;
	ROUGHNESS = 0.04;
	SPECULAR = 0.9;
	ALPHA = clamp(0.06 + f * 0.8, 0.0, 0.85) * fade;
}",
		},
	};

	/// <summary> A bubble of this radius (m) under `parent`, with a glint. </summary>
	public static MeshInstance3D Ball( Node3D parent, float radius )
	{
		sphere ??= new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 };
		var ball = new MeshInstance3D { Name = "Film", Mesh = sphere, MaterialOverride = Material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild( ball );
		ball.Scale = Vector3.One * radius;
		var glint = new MeshInstance3D
		{
			Name = "Glint", Mesh = sphere, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.White, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
			Position = new Vector3( -0.45f, 0.55f, 0.55f ), Scale = new Vector3( 0.09f, 0.14f, 0.09f ),
		};
		ball.AddChild( glint );
		return ball;
	}

	/// <summary> A burst of little bubbles where one popped. </summary>
	public static void PopFx( Vector3 at, float radius )
	{
		var count = (int)MathX.Clamp( 10f + radius * 14f, 10f, 40f );
		for ( var i = 0; i < count; i++ )
		{
			var dir = Rng.OnSphere();
			WaterBubble.Create( at + dir * radius * Rng.Float( 0.7f, 1f ), Rng.Float( 0.4f, 1.3f ), Rng.Float( 0f, 0.04f ), Rng.Float( 0.5f, 1.1f ) );
		}
	}

	/// <summary> A big pop: curls of film flung out, and a ring of film blown outward that fades. </summary>
	public static void Shards( Vector3 at, float radius )
	{
		var tree = Engine.GetMainLoop() as SceneTree;
		if ( tree?.CurrentScene is null ) return;
		shard ??= new QuadMesh { Size = Vector2.One };
		sphere ??= new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 };
		var burst = new PopBurst { Name = "Bubble Pop" };
		tree.CurrentScene.AddChild( burst );
		burst.GlobalPosition = at;
		burst.ring = new MeshInstance3D { Mesh = sphere, MaterialOverride = Material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Scale = Vector3.One * radius };
		burst.AddChild( burst.ring );
		burst.radius = radius;
		var count = (int)MathX.Clamp( 8f + radius * 6f, 8f, 20f );
		for ( var i = 0; i < count; i++ )
		{
			var dir = Rng.OnSphere();
			var piece = new MeshInstance3D { Mesh = shard, MaterialOverride = Material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			burst.AddChild( piece );
			piece.Position = dir * radius;
			burst.pieces.Add( (piece, dir * Rng.Float( 3f, 7f ) * MathF.Sqrt( radius ), Rng.OnSphere(), Rng.Float( 0.12f, 0.3f ) * radius) );
		}
	}

	partial class PopBurst : Node3D
	{
		public MeshInstance3D ring;
		public float radius;
		public readonly List<(MeshInstance3D Node, Vector3 Velocity, Vector3 Spin, float Size)> pieces = new();
		float age;
		const float Life = 0.4f;

		public override void _Process( double delta )
		{
			var dt = (float)delta;
			age += dt;
			var t = age / Life;
			if ( t >= 1f ) { QueueFree(); return; }
			var ease = 1f - (1f - t) * (1f - t);
			ring.Scale = Vector3.One * radius * (1f + 0.9f * ease);
			ring.SetInstanceShaderParameter( "fade", (1f - t) * 0.8f );
			for ( var i = 0; i < pieces.Count; i++ )
			{
				var (node, velocity, spin, size) = pieces[i];
				velocity *= MathF.Exp( -5f * dt );
				pieces[i] = (node, velocity, spin, size);
				node.Position += velocity * dt;
				node.Basis = new Basis( spin, age * 9f ).Scaled( new Vector3( size, size * 0.5f, size ) * (1f - t * 0.6f) );
				node.SetInstanceShaderParameter( "fade", 1f - t );
			}
		}
	}
}

/// <summary>
/// The Bubble Cannon's first person model: the Blaster's rig with this gun on the gun bone (like the Gore Vac). Sea
/// teal and coral pink, a glass globe on top with the next bubble wobbling in it ("Loaded", sized by the gun), and a
/// bell muzzle with a soap wand ring across its mouth. Gun space: inches, +X along the barrel, +Z up, the grip at the origin.
/// </summary>
public static partial class BubbleCannonModel
{
	public static readonly Vector3 MuzzleAt = new( 30f, 0f, 3.2f );
	public static readonly Color Teal = new( 0.15f, 0.75f, 0.8f );
	public static readonly Color Coral = new( 1f, 0.42f, 0.55f );

	static StandardMaterial3D teal, coral, black, metal, glass, yellow;
	static StandardMaterial3D Paint( Color c, float roughness, float metallic = 0f ) => new() { AlbedoColor = c, Roughness = roughness, Metallic = metallic, RimEnabled = true, Rim = 0.3f, RimTint = 0.6f };
	static StandardMaterial3D TealPaint => teal ??= Paint( Teal, 0.35f );
	static StandardMaterial3D CoralPaint => coral ??= Paint( Coral, 0.4f );
	static StandardMaterial3D BlackPaint => black ??= Paint( new Color( 0.05f, 0.06f, 0.08f ), 0.6f );
	static StandardMaterial3D Metal => metal ??= Paint( new Color( 0.7f, 0.72f, 0.76f ), 0.25f, 0.75f );
	static StandardMaterial3D YellowPaint => yellow ??= Paint( new Color( 1f, 0.86f, 0.2f ), 0.4f );
	static StandardMaterial3D Glass => glass ??= new StandardMaterial3D
	{
		AlbedoColor = new Color( 0.8f, 0.97f, 1f, 0.14f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.15f,
		RimEnabled = true, Rim = 0.4f, RimTint = 0.2f,
	};

	static readonly Basis GunToBone = new( new Vector3( 0f, 1f, 0f ), new Vector3( -1f, 0f, 0f ), new Vector3( 0f, 0f, 1f ) );

	public static void Attach( Skeleton3D skeleton )
	{
		if ( skeleton.FindBone( "gun" ) < 0 ) return;
		var onGun = new BoneAttachment3D { Name = "Bubble Cannon Gun Bone", BoneName = "gun" };
		skeleton.AddChild( onGun );
		var body = new Node3D { Name = "Bubble Cannon", Transform = new Transform3D( GunToBone.Scaled( Vector3.One * 0.01f ), Vector3.Zero ) };
		onGun.AddChild( body );
		BuildGun( body );
		onGun.AddChild( new Node3D { Name = "Muzzle", Position = GunToBone * MuzzleAt * 0.01f } );
	}

	static MeshInstance3D Part( Node3D parent, Mesh mesh, Material material, Vector3 at, Basis? turn = null )
	{
		var mi = new MeshInstance3D { Name = "Part", Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild( mi );
		mi.Transform = new Transform3D( turn ?? Basis.Identity, at );
		return mi;
	}

	static MeshInstance3D Box( Node3D parent, Vector3 centre, Vector3 size, Material material, float tipDegrees = 0f ) =>
		Part( parent, new BoxMesh { Size = size }, material, centre, tipDegrees == 0f ? null : new Basis( new Vector3( 0f, 1f, 0f ), MathX.Rad( tipDegrees ) ) );

	static MeshInstance3D AlongX( Node3D parent, Vector3 centre, float radius, float length, Material material, float frontRadius = -1f, int sides = 20 ) =>
		Part( parent, new CylinderMesh { BottomRadius = radius, TopRadius = frontRadius < 0f ? radius : frontRadius, Height = length, RadialSegments = sides, Rings = 1 }, material, centre, new Basis( Vector3.Back, -MathF.PI * 0.5f ) );

	static MeshInstance3D Ball( Node3D parent, Vector3 centre, float radius, Material material ) =>
		Part( parent, new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 24, Rings = 12 }, material, centre );

	static void BuildGun( Node3D gun )
	{
		// Grip, trigger and guard where the Blaster's were.
		Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), BlackPaint, 18f );
		Box( gun, new Vector3( 1.6f, 0f, -1.1f ), new Vector3( 0.5f, 0.5f, 1.6f ), YellowPaint );
		Box( gun, new Vector3( 2.4f, 0f, -2.1f ), new Vector3( 4.2f, 0.6f, 0.5f ), BlackPaint );

		// A rounded teal body with coral bands.
		AlongX( gun, new Vector3( 6f, 0f, 3.2f ), 2.9f, 14f, TealPaint, 2.6f, 24 );
		Ball( gun, new Vector3( -1f, 0f, 3.2f ), 2.9f, TealPaint );
		foreach ( var x in new[] { 2.5f, 9.5f } ) AlongX( gun, new Vector3( x, 0f, 3.2f ), 3.05f, 0.9f, CoralPaint, sides: 24 );
		Box( gun, new Vector3( 5.5f, 0f, 0.4f ), new Vector3( 11f, 2.6f, 1.4f ), BlackPaint );

		// The glass globe on top with the next bubble wobbling inside, on a metal collar.
		var globeAt = new Vector3( 5.5f, 0f, 8.6f );
		Part( gun, new CylinderMesh { TopRadius = 1.6f, BottomRadius = 1.9f, Height = 1.4f, RadialSegments = 20 }, Metal, globeAt + new Vector3( 0f, 0f, -2.8f ), new Basis( Vector3.Right, MathF.PI * 0.5f ) );
		Ball( gun, globeAt, 3.1f, Glass );
		var loaded = new Node3D { Name = "Loaded", Position = globeAt };
		gun.AddChild( loaded );
		var inner = Part( loaded, new SphereMesh { Radius = 2.2f, Height = 4.4f, RadialSegments = 24, Rings = 12 }, BubbleFilm.Material, Vector3.Zero );
		inner.AddChild( new Wobble { Name = "Wobble" } );

		// The barrel: teal into a yellow bell, with the wand ring across the mouth.
		AlongX( gun, new Vector3( 17.5f, 0f, 3.2f ), 1.9f, 9f, TealPaint, 2.1f );
		AlongX( gun, new Vector3( 25.5f, 0f, 3.2f ), 2.1f, 7f, YellowPaint, 4.2f, 28 );
		var ring = new TorusMesh { InnerRadius = 3.6f, OuterRadius = 4.3f, Rings = 28, RingSegments = 8 };
		Part( gun, ring, CoralPaint, new Vector3( 29.3f, 0f, 3.2f ), new Basis( Vector3.Back, -MathF.PI * 0.5f ) );
		Part( gun, new CylinderMesh { TopRadius = 3.6f, BottomRadius = 3.6f, Height = 0.05f, RadialSegments = 28 }, BubbleFilm.Material, new Vector3( 29.3f, 0f, 3.2f ), new Basis( Vector3.Back, -MathF.PI * 0.5f ) );

		// Stock.
		Box( gun, new Vector3( -6.5f, 0f, 2.9f ), new Vector3( 7f, 2.6f, 4.0f ), BlackPaint );
		Box( gun, new Vector3( -10.2f, 0f, 2.8f ), new Vector3( 0.6f, 2.7f, 4.4f ), BlackPaint );
		Box( gun, new Vector3( -8.6f, 0f, 2.9f ), new Vector3( 0.4f, 2.65f, 4.05f ), CoralPaint );
	}

	/// <summary> A little Bubble Cannon (metres, muzzle toward -Z). </summary>
	public static void BuildIcon( Node3D parent )
	{
		var gun = new Node3D { Name = "Bubble Cannon Icon", Basis = new Basis( new Vector3( 0f, 0f, -1f ), new Vector3( -1f, 0f, 0f ), new Vector3( 0f, 1f, 0f ) ).Scaled( Vector3.One * 0.03f ) };
		parent.AddChild( gun );
		BuildGun( gun );
	}

	/// <summary> Makes the bubble in the globe wobble (the gun sizes the "Loaded" node above it). </summary>
	partial class Wobble : Node
	{
		float time;
		public override void _Process( double delta )
		{
			time += (float)delta * 6f;
			GetParent<Node3D>().Scale = new Vector3( 1f + 0.06f * MathF.Sin( time ), 1f + 0.06f * MathF.Sin( time + 2.1f ), 1f + 0.06f * MathF.Sin( time + 4.2f ) );
		}
	}
}
