using Godot;

/// <summary> The Pinball-Bot's numbers (what one skill point does, and the base kit). </summary>
public static class PinballFx
{
	// The Pinball Launcher: hold to pull the spring back, let go to launch a steel ball that bounces off everything.
	public const float PullTime = 0.6f, MinSpeed = 18f, MaxSpeed = 34f, RefSpeed = 26f, Radius = 0.14f, BumperGain = 1.12f, MaxEnemyHits = 4;
	public const float HeavySteelPer = 0.1f, SpringPer = 0.1f, BumperPer = 0.04f, JackpotPer = 0.1f, SavePer = 1.2f;
	public static int MultiballSplit( int n ) => n;
	// The Flipper Arms: left click flips the left flipper, right click the right one.
	public const float FlipReach = 3.2f, FlipCone = 0.2f /* dot */, FlipCooldown = 0.38f, FlipShove = 4f, FlipStun = 0.45f, BallFlipSpeed = 32f;
	public const float PowerFlipPer = 0.12f, WidePer = 0.1f, QuickFlipPer = 0.1f, ComboPer = 0.08f, ComboTime = 1.5f, LaunchPadPer = 0.15f, BallReturnPer = 0.15f;
	public const int ComboMax = 5;
	public static float RampDamage( int n ) => 40f + 35f * n;
	public const float RampDelay = 0.7f, RampSplash = 2.5f;
	// The roll (mouse 4).
	public const float RollCost = 30f, RollTime = 1.6f, RollBoost = 4f, RollReach = 1.3f, RollDamage = 50f, EyeDrop = 0.75f;
	public const float SpinUpPer = 0.15f, LongRollPer = 0.4f, StrikePer = 0.15f, TiltPer = 3f;
	public const int HighScoreNeeds = 3;
	public static float HighScoreDamage( int n ) => 70f + 45f * n;
	public const float HighScoreRadius = 4f;

	public static readonly Color Chrome = new( 0.88f, 0.9f, 0.95f ), Neon = new( 1f, 0.3f, 0.85f ), Yellow = new( 1f, 0.86f, 0.15f ), Purple = new( 0.36f, 0.14f, 0.72f );

	/// <summary> Tests: how often each thing happened. </summary>
	public static int Balls, BallHits, Splits, Flips, FlipHits, BallsFlipped, Ramps, Rolls, Bowled, HighScores;
	public static float FastestBall;
}

/// <summary>
/// The Pinball-Bot's guns. The Pinball Launcher (slot 1): hold left click to pull its spring, let go to launch a
/// steel ball (`Pinball`): it bounces off the world and off enemies like bumpers, faster with each enemy it hits
/// (a DING each time). The Flipper Arms (slot 2): two big flippers on the arms; left click flips the left one, right
/// click the right one, whacking what is in front up and away (light enemies fly) and slapping any pinball in reach
/// back out at the crosshair, harder.
/// </summary>
public partial class PlayerWeapon
{
	public bool Pinball, Flippers;
	float spring;
	bool pulling;
	TimeSince sinceLeftFlip = 9f, sinceRightFlip = 9f;
	double lastFlipHitAt = -99;
	int combo;
	public float Spring => spring;
	public int Combo => combo;

	public static PlayerWeapon PinballLauncher( int slot = 1 ) => new()
	{
		Name = "PinballLauncher", DisplayName = "Pinball Launcher", Slot = slot, ModelName = "pinball", Pinball = true,
		Damage = 42f, FireRate = 2.4f, Range = 40f, SpreadDegrees = 0f, Recoil = 1.4f,
		MagazineSize = 5, StartingReserve = 60, ReloadTime = 1.8f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.15f, FovKickPerShot = 1.2f, ShootSound = "flipper",
	};

