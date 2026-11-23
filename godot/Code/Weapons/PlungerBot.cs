using Godot;

/// <summary> The Plunger-Bot's numbers (what one skill point does, and the base kit). </summary>
public static class PlungerFx
{
	// The Plunger Launcher: plungers stick; at `PopAt` on one enemy they all pop off at once (SHLORP); right click yanks them back.
	public const float Speed = 42f, Drop = 3f, PopAt = 3, PopDamage = 70f, YankDamage = 18f, YankPull = 2.2f, StickTime = 8f;
	public const float SuctionPer = 0.1f, RapidPer = 0.08f, ClingyPer = 0.05f, ShlorpPer = 0.2f, TugPer = 0.4f, TugDamagePer = 8f;
	public static int NightmareThrows( int n ) => 1 + n;
	// The Flush Bomb: a toilet that lands, flushes (a whirlpool pulling enemies round and in), then geysers them up.
	public const float FlushTime = 2.4f, FlushRadius = 5f, PullSpeed = 4f, SwirlSpeed = 5f, SwirlDps = 12f, GeyserRadius = 2.6f, GeyserDamage = 60f;
	public const float StrongFlushPer = 0.15f, LongFlushPer = 0.5f, WiderBowlPer = 0.12f, GeyserPer = 0.2f, SwirlyPer = 6f;
	public static float CloggedDamage( int n ) => 100f + 60f * n;
	public static float CloggedDps( int n ) => 8f * n;
	// The plunger slingshot (mouse 4).
	public const float SlingCost = 35f, SlingSpeed = 16f, SlingLift = 6f, FlopRadius = 3f, FlopDamage = 40f;
	public const float LongBungeePer = 0.15f, ElasticPer = 0.1f, BellyFlopPer = 20f, BounceBackPer = 4f;
	public static float CannonballRadius( int n ) => 4f + 0.5f * n;
	public static float CannonballDamage( int n ) => 70f + 45f * n;

	public static readonly Color Rubber = new( 0.78f, 0.1f, 0.12f ), Wood = new( 0.66f, 0.45f, 0.24f ), Porcelain = new( 0.96f, 0.96f, 0.93f ), Water = new( 0.35f, 0.75f, 1f );

	/// <summary> Tests: how often each thing happened. </summary>
	public static int Plungers, Stuck, Pops, Yanks, Nightmares, Toilets, Flushes, Geysers, Clogs, Slings, Flops, Cannonballs;
}

/// <summary>
/// The Plunger-Bot's guns. The Plunger Launcher (slot 1) fires rubber plungers that stick to what they hit with a
/// THWOP (`PlungerShot`); stuck to an enemy they wobble there (`StuckPlunger`), and once `PopAt` are on one they all
/// come off at once in a SHLORP that tears into it. Right click yanks every stuck plunger back on its bungee,
/// dragging each enemy a little toward you. The Flush Bomb (slot 2) lobs a toilet (`FlushBomb`) that flushes: a
/// whirlpool pulls enemies round and in, then a geyser throws them up. Right click flushes it now.
/// </summary>
public partial class PlayerWeapon
{
	public bool Plunger, Toilet;
	FlushBomb bomb;
	public FlushBomb ActiveBomb => bomb is not null && IsInstanceValid( bomb ) ? bomb : null;

	public static PlayerWeapon PlungerLauncher( int slot = 1 ) => new()
	{
		Name = "PlungerLauncher", DisplayName = "Plunger Launcher", Slot = slot, ModelName = "plunger", Plunger = true,
		Damage = 30f, FireRate = 2.6f, Range = 40f, SpreadDegrees = 0.5f, Recoil = 1.2f,
		MagazineSize = 8, StartingReserve = 96, ReloadTime = 2f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.12f, FovKickPerShot = 1f, ShootSound = "plunger_pop",
	};

