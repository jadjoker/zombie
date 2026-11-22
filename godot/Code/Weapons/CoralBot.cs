using Godot;

/// <summary> The Coral-Bot's numbers (what one skill point does, and the base kit). </summary>
public static class CoralFx
{
	// The Spine Gun: each spine that lands grows coral on the enemy (a stack); coral slows it, and at `ShatterAt` it
	// shatters for `ShatterDamage` (half to enemies close by).
	public const float ShatterDamage = 55f, ShatterReach = 2f, SlowPerStack = 0.03f, StackFade = 3f;
	public static int ShatterAt( int n ) => 12 - n;
	public static float MaxSlow( int n ) => 0.3f + 0.06f * n;
	public const float SharpPer = 0.08f, BrittlePer = 0.2f, ToxicPer = 6f, ToxicTime = 3f, RicochetPer = 0.1f, RicochetReach = 6f;
	public static float BleachBelow( int n ) => 0.15f + 0.1f * n;
	// Reef Seeds: left click plants a polyp turret, right click grows a wall (a seed each).
	public const float PlantRange = 16f, TurretLife = 18f, TurretRange = 14f, TurretRate = 2.5f, TurretDamage = 14f;
	public const int MaxTurrets = 2, MaxWalls = 2;
	public const float WallLife = 12f, WallWidth = 3.2f, WallHeight = 1.9f, WallThick = 0.5f;
	public const float ToughPer = 3f, QuickSeedPer = 0.12f, BarragePer = 0.15f, HealingPer = 1.5f, HealingReach = 4f;
	public const float SpikedPer = 12f, AnemonePer = 0.15f, LinkReach = 10f;
	public static float SeedReloadScale( int n ) => MathF.Max( 0.4f, 1f - QuickSeedPer * n );
	public static float VeinDps( int n ) => 15f * n;
	public static float LinkRate( int n ) => 1f + 0.2f * n;
	// Burrow (mouse 4).
	/// <summary> Seconds after an eruption before the next burrow (it can't be chained into staying untouchable). </summary>
	public const float BurrowCooldown = 3f;
	public const float BurrowTime = 1.2f, BurrowCost = 50f, BurrowBoost = 3f, EruptDamage = 60f, EruptRadius = 3.2f, EyeDrop = 1.15f;
	public const float DeepDigPer = 0.15f, UpheavalPer = 0.2f, SandstormPer = 0.1f, CalcifyAfter = 1f, CalcifyPer = 0.06f, RegrowthPer = 6f;
	public static float BarrierTime( int n ) => 3f + 2f * n;
	public static float BarrierDamage( int n ) => 80f + 40f * n;
	public const float BarrierRadius = 5f;

	/// <summary> Tests: how often each thing happened. </summary>
	public static int Stacks, Shatters, Ricochets, Bleached, Turrets, TurretShots, Walls, VeinHits, Healed, Burrows, Eruptions, Barriers;

	public static readonly Color Pink = new( 1f, 0.45f, 0.52f ), Orange = new( 1f, 0.62f, 0.3f ), Bone = new( 0.95f, 0.92f, 0.84f ), Teal = new( 0.18f, 0.62f, 0.62f );
	public static readonly Color Polyp = new( 1f, 0.35f, 0.75f );

	/// <summary> Coral as gibs: pink branches with a pale inside, sinking like stone. </summary>
	public static GibRecipe Gibs => gibs ??= new GibRecipe
	{
		Kind = GibKind.Rock, Goo = Polyp,
		Outer = GibKit.Paint( Pink, 0.7f ), Inner = GibKit.Paint( Bone, 0.9f ), Layer = GibKit.Paint( Orange, 0.7f ),
		Bone = GibKit.Paint( Bone, 0.5f ), Sinew = GibKit.Paint( Polyp, 0.4f ),
	};
	/// <summary> An enemy turned to stone (Bleach): grey-white rock. </summary>
	public static GibRecipe Stone => stone ??= new GibRecipe
	{
		Kind = GibKind.Rock, Goo = Bone,
		Outer = GibKit.Paint( new Color( 0.82f, 0.8f, 0.76f ), 0.95f ), Inner = GibKit.Paint( new Color( 0.66f, 0.63f, 0.6f ), 0.95f ),
		Layer = GibKit.Paint( Bone, 0.8f ), Bone = GibKit.Paint( Bone, 0.5f ), Sinew = GibKit.Paint( Bone, 0.6f ),
	};
	static GibRecipe gibs, stone;
}

/// <summary>
/// The Coral-Bot's guns. The Spine Gun (slot 1) is automatic and grows coral on what it hits (`CoralGrowth`): slower and
/// slower until the coral shatters. Reef Seeds (slot 2) plant a polyp turret (left click) or grow a coral wall (right
/// click) where you aim, a seed each; at most `MaxTurrets` and `MaxWalls` stand at once (the oldest withers).
/// </summary>
public partial class PlayerWeapon
{
	public bool Spines, Seeds;

