using Godot;

/// <summary> The Hook-Bot's numbers (what one skill point does, and the base kit). </summary>
public static class HookFx
{
	// The Harpoon Reel: one harpoon out at a time; a light enemy it bites is reeled in and yanked.
	public const float HarpoonSpeed = 55f, HarpoonDrop = 4f, MaxRange = 26f, ReelSpeed = 13f, HoldDistance = 2.2f, ReelTimeout = 2.6f;
	public const float YankDamage = 45f, YankStun = 0.5f;
	public const float BarbedPer = 0.1f, QuickReelPer = 0.12f, TowPer = 15f, GutPer = 5f, GutTime = 4f, NetRadius = 3.5f;
	public static float SnagBelow( int n ) => 0.3f + 0.1f * n;
	public static int TrawlerCatches( int n ) => 1 + n;
	// The Lantern Lure: enemies go for it while it glows (it is the player's decoy), it zaps what is near, then bursts.
	public const float LureLife = 4f, ZapRadius = 2.6f, ZapDamage = 10f, ZapEvery = 0.5f, BurstRadius = 4f;
	public const float BrighterPer = 0.12f, LongGlowPer = 0.3f, ShockPer = 4f, ChumPer = 0.2f, BaitHealPer = 4f, BloomRadius = 4.5f;
	public static float BloomDamage( int n ) => 120f + 60f * n;
	// The grapple (mouse 4).
	public const float GrappleRange = 24f, GrappleCost = 35f, GrappleSpeed = 950f /* inches a second */, ZipKick = 40f;
	public const float LongCastPer = 3f, MomentumPer = 0.4f, MomentumTime = 2f, StompPer = 25f, RechargePer = 0.1f;
	public const float SeaDogPer = 5f, SeaDogTime = 3f, LeviathanRadius = 4f;
	public static float LeviathanDamage( int n ) => 100f + 60f * n;

	/// <summary> Tests: how often each thing happened. </summary>
	public static int Harpoons, Hooked, Yanks, Snags, Tows, Netted, Lures, Zaps, Bursts, Blooms, Zips, Kicks, Slams, SeaDogHeals;
	public static float LastReelStart = -1f, LastReelEnd = -1f;
}

/// <summary>
/// The Hook-Bot's guns. The Harpoon Reel (slot 1) fires a barbed harpoon on a line: a light enemy it bites is reeled
/// in to `HoldDistance` and yanked (a hit and a short stun), ready for the axe; a heavy one just takes the hit. Only one
/// harpoon is out at a time. The Lantern Lure (slot 2) lobs a glowing angler lantern: once it lands every enemy goes
/// for it instead of the player (it is `PlayerSurvivor.Decoy`), it zaps what comes near, and it bursts when its glow
/// runs out or on right click.
/// </summary>
public partial class PlayerWeapon
{
	public bool Harpoon, Lure;
	HarpoonLine line;
	LanternLure lure;
	/// <summary> A harpoon is out (flying, hooked or coming back): no next shot until it is home. </summary>
	public bool LineOut => line is not null && IsInstanceValid( line );
	public HarpoonLine Line => LineOut ? line : null;
	public LanternLure ActiveLure => lure is not null && IsInstanceValid( lure ) ? lure : null;
	/// <summary>
	/// What the damage upgrades multiply a skill effect by (zaps, jaws, shatters, turrets): half what they do for the
	/// shot itself, so the big area effects don't wipe whole late waves once the gun is fully upgraded.
	/// </summary>
	public float UpgradeScale => 1f + 0.5f * DamagePerLevel * DamageLevel;
	/// <summary> The shot's own upgrade multiplier. </summary>
	public float ShotScale => 1f + DamagePerLevel * DamageLevel;

	public static PlayerWeapon HarpoonReel( int slot = 1 ) => new()
	{
		Name = "HarpoonReel", DisplayName = "Harpoon Reel", Slot = slot, ModelName = "harpoon", Harpoon = true,
		Damage = 110f, FireRate = 1.5f, Range = HookFx.MaxRange, SpreadDegrees = 0.3f, Recoil = 2.2f,
		MagazineSize = 4, StartingReserve = 40, ReloadTime = 2f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.3f, FovKickPerShot = 2f, ShootSound = "harpoon",
		AimFovScale = 0.75f, AimSpreadScale = 0.3f,
	};

	public static PlayerWeapon LanternLure( int slot = 2 ) => new()
	{
		Name = "LanternLure", DisplayName = "Lantern Lure", Slot = slot, ModelName = "lure", Lure = true,
		Damage = 60f /* the burst */, FireRate = 1f / 8f, Range = 30f, SpreadDegrees = 0f, Recoil = 1f,
		MagazineSize = 2, StartingReserve = 16, ReloadTime = 1.5f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.15f, FovKickPerShot = 1f, ShootSound = "ink_throw",
	};

