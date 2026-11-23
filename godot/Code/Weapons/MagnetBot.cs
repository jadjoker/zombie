using Godot;

/// <summary> The Magnet-Bot's numbers (what one skill point does, and the base kit). </summary>
public static class MagnetFx
{
	// The Scrap Cannon: hold to gather scrap into a ball at the muzzle, let go to fire it; right click recalls them.
	public const float GatherTime = 0.9f, GatherReach = 9f, BallSpeed = 24f, BallRadius = 0.26f, JunkPer = 0.08f, JunkMax = 1f;
	public const float HeavyMetalPer = 0.1f, QuickGatherPer = 0.1f, WreckingPer = 0.12f, JunkMagnetPer = 0.04f, ShrapnelPer = 0.12f, ShrapnelReach = 5f;
	public static int KatamariHolds( int n ) => 1 + n;
	// The Polarity Gun: red (+) on left click, blue (-) on right. Opposites pull together and smash; likes push apart.
	public const float TagTime = 6f, FieldReach = 14f, AttractSpeed = 7f, RepelSpeed = 3f, RepelReach = 4f, SmashReach = 1.4f, SmashDamage = 85f, SmashStun = 0.7f;
	public const float StrongFieldPer = 0.15f, LongChargePer = 1f, HardSmashPer = 0.15f, ClingPer = 4f, SpreadPer = 0.12f, SpreadReach = 4f;
	public static float ArcWeldDamage( int n ) => 15f + 10f * n;
	public static float MagnetarReach( int n ) => 4f + n;
	public static float MagnetarDamage( int n ) => 50f + 35f * n;
	// The Repulsor Pulse (mouse 4).
	public const float PulseCost = 30f, PulseRadius = 5f, PulseDamage = 30f, PulseShove = 6f;
	public const float OverchargePer = 0.15f, CapacitorPer = 0.1f, ArmourPer = 0.06f, ArmourTime = 3f, DischargeReach = 8f;
	public static float DischargeDamage( int n ) => 15f + 5f * n;
	public static float ReversalDamage( int n ) => 50f + 35f * n;
	public const float ReversalPull = 0.45f;

	public static readonly Color Red = new( 1f, 0.22f, 0.2f ), Blue = new( 0.25f, 0.55f, 1f ), Steel = new( 0.72f, 0.75f, 0.8f );
	static GibRecipe gibs;
	/// <summary> Scrap as gibs: rusty plates and bright steel, sinking like armour. </summary>
	public static GibRecipe Gibs => gibs ??= new GibRecipe
	{
		Kind = GibKind.Rock, Goo = new Color( 0.45f, 0.3f, 0.2f ),
		Outer = GibKit.Paint( new Color( 0.5f, 0.32f, 0.22f ), 0.8f ), Inner = GibKit.Paint( Steel, 0.3f ), Layer = GibKit.Paint( new Color( 0.3f, 0.3f, 0.33f ), 0.6f ),
		Bone = GibKit.Paint( Steel, 0.3f ), Sinew = GibKit.Paint( Red, 0.5f ),
	};

	/// <summary> Tests: how often each thing happened. </summary>
	public static int Balls, BallHits, Junk, Recalls, Shrapnels, Carried, Tags, Smashes, Arcs, Spreads, Magnetars, Pulses, Discharges, Reversals;
}

/// <summary>
/// The Magnet-Bot's guns. The Scrap Cannon (slot 1): hold left click and its magnet drags the gore and scrap near by
/// into a ball at the muzzle (a fuller ball and more junk hit harder), let go to fire it; it bounces and rolls
/// (`ScrapBall`), and right click calls every ball back, hitting what is in the way. The Polarity Gun (slot 2): left
/// click tags an enemy red, right click blue (`MagnetTag`): opposite tags drag their enemies together until they
/// smash, like tags shove apart.
/// </summary>
public partial class PlayerWeapon
{
	public bool Scrap, Polarity;
	float gather, junkBonus;
	bool gathering;
	Node3D gatherBall;
	TimeSince sinceGatherHum = 9f;

	public float Gather => gather;
	/// <summary> A recalled scrap ball came home: a ball back in the magazine. </summary>
	public void CatchBall() { if ( Ammo < MagazineSize ) Ammo++; }
	public float JunkBonus => junkBonus;