	public static PlayerWeapon FlipperArms( int slot = 2 ) => new()
	{
		Name = "FlipperArms", DisplayName = "Flipper Arms", Slot = slot, ModelName = "flippers", Flippers = true,
		Damage = 40f, FireRate = 1f / PinballFx.FlipCooldown, Range = PinballFx.FlipReach, SpreadDegrees = 0f, Recoil = 0.6f,
		MagazineSize = 1, StartingReserve = 0, ReloadTime = 0.1f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.2f, FovKickPerShot = 1.5f, ShootSound = "flipper",
	};

	float PullSpeed => 1f / PinballFx.PullTime;

	readonly Dictionary<string, Node3D> parts = new();
	ViewModel partsOf;
	/// <summary> A named piece of the gun in view (the spring, the flippers), looked up once per view model. </summary>
	Node3D Part( string name )
	{
		if ( ViewModel is null ) return null;
		if ( partsOf != ViewModel ) { parts.Clear(); partsOf = ViewModel; }
		if ( !parts.TryGetValue( name, out var n ) || (n is not null && !IsInstanceValid( n )) ) parts[name] = n = ViewModel.FindChild( name, true, false ) as Node3D;
		return n;
	}

	void UpdatePinball( float dt, bool free )
	{
		if ( controller.In.JustPressed( "reload" ) ) StartReload();
		var held = free && controller.In.Pressed( "attack1" ) && !IsReloading && (Ammo > 0 || InfiniteAmmo);
		if ( free && controller.In.JustPressed( "attack1" ) && Ammo <= 0 && !InfiniteAmmo ) { if ( Reserve > 0 ) StartReload(); else Sfx.Play2D( "dry_fire" ); }
		if ( held && TimeSinceShot >= 1f / CurrentFireRate )
		{
			if ( !pulling ) { pulling = true; spring = 0f; Sfx.Play2D( "trident_charge", 0.3f, 1.8f, Sfx.GunBus ); }
			spring = MathF.Min( 1f, spring + dt * PullSpeed );
		}
		else if ( pulling ) LaunchBall();
		if ( Part( "Spring" ) is { } s ) s.Scale = new Vector3( 1f, 1f, 1f - 0.6f * spring );
	}

	/// <summary> Testing aid: launch a ball with the spring pulled this far (0 to 1). </summary>
	public void TestLaunch( float pull ) { pulling = true; spring = pull; TimeSinceShot = 999f; LaunchBall(); }

	void LaunchBall()
	{
		pulling = false;
		var pull = spring;
		spring = 0f;
		if ( !BotShot( 0.8f, 1.3f - 0.3f * pull ) ) return;
		PinballFx.Balls++;
		var speed = MathX.Lerp( PinballFx.MinSpeed, PinballFx.MaxSpeed, pull ) * (1f + PinballFx.SpringPer * Skill( SkillId.SuperSpring ));
		var aim = Aim;
		var from = controller.SeenFromOutside ? controller.EyeTransform.Origin + aim * 0.7f : Eye + aim * 0.6f - controller.Camera.GlobalBasis.Y * 0.12f;
		global::Pinball.Fire( this, controller, from, aim * speed, CurrentDamage * (1f + PinballFx.HeavySteelPer * Skill( SkillId.HeavySteel )) );
		Sfx.Play2D( "boing", 0.4f, 1.6f, Sfx.GunBus );
	}

	void UpdateFlippers( float dt, bool free )
	{
		Ammo = MagazineSize;
		var cooldown = PinballFx.FlipCooldown * MathF.Max( 0.4f, 1f - PinballFx.QuickFlipPer * Skill( SkillId.QuickFlip ) );
		if ( free && controller.In.JustPressed( "attack1" ) && sinceLeftFlip >= cooldown ) Flip( true );
		if ( free && controller.In.JustPressed( "attack2" ) && sinceRightFlip >= cooldown ) Flip( false );
		if ( ViewModel is null ) return;
		if ( Part( "Left Flipper" ) is { } l ) l.Rotation = new Vector3( 0f, 0f, MathX.Rad( 55f * Swing( sinceLeftFlip ) ) );
		if ( Part( "Right Flipper" ) is { } r ) r.Rotation = new Vector3( 0f, 0f, -MathX.Rad( 55f * Swing( sinceRightFlip ) ) );
	}