	/// <summary> Where the line leaves the gun: its muzzle in view, the robot's gun in third person, else by the eyes. </summary>
	public Vector3 LinePoint()
	{
		if ( Active && ViewModel is not null && ViewModel.Visible ) return ViewModel.Muzzle().Origin;
		if ( controller.SeenFromOutside && controller.GetNodeOrNull<PlayerRig>( "PlayerRig" )?.WorldMuzzle() is { } held ) return held.Origin;
		var basis = controller.Camera?.GlobalBasis ?? controller.EyeTransform.Basis;
		return (controller.Camera?.GlobalPosition ?? controller.EyeTransform.Origin) - basis.Z * 0.5f - basis.Y * 0.2f + basis.X * 0.15f;
	}

	void FireHarpoon( Vector3 start, Vector3 forward, float spread, Vector3 trailStart )
	{
		var direction = (forward + Rng.InSphere( 1f ) * spread).Normalized();
		var exclude = new Godot.Collections.Array<Rid> { controller.GetRid() };
		var aim = Trace.Ray( controller, start, start + direction * HookFx.MaxRange, Layers.World | Layers.Creature, exclude );
		var target = aim.Hit ? aim.HitPosition : start + direction * HookFx.MaxRange;
		// It flies from just in front of the eyes (the gun's muzzle moves with its animations); the rope is drawn from the muzzle.
		var from = controller.SeenFromOutside ? controller.EyeTransform.Origin + direction * 0.6f : start + direction * 0.3f;
		var damage = CurrentDamage * (1f + HookFx.BarbedPer * Skill( SkillId.BarbedHooks ));
		line = HarpoonLine.Fire( this, controller, from, (target - from).SafeNormal(), damage );
	}

	void ThrowLure( Vector3 start, Vector3 forward )
	{
		ActiveLure?.Burst();
		var from = controller.SeenFromOutside ? controller.EyeTransform.Origin + forward * 0.6f : start + forward * 0.5f - controller.Camera.GlobalBasis.Y * 0.15f;
		lure = global::LanternLure.Throw( this, controller, from, forward );
	}

	/// <summary> Testing aid: the lure out now bursts (as right click does). </summary>
	public void TestBurstLure() => ActiveLure?.Burst();
}

/// <summary>
/// A harpoon on its line. It flies with a little drop; an enemy it bites takes the hit and, if light enough to drag
/// (`Creature.Pullable`), is reeled in to the player and yanked; a heavy one just takes the hit. Then the harpoon
/// comes home. Trawler nets enemies near the first and reels them in together; Tow Line bowls the reeled ones through
/// whatever they pass; Snag tears off a weak limb as the reel starts; Gutting Hook makes them bleed.
/// </summary>
public partial class HarpoonLine : Node3D
{
	enum State { Flying, Reeling, Stuck, Returning }
	State state;
	PlayerController owner;
	PlayerWeapon weapon;
	Vector3 velocity, hookOffset;
	float damage, travelled, stateAge;
	Creature target;
	readonly List<Creature> netted = new();
	readonly HashSet<Creature> towed = new();
	readonly List<MeshInstance3D> netLines = new();
	MeshInstance3D rope;
	Node3D head;
	Godot.Collections.Array<Rid> exclude;

	public bool Reeling => state == State.Reeling;
	public Creature Target => target;

	int Skill( SkillId id ) => Skills.Rank( owner, id );

	static StandardMaterial3D ropeMaterial;
	static StandardMaterial3D RopeMaterial => ropeMaterial ??= new StandardMaterial3D { AlbedoColor = new Color( 0.86f, 0.74f, 0.5f ), Roughness = 0.8f };

	public static HarpoonLine Fire( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 direction, float damage )
	{
		HookFx.Harpoons++;
		var h = new HarpoonLine { Name = "Harpoon", weapon = weapon, owner = owner, velocity = direction * HookFx.HarpoonSpeed, damage = damage };
		owner.GetTree().CurrentScene.AddChild( h );
		h.GlobalPosition = from;
		h.exclude = new Godot.Collections.Array<Rid> { owner.GetRid() };
		h.Build();
		return h;
	}

	void Build()
	{
		head = new Node3D { Name = "Head" };
		AddChild( head );
		// Built along -Z: a brass shaft, a steel point with two barbs, and a ring for the rope.
		var steel = GunKit.Paint( new Color( 0.75f, 0.78f, 0.82f ), 0.25f, 0.8f );
		var brass = GunKit.Paint( new Color( 0.8f, 0.62f, 0.28f ), 0.35f, 0.6f );
		GunKit.Rod( head, new Vector3( 0f, 0f, 0.45f ), new Vector3( 0f, 0f, -0.15f ), 0.016f, brass );
		GunKit.Spike( head, new Vector3( 0f, 0f, -0.15f ), new Vector3( 0f, 0f, -0.36f ), 0.035f, steel );
		foreach ( var side in new[] { -1f, 1f } )
			GunKit.Spike( head, new Vector3( 0f, 0f, -0.2f ), new Vector3( side * 0.07f, 0f, -0.08f ), 0.014f, steel, 6 );
		rope = GunKit.Line( this, RopeMaterial, 0.008f );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		stateAge += dt;
		if ( !IsInstanceValid( owner ) || !IsInstanceValid( weapon ) ) { QueueFree(); return; }
		switch ( state )
		{
			case State.Flying: Fly( dt ); break;
			case State.Reeling: Reel( dt ); break;
			case State.Stuck: if ( stateAge > 0.3f ) Return(); break;
			case State.Returning:
				var home = weapon.LinePoint();
				var to = home - GlobalPosition;
				var step = 70f * dt;
				if ( to.Length() <= step ) { QueueFree(); return; }
				GlobalPosition += to.Normalized() * step;
				break;
		}
		if ( !IsInstanceValid( this ) || IsQueuedForDeletion() ) return;
		var muzzle = weapon.LinePoint();
		GunKit.Lay( rope, muzzle, GlobalPosition );
		var along = state == State.Returning ? muzzle - GlobalPosition : GlobalPosition - muzzle;
		if ( state == State.Flying ) along = velocity;
		if ( along.LengthSquared() > 1e-6f ) head.GlobalBasis = Shapes.LookAt( state == State.Returning ? -along : along );
		for ( var i = 0; i < netLines.Count; i++ )
			if ( i < netted.Count && IsInstanceValid( netted[i] ) && !netted[i].IsDead ) GunKit.Lay( netLines[i], GlobalPosition, SkillFx.NearestPart( netted[i], GlobalPosition ) );
			else netLines[i].Visible = false;
	}