	public static PlayerWeapon ScrapCannon( int slot = 1 ) => new()
	{
		Name = "ScrapCannon", DisplayName = "Scrap Cannon", Slot = slot, ModelName = "scrap", Scrap = true,
		Damage = 110f /* a full ball at full speed */, FireRate = 2f, Range = 40f, SpreadDegrees = 0f, Recoil = 2.5f,
		MagazineSize = 6, StartingReserve = 60, ReloadTime = 2f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.3f, FovKickPerShot = 2f, ShootSound = "magnet_clang",
	};

	public static PlayerWeapon PolarityGun( int slot = 2 ) => new()
	{
		Name = "PolarityGun", DisplayName = "Polarity Gun", Slot = slot, ModelName = "polarity", Polarity = true,
		Damage = 28f, FireRate = 3.5f, Range = 45f, SpreadDegrees = 0.4f, Recoil = 0.8f,
		MagazineSize = 12, StartingReserve = 144, ReloadTime = 1.6f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.08f, FovKickPerShot = 0.6f, ShootSound = "beam_fire",
	};

	/// <summary> A shot for the guns that fire on their own terms (scrap, polarity, pinball): ammo, sound, kick. </summary>
	bool BotShot( float volume = 0.9f, float pitch = 1f )
	{
		if ( IsReloading || TimeSinceShot < 1f / CurrentFireRate ) return false;
		if ( Ammo <= 0 && !InfiniteAmmo ) { if ( Reserve > 0 ) StartReload(); else Sfx.Play2D( "dry_fire" ); return false; }
		TimeSinceShot = 0f;
		if ( !InfiniteAmmo ) Ammo--;
		Sfx.Play2D( ShootSound, volume, pitch, Sfx.GunBus );
		if ( ViewModel is not null && ViewModel.Visible ) ViewModel.Fire();
		juice?.Shake( ShakePerShot );
		juice?.KickFov( FovKickPerShot );
		Controls.Rumble( 0.25f );
		controller.EyeAngles += new Angles( -Recoil, Rng.Float( -Recoil, Recoil ) * 0.3f, 0f );
		return true;
	}

	Vector3 Aim => controller.Camera is { } cam ? -cam.GlobalBasis.Z : controller.EyeAngles.Forward;
	Vector3 Eye => controller.Camera?.GlobalPosition ?? controller.EyeTransform.Origin;

	/// <summary> The Scrap Cannon and the Polarity Gun each frame (their own triggers, no aiming down sights). </summary>
	void UpdateMagnet( float dt, bool free )
	{
		if ( controller.In.JustPressed( "reload" ) ) StartReload();
		if ( Scrap ) UpdateScrap( dt, free );
		else if ( free && !IsReloading )
		{
			if ( controller.In.JustPressed( "attack1" ) ) FireTag( +1 );
			else if ( controller.In.JustPressed( "attack2" ) ) FireTag( -1 );
		}
	}

	float GatherSpeed => 1f / (MagnetFx.GatherTime * MathF.Max( 0.3f, 1f - MagnetFx.QuickGatherPer * Skill( SkillId.QuickGather ) ));

	void UpdateScrap( float dt, bool free )
	{
		if ( free && controller.In.JustPressed( "attack2" ) ) Recall();
		var held = free && controller.In.Pressed( "attack1" ) && !IsReloading && (Ammo > 0 || InfiniteAmmo);
		if ( free && controller.In.JustPressed( "attack1" ) && Ammo <= 0 && !InfiniteAmmo ) { if ( Reserve > 0 ) StartReload(); else Sfx.Play2D( "dry_fire" ); }
		if ( held && TimeSinceShot >= 1f / CurrentFireRate )
		{
			if ( !gathering ) { gathering = true; gather = 0f; junkBonus = 0f; }
			GatherStep( dt );
		}
		else if ( gathering ) FireScrap();
		ShowGatherBall();
	}

	/// <summary> Testing aid: gather for this long (as if left click were held), then fire. </summary>
	public void TestScrap( float seconds )
	{
		gathering = true;
		gather = 0f;
		junkBonus = 0f;
		for ( var t = 0f; t < seconds; t += 1f / 30f ) GatherStep( 1f / 30f );
		TimeSinceShot = 999f;
		FireScrap();
	}