	public static PlayerWeapon FlushBombs( int slot = 2 ) => new()
	{
		Name = "FlushBomb", DisplayName = "Flush Bomb", Slot = slot, ModelName = "toilet", Toilet = true,
		Damage = PlungerFx.GeyserDamage, FireRate = 1f, Range = 30f, SpreadDegrees = 0f, Recoil = 1f,
		MagazineSize = 2, StartingReserve = 18, ReloadTime = 2.2f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.15f, FovKickPerShot = 1f, ShootSound = "ink_throw",
	};

	void FirePlunger( Vector3 start, Vector3 forward, float spread )
	{
		PlungerFx.Plungers++;
		var direction = (forward + Rng.InSphere( 1f ) * spread).Normalized();
		var from = controller.ThirdPerson ? controller.EyeTransform.Origin + direction * 0.6f : start + direction * 0.4f - controller.Camera.GlobalBasis.Y * 0.12f;
		PlungerShot.Fire( this, controller, from, direction * PlungerFx.Speed, CurrentDamage * (1f + PlungerFx.SuctionPer * Skill( SkillId.ExtraSuction )) );
	}

	void ThrowToilet( Vector3 start, Vector3 forward )
	{
		ActiveBomb?.Flush();
		var from = controller.ThirdPerson ? controller.EyeTransform.Origin + forward * 0.6f : start + forward * 0.6f - controller.Camera.GlobalBasis.Y * 0.2f;
		bomb = FlushBomb.Throw( this, controller, from, forward );
	}

	/// <summary> Right click with the launcher: every plunger stuck in an enemy is yanked back on its bungee. </summary>
	public void Unclog()
	{
		var mine = StuckPlunger.All.Where( p => p.Owner == controller && IsInstanceValid( p ) ).ToList();
		if ( mine.Count == 0 ) return;
		var tug = Skill( SkillId.TugOfWar );
		var root = GetTree().CurrentScene;
		foreach ( var group in mine.GroupBy( p => p.Victim ) )
		{
			var c = group.Key;
			if ( c is null || !IsInstanceValid( c ) || c.IsDead ) { foreach ( var p in group ) p.Drop(); continue; }
			var count = group.Count();
			PlungerFx.Yanks += count;
			var at = SkillFx.NearestPart( c, controller.GlobalPosition );
			var cord = new Node3D { Name = "Bungee" };
			root.AddChild( cord );
			Sculpt.Limb( cord, LinePoint(), at, 0.015f, 0.015f, GunKit.Paint( new Color( 0.9f, 0.85f, 0.2f ), 0.6f ), "Cord" );
			Fx.Expire( cord, 0.15f );
			SkillFx.HitPart( c, (PlungerFx.YankDamage + PlungerFx.TugDamagePer * tug) * count * UpgradeScale, controller.GlobalPosition, controller );
			if ( !c.IsDead ) { SkillFx.Shove( c, (controller.GlobalPosition - c.GlobalPosition).Flat().SafeNormal() * (PlungerFx.YankPull + PlungerFx.TugPer * tug) * MathF.Min( 3, count ) ); c.Stun( 0.3f ); }
			else MarkKill();
			foreach ( var p in group ) p.Drop();
		}
		Sfx.Play2D( "plunger_shlorp", 0.8f, 1.2f, Sfx.GunBus );
		Sfx.Play2D( "boing", 0.6f, 1.1f, Sfx.GunBus );
		juice?.Shake( 0.25f );
		MarkHit();
	}
}

/// <summary> A plunger in flight: a little drop, then it sticks to whatever it meets. </summary>
public partial class PlungerShot : Node3D
{
	PlayerController owner;
	PlayerWeapon weapon;
	Vector3 velocity;
	float damage, travelled;
	Node3D look;