	void Fly( float dt )
	{
		velocity += Vector3.Down * HookFx.HarpoonDrop * dt;
		var at = GlobalPosition;
		var step = velocity * dt;
		var tr = Trace.Ray( owner, at, at + step, Layers.World | Layers.Creature, exclude );
		if ( tr.Hit ) { Hit( tr ); return; }
		GlobalPosition = at + step;
		travelled += step.Length();
		if ( travelled > HookFx.MaxRange ) Return();
		if ( WaterFeel.IsOn() && Rng.Float() < 0.5f ) WaterBubble.Create( at, Rng.Float( 0.3f, 0.6f ), 0f, Rng.Float( 0.3f, 0.6f ) );
	}

	void Hit( TraceResult tr )
	{
		GlobalPosition = tr.HitPosition;
		var shootable = Shootables.Find( tr.Node );
		if ( shootable is GlowBulb bulb ) { bulb.ReceiveShot( damage, tr.Node, velocity.Normalized(), tr.HitPosition, owner.GlobalPosition, owner, true ); Return(); return; }
		if ( shootable?.ShotOwner is Creature { IsEnemy: true } c && !shootable.IsDead && !c.IsDead )
		{
			HookFx.Hooked++;
			weapon.MarkHit();
			Sfx.Play( "trident_flesh", tr.HitPosition, 0.8f, 0.9f );
			Fx.ImpactFlesh( tr.HitPosition, -velocity.Normalized(), new Color( 0.9f, 0.3f, 0.2f ) );
			shootable.ReceiveShot( damage, tr.Node, velocity.Normalized(), tr.HitPosition, owner.GlobalPosition, owner, true );
			if ( c.IsDead ) { weapon.MarkKill(); Return(); return; }
			var gut = Skill( SkillId.GuttingHook );
			if ( gut > 0 ) Affliction.Apply( c, Affliction.Kind.Bleed, HookFx.GutPer * gut, HookFx.GutTime, owner );
			if ( !c.Pullable ) { state = State.Stuck; stateAge = 0f; target = c; return; }
			target = c;
			hookOffset = tr.HitPosition - c.GlobalPosition;
			state = State.Reeling;
			stateAge = 0f;
			HookFx.LastReelStart = (target.GlobalPosition - owner.GlobalPosition).Length();
			Sfx.Play2D( "trident_charge", 0.4f, 0.7f, Sfx.GunBus );
			Net();
			Snag( c );
			return;
		}
		if ( tr.Node is SeaMineShell ) SeaMine.Tink( tr.HitPosition, tr.Normal );
		else Fx.ImpactWorld( tr.HitPosition, tr.Normal );
		Sfx.Play( "hit_world", tr.HitPosition, 0.6f, 0.8f );
		state = State.Stuck;
		stateAge = 0f;
	}

	/// <summary> Trawler: the harpoon bursts into a net, and the enemies near the first one are reeled in with it. </summary>
	void Net()
	{
		var n = Skill( SkillId.Trawler );
		if ( n <= 0 ) return;
		var near = Creature.Everyone.Where( c => IsInstanceValid( c ) && c != target && c.IsEnemy && !c.IsDead && c.Pullable && c.GlobalPosition.DistanceTo( target.GlobalPosition ) < HookFx.NetRadius )
			.OrderBy( c => c.GlobalPosition.DistanceTo( target.GlobalPosition ) ).Take( HookFx.TrawlerCatches( n ) );
		foreach ( var c in near )
		{
			netted.Add( c );
			netLines.Add( GunKit.Line( this, RopeMaterial, 0.006f ) );
			HookFx.Netted++;
		}
		if ( netted.Count > 0 ) Sfx.Play( "bubble_trap", target.GlobalPosition, 0.6f, 0.8f );
	}

