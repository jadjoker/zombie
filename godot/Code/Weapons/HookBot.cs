using Godot;

/// <summary> The Hook-Bot's numbers (what one skill point does, and the base kit). </summary>
public static class HookFx
{
	// The Grab Reel: hold to grab and carry a light enemy, swing it into things, let go to fling it, right click to throw.
	public const float GrabRange = 18f, AimWidth = 0.9f, HoldDistance = 4.2f, HoldMin = 2.6f, HoldMax = 6f;
	public const float Follow = 16f, CarryCap = 28f;          // how hard the held enemy chases the hold point (1/s), its top speed (m/s)
	public const float ImpactSpeed = 6f, ImpactRef = 14f;     // m/s it was really moving: below this nothing is hurt; at ImpactRef a blow does the gun's damage
	public const float ImpactMax = 2f, ImpactEvery = 0.3f;    // the most a blow can be worth (x damage); a held enemy's slams this far apart
	public const float ThrowSpeed = 24f, FlingCap = 22f, Gravity = 7f, Drag = 0.35f, FlightMax = 4f;
	public const float BowlRadius = 1.1f, BowlShare = 0.6f, BowlSelf = 0.5f, LandStun = 0.6f;
	public const float BeamDps = 0.9f, BeamTick = 0.25f;     // heavy enemies: x damage a second while the beam is on them
	public const float ChargeRegen = 1.6f;                   // seconds per throw charge back
	// Catch of the Day.
	public const float HeavyHandsPer = 0.12f, LongArmPer = 2f, StrongArmPer = 0.12f, CrusherPer = 6f, BowlingPer = 0.2f;
	public static float DepthDamage( int n ) => 80f + 50f * n;
	public const float DepthRadius = 3.5f;
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
	public static int Grabs, GrabSlams, Bowled, Flings, Throws, Landings, Beams, DepthCharges, Lures, Zaps, Bursts, Blooms, Zips, Kicks, Slams, SeaDogHeals;
	public static float BeamDamage;
}

/// <summary>
/// The Hook-Bot's guns. The Grab Reel (slot 1, user's design after a physics gun): hold left click on a light enemy
/// (`Creature.Pullable`) to lift and carry it on a beam; swing the view and it slams into the ground, walls and other
/// enemies (damage by speed); let go and it flies on with the swing; right click while holding throws it hard (a
/// throw charge each, they come back over time). A heavy enemy can't be lifted: the beam locks on and hurts it while
/// held. The Lantern Lure (slot 2) lobs a glowing angler lantern: once it lands every enemy goes for it instead of the
/// player (it is `PlayerSurvivor.Decoy`), it zaps what comes near, and it bursts when its glow runs out or on right click.
/// </summary>
public partial class PlayerWeapon
{
	public bool Grabber, Lure;
	LanternLure lure;
	GrabbedBody held;
	Creature beamOn;
	MeshInstance3D beam;
	float beamTick, chargeBack;
	bool grabbing;
	/// <summary> The enemy on the Grab Reel's beam (lifted), or null. </summary>
	public GrabbedBody Held => held is not null && IsInstanceValid( held ) && held.IsHeld ? held : null;
	/// <summary> The heavy enemy the beam is locked on (not lifted), or null. </summary>
	public Creature BeamTarget => beamOn is not null && IsInstanceValid( beamOn ) && !beamOn.IsDead ? beamOn : null;
	public LanternLure ActiveLure => lure is not null && IsInstanceValid( lure ) ? lure : null;
	/// <summary> Tests: hold the trigger down (as if left click were held) and press right click (a throw). </summary>
	public bool ForceGrab;
	bool forceThrow;
	public void TestThrow() => forceThrow = true;
	/// <summary>
	/// What the damage upgrades multiply a skill effect by (zaps, jaws, shatters, turrets): half what they do for the
	/// shot itself, so the big area effects don't wipe whole late waves once the gun is fully upgraded.
	/// </summary>
	public float UpgradeScale => 1f + 0.5f * DamagePerLevel * DamageLevel;
	/// <summary> The shot's own upgrade multiplier. </summary>
	public float ShotScale => 1f + DamagePerLevel * DamageLevel;
	/// <summary> A blow from the Grab Reel (a slam, a bowl, a landing) at `speed` m/s: the gun's damage at ImpactRef, more with Heavy Hands. </summary>
	public float ImpactDamage( float speed ) => CurrentDamage * MathX.Clamp( speed / HookFx.ImpactRef, 0f, HookFx.ImpactMax ) * (1f + HookFx.HeavyHandsPer * Skill( SkillId.HeavyHands ));
	public float GrabReach => HookFx.GrabRange + HookFx.LongArmPer * Skill( SkillId.LongArm );