	/// <summary> The paddles snap up and fall back. </summary>
	static float Swing( float t ) => t < 0.06f ? t / 0.06f : MathF.Max( 0f, 1f - (t - 0.06f) / 0.22f );
	/// <summary> For the third person gun: how far each flipper is up (0 to 1) and how far the spring is pulled. </summary>
	public float FlipSwing( bool left ) => Swing( left ? sinceLeftFlip : sinceRightFlip );
	public float SpringPull => spring;

	/// <summary> Flips one flipper (left or right), as a click does; also the testing hook. </summary>
	public void Flip( bool left )
	{
		PinballFx.Flips++;
		if ( left ) sinceLeftFlip = 0f; else sinceRightFlip = 0f;
		Sfx.Play2D( "flipper", 0.9f, Rng.Float( 0.95f, 1.08f ), Sfx.GunBus );
		juice?.Shake( 0.12f );
		Controls.Rumble( 0.2f );
		var eye = Eye;
		var aim = Aim;
		var flatAim = aim.Flat().SafeNormal();
		var side = flatAim.Cross( Vector3.Up ) * (left ? -1f : 1f);
		var reach = PinballFx.FlipReach * (1f + PinballFx.WidePer * Skill( SkillId.WideFlipper ));
		var feet = controller.GlobalPosition;
		// Combo: hits that keep coming hit harder.
		if ( Clock.Now - lastFlipHitAt > PinballFx.ComboTime ) combo = 0;
		var damage = CurrentDamage * (1f + PinballFx.PowerFlipPer * Skill( SkillId.PowerFlip )) * (1f + PinballFx.ComboPer * Skill( SkillId.Combo ) * combo);
		var knock = PinballFx.FlipShove * (1f + PinballFx.LaunchPadPer * Skill( SkillId.LaunchPad ));
		var ramp = Skill( SkillId.Ramp );
		var hits = 0;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var part = SkillFx.NearestPart( c, eye );
			var to = part - eye;
			var flat = (part - feet).Flat();
			if ( to.Length() > reach + 0.4f || flat.Normalized().Dot( flatAim ) < PinballFx.FlipCone ) continue;
			hits++;
			PinballFx.FlipHits++;
			SkillFx.HitPart( c, damage, eye, controller );
			if ( c.IsDead ) { MarkKill(); continue; }
			// The flipper's side throws it off at an angle, the way a flipper sends a ball.
			SkillFx.Shove( c, (flatAim + side * -0.35f).SafeNormal() * knock + Vector3.Up * 0.9f );
			c.Stun( PinballFx.FlipStun );
			if ( ramp > 0 && c.Pullable ) PinballRamp.Launch( controller, c, PinballFx.RampDamage( ramp ) * UpgradeScale );
		}
		if ( hits > 0 )
		{
			MarkHit();
			lastFlipHitAt = Clock.Now;
			combo = Math.Min( PinballFx.ComboMax, combo + 1 );
			Sfx.Play2D( "pinball_ding", 0.6f, 1f + 0.08f * combo, Sfx.GunBus );
			juice?.Shake( 0.25f );
		}
		// A pinball in reach is slapped back out at the crosshair, harder.
		var target = Trace.Ray( controller, eye, eye + aim * 40f, Layers.World | Layers.Creature, new Godot.Collections.Array<Rid> { controller.GetRid() } );
		var point = target.Hit ? target.HitPosition : eye + aim * 40f;
		foreach ( var ball in BotBall.All.OfType<global::Pinball>().ToList() )
		{
			if ( ball.Owner != controller || ball.Ended || ball.GlobalPosition.DistanceTo( eye ) > reach + 0.6f ) continue;
			ball.Flipped( (point - ball.GlobalPosition).SafeNormal(), 1f + PinballFx.BallReturnPer * Skill( SkillId.BallReturn ) );
		}
		// And the loose gore in front gets a whack too.
		if ( GoreSystem.Get( controller ) is { } gore )
			foreach ( var gib in gore.Gibs )
				if ( IsInstanceValid( gib ) && gib.GlobalPosition.DistanceTo( eye ) < reach && (gib.GlobalPosition - feet).Flat().Normalized().Dot( flatAim ) > 0f )
					gib.ApplyCentralImpulse( (aim + Vector3.Up * 0.4f) * 3f * gib.Mass );
	}
}