	public static void Fire( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 velocity, float damage )
	{
		var s = new PlungerShot { Name = "Plunger", weapon = weapon, owner = owner, velocity = velocity, damage = damage };
		owner.GetTree().CurrentScene.AddChild( s );
		s.GlobalPosition = from;
		s.look = PlungerModels.Plunger( s );
	}

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		if ( !IsInstanceValid( owner ) ) { QueueFree(); return; }
		velocity += Vector3.Down * PlungerFx.Drop * dt;
		var at = GlobalPosition;
		var step = velocity * dt;
		look.GlobalBasis = Shapes.LookAt( velocity );
		var tr = Trace.Ray( owner, at, at + step, Layers.World | Layers.Creature, new Godot.Collections.Array<Rid> { owner.GetRid() } );
		if ( tr.Hit ) { Land( tr ); return; }
		GlobalPosition = at + step;
		travelled += step.Length();
		if ( travelled > 45f ) QueueFree();
	}

	void Land( TraceResult tr )
	{
		Sfx.Play( "plunger_pop", tr.HitPosition, 0.9f, Rng.Float( 0.9f, 1.15f ) );
		var shootable = Shootables.Find( tr.Node );
		if ( shootable is GlowBulb bulb ) { bulb.ReceiveShot( damage, tr.Node, velocity.Normalized(), tr.HitPosition, owner.GlobalPosition, owner, true ); QueueFree(); return; }
		if ( SkillFx.EnemyOf( tr.Node ) is { } c && shootable is not null && !shootable.IsDead )
		{
			shootable.ReceiveShot( damage, tr.Node, velocity.Normalized(), tr.HitPosition, owner.GlobalPosition, owner, true );
			if ( !c.IsDead ) { weapon?.MarkHit(); StuckPlunger.Stick( c, tr.Node, tr.HitPosition, velocity.Normalized(), owner, weapon ); }
			else weapon?.MarkKill();
			QueueFree();
			return;
		}
		// Stuck in the sand or a wall for a moment (a funny wobble), then gone.
		var stuck = new Node3D { Name = "Stuck Plunger" };
		GetTree().CurrentScene.AddChild( stuck );
		stuck.GlobalPosition = tr.HitPosition;
		var model = PlungerModels.Plunger( stuck );
		model.GlobalBasis = Shapes.LookAt( -tr.Normal );
		Fx.Expire( stuck, 3f );
		QueueFree();
	}
}

/// <summary>
/// A plunger stuck on an enemy: it rides the part it hit and wobbles. Clingy slows the enemy for each one. When
/// `PopAt` are on one enemy they all pop off at once (SHLORP: damage, gore, the plungers flying off), and Plumber's
/// Nightmare throws them on at the nearest other enemies.
/// </summary>
public partial class StuckPlunger : Node3D
{
	public static readonly List<StuckPlunger> All = new();
	Creature victim;
	Node3D part;
	Vector3 local, lastAt;
	Basis localTurn;
	PlayerController owner;
	PlayerWeapon weapon;
	float age;
	bool hasLast;
	Node3D model;

	public Creature Victim => victim;
	public PlayerController Owner => owner;
	public static int On( Creature c ) => All.Count( p => p.victim == c && IsInstanceValid( p ) );

	public static void Stick( Creature c, Node hitNode, Vector3 at, Vector3 direction, PlayerController owner, PlayerWeapon weapon )
	{
		PlungerFx.Stuck++;
		var p = new StuckPlunger { Name = "Stuck Plunger", victim = c, owner = owner, weapon = weapon };
		p.part = hitNode as Node3D ?? c;
		owner.GetTree().CurrentScene.AddChild( p );
		p.model = PlungerModels.Plunger( p );
		var inv = p.part.GlobalTransform.AffineInverse();
		p.local = inv * at;
		p.localTurn = p.part.GlobalBasis.Inverse() * Shapes.LookAt( direction );
		p.GlobalPosition = at;
		All.Add( p );
		if ( On( c ) >= PlungerFx.PopAt ) Pop( c, owner, weapon );
	}

	public override void _ExitTree() => All.Remove( this );