	void GatherStep( float dt )
	{
		gather = MathF.Min( 1f, gather + dt * GatherSpeed );
		if ( sinceGatherHum > 1.1f ) { sinceGatherHum = 0f; Sfx.Play2D( "magnet_hum", 0.4f, 0.8f + 0.4f * gather, Sfx.GunBus ); }
		// The magnet drags loose gore and scrap in: each piece that reaches the muzzle is junk in the ball.
		var muzzle = LinePoint();
		var reach = MagnetFx.GatherReach;
		var gore = GoreSystem.Get( controller );
		if ( gore is null ) return;
		foreach ( var gib in gore.Gibs.ToList() )
		{
			if ( !IsInstanceValid( gib ) || gib.IsFading ) continue;
			var to = muzzle - gib.GlobalPosition;
			var d = to.Length();
			if ( d > reach ) continue;
			if ( d < 0.7f )
			{
				MagnetFx.Junk++;
				var per = gib.Recycled ? 0.01f : MagnetFx.JunkPer + MagnetFx.JunkMagnetPer * Skill( SkillId.JunkMagnet );
				junkBonus = MathF.Min( MagnetFx.JunkMax, junkBonus + per );
				Sfx.Play2D( "mine_tink", 0.25f, Rng.Float( 0.9f, 1.4f ), Sfx.GunBus );
				gib.QueueFree();
				continue;
			}
			gib.Suck( MathX.Clamp( d / 3f, 0.3f, 1f ) );
			gib.LinearVelocity = gib.LinearVelocity.Lerp( to / d * MathX.Lerp( 14f, 5f, d / reach ), 1f - MathF.Exp( -6f * dt ) );
		}
	}

	void FireScrap()
	{
		gathering = false;
		var charge = gather;
		gather = 0f;
		if ( !BotShot( 0.8f, 1.2f - 0.3f * charge ) ) return;
		MagnetFx.Balls++;
		var size = MathX.Lerp( 0.55f, 1f, charge ) * (1f + MagnetFx.WreckingPer * Skill( SkillId.WreckingBall ));
		var damage = CurrentDamage * MathX.Lerp( 0.5f, 1f, charge ) * (1f + junkBonus) * (1f + MagnetFx.HeavyMetalPer * Skill( SkillId.HeavyMetal ));
		var aim = Aim;
		var from = controller.SeenFromOutside ? controller.EyeTransform.Origin + aim * 0.8f : Eye + aim * 0.7f - controller.Camera.GlobalBasis.Y * 0.15f;
		ScrapBall.Fire( this, controller, from, aim * MagnetFx.BallSpeed * MathX.Lerp( 0.7f, 1f, charge ), size, damage, junkBonus );
		junkBonus = 0f;
	}

	/// <summary> Right click: every scrap ball out flies back to the gun, hitting what is in the way. </summary>
	public void Recall()
	{
		var any = false;
		foreach ( var ball in BotBall.All.OfType<ScrapBall>().ToList() )
			if ( ball.Owner == controller && !ball.Ended ) { ball.Recall(); any = true; }
		if ( !any ) return;
		MagnetFx.Recalls++;
		Sfx.Play2D( "magnet_hum", 0.6f, 1.5f, Sfx.GunBus );
	}

	void ShowGatherBall()
	{
		if ( gathering && gather > 0f )
		{
			if ( gatherBall is null || !IsInstanceValid( gatherBall ) )
			{
				gatherBall = new Node3D { Name = "Gathering Scrap", TopLevel = true };
				controller.AddChild( gatherBall );
				ScrapBall.Look( gatherBall, 1f );
			}
			gatherBall.Visible = true;
			gatherBall.GlobalPosition = LinePoint() + Aim * 0.12f;
			gatherBall.Scale = Vector3.One * MathX.Lerp( 0.15f, 0.42f, gather );
			gatherBall.Rotation += new Vector3( 0.03f, 0.05f, 0f );
		}
		else if ( gatherBall is not null && IsInstanceValid( gatherBall ) ) gatherBall.Visible = false;
	}

	/// <summary> Testing aid: tags the enemy under the crosshair (+1 red, -1 blue). </summary>
	public void TestTag( int charge ) { TimeSinceShot = 999f; FireTag( charge ); }

