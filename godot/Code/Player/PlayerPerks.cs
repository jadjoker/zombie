using Godot;

/// <summary> The pickups creatures can drop besides ammo. </summary>
public enum PerkKind { Reload, FireRate, Explosive, Electric, Chest, Shield, Move, Blast, Pierce, Reach, Swarm }

/// <summary>
/// The player's pickups (dropped by creatures, see <see cref="PerkPickup"/>): how many of each they hold, and what
/// each does.
/// - Faster Reload: each one makes reloading 6% quicker.
/// - Fire Rate: each one adds 2% to every gun's rate of fire.
/// - Explosive Round: an emptied magazine is thrown and explodes on what it hits (100 damage to creatures round it,
///   none to players, +50 for each more).
/// - Electric Bullets: a rifle hit has a 25% chance (5% more for each pickup after the first) to shock for 20 more; the shock has a 70% chance to jump to the
///   next creature near by, up to 8 jumps (+10 damage for each more).
/// - Pirate Chest: 4000 points, there and then (does not stack).
/// - Shield: 25 shield (a blue bar over the health bar), +10 for each more. It takes hits before health does, comes
///   back in half the time health does, but after being emptied it waits twice as long as health.
/// - Movement Buff: the first adds 0.5 m/s and 10% more stamina; each more adds 5% stamina.
/// - Biden Blast: once a wave per pickup (up to three a wave), G (RB on a pad) lets off a blue blast: 500 damage to every creature within 8 m.
///   (RB is also the axe on a pad: while the blast is ready it fires the blast instead.)
/// Every pickup above drops for Ram-Bot only, except the shield and the pirate chest, which (like ammo) drop for every robot.
/// - Trident Pierce (Trident-Bot only, 4%): each one lets a trident seek and go through one more enemy.
/// - Trident Reach (Trident-Bot only, 1%): each one lets a trident look 1 m further for the next enemy to seek.
/// - Trident Swarm (Trident-Bot only, 1%, at most 3): when a trident first hits an enemy, small half-damage tridents may come
///   out and seek other enemies: 1 stack 20% of one; 2 stacks 40% of one, 20% of two; 3 stacks 60%, 40%, 20% (`TridentBolt.Splinter`).
/// A new game clears them. Lives on the player (a child of the PlayerController named PlayerPerks).
/// </summary>
public partial class PlayerPerks : Node
{
	public static readonly (PerkKind Kind, string Name, string Icon, float Chance, Color Colour)[] Table =
	{
		(PerkKind.Reload, "Faster Reload", "reload", 0.005f, new Color( 0.27f, 0.59f, 1f )),
		(PerkKind.FireRate, "Fire Rate", "firerate", 0.005f, new Color( 1f, 0.55f, 0.16f )),
		(PerkKind.Explosive, "Explosive Round", "explosive", 0.005f, new Color( 0.9f, 0.24f, 0.2f )),
		(PerkKind.Electric, "Electric Bullets", "electric", 0.005f, new Color( 0.59f, 0.31f, 1f )),
		(PerkKind.Chest, "Pirate Chest", "chest", 0.005f, new Color( 1f, 0.8f, 0.24f )),
		(PerkKind.Shield, "Shield", "shield", 0.015f, new Color( 0.16f, 0.75f, 1f )),
		(PerkKind.Move, "Movement Buff", "move", 0.0025f, new Color( 0.24f, 0.78f, 0.43f )),
		(PerkKind.Blast, "Biden Blast", "blast", 0.0025f, new Color( 0.16f, 0.35f, 0.9f )),
		(PerkKind.Pierce, "Trident Pierce", "pierce", 0.04f, new Color( 0.12f, 0.75f, 0.72f )),
		(PerkKind.Reach, "Trident Reach", "reach", 0.01f, new Color( 0.62f, 0.4f, 1f )),
		(PerkKind.Swarm, "Trident Swarm", "swarm", 0.01f, new Color( 0.95f, 0.75f, 0.15f )),
	};

