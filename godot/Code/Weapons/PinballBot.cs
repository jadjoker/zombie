using Godot;

/// <summary> The Pinball-Bot's numbers (what one skill point does, and the base kit). </summary>
public static class PinballFx
{
	// The Pinball Launcher: hold to pull the spring back, let go to launch a steel ball that bounces off everything.
	public const float PullTime = 0.6f, MinSpeed = 18f, MaxSpeed = 34f, RefSpeed = 26f, Radius = 0.14f;
	public const int MaxEnemyHits = 12, ReboundPer = 2;   // a ball chains through a crowd (user's ask), each hit lighter
	// Off an enemy a ball is kicked out like off a pop bumper: it sits in it a beat (`BumpPause`), then leaves at least
	// `BumperKick` fast (`BumperGain` faster than it came, up to `MaxBallSpeed`), steered `ChainAim` of the way (most of it) toward
	// the next enemy within `ChainRange` ahead of it, so a good shot chains through a crowd.
	public const float BumperGain = 1.1f, BumperKick = 20f, MaxBallSpeed = 46f, ChainAim = 0.97f, ChainRange = 14f, ChainCone = 0.15f, BumpPause = 0.035f;
	public const float HeavySteelPer = 0.1f, SpringPer = 0.1f, BumperPer = 0.04f, JackpotPer = 0.04f, RicochetAimPer = 0.04f;
	public static int MultiballSplit( int n ) => n;
	// The Flipper Arms (user's design, real flippers): a click raises that flipper and holding keeps it up; the flip
	// whacks what is in front, launching light enemies as human pinballs that bounce off the others. A flip that meets an
	// enemy out at the flipper's tip (`SweetFrom` of the reach and on) is PERFECT: harder, and it flies faster. Holding a
	// flipper up catches your own pinballs on it (a cradle); let go and the ball rolls down it for `CradleWindow`, and a
	// flip then is a skill shot (`SkillShotBonus`) at the crosshair.
	public const float FlipReach = 3.4f, FlipCone = 0.2f /* dot */, FlipCooldown = 0.3f, FlipStun = 0.45f, BallFlipSpeed = 32f;
	public const float SweetFrom = 0.6f, SweetBonus = 1.6f, LaunchSpeed = 12f, PerfectSpeed = 19f, FlightMax = 1.5f, HumanRadius = 1.1f, HumanShare = 0.8f;
	public const int HumanHits = 3;
	public const float CradleHold = 3f, CradleWindow = 0.4f, SkillShotBonus = 2f;
	public const float PowerFlipPer = 0.12f, WidePer = 0.1f, QuickFlipPer = 0.1f, SweetSpotPer = 0.08f, SweetBonusPer = 0.15f, LaunchPadPer = 0.15f, BallReturnPer = 0.15f;
	public const float ComboTime = 1.5f;
	public const int ComboMax = 9;
	public static float RampDamage( int n ) => 40f + 35f * n;
	public const float RampSplash = 2.5f;
	// The roll (mouse 4).
	// The roll is its own physics (user's ask): momentum you steer, faster downhill, bouncing off walls, a hop on jump;
	// enemies it meets take damage by its speed (`RollDamagePer` a m/s), light ones fly off as human pinballs and slow it
	// a little, heavy ones bounce it off. Third person while it lasts.
	public const float RollCost = 30f, RollTime = 3.5f, RollStart = 9f, RollTop = 12f, RollAccel = 16f, RollGravity = 14f, RollHop = 5.5f;
	public const float RollReach = 1.25f, RollDamagePer = 9f, RollMinSpeed = 3f, RollRehit = 0.45f, EyeDrop = 0.75f;
	public const float SpinUpPer = 0.15f, LongRollPer = 0.6f, StrikePer = 0.15f, TiltPer = 3f;
	public const int HighScoreNeeds = 3;
	public static float HighScoreDamage( int n ) => 70f + 45f * n;
	public const float HighScoreRadius = 4f;

	public static readonly Color Chrome = new( 0.88f, 0.9f, 0.95f ), Neon = new( 1f, 0.3f, 0.85f ), Yellow = new( 1f, 0.86f, 0.15f ), Purple = new( 0.36f, 0.14f, 0.72f ), Cyan = new( 0.3f, 0.95f, 1f );

	/// <summary> Tests: how often each thing happened. </summary>
	public static int Assisted, Balls, BallHits, Chains, LongestChain, Splits, Flips, FlipHits, Perfects, BallsFlipped, Cradles, SkillShots, Launched, HumanHitsDone, Ramps, Rolls, Bowled, HighScores;
	public static float FastestBall;

	/// <summary> The next enemy a ball (or a flung enemy) could go on to: in front of `dir` within `range`, not `skip`. </summary>
	public static Creature NextTarget( Vector3 from, Vector3 dir, float range, Creature skip, Func<Creature, bool> exclude = null, float minDot = ChainCone )
	{
		Creature best = null;
		var bestScore = float.MaxValue;
		foreach ( var c in Creature.Everyone )
		{
			if ( c == skip || !GodotObject.IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || (exclude?.Invoke( c ) ?? false) ) continue;
			var to = SkillFx.NearestPart( c, from ) - from;
			var d = to.Length();
			if ( d > range || d < 0.2f ) continue;
			var dot = (to / d).Dot( dir );
			if ( dot < minDot ) continue;
			var score = d * (2f - dot);   // near and straight ahead
			if ( score < bestScore ) { bestScore = score; best = c; }
		}
		return best;
	}

	/// <summary> Where to send a ball at an enemy: the middle of its parts (its nearest part can be a leg tip a ball slips past). </summary>
	public static Vector3 AimPoint( Creature c )
	{
		var sum = Vector3.Zero;
		var n = 0;
		foreach ( var p in CreaturePart.All ) if ( GodotObject.IsInstanceValid( p ) && p.Creature == c ) { sum += p.GlobalPosition; n++; }
		return n > 0 ? sum / n : c.GlobalPosition + Vector3.Up * 0.4f;
	}

	/// <summary> The pinball hit sounds (cut from the user's recordings), a little higher up a chain. </summary>
	public static void HitSound( Vector3 at, int chain, float volume = 0.9f, bool heavy = false )
	{
		Sfx.Play( heavy ? $"pinball_hit_{Rng.Int( 5, 6 )}" : $"pinball_hit_{Rng.Int( 1, 4 )}", at, volume, MathF.Min( 1.6f, 0.95f + 0.07f * (chain - 1) ) * Rng.Float( 0.97f, 1.03f ) );
		if ( chain >= 3 ) Sfx.Play( "pinball_ding", at, 0.5f + 0.05f * chain, 1f + 0.06f * chain );
	}
}