	public void Drop()
	{
		if ( !IsInstanceValid( this ) || IsQueuedForDeletion() ) return;
		All.Remove( this );
		// It flies off with a wobble and fades.
		var tween = CreateTween();
		tween.TweenProperty( this, "global_position", GlobalPosition + Rng.OnSphere() * 0.6f + Vector3.Up * 0.8f, 0.35f );
		tween.Parallel().TweenProperty( this, "scale", Vector3.One * 0.05f, 0.35f );
		tween.TweenCallback( Callable.From( QueueFree ) );
	}

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		if ( !All.Contains( this ) ) return;
		age += dt;
		if ( victim is null || !IsInstanceValid( victim ) || victim.IsDead || !IsInstanceValid( part ) || age > PlungerFx.StickTime ) { Drop(); return; }
		GlobalTransform = new Transform3D( part.GlobalBasis * localTurn, part.GlobalTransform * local );
		var wobble = MathF.Sin( age * 18f ) * MathF.Exp( -age * 2f );
		model.Scale = new Vector3( 1f + 0.15f * wobble, 1f + 0.15f * wobble, 1f - 0.2f * wobble );
		// Clingy: a plunger holds back part of each step it takes (bosses don't slow).
		var clingy = Skills.Rank( owner, SkillId.Clingy );
		var now = victim.GlobalPosition;
		if ( clingy > 0 && hasLast && victim.Pullable )
		{
			var step = now - lastAt;
			if ( step.Length() < 2f ) victim.Pull( -step * MathF.Min( 0.5f, PlungerFx.ClingyPer * clingy ) );
		}
		lastAt = victim.GlobalPosition;
		hasLast = true;
	}

	/// <summary> SHLORP: enough plungers on one enemy all pop off at once. </summary>
	static void Pop( Creature c, PlayerController owner, PlayerWeapon weapon )
	{
		PlungerFx.Pops++;
		var on = All.Where( p => p.victim == c ).ToList();
		var at = SkillFx.NearestPart( c, c.GlobalPosition + Vector3.Up * 0.6f );
		var scale = weapon is not null && IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f;
		var damage = PlungerFx.PopDamage * scale * (1f + PlungerFx.ShlorpPer * Skills.Rank( owner, SkillId.Shlorp ));
		foreach ( var p in on ) p.Drop();
		SkillFx.HitPart( c, damage, at + Vector3.Up, owner );
		Sfx.Play( "plunger_shlorp", at, 1f, Rng.Float( 0.9f, 1.05f ) );
		for ( var i = 0; i < 14; i++ ) GooDroplet.Spawn( at, (Rng.OnSphere() + Vector3.Up).SafeNormal() * Rng.Float( 2f, 6f ), new Color( 0.7f, 0.12f, 0.14f ), Rng.Float( 1.2f, 2.4f ) );
		owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.2f );
		if ( c.IsDead ) weapon?.MarkKill(); else c.Stun( 0.4f );
		// Plumber's Nightmare: the popped plungers fly on at the nearest enemies.
		var n = Skills.Rank( owner, SkillId.PlumbersNightmare );
		if ( n <= 0 || weapon is null || !IsInstanceValid( weapon ) ) return;
		var next = Creature.Everyone.Where( o => o != c && IsInstanceValid( o ) && o.IsEnemy && !o.IsDead && o.GlobalPosition.DistanceTo( at ) < 9f )
			.OrderBy( o => o.GlobalPosition.DistanceTo( at ) ).Take( PlungerFx.NightmareThrows( n ) ).ToList();
		foreach ( var o in next )
		{
			PlungerFx.Nightmares++;
			var to = SkillFx.NearestPart( o, at );
			PlungerShot.Fire( weapon, owner, at + (to - at).SafeNormal() * 0.6f, (to - at).SafeNormal() * PlungerFx.Speed, weapon.CurrentDamage );
		}
	}
}

/// <summary>
/// The Flush Bomb: a little toilet. Thrown in an arc it lands upright, then flushes for `FlushTime`: enemies within
/// `FlushRadius` are swept round and in (a whirlpool of blue water and bubbles), taking a little all the while; then a
/// geyser throws the ones in the middle up into the air for `GeyserDamage`. Clogged ends it in a sewage blast and a
/// toxic puddle instead.
/// </summary>
public partial class FlushBomb : Node3D
{
	PlayerController owner;
	PlayerWeapon weapon;
	Vector3 velocity;
	bool landed, flushed;
	float age, flight, life, swirlTick;
	Node3D toilet, lid, vortex;