	public static PlayerWeapon GrabReel( int slot = 1 ) => new()
	{
		Name = "GrabReel", DisplayName = "Grab Reel", Slot = slot, ModelName = "grabber", Grabber = true,
		Damage = 60f /* a blow at ImpactRef */, FireRate = 4f, Range = HookFx.GrabRange, SpreadDegrees = 0f, Recoil = 0f,
		MagazineSize = 3 /* throw charges */, StartingReserve = 0, ReloadTime = 1f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.3f, FovKickPerShot = 2f, ShootSound = "ink_throw",
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

	static StandardMaterial3D beamMaterial;
	static StandardMaterial3D BeamMaterial => beamMaterial ??= new StandardMaterial3D
	{
		AlbedoColor = new Color( 0.45f, 1f, 0.55f, 0.75f ), EmissionEnabled = true, Emission = new Color( 0.35f, 1f, 0.5f ), EmissionEnergyMultiplier = 2.5f,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
	};

	/// <summary> The Grab Reel each frame: grab on a press, carry while held, fling on letting go, throw on right click. </summary>
	void UpdateGrab( float dt, bool free )
	{
		// Throw charges come back one at a time.
		if ( Ammo < MagazineSize ) { chargeBack += dt; if ( chargeBack >= HookFx.ChargeRegen ) { chargeBack = 0f; Ammo++; } }
		else chargeBack = 0f;

		var down = free && (controller.In.Pressed( "attack1" ) || ForceGrab);
		var throwNow = free && (controller.In.JustPressed( "attack2" ) || forceThrow);
		forceThrow = false;
		var eye = controller.Camera?.GlobalPosition ?? controller.EyeTransform.Origin;
		var forward = -(controller.Camera?.GlobalBasis ?? controller.EyeTransform.Basis).Z;

		if ( down && !grabbing ) { grabbing = true; TryGrab( eye, forward ); }
		if ( !down && grabbing ) { grabbing = false; LetGo( false, forward ); }

		if ( Held is { } h )
		{
			if ( throwNow )
			{
				if ( Ammo > 0 ) { if ( !InfiniteAmmo ) Ammo--; LetGo( true, forward ); }   // still held down: no grab until it is let go
				else Sfx.Play2D( "dry_fire", 0.6f );
			}
			else h.HoldAt( eye + forward * h.Distance, dt );
		}
		else if ( held is not null ) { held = null; }

		if ( BeamTarget is { } heavy )
		{
			if ( !down || heavy.GlobalPosition.DistanceTo( eye ) > GrabReach * 1.25f ) beamOn = null;
			else
			{
				beamTick -= dt;
				if ( beamTick <= 0f )
				{
					beamTick = HookFx.BeamTick;
					var hurt = CurrentDamage * HookFx.BeamDps * HookFx.BeamTick * (1f + HookFx.HeavyHandsPer * Skill( SkillId.HeavyHands ));
					HookFx.BeamDamage += hurt;
					SkillFx.HitPart( heavy, hurt, LinePoint(), controller );
					MarkHit();
				}
			}
		}
		else beamOn = null;

		// The beam: from the muzzle to whatever it holds.
		var end = Held is { } carried ? carried.Centre : BeamTarget is { } locked ? SkillFx.NearestPart( locked, LinePoint() ) : (Vector3?)null;
		if ( end is { } to )
		{
			if ( beam is null || !IsInstanceValid( beam ) ) beam = GunKit.Line( controller.GetTree().CurrentScene, BeamMaterial, 0.035f );
			GunKit.Lay( beam, LinePoint(), to );
		}
		else if ( beam is not null && IsInstanceValid( beam ) ) beam.Visible = false;
	}

	void TryGrab( Vector3 eye, Vector3 forward )
	{
		var exclude = new Godot.Collections.Array<Rid> { controller.GetRid() };
		var tr = Trace.Ray( controller, eye, eye + forward * GrabReach, Layers.World | Layers.Creature, exclude );
		var c = tr.Hit ? (Shootables.Find( tr.Node ) as CreaturePart)?.Creature as Creature : null;
		if ( c is null && GoreBall.NearestToLine( eye, forward, GrabReach, HookFx.AimWidth ) is { } near )
			c = NearestCreature( near );
		if ( c is null || !c.IsEnemy || c.IsDead || c.Grabbed ) { Sfx.Play2D( "dry_fire", 0.4f, 1.4f ); return; }
		// Online, a joiner's copy of the host's creature only shows the beam: the host's copy is the one lifted and thrown.
		if ( c.Pullable && !c.Puppet )
		{
			HookFx.Grabs++;
			held = GrabbedBody.Grab( this, controller, c, MathX.Clamp( c.GlobalPosition.DistanceTo( eye ), HookFx.HoldMin, HookFx.HoldMax ) );
			Sfx.Play2D( "beam_fire", 0.45f, 1.6f, Sfx.GunBus );
			Controls.Rumble( 0.2f );
		}
		else
		{
			HookFx.Beams++;
			beamOn = c;
			beamTick = 0f;
			Sfx.Play2D( "beam_fire", 0.45f, 0.8f, Sfx.GunBus );
		}
	}

	static Creature NearestCreature( Vector3 at )
	{
		Creature best = null;
		var bestD = 2.5f;
		foreach ( var c in Creature.Everyone )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var d = SkillFx.NearestPart( c, at ).DistanceTo( at );
			if ( d < bestD ) { bestD = d; best = c; }
		}
		return best;
	}