	public const float ReloadPerStack = 0.06f;
	public const float FireRatePerStack = 0.02f;
	public const float ClipDamage = 100f, ClipDamagePerStack = 50f, ClipRadius = 4.5f;
	public const float ShockChance = 0.25f, ShockDamage = 20f, ShockDamagePerStack = 10f, ShockJumpChance = 0.7f, ShockJumpRange = 7f;
	public const int ShockMaxJumps = 8;
	/// <summary> Each Electric Bullets pickup after the first adds this to the shock chance. </summary>
	public const float ShockChancePerStack = 0.05f;
	public float CurrentShockChance => Count( PerkKind.Electric ) == 0 ? 0f : MathF.Min( 1f, ShockChance + ShockChancePerStack * (Count( PerkKind.Electric ) - 1) );
	/// <summary> Blasts a wave: one per Biden Blast picked up, up to this many. </summary>
	public const int MaxBlastsPerWave = 3;
	public const int ChestPoints = 4000;
	public const float ShieldFirst = 25f, ShieldPerStack = 10f;
	public const float MoveSpeedBonus = 0.5f;   // metres per second
	public const float BlastDamage = 500f, BlastRadius = 8f;

	readonly int[] counts = new int[Enum.GetValues<PerkKind>().Length];

	/// <summary>
	/// Whether creatures drop this one for the robot being played (user's choice): the shield and the pirate chest drop for
	/// every robot (like ammo), Trident Pierce and Trident Reach for Trident-Bot only, every other pickup for Ram-Bot (the rifle and shotgun kit) only.
	/// </summary>
	public static bool Drops( PerkKind kind ) => kind switch
	{
		PerkKind.Shield or PerkKind.Chest => true,
		PerkKind.Pierce or PerkKind.Reach => RobotClass.Current == RobotClass.TridentBot,
		PerkKind.Swarm => RobotClass.Current == RobotClass.TridentBot && SwarmRoom(),
		_ => RobotClass.Current == RobotClass.RamBot,
	};
	/// <summary> More enemies a trident seeks after its first, one per Trident Pierce. </summary>
	public int ExtraPierce => Count( PerkKind.Pierce );
	/// <summary> Trident Swarm stacks this many times at most; it stops dropping once every player has them all. </summary>
	public const int MaxSwarm = 3;
	static bool SwarmRoom()
	{
		foreach ( var survivor in PlayerSurvivor.All )
			if ( (Of( survivor.Controller )?.Count( PerkKind.Swarm ) ?? 0) < MaxSwarm ) return true;
		return false;
	}
	/// <summary> Metres more a trident looks for the next enemy to seek, per Trident Reach. </summary>
	public const float ReachPerStack = 1f;
	public float ExtraSeekRange => ReachPerStack * Count( PerkKind.Reach );
	public int Count( PerkKind kind ) => counts[(int)kind];

	PlayerController controller;
	PlayerSurvivor survivor;
	OmniLight3D shieldGlow;

	// ---- Shield ----
	public float ShieldMax => Count( PerkKind.Shield ) == 0 ? 0f : ShieldFirst + ShieldPerStack * (Count( PerkKind.Shield ) - 1);
	public float Shield { get; private set; }
	public TimeSince TimeSinceShieldHit { get; private set; } = 999f;
	bool shieldBroken;

	// ---- Biden Blast ----
	int blastWave = -1;
	int blastsUsed;
	ulong blastFrame;
	/// <summary> How many blasts this wave allows: one per pickup, up to three. </summary>
	public int BlastsPerWave => Math.Min( Count( PerkKind.Blast ), MaxBlastsPerWave );
	/// <summary> Blasts still to let off this wave. </summary>
	public int BlastsLeft => BlastsPerWave - (blastWave == (WaveDirector.Current?.Wave ?? 0) ? blastsUsed : 0);
	public bool BlastReady => BlastsLeft > 0;