	public static PlayerWeapon SpineGun( int slot = 1 ) => new()
	{
		Name = "SpineGun", DisplayName = "Spine Gun", Slot = slot, ModelName = "spines", Spines = true,
		Damage = 20f, FireRate = 7f, Range = 1800f * U.In, SpreadDegrees = 1.4f, Recoil = 0.45f,
		MagazineSize = 28, StartingReserve = 224, ReloadTime = 1.9f, BulletImpulse = 80f * U.In, Automatic = true,
		MuzzleFlashScale = 0.15f, ShakePerShot = 0.06f, FovKickPerShot = 0.6f, ShootSound = "card_throw",
	};

	public static PlayerWeapon ReefSeeds( int slot = 2 ) => new()
	{
		Name = "ReefSeeds", DisplayName = "Reef Seeds", Slot = slot, ModelName = "seeds", Seeds = true,
		Damage = CoralFx.TurretDamage, FireRate = 2f, Range = CoralFx.PlantRange, SpreadDegrees = 0f, Recoil = 1f,
		MagazineSize = 3, StartingReserve = 24, ReloadTime = 2.2f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.12f, FovKickPerShot = 1f, ShootSound = "bubble_blow",
	};

	/// <summary> Testing aid: a seed grown into a wall where you aim (as right click does). </summary>
	public void TestPlantWall() => PlantSeed( controller.Camera.GlobalPosition, -controller.Camera.GlobalBasis.Z, true );

	/// <summary> A spine landed on an enemy: coral grows on it, and Ricochet may carry the spine on to another. </summary>
	void SpineHit( TraceResult tr, Vector3 direction )
	{
		if ( SkillFx.EnemyOf( tr.Node ) is not { } c ) return;
		CoralGrowth.Add( c, 1, controller, this, tr.HitPosition );
		var bounce = Skill( SkillId.Ricochet );
		if ( bounce <= 0 || Rng.Float() >= CoralFx.RicochetPer * bounce ) return;
		Creature next = null;
		var best = CoralFx.RicochetReach;
		foreach ( var other in Creature.Everyone )
		{
			if ( other == c || !IsInstanceValid( other ) || !other.IsEnemy || other.IsDead ) continue;
			var d = other.GlobalPosition.DistanceTo( c.GlobalPosition );
			if ( d < best ) { best = d; next = other; }
		}
		if ( next is null ) return;
		CoralFx.Ricochets++;
		var to = SkillFx.NearestPart( next, tr.HitPosition );
		SpineStreak( GetTree().CurrentScene, tr.HitPosition, to );
		SkillFx.HitPart( next, CurrentDamage * (1f + CoralFx.SharpPer * Skill( SkillId.SharpSpines )), tr.HitPosition, controller );
		if ( !next.IsDead ) CoralGrowth.Add( next, 1, controller, this, to );
	}

	/// <summary> A spine's flight drawn for a moment (ricochets, turret shots). </summary>
	public static void SpineStreak( Node root, Vector3 from, Vector3 to )
	{
		var streak = new Node3D { Name = "Spine Streak" };
		root.AddChild( streak );
		var glow = GunKit.Paint( CoralFx.Pink, 0.4f, 0f, 2.5f );
		Sculpt.Limb( streak, from, to, 0.012f, 0.02f, glow, "Streak" );
		Fx.Expire( streak, 0.08f );
	}

	/// <summary> A seed goes where you aim: a turret (left click) or a wall (right click). </summary>
	void PlantSeed( Vector3 start, Vector3 forward, bool wall )
	{
		var exclude = new Godot.Collections.Array<Rid> { controller.GetRid() };
		var tr = Trace.Ray( controller, start, start + forward * CoralFx.PlantRange, Layers.World, exclude );
		var at = tr.Hit ? tr.HitPosition : start + forward * CoralFx.PlantRange;
		var floor = Trace.Ray( controller, at + Vector3.Up * 0.5f, at + Vector3.Down * 30f, Layers.World, exclude );
		if ( floor.Hit ) at = floor.HitPosition;
		if ( wall ) CoralWall.Grow( controller, this, at, forward );
		else PolypTurret.Plant( controller, this, at );
		Sfx.Play( "splat", at, 0.6f, 0.7f );
		GpuFx.SandPuff( at + Vector3.Up * 0.2f, Vector3.Up * 1.2f, new Color( 0.8f, 0.72f, 0.55f, 0.8f ), 14f, 0.9f );
		if ( wall ) { Sfx.Play2D( ShootSound, 0.9f, 0.7f, Sfx.GunBus ); ViewModel?.Fire(); }
	}
}