	void FireTag( int charge )
	{
		if ( !BotShot( 0.6f, charge > 0 ? 1.4f : 1.1f ) ) return;
		var start = Eye;
		var direction = (Aim + Rng.InSphere( 1f ) * MathF.Tan( MathX.Rad( SpreadDegrees ) )).Normalized();
		var tr = Trace.Ray( controller, start, start + direction * Range, Layers.World | Layers.Creature, new Godot.Collections.Array<Rid> { controller.GetRid() } );
		var end = tr.Hit ? tr.HitPosition : start + direction * Range;
		var colour = charge > 0 ? MagnetFx.Red : MagnetFx.Blue;
		var root = GetTree().CurrentScene;
		var streak = new Node3D { Name = "Polarity Streak" };
		root.AddChild( streak );
		Sculpt.Limb( streak, LinePoint(), end, 0.03f, 0.015f, Sculpt.Glow( colour, 5f ), "Streak" );
		Fx.Expire( streak, 0.1f );
		if ( !tr.Hit ) return;
		var shootable = Shootables.Find( tr.Node );
		if ( shootable is not null && !shootable.IsDead ) shootable.ReceiveShot( CurrentDamage, tr.Node, direction, tr.HitPosition, start, controller, true );
		if ( SkillFx.EnemyOf( tr.Node ) is { } c )
		{
			MarkHit();
			MagnetTag.Put( c, charge, controller, this );
			var spread = Skill( SkillId.SpreadCharge );
			if ( spread > 0 && Rng.Float() < MagnetFx.SpreadPer * spread )
			{
				Creature next = null;
				var best = MagnetFx.SpreadReach;
				foreach ( var o in Creature.Everyone )
				{
					if ( o == c || !IsInstanceValid( o ) || !o.IsEnemy || o.IsDead || MagnetTag.Of( o ) is not null ) continue;
					var d = o.GlobalPosition.DistanceTo( c.GlobalPosition );
					if ( d < best ) { best = d; next = o; }
				}
				if ( next is not null ) { MagnetFx.Spreads++; SkillFx.Zap( tr.HitPosition, SkillFx.NearestPart( next, tr.HitPosition ) ); MagnetTag.Put( next, charge, controller, this ); }
			}
		}
		else Fx.ImpactWorld( tr.HitPosition, tr.Normal );
	}
}

/// <summary>
/// A ball of magnetised scrap (gore, shell, bits of wreck). It bounces and rolls; an enemy it meets takes its damage
/// (more for a bigger ball moving faster) and is knocked along. Shrapnel bursts it at the end into shards that seek the
/// enemies near by; Katamari sticks the light enemies it hits to it and carries them along. Recalled, it flies back to
/// the gun (gravity off) and is caught, giving a ball back.
/// </summary>
public partial class ScrapBall : BotBall
{
	float size, damage, junk;
	bool recalled;
	readonly List<Creature> stuck = new();

	public static ScrapBall Fire( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 velocity, float size, float damage, float junk )
	{
		var b = new ScrapBall { Name = "Scrap Ball", size = size, damage = damage, junk = junk };
		b.Radius = MagnetFx.BallRadius * size;
		b.Gravity = 4f;
		b.Drag = 0.1f;
		b.Restitution = 0.5f;
		b.BumperGain = 0.75f;
		b.MaxBounces = 3 + Skills.Rank( owner, SkillId.Bouncer );
		b.Life = 5f;
		b.Launch( weapon, owner, from, velocity );
		return b;
	}

	public IReadOnlyList<Creature> Stuck => stuck;

	/// <summary> The ball's look: a lumpy grey-and-rust ball of junk, bolts and bits of shell, with a red glow. </summary>
	public static void Look( Node3D parent, float r )
	{
		var rust = GunKit.Paint( new Color( 0.5f, 0.3f, 0.2f ), 0.8f, 0.3f );
		var steel = GunKit.Paint( MagnetFx.Steel, 0.35f, 0.8f );
		var shell = GunKit.Paint( new Color( 0.85f, 0.78f, 0.65f ), 0.6f );
		var meat = GunKit.Paint( new Color( 0.62f, 0.16f, 0.18f ), 0.5f );
		GunKit.Ball( parent, Vector3.Zero, r * 0.82f, rust );
		var mats = new[] { steel, shell, meat, rust, steel };
		for ( var i = 0; i < 14; i++ )
		{
			var dir = Rng.OnSphere();
			var m = mats[i % mats.Length];
			if ( i % 3 == 0 ) GunKit.Box( parent, dir * r * 0.8f, Vector3.One * r * Rng.Float( 0.25f, 0.45f ), m, Rng.Float( -40f, 40f ) );
			else GunKit.Ball( parent, dir * r * 0.78f, r * Rng.Float( 0.2f, 0.34f ), m );
		}
		GunKit.Rod( parent, new Vector3( -r, 0.1f * r, 0f ), new Vector3( r, -0.1f * r, 0f ), r * 0.08f, steel );
		Sculpt.Blob( parent, Vector3.Zero, Vector3.One * r * 2.3f, SkillFx.Rim( MagnetFx.Red, 2f ), null, "Field" );
	}