	// ---- What the stacks come to ----
	public float ReloadScale => MathF.Pow( 1f - ReloadPerStack, Count( PerkKind.Reload ) );
	public float FireRateScale => 1f + FireRatePerStack * Count( PerkKind.FireRate );
	public float ClipBlastDamage => Count( PerkKind.Explosive ) == 0 ? 0f : ClipDamage + ClipDamagePerStack * (Count( PerkKind.Explosive ) - 1);
	public float SpeedBonus => Count( PerkKind.Move ) > 0 ? MoveSpeedBonus : 0f;
	public float StaminaBonus => Count( PerkKind.Move ) == 0 ? 0f : 0.1f + 0.05f * (Count( PerkKind.Move ) - 1);

	public static PlayerPerks Of( Node controller ) => controller?.GetNodeOrNull<PlayerPerks>( "PlayerPerks" );
	/// <summary> Tests: pickups taken, shocks dealt and jumps made, clips thrown, blasts let off. </summary>
	public static int Taken, Shocks, ShockJumps, ClipsThrown, Blasts, Blocked;

	public override void _Ready()
	{
		Name = "PlayerPerks";
		controller = GetParent<PlayerController>();
		if ( !InputMap.HasAction( "blast" ) )
		{
			InputMap.AddAction( "blast", 0.5f );
			InputMap.ActionAddEvent( "blast", new InputEventKey { PhysicalKeycode = Key.G } );
			InputMap.ActionAddEvent( "blast", new InputEventJoypadButton { ButtonIndex = JoyButton.RightShoulder } );
		}
		shieldGlow = new OmniLight3D { Name = "Shield Glow", LightColor = new Color( 0.3f, 0.75f, 1f ), LightEnergy = 0f, OmniRange = 3.5f, ShadowEnabled = false, Position = Vector3.Up * 1f };
		controller.CallDeferred( Node.MethodName.AddChild, shieldGlow );
	}

	void Hook()
	{
		survivor = controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		if ( survivor?.Health is { } h ) h.Absorb = Absorb;
	}

	/// <summary> A new game: nothing held. </summary>
	public void Clear()
	{
		Array.Clear( counts );
		Shield = 0f;
		shieldBroken = false;
		blastWave = -1;
		Apply();
	}

	/// <summary> Takes a pickup. </summary>
	public void Give( PerkKind kind )
	{
		Taken++;
		if ( survivor is null ) Hook();
		if ( kind == PerkKind.Chest ) { survivor?.RewardPoints( ChestPoints ); return; }
		if ( kind == PerkKind.Swarm && counts[(int)kind] >= MaxSwarm ) return;
		counts[(int)kind]++;
		if ( kind == PerkKind.Shield ) { Shield = ShieldMax; shieldBroken = false; }
		Apply();
	}

	/// <summary> Puts the movement buff's stamina on the movement kit (the speed is added by WaterFeel and the kit). </summary>
	void Apply()
	{
		if ( controller?.GetNodeOrNull<PlayerMovement>( "PlayerMovement" ) is { } m ) m.StaminaBonus = StaminaBonus;
	}