/// <summary>
/// The Pinball-Bot's guns. The Pinball Launcher (slot 1): hold left click to pull its plunger, let go to launch a
/// steel ball (`Pinball`) that bounces off the world and is kicked off enemies like off pop bumpers, chaining from one
/// to the next. The Flipper Arms (slot 2): real flippers, left click the left one and right click the right one (see
/// `PinballFx`): flips launch enemies as human pinballs, a tip hit is PERFECT, a held flipper cradles your pinballs and
/// a flip as one rolls off it is a skill shot.
/// </summary>
public partial class PlayerWeapon
{
	public bool Pinball, Flippers;
	float spring;
	bool pulling;
	bool leftUp, rightUp;
	/// <summary> Testing: hold a flipper up as if its button were held. </summary>
	public bool TestHoldLeft, TestHoldRight;
	TimeSince sinceLeftMove = 9f, sinceRightMove = 9f, sinceLeftFlip = 9f, sinceRightFlip = 9f;
	double lastFlipHitAt = -99;
	int combo;
	public float Spring => spring;
	public int Combo => combo;
	/// <summary> Testing: hold the launcher's trigger down (pull the plunger). </summary>
	public bool TestHoldPull;

	public static PlayerWeapon PinballLauncher( int slot = 1 ) => new()
	{
		Name = "PinballLauncher", DisplayName = "Pinball Launcher", Slot = slot, ModelName = "pinball", Pinball = true,
		Damage = 22f, FireRate = 2.4f, Range = 40f, SpreadDegrees = 0f, Recoil = 1.4f,
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

	void UpdatePinball( float dt, bool free )
	{
		if ( controller.In.JustPressed( "reload" ) ) StartReload();
		var held = (TestHoldPull || free && controller.In.Pressed( "attack1" )) && !IsReloading && (Ammo > 0 || InfiniteAmmo);
		if ( free && controller.In.JustPressed( "attack1" ) && Ammo <= 0 && !InfiniteAmmo ) { if ( Reserve > 0 ) StartReload(); else Sfx.Play2D( "dry_fire" ); }
		if ( held && TimeSinceShot >= 1f / CurrentFireRate )
		{
			if ( !pulling ) { pulling = true; spring = 0f; Sfx.Play2D( "trident_charge", 0.3f, 1.8f, Sfx.GunBus ); }
			spring = MathF.Min( 1f, spring + dt * PullSpeed );
		}
		else if ( pulling ) LaunchBall();
		if ( ViewModel is not null ) PinballModels.PoseLauncher( ViewModel, spring, Ammo, MagazineSize, TimeSinceShot );
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
		// Aim assist, the Gore Vac's (user's ask): with no enemy right under the crosshair, one close beside its line is aimed at.
		var eye = controller.Camera?.GlobalPosition ?? Eye;
		var ray = Trace.Ray( controller, eye, eye + aim * 80f, Layers.World | Layers.Creature, new Godot.Collections.Array<Rid> { controller.GetRid() } );
		var reach = ray.Hit ? ray.HitPosition.DistanceTo( eye ) + 1f : 80f;
		if ( Shootables.Find( ray.Node )?.ShotOwner is not Creature && GoreBall.NearestToLine( eye, aim, reach, 1.4f ) is { } assisted ) { aim = (assisted - from).SafeNormal(); PinballFx.Assisted++; }
		global::Pinball.Fire( this, controller, from, aim * speed, CurrentDamage * (1f + PinballFx.HeavySteelPer * Skill( SkillId.HeavySteel )) );
		Sfx.Play2D( "boing", 0.4f, 1.6f, Sfx.GunBus );
		Sfx.Play2D( "flipper", 0.5f, 0.8f + 0.3f * pull, Sfx.GunBus );
	}

	// ---- The flippers ----

	void UpdateFlippers( float dt, bool free )
	{
		Ammo = MagazineSize;
		if ( free && controller.In.JustPressed( "attack1" ) ) Raise( true );
		if ( leftUp && !TestHoldLeft && (!free || controller.In.JustReleased( "attack1" ) || !controller.In.Pressed( "attack1" )) ) Lower( true );
		if ( free && controller.In.JustPressed( "attack2" ) ) Raise( false );
		if ( rightUp && !TestHoldRight && (!free || controller.In.JustReleased( "attack2" ) || !controller.In.Pressed( "attack2" )) ) Lower( false );
		Cradle( dt );
		if ( ViewModel is not null ) PinballModels.PoseFlippers( ViewModel, FlipSwing( true ), FlipSwing( false ) );
	}

	/// <summary> A flipper goes up (a click): the flip, if it is off cooldown. </summary>
	public void Raise( bool left )
	{
		if ( left ? leftUp : rightUp ) return;
		if ( left ) { leftUp = true; sinceLeftMove = 0f; } else { rightUp = true; sinceRightMove = 0f; }
		var cooldown = PinballFx.FlipCooldown * MathF.Max( 0.4f, 1f - PinballFx.QuickFlipPer * Skill( SkillId.QuickFlip ) );
		if ( (left ? sinceLeftFlip : sinceRightFlip) < cooldown ) { Sfx.Play2D( "flipper", 0.4f, 1.2f, Sfx.GunBus ); return; }
		if ( left ) sinceLeftFlip = 0f; else sinceRightFlip = 0f;
		DoFlip( left );
	}

	/// <summary> A flipper comes down (the button let go): a ball on it starts to roll off. </summary>
	public void Lower( bool left )
	{
		if ( !(left ? leftUp : rightUp) ) return;
		if ( left ) { leftUp = false; sinceLeftMove = 0f; } else { rightUp = false; sinceRightMove = 0f; }
		foreach ( var ball in BotBall.All.OfType<global::Pinball>() )
			if ( ball.Owner == controller && ball.OnFlipper == (left ? 1 : 2) ) ball.RollOff();
	}

	/// <summary> Testing: a quick flip (up and straight back down). </summary>
	public void Flip( bool left ) { Raise( left ); Lower( left ); }

	/// <summary> How far each flipper is up (0 to 1): it snaps up fast and falls back a little slower. </summary>
	public float FlipSwing( bool left )
	{
		var up = left ? leftUp : rightUp;
		var t = left ? (float)sinceLeftMove : (float)sinceRightMove;
		if ( up ) return MathF.Min( 1f, t / 0.045f );
		// Down: from up, after a quick flip it still reaches the top first.
		var flip = left ? (float)sinceLeftFlip : (float)sinceRightFlip;
		if ( flip < 0.045f ) return flip / 0.045f;
		return MathF.Max( 0f, 1f - t / 0.11f );
	}
	public float SpringPull => spring;

	Vector3 FlipperTip( bool left )
	{
		// In view the ball sits on the gun's own flipper tip; seen from outside, just ahead of that side.
		if ( !controller.SeenFromOutside && ViewModel is not null && PinballModels.FlipperTip( ViewModel, left ) is { } tip ) return tip;
		var aim = Aim;
		var side = aim.Flat().SafeNormal().Cross( Vector3.Up ) * (left ? -1f : 1f);
		return Eye + aim * 1.25f + side * 0.22f - controller.Camera.GlobalBasis.Y * 0.3f;
	}

	/// <summary> Held flippers catch your pinballs that come near (a cradle); a ball sits at that flipper's tip. </summary>
	void Cradle( float dt )
	{
		if ( !leftUp && !rightUp ) return;
		var eye = Eye;
		var aim = Aim;
		var reach = PinballFx.FlipReach * (1f + PinballFx.WidePer * Skill( SkillId.WideFlipper ));
		foreach ( var ball in BotBall.All.OfType<global::Pinball>().ToList() )
		{
			if ( ball.Owner != controller || ball.Ended ) continue;
			if ( ball.OnFlipper == 0 )
			{
				if ( ball.SinceFlipped < 0.4f || ball.GlobalPosition.DistanceTo( eye ) > reach + 0.5f || (ball.GlobalPosition - eye).SafeNormal().Dot( aim ) < 0.1f ) continue;
				var left = leftUp && (!rightUp || (ball.GlobalPosition - eye).Dot( aim.Cross( Vector3.Up ) ) < 0f);
				ball.Catch( left ? 1 : 2 );
				PinballFx.Cradles++;
				Sfx.Play2D( "pinball_hit_2", 0.5f, 0.7f, Sfx.GunBus );
			}
			if ( ball.OnFlipper == 1 && leftUp ) ball.HoldAt( FlipperTip( true ), dt );
			else if ( ball.OnFlipper == 2 && rightUp ) ball.HoldAt( FlipperTip( false ), dt );
		}
	}

	void DoFlip( bool left )
	{
		PinballFx.Flips++;
		Sfx.Play2D( "flipper", 0.95f, Rng.Float( 0.95f, 1.08f ), Sfx.GunBus );
		juice?.Shake( 0.12f );
		Controls.Rumble( 0.2f );
		var eye = Eye;
		var aim = Aim;
		var flatAim = aim.Flat().SafeNormal();
		var side = flatAim.Cross( Vector3.Up ) * (left ? -1f : 1f);   // the flipper's own side
		var reach = PinballFx.FlipReach * (1f + PinballFx.WidePer * Skill( SkillId.WideFlipper ));
		var feet = controller.GlobalPosition;
		if ( Clock.Now - lastFlipHitAt > PinballFx.ComboTime ) combo = 0;
		var damage = CurrentDamage * (1f + PinballFx.PowerFlipPer * Skill( SkillId.PowerFlip ));
		var sweetFrom = MathF.Max( 0.3f, PinballFx.SweetFrom - PinballFx.SweetSpotPer * Skill( SkillId.SweetSpot ) );
		var sweetBonus = PinballFx.SweetBonus + PinballFx.SweetBonusPer * Skill( SkillId.SweetSpot );
		var launchPad = 1f + PinballFx.LaunchPadPer * Skill( SkillId.LaunchPad );
		var hits = 0;
		var perfect = false;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || c.Grabbed ) continue;
			var part = SkillFx.NearestPart( c, eye );
			var to = part - eye;
			var flat = (part - feet).Flat();
			var dist = to.Length();
			if ( dist > reach + 0.4f || flat.Normalized().Dot( flatAim ) < PinballFx.FlipCone ) continue;
			// Each flipper covers its own side and the middle.
			if ( flat.Normalized().Dot( side ) < -0.45f ) continue;
			hits++;
			PinballFx.FlipHits++;
			var tip = dist >= reach * sweetFrom;
			if ( tip ) { perfect = true; PinballFx.Perfects++; }
			SkillFx.HitPart( c, damage * (tip ? sweetBonus : 1f), eye, controller );
			Fx.ImpactFlesh( part, -flatAim, new Color( 0.9f, 0.3f, 0.2f ) );
			BumperFlash.Spawn( part, -flatAim, tip ? PinballFx.Yellow : PinballFx.Neon, tip ? 1.4f : 1f );
			if ( c.IsDead ) { MarkKill(); continue; }
			// Off it goes, the way a flipper sends a ball: mostly where you aim, a little across.
			var dir = (flatAim + side * -0.18f).SafeNormal();
			if ( c.Pullable && !c.Carried )
			{
				var next = PinballFx.NextTarget( part, dir, PinballFx.ChainRange, c );
				if ( next is not null ) dir = dir.Lerp( (SkillFx.NearestPart( next, part ) - part).Flat().SafeNormal(), 0.4f ).SafeNormal();
				HumanPinball.Launch( this, controller, c, (dir + Vector3.Up * (tip ? 0.28f : 0.38f)).SafeNormal() * (tip ? PinballFx.PerfectSpeed : PinballFx.LaunchSpeed) * launchPad, damage * launchPad );
			}
			else
			{
				SkillFx.Shove( c, dir * 1.5f + Vector3.Up * 0.4f );
				c.Stun( PinballFx.FlipStun );
			}
		}
		if ( hits > 0 )
		{
			MarkHit();
			lastFlipHitAt = Clock.Now;
			combo = Math.Min( PinballFx.ComboMax, combo + 1 );
			PinballFx.HitSound( eye + flatAim * 2f, combo, 1f );
			juice?.Shake( perfect ? 0.4f : 0.25f );
			if ( perfect )
			{
				Sfx.Play2D( "pinball_ding", 0.8f, 1.5f, Sfx.GunBus );
				PinballPopup.Show( eye + aim * 2.2f + Vector3.Up * 0.4f, "PERFECT!", PinballFx.Yellow, 1.3f );
				Controls.Rumble( 0.45f );
			}
		}