	void LetGo( bool thrown, Vector3 forward )
	{
		beamOn = null;
		if ( Held is not { } h ) return;
		held = null;
		if ( thrown )
		{
			HookFx.Throws++;
			var speed = HookFx.ThrowSpeed * (1f + HookFx.StrongArmPer * Skill( SkillId.StrongArm ));
			h.Release( forward * speed + h.Moved * 0.3f, true );
			Sfx.Play2D( ShootSound, 0.7f, 0.8f, Sfx.GunBus );
			juice?.Shake( ShakePerShot );
			juice?.KickFov( FovKickPerShot );
		}
		else
		{
			HookFx.Flings++;
			h.Release( h.Moved.LimitLength( HookFx.FlingCap ), false );
		}
	}

	/// <summary> Put away or dropped: whatever is held falls. </summary>
	void DropGrab()
	{
		grabbing = false;
		beamOn = null;
		if ( Held is { } h ) { held = null; h.Release( h.Moved.LimitLength( HookFx.FlingCap ), false ); }
		if ( beam is not null && IsInstanceValid( beam ) ) beam.Visible = false;
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
/// An enemy on the Grab Reel: while held it chases the point in front of the player (its brain off, `Creature.Grabbed`),
/// slamming into the world and bowling other enemies over when it moves fast; let go, it flies on (gravity, drag,
/// tumbling), bounces off walls, and lands, hurt by how hard it hit. Crusher hurts it while held; a thrown one with
/// Depth Charge bursts where it first hits.
/// </summary>
public partial class GrabbedBody : Node3D
{
	public Creature Victim { get; private set; }
	PlayerWeapon weapon;
	PlayerController owner;
	Vector3 centreOffset, spin;
	Basis restBasis;
	public Vector3 Velocity { get; private set; }
	/// <summary> How fast it really moved last frame (held): pressing it into the floor is not a slam, only arriving fast is. </summary>
	Vector3 moved;
	public Vector3 Moved => moved;
	public float Distance;
	public bool IsHeld { get; private set; } = true;
	bool thrown, charged;
	float flying, sinceSlam = 9f, crushTick;
	readonly Dictionary<Creature, double> bowled = new();

	public Vector3 Centre => Victim.GlobalPosition + centreOffset;
	int Skill( SkillId id ) => Skills.Rank( owner, id );

	public static GrabbedBody Grab( PlayerWeapon weapon, PlayerController owner, Creature victim, float distance )
	{
		var g = new GrabbedBody { Name = "Grabbed", weapon = weapon, owner = owner, Victim = victim, Distance = distance };
		victim.Grabbed = true;
		g.restBasis = victim.GlobalBasis;
		// The middle of its parts (where the beam takes it), as the bubble trap does.
		var sum = Vector3.Zero;
		var n = 0;
		foreach ( var p in CreaturePart.All ) if ( IsInstanceValid( p ) && p.Creature == victim ) { sum += p.GlobalPosition; n++; }
		g.centreOffset = n > 0 ? sum / n - victim.GlobalPosition : Vector3.Up * 0.4f;
		owner.GetTree().CurrentScene.AddChild( g );
		return g;
	}

	public override void _ExitTree()
	{
		if ( Victim is not null && IsInstanceValid( Victim ) && Victim.Grabbed ) { Victim.Grabbed = false; Victim.Pull( Vector3.Zero ); }
	}

	/// <summary> Held: chases `point` (eased, capped), slams into the world, bowls through other enemies. </summary>
	public void HoldAt( Vector3 point, float dt )
	{
		if ( !Alive() ) return;
		Distance = MathX.Approach( Distance, HookFx.HoldDistance, dt * 3f );
		var from = Centre;
		var want = (point - from) * HookFx.Follow;
		Velocity = want.LimitLength( HookFx.CarryCap );
		Move( from, Velocity * dt, true );
		if ( !Alive() ) return;
		moved = (Centre - from) / MathF.Max( dt, 1e-4f );
		// Crusher: the beam squeezes it.
		var crush = Skill( SkillId.Crusher );
		if ( crush > 0 && (crushTick -= dt) <= 0f )
		{
			crushTick = 0.25f;
			PlayerPerks.Hurt( Victim, HookFx.CrusherPer * crush * 0.25f * weapon.UpgradeScale, Centre, Centre, owner );
		}
		// A little wriggle so it reads as alive and caught.
		Victim.GlobalBasis = new Basis( Vector3.Up, MathF.Sin( (float)Clock.Now * 9f ) * 0.15f ) * restBasis;
	}

	/// <summary> Let go: it flies on with `velocity` (a throw is faster, and is what Depth Charge goes off on). </summary>
	public void Release( Vector3 velocity, bool isThrow )
	{
		IsHeld = false;
		thrown = isThrow;
		charged = isThrow && Skill( SkillId.DepthCharge ) > 0;
		Velocity = velocity;
		spin = Rng.OnSphere() * MathX.Clamp( velocity.Length() * 0.6f, 2f, 12f );
		flying = 0f;
		bowled.Clear();
		if ( velocity.Length() < 1.5f ) Velocity = Vector3.Down;
	}

	bool Alive()
	{
		if ( Victim is not null && IsInstanceValid( Victim ) && !Victim.IsDead ) return true;
		IsHeld = false;
		QueueFree();
		return false;
	}

	public override void _Process( double delta )
	{
		if ( !Alive() || IsHeld ) return;
		var dt = (float)delta;
		flying += dt;
		Velocity += Vector3.Down * HookFx.Gravity * dt;
		Velocity *= MathF.Exp( -HookFx.Drag * dt );
		Victim.GlobalBasis = (new Basis( spin.SafeNormal() == Vector3.Zero ? Vector3.Up : spin.SafeNormal(), spin.Length() * flying ) * restBasis).Orthonormalized();
		Move( Centre, Velocity * dt, false );
		if ( IsInstanceValid( this ) && !IsQueuedForDeletion() && flying > HookFx.FlightMax ) Land( 0f, null );
	}

	/// <summary> Moves the enemy along `step`, stopping at the world: a fast hit is a slam (held) or an impact (flying). </summary>
	void Move( Vector3 from, Vector3 step, bool holding )
	{
		// Held, a blow is only as hard as it was really moving (shoving it into the floor does nothing); flying, its speed.
		var speed = holding ? MathF.Min( moved.Length(), Velocity.Length() ) : Velocity.Length();
		Bowl( from, speed );
		if ( !Alive() ) return;
		var tr = Trace.Ray( Victim, from, from + step + step.SafeNormal() * 0.35f, Layers.World, new Godot.Collections.Array<Rid> { owner.GetRid() } );
		if ( !tr.Hit ) { Victim.GlobalPosition += step; return; }
		// Up against the world: rest just off it.
		Victim.GlobalPosition = tr.HitPosition + tr.Normal * 0.35f - centreOffset;
		if ( holding )
		{
			if ( speed >= HookFx.ImpactSpeed && sinceSlam >= HookFx.ImpactEvery ) Slam( tr.HitPosition, tr.Normal, speed );
			Velocity = Vector3.Zero;
			return;
		}
		// Flying: the first hard hit hurts (and sets Depth Charge off); off a wall it bounces, on the floor it lands.
		if ( speed >= HookFx.ImpactSpeed ) Slam( tr.HitPosition, tr.Normal, speed );
		if ( !Alive() ) return;
		if ( tr.Normal.Y > 0.55f || speed < 3f ) { Land( speed, tr.HitPosition ); return; }
		Velocity = Velocity.Bounce( tr.Normal ) * 0.35f;
	}

	void Slam( Vector3 at, Vector3 normal, float speed )
	{
		sinceSlam = 0f;
		HookFx.GrabSlams++;
		SkillFx.HitPart( Victim, weapon.ImpactDamage( speed ), at, owner );
		Fx.ImpactWorld( at, normal );
		Sfx.Play( "dive_impact", at, MathX.Clamp( speed / 20f, 0.3f, 1f ), Rng.Float( 0.9f, 1.15f ) );
		Jetpack.SlamBubbles( at, normal, MathX.Clamp( speed / 25f, 0.2f, 0.8f ) );
		owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( MathX.Clamp( speed / 40f, 0.1f, 0.45f ) );
		Controls.Rumble( MathX.Clamp( speed / 40f, 0.15f, 0.5f ) );
		weapon.MarkHit();
		if ( Victim.IsDead ) weapon.MarkKill();
		if ( charged ) DepthCharge( at );
	}

	/// <summary> Depth Charge: a thrown enemy bursts where it first hits. </summary>
	void DepthCharge( Vector3 at )
	{
		charged = false;
		HookFx.DepthCharges++;
		PlayerPerks.HurtCreatures( at, HookFx.DepthRadius, HookFx.DepthDamage( Skill( SkillId.DepthCharge ) ) * weapon.UpgradeScale, owner );
		BlueBlast.Spawn( GetTree().CurrentScene, at, HookFx.DepthRadius );
	}

	/// <summary> Other enemies in its way are bowled over (once each a flight; every half second while held). </summary>
	void Bowl( Vector3 at, float speed )
	{
		if ( speed < HookFx.ImpactSpeed * 0.7f ) return;
		var now = Clock.Now;
		foreach ( var c in Creature.Everyone )
		{
			if ( c == Victim || !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || c.Grabbed ) continue;
			if ( SkillFx.NearestPart( c, at ).DistanceTo( at ) > HookFx.BowlRadius ) continue;
			if ( bowled.TryGetValue( c, out var last ) && (!IsHeld || now - last < 0.5) ) continue;
			bowled[c] = now;
			HookFx.Bowled++;
			var hurt = weapon.ImpactDamage( speed ) * (HookFx.BowlShare + HookFx.BowlingPer * Skill( SkillId.Bowling ));
			SkillFx.HitPart( c, hurt, at, owner );
			SkillFx.Shove( c, Velocity.SafeNormal() * 1.6f + Vector3.Up * 0.3f );
			if ( !c.IsDead ) c.Stun( HookFx.LandStun );
			PlayerPerks.Hurt( Victim, weapon.ImpactDamage( speed ) * HookFx.BowlSelf, at, at, owner );
			Sfx.Play( "stab_squish_1", at, 0.7f, Rng.Float( 0.8f, 1f ) );
			if ( charged ) DepthCharge( at );
			if ( !Alive() ) return;
		}
	}

	void Land( float speed, Vector3? at )
	{
		HookFx.Landings++;
		if ( Alive() )
		{
			// Set down on the floor below, upright again, a moment dazed.
			var centre = Centre;
			var floor = Trace.Ray( Victim, centre, centre + Vector3.Down * 30f, Layers.World, new Godot.Collections.Array<Rid> { owner.GetRid() } );
			Victim.GlobalPosition = floor.Hit ? floor.HitPosition : Victim.GlobalPosition with { Y = SeaTerrain.HeightAt( Victim.GlobalPosition.X, Victim.GlobalPosition.Z ) };
			var flat = (restBasis * Vector3.Forward).Flat().SafeNormal();
			Victim.GlobalBasis = flat == Vector3.Zero ? restBasis : Shapes.LookAt( flat );
			Victim.Grabbed = false;
			Victim.Pull( Vector3.Zero );
			Victim.Stun( HookFx.LandStun );
		}
		QueueFree();
	}

	public override void _PhysicsProcess( double delta ) => sinceSlam += (float)delta;
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
	public static readonly Vector3 LureMuzzle = new( 22f, 0f, 5f );
	static readonly Color Navy = new( 0.08f, 0.12f, 0.22f ), Orange = new( 0.95f, 0.5f, 0.16f ), Rope = new( 0.86f, 0.74f, 0.5f ), Brass = new( 0.8f, 0.64f, 0.3f );

	public static readonly Vector3 GrabMuzzle = new( 31f, 0f, 3.2f );

	/// <summary> The Grab Reel: the harpoon gun's stock and reel, a short navy barrel, three orange prongs and a green glow orb between them. </summary>
	public static void GrabGun( Node3D gun )
	{
		var navy = GunKit.Paint( Navy, 0.5f );
		var orange = GunKit.Paint( Orange, 0.45f );
		var brass = GunKit.Paint( Brass, 0.3f, 0.7f );
		var rope = GunKit.Paint( Rope, 0.85f );
		var wood = GunKit.Paint( new Color( 0.45f, 0.28f, 0.15f ), 0.7f );
		var glow = GunKit.Paint( new Color( 0.45f, 1f, 0.55f ), 0.2f, 0f, 4f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), wood, 18f );
		GunKit.Box( gun, new Vector3( 2.4f, 0f, -2.1f ), new Vector3( 4.2f, 0.6f, 0.5f ), brass );
		GunKit.AlongX( gun, new Vector3( 11f, 0f, 3.2f ), 2.1f, 18f, navy, 1.5f, 18 );
		foreach ( var x in new[] { 4f, 12f, 19f } ) GunKit.AlongX( gun, new Vector3( x, 0f, 3.2f ), 2.25f, 1.2f, orange, -1f, 18 );
		GunKit.Box( gun, new Vector3( 4f, 0f, 0.6f ), new Vector3( 9f, 2.2f, 2.2f ), navy );
		GunKit.Part( gun, new CylinderMesh { TopRadius = 3f, BottomRadius = 3f, Height = 1.6f, RadialSegments = 20 }, brass, new Vector3( 6f, -3.2f, 2.2f ), null, "Reel" );
		GunKit.Part( gun, new CylinderMesh { TopRadius = 2.5f, BottomRadius = 2.5f, Height = 1.7f, RadialSegments = 20 }, rope, new Vector3( 6f, -3.2f, 2.2f ), null, "Rope Coil" );
		// Three prongs round the mouth, bent in at the tips.
		for ( var i = 0; i < 3; i++ )
		{
			var a = i * MathF.Tau / 3f + 0.5f;
			var ring = new Vector3( 0f, MathF.Cos( a ), MathF.Sin( a ) );
			var root = new Vector3( 20f, 0f, 3.2f ) + ring * 1.9f;
			var bend = new Vector3( 27f, 0f, 3.2f ) + ring * 2.6f;
			var tip = new Vector3( 31f, 0f, 3.2f ) + ring * 1.3f;
			GunKit.Rod( gun, root, bend, 0.35f, orange );
			GunKit.Spike( gun, bend, tip, 0.35f, orange, 6 );
		}
		var loaded = new Node3D { Name = "Loaded" };
		gun.AddChild( loaded );
		GunKit.Part( loaded, new SphereMesh { Radius = 1.3f, Height = 2.6f, RadialSegments = 16, Rings = 8 }, glow, new Vector3( 27f, 0f, 3.2f ), null, "Orb" );
		GunKit.AlongX( gun, new Vector3( -3.5f, 0f, 2.4f ), 1.1f, 4f, wood, 1.4f, 12 );
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