	/// <summary> The shield takes a hit first: what is left over goes on to health. Poison and the like go straight through. </summary>
	float Absorb( DamageInfo damage )
	{
		// The Trident-Bot's energy shield, raised: it takes 65% of the hit before anything else.
		if ( damage.Attacker is not PlayerAfflictions && controller.GetNodeOrNull<WeaponInventory>( "WeaponInventory" )?.Current is { Spear: true, Blocking: true } )
		{
			damage.Damage *= 1f - PlayerWeapon.BlockShare;
			Blocked++;
			Sfx.Play2D( "glass_hit", 0.45f, 1.4f );
		}
		if ( Shield <= 0f || damage.Attacker is PlayerAfflictions ) return damage.Damage;
		TimeSinceShieldHit = 0f;
		var taken = MathF.Min( Shield, damage.Damage );
		Shield -= taken;
		if ( Shield <= 0f ) { shieldBroken = true; Sfx.Play2D( "glass_break", 0.5f, 1.2f ); }
		else Sfx.Play2D( "glass_hit", 0.4f, 0.9f );
		controller.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.15f );
		return damage.Damage - taken;
	}

	/// <summary> G or RB was pressed for the blast this frame (so the axe stands aside). </summary>
	public bool BlastPressed => Count( PerkKind.Blast ) > 0 && Input.IsActionJustPressed( "blast" ) && (BlastReady || blastFrame == Engine.GetProcessFrames());

	public override void _Process( double delta )
	{
		if ( survivor is null ) { Hook(); if ( survivor is null ) return; }
		var dt = (float)delta;
		// The shield comes back after half health's wait (twice health's if it was emptied), twice as fast.
		if ( ShieldMax > 0f && Shield < ShieldMax && survivor.IsAlive )
		{
			var wait = shieldBroken ? survivor.RegenDelay * 2f : survivor.RegenDelay * 0.5f;
			if ( TimeSinceShieldHit > wait )
			{
				shieldBroken = false;
				Shield = MathF.Min( ShieldMax, Shield + survivor.RegenPerSecond * 2f * dt );
			}
		}
		if ( shieldGlow is not null && IsInstanceValid( shieldGlow ) )
			shieldGlow.LightEnergy = MathX.Lerp( shieldGlow.LightEnergy, Shield > 0f ? 0.9f + 0.25f * MathF.Sin( (float)Clock.Now * 3f ) : 0f, 1f - MathF.Exp( -6f * dt ) );

		if ( survivor.IsAlive && BlastReady && Input.IsActionJustPressed( "blast" ) && !DevMenu.IsOpen && !PlayerWeapon.MouseFree && !DockHouse.WeaponsHeld ) LetOffBlast();
	}

	/// <summary> The Biden Blast: a blue ring out from the player, 500 to every creature within 8 m. Once a wave. </summary>
	public void LetOffBlast()
	{
		var wave = WaveDirector.Current?.Wave ?? 0;
		if ( blastWave != wave ) { blastWave = wave; blastsUsed = 0; }
		blastsUsed++;
		blastFrame = Engine.GetProcessFrames();
		Blasts++;
		var at = controller.GlobalPosition + Vector3.Up * 1f;
		BlueBlast.Spawn( controller.GetTree().CurrentScene, at, BlastRadius );
		HurtCreatures( at, BlastRadius, BlastDamage, controller );
		controller.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 1f );
		Controls.Rumble( 0.7f );
	}

	/// <summary>
	/// Straight damage to a creature's health (no part multipliers: a blast or a shock does what it says), less what
	/// its armour turns (<see cref="Creature.AreaDamageScale"/>: the Rocky Shark's plates), with the points for the
	/// hit or the kill going to the shooter.
	/// </summary>
	public static void Hurt( Creature creature, float damage, Vector3 where, Vector3 from, PlayerController shooter )
	{
		if ( creature?.Health is not { } health || creature.IsDead ) return;
		health.OnDamage( new DamageInfo { Damage = damage * creature.AreaDamageScale, Position = where, Origin = from, Attacker = shooter } );
		shooter?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.RewardHit( creature.IsDead, false, true );
	}

	/// <summary> Damage to every enemy creature with a part inside a radius. Returns how many. </summary>
	public static int HurtCreatures( Vector3 at, float radius, float damage, PlayerController shooter )
	{
		var hit = 0;
		foreach ( var creature in Creature.Everyone.Where( c => c.IsEnemy && !c.IsDead ).ToList() )
		{
			CreaturePart nearest = null;
			var best = radius;
			foreach ( var part in CreaturePart.All )
			{
				if ( part.Creature != creature || !IsInstanceValid( part ) ) continue;
				if ( creature is Mermaid && part.Index == Mermaid.ShieldIndex ) continue;   // a blast goes round a shield
				var d = part.GlobalPosition.DistanceTo( at );
				if ( d < best ) { best = d; nearest = part; }
			}
			if ( nearest is null ) continue;
			Hurt( creature, damage, nearest.GlobalPosition, at, shooter );
			hit++;
		}
		return hit;
	}

	/// <summary>
	/// Electric Bullets: a rifle hit on a creature may shock it, and the shock may jump on to the next one near by.
	/// </summary>
	public void RifleHit( Node3D hitPart, Vector3 at )
	{
		if ( Count( PerkKind.Electric ) == 0 || Rng.Float() > CurrentShockChance ) return;
		var first = (Shootables.Find( hitPart ) as CreaturePart)?.Creature as Creature;
		if ( first is null ) return;
		var damage = ShockDamage + ShockDamagePerStack * (Count( PerkKind.Electric ) - 1);
		var shocked = new HashSet<Creature>();
		var from = at;
		var target = first;
		for ( var jump = 0; jump <= ShockMaxJumps && target is not null; jump++ )
		{
			shocked.Add( target );
			var part = CreaturePart.All.Where( p => p.Creature == target && IsInstanceValid( p ) && !(target is Mermaid && p.Index == Mermaid.ShieldIndex) )
				.OrderBy( p => p.GlobalPosition.DistanceSquaredTo( from ) ).FirstOrDefault();
			if ( part is null ) break;
			var to = part.GlobalPosition;
			if ( jump > 0 ) { ShockJumps++; LightningArc.Spawn( controller.GetTree().CurrentScene, from, to ); }
			Shocks++;
			LightningArc.Spark( controller.GetTree().CurrentScene, to );
			Hurt( target, damage, to, from, controller );
			from = to;
			if ( Rng.Float() > ShockJumpChance ) break;
			target = Creature.Everyone.Where( c => c.IsEnemy && !c.IsDead && !shocked.Contains( c ) && c.GlobalPosition.DistanceTo( from ) < ShockJumpRange )
				.OrderBy( c => c.GlobalPosition.DistanceSquaredTo( from ) ).FirstOrDefault();
		}
		Sfx.Play( "beam_fire", at, 0.35f, 2.2f );
	}

	/// <summary> Explosive Round: the emptied magazine, thrown where the player is looking. </summary>
	public void ThrowClip()
	{
		if ( Count( PerkKind.Explosive ) == 0 ) return;
		ClipsThrown++;
		var camera = controller.Camera;
		var forward = -camera.GlobalBasis.Z;
		ThrownClip.Throw( controller.GetTree().CurrentScene, camera.GlobalPosition + forward * 0.5f - camera.GlobalBasis.Y * 0.15f, forward * 15f + Vector3.Up * 2.5f, ClipBlastDamage, controller );
	}
}