	public bool Landed => landed;
	int Skill( SkillId id ) => Skills.Rank( owner, id );
	float Radius => PlungerFx.FlushRadius * (1f + PlungerFx.WiderBowlPer * Skill( SkillId.WiderBowl ));

	public static FlushBomb Throw( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 forward )
	{
		PlungerFx.Toilets++;
		var b = new FlushBomb { Name = "Flush Bomb", weapon = weapon, owner = owner };
		b.velocity = forward * 14f + Vector3.Up * 4f;
		b.life = PlungerFx.FlushTime + PlungerFx.LongFlushPer * b.Skill( SkillId.LongFlush );
		owner.GetTree().CurrentScene.AddChild( b );
		b.GlobalPosition = from;
		b.toilet = new Node3D { Name = "Toilet", Scale = Vector3.One * 1.3f };
		b.AddChild( b.toilet );
		b.lid = PlungerModels.Toilet( b.toilet );
		return b;
	}

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		if ( flushed ) return;
		if ( !IsInstanceValid( owner ) ) { QueueFree(); return; }
		if ( !landed ) { Fly( dt ); return; }
		age += dt;
		// The lid flaps while it gurgles; the whirlpool turns.
		lid.Rotation = new Vector3( -MathX.Rad( 60f + 30f * MathF.Sin( age * 14f ) ), 0f, 0f );
		vortex.Rotation = new Vector3( 0f, -age * 9f, 0f );
		vortex.Scale = new Vector3( Radius, 1f + 0.2f * MathF.Sin( age * 5f ), Radius ) * MathF.Min( 1f, age * 3f );
		Swirl( dt );
		if ( age >= life ) Flush();
	}

	void Fly( float dt )
	{
		flight += dt;
		velocity += Vector3.Down * 9f * dt;
		var at = GlobalPosition;
		var step = velocity * dt;
		var tr = Trace.Sphere( owner, 0.2f, at, at + step, Layers.World | Layers.Creature, new Godot.Collections.Array<Rid> { owner.GetRid() } );
		if ( tr.Hit || flight > 3f ) { Land( tr.Hit ? tr.HitPosition : at ); return; }
		GlobalPosition = at + step;
		toilet.Rotation += new Vector3( 7f * dt, 3f * dt, 0f );
	}

	void Land( Vector3 at )
	{
		var floor = Trace.Ray( owner, at + Vector3.Up * 0.3f, at + Vector3.Down * 30f, Layers.World, new Godot.Collections.Array<Rid> { owner.GetRid() } );
		GlobalPosition = floor.Hit ? floor.HitPosition : at;
		toilet.Rotation = new Vector3( 0f, Rng.Float( 0f, MathF.Tau ), 0f );
		landed = true;
		Sfx.Play( "land", GlobalPosition, 0.6f, 0.8f );
		Sfx.Play( "flush", GlobalPosition, 0.9f, 1f );
		GpuFx.SandPuff( GlobalPosition + Vector3.Up * 0.1f, Vector3.Up * 0.6f, new Color( 0.8f, 0.75f, 0.6f, 0.7f ), 10f, 0.8f );
		vortex = new Node3D { Name = "Swirl", Position = Vector3.Up * 0.05f };
		AddChild( vortex );
		var water = Sculpt.Glow( PlungerFx.Water with { A = 0.55f }, 1.2f );
		water.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		for ( var arm = 0; arm < 3; arm++ )
			for ( var i = 0; i < 10; i++ )
			{
				var a = arm * MathF.Tau / 3f + i * 0.45f;
				var r = 1f - i / 10f;
				Sculpt.Blob( vortex, new Vector3( MathF.Cos( a ) * r, 0.02f + 0.03f * (1f - r), MathF.Sin( a ) * r ), new Vector3( 0.22f, 0.05f, 0.12f ) * (0.6f + r), water, new Basis( Vector3.Up, -a ), "Swirl Arm" );
			}
	}

	void Swirl( float dt )
	{
		var centre = GlobalPosition;
		var radius = Radius;
		var pull = 1f + PlungerFx.StrongFlushPer * Skill( SkillId.StrongFlush );
		var hurt = (swirlTick -= dt) <= 0f;
		if ( hurt ) swirlTick = 0.25f;
		var dps = (PlungerFx.SwirlDps + PlungerFx.SwirlyPer * Skill( SkillId.Swirly )) * (weapon is not null && IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f);
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var to = (centre - c.GlobalPosition).Flat();
			var d = to.Length();
			if ( d > radius ) continue;
			if ( c.Pullable && d > 0.3f )
			{
				var inward = to / d;
				var round = inward.Cross( Vector3.Up );
				c.Pull( (inward * PlungerFx.PullSpeed * pull + round * PlungerFx.SwirlSpeed * pull * MathX.Clamp( d, 0.5f, 2f ) / 2f) * dt );
				c.Stun( 0.1f );
			}
			if ( hurt ) PlayerPerks.Hurt( c, dps * 0.25f, SkillFx.NearestPart( c, centre ), centre, owner );
		}
		if ( WaterFeel.IsOn() && Rng.Float() < 0.6f ) WaterBubble.Create( centre + Rng.InSphere( radius * 0.5f ).Flat(), Rng.Float( 0.3f, 0.6f ), 0f, Rng.Float( 0.4f, 0.9f ) );
	}

	/// <summary> The flush ends (or right click): a geyser out of the bowl, or with Clogged a sewage blast. </summary>
	public void Flush()
	{
		if ( flushed ) return;
		flushed = true;
		PlungerFx.Flushes++;
		var centre = GlobalPosition;
		var scale = weapon is not null && IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f;
		var clog = Skill( SkillId.Clogged );
		if ( landed && clog > 0 )
		{
			PlungerFx.Clogs++;
			var reach = Radius;
			foreach ( var c in Creature.Everyone.ToList() )
			{
				if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
				var d = c.GlobalPosition.DistanceTo( centre );
				if ( d > reach ) continue;
				SkillFx.HitPart( c, PlungerFx.CloggedDamage( clog ) * scale * (1f - 0.4f * d / reach), centre, owner );
				SkillFx.Shove( c, (c.GlobalPosition - centre).Flat().SafeNormal() * 1.8f + Vector3.Up * 0.5f );
			}
			for ( var i = 0; i < 8; i++ ) SkillFx.DropToxic( owner, centre + Rng.InSphere( 2f ).Flat(), PlungerFx.CloggedDps( clog ) * scale, new Color( 0.45f, 0.4f, 0.15f ) );
			Shockwave.Spawn( GetTree().CurrentScene, centre + Vector3.Up * 0.4f, reach, new Color( 0.5f, 0.42f, 0.18f ) );
			Sfx.Play( "missile_boom", centre, 0.8f, 0.9f );
			for ( var i = 0; i < 30; i++ ) GooDroplet.Spawn( centre + Vector3.Up * 0.5f, (Rng.OnSphere() + Vector3.Up).SafeNormal() * Rng.Float( 3f, 8f ), new Color( 0.42f, 0.32f, 0.12f ), Rng.Float( 1.5f, 3f ) );
		}
		else if ( landed )
		{
			PlungerFx.Geysers++;
			var damage = PlungerFx.GeyserDamage * scale * (1f + PlungerFx.GeyserPer * Skill( SkillId.Geyser ));
			foreach ( var c in Creature.Everyone.ToList() )
			{
				if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
				if ( (c.GlobalPosition - centre).Flat().Length() > PlungerFx.GeyserRadius ) continue;
				SkillFx.HitPart( c, damage, centre, owner );
				SkillFx.Shove( c, Vector3.Up * 2.2f + Rng.InSphere( 0.6f ).Flat() );
				if ( !c.IsDead ) c.Stun( 0.8f );
			}
			for ( var i = 0; i < 40; i++ ) GooDroplet.Spawn( centre + Vector3.Up * 0.3f, (Vector3.Up * 3f + Rng.InSphere( 0.7f )).SafeNormal() * Rng.Float( 6f, 12f ), PlungerFx.Water, Rng.Float( 1.2f, 2.4f ) );
			Sfx.Play( "splash", centre, 1f, 1.1f );
			Sfx.Play( "boing", centre, 0.6f, 0.7f );
		}
		owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.35f );
		GibKit.Burst( PlungerModels.Gibs, centre + Vector3.Up * 0.4f, Vector3.Up, 0.08f, 8, 1.2f );
		QueueFree();
	}
}