		// Your pinballs: one rolling off a flipper goes out as a skill shot, one cradled on the other flipper or loose in
		// reach is slapped out at the crosshair.
		var target = Trace.Ray( controller, eye, eye + aim * 40f, Layers.World | Layers.Creature, new Godot.Collections.Array<Rid> { controller.GetRid() } );
		var point = target.Hit ? target.HitPosition : eye + aim * 40f;
		var returnBonus = 1f + PinballFx.BallReturnPer * Skill( SkillId.BallReturn );
		foreach ( var ball in BotBall.All.OfType<global::Pinball>().ToList() )
		{
			if ( ball.Owner != controller || ball.Ended ) continue;
			if ( ball.OnFlipper == (left ? 1 : 2) && !ball.Rolling ) continue;   // still held up on this one: can't happen, but never fire it from under itself
			var near = ball.OnFlipper != 0 || ball.GlobalPosition.DistanceTo( eye ) <= reach + 0.6f;
			if ( !near ) continue;
			if ( ball.Rolling )
			{
				PinballFx.SkillShots++;
				ball.Flipped( (point - ball.GlobalPosition).SafeNormal(), PinballFx.SkillShotBonus * returnBonus, 1.3f );
				PinballPopup.Show( ball.GlobalPosition + Vector3.Up * 0.3f, "SKILL SHOT!", PinballFx.Cyan, 1.2f );
				Sfx.Play2D( "pinball_ding", 0.9f, 1.8f, Sfx.GunBus );
				juice?.Shake( 0.3f );
			}
			else ball.Flipped( (point - ball.GlobalPosition).SafeNormal(), returnBonus, 1f );
		}
		// And the loose gore in front gets a whack too.
		if ( GoreSystem.Get( controller ) is { } gore )
			foreach ( var gib in gore.Gibs )
				if ( IsInstanceValid( gib ) && gib.GlobalPosition.DistanceTo( eye ) < reach && (gib.GlobalPosition - feet).Flat().Normalized().Dot( flatAim ) > 0f )
					gib.ApplyCentralImpulse( (aim + Vector3.Up * 0.4f) * 3f * gib.Mass );
	}
}