	protected override void Build() => Look( look, Radius );

	public void Recall()
	{
		if ( recalled ) return;
		recalled = true;
		Gravity = 0f;
		Drag = 0f;
		Life = age + 3f;
		MaxBounces = 99;
		HitGapReset();
	}

	void HitGapReset() => HitGap = 0.15f;

	protected override void Step( float dt )
	{
		// Katamari: what it stuck to rides along with it.
		for ( var i = stuck.Count - 1; i >= 0; i-- )
		{
			var c = stuck[i];
			if ( !IsInstanceValid( c ) || c.IsDead ) { stuck.RemoveAt( i ); continue; }
			var to = GlobalPosition - (c.GlobalPosition + Vector3.Up * 0.4f);
			if ( to.Length() > 0.3f ) c.Pull( to * MathF.Min( 1f, 12f * dt ) );
		}
		if ( !recalled ) return;
		var home = weapon is not null && IsInstanceValid( weapon ) ? weapon.LinePoint() : owner.GlobalPosition + Vector3.Up;
		var toHome = home - GlobalPosition;
		if ( toHome.Length() < 1.1f )
		{
			if ( weapon is not null && IsInstanceValid( weapon ) ) weapon.CatchBall();
			Sfx.Play2D( "magnet_clang", 0.5f, 1.4f, Sfx.GunBus );
			End();
			return;
		}
		Velocity = Velocity.Lerp( toHome.Normalized() * 30f, 1f - MathF.Exp( -5f * dt ) );
	}

	protected override bool HitEnemy( Creature c, Node3D part, Vector3 at, float speed )
	{
		MagnetFx.BallHits++;
		var scale = MathX.Clamp( speed / MagnetFx.BallSpeed, 0.6f, 1.4f ) * (1f + 0.25f * stuck.Count);
		Strike( c, part, at, damage * scale );
		Sfx.Play( "magnet_clang", at, 0.8f, Rng.Float( 0.85f, 1.05f ) );
		owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.1f );
		if ( c.IsDead ) { weapon?.MarkKill(); return false; }
		weapon?.MarkHit();
		SkillFx.Shove( c, Velocity.Flat().SafeNormal() * 1.1f * size );
		var kat = Skills.Rank( owner, SkillId.Katamari );
		if ( kat > 0 && c.Pullable && c.Weight < 600f && stuck.Count < MagnetFx.KatamariHolds( kat ) && !stuck.Contains( c ) )
		{
			MagnetFx.Carried++;
			stuck.Add( c );
			c.Stun( 1.5f );
			return false;
		}
		return !recalled;
	}

	protected override void HitWorld( Vector3 at, Vector3 normal, float speed )
	{
		if ( speed > 4f ) Sfx.Play( "hit_world", at, MathX.Clamp( speed / 20f, 0.2f, 0.7f ), 0.7f );
		if ( speed > 6f ) GpuFx.SandPuff( at, normal * 1.2f, new Color( 0.8f, 0.74f, 0.6f, 0.7f ), 6f, 0.6f );
	}

	protected override void Finish()
	{
		foreach ( var c in stuck ) if ( IsInstanceValid( c ) && !c.IsDead ) { SkillFx.HitPart( c, damage * 0.5f, GlobalPosition, owner ); c.Stun( 0.3f ); }
		stuck.Clear();
		var n = Skills.Rank( owner, SkillId.Shrapnel );
		if ( n <= 0 || recalled ) { GibKit.Burst( MagnetFx.Gibs, GlobalPosition, Vector3.Up, 0.05f, 3, 0.6f ); return; }
		// Shrapnel: the ball bursts into shards that fly at the enemies near by.
		MagnetFx.Shrapnels++;
		var at = GlobalPosition;
		var targets = Creature.Everyone.Where( c => IsInstanceValid( c ) && c.IsEnemy && !c.IsDead && c.GlobalPosition.DistanceTo( at ) < MagnetFx.ShrapnelReach )
			.OrderBy( c => c.GlobalPosition.DistanceTo( at ) ).Take( 2 + n ).ToList();
		foreach ( var c in targets )
		{
			var to = SkillFx.NearestPart( c, at );
			PlayerWeapon.SpineStreak( GetTree().CurrentScene, at, to );
			SkillFx.HitPart( c, damage * MagnetFx.ShrapnelPer * n, at, owner );
		}
		GibKit.Burst( MagnetFx.Gibs, at, Vector3.Up, 0.06f, 6, 1f );
		Sfx.Play( "glass_break", at, 0.6f, 0.6f );
	}
}