/// <summary>
/// Coral growing on an enemy: a nub on the part a spine hit for each stack. Each stack slows it (`SlowPerStack`, up to
/// `MaxSlow`) by holding back part of each step it takes; stacks fall away after `StackFade` with no new spine. At
/// `ShatterAt` the coral shatters: `ShatterDamage` to it, half to enemies close by, a burst of coral gibs, Toxic Polyps'
/// poison, and Bleach turns a weak one to stone.
/// </summary>
public partial class CoralGrowth : Node
{
	Creature victim;
	PlayerController owner;
	PlayerWeapon weapon;
	int stacks;
	float fade;
	Vector3 last;
	bool hasLast;
	readonly List<Node3D> nubs = new();

	public int Stacks => stacks;
	public static CoralGrowth Of( Creature c ) => c?.GetNodeOrNull<CoralGrowth>( "Coral" );
	int Skill( SkillId id ) => Skills.Rank( owner, id );

	public static void Add( Creature c, int count, PlayerController owner, PlayerWeapon weapon, Vector3 at )
	{
		if ( c is null || c.IsDead || !IsInstanceValid( c ) ) return;
		var g = Of( c );
		if ( g is null )
		{
			g = new CoralGrowth { Name = "Coral", victim = c, owner = owner, weapon = weapon };
			c.AddChild( g );
		}
		g.Grow( count, at );
	}

	void Grow( int count, Vector3 at )
	{
		fade = CoralFx.StackFade;
		for ( var i = 0; i < count; i++ )
		{
			stacks++;
			CoralFx.Stacks++;
			Nub( at );
		}
		if ( stacks >= CoralFx.ShatterAt( Skill( SkillId.FastGrowth ) ) ) Shatter();
	}

	void Nub( Vector3 at )
	{
		// The nearest part carries it, so it moves with the limb.
		Node3D holder = victim;
		var bestD = float.MaxValue;
		foreach ( var p in CreaturePart.All )
		{
			if ( p.Creature != victim || !IsInstanceValid( p ) ) continue;
			var d = p.GlobalPosition.DistanceSquaredTo( at );
			if ( d < bestD ) { bestD = d; holder = p; }
		}
		var nub = new Node3D { Name = "Coral Nub" };
		holder.AddChild( nub );
		nub.GlobalPosition = at;
		var pink = GunKit.Paint( Rng.Float() < 0.7f ? CoralFx.Pink : CoralFx.Orange, 0.7f );
		var size = Rng.Float( 0.05f, 0.09f );
		for ( var k = 0; k < 3; k++ )
		{
			var dir = (Rng.OnSphere() + Vector3.Up * 0.5f).SafeNormal();
			Sculpt.Limb( nub, Vector3.Zero, dir * size * Rng.Float( 1.5f, 2.6f ), size * 0.45f, size * 0.2f, pink, "Branch" );
		}
		Sculpt.Blob( nub, Vector3.Zero, Vector3.One * size * 1.2f, pink, null, "Base" );
		nubs.Add( nub );
	}

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		if ( victim is null || !IsInstanceValid( victim ) || victim.IsDead ) { Clear(); QueueFree(); return; }
		// The slow: hold back part of each step it took (pulls and its own walking alike). Bosses are too big to slow.
		var now = victim.GlobalPosition;
		if ( hasLast && victim.Pullable && stacks > 0 )
		{
			var step = now - last;
			var slow = MathF.Min( CoralFx.MaxSlow( Skill( SkillId.HeavyClusters ) ), CoralFx.SlowPerStack * stacks );
			if ( step.Length() < 2f ) victim.Pull( -step * slow );
		}
		last = victim.GlobalPosition;
		hasLast = true;
		fade -= dt;
		if ( fade <= 0f && stacks > 0 )
		{
			stacks = Math.Max( 0, stacks - 2 );
			fade = 0.6f;
			while ( nubs.Count > stacks ) { if ( IsInstanceValid( nubs[^1] ) ) nubs[^1].QueueFree(); nubs.RemoveAt( nubs.Count - 1 ); }
		}
	}

	void Clear()
	{
		foreach ( var n in nubs ) if ( IsInstanceValid( n ) ) n.QueueFree();
		nubs.Clear();
		stacks = 0;
	}

	void Shatter()
	{
		CoralFx.Shatters++;
		var at = SkillFx.NearestPart( victim, victim.GlobalPosition + Vector3.Up * 0.6f );
		Clear();
		var scale = (weapon is not null && IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f) * (1f + CoralFx.BrittlePer * Skill( SkillId.BrittleBurst ));
		var reach = CoralFx.ShatterReach * (1f + CoralFx.BrittlePer * Skill( SkillId.BrittleBurst ));
		GibKit.Burst( CoralFx.Gibs, at, Vector3.Up, 0.09f, 6, 1.1f );
		Sfx.Play( "glass_break", at, 0.6f, 0.75f );
		SkillFx.HitPart( victim, CoralFx.ShatterDamage * scale, at, owner );
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( c == victim || !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			if ( SkillFx.NearestPart( c, at ).DistanceTo( at ) > reach ) continue;
			SkillFx.HitPart( c, CoralFx.ShatterDamage * 0.5f * scale, at, owner );
		}
		var toxic = Skill( SkillId.ToxicPolyps );
		if ( toxic > 0 && !victim.IsDead ) Affliction.Apply( victim, Affliction.Kind.Poison, CoralFx.ToxicPer * toxic, CoralFx.ToxicTime, owner );
		var bleach = Skill( SkillId.Bleach );
		if ( bleach > 0 && !victim.IsDead && victim.Health is { } h && h.Current <= h.MaxHealth * CoralFx.BleachBelow( bleach ) )
		{
			CoralFx.Bleached++;
			GibKit.Burst( CoralFx.Stone, victim.GlobalPosition + Vector3.Up * 0.5f, Vector3.Up, 0.16f, 10, 1.2f );
			GpuFx.SandPuff( victim.GlobalPosition + Vector3.Up * 0.5f, Vector3.Up, new Color( 0.9f, 0.88f, 0.84f, 0.9f ), 24f, 1f );
			Sfx.Play( "kraken_slam", at, 0.5f, 1.6f );
			SkillFx.HitPart( victim, h.Current + 1f, at, owner );
		}
	}
}