/// <summary>
/// A steel pinball. It bounces off the world and rolls; an enemy it meets is a pop bumper: it sits in it a beat, a
/// flash and a clack (louder up the chain, a DING from the third), its damage (more the faster it goes, and with
/// Jackpot more for each enemy it hit before), and it is kicked out at `BumperKick` or faster toward the next enemy.
/// Multiball splits it on its first enemy. A held flipper cradles it; flipped, it flies out at the crosshair, harder.
/// </summary>
public partial class Pinball : BotBall
{
	float damage, bonus = 1f;
	bool split;
	MeshInstance3D streak;
	StandardMaterial3D streakPaint;
	OmniLight3D glow;
	/// <summary> Which flipper holds it (0 none, 1 left, 2 right), and whether it is rolling off after the button was let go. </summary>
	public int OnFlipper { get; private set; }
	public bool Rolling { get; private set; }
	float rollAge, heldAge;
	/// <summary> The enemy a kick sent it to: it homes on it until it gets there (an enemy that moves, or a bump in the sand, used to end the chain). </summary>
	Creature chasing;
	float chaseLeft;
	public Creature Chasing => chasing;
	TimeSince sinceFlipped = 9f;
	public float SinceFlipped => sinceFlipped;

	public static Pinball Fire( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 velocity, float damage, bool split = false )
	{
		var b = new Pinball { Name = "Pinball", damage = damage, split = split };
		b.Radius = PinballFx.Radius;
		b.Gravity = 3.5f;
		b.Drag = 0.05f;
		b.Restitution = 0.82f;
		b.BumperGain = PinballFx.BumperGain + PinballFx.BumperPer * Skills.Rank( owner, SkillId.Bumper );
		b.MaxBounces = 7;
		b.Life = 6f;
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
		glow = new OmniLight3D { LightColor = PinballFx.Neon, LightEnergy = 0.8f, OmniRange = 1.5f, ShadowEnabled = false };
		AddChild( glow );
		// A neon streak behind it, longer the faster it goes.
		streakPaint = new StandardMaterial3D
		{
			AlbedoColor = PinballFx.Neon with { A = 0.45f }, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		streakPaint.SetMeta( "no_caustics", true );
		streak = new MeshInstance3D { Name = "Streak", Mesh = new CylinderMesh { TopRadius = Radius * 0.85f, BottomRadius = 0.01f, Height = 1f, RadialSegments = 10, Rings = 1 }, MaterialOverride = streakPaint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( streak );
	}

	protected override void Step( float dt )
	{
		Chase( dt );
		Seek( dt );
		// The streak: from the ball back along where it came from.
		var speed = Cradled || holdFor > 0f ? 0f : Velocity.Length();
		var length = MathX.Clamp( speed * 0.035f, 0f, 1.6f );
		streak.Visible = length > 0.08f;
		if ( streak.Visible )
		{
			var back = -Velocity.SafeNormal();
			streak.GlobalTransform = new Transform3D( GunKit.Along( -back ) * Basis.FromScale( new Vector3( 1f, length, 1f ) ), GlobalPosition + back * length * 0.5f );
		}
		glow.LightEnergy = MathX.Approach( glow.LightEnergy, 0.8f, dt * 12f );
		if ( Rolling )
		{
			// Rolling down a lowered flipper toward its hinge: a flip now is a skill shot; too late and it drops.
			rollAge += dt;
			GlobalPosition += Velocity * dt;
			if ( rollAge > PinballFx.CradleWindow ) { Drop(); return; }
		}
		else if ( OnFlipper != 0 && (heldAge += dt) > PinballFx.CradleHold ) Drop();
	}

	/// <summary>
	/// Homing on the enemy the last kick chose: straight at its middle at its speed (no drop), so the chain carries on
	/// through a moving crowd. If that one dies first it picks the next; after `chaseLeft` it flies on free.
	/// </summary>
	void Chase( float dt )
	{
		if ( chasing is null || Cradled || holdFor > 0f ) return;
		chaseLeft -= dt;
		if ( !IsInstanceValid( chasing ) || chasing.IsDead )
		{
			chasing = PinballFx.NextTarget( GlobalPosition, Velocity.SafeNormal(), PinballFx.ChainRange, null, RecentlyHit, -1f );
			chaseLeft = chasing is null ? 0f : 0.6f;
		}
		if ( chasing is null || chaseLeft <= 0f ) { chasing = null; return; }
		var speed = MathF.Max( Velocity.Length(), PinballFx.BumperKick );
		var want = (PinballFx.AimPoint( chasing ) - GlobalPosition).SafeNormal() * speed;
		Velocity = Velocity.Lerp( want, MathF.Min( 1f, dt * 30f ) ).Normalized() * speed;
	}

	/// <summary>
	/// Its first flight homes gently like a Gore Vac ball (`GoreBall`'s turn, cone and range): onto the enemy nearest its
	/// heading, until the first enemy hit (after that the kicks and `Chase` steer it).
	/// </summary>
	void Seek( float dt )
	{
		if ( hitEnemy || chasing is not null || Cradled || holdFor > 0f || OnFlipper != 0 || Rolling ) return;
		if ( (sinceSeek += dt) > 0.1f )
		{
			sinceSeek = 0f;
			seeking = null;
			var heading = Velocity.SafeNormal();
			var best = float.MaxValue;
			foreach ( var c in Creature.Everyone )
			{
				if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
				var to = PinballFx.AimPoint( c ) - GlobalPosition;
				var d = to.Length();
				if ( d > SeekRange || d < 0.01f ) continue;
				var along = heading.Dot( to / d );
				if ( along < SeekCone ) continue;
				var score = d * (2f - along);
				if ( score < best ) { best = score; seeking = c; }
			}
		}
		if ( seeking is null || !IsInstanceValid( seeking ) || seeking.IsDead ) return;
		var speed = Velocity.Length();
		var dir = Velocity / MathF.Max( speed, 0.01f );
		// Already going to hit it: leave the line alone (steering into its middle bounces the ball straight back and ends the chain).
		var off = PinballFx.AimPoint( seeking ) - GlobalPosition;
		if ( (off - dir * off.Dot( dir )).Length() < SeekHitWidth ) return;
		var want = off.SafeNormal();
		var angle = dir.AngleTo( want );
		Velocity = dir.Slerp( want, MathF.Min( 1f, SeekTurn * dt / MathF.Max( angle, 0.001f ) ) ).Normalized() * speed;
	}

	const float SeekTurn = 3.5f, SeekCone = 0.9f, SeekRange = 40f, SeekHitWidth = 0.3f;
	bool hitEnemy;
	float sinceSeek = 1f;
	Creature seeking;

	/// <summary> A held flipper caught it (1 left, 2 right). </summary>
	public void Catch( int flipper )
	{
		look.Scale = Vector3.One * 0.35f;
		OnFlipper = flipper;
		chasing = null;
		Rolling = false;
		heldAge = 0f;
		Cradled = true;
		Velocity = Vector3.Zero;
		Life = age + PinballFx.CradleHold + 3f;
	}

	/// <summary> Sits at the flipper's tip, easing there from wherever it was caught. </summary>
	public void HoldAt( Vector3 at, float dt ) => GlobalPosition = GlobalPosition.Lerp( at, MathF.Min( 1f, dt * 18f ) );

	/// <summary> The flipper went down under it: it rolls off, slowly, toward the player. </summary>
	public void RollOff()
	{
		if ( OnFlipper == 0 ) return;
		Rolling = true;
		rollAge = 0f;
		var cam = owner.Camera.GlobalBasis;
		Velocity = (cam.Z * 0.6f - cam.Y * 0.8f).Normalized() * 1.1f;
	}

	void Drop()
	{
		look.Scale = Vector3.One;
		OnFlipper = 0;
		Rolling = false;
		Cradled = false;
		Velocity = -owner.Camera.GlobalBasis.Z * 4f + Vector3.Down;
	}

	protected override Vector3 BounceOffEnemy( Creature c, Vector3 normal, Vector3 at, Vector3 velocity )
	{
		hitEnemy = true;
		var speed = velocity.Length();
		// A pop bumper kicks it away from itself, whatever angle it came in at.
		var dir = (velocity.Bounce( normal ).SafeNormal() * 0.55f + normal * 0.45f).SafeNormal();
		// Out across the floor, as a bumper kicks along its table (a hit on the top or underside of a part would send it into the sand or the sky).
		dir = (dir with { Y = MathX.Clamp( dir.Y * 0.3f, 0.02f, 0.25f ) }).SafeNormal();
		var aim = PinballFx.ChainAim + PinballFx.RicochetAimPer * Skills.Rank( owner, SkillId.Rebound );
		var next = PinballFx.NextTarget( GlobalPosition, dir, PinballFx.ChainRange, c, RecentlyHit, -1f );   // any way round: a bumper kicks it on to the nearest one, ahead preferred
		if ( next is not null )
		{
			// Aimed a little high, as gravity takes it down on the way.
			var to = PinballFx.AimPoint( next ) - GlobalPosition;
			var outSpeed = MathX.Clamp( MathF.Max( speed * BumperGain, PinballFx.BumperKick ), 0f, PinballFx.MaxBallSpeed );
			var want = (to + Vector3.Up * 0.5f * Gravity * MathF.Pow( to.Length() / outSpeed, 2f )).SafeNormal();
			dir = dir.Lerp( want, MathF.Min( 1f, aim ) ).SafeNormal();
		}
		else dir = (dir + Vector3.Up * 0.08f).SafeNormal();
		holdFor = PinballFx.BumpPause;
		chasing = next;
		chaseLeft = next is null ? 0f : 0.9f;
		return dir * MathX.Clamp( MathF.Max( speed * BumperGain, PinballFx.BumperKick ), 0f, PinballFx.MaxBallSpeed );
	}

	protected override bool HitEnemy( Creature c, Node3D part, Vector3 at, float speed )
	{
		PinballFx.BallHits++;
		PinballFx.FastestBall = MathF.Max( PinballFx.FastestBall, speed );
		if ( enemyHits >= 2 ) PinballFx.Chains++;
		PinballFx.LongestChain = Math.Max( PinballFx.LongestChain, enemyHits );
		var jackpot = 1f + PinballFx.JackpotPer * Skills.Rank( owner, SkillId.Jackpot ) * (enemyHits - 1);
		Strike( c, part, at, damage * bonus * jackpot * MathX.Clamp( speed / PinballFx.RefSpeed, 0.6f, 2f ) );
		PinballFx.HitSound( at, enemyHits );
		BumperFlash.Spawn( at, -Velocity.SafeNormal(), enemyHits >= 3 ? PinballFx.Yellow : PinballFx.Neon, 0.8f + 0.15f * enemyHits );
		if ( enemyHits >= 2 ) PinballPopup.Show( at + Vector3.Up * 0.35f, enemyHits % 5 == 0 ? "JACKPOT!" : $"x{enemyHits}", enemyHits >= 3 ? PinballFx.Yellow : PinballFx.Neon, 0.8f + 0.1f * enemyHits );
		CreatureGore.Jiggle( part, 1.2f );
		glow.LightEnergy = 5f;
		if ( owner.GlobalPosition.DistanceTo( at ) < 8f ) owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.08f );
		if ( c.IsDead ) weapon?.MarkKill();
		else
		{
			weapon?.MarkHit();
			// The bumper's kick goes both ways: a knock along the ball's path and a stagger.
			SkillFx.Shove( c, Velocity.Flat().SafeNormal() * MathX.Clamp( speed / 30f, 0.3f, 1.2f ) );
			c.Stun( 0.2f );
		}
		var multi = Skills.Rank( owner, SkillId.Multiball );
		if ( multi > 0 && !split && enemyHits == 1 )
		{
			// Multiball: it splits into more balls off the first bumper, each heading for an enemy of its own if it can.
			split = true;
			PinballPopup.Show( at + Vector3.Up * 0.6f, "MULTIBALL!", PinballFx.Cyan, 1.1f );
			for ( var i = 0; i < PinballFx.MultiballSplit( multi ); i++ )
			{
				PinballFx.Splits++;
				var dir = (-Velocity.SafeNormal() + Rng.OnSphere() * 0.8f + Vector3.Up * 0.3f).SafeNormal();
				if ( PinballFx.NextTarget( at, dir, PinballFx.ChainRange, c ) is { } t ) dir = (PinballFx.AimPoint( t ) - at).SafeNormal();
				Fire( weapon, owner, GlobalPosition + dir * 0.3f, dir * MathF.Max( speed, PinballFx.BumperKick ), damage * 0.6f, true );
			}
		}
		Life = MathF.Max( Life, age + 3f );   // still chaining: keep going
		if ( enemyHits >= PinballFx.MaxEnemyHits + PinballFx.ReboundPer * Skills.Rank( owner, SkillId.Rebound ) ) { End(); return true; }
		return true;
	}

	protected override void HitWorld( Vector3 at, Vector3 normal, float speed )
	{
		if ( speed > 3f ) Sfx.Play( "mine_tink", at, MathX.Clamp( speed / 30f, 0.15f, 0.6f ), Rng.Float( 1.2f, 1.5f ) );
	}

	/// <summary> A flipper slapped it: out at the crosshair, faster and harder (`harder` multiplies its damage). </summary>
	public void Flipped( Vector3 direction, float harder, float faster )
	{
		PinballFx.BallsFlipped++;
		look.Scale = Vector3.One;
		chasing = null;
		OnFlipper = 0;
		Rolling = false;
		Cradled = false;
		holdFor = 0f;
		Velocity = direction * MathF.Max( Velocity.Length() * 1.2f, PinballFx.BallFlipSpeed * faster );
		bonus *= harder;
		bounces = 0;
		Life = age + 4f;
		sinceFlipped = 0f;
		glow.LightEnergy = 4f;
		Sfx.Play( "pinball_ding", GlobalPosition, 0.8f, 1.6f );
	}

	protected override void Finish()
	{
		if ( WaterFeel.IsOn() ) for ( var i = 0; i < 4; i++ ) WaterBubble.Create( GlobalPosition + Rng.InSphere( 0.1f ), Rng.Float( 0.2f, 0.4f ), 0f, Rng.Float( 0.3f, 0.6f ) );
	}
}

/// <summary>
/// An enemy a flipper sent flying: a human pinball. It flies along a low arc, tumbling; another enemy it meets is hit
/// (both hurt, more the faster it goes), and it bounces off toward the next one, up to `HumanHits` times; walls bounce
/// it, the floor lands it dazed. Ramp makes the landing a crash that hurts what is near.
/// </summary>
public partial class HumanPinball : Node3D
{
	Creature victim;
	PlayerWeapon weapon;
	PlayerController owner;
	Vector3 velocity, spin, centreOffset;
	Basis restBasis;
	float damage, flying;
	int hits;
	readonly HashSet<Creature> struck = new();