/// <summary>
/// A polarity tag on an enemy: a red (+) or blue (-) ring of light. Every frame each tag looks at the others: an
/// opposite one within `FieldReach` drags them together (only the light ones move; a heavy one is an anchor the other
/// flies to), a like one close by shoves them apart. When an opposite pair meets they smash (both hurt and stunned)
/// and the tags are spent. Static Cling zaps the enemy while tagged; Arc Weld and Magnetar add to the smash.
/// </summary>
public partial class MagnetTag : Node3D
{
	public static readonly List<MagnetTag> All = new();
	Creature victim;
	PlayerController owner;
	PlayerWeapon weapon;
	public int Charge;
	float left, clingTick;
	MeshInstance3D ring;
	bool spent;

	public Creature Victim => victim;
	int Skill( SkillId id ) => Skills.Rank( owner, id );

	public static MagnetTag Of( Creature c ) => All.FirstOrDefault( t => t.victim == c && !t.spent && IsInstanceValid( t ) );

	public static MagnetTag Put( Creature c, int charge, PlayerController owner, PlayerWeapon weapon )
	{
		MagnetFx.Tags++;
		var tag = Of( c );
		if ( tag is null )
		{
			tag = new MagnetTag { Name = "Magnet Tag", victim = c, owner = owner, weapon = weapon };
			owner.GetTree().CurrentScene.AddChild( tag );
			All.Add( tag );
		}
		tag.Charge = charge;
		tag.left = MagnetFx.TagTime + MagnetFx.LongChargePer * tag.Skill( SkillId.LongCharge );
		tag.Paint();
		Sfx.Play( "beam_charge", c.GlobalPosition, 0.4f, charge > 0 ? 1.8f : 1.4f );
		return tag;
	}

	void Paint()
	{
		if ( ring is null )
		{
			ring = new MeshInstance3D { Name = "Ring", Mesh = new TorusMesh { InnerRadius = 0.55f, OuterRadius = 0.65f, Rings = 24, RingSegments = 8 }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			AddChild( ring );
		}
		ring.MaterialOverride = Sculpt.Glow( Charge > 0 ? MagnetFx.Red : MagnetFx.Blue, 4f );
	}

	public override void _ExitTree() => All.Remove( this );

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		if ( spent || victim is null || !IsInstanceValid( victim ) || victim.IsDead || !IsInstanceValid( owner ) ) { Spend(); return; }
		left -= dt;
		if ( left <= 0f ) { Spend(); return; }
		var centre = victim.GlobalPosition + Vector3.Up * 0.5f;
		GlobalPosition = centre;
		ring.Rotation = new Vector3( 0f, left * 4f * Charge, 0f );
		ring.Scale = Vector3.One * (1f + 0.08f * MathF.Sin( left * 12f ));
		var field = 1f + MagnetFx.StrongFieldPer * Skill( SkillId.StrongField );
		foreach ( var other in All.ToList() )
		{
			if ( other == this || other.spent || !IsInstanceValid( other ) || other.victim is null || !IsInstanceValid( other.victim ) || other.victim.IsDead ) continue;
			var to = other.victim.GlobalPosition - victim.GlobalPosition;
			var d = to.Length();
			if ( other.Charge != Charge )
			{
				if ( d < MagnetFx.SmashReach ) { Smash( other ); return; }
				if ( d > MagnetFx.FieldReach || !victim.Pullable ) continue;
				// The lighter one does most of the moving; an anchor (a boss) doesn't move at all.
				var share = other.victim.Pullable ? MathX.Clamp( other.victim.Weight / (other.victim.Weight + victim.Weight), 0.25f, 0.75f ) * 2f : 2f;
				var speed = MagnetFx.AttractSpeed * field * share * MathX.Lerp( 1.3f, 0.6f, d / MagnetFx.FieldReach );
				victim.Pull( to / d * MathF.Min( speed * dt, d * 0.5f ) );
				victim.Stun( 0.15f );
			}
			else if ( d < MagnetFx.RepelReach && d > 0.01f && victim.Pullable )
				victim.Pull( -to / d * MagnetFx.RepelSpeed * field * dt * (1f - d / MagnetFx.RepelReach) );
		}
		var cling = Skill( SkillId.StaticCling );
		if ( cling > 0 && (clingTick -= dt) <= 0f )
		{
			clingTick = 0.5f;
			PlayerPerks.Hurt( victim, MagnetFx.ClingPer * cling * 0.5f * (weapon is not null && IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f), centre, centre, owner );
		}
	}

