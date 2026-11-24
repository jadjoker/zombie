using Godot;

/// <summary>
/// The Goo-Bot's mouse 4: the splat jump. A tap bounces you up as a blob of jelly; at the top you slam down fast the way
/// you look (straight down unless you look down at something), and the landing is a splat that hurts and glues the enemies near you for a moment and puts a blob of goo on each (their goo balls'
/// clocks restart). Super Bounce goes higher and wider, Soft Landing turns away part of each hit after it, Puddle
/// leaves a sticky stinging puddle, and Gooquake sends a jelly ripple through the floor a beat later that flings the
/// enemies near up.
/// </summary>
public partial class Jetpack
{
	bool splatting, slamming;
	Vector3 slamDir;
	double splatAt = -99, quakeAt = -1;
	Vector3 quakeFrom;
	public bool Splatting => splatting;
	/// <summary> When the last splat landed (for tests). </summary>
	public double SplattedAt { get; private set; } = -99;

	public void TestSplat() => TrySplat();

	float Bounce => 1f + GooFx.BouncePer * Skill( SkillId.SuperBounce );

	void TrySplat()
	{
		if ( Surging || splatting ) return;
		if ( Power < GooFx.SplatCost ) { TimeSinceRefused = 0f; return; }
		Power -= GooFx.SplatCost;
		timeSinceThrust = 0f;
		var camera = controller.Camera;
		var forward = (camera is not null ? -camera.GlobalBasis.Z : controller.EyeAngles.Forward).Flat().SafeNormal();
		controller.Velocity = forward * GooFx.SplatForward + Vector3.Up * GooFx.SplatUp * MathF.Sqrt( Bounce );
		controller.PreventGrounding( 0.25f );
		splatting = true;
		slamming = false;
		splatAt = Clock.Now;
		var feet = controller.GlobalPosition;
		GooFx.Splash( feet + Vector3.Up * 0.2f, 16, 3f, 1.8f );
		GooPuddle.Lay( controller, GooFx.Ground( controller, feet ), 0.6f, 2f, 0f, 0f );
		Sfx.Play( "jelly_boing", feet, 1f, 0.75f );
		Sfx.Play( "goo_stick", feet, 0.7f, 0.6f );
		Juice?.Shake( 0.15f );
		Juice?.KickFov( 10f );
		Controls.Rumble( 0.3f );
	}

	void SplatLanded()
	{
		splatting = slamming = false;
		FloppedAt = SplattedAt = Clock.Now;
		GooFx.Splats++;
		var at = controller.GlobalPosition;
		var radius = GooFx.SplatRadius * Bounce;
		var gun = GooBall.GunOf( controller );
		foreach ( var c in GooFx.Near( at + Vector3.Up * 0.5f, radius ) )
		{
			var d = (c.GlobalPosition - at).Flat().Length();
			SkillFx.HitPart( c, GooFx.SplatDamage * Bounce * (1f - 0.4f * MathX.Clamp( d / radius, 0f, 1f )), at + Vector3.Up, controller );
			if ( c.IsDead ) continue;
			SkillFx.Shove( c, (c.GlobalPosition - at).Flat().SafeNormal() * 0.6f );
			c.Stun( GooFx.SplatGlue );
			GooBall.Add( c, SkillFx.NearestPart( c, at + Vector3.Up * 0.3f ), controller, gun );
		}
		var ground = GooFx.Ground( controller, at );
		var puddle = Skill( SkillId.Puddle );
		GooPuddle.Lay( controller, ground, puddle > 0 ? radius * 0.75f : 1.4f, puddle > 0 ? GooFx.PuddleTime( puddle ) : 3f,
			puddle > 0 ? GooFx.PuddleSlow + GooFx.PuddleSlowPer * puddle : 0f, puddle > 0 ? GooFx.PuddleDps * puddle : 0f );
		var soft = Skill( SkillId.SoftLanding );
		if ( soft > 0 && PlayerPerks.Of( controller ) is { } perks ) { perks.Guard = GooFx.SoftPer * soft; perks.GuardUntil = Clock.Now + GooFx.SoftTime; }
		if ( Skill( SkillId.Gooquake ) > 0 ) { quakeAt = Clock.Now + GooFx.QuakeDelay; quakeFrom = ground; }
		GooFx.Splash( at + Vector3.Up * 0.2f, 40, 5f, 2.2f );
		Shockwave.Spawn( GetTree().CurrentScene, at + Vector3.Up * 0.2f, radius, GooFx.Magenta );
		Sfx.Play( "goo_pop", at, 0.9f, 0.8f );
		Sfx.Play( "dive_impact", at, 0.5f, 1.4f );
		Juice?.Shake( 0.35f );
		Controls.Rumble( 0.4f );
	}