/// <summary>
/// A piece of the Coral-Bot's reef: a polyp turret or a wall. They link (Living Reef), heal the player near them
/// (Healing Reef), and wither when their time is up (or when too many of their kind stand).
/// </summary>
public abstract partial class ReefPiece : Node3D
{
	public static readonly List<ReefPiece> All = new();
	protected PlayerController owner;
	protected PlayerWeapon weapon;
	protected float age, life;
	protected int Skill( SkillId id ) => Skills.Rank( owner, id );
	public bool Withering => age >= life;
	public PlayerController Owner2 => owner;
	public abstract Vector3 Heart { get; }

	public override void _EnterTree() => All.Add( this );
	public override void _ExitTree() => All.Remove( this );

	/// <summary> Ages it; returns how grown it is (0 to 1: up out of the sand, shrinking away at the end). </summary>
	protected float Age( float dt )
	{
		age += dt;
		var grow = MathX.Clamp( age / 0.35f, 0f, 1f );
		var wither = MathX.Clamp( (life - age) / 0.5f, 0f, 1f );
		if ( age >= life ) QueueFree();
		return MathF.Min( 1f - (1f - grow) * (1f - grow), wither );
	}

	/// <summary> Too many of a kind: the oldest withers now. </summary>
	protected static void Thin<T>( PlayerController owner, int max ) where T : ReefPiece
	{
		var mine = All.OfType<T>().Where( p => p.owner == owner && !p.Withering ).OrderBy( p => -p.age ).ToList();
		for ( var i = 0; i < mine.Count - max + 1; i++ ) mine[i].life = MathF.Min( mine[i].life, mine[i].age + 0.5f );
	}

	public static void ClearAll()
	{
		foreach ( var p in All.ToList() ) if ( IsInstanceValid( p ) ) p.QueueFree();
	}
}

/// <summary> A polyp turret: a coral stalk with a glowing polyp that spits spines at the nearest enemy in sight. </summary>
public partial class PolypTurret : ReefPiece
{
	float shotTick;
	Node3D head;
	MeshInstance3D polyp;
	public override Vector3 Heart => GlobalPosition + Vector3.Up * 1.05f;

	public static PolypTurret Plant( PlayerController owner, PlayerWeapon weapon, Vector3 at )
	{
		Thin<PolypTurret>( owner, CoralFx.MaxTurrets );
		CoralFx.Turrets++;
		var t = new PolypTurret { Name = "Polyp Turret", owner = owner, weapon = weapon };
		t.life = CoralFx.TurretLife + CoralFx.ToughPer * t.Skill( SkillId.ToughPolyps );
		owner.GetTree().CurrentScene.AddChild( t );
		ReefLinks.Ensure( owner.GetTree() );
		t.GlobalPosition = at;
		t.Build();
		return t;
	}