/// <summary> The Plunger-Bot's models: a plunger, a toilet, and its two guns (on the Blaster's rig). </summary>
public static class PlungerModels
{
	public static readonly Vector3 PlungerMuzzle = new( 30f, 0f, 3.4f ), ToiletMuzzle = new( 22f, 0f, 5f );
	static GibRecipe gibs;
	/// <summary> A smashed toilet: porcelain shards with a pale inside, sinking like stone. </summary>
	public static GibRecipe Gibs => gibs ??= new GibRecipe
	{
		Kind = GibKind.Rock, Goo = PlungerFx.Water,
		Outer = GibKit.Paint( PlungerFx.Porcelain, 0.2f ), Inner = GibKit.Paint( new Color( 0.85f, 0.83f, 0.78f ), 0.8f ), Layer = GibKit.Paint( PlungerFx.Porcelain, 0.3f ),
		Bone = GibKit.Paint( PlungerFx.Porcelain, 0.3f ), Sinew = GibKit.Paint( PlungerFx.Water, 0.3f ),
	};

	/// <summary> A plunger along -Z (the cup at the front): a red rubber cup on a wooden handle. </summary>
	public static Node3D Plunger( Node3D parent, float s = 1f )
	{
		var n = new Node3D { Name = "Plunger Look" };
		parent.AddChild( n );
		var rubber = GunKit.Paint( PlungerFx.Rubber, 0.35f );
		var wood = GunKit.Paint( PlungerFx.Wood, 0.7f );
		GunKit.Rod( n, new Vector3( 0f, 0f, 0.5f * s ), new Vector3( 0f, 0f, -0.05f * s ), 0.02f * s, wood );
		Sculpt.Cone( n, new Vector3( 0f, 0f, -0.15f * s ), new Vector3( 0f, 0f, 0.02f * s ), 0.11f * s, rubber, 14, "Cup" );
		Sculpt.Blob( n, new Vector3( 0f, 0f, -0.14f * s ), new Vector3( 0.23f, 0.23f, 0.05f ) * s, rubber, null, "Rim" );
		Sculpt.Blob( n, new Vector3( 0f, 0f, 0.5f * s ), Vector3.One * 0.05f * s, wood, null, "Knob" );
		return n;
	}