/// <summary>
/// An emptied magazine thrown by the player (Explosive Round): it seeks the enemy nearest the way it was thrown (no
/// climb first, unlike MC-51's missiles: it turns onto them at once), tumbling, and explodes on what it hits. With no
/// enemy about it arcs and drops as a plain throw.
/// </summary>
public partial class ThrownClip : Node3D
{
	Vector3 velocity;
	float damage;
	PlayerController thrower;
	TimeSince age;
	Creature target;
	TimeSince sinceLook = 99f;
	/// <summary> How fast it flies once it has a target, and how quickly it turns onto it (radians a second). </summary>
	public const float SeekSpeed = 18f, SeekTurn = 7f, SeekRange = 45f;
	public static int Seeking;

	/// <summary> The enemy it goes for: the nearest one within reach and roughly the way it is flying. </summary>
	Creature PickTarget()
	{
		var at = GlobalPosition;
		var heading = velocity.SafeNormal();
		Creature best = null;
		var bestScore = float.MaxValue;
		foreach ( var c in Creature.Everyone )
		{
			if ( !c.IsEnemy || c.IsDead || !IsInstanceValid( c ) ) continue;
			var to = Aim( c ) - at;
			var distance = to.Length();
			if ( distance > SeekRange ) continue;
			var along = heading.Dot( to / MathF.Max( distance, 0.01f ) );
			if ( along < 0.2f ) continue;   // not behind the thrower
			var score = distance * (2f - along);
			if ( score < bestScore ) { bestScore = score; best = c; }
		}
		return best;
	}

	static Vector3 Aim( Creature c ) => c.GlobalPosition + Vector3.Up * 0.5f;