/// <summary>
/// A steel pinball. It bounces off the world and rolls; an enemy it meets is a bumper: a DING, its damage (more the
/// faster it goes, and with Jackpot more for each enemy it hit before), and a bounce off it faster than it came
/// (`BumperGain`, more with Bumper). Magnetic Save bends it toward enemies; Multiball splits it on its first enemy.
/// Flippers slap it back out harder.
/// </summary>
public partial class Pinball : BotBall
{
	float damage, bonus = 1f;
	bool split;

	public static Pinball Fire( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 velocity, float damage, bool split = false )
	{
		var b = new Pinball { Name = "Pinball", damage = damage, split = split };
		b.Radius = PinballFx.Radius;
		b.Gravity = 3.5f;
		b.Drag = 0.05f;
		b.Restitution = 0.82f;
		b.BumperGain = PinballFx.BumperGain + PinballFx.BumperPer * Skills.Rank( owner, SkillId.Bumper );
		b.MaxBounces = 7;
		b.Life = 5f;
		b.HitGap = 0.25f;
		b.Launch( weapon, owner, from, velocity );
		return b;
	}

	public static void Look( Node3D parent, float r )
	{
		var chrome = GunKit.Paint( PinballFx.Chrome, 0.05f, 1f );
		GunKit.Ball( parent, Vector3.Zero, r, chrome );
		var glint = Sculpt.Glow( Colors.White, 2f );
		Sculpt.Blob( parent, new Vector3( -0.35f, 0.45f, 0.4f ) * r, Vector3.One * r * 0.35f, glint, null, "Glint" );
	}

	protected override void Build()
	{
		Look( look, Radius );
		var trail = new OmniLight3D { LightColor = PinballFx.Neon, LightEnergy = 0.8f, OmniRange = 1.5f, ShadowEnabled = false };
		AddChild( trail );

	}

	protected override void Step( float dt )
	{
		var save = Skills.Rank( owner, SkillId.MagneticSave );
		if ( save <= 0 ) return;
		Creature best = null;
		var bestD = 10f;
		var at = GlobalPosition;
		foreach ( var c in Creature.Everyone )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var d = c.GlobalPosition.DistanceTo( at );
			if ( d < bestD && (c.GlobalPosition - at).Normalized().Dot( Velocity.Normalized() ) > 0.3f ) { bestD = d; best = c; }
		}
		if ( best is null ) return;
		var speed = Velocity.Length();
		var want = (SkillFx.NearestPart( best, at ) - at).SafeNormal() * speed;
		Velocity = Velocity.Lerp( want, MathF.Min( 1f, PinballFx.SavePer * save * dt ) ).Normalized() * speed;
	}

	protected override bool HitEnemy( Creature c, Node3D part, Vector3 at, float speed )
	{
		PinballFx.BallHits++;
		PinballFx.FastestBall = MathF.Max( PinballFx.FastestBall, speed );
		var jackpot = 1f + PinballFx.JackpotPer * Skills.Rank( owner, SkillId.Jackpot ) * (enemyHits - 1);
		Strike( c, part, at, damage * bonus * jackpot * MathX.Clamp( speed / PinballFx.RefSpeed, 0.4f, 2f ) );
		Sfx.Play( "pinball_ding", at, 0.9f, 1f + 0.12f * (enemyHits - 1) );
		if ( c.IsDead ) weapon?.MarkKill(); else { weapon?.MarkHit(); SkillFx.Shove( c, Velocity.Flat().SafeNormal() * 0.5f ); }
		var multi = Skills.Rank( owner, SkillId.Multiball );
		if ( multi > 0 && !split && enemyHits == 1 )
		{
			// Multiball: it splits into more balls off the first bumper.
			split = true;
			for ( var i = 0; i < PinballFx.MultiballSplit( multi ); i++ )
			{
				PinballFx.Splits++;
				var dir = (-Velocity.SafeNormal() + Rng.OnSphere() * 0.8f + Vector3.Up * 0.3f).SafeNormal();
				Fire( weapon, owner, GlobalPosition + dir * 0.3f, dir * MathF.Max( speed, PinballFx.MinSpeed ), damage * 0.6f, true );
			}
		}
		if ( enemyHits >= PinballFx.MaxEnemyHits ) { End(); return true; }
		return true;
	}

	protected override void HitWorld( Vector3 at, Vector3 normal, float speed )
	{
		if ( speed > 3f ) Sfx.Play( "mine_tink", at, MathX.Clamp( speed / 30f, 0.15f, 0.6f ), Rng.Float( 1.2f, 1.5f ) );
	}

	/// <summary> A flipper slapped it: out at the crosshair, faster and harder. </summary>
	public void Flipped( Vector3 direction, float harder )
	{
		PinballFx.BallsFlipped++;
		Velocity = direction * MathF.Max( Velocity.Length() * 1.2f, PinballFx.BallFlipSpeed );
		bonus *= harder;
		bounces = 0;
		Life = age + 4f;
		Sfx.Play( "pinball_ding", GlobalPosition, 0.8f, 1.6f );
	}

	protected override void Finish()
	{
		if ( WaterFeel.IsOn() ) for ( var i = 0; i < 4; i++ ) WaterBubble.Create( GlobalPosition + Rng.InSphere( 0.1f ), Rng.Float( 0.2f, 0.4f ), 0f, Rng.Float( 0.3f, 0.6f ) );
	}
}