	/// <summary> Gooquake: a ripple of jelly through the floor that flings the enemies near up. </summary>
	void Quake()
	{
		quakeAt = -1;
		GooFx.Quakes++;
		var n = Skill( SkillId.Gooquake );
		var reach = GooFx.QuakeReach( n );
		var scale = GooBall.GunOf( controller )?.UpgradeScale ?? 1f;
		foreach ( var c in GooFx.Near( quakeFrom + Vector3.Up * 0.5f, reach ) )
		{
			SkillFx.HitPart( c, GooFx.QuakeDamage( n ) * scale, quakeFrom, controller );
			if ( c.IsDead ) continue;
			SkillFx.Shove( c, (c.GlobalPosition - quakeFrom).Flat().SafeNormal() * 0.8f + Vector3.Up * 1.6f );
			c.Stun( 0.6f );
		}
		for ( var i = 0; i < 3; i++ ) Shockwave.Spawn( GetTree().CurrentScene, quakeFrom + Vector3.Up * (0.1f + 0.15f * i), reach * (0.6f + 0.2f * i), i == 1 ? GooFx.Grape : GooFx.Magenta );
		for ( var i = 0; i < 16; i++ )
		{
			var a = i / 16f * MathF.Tau;
			GooFx.Splash( quakeFrom + new Vector3( MathF.Cos( a ), 0.1f, MathF.Sin( a ) ) * reach * 0.7f, 3, 4f, 2f );
		}
		Sfx.Play( "kraken_slam", quakeFrom, 0.7f, 1.5f );
		Sfx.Play( "jelly_boing", quakeFrom, 1f, 0.5f );
		Juice?.Shake( 0.5f );
	}

	/// <summary> Each frame: the splat jump's landing and Gooquake's delay. </summary>
	void GooSkills( float dt )
	{
		if ( splatting )
		{
			if ( (Clock.Now - splatAt) % 0.08 < dt ) GooDroplet.Spawn( controller.GlobalPosition + Vector3.Up * 0.6f, -controller.Velocity * 0.1f, GooFx.Grape, 1.6f, 0.6f );
			if ( !slamming && Clock.Now - splatAt > 0.2f && controller.Velocity.Y <= 0.5f )
			{
				// The top: slam down the way you look if that is downward enough, else straight down.
				slamming = true;
				var look = controller.Camera is { } cam ? -cam.GlobalBasis.Z : controller.EyeAngles.Forward;
				slamDir = look.Y < -0.35f ? look.Normalized() : (Vector3.Down * 3f + look.Flat() * 0.6f).Normalized();
				Sfx.Play( "dive_whoosh", controller.GlobalPosition, 0.5f, 1.3f );
				Juice?.KickFov( 8f );
			}
			// The slam outruns the water's sink cap: held at full speed until it lands.
			if ( slamming ) controller.Velocity = slamDir * GooFx.SlamSpeed;
			if ( Clock.Now - splatAt > 0.3f && controller.IsOnGround ) SplatLanded();
			else if ( Clock.Now - splatAt > 4f ) splatting = slamming = false;
		}
		if ( quakeAt > 0 && Clock.Now >= quakeAt ) Quake();
	}
}