	public static void Launch( PlayerWeapon weapon, PlayerController owner, Creature victim, Vector3 velocity, float damage )
	{
		if ( !IsInstanceValid( victim ) || victim.Grabbed ) return;
		PinballFx.Launched++;
		var h = new HumanPinball { Name = "Human Pinball", weapon = weapon, owner = owner, victim = victim, velocity = velocity, damage = damage };
		victim.Grabbed = true;
		h.restBasis = victim.GlobalBasis;
		var sum = Vector3.Zero;
		var n = 0;
		foreach ( var p in CreaturePart.All ) if ( IsInstanceValid( p ) && p.Creature == victim ) { sum += p.GlobalPosition; n++; }
		h.centreOffset = n > 0 ? sum / n - victim.GlobalPosition : Vector3.Up * 0.4f;
		h.spin = Rng.OnSphere() * MathX.Clamp( velocity.Length() * 0.7f, 3f, 14f );
		owner.GetTree().CurrentScene.AddChild( h );
	}

	Vector3 Centre => victim.GlobalPosition + centreOffset;

	bool Alive()
	{
		if ( victim is not null && IsInstanceValid( victim ) && !victim.IsDead && IsInstanceValid( owner ) ) return true;
		QueueFree();
		return false;
	}

	public override void _ExitTree()
	{
		if ( victim is not null && IsInstanceValid( victim ) && victim.Grabbed ) { victim.Grabbed = false; victim.Pull( Vector3.Zero ); }
	}