	/// <summary> Snag: a limb already shot down this far is torn off as the reel starts. </summary>
	void Snag( Creature c )
	{
		var n = Skill( SkillId.Snag );
		if ( n <= 0 || c is not PartCreature body ) return;
		foreach ( var limb in body.Limbs() )
		{
			if ( limb.Health > limb.MaxHealth * HookFx.SnagBelow( n ) ) continue;
			HookFx.Snags++;
			var at = limb.Go.GlobalPosition;
			c.ReceiveShot( limb.Index, limb.Health + 1f, (owner.GlobalPosition - at).SafeNormal(), at, owner.GlobalPosition, owner.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ), true );
			Sfx.Play( "gib", at, 1f, 0.8f );
			return;
		}
	}

	void Reel( float dt )
	{
		if ( target is null || !IsInstanceValid( target ) || target.IsDead ) { Return(); return; }
		var speed = HookFx.ReelSpeed * (1f + HookFx.QuickReelPer * Skill( SkillId.QuickReel ));
		var me = owner.GlobalPosition;
		var tow = Skill( SkillId.TowLine );
		foreach ( var c in netted.Prepend( target ) )
		{
			if ( !IsInstanceValid( c ) || c.IsDead ) continue;
			var to = me - c.GlobalPosition;
			to.Y *= 0.5f;
			var d = to.Length();
			if ( d > HookFx.HoldDistance ) c.Pull( to / d * MathF.Min( speed * dt, d - HookFx.HoldDistance ) );
			if ( tow <= 0 ) continue;
			// Tow Line: whatever it is dragged through is bowled aside and hurt (once each a reel).
			foreach ( var other in Creature.Everyone.ToList() )
			{
				if ( !IsInstanceValid( other ) || other == c || other.IsDead || !other.IsEnemy || towed.Contains( other ) || netted.Contains( other ) || other == target ) continue;
				if ( other.GlobalPosition.DistanceTo( c.GlobalPosition ) > 1.3f ) continue;
				towed.Add( other );
				HookFx.Tows++;
				SkillFx.HitPart( other, HookFx.TowPer * tow * weapon.UpgradeScale, c.GlobalPosition, owner );
				SkillFx.Shove( other, (other.GlobalPosition - c.GlobalPosition).Flat().SafeNormal() * 1.4f );
			}
		}
		GlobalPosition = target.GlobalPosition + hookOffset;
		// The yank waits for everything on the line (the net's catch too) to come in.
		var left = netted.Prepend( target ).Where( c => IsInstanceValid( c ) && !c.IsDead ).Max( c => (me - c.GlobalPosition).Flat().Length() );
		if ( left <= HookFx.HoldDistance + 0.35f || stateAge > HookFx.ReelTimeout ) Yank();
	}

	/// <summary> The reel's end: a hard yank on everything on the line, which stuns it a moment. </summary>
	void Yank()
	{
		HookFx.LastReelEnd = (target.GlobalPosition - owner.GlobalPosition).Flat().Length();
		foreach ( var c in netted.Prepend( target ) )
		{
			if ( !IsInstanceValid( c ) || c.IsDead ) continue;
			HookFx.Yanks++;
			SkillFx.HitPart( c, HookFx.YankDamage * weapon.ShotScale, owner.GlobalPosition, owner );
			if ( !c.IsDead ) c.Stun( HookFx.YankStun );
			else weapon.MarkKill();
		}
		weapon.MarkHit();
		Sfx.Play( "stab_squish_1", GlobalPosition, 0.8f );
		owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.25f );
		Controls.Rumble( 0.35f );
		Return();
	}

	void Return()
	{
		state = State.Returning;
		stateAge = 0f;
		target = null;
		netted.Clear();
		foreach ( var l in netLines ) l.Visible = false;
	}
}

/// <summary>
/// The Lantern Lure: an angler's lantern on a weighted stalk. Thrown in an arc, it settles on the floor; while it
/// glows it is the player's decoy (enemies go for it and their hits on it don't land), it zaps enemies within
/// `ZapRadius`, and when its glow runs out (or on right click, or another is thrown) it bursts. Abyssal Bloom makes the
/// burst a giant angler jaw (`AbyssJaw`).
/// </summary>
public partial class LanternLure : DecoyBase
{
	PlayerController owner;
	PlayerWeapon weapon;
	Vector3 velocity;
	bool planted, burst;
	float age, flight, life, zapTick, burstDamage;
	OmniLight3D light;
	MeshInstance3D bulb;
	Node3D stalk;

	public override bool Gone => !planted || burst;
	public bool Planted => planted;
	int Skill( SkillId id ) => Skills.Rank( owner, id );
	float Brighter => 1f + HookFx.BrighterPer * Skill( SkillId.BrighterBulb );

	static readonly Color Glow = new( 0.55f, 1f, 0.45f );

	public static LanternLure Throw( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 forward )
	{
		HookFx.Lures++;
		var l = new LanternLure { Name = "Lantern Lure", weapon = weapon, owner = owner };
		l.velocity = forward * 15f + Vector3.Up * 3.5f;
		l.life = HookFx.LureLife + HookFx.LongGlowPer * l.Skill( SkillId.LongGlow );
		l.burstDamage = weapon.CurrentDamage * (1f + HookFx.ChumPer * l.Skill( SkillId.ChumBucket ));
		owner.GetTree().CurrentScene.AddChild( l );
		l.GlobalPosition = from;
		l.Build();
		return l;
	}