	void Smash( MagnetTag other )
	{
		MagnetFx.Smashes++;
		var a = victim;
		var b = other.victim;
		var at = (a.GlobalPosition + b.GlobalPosition) * 0.5f + Vector3.Up * 0.5f;
		var scale = weapon is not null && IsInstanceValid( weapon ) ? weapon.ShotScale : 1f;
		var damage = MagnetFx.SmashDamage * scale * (1f + MagnetFx.HardSmashPer * Skill( SkillId.HardSmash ));
		other.Spend();
		Spend();
		foreach ( var c in new[] { a, b } )
		{
			SkillFx.HitPart( c, damage, at, owner );
			if ( !c.IsDead ) c.Stun( MagnetFx.SmashStun );
		}
		weapon?.MarkHit();
		Sfx.Play( "magnet_clang", at, 1f, 0.7f );
		Sfx.Play( "axe_hit", at, 0.7f, 0.7f );
		Shockwave.Spawn( GetTree().CurrentScene, at, 2f, MagnetFx.Steel );
		owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.3f );
		Controls.Rumble( 0.3f );
		for ( var i = 0; i < 10; i++ ) GooDroplet.Spawn( at, (Rng.OnSphere() + Vector3.Up * 0.5f).SafeNormal() * Rng.Float( 2f, 5f ), new Color( 0.7f, 0.12f, 0.12f ), Rng.Float( 1f, 2f ) );
		var upgrade = weapon is not null && IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f;
		var arc = Skill( SkillId.ArcWeld );
		if ( arc > 0 )
			foreach ( var c in Creature.Everyone.Where( c => IsInstanceValid( c ) && c.IsEnemy && !c.IsDead && c != a && c != b && c.GlobalPosition.DistanceTo( at ) < 6f ).OrderBy( c => c.GlobalPosition.DistanceTo( at ) ).Take( arc ).ToList() )
			{
				MagnetFx.Arcs++;
				var to = SkillFx.NearestPart( c, at );
				SkillFx.Zap( at, to );
				PlayerPerks.Hurt( c, MagnetFx.ArcWeldDamage( arc ) * upgrade, to, at, owner );
			}
		var star = Skill( SkillId.Magnetar );
		if ( star <= 0 ) return;
		// Magnetar: the smash pulls everything near into it and crushes it.
		MagnetFx.Magnetars++;
		var reach = MagnetFx.MagnetarReach( star );
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var d = c.GlobalPosition.DistanceTo( at );
			if ( d > reach ) continue;
			if ( c.Pullable ) c.Pull( (at - c.GlobalPosition).Flat() * 0.7f );
			if ( c != a && c != b ) SkillFx.HitPart( c, MagnetFx.MagnetarDamage( star ) * upgrade, at, owner );
			if ( !c.IsDead ) c.Stun( 0.6f );
		}
		Shockwave.Spawn( GetTree().CurrentScene, at, reach, MagnetFx.Blue );
		Sfx.Play( "kraken_slam", at, 0.7f, 1.4f );
	}

	public void Spend()
	{
		if ( spent ) return;
		spent = true;
		QueueFree();
	}
}