	public override void _Process( double delta )
	{
		if ( !Alive() ) return;
		var dt = (float)delta;
		flying += dt;
		velocity += Vector3.Down * 9f * dt;
		velocity *= MathF.Exp( -0.3f * dt );
		victim.GlobalBasis = (new Basis( spin.SafeNormal() == Vector3.Zero ? Vector3.Up : spin.SafeNormal(), spin.Length() * flying ) * restBasis).Orthonormalized();
		var from = Centre;
		var step = velocity * dt;
		// Other enemies in its way: a hit on both, and a bounce on toward the next.
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( c == victim || struck.Contains( c ) || !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var p = SkillFx.NearestPart( c, from );
			if ( p.DistanceTo( from ) > PinballFx.HumanRadius ) continue;
			HitOther( c, p, from );
			if ( !Alive() ) return;
			step = velocity * dt;
			break;
		}
		var tr = Trace.Ray( victim, from, from + step + step.SafeNormal() * 0.35f, Layers.World, new Godot.Collections.Array<Rid> { owner.GetRid() } );
		if ( !tr.Hit ) { victim.GlobalPosition += step; if ( flying > PinballFx.FlightMax ) Land(); return; }
		victim.GlobalPosition = tr.HitPosition + tr.Normal * 0.35f - centreOffset;
		if ( tr.Normal.Y > 0.55f || velocity.Length() < 3f ) { Land(); return; }
		velocity = velocity.Bounce( tr.Normal ) * 0.55f;
		Sfx.Play( "dive_impact", tr.HitPosition, 0.5f, 1.3f );
	}