	void Build()
	{
		stalk = new Node3D { Name = "Stalk", Scale = Vector3.One * 1.7f };
		AddChild( stalk );
		var dark = GunKit.Paint( new Color( 0.08f, 0.1f, 0.16f ), 0.6f );
		var fin = GunKit.Paint( new Color( 0.12f, 0.45f, 0.5f ), 0.5f );
		Sculpt.Cone( stalk, Vector3.Zero, new Vector3( 0f, 0.12f, 0f ), 0.16f, dark, 10, "Weight" );
		Sculpt.Limb( stalk, new Vector3( 0f, 0.08f, 0f ), new Vector3( 0.05f, 0.42f, 0f ), 0.03f, 0.02f, fin, "Stalk" );
		Sculpt.Limb( stalk, new Vector3( 0.05f, 0.42f, 0f ), new Vector3( 0.18f, 0.58f, 0f ), 0.02f, 0.016f, fin, "Stalk Tip" );
		bulb = Sculpt.Blob( stalk, new Vector3( 0.2f, 0.56f, 0f ), Vector3.One * 0.2f, Sculpt.Glow( Glow, 5f ), null, "Bulb" );
		Sculpt.Blob( stalk, new Vector3( 0.2f, 0.56f, 0f ), Vector3.One * 0.34f, SkillFx.Rim( Glow, 1.5f ), null, "Halo" );
		light = new OmniLight3D { LightColor = Glow, LightEnergy = 2.2f, OmniRange = 5f, ShadowEnabled = false, Position = new Vector3( 0.2f, 0.6f, 0f ) };
		stalk.AddChild( light );
	}

	/// <summary> An enemy struck it: its glow drains faster (a crowd chews through it). </summary>
	public override void Struck()
	{
		age += 0.25f;
		LightningArc.Spark( GetTree().CurrentScene, bulb.GlobalPosition );
		light.LightEnergy = 0.5f;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( burst ) return;
		if ( !IsInstanceValid( owner ) ) { QueueFree(); return; }
		if ( !planted ) { Fly( dt ); return; }
		age += dt;
		var glow = 0.75f + 0.25f * MathF.Sin( age * 7f );
		light.LightEnergy = MathX.Lerp( light.LightEnergy, 2.4f * glow * Brighter, 1f - MathF.Exp( -8f * dt ) );
		stalk.Rotation = new Vector3( 0f, age * 0.6f, MathF.Sin( age * 2.3f ) * 0.12f );
		if ( (zapTick -= dt) <= 0f ) { zapTick = HookFx.ZapEvery; Zap(); }
		if ( age >= life ) Burst();
	}

	void Fly( float dt )
	{
		flight += dt;
		velocity += Vector3.Down * 9f * dt;
		velocity *= MathF.Exp( -0.6f * dt );
		var at = GlobalPosition;
		var step = velocity * dt;
		var tr = Trace.Sphere( owner, 0.12f, at, at + step, Layers.World | Layers.Creature, new Godot.Collections.Array<Rid> { owner.GetRid() } );
		if ( tr.Hit || flight > 3f ) { Plant( tr.Hit ? tr.HitPosition : at ); return; }
		GlobalPosition = at + step;
		stalk.Rotation += new Vector3( 6f * dt, 0f, 4f * dt );
	}