/// <summary> Ramp: a flipped enemy flies up and comes down hard a moment later, hurting what it lands near. </summary>
public partial class PinballRamp : Node
{
	PlayerController owner;
	Creature victim;
	float damage, age;

	public static void Launch( PlayerController owner, Creature c, float damage )
	{
		PinballFx.Ramps++;
		var r = new PinballRamp { Name = "Ramp", owner = owner, victim = c, damage = damage };
		owner.GetTree().CurrentScene.AddChild( r );
		c.Stun( PinballFx.RampDelay + 0.3f );
	}

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		age += dt;
		if ( victim is null || !IsInstanceValid( victim ) || victim.IsDead || !IsInstanceValid( owner ) ) { QueueFree(); return; }
		// Up, then down: a little arc over the ramp.
		var t = age / PinballFx.RampDelay;
		var lift = t < 0.5f ? 6f : -6f;
		victim.Pull( Vector3.Up * lift * dt );
		if ( age < PinballFx.RampDelay ) return;
		var at = victim.GlobalPosition;
		SkillFx.HitPart( victim, damage, at + Vector3.Up, owner );
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( c == victim || !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || c.GlobalPosition.DistanceTo( at ) > PinballFx.RampSplash ) continue;
			SkillFx.HitPart( c, damage * 0.5f, at, owner );
		}
		Shockwave.Spawn( GetTree().CurrentScene, at + Vector3.Up * 0.3f, PinballFx.RampSplash, PinballFx.Neon );
		Sfx.Play( "dive_impact", at, 0.7f, 1.3f );
		Sfx.Play( "pinball_ding", at, 0.7f, 0.8f );
		QueueFree();
	}
}

/// <summary> The Pinball-Bot's gun models, on the Blaster's rig. </summary>
public static class PinballModels
{
	public static readonly Vector3 LauncherMuzzle = new( 26f, 0f, 3.4f ), FlipperMuzzle = new( 18f, 0f, 3f );