/// <summary> The Magnet-Bot's gun models, on the Blaster's rig like the trident shooter. </summary>
public static class MagnetModels
{
	public static readonly Vector3 ScrapMuzzle = new( 26f, 0f, 3.4f ), PolarityMuzzle = new( 28f, 0f, 3.2f );
	static readonly Color Dark = new( 0.14f, 0.15f, 0.19f );

	public static void ScrapCannon( Node3D gun )
	{
		var dark = GunKit.Paint( Dark, 0.5f, 0.4f );
		var red = GunKit.Paint( MagnetFx.Red, 0.4f, 0.2f );
		var steel = GunKit.Paint( MagnetFx.Steel, 0.25f, 0.85f );
		var copper = GunKit.Paint( new Color( 0.8f, 0.45f, 0.22f ), 0.35f, 0.8f );
		var glow = GunKit.Paint( MagnetFx.Red, 0.3f, 0f, 3f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), dark, 18f );
		// A fat copper coil body, and a big red horseshoe magnet out front with silver tips.
		GunKit.AlongX( gun, new Vector3( 5f, 0f, 3.2f ), 2.6f, 10f, dark, 2.6f, 18 );
		for ( var i = 0; i < 6; i++ ) GunKit.AlongX( gun, new Vector3( 1.5f + i * 1.4f, 0f, 3.2f ), 2.85f, 0.7f, copper, -1f, 18 );
		foreach ( var side in new[] { -1f, 1f } )
		{
			GunKit.Rod( gun, new Vector3( 11f, side * 2.2f, 3.2f ), new Vector3( 22f, side * 2.6f, 3.2f ), 1.3f, red, 14 );
			GunKit.Box( gun, new Vector3( 23.5f, side * 2.6f, 3.2f ), new Vector3( 3f, 2.6f, 2.6f ), steel );
		}
		GunKit.Rod( gun, new Vector3( 11f, -2.2f, 3.2f ), new Vector3( 11f, 2.2f, 3.2f ), 1.3f, red, 14 );
		var loaded = new Node3D { Name = "Loaded" };
		gun.AddChild( loaded );
		GunKit.Ball( loaded, new Vector3( 22f, 0f, 3.2f ), 0.9f, glow );
		GunKit.AlongX( gun, new Vector3( -3.4f, 0f, 2.5f ), 1.1f, 3.8f, dark, 1.3f, 12 );
	}

	public static void PolarityGun( Node3D gun )
	{
		var dark = GunKit.Paint( Dark, 0.5f, 0.4f );
		var red = GunKit.Paint( MagnetFx.Red, 0.4f, 0.2f );
		var blue = GunKit.Paint( MagnetFx.Blue, 0.4f, 0.2f );
		var steel = GunKit.Paint( MagnetFx.Steel, 0.25f, 0.85f );
		var redGlow = GunKit.Paint( MagnetFx.Red, 0.3f, 0f, 3f );
		var blueGlow = GunKit.Paint( MagnetFx.Blue, 0.3f, 0f, 3f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), dark, 18f );
		// Two barrels side by side, one red and one blue, ending in glowing pole tips, with a steel band joining them.
		GunKit.Box( gun, new Vector3( 5f, 0f, 2.4f ), new Vector3( 10f, 4.4f, 2.6f ), dark );
		GunKit.AlongX( gun, new Vector3( 15f, 1.3f, 3.2f ), 1.1f, 20f, red, 1.1f, 16 );
		GunKit.AlongX( gun, new Vector3( 15f, -1.3f, 3.2f ), 1.1f, 20f, blue, 1.1f, 16 );
		GunKit.Ball( gun, new Vector3( 25.5f, 1.3f, 3.2f ), 1.2f, redGlow );
		GunKit.Ball( gun, new Vector3( 25.5f, -1.3f, 3.2f ), 1.2f, blueGlow );
		foreach ( var x in new[] { 10f, 18f } ) GunKit.Box( gun, new Vector3( x, 0f, 3.2f ), new Vector3( 1.2f, 5.2f, 2.8f ), steel );
		GunKit.Box( gun, new Vector3( 6f, 0f, 5f ), new Vector3( 4f, 1f, 1.4f ), steel );
		GunKit.AlongX( gun, new Vector3( -3.4f, 0f, 2.5f ), 1.1f, 3.8f, dark, 1.3f, 12 );
	}
}