	/// <summary> A little white toilet, base on the ground; returns the lid (hinged at the back). </summary>
	public static Node3D Toilet( Node3D parent )
	{
		var white = GunKit.Paint( PlungerFx.Porcelain, 0.15f );
		var seat = GunKit.Paint( new Color( 0.9f, 0.88f, 0.84f ), 0.4f );
		var chrome = GunKit.Paint( new Color( 0.85f, 0.87f, 0.9f ), 0.15f, 0.9f );
		var water = Sculpt.Glow( PlungerFx.Water, 1.5f );
		Sculpt.Cone( parent, new Vector3( 0f, 0f, 0f ), new Vector3( 0f, 0.2f, 0f ), 0.12f, white, 14, "Foot" );
		Sculpt.Blob( parent, new Vector3( 0f, 0.24f, 0.02f ), new Vector3( 0.34f, 0.18f, 0.42f ), white, null, "Bowl" );
		Sculpt.Blob( parent, new Vector3( 0f, 0.31f, 0.02f ), new Vector3( 0.26f, 0.03f, 0.32f ), water, null, "Water" );
		GunKit.Box( parent, new Vector3( 0f, 0.42f, 0.22f ), new Vector3( 0.34f, 0.3f, 0.12f ), white );
		GunKit.Box( parent, new Vector3( 0.12f, 0.53f, 0.2f ), new Vector3( 0.06f, 0.02f, 0.04f ), chrome );
		var lid = new Node3D { Name = "Lid", Position = new Vector3( 0f, 0.33f, 0.18f ) };
		parent.AddChild( lid );
		Sculpt.Blob( lid, new Vector3( 0f, 0f, -0.18f ), new Vector3( 0.32f, 0.03f, 0.38f ), seat, null, "Lid" );
		return lid;
	}