	void Build()
	{
		var pink = GunKit.Paint( CoralFx.Pink, 0.7f );
		var orange = GunKit.Paint( CoralFx.Orange, 0.7f );
		var bone = GunKit.Paint( CoralFx.Bone, 0.8f );
		Sculpt.Blob( this, new Vector3( 0f, 0.1f, 0f ), new Vector3( 0.8f, 0.35f, 0.8f ), bone, null, "Base" );
		Sculpt.Limb( this, new Vector3( 0f, 0.1f, 0f ), new Vector3( 0.05f, 0.85f, 0f ), 0.2f, 0.13f, pink, "Stalk" );
		foreach ( var (a, h) in new[] { (0.4f, 0.4f), (2.5f, 0.55f), (4.4f, 0.3f) } )
		{
			var side = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			Sculpt.Limb( this, new Vector3( 0f, h, 0f ), side * 0.4f + Vector3.Up * (h + 0.35f), 0.08f, 0.04f, orange, "Branch" );
			Sculpt.Blob( this, side * 0.4f + Vector3.Up * (h + 0.37f), Vector3.One * 0.09f, GunKit.Paint( CoralFx.Polyp, 0.4f, 0f, 2f ), null, "Bud" );
		}
		head = new Node3D { Name = "Head", Position = new Vector3( 0.05f, 1.0f, 0f ) };
		AddChild( head );
		polyp = Sculpt.Blob( head, Vector3.Zero, new Vector3( 0.36f, 0.3f, 0.36f ), GunKit.Paint( CoralFx.Polyp, 0.35f, 0f, 2.5f ), null, "Polyp" );
		for ( var i = 0; i < 8; i++ )
		{
			var a = i * MathF.Tau / 8f;
			var dir = new Vector3( MathF.Cos( a ), 0.6f, MathF.Sin( a ) ).Normalized();
			Sculpt.Cone( head, dir * 0.14f, dir * 0.34f, 0.035f, bone, 6, "Spine" );
		}
		head.AddChild( new OmniLight3D { LightColor = CoralFx.Polyp, LightEnergy = 0.8f, OmniRange = 2.5f, ShadowEnabled = false } );
		Scale = Vector3.One * 0.05f;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var grown = Age( dt );
		Scale = Vector3.One * MathF.Max( 0.05f, grown );
		if ( !IsInstanceValid( owner ) || IsQueuedForDeletion() ) return;
		var rate = CoralFx.TurretRate * (1f + CoralFx.BarragePer * Skill( SkillId.Barrage )) * (ReefLinks.Linked( this ) ? CoralFx.LinkRate( Skill( SkillId.LivingReef ) ) : 1f);
		polyp.Scale = Vector3.One * (1f + 0.15f * MathF.Sin( age * 6f ));
		if ( grown < 0.9f || (shotTick -= dt) > 0f ) return;
		var from = head.GlobalPosition;
		var target = Target( from );
		if ( target is null ) { shotTick = 0.2f; return; }
		shotTick = 1f / rate;
		var to = SkillFx.NearestPart( target, from );
		head.GlobalBasis = Shapes.LookAt( to - from );
		CoralFx.TurretShots++;
		PlayerWeapon.SpineStreak( GetTree().CurrentScene, from, to );
		var scale = (IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f) * (1f + CoralFx.AnemonePer * Skill( SkillId.Anemone ));
		SkillFx.HitPart( target, CoralFx.TurretDamage * scale, from, owner );
		if ( !target.IsDead ) CoralGrowth.Add( target, 1, owner, weapon, to );
		Sfx.Play( "card_throw", from, 0.35f, 1.3f );
	}

	Creature Target( Vector3 from )
	{
		Creature best = null;
		var bestD = CoralFx.TurretRange;
		var exclude = new Godot.Collections.Array<Rid> { owner.GetRid() };
		foreach ( var c in Creature.Everyone )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var to = SkillFx.NearestPart( c, from );
			var d = to.DistanceTo( from );
			if ( d >= bestD ) continue;
			var tr = Trace.Ray( this, from, to, Layers.World, exclude );
			if ( tr.Hit && tr.HitPosition.DistanceTo( to ) > 0.5f ) continue;
			bestD = d;
			best = c;
		}
		return best;
	}
}

/// <summary> A coral wall grown across your aim: solid to enemies and shots, gone after `WallLife`. Spiked Walls hurt what touches it. </summary>
public partial class CoralWall : ReefPiece
{
	StaticBody3D body;
	Node3D look;
	float tick;
	public override Vector3 Heart => GlobalPosition + Vector3.Up * CoralFx.WallHeight * 0.55f;

	public static CoralWall Grow( PlayerController owner, PlayerWeapon weapon, Vector3 at, Vector3 facing, float life = CoralFx.WallLife, float width = CoralFx.WallWidth, bool thin = true )
	{
		if ( thin ) Thin<CoralWall>( owner, CoralFx.MaxWalls );
		CoralFx.Walls++;
		var w = new CoralWall { Name = "Coral Wall", owner = owner, weapon = weapon, life = life };
		owner.GetTree().CurrentScene.AddChild( w );
		ReefLinks.Ensure( owner.GetTree() );
		w.GlobalPosition = at;
		var flat = facing.Flat().SafeNormal();
		if ( flat == Vector3.Zero ) flat = Vector3.Forward;
		w.GlobalBasis = Shapes.LookAt( flat );
		w.Build( width );
		Sfx.Play( "kraken_rise", at, 0.5f, 1.8f );
		return w;
	}