	void HitOther( Creature c, Vector3 at, Vector3 from )
	{
		struck.Add( c );
		hits++;
		PinballFx.HumanHitsDone++;
		var speed = velocity.Length();
		var hurt = damage * PinballFx.HumanShare * MathX.Clamp( speed / PinballFx.LaunchSpeed, 0.5f, 1.8f );
		SkillFx.HitPart( c, hurt, from, owner );
		PlayerPerks.Hurt( victim, hurt * 0.5f, at, at, owner );
		PinballFx.HitSound( at, hits + 1, 1f, heavy: true );
		BumperFlash.Spawn( at, (from - at).SafeNormal(), hits >= 2 ? PinballFx.Yellow : PinballFx.Neon, 1.3f );
		if ( hits >= 2 ) PinballPopup.Show( at + Vector3.Up * 0.5f, $"x{hits + 1}", PinballFx.Yellow, 1f );
		weapon?.MarkHit();
		if ( c.IsDead ) weapon?.MarkKill();
		else { SkillFx.Shove( c, velocity.Flat().SafeNormal() * 1.4f + Vector3.Up * 0.3f ); c.Stun( PinballFx.FlipStun ); }
		owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.15f );
		if ( !Alive() ) return;
		if ( hits >= PinballFx.HumanHits ) { velocity *= 0.3f; return; }
		// Bumped off it toward the next one along, as a ball would be.
		var dir = (from - at).Flat().SafeNormal().Lerp( velocity.Flat().SafeNormal(), 0.4f ).SafeNormal();
		if ( PinballFx.NextTarget( from, dir, PinballFx.ChainRange * 0.7f, victim, x => struck.Contains( x ) ) is { } next )
			dir = dir.Lerp( (SkillFx.NearestPart( next, from ) - from).Flat().SafeNormal(), 0.6f ).SafeNormal();
		velocity = (dir + Vector3.Up * 0.3f).SafeNormal() * MathF.Max( speed * 0.95f, PinballFx.LaunchSpeed );
		flying = MathF.Min( flying, PinballFx.FlightMax * 0.5f );
	}

	void Land()
	{
		if ( Alive() ) SeaStir.SiltRing( Centre with { Y = SeaStir.GroundBelow( Centre ) }, 0.6f );
		if ( Alive() )
		{
			var centre = Centre;
			var floor = Trace.Ray( victim, centre, centre + Vector3.Down * 30f, Layers.World, new Godot.Collections.Array<Rid> { owner.GetRid() } );
			victim.GlobalPosition = floor.Hit ? floor.HitPosition : victim.GlobalPosition with { Y = SeaTerrain.HeightAt( victim.GlobalPosition.X, victim.GlobalPosition.Z ) };
			var flat = (restBasis * Vector3.Forward).Flat().SafeNormal();
			victim.GlobalBasis = flat == Vector3.Zero ? restBasis : Shapes.LookAt( flat );
			victim.Grabbed = false;
			victim.Pull( Vector3.Zero );
			victim.Stun( PinballFx.FlipStun + 0.3f );
			// Ramp: it comes down in a crash.
			var ramp = Skills.Rank( owner, SkillId.Ramp );
			if ( ramp > 0 )
			{
				PinballFx.Ramps++;
				var at = victim.GlobalPosition;
				var crash = PinballFx.RampDamage( ramp ) * (weapon?.UpgradeScale ?? 1f);
				SkillFx.HitPart( victim, crash, at + Vector3.Up, owner );
				foreach ( var c in Creature.Everyone.ToList() )
				{
					if ( c == victim || !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || c.GlobalPosition.DistanceTo( at ) > PinballFx.RampSplash ) continue;
					SkillFx.HitPart( c, crash * 0.5f, at, owner );
				}
				Shockwave.Spawn( GetTree().CurrentScene, at + Vector3.Up * 0.3f, PinballFx.RampSplash, PinballFx.Neon );
				Sfx.Play( "dive_impact", at, 0.7f, 1.3f );
				PinballFx.HitSound( at, 3 );
			}
		}
		QueueFree();
	}
}

/// <summary> A pop bumper's flash where a ball (or a flipper) hits: a neon ring springing out, and a blink of light. </summary>
public partial class BumperFlash : Node3D
{
	float age, size;
	MeshInstance3D ring;
	StandardMaterial3D paint;
	OmniLight3D light;
	static TorusMesh torus;