	public static void PlungerGun( Node3D gun )
	{
		var tile = GunKit.Paint( PlungerFx.Porcelain, 0.2f );
		var rubber = GunKit.Paint( PlungerFx.Rubber, 0.35f );
		var wood = GunKit.Paint( PlungerFx.Wood, 0.7f );
		var chrome = GunKit.Paint( new Color( 0.85f, 0.87f, 0.9f ), 0.15f, 0.9f );
		var cord = GunKit.Paint( new Color( 0.9f, 0.85f, 0.2f ), 0.6f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), wood, 18f );
		// A chrome pipe barrel off a white porcelain body, a coil of yellow bungee, and a plunger sat in the mouth.
		GunKit.Box( gun, new Vector3( 4f, 0f, 2.6f ), new Vector3( 9f, 3.4f, 3.4f ), tile );
		GunKit.AlongX( gun, new Vector3( 15f, 0f, 3.4f ), 1.3f, 14f, chrome, 1.3f, 16 );
		GunKit.AlongX( gun, new Vector3( 8.6f, 0f, 3.4f ), 1.6f, 1.4f, chrome, -1f, 16 );
		GunKit.AlongX( gun, new Vector3( 4f, 0f, -0.6f ), 1.5f, 3f, cord, -1f, 14 );
		var loaded = new Node3D { Name = "Loaded" };
		gun.AddChild( loaded );
		GunKit.Rod( loaded, new Vector3( 14f, 0f, 3.4f ), new Vector3( 24f, 0f, 3.4f ), 0.45f, wood );
		GunKit.Spike( loaded, new Vector3( 28f, 0f, 3.4f ), new Vector3( 23.5f, 0f, 3.4f ), 2.6f, rubber, 14 );
		GunKit.AlongX( gun, new Vector3( -3.4f, 0f, 2.5f ), 1.1f, 3.8f, wood, 1.3f, 12 );
	}

	public static void ToiletLauncher( Node3D gun )
	{
		var tile = GunKit.Paint( PlungerFx.Porcelain, 0.2f );
		var chrome = GunKit.Paint( new Color( 0.85f, 0.87f, 0.9f ), 0.15f, 0.9f );
		var wood = GunKit.Paint( PlungerFx.Wood, 0.7f );
		var water = GunKit.Paint( PlungerFx.Water, 0.2f, 0f, 1.5f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), wood, 18f );
		// A porcelain cistern on the gun with a chrome flush handle, and the next little toilet on top.
		GunKit.Box( gun, new Vector3( 6f, 0f, 3f ), new Vector3( 12f, 5f, 4.4f ), tile );
		GunKit.Box( gun, new Vector3( 6f, 0f, 5.4f ), new Vector3( 12.6f, 5.4f, 0.6f ), tile );
		GunKit.Rod( gun, new Vector3( 4f, 2.6f, 4.6f ), new Vector3( 4f, 4.2f, 4.6f ), 0.3f, chrome );
		GunKit.Box( gun, new Vector3( 5f, 4.2f, 4.6f ), new Vector3( 2.4f, 0.4f, 0.6f ), chrome );
		var loaded = new Node3D { Name = "Loaded" };
		gun.AddChild( loaded );
		GunKit.Ball( loaded, new Vector3( 17f, 0f, 6.2f ), 2.4f, tile );
		GunKit.Ball( loaded, new Vector3( 17f, 0f, 7.4f ), 1.6f, water );
		GunKit.Box( loaded, new Vector3( 14f, 0f, 7.6f ), new Vector3( 1.4f, 3.4f, 3f ), tile );
		GunKit.AlongX( gun, new Vector3( -3.4f, 0f, 2.5f ), 1.1f, 3.8f, wood, 1.3f, 12 );
	}
}