	void Build( float width )
	{
		body = new StaticBody3D { Name = "Wall Body", CollisionLayer = Layers.World, CollisionMask = 0 };
		AddChild( body );
		body.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3( width, CoralFx.WallHeight, CoralFx.WallThick ) }, Position = Vector3.Up * CoralFx.WallHeight * 0.5f } );
		look = new Node3D { Name = "Look" };
		AddChild( look );
		var rng = new Random( (int)(GlobalPosition.X * 100f) ^ (int)(GlobalPosition.Z * 37f) );
		float R( float a, float b ) => a + (float)rng.NextDouble() * (b - a);
		var pink = GunKit.Paint( CoralFx.Pink, 0.75f );
		var orange = GunKit.Paint( new Color( 1f, 0.5f, 0.22f ), 0.6f );
		var bone = GunKit.Paint( CoralFx.Bone, 0.85f );
		var polyp = GunKit.Paint( CoralFx.Polyp, 0.4f, 0f, 1.8f );
		// A ridge of lumpy coral heads, branches and brain coral, with bright polyps in it.
		var count = (int)(width / 0.45f);
		for ( var i = 0; i < count; i++ )
		{
			var x = MathX.Lerp( -width * 0.5f + 0.2f, width * 0.5f - 0.2f, i / (float)Math.Max( 1, count - 1 ) );
			var h = R( 0.75f, 1.0f ) * CoralFx.WallHeight;
			Sculpt.Blob( look, new Vector3( x, h * 0.45f, R( -0.08f, 0.08f ) ), new Vector3( R( 0.5f, 0.7f ), h, R( 0.45f, 0.6f ) ), i % 3 == 1 ? bone : pink, null, "Head" );
			var tip = new Vector3( x + R( -0.25f, 0.25f ), h + R( 0.15f, 0.35f ), R( -0.15f, 0.15f ) );
			Sculpt.Limb( look, new Vector3( x, h * 0.7f, 0f ), tip, 0.09f, 0.05f, i % 2 == 0 ? orange : pink, "Branch" );
			Sculpt.Blob( look, tip, Vector3.One * 0.13f, polyp, null, "Tip" );
			if ( i % 2 == 0 ) Sculpt.Blob( look, new Vector3( x + R( -0.15f, 0.15f ), h * R( 0.4f, 0.9f ), 0.28f ), Vector3.One * 0.1f, polyp, null, "Polyp" );
			if ( i % 2 == 1 ) Sculpt.Blob( look, new Vector3( x + R( -0.15f, 0.15f ), h * R( 0.4f, 0.9f ), -0.28f ), Vector3.One * 0.1f, polyp, null, "Polyp" );
		}
		look.Scale = new Vector3( 1f, 0.05f, 1f );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var grown = Age( dt );
		look.Scale = new Vector3( 1f, MathF.Max( 0.05f, grown ), 1f );
		body.Position = Vector3.Down * CoralFx.WallHeight * (1f - grown);
		if ( !IsInstanceValid( owner ) || IsQueuedForDeletion() ) return;
		var spiked = Skill( SkillId.SpikedWalls );
		if ( spiked <= 0 || (tick -= dt) > 0f ) return;
		tick = 0.25f;
		var right = GlobalBasis.X;
		var scale = IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var rel = c.GlobalPosition - GlobalPosition;
			var along = rel.Dot( right );
			var across = rel.Dot( GlobalBasis.Z );
			if ( MathF.Abs( along ) > CoralFx.WallWidth * 0.5f + 0.6f || MathF.Abs( across ) > CoralFx.WallThick * 0.5f + 0.9f || rel.Y > CoralFx.WallHeight + 0.5f ) continue;
			PlayerPerks.Hurt( c, CoralFx.SpikedPer * spiked * 0.25f * scale, c.GlobalPosition, GlobalPosition, owner );
			if ( Rng.Float() < 0.5f ) CoralGrowth.Add( c, 1, owner, weapon, SkillFx.NearestPart( c, GlobalPosition ) );
		}
	}
}

/// <summary>
/// What the reef does together, run once a frame: Healing Reef heals the player near any piece, and Living Reef links
/// pieces within `LinkReach` with glowing veins that hurt enemies crossing them (and makes linked turrets fire faster).
/// </summary>
public partial class ReefLinks : Node
{
	readonly List<MeshInstance3D> veins = new();
	/// <summary> Tests: veins drawn this frame. </summary>
	public static int Pairs;
	float tick, healCarry;
	static ShaderMaterial veinMaterial;

	public static void Ensure( SceneTree tree )
	{
		if ( tree.CurrentScene.GetNodeOrNull<ReefLinks>( "ReefLinks" ) is null ) tree.CurrentScene.AddChild( new ReefLinks { Name = "ReefLinks" } );
	}

