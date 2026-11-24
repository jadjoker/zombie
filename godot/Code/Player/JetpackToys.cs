using Godot;

/// <summary>
/// The Plunger-Bot's and Pinball-Bot's mouse 4 moves (the Goo-Bot's splat jump is in JetpackGoo.cs). The plunger slingshot plants a
/// plunger at your feet and flings you off it on a bungee along your aim; landing is a belly flop (Cannonball makes it
/// a shockwave). The roll curls you into a ball: fast and low, bowling over the enemies you run into (High Score ends
/// a big enough strike in a blast and a spray of pinballs).
/// </summary>
public partial class Jetpack
{
	// Sling
	bool slinging;
	double slungAt = -99;
	Node3D slingPlunger;
	MeshInstance3D slingCord;
	public bool Slinging => slinging;
	/// <summary> For the third person rig: when the last belly flop (or Goo-Bot splat) landed. </summary>
	public double FloppedAt { get; private set; } = -99;
	// Roll
	public bool Rolling { get; private set; }
	float rollLeft;
	readonly HashSet<Creature> bowled = new();
	public int BowledThisRoll => bowled.Count;

	/// <summary> Testing aids: the moves as if mouse 4 were tapped. </summary>
	public void TestSling() => TrySling();
	public void TestRoll() => ToggleRoll();

	// ---- Plunger-Bot: the slingshot ----

	void TrySling()
	{
		if ( Surging || slinging ) return;
		var cost = PlungerFx.SlingCost * (1f - PlungerFx.ElasticPer * Skill( SkillId.Elastic ));
		if ( Power < cost ) { TimeSinceRefused = 0f; return; }
		Power -= cost;
		timeSinceThrust = 0f;
		PlungerFx.Slings++;
		var camera = controller.Camera;
		var forward = camera is not null ? -camera.GlobalBasis.Z : controller.EyeAngles.Forward;
		// Always a bit up: the bungee flings you in an arc, not into the floor.
		var dir = (forward.Flat().SafeNormal() * MathF.Max( 0.4f, forward.Flat().Length() ) + Vector3.Up * MathF.Max( 0.3f, forward.Y )).SafeNormal();
		var speed = PlungerFx.SlingSpeed * (1f + PlungerFx.LongBungeePer * Skill( SkillId.LongBungee ));
		var feet = controller.GlobalPosition;
		controller.Velocity = dir * speed + Vector3.Up * PlungerFx.SlingLift;
		controller.PreventGrounding( 0.25f );
		slinging = true;
		slungAt = Clock.Now;
		// The plunger stays stuck where you stood, the bungee stretching after you.
		if ( slingPlunger is not null && IsInstanceValid( slingPlunger ) ) slingPlunger.QueueFree();
		slingPlunger = new Node3D { Name = "Sling Plunger" };
		GetTree().CurrentScene.AddChild( slingPlunger );
		slingPlunger.GlobalPosition = feet;
		var look = PlungerModels.Plunger( slingPlunger, 1.6f );
		look.GlobalBasis = Shapes.LookAt( Vector3.Down );
		slingCord ??= GunKit.Line( controller, GunKit.Paint( new Color( 0.9f, 0.85f, 0.2f ), 0.6f ), 0.02f );
		slingCord.Visible = true;
		Sfx.Play( "plunger_pop", feet, 1f, 0.8f );
		Sfx.Play( "boing", feet, 0.9f, 0.9f );
		Juice?.Shake( 0.2f );
		Juice?.KickFov( 12f );
	}