	void Plant( Vector3 at )
	{
		var floor = Trace.Ray( owner, at + Vector3.Up * 0.3f, at + Vector3.Down * 30f, Layers.World, new Godot.Collections.Array<Rid> { owner.GetRid() } );
		GlobalPosition = floor.Hit ? floor.HitPosition : at;
		stalk.Rotation = Vector3.Zero;
		planted = true;
		Sfx.Play( "land", GlobalPosition, 0.5f, 1.4f );
		Sfx.Play( "beam_charge", GlobalPosition, 0.35f, 1.6f );
		GpuFx.SandPuff( GlobalPosition + Vector3.Up * 0.1f, Vector3.Up * 0.6f, new Color( 0.8f, 0.75f, 0.6f, 0.7f ), 8f, 0.8f );
		if ( owner.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) is { } survivor )
		{
			if ( survivor.Decoy is { } old && old != this && IsInstanceValid( old ) && old is not LanternLure ) old.QueueFree();
			survivor.Decoy = this;
		}
	}

	void Zap()
	{
		var at = GlobalPosition + Vector3.Up * 0.55f;
		var reach = HookFx.ZapRadius * Brighter;
		var damage = (HookFx.ZapDamage + HookFx.ShockPer * Skill( SkillId.ShockLantern )) * weapon.UpgradeScale;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var part = SkillFx.NearestPart( c, at );
			if ( part.DistanceTo( at ) > reach ) continue;
			HookFx.Zaps++;
			SkillFx.Zap( at, part );
			PlayerPerks.Hurt( c, damage, part, at, owner );
		}
	}

	/// <summary> Its glow is spent (or right click): it bursts, and stops being the decoy. </summary>
	public void Burst()
	{
		if ( burst ) return;
		burst = true;
		HookFx.Bursts++;
		var at = GlobalPosition + Vector3.Up * 0.5f;
		var parent = GetTree().CurrentScene;
		if ( owner.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) is { } survivor && survivor.Decoy == this ) survivor.Decoy = null;
		var bloom = Skill( SkillId.AbyssalBloom );
		if ( bloom > 0 && planted ) AbyssJaw.Spawn( owner, GlobalPosition, HookFx.BloomDamage( bloom ) * weapon.UpgradeScale, this );
		else
		{
			var hits = 0;
			var radius = HookFx.BurstRadius * Brighter;
			foreach ( var c in Creature.Everyone.ToList() )
			{
				if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
				var part = SkillFx.NearestPart( c, at );
				var d = part.DistanceTo( at );
				if ( d > radius ) continue;
				hits++;
				SkillFx.HitPart( c, burstDamage * (1f - 0.4f * d / radius), at, owner );
				SkillFx.Shove( c, (c.GlobalPosition - at).Flat().SafeNormal() * 1.2f );
			}
			Heal( hits );
			Shockwave.Spawn( parent, at, radius, Glow );
			for ( var i = 0; i < 6; i++ ) SkillFx.Zap( at, at + Rng.OnSphere() * radius * Rng.Float( 0.5f, 1f ) );
			Sfx.Play( "glass_break", at, 0.8f, 1.3f );
			Sfx.Play( "bubble_burst", at, 0.8f, 0.8f );
		}
		if ( Skill( SkillId.ChumBucket ) > 0 )
			for ( var i = 0; i < 18; i++ ) GooDroplet.Spawn( at, (Rng.OnSphere() + Vector3.Up).SafeNormal() * Rng.Float( 3f, 7f ), new Color( 0.7f, 0.1f, 0.12f ), Rng.Float( 1.5f, 3f ) );
		QueueFree();
	}

	/// <summary> Bait and Switch: health back for each enemy the burst (or the jaw) hit. </summary>
	public void Heal( int hits )
	{
		var n = Skill( SkillId.BaitAndSwitch );
		if ( n <= 0 || hits <= 0 || owner.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.Health is not { } h ) return;
		h.Heal( HookFx.BaitHealPer * n * hits );
	}
}

/// <summary>
/// Abyssal Bloom: a giant angler's jaw bursts up out of the sand where the lure was, gaping, drags in what is near,
/// snaps shut on it (`HookFx.BloomDamage`), and sinks back down.
/// </summary>
public partial class AbyssJaw : Node3D
{
	const float Rise = 0.35f, Snap = 0.6f, Hold = 0.95f, End = 1.5f;
	PlayerController owner;
	LanternLure from;
	float damage, age;
	bool snapped;
	Node3D left, right, lantern;
	OmniLight3D glow;

	public static void Spawn( PlayerController owner, Vector3 at, float damage, LanternLure from )
	{
		HookFx.Blooms++;
		var j = new AbyssJaw { Name = "Abyss Jaw", owner = owner, damage = damage, from = from };
		owner.GetTree().CurrentScene.AddChild( j );
		j.ground = at.Y;
		j.GlobalPosition = at + Vector3.Down * 4f;
		j.GlobalBasis = new Basis( Vector3.Up, owner.EyeAngles.Yaw * MathF.PI / 180f );
		j.Build();
		Sfx.Play( "kraken_rise", at, 0.9f, 1.3f );
		GpuFx.SandPuff( at + Vector3.Up * 0.3f, Vector3.Up * 2f, new Color( 0.8f, 0.72f, 0.55f, 0.85f ), 30f, 1.2f );
	}