	/// <summary> Whether this piece is linked to another (Living Reef). </summary>
	public static bool Linked( ReefPiece piece )
	{
		if ( Skills.Rank( piece.Owner2, SkillId.LivingReef ) <= 0 ) return false;
		foreach ( var other in ReefPiece.All )
			if ( other != piece && other.Owner2 == piece.Owner2 && !other.Withering && other.Heart.DistanceTo( piece.Heart ) < CoralFx.LinkReach ) return true;
		return false;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var pieces = ReefPiece.All.Where( p => IsInstanceValid( p ) && !p.Withering && IsInstanceValid( p.Owner2 ) ).ToList();
		var owner = pieces.FirstOrDefault()?.Owner2;
		var pairs = new List<(Vector3 A, Vector3 B)>();
		var rank = owner is null ? 0 : Skills.Rank( owner, SkillId.LivingReef );
		if ( rank > 0 )
		{
			// Each piece links to its two nearest within reach (a ring of walls is a ring, not a web).
			var seen = new HashSet<(int, int)>();
			for ( var i = 0; i < pieces.Count; i++ )
			{
				var near = Enumerable.Range( 0, pieces.Count ).Where( k => k != i && pieces[i].Heart.DistanceTo( pieces[k].Heart ) < CoralFx.LinkReach )
					.OrderBy( k => pieces[i].Heart.DistanceTo( pieces[k].Heart ) ).Take( 2 );
				foreach ( var k in near )
					if ( seen.Add( (Math.Min( i, k ), Math.Max( i, k )) ) ) pairs.Add( (pieces[i].Heart, pieces[k].Heart) );
			}
		}
		Pairs = pairs.Count;
		while ( veins.Count < pairs.Count ) veins.Add( GunKit.Line( this, VeinMaterial, 0.05f ) );
		for ( var i = 0; i < veins.Count; i++ )
			if ( i < pairs.Count ) GunKit.Lay( veins[i], pairs[i].A, pairs[i].B );
			else veins[i].Visible = false;
		VeinMaterial.SetShaderParameter( "pulse", (float)Clock.Now );

		tick -= dt;
		if ( tick > 0f ) return;
		tick = 0.25f;
		if ( pairs.Count > 0 )
			foreach ( var c in Creature.Everyone.ToList() )
			{
				if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
				var p = c.GlobalPosition + Vector3.Up * 0.5f;
				if ( !pairs.Any( s => Distance( p, s.A, s.B ) < 0.9f ) ) continue;
				CoralFx.VeinHits++;
				PlayerPerks.Hurt( c, CoralFx.VeinDps( rank ) * 0.25f, p, p, owner );
				LightningArc.Spark( GetTree().CurrentScene, p );
			}
		// Healing Reef: health back near any piece.
		foreach ( var player in pieces.Select( p => p.Owner2 ).Distinct() )
		{
			var heal = Skills.Rank( player, SkillId.HealingReef );
			if ( heal <= 0 || player.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) is not { Health: { } h, IsAlive: true } ) continue;
			if ( !pieces.Any( p => p.Owner2 == player && p.Heart.DistanceTo( player.GlobalPosition + Vector3.Up ) < CoralFx.HealingReach ) ) continue;
			if ( h.Current >= h.MaxHealth ) continue;
			healCarry += CoralFx.HealingPer * heal * 0.25f;
			CoralFx.Healed++;
			h.Heal( healCarry );
			healCarry = 0f;
		}
	}

	static float Distance( Vector3 p, Vector3 a, Vector3 b )
	{
		var ab = b - a;
		var t = MathX.Clamp( (p - a).Dot( ab ) / MathF.Max( 1e-4f, ab.LengthSquared() ), 0f, 1f );
		return p.DistanceTo( a + ab * t );
	}

	static ShaderMaterial VeinMaterial => veinMaterial ??= new ShaderMaterial { Shader = new Shader { Code = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform float pulse = 0.0;
varying vec3 world;
void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
	float flow = 0.55 + 0.45 * sin((world.x + world.z) * 3.0 - pulse * 8.0);
	vec3 c = mix(vec3(1.0, 0.3, 0.75), vec3(0.55, 0.3, 1.0), flow);
	ALBEDO = c * (1.2 + flow);
	ALPHA = 0.6 + 0.4 * flow;
}" } };
}

/// <summary> The Coral-Bot's gun models, on the Blaster's rig like the trident shooter. </summary>
public static class CoralModels
{
	public static readonly Vector3 SpineMuzzle = new( 30f, 0f, 3.4f ), SeedMuzzle = new( 26f, 0f, 3.6f );

