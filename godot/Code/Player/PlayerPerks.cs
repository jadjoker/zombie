using Godot;

/// <summary> The pickups creatures can drop besides ammo. </summary>
public enum PerkKind { Reload, FireRate, Explosive, Electric, Chest, Shield, Move, Blast }

/// <summary>
/// The player's pickups (dropped by creatures, see <see cref="PerkPickup"/>): how many of each they hold, and what
/// each does.
/// - Faster Reload: each one makes reloading 6% quicker.
/// - Fire Rate: each one adds 2% to every gun's rate of fire.
/// - Explosive Round: an emptied magazine is thrown and explodes on what it hits (100 damage to creatures round it,
///   none to players, +50 for each more).
/// - Electric Bullets: a rifle hit has a 25% chance to shock for 20 more; the shock has a 70% chance to jump to the
///   next creature near by, up to 8 jumps (+10 damage for each more).
/// - Pirate Chest: 4000 points, there and then (does not stack).
/// - Shield: 25 shield (a blue bar over the health bar), +10 for each more. It takes hits before health does, comes
///   back in half the time health does, but after being emptied it waits twice as long as health.
/// - Movement Buff: the first adds 0.5 m/s and 10% more stamina; each more adds 5% stamina.
/// - Biden Blast: once a wave, G (RB on a pad) lets off a blue blast: 500 damage to every creature within 8 m.
///   (RB is also the axe on a pad: while the blast is ready it fires the blast instead.)
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
	};

	public const float ReloadPerStack = 0.06f;
	public const float FireRatePerStack = 0.02f;
	public const float ClipDamage = 100f, ClipDamagePerStack = 50f, ClipRadius = 4.5f;
	public const float ShockChance = 0.25f, ShockDamage = 20f, ShockDamagePerStack = 10f, ShockJumpChance = 0.7f, ShockJumpRange = 7f;
	public const int ShockMaxJumps = 8;
	public const int ChestPoints = 4000;
	public const float ShieldFirst = 25f, ShieldPerStack = 10f;
	public const float MoveSpeedBonus = 0.5f;   // metres per second
	public const float BlastDamage = 500f, BlastRadius = 8f;

	readonly int[] counts = new int[8];
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
	ulong blastFrame;
	public bool BlastReady => Count( PerkKind.Blast ) > 0 && blastWave != (WaveDirector.Current?.Wave ?? 0);

	// ---- What the stacks come to ----
	public float ReloadScale => MathF.Pow( 1f - ReloadPerStack, Count( PerkKind.Reload ) );
	public float FireRateScale => 1f + FireRatePerStack * Count( PerkKind.FireRate );
	public float ClipBlastDamage => Count( PerkKind.Explosive ) == 0 ? 0f : ClipDamage + ClipDamagePerStack * (Count( PerkKind.Explosive ) - 1);
	public float SpeedBonus => Count( PerkKind.Move ) > 0 ? MoveSpeedBonus : 0f;
	public float StaminaBonus => Count( PerkKind.Move ) == 0 ? 0f : 0.1f + 0.05f * (Count( PerkKind.Move ) - 1);

	public static PlayerPerks Of( Node controller ) => controller?.GetNodeOrNull<PlayerPerks>( "PlayerPerks" );
	/// <summary> Tests: pickups taken, shocks dealt and jumps made, clips thrown, blasts let off. </summary>
	public static int Taken, Shocks, ShockJumps, ClipsThrown, Blasts;

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
		blastWave = WaveDirector.Current?.Wave ?? 0;
		blastFrame = Engine.GetProcessFrames();
		Blasts++;
		var at = controller.GlobalPosition + Vector3.Up * 1f;
		BlueBlast.Spawn( controller.GetTree().CurrentScene, at, BlastRadius );
		HurtCreatures( at, BlastRadius, BlastDamage, controller );
		controller.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 1f );
		Controls.Rumble( 0.7f );
	}

	/// <summary>
	/// Straight damage to a creature's health (no part multipliers: a blast or a shock does what it says), with the
	/// points for the hit or the kill going to the shooter.
	/// </summary>
	public static void Hurt( Creature creature, float damage, Vector3 where, Vector3 from, PlayerController shooter )
	{
		if ( creature?.Health is not { } health || creature.IsDead ) return;
		health.OnDamage( new DamageInfo { Damage = damage, Position = where, Origin = from, Attacker = shooter } );
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
		if ( Count( PerkKind.Electric ) == 0 || Rng.Float() > ShockChance ) return;
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

/// <summary> An emptied magazine thrown by the player (Explosive Round): tumbles, drops, and explodes on what it hits. </summary>
public partial class ThrownClip : Node3D
{
	Vector3 velocity;
	float damage;
	PlayerController thrower;
	TimeSince age;

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
		velocity += Vector3.Down * 7f * dt;
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