	void Build()
	{
		var r = HookFx.BloomRadius;
		var skin = GunKit.Paint( new Color( 0.1f, 0.28f, 0.38f ), 0.55f );
		var spots = GunKit.Paint( new Color( 0.5f, 0.25f, 0.65f ), 0.5f );
		var gum = GunKit.Paint( new Color( 0.45f, 0.08f, 0.12f ), 0.6f );
		var tooth = GunKit.Paint( new Color( 0.95f, 0.93f, 0.85f ), 0.3f );
		var eye = Sculpt.Glow( new Color( 0.6f, 1f, 0.4f ), 3f );
		foreach ( var side in new[] { -1f, 1f } )
		{
			// Each half hinges at the bottom of the middle line and swings out to open.
			var half = new Node3D { Name = side < 0f ? "Left Jaw" : "Right Jaw" };
			AddChild( half );
			Sculpt.Blob( half, new Vector3( side * r * 0.5f, 0.2f, 0f ), new Vector3( r * 1.0f, 3.2f, r * 1.9f ), skin, null, "Shell" );
			Sculpt.Blob( half, new Vector3( side * r * 0.12f, 0.8f, 0f ), new Vector3( r * 0.3f, 1.2f, r * 1.6f ), gum, null, "Gum" );
			for ( var i = 0; i < 5; i++ )
				Sculpt.Blob( half, new Vector3( side * r * Rng.Float( 0.5f, 0.85f ), Rng.Float( 0.6f, 1.6f ), Rng.Float( -0.7f, 0.7f ) * r ), Vector3.One * Rng.Float( 0.3f, 0.55f ), spots, null, "Spot" );
			for ( var i = 0; i < 9; i++ )
			{
				var z = MathX.Lerp( -0.8f, 0.8f, i / 8f ) * r;
				var len = MathX.Lerp( 0.7f, 1.3f, 1f - MathF.Abs( z / r ) ) * (i % 2 == 0 ? 1f : 0.7f);
				Sculpt.Cone( half, new Vector3( side * 0.25f, 1.45f, z ), new Vector3( -side * 0.35f, 1.45f + len, z ), 0.16f, tooth, 8, "Tooth" );
			}
			Sculpt.Blob( half, new Vector3( side * r * 0.75f, 1.4f, -r * 0.55f ), Vector3.One * 0.55f, eye, null, "Eye" );
			if ( side < 0f ) left = half; else right = half;
		}
		lantern = new Node3D { Name = "Lantern" };
		AddChild( lantern );
		Sculpt.Limb( lantern, new Vector3( 0f, 1.6f, -r * 0.9f ), new Vector3( 0f, 3.4f, -r * 0.4f ), 0.12f, 0.07f, skin, "Stalk" );
		Sculpt.Blob( lantern, new Vector3( 0f, 3.5f, -r * 0.25f ), Vector3.One * 0.6f, Sculpt.Glow( new Color( 0.55f, 1f, 0.45f ), 5f ), null, "Bulb" );
		glow = new OmniLight3D { LightColor = new Color( 0.55f, 1f, 0.45f ), LightEnergy = 4f, OmniRange = 9f, ShadowEnabled = false, Position = new Vector3( 0f, 3.5f, -r * 0.25f ) };
		AddChild( glow );
	}

	float Open( float t )
	{
		if ( t < Rise ) return 1f;
		if ( t < Snap ) return 1f;
		if ( t < Snap + 0.08f ) return 1f - (t - Snap) / 0.08f;
		return 0f;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		age += dt;
		var r = HookFx.BloomRadius;
		// Up out of the sand, then down again after the bite.
		var up = age < Rise ? 1f - MathF.Pow( 1f - age / Rise, 3f ) : age < Hold ? 1f : 1f - MathX.Clamp( (age - Hold) / (End - Hold), 0f, 1f );
		var floorY = ground;
		GlobalPosition = GlobalPosition with { Y = floorY - 3.5f * (1f - up) - 0.6f };
		var open = Open( age );
		left.Rotation = new Vector3( 0f, 0f, MathX.Rad( 65f * open ) );
		right.Rotation = new Vector3( 0f, 0f, -MathX.Rad( 65f * open ) );
		glow.LightEnergy = 4f * up;
		var centre = GlobalPosition with { Y = floorY } + Vector3.Up * 0.8f;
		// Drag what is near into the mouth while it gapes.
		if ( age > Rise * 0.5f && age < Snap )
			foreach ( var c in Creature.Everyone.ToList() )
			{
				if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || !c.Pullable ) continue;
				var to = (centre - c.GlobalPosition).Flat();
				var d = to.Length();
				if ( d > r * 1.7f || d < 0.4f ) continue;
				c.Pull( to / d * MathF.Min( d - 0.3f, 14f * dt ) );
			}
		if ( !snapped && age >= Snap + 0.06f )
		{
			snapped = true;
			var hits = 0;
			foreach ( var c in Creature.Everyone.ToList() )
			{
				if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
				if ( (SkillFx.NearestPart( c, centre ) - centre).Flat().Length() > r ) continue;
				hits++;
				SkillFx.HitPart( c, damage, centre + Vector3.Up, owner );
				if ( !c.IsDead ) c.Stun( 0.6f );
			}
			if ( IsInstanceValid( from ) ) from.Heal( hits );
			else if ( Skills.Rank( owner, SkillId.BaitAndSwitch ) is var bait && bait > 0 ) owner.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.Health?.Heal( HookFx.BaitHealPer * bait * hits );
			Sfx.Play( "crab_snap", centre, 1f, 0.55f );
			Sfx.Play( "kraken_slam", centre, 0.8f, 1.3f );
			owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.6f );
			Controls.Rumble( 0.6f );
			Shockwave.Spawn( GetTree().CurrentScene, centre, r * 1.2f, new Color( 0.55f, 1f, 0.45f ) );
			for ( var i = 0; i < 20; i++ ) GooDroplet.Spawn( centre + Vector3.Up * 1.5f, (Rng.OnSphere() + Vector3.Up).SafeNormal() * Rng.Float( 3f, 8f ), new Color( 0.75f, 0.08f, 0.1f ), Rng.Float( 1.5f, 3.5f ) );
		}
		if ( age >= End ) QueueFree();
	}
	float ground;
}

/// <summary> The Hook-Bot's gun models, on the Blaster's rig like the trident shooter. </summary>
public static class HookModels
{
	public static readonly Vector3 HarpoonMuzzle = new( 34f, 0f, 3.2f ), LureMuzzle = new( 22f, 0f, 5f );
	static readonly Color Navy = new( 0.08f, 0.12f, 0.22f ), Orange = new( 0.95f, 0.5f, 0.16f ), Rope = new( 0.86f, 0.74f, 0.5f ), Brass = new( 0.8f, 0.64f, 0.3f );