	public static void Throw( Node parent, Vector3 from, Vector3 velocity, float damage, PlayerController thrower )
	{
		var clip = new ThrownClip { Name = "Thrown Clip", velocity = velocity, damage = damage, thrower = thrower };
		parent.AddChild( clip );
		clip.GlobalPosition = from;
		clip.age = 0f;
		Sculpt.Block( clip, Vector3.Zero, new Vector3( 0.05f, 0.2f, 0.09f ), Sculpt.Paint( new Color( 0.12f, 0.12f, 0.14f ), 0.4f, 0.6f ) );
		Sculpt.Block( clip, new Vector3( 0f, 0.09f, 0f ), new Vector3( 0.055f, 0.03f, 0.095f ), Sculpt.Glow( new Color( 1f, 0.45f, 0.15f ), 3f ) );
		clip.AddChild( new OmniLight3D { LightColor = new Color( 1f, 0.5f, 0.2f ), LightEnergy = 0.8f, OmniRange = 2f, ShadowEnabled = false } );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var from = GlobalPosition;
		if ( (target is null || !IsInstanceValid( target ) || target.IsDead) && sinceLook > 0.15f )
		{
			sinceLook = 0f;
			var picked = PickTarget();
			if ( picked is not null && target is null ) Seeking++;
			target = picked;
		}
		if ( target is not null && IsInstanceValid( target ) && !target.IsDead )
		{
			// Heat seeking: swing the heading onto the target and speed up to seeking speed.
			var want = (Aim( target ) - from).SafeNormal();
			var heading = velocity.SafeNormal();
			var angle = heading.AngleTo( want );
			var turn = MathF.Min( 1f, SeekTurn * dt / MathF.Max( angle, 0.001f ) );
			heading = heading.Slerp( want, turn ).Normalized();
			var speed = MathX.Lerp( velocity.Length(), SeekSpeed, 1f - MathF.Exp( -3f * dt ) );
			velocity = heading * speed;
		}
		else velocity += Vector3.Down * 7f * dt;
		var to = from + velocity * dt;
		var exclude = thrower is not null && IsInstanceValid( thrower ) ? new Godot.Collections.Array<Rid> { thrower.GetRid() } : null;
		var hit = Trace.Ray( this, from, to, Layers.World | Layers.Creature, exclude );
		if ( hit.Hit || age > 4f )
		{
			var at = hit.Hit ? hit.HitPosition - velocity.SafeNormal() * 0.1f : to;
			AlienBlast.Spawn( GetTree().CurrentScene, at, 1.3f );
			PlayerPerks.HurtCreatures( at, PlayerPerks.ClipRadius, damage, IsInstanceValid( thrower ) ? thrower : null );
			QueueFree();
			return;
		}
		GlobalPosition = to;
		RotateObjectLocal( Vector3.Right, dt * 14f );
	}
}

/// <summary> A jagged bolt of lightning between two points (Electric Bullets), gone in a blink; and the spark where it lands. </summary>
public static class LightningArc
{
	static StandardMaterial3D bolt, core;

	static void Materials()
	{
		if ( bolt is not null ) return;
		bolt = new StandardMaterial3D { AlbedoColor = new Color( 0.7f, 0.5f, 1f ), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color( 0.7f, 0.5f, 1f ), EmissionEnergyMultiplier = 5f };
		core = new StandardMaterial3D { AlbedoColor = new Color( 1f, 1f, 0.7f ), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color( 1f, 1f, 0.6f ), EmissionEnergyMultiplier = 6f };
		bolt.SetMeta( "no_caustics", true );
		core.SetMeta( "no_caustics", true );
	}

	public static void Spawn( Node parent, Vector3 from, Vector3 to )
	{
		Materials();
		var root = new Node3D { Name = "Lightning" };
		parent.AddChild( root );
		const int pieces = 6;
		var last = from;
		var length = from.DistanceTo( to );
		for ( var i = 1; i <= pieces; i++ )
		{
			var next = i == pieces ? to : from.Lerp( to, i / (float)pieces ) + Rng.InSphere( MathF.Min( 0.45f, length * 0.12f ) );
			Sculpt.Limb( root, last, next, 0.035f, 0.035f, bolt, "Bolt" );
			Sculpt.Limb( root, last, next, 0.014f, 0.014f, core, "Core" );
			last = next;
		}
		var light = new OmniLight3D { LightColor = new Color( 0.75f, 0.6f, 1f ), LightEnergy = 3f, OmniRange = 5f, ShadowEnabled = false };
		root.AddChild( light );
		light.GlobalPosition = (from + to) * 0.5f;
		Fx.Expire( root, 0.14f );
	}