	public static void Spawn( Vector3 at, Vector3 facing, Color colour, float size = 1f )
	{
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( root is null ) return;
		var f = new BumperFlash { Name = "Bumper Flash", size = size };
		root.AddChild( f );
		f.GlobalPosition = at;
		f.paint = new StandardMaterial3D
		{
			AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			EmissionEnabled = true, Emission = colour, EmissionEnergyMultiplier = 3f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		f.paint.SetMeta( "no_caustics", true );
		torus ??= new TorusMesh { InnerRadius = 0.8f, OuterRadius = 1f, Rings = 24, RingSegments = 6 };
		f.ring = new MeshInstance3D { Mesh = torus, MaterialOverride = f.paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		f.AddChild( f.ring );
		f.ring.GlobalBasis = GunKit.Along( facing == Vector3.Zero ? Vector3.Up : facing.Normalized() );
		f.light = new OmniLight3D { LightColor = colour, LightEnergy = 4f * size, OmniRange = 2.5f * size, ShadowEnabled = false };
		f.AddChild( f.light );
		f.Tick( 0f );
	}

	void Tick( float dt )
	{
		age += dt;
		var t = MathF.Min( 1f, age / 0.22f );
		var grow = 1f - (1f - t) * (1f - t);
		ring.Scale = Vector3.One * size * MathX.Lerp( 0.08f, 0.45f, grow );
		paint.AlbedoColor = paint.AlbedoColor with { A = 1f - t };
		light.LightEnergy = 4f * size * (1f - t);
		if ( t >= 1f ) QueueFree();
	}

	public override void _Process( double delta ) => Tick( (float)delta );
}

/// <summary> Pinball-table words over a hit (x2, JACKPOT!, PERFECT!): pop in big, rise and fade. </summary>
public partial class PinballPopup : Label3D
{
	float age, size;

	public static void Show( Vector3 at, string text, Color colour, float size = 1f )
	{
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( root is null ) return;
		var p = new PinballPopup
		{
			Name = "Pinball Popup", Text = text, Font = UiStyle.Display, FontSize = 64, OutlineSize = 18, Modulate = colour,
			OutlineModulate = new Color( 0.12f, 0.02f, 0.2f ), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
			Shaded = false, PixelSize = 0.0035f, size = size, FixedSize = false,
		};
		root.AddChild( p );
		p.GlobalPosition = at;
		p.Scale = Vector3.One * 0.01f;
	}

	public override void _Process( double delta )
	{
		age += (float)delta;
		var pop = age < 0.08f ? age / 0.08f * 1.35f : MathX.Lerp( 1.35f, 1f, MathF.Min( 1f, (age - 0.08f) / 0.12f ) );
		Scale = Vector3.One * size * pop;
		GlobalPosition += Vector3.Up * (float)delta * 0.7f;
		var fade = MathX.Clamp( (0.75f - age) / 0.25f, 0f, 1f );
		Modulate = Modulate with { A = fade };
		OutlineModulate = OutlineModulate with { A = fade };
		if ( age > 0.75f ) QueueFree();
	}
}

/// <summary>
/// The Pinball-Bot's gun models, on the Blaster's rig: made in Blender (`Tools/blender/pinball_guns.py`, in GunKit's gun
/// space) with their moving parts as named nodes, which `PoseLauncher` and `PoseFlippers` move in view and in third
/// person alike.
/// </summary>
public static class PinballModels
{
	public static readonly Vector3 LauncherMuzzle = new( 25.5f, 0f, 3f ), FlipperMuzzle = new( 20f, 0f, 9f );
	/// <summary> How far a flipper turns from rest to up (degrees). </summary>
	public const float FlipAngle = 60f;
	const float PlungerTravel = 2.6f;
	static readonly Vector3 LoadedRest = new( 3.4f, 0f, 5.95f );

	static PackedScene launcher, flippers;

	public static void Launcher( Node3D gun )
	{
		launcher ??= GD.Load<PackedScene>( "res://assets/models/weapons/pinball_launcher.glb" );
		if ( launcher?.Instantiate() is not Node3D model ) return;
		gun.AddChild( model );
		NoShadows( model );
	}

	public static void Flippers( Node3D gun )
	{
		flippers ??= GD.Load<PackedScene>( "res://assets/models/weapons/pinball_flippers.glb" );
		if ( flippers?.Instantiate() is Node3D model ) { gun.AddChild( model ); NoShadows( model ); }
	}

	static void NoShadows( Node n )
	{
		if ( n is GeometryInstance3D g ) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		foreach ( var k in n.GetChildren() ) NoShadows( k );
	}

	static readonly Dictionary<(ulong, string), Node3D> found = new();
	static Node3D Find( Node3D root, string name )
	{
		var key = (root.GetInstanceId(), name);
		if ( !found.TryGetValue( key, out var n ) || (n is not null && !GodotObject.IsInstanceValid( n )) )
		{
			if ( found.Count > 400 ) found.Clear();
			found[key] = n = root.FindChild( name, true, false ) as Node3D;
		}
		return n;
	}

	/// <summary> The plunger pulled back (0 to 1) with the spring stretching, the ball count lit, the lane's ball shown. </summary>
	public static void PoseLauncher( Node3D root, float pull, int ammo, int magazine, float sinceShot )
	{
		if ( Find( root, "Plunger" ) is { } plunger ) plunger.Position = new Vector3( -PlungerTravel * pull + Wobble( pull, sinceShot ), 0f, 0f );
		if ( Find( root, "Spring" ) is { } spring ) spring.Scale = new Vector3( 1f + 0.85f * pull, 1f, 1f );
		if ( Find( root, "Loaded" ) is { } loaded )
		{
			// The ball rides back down the lane with the plunger as it is pulled (rolling as it goes), and is gone with the shot.
			loaded.Visible = ammo > 0 && sinceShot > 0.25f;
			var back = PlungerTravel * pull + Wobble( pull, sinceShot );
			loaded.Position = LoadedRest - new Vector3( back, 0f, 0f );
			loaded.Rotation = new Vector3( 0f, back / 0.95f, 0f );
		}
		for ( var i = 1; i <= 5; i++ )
			if ( Find( root, $"Ball Light {i} Lit" ) is { } lit ) lit.Visible = i <= Math.Min( ammo, 5 ) && (magazine <= 5 || i <= (int)MathF.Ceiling( ammo * 5f / magazine ));
	}

	/// <summary> Just after a shot the plunger snaps home and shudders. </summary>
	static float Wobble( float pull, float sinceShot ) => pull > 0f || sinceShot > 0.4f ? 0f : MathF.Sin( sinceShot * 60f ) * 0.5f * (1f - sinceShot / 0.4f);

	/// <summary> Where a cradled ball sits on a flipper: just over its tip, in the world. </summary>
	public static Vector3? FlipperTip( Node3D root, bool left )
	{
		if ( Find( root, left ? "Left Flipper" : "Right Flipper" ) is not { } pivot ) return null;
		var a = MathX.Rad( left ? -122f : 122f );
		return pivot.GlobalTransform * new Vector3( MathF.Cos( a ) * 4.2f, MathF.Sin( a ) * 4.2f, 2.2f );
	}

	/// <summary> Each flipper up by its swing (0 rest, 1 up). </summary>
	public static void PoseFlippers( Node3D root, float left, float right )
	{
		if ( Find( root, "Left Flipper" ) is { } l ) l.Rotation = new Vector3( 0f, 0f, MathX.Rad( FlipAngle * left ) );
		if ( Find( root, "Right Flipper" ) is { } r ) r.Rotation = new Vector3( 0f, 0f, -MathX.Rad( FlipAngle * right ) );
	}
}