	public static void SpineGun( Node3D gun )
	{
		var pink = GunKit.Paint( CoralFx.Pink, 0.6f );
		var orange = GunKit.Paint( CoralFx.Orange, 0.6f );
		var bone = GunKit.Paint( CoralFx.Bone, 0.7f );
		var teal = GunKit.Paint( CoralFx.Teal, 0.4f, 0.4f );
		var dark = GunKit.Paint( new Color( 0.1f, 0.1f, 0.12f ), 0.6f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), dark, 18f );
		GunKit.Box( gun, new Vector3( 2.4f, 0f, -2.1f ), new Vector3( 4.2f, 0.6f, 0.5f ), dark );
		// A teal body grown over with coral, a drum of spines underneath, and a barrel ringed with bone spikes.
		GunKit.AlongX( gun, new Vector3( 7f, 0f, 3.3f ), 1.9f, 16f, teal, 1.6f, 20 );
		GunKit.Ball( gun, new Vector3( -1f, 0f, 3.3f ), 1.9f, teal );
		var rng = new Random( 7 );
		for ( var i = 0; i < 9; i++ )
		{
			var x = (float)rng.NextDouble() * 14f;
			var a = (float)rng.NextDouble() * MathF.Tau;
			var at = new Vector3( x, MathF.Cos( a ) * 1.7f, 3.3f + MathF.Abs( MathF.Sin( a ) ) * 1.7f );
			GunKit.Ball( gun, at, 0.55f + (float)rng.NextDouble() * 0.35f, i % 3 == 0 ? orange : pink );
			GunKit.Spike( gun, at, at + new Vector3( 0.4f, MathF.Cos( a ) * 0.8f, 1.1f ), 0.25f, bone, 6 );
		}
		GunKit.Part( gun, new CylinderMesh { TopRadius = 1.7f, BottomRadius = 1.7f, Height = 2.4f, RadialSegments = 16 }, bone, new Vector3( 9f, 0f, 0.4f ), new Basis( Vector3.Right, MathF.PI * 0.5f ), "Drum" );
		GunKit.AlongX( gun, new Vector3( 20.5f, 0f, 3.4f ), 1.3f, 12f, bone, 1.1f );
		for ( var i = 0; i < 6; i++ )
		{
			var a = i * MathF.Tau / 6f;
			var at = new Vector3( 27.5f, MathF.Cos( a ) * 1.4f, 3.4f + MathF.Sin( a ) * 1.4f );
			GunKit.Spike( gun, at, at + new Vector3( 2.2f, MathF.Cos( a ) * 0.7f, MathF.Sin( a ) * 0.7f ), 0.3f, orange, 6 );
		}
		GunKit.AlongX( gun, new Vector3( -3.5f, 0f, 2.8f ), 1.1f, 4f, dark, 1.4f, 12 );
	}

	public static void SeedPod( Node3D gun )
	{
		var pink = GunKit.Paint( CoralFx.Pink, 0.6f );
		var orange = GunKit.Paint( CoralFx.Orange, 0.6f );
		var bone = GunKit.Paint( CoralFx.Bone, 0.7f );
		var polyp = GunKit.Paint( CoralFx.Polyp, 0.35f, 0f, 2.2f );
		var dark = GunKit.Paint( new Color( 0.1f, 0.1f, 0.12f ), 0.6f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), dark, 18f );
		// A bulb-shaped brain coral launcher with three seed pods in a rack on top and a flared mouth.
		var body = GunKit.Ball( gun, new Vector3( 6f, 0f, 3.6f ), 3f, pink );
		body.Scale = new Vector3( 2f, 0.85f, 0.85f );
		for ( var i = 0; i < 4; i++ )
		{
			var ring = GunKit.Part( gun, new TorusMesh { InnerRadius = 2.2f, OuterRadius = 2.55f, Rings = 20, RingSegments = 6 }, bone, new Vector3( 2.2f + i * 2.5f, 0f, 3.6f ), new Basis( Vector3.Back, -MathF.PI * 0.5f ), "Brain Ridge" );
			ring.Scale = Vector3.One * (1f - MathF.Abs( i - 1.5f ) * 0.15f);
		}
		GunKit.AlongX( gun, new Vector3( 14.5f, 0f, 3.6f ), 1.4f, 6f, orange, 2.3f, 20 );
		GunKit.Part( gun, new TorusMesh { InnerRadius = 1.9f, OuterRadius = 2.5f, Rings = 20, RingSegments = 8 }, pink, new Vector3( 17.6f, 0f, 3.6f ), new Basis( Vector3.Back, -MathF.PI * 0.5f ), "Mouth" );
		var rack = new Node3D { Name = "Loaded", Position = new Vector3( 5f, 0f, 6.6f ), Scale = Vector3.One * 0.7f };
		gun.AddChild( rack );
		for ( var i = 0; i < 3; i++ )
		{
			GunKit.Ball( rack, new Vector3( -3f + i * 3f, 0f, 0f ), 1.3f, polyp );
			GunKit.Spike( rack, new Vector3( -3f + i * 3f, 0f, 0.8f ), new Vector3( -3f + i * 3f, 0f, 2.6f ), 0.4f, bone, 6 );
		}
		GunKit.AlongX( gun, new Vector3( -3.2f, 0f, 3f ), 1f, 3.5f, dark, 1.3f, 12 );
	}
}