	public static void Spark( Node parent, Vector3 at )
	{
		Materials();
		var root = new Node3D { Name = "Shock" };
		parent.AddChild( root );
		for ( var i = 0; i < 5; i++ )
		{
			var dir = Rng.OnSphere();
			Sculpt.Limb( root, at, at + dir * Rng.Float( 0.25f, 0.55f ), 0.02f, 0.005f, i % 2 == 0 ? core : bolt, "Spark" );
		}
		Fx.Expire( root, 0.12f );
	}
}

/// <summary> The Biden Blast: a blue ring and dome racing out from the player to the blast's radius, with a flash and a boom. </summary>
public partial class BlueBlast : Node3D
{
	const float Life = 0.6f;
	float radius = 8f;
	TimeSince age;
	MeshInstance3D ring, dome, flash;
	StandardMaterial3D ringM, domeM, flashM;
	OmniLight3D light;

	public static void Spawn( Node parent, Vector3 at, float radius )
	{
		var b = new BlueBlast { Name = "Biden Blast", radius = radius };
		parent.AddChild( b );
		b.GlobalPosition = at;
	}

	static StandardMaterial3D Fire( Color c, float energy )
	{
		var m = new StandardMaterial3D
		{
			AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		m.SetMeta( "no_caustics", true );
		return m;
	}

	public override void _Ready()
	{
		age = 0f;
		ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1f, Rings = 48, RingSegments = 8 }, MaterialOverride = ringM = Fire( new Color( 0.3f, 0.6f, 1f ), 5f ), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		dome = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 }, MaterialOverride = domeM = Fire( new Color( 0.2f, 0.45f, 1f, 0.35f ), 2.5f ), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		flash = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 }, MaterialOverride = flashM = Fire( new Color( 0.85f, 0.95f, 1f ), 6f ), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( ring ); AddChild( dome ); AddChild( flash );
		light = new OmniLight3D { LightColor = new Color( 0.35f, 0.6f, 1f ), LightEnergy = 8f, OmniRange = radius * 1.5f, ShadowEnabled = false };
		AddChild( light );
		Sfx.Play( "missile_boom", GlobalPosition, 1f, 1.5f );
		Sfx.Play2D( "surge", 0.8f, 0.7f );
		if ( TridentBolt.UnderWater( GlobalPosition ) )
			for ( var i = 0; i < 40; i++ )
			{
				var a = Rng.Float( 0f, MathF.Tau );
				WaterBubble.Create( GlobalPosition + new Vector3( MathF.Cos( a ), Rng.Float( -0.3f, 0.6f ), MathF.Sin( a ) ) * Rng.Float( 1f, radius ), Rng.Float( 0.3f, 1f ), Rng.Float( 0f, 0.3f ), Rng.Float( 0.5f, 1.4f ) );
			}
	}

	public override void _Process( double delta )
	{
		var t = MathX.Clamp( (float)age / Life, 0f, 1f );
		var ease = 1f - (1f - t) * (1f - t);
		ring.Scale = new Vector3( radius * ease, 1.2f, radius * ease );
		ringM.AlbedoColor = ringM.AlbedoColor with { A = MathX.Clamp( 1.3f - t * 1.3f, 0f, 1f ) };
		dome.Scale = Vector3.One * radius * ease;
		domeM.AlbedoColor = domeM.AlbedoColor with { A = 0.35f * (1f - t) };
		flash.Scale = Vector3.One * MathX.Lerp( 0.5f, 2.5f, MathF.Min( 1f, t * 4f ) );
		flashM.AlbedoColor = flashM.AlbedoColor with { A = MathX.Clamp( 1f - t * 4f, 0f, 1f ) };
		light.LightEnergy = 8f * (1f - t) * (1f - t);
		if ( t >= 1f ) QueueFree();
	}
}