	public static void Launcher( Node3D gun )
	{
		var purple = GunKit.Paint( PinballFx.Purple, 0.35f );
		var chrome = GunKit.Paint( PinballFx.Chrome, 0.08f, 1f );
		var yellow = GunKit.Paint( PinballFx.Yellow, 0.35f );
		var neon = GunKit.Paint( PinballFx.Neon, 0.3f, 0f, 3f );
		var red = GunKit.Paint( new Color( 0.9f, 0.15f, 0.2f ), 0.3f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), purple, 18f );
		// A purple cabinet-coloured body with neon strips, a clear chrome lane for the ball, and the spring plunger at
		// the back with its red knob (it squashes as you pull).
		GunKit.Box( gun, new Vector3( 8f, 0f, 3f ), new Vector3( 16f, 4f, 3.4f ), purple );
		foreach ( var y in new[] { -2.05f, 2.05f } ) GunKit.Box( gun, new Vector3( 8f, y, 3f ), new Vector3( 16f, 0.3f, 1f ), neon );
		GunKit.AlongX( gun, new Vector3( 19f, 0f, 3.4f ), 1.2f, 12f, chrome, 1.2f, 16 );
		var spring = new Node3D { Name = "Spring", Position = new Vector3( 0f, 0f, 0f ) };
		gun.AddChild( spring );
		for ( var i = 0; i < 5; i++ ) GunKit.AlongX( spring, new Vector3( 1.4f + i * 0.8f, 0f, 5.4f ), 0.6f, 0.3f, chrome, -1f, 12 );
		GunKit.Ball( gun, new Vector3( 0.4f, 0f, 5.4f ), 1f, red );
		GunKit.Box( gun, new Vector3( 10f, 0f, 4.9f ), new Vector3( 5f, 2.6f, 0.4f ), yellow );
		var loaded = new Node3D { Name = "Loaded" };
		gun.AddChild( loaded );
		GunKit.Ball( loaded, new Vector3( 24.5f, 0f, 3.4f ), 1.1f, chrome );
		GunKit.AlongX( gun, new Vector3( -3.4f, 0f, 2.5f ), 1.1f, 3.8f, purple, 1.3f, 12 );
	}

	public static void Flippers( Node3D gun )
	{
		var purple = GunKit.Paint( PinballFx.Purple, 0.35f );
		var chrome = GunKit.Paint( PinballFx.Chrome, 0.08f, 1f );
		var yellow = GunKit.Paint( PinballFx.Yellow, 0.35f );
		var rubber = GunKit.Paint( new Color( 0.95f, 0.95f, 0.95f ), 0.6f );
		var neon = GunKit.Paint( PinballFx.Neon, 0.3f, 0f, 3f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), purple, 18f );
		// A bumper on a short mount, with two big yellow flippers hinged either side (each a pivot node that swings).
		GunKit.Box( gun, new Vector3( 4f, 0f, 2.6f ), new Vector3( 8f, 3.2f, 3f ), purple );
		GunKit.AlongX( gun, new Vector3( 10f, 0f, 3f ), 2.2f, 2.4f, neon, -1f, 18 );
		GunKit.AlongX( gun, new Vector3( 11.4f, 0f, 3f ), 2.4f, 0.6f, chrome, -1f, 18 );
		foreach ( var left in new[] { true, false } )
		{
			var s = left ? 1f : -1f;
			var pivot = new Node3D { Name = left ? "Left Flipper" : "Right Flipper", Position = new Vector3( 8f, s * 3.6f, 3f ) };
			gun.AddChild( pivot );
			GunKit.AlongX( pivot, new Vector3( 0f, 0f, 0f ), 1.2f, 1.6f, chrome, -1f, 14 );
			GunKit.Spike( pivot, new Vector3( 0f, 0f, 0f ), new Vector3( 11f, -s * 2.4f, 0f ), 1.1f, yellow, 12 );
			GunKit.Spike( pivot, new Vector3( 0.4f, 0f, 0f ), new Vector3( 10.6f, -s * 2.4f, 0f ), 1.25f, rubber, 12 ).Scale = new Vector3( 1f, 1f, 0.5f );
		}
		GunKit.AlongX( gun, new Vector3( -3.4f, 0f, 2.5f ), 1.1f, 3.8f, purple, 1.3f, 12 );
	}
}