	void SlingLanded()
	{
		slinging = false;
		FloppedAt = Clock.Now;
		if ( slingCord is not null ) slingCord.Visible = false;
		if ( slingPlunger is not null && IsInstanceValid( slingPlunger ) ) Fx.Expire( slingPlunger, 0.5f );
		slingPlunger = null;
		PlungerFx.Flops++;
		var at = controller.GlobalPosition;
		var cannon = Skill( SkillId.Cannonball );
		var radius = cannon > 0 ? PlungerFx.CannonballRadius( cannon ) : PlungerFx.FlopRadius;
		var damage = PlungerFx.FlopDamage + PlungerFx.BellyFlopPer * Skill( SkillId.BellyFlop ) + (cannon > 0 ? PlungerFx.CannonballDamage( cannon ) : 0f);
		if ( cannon > 0 ) PlungerFx.Cannonballs++;
		var hits = 0;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var d = c.GlobalPosition.DistanceTo( at );
			if ( d > radius ) continue;
			hits++;
			SkillFx.HitPart( c, damage * (1f - 0.4f * d / radius), at + Vector3.Up, controller );
			SkillFx.Shove( c, (c.GlobalPosition - at).Flat().SafeNormal() * 1.4f + Vector3.Up * (cannon > 0 ? 1.4f : 0.4f) );
			if ( !c.IsDead ) c.Stun( 0.4f );
		}
		var heal = Skill( SkillId.BounceBack );
		if ( heal > 0 && hits > 0 ) controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.Health?.Heal( PlungerFx.BounceBackPer * heal * hits );
		Shockwave.Spawn( GetTree().CurrentScene, at + Vector3.Up * 0.2f, radius, cannon > 0 ? PlungerFx.Water : PlungerFx.Rubber );
		Jetpack.SlamBubbles( at, Vector3.Up, cannon > 0 ? 1f : 0.5f );
		Sfx.Play( "dive_impact", at, cannon > 0 ? 1f : 0.6f, 1.2f );
		Sfx.Play( "boing", at, 0.6f, 0.6f );
		Juice?.Shake( cannon > 0 ? 0.6f : 0.3f );
		Controls.Rumble( 0.4f );
	}

	// ---- Pinball-Bot: the roll ----

	void ToggleRoll()
	{
		if ( Rolling ) { EndRoll(); return; }
		if ( Surging ) return;
		if ( Power < PinballFx.RollCost ) { TimeSinceRefused = 0f; return; }
		Power -= PinballFx.RollCost;
		timeSinceThrust = 0f;
		PinballFx.Rolls++;
		Rolling = true;
		bowled.Clear();
		rollLeft = PinballFx.RollTime + PinballFx.LongRollPer * Skill( SkillId.LongRoll );
		if ( PlayerPerks.Of( controller ) is { } perks ) { perks.Boost = PinballFx.RollBoost * (1f + PinballFx.SpinUpPer * Skill( SkillId.SpinUp )); perks.BoostUntil = Clock.Now + rollLeft; }
		Sfx.Play( "pinball_ding", controller.GlobalPosition, 0.8f, 0.7f );
		Sfx.Play( "slide", controller.GlobalPosition, 0.7f, 1.3f );
		Juice?.KickFov( 8f );
	}

	void EndRoll()
	{
		if ( !Rolling ) return;
		Rolling = false;
		rollLeft = 0f;
		if ( PlayerPerks.Of( controller ) is { } perks ) perks.BoostUntil = 0;
		var n = Skill( SkillId.HighScore );
		if ( n <= 0 || bowled.Count < PinballFx.HighScoreNeeds ) return;
		// High Score: a big enough strike ends in a blast, and pinballs spray out of it.
		PinballFx.HighScores++;
		var at = controller.GlobalPosition + Vector3.Up * 0.6f;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || c.GlobalPosition.DistanceTo( at ) > PinballFx.HighScoreRadius ) continue;
			SkillFx.HitPart( c, PinballFx.HighScoreDamage( n ), at, controller );
			SkillFx.Shove( c, (c.GlobalPosition - at).Flat().SafeNormal() * 1.5f );
		}
		if ( controller.GetChildren().OfType<PlayerWeapon>().FirstOrDefault( w => w.Pinball ) is { } launcher )
			for ( var i = 0; i < 3; i++ )
			{
				var dir = (Rng.OnSphere().Flat().SafeNormal() + Vector3.Up * 0.4f).SafeNormal();
				Pinball.Fire( launcher, controller, at + dir * 0.5f, dir * PinballFx.MaxSpeed * 0.8f, launcher.CurrentDamage * 0.7f, true );
			}
		Shockwave.Spawn( GetTree().CurrentScene, at, PinballFx.HighScoreRadius, PinballFx.Yellow );
		Sfx.Play( "pinball_ding", at, 1f, 1.2f );
		Sfx.Play( "pinball_ding", at, 1f, 1.5f );
		Sfx.Play( "missile_boom", at, 0.6f, 1.3f );
		Juice?.Shake( 0.5f );
	}

	/// <summary> Each frame: the splat jump, the sling's cord and landing, the roll's bowling and timer. </summary>
	void ToySkills( float dt )
	{
		GooSkills( dt );
		if ( slinging )
		{
			if ( slingCord is not null && slingPlunger is not null && IsInstanceValid( slingPlunger ) )
			{
				var stretch = Clock.Now - slungAt;
				slingCord.Visible = stretch < 0.45f;
				if ( slingCord.Visible ) GunKit.Lay( slingCord, slingPlunger.GlobalPosition + Vector3.Up * 0.5f, controller.GlobalPosition + Vector3.Up * 0.8f );
			}
			if ( Clock.Now - slungAt > 0.3f && controller.IsOnGround ) SlingLanded();
			else if ( Clock.Now - slungAt > 4f ) { slinging = false; if ( slingCord is not null ) slingCord.Visible = false; }
		}
		if ( Rolling )
		{
			rollLeft -= dt;
			controller.EyeDrop = MathX.Lerp( controller.EyeDrop, PinballFx.EyeDrop, 1f - MathF.Exp( -14f * dt ) );
			var at = controller.GlobalPosition;
			var damage = PinballFx.RollDamage * (1f + PinballFx.StrikePer * Skill( SkillId.Strike ));
			foreach ( var c in Creature.Everyone.ToList() )
			{
				if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || bowled.Contains( c ) ) continue;
				if ( SkillFx.NearestPart( c, at + Vector3.Up * 0.5f ).DistanceTo( at + Vector3.Up * 0.5f ) > PinballFx.RollReach ) continue;
				bowled.Add( c );
				PinballFx.Bowled++;
				SkillFx.HitPart( c, damage, at, controller );
				SkillFx.Shove( c, (controller.Velocity.Flat().SafeNormal() + (c.GlobalPosition - at).Flat().SafeNormal()).SafeNormal() * 2f + Vector3.Up * 0.8f );
				if ( !c.IsDead ) c.Stun( 0.5f );
				Power = MathF.Min( MaxPower, Power + PinballFx.TiltPer * Skill( SkillId.TiltBonus ) );
				Sfx.Play( "pinball_ding", at, 0.8f, 1f + 0.1f * bowled.Count );
				Juice?.Shake( 0.2f );
			}
			if ( rollLeft <= 0f ) EndRoll();
		}
	}
}