	public static void HarpoonGun( Node3D gun )
	{
		var navy = GunKit.Paint( Navy, 0.5f );
		var orange = GunKit.Paint( Orange, 0.45f );
		var brass = GunKit.Paint( Brass, 0.3f, 0.7f );
		var rope = GunKit.Paint( Rope, 0.85f );
		var steel = GunKit.Paint( new Color( 0.75f, 0.78f, 0.82f ), 0.25f, 0.8f );
		var wood = GunKit.Paint( new Color( 0.45f, 0.28f, 0.15f ), 0.7f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), wood, 18f );
		GunKit.Box( gun, new Vector3( 2.4f, 0f, -2.1f ), new Vector3( 4.2f, 0.6f, 0.5f ), brass );
		// A long navy tube with orange bands, a reel of rope on the side, and the harpoon itself sat in the mouth.
		GunKit.AlongX( gun, new Vector3( 12f, 0f, 3.2f ), 1.7f, 26f, navy, 1.5f, 18 );
		foreach ( var x in new[] { 2f, 14f, 24f } ) GunKit.AlongX( gun, new Vector3( x, 0f, 3.2f ), 1.85f, 1.2f, orange, -1f, 18 );
		GunKit.Box( gun, new Vector3( 4f, 0f, 0.6f ), new Vector3( 9f, 2.2f, 2.2f ), navy );
		var reel = GunKit.Part( gun, new CylinderMesh { TopRadius = 3f, BottomRadius = 3f, Height = 1.6f, RadialSegments = 20 }, brass, new Vector3( 6f, -3.2f, 2.2f ), null, "Reel" );
		reel.Name = "Reel";
		GunKit.Part( gun, new CylinderMesh { TopRadius = 2.5f, BottomRadius = 2.5f, Height = 1.7f, RadialSegments = 20 }, rope, new Vector3( 6f, -3.2f, 2.2f ), null, "Rope Coil" );
		GunKit.Rod( gun, new Vector3( 6f, -4.2f, 2.2f ), new Vector3( 6f, -4.2f, 5.2f ), 0.3f, brass );
		var loaded = new Node3D { Name = "Loaded" };
		gun.AddChild( loaded );
		GunKit.Rod( loaded, new Vector3( 20f, 0f, 3.2f ), new Vector3( 33f, 0f, 3.2f ), 0.45f, brass );
		GunKit.Spike( loaded, new Vector3( 33f, 0f, 3.2f ), new Vector3( 38f, 0f, 3.2f ), 0.9f, steel );
		foreach ( var side in new[] { -1f, 1f } ) GunKit.Spike( loaded, new Vector3( 34f, 0f, 3.2f ), new Vector3( 32f, side * 1.6f, 3.2f ), 0.3f, steel, 6 );
		GunKit.AlongX( gun, new Vector3( -3.5f, 0f, 2.4f ), 1.1f, 4f, wood, 1.4f, 12 );
		GunKit.Box( gun, new Vector3( 8f, 0f, 5.6f ), new Vector3( 1.2f, 0.6f, 1.6f ), brass );
	}

	public static void LureLauncher( Node3D gun )
	{
		var navy = GunKit.Paint( Navy, 0.5f );
		var orange = GunKit.Paint( Orange, 0.45f );
		var brass = GunKit.Paint( Brass, 0.3f, 0.7f );
		var fin = GunKit.Paint( new Color( 0.12f, 0.45f, 0.5f ), 0.5f );
		var glow = GunKit.Paint( new Color( 0.55f, 1f, 0.45f ), 0.3f, 0f, 4f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), navy, 18f );
		// A brass lantern frame on a short orange body, with the next lure's bulb glowing on a curled stalk.
		GunKit.AlongX( gun, new Vector3( 6f, 0f, 3.2f ), 2.4f, 12f, orange, 2f, 18 );
		GunKit.Ball( gun, new Vector3( 0f, 0f, 3.2f ), 2.4f, orange );
		GunKit.AlongX( gun, new Vector3( 14f, 0f, 3.2f ), 1.6f, 5f, brass, 2.4f, 18 );
		foreach ( var y in new[] { -1.6f, 1.6f } ) GunKit.Rod( gun, new Vector3( 12f, y, 5.4f ), new Vector3( 22f, y, 5.4f ), 0.3f, brass );
		var loaded = new Node3D { Name = "Loaded" };
		gun.AddChild( loaded );
		GunKit.Rod( loaded, new Vector3( 12f, 0f, 5f ), new Vector3( 17f, 0f, 8.5f ), 0.4f, fin );
		GunKit.Rod( loaded, new Vector3( 17f, 0f, 8.5f ), new Vector3( 21f, 0f, 7f ), 0.35f, fin );
		GunKit.Ball( loaded, new Vector3( 21.5f, 0f, 6f ), 1.6f, glow );
		GunKit.AlongX( gun, new Vector3( -3.2f, 0f, 2.6f ), 1f, 3.5f, navy, 1.3f, 12 );
	}
}
