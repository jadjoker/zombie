using Godot;

/// <summary>
/// The Magnet-Bot's, Plunger-Bot's and Pinball-Bot's mouse 4 moves. The Repulsor Pulse throws every enemy near you
/// back (and the loose gore with them); Pole Reversal first drags them in for a beat. The plunger slingshot plants a
/// plunger at your feet and flings you off it on a bungee along your aim; landing is a belly flop (Cannonball makes it
/// a shockwave). The roll curls you into a ball: fast and low, bowling over the enemies you run into (High Score ends
/// a big enough strike in a blast and a spray of pinballs).
/// </summary>
public partial class Jetpack
{
	// Pulse
	double reversalAt = -1;
	public double ArmourUntil { get; private set; } = -99;
	// Sling
	bool slinging;
	double slungAt = -99;
	Node3D slingPlunger;
	MeshInstance3D slingCord;
	public bool Slinging => slinging;
	/// <summary> For the third person rig: when the last pulse went off and the last belly flop landed, and whether Pole Reversal is pulling. </summary>
	public double PulsedAt { get; private set; } = -99;
	public double FloppedAt { get; private set; } = -99;
	public bool Reversing => reversalAt > 0;
	// Roll
	public bool Rolling { get; private set; }
	float rollLeft, sinceBounce;
	bool rollWasThird;
	readonly HashSet<Creature> bowled = new();
	readonly Dictionary<Creature, double> rollHits = new();
	Node3D rollShell;
	public int BowledThisRoll => bowled.Count;

	/// <summary> Testing aids: the moves as if mouse 4 were tapped. </summary>
	public void TestPulse() => TryPulse();
	public void TestSling() => TrySling();
	public void TestRoll() => ToggleRoll();

	// ---- Magnet-Bot: the Repulsor Pulse ----

	void TryPulse()
	{
		if ( Surging || reversalAt > 0 ) return;
		var cost = MagnetFx.PulseCost * (1f - MagnetFx.CapacitorPer * Skill( SkillId.Capacitor ));
		if ( Power < cost ) { TimeSinceRefused = 0f; return; }
		Power -= cost;
		timeSinceThrust = 0f;
		MagnetFx.Pulses++;
		var rev = Skill( SkillId.PoleReversal );
		if ( rev > 0 )
		{
			// Pole Reversal: the field pulls first, then the pulse goes off.
			reversalAt = Clock.Now + MagnetFx.ReversalPull;
			Sfx.Play( "magnet_hum", controller.GlobalPosition, 0.8f, 0.6f );
			Shockwave.Spawn( GetTree().CurrentScene, controller.GlobalPosition + Vector3.Up, PulseRadius * 1.6f, MagnetFx.Red );
			return;
		}
		Pulse( false );
	}

	float PulseRadius => MagnetFx.PulseRadius * (1f + MagnetFx.OverchargePer * Skill( SkillId.Overcharge ));

	void Pulse( bool reversed )
	{
		var at = controller.GlobalPosition + Vector3.Up * 0.8f;
		var over = 1f + MagnetFx.OverchargePer * Skill( SkillId.Overcharge );
		var radius = PulseRadius;
		PulsedAt = Clock.Now;
		var rev = Skill( SkillId.PoleReversal );
		var damage = MagnetFx.PulseDamage * over + (reversed ? MagnetFx.ReversalDamage( rev ) : 0f);
		if ( reversed ) MagnetFx.Reversals++;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var d = c.GlobalPosition.DistanceTo( controller.GlobalPosition );
			if ( d > radius ) continue;
			SkillFx.HitPart( c, damage * (1f - 0.35f * d / radius), at, controller );
			SkillFx.Shove( c, (c.GlobalPosition - controller.GlobalPosition).Flat().SafeNormal() * MagnetFx.PulseShove * (1f - 0.5f * d / radius) + Vector3.Up * 0.4f );
			if ( !c.IsDead ) c.Stun( 0.35f );
		}
		// The loose gore is thrown back too.
		if ( GoreSystem.Get( controller ) is { } gore )
			foreach ( var gib in gore.Gibs )
			{
				if ( !IsInstanceValid( gib ) ) continue;
				var to = gib.GlobalPosition - at;
				if ( to.Length() > radius * 1.5f ) continue;
				gib.ApplyCentralImpulse( (to.SafeNormal() + Vector3.Up * 0.4f) * 6f * gib.Mass );
			}
		var armour = Skill( SkillId.ScrapArmour );
		if ( armour > 0 && PlayerPerks.Of( controller ) is { } perks ) { perks.Guard = MagnetFx.ArmourPer * armour; perks.GuardUntil = Clock.Now + MagnetFx.ArmourTime; ArmourUntil = perks.GuardUntil; }
		var dis = Skill( SkillId.Discharge );
		if ( dis > 0 )
			foreach ( var c in Creature.Everyone.Where( c => IsInstanceValid( c ) && c.IsEnemy && !c.IsDead && c.GlobalPosition.DistanceTo( at ) < MagnetFx.DischargeReach ).OrderBy( c => c.GlobalPosition.DistanceTo( at ) ).Take( dis ).ToList() )
			{
				MagnetFx.Discharges++;
				var to = SkillFx.NearestPart( c, at );
				SkillFx.Zap( at, to );
				PlayerPerks.Hurt( c, MagnetFx.DischargeDamage( dis ), to, at, controller );
			}
		Shockwave.Spawn( GetTree().CurrentScene, at, radius, reversed ? MagnetFx.Red : MagnetFx.Blue );
		Sfx.Play( "magnet_clang", at, 0.9f, 0.6f );
		Sfx.Play( "surge", at, 0.6f, 1.4f );
		Juice?.Shake( 0.35f );
		Juice?.KickFov( 6f );
		Controls.Rumble( 0.4f );
	}

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

	float SpinUp => 1f + PinballFx.SpinUpPer * Skill( SkillId.SpinUp );
	Vector3 BallCentre => controller.GlobalPosition + Vector3.Up * 0.6f;

	/// <summary> One physics tick of the roll: the body is a ball with momentum (the controller's own walk is off). </summary>
	bool RollStep( float dt )
	{
		if ( !Rolling ) return false;
		var c = controller;
		var v = c.Velocity;
		var h = v.Flat();
		var input = c.UseInputControls ? c.In.GetVector( "left", "right", "backward", "forward" ) : Vector2.Zero;
		var yaw = new Angles( 0f, c.EyeAngles.Yaw, 0f );
		var dir = (yaw.Forward * input.Y + yaw.Right * input.X).Flat().ClampLength( 1f );
		var top = PinballFx.RollTop * SpinUp;
		// Steering pushes the ball the way you press; rolling on its own it slows only a little.
		if ( dir.Length() > 0.1f ) h += dir * PinballFx.RollAccel * dt;
		h *= MathF.Exp( -(dir.Length() > 0.1f ? 0.2f : 0.5f) * dt );
		// Downhill it gathers speed, uphill it loses it.
		if ( c.IsOnGround )
		{
			var n = c.GroundNormal;
			var g = Vector3.Down * PinballFx.RollGravity;
			h += (g - n * g.Dot( n )).Flat() * dt;
		}
		// Past its top speed (a slope, a bumper) it eases back down to it; never more than twice it.
		var speed = h.Length();
		if ( speed > top ) h = h / speed * MathF.Min( top * 2f, MathF.Max( top, speed - (speed - top) * 1.5f * dt ) );
		var y = v.Y;
		if ( c.IsOnGround )
		{
			y = -1f;
			if ( c.UseInputControls && c.In.JustPressed( "jump" ) ) { y = PinballFx.RollHop; c.PreventGrounding( 0.15f ); Sfx.Play( "boing", c.GlobalPosition, 0.5f, 1.3f ); }
		}
		else y -= PinballFx.RollGravity * 1.3f * dt;
		var before = h + Vector3.Up * y;
		c.Velocity = before;
		c.MoveAndSlide();
		// Off a wall it bounces like a ball off a rail.
		for ( var i = 0; i < c.GetSlideCollisionCount(); i++ )
		{
			var n = c.GetSlideCollision( i ).GetNormal();
			if ( MathF.Abs( n.Y ) > 0.55f ) continue;
			var flat = n.Flat().SafeNormal();
			var into = -before.Flat().Dot( flat );
			if ( into < 2.5f || sinceBounce < 0.1f ) continue;
			sinceBounce = 0f;
			var hb = before.Flat();
			hb = (hb - 2f * hb.Dot( flat ) * flat) * 0.75f;
			c.Velocity = hb + Vector3.Up * c.Velocity.Y;
			PinballFx.HitSound( c.GlobalPosition + Vector3.Up * 0.6f, 1, MathX.Clamp( into / 12f, 0.4f, 1f ) );
			BumperFlash.Spawn( c.GlobalPosition + Vector3.Up * 0.6f - flat * 0.6f, flat, PinballFx.Neon, 1.2f );
			Juice?.Shake( MathX.Clamp( into / 30f, 0.1f, 0.4f ) );
			Controls.Rumble( 0.3f );
			break;
		}
		c.SetGround( c.IsOnFloor() && c.Velocity.Y <= 0.5f, c.GetFloorNormal() );
		return true;
	}

	/// <summary> Enemies the ball meets: damage by its speed; light ones are knocked flying (human pinballs), heavy ones bounce it off. </summary>
	void RollHits()
	{
		var at = BallCentre;
		var v = controller.Velocity;
		var speed = v.Flat().Length();
		if ( speed < PinballFx.RollMinSpeed ) return;
		var now = Clock.Now;
		var strike = 1f + PinballFx.StrikePer * Skill( SkillId.Strike );
		var launcher = controller.GetChildren().OfType<PlayerWeapon>().FirstOrDefault( w => w.Pinball );
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || c.Grabbed ) continue;
			var part = SkillFx.NearestPart( c, at );
			if ( part.DistanceTo( at ) > PinballFx.RollReach ) continue;
			if ( rollHits.TryGetValue( c, out var last ) && now - last < PinballFx.RollRehit ) continue;
			rollHits[c] = now;
			var fresh = bowled.Add( c );
			PinballFx.Bowled++;
			var damage = PinballFx.RollDamagePer * speed * strike;
			SkillFx.HitPart( c, damage, at, controller );
			var away = (c.GlobalPosition - controller.GlobalPosition).Flat().SafeNormal();
			if ( away == Vector3.Zero ) away = v.Flat().SafeNormal();
			BumperFlash.Spawn( part, -away, bowled.Count >= 3 ? PinballFx.Yellow : PinballFx.Neon, 1.3f );
			PinballFx.HitSound( part, bowled.Count, 1f, heavy: true );
			if ( fresh && bowled.Count >= 2 ) PinballPopup.Show( part + Vector3.Up * 0.6f, bowled.Count >= 5 ? "STRIKE!" : $"x{bowled.Count}", bowled.Count >= 3 ? PinballFx.Yellow : PinballFx.Neon, 1f + 0.08f * bowled.Count );
			Power = MathF.Min( MaxPower, Power + PinballFx.TiltPer * Skill( SkillId.TiltBonus ) );
			Juice?.Shake( MathX.Clamp( speed / 40f, 0.15f, 0.4f ) );
			Controls.Rumble( 0.35f );
			if ( c.IsDead ) { launcher?.MarkKill(); continue; }
			launcher?.MarkHit();
			if ( c.Pullable && !c.Carried && launcher is not null )
			{
				// A light one is bowled away ahead of the ball, into the others; the ball loses a little.
				var fling = (v.Flat().SafeNormal() * 0.6f + away * 0.4f).SafeNormal();
				HumanPinball.Launch( launcher, controller, c, (fling + Vector3.Up * 0.35f).SafeNormal() * MathF.Max( PinballFx.LaunchSpeed, speed * 1.1f ), damage * 0.6f );
				controller.Velocity = v * 0.85f;
			}
			else
			{
				// A heavy one: the ball bounces off it.
				var hb = v.Flat();
				hb = (hb - 2f * hb.Dot( -away ) * -away) * 0.7f;
				controller.Velocity = hb + Vector3.Up * MathF.Max( v.Y, 2.5f );
				c.Stun( 0.4f );
			}
		}
	}

	/// <summary> A see-through pinball round the curled-up robot, with neon bands, turning as it rolls. </summary>
	void Shell( bool on )
	{
		if ( on && rollShell is null )
		{
			rollShell = new Node3D { Name = "Roll Shell" };
			controller.AddChild( rollShell );
			// Plain glass (user's ask, no neon bands): clear in the middle, bright at the edges, bending what is behind it a
			// little, with a sharp highlight; the inside drawn too, faintly, so it reads as a hollow ball.
			var glass = new ShaderMaterial { Shader = RollGlass };
			glass.SetMeta( "no_caustics", true );
			var inner = new ShaderMaterial { Shader = RollGlass };
			inner.SetShaderParameter( "inside", 1f );
			inner.SetMeta( "no_caustics", true );
			var sphere = new SphereMesh { Radius = 0.78f, Height = 1.56f, RadialSegments = 64, Rings = 32 };
			rollShell.AddChild( new MeshInstance3D { Mesh = sphere, MaterialOverride = inner, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
			rollShell.AddChild( new MeshInstance3D { Mesh = sphere, MaterialOverride = glass, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		}
		if ( rollShell is null ) return;
		rollShell.Visible = on;
		rollShell.Position = Vector3.Up * 0.62f;
	}

	static readonly Shader RollGlass = new()
	{
		Code = @"shader_type spatial;
render_mode blend_mix, depth_draw_never, specular_schlick_ggx;
uniform float inside = 0.0;
uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
void vertex() { if (inside > 0.5) { NORMAL = -NORMAL; } }
void fragment() {
	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.6);
	// What is behind, bent toward the edges like a lens.
	vec2 bend = NORMAL.xy * (0.025 + 0.04 * fres) * (inside > 0.5 ? -0.5 : 1.0);
	vec3 behind = textureLod(screen, SCREEN_UV - bend, 0.0).rgb;
	vec3 tint = vec3(0.86, 0.95, 1.0);
	ALBEDO = mix(behind * tint, vec3(0.9, 0.97, 1.0), fres * 0.35);
	ALPHA = (inside > 0.5 ? 0.25 : 1.0) * clamp(0.18 + fres * 0.75, 0.0, 0.92);
	ROUGHNESS = 0.03;
	METALLIC = 0.0;
	SPECULAR = 1.0;
	CLEARCOAT = 1.0;
	CLEARCOAT_ROUGHNESS = 0.02;
	EMISSION = vec3(0.6, 0.8, 1.0) * fres * 0.15;
}",
	};

	void SpinShell( float dt )
	{
		if ( rollShell is null ) return;
		var flat = controller.Velocity.Flat();
		if ( flat.LengthSquared() < 0.01f ) return;
		var axis = Vector3.Up.Cross( flat ).Normalized();
		rollShell.GlobalBasis = (new Basis( axis, flat.Length() * dt / 0.78f ) * rollShell.GlobalBasis).Orthonormalized();
	}

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
		rollHits.Clear();
		rollLeft = PinballFx.RollTime + PinballFx.LongRollPer * Skill( SkillId.LongRoll );
		// Into third person (first person showed nothing of it), back to the view you had at the end.
		rollWasThird = controller.ThirdPerson;
		if ( !controller.IsRemote ) controller.ThirdPerson = true;
		// Launched off in the way you move (or look), at least `RollStart` fast.
		var yaw = new Angles( 0f, controller.EyeAngles.Yaw, 0f );
		var input = controller.UseInputControls ? controller.In.GetVector( "left", "right", "backward", "forward" ) : Vector2.Zero;
		var dir = (yaw.Forward * input.Y + yaw.Right * input.X).Flat().SafeNormal();
		if ( dir == Vector3.Zero ) dir = yaw.Forward.Flat().SafeNormal();
		var h = controller.Velocity.Flat();
		controller.Velocity = dir * MathF.Max( h.Length(), PinballFx.RollStart * SpinUp ) + Vector3.Up * controller.Velocity.Y;
		controller.MoveOverride = RollStep;
		Shell( true );
		Sfx.Play( "pinball_ding", controller.GlobalPosition, 0.8f, 0.7f );
		Sfx.Play( "boing", controller.GlobalPosition, 0.6f, 0.8f );
		Sfx.Play( "slide", controller.GlobalPosition, 0.7f, 1.3f );
		Juice?.KickFov( 8f );
	}

	void EndRoll()
	{
		if ( !Rolling ) return;
		Rolling = false;
		rollLeft = 0f;
		if ( controller.MoveOverride == RollStep ) controller.MoveOverride = null;
		if ( !controller.IsRemote ) controller.ThirdPerson = rollWasThird;
		Shell( false );
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

	/// <summary> Each frame: the reversal's delay, the sling's cord and landing, the roll's bowling and timer. </summary>
	void ToySkills( float dt )
	{
		if ( reversalAt > 0 )
		{
			// Pole Reversal's pull, then the pulse.
			var radius = PulseRadius * 1.6f;
			foreach ( var c in Creature.Everyone )
			{
				if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || !c.Pullable ) continue;
				var to = (controller.GlobalPosition - c.GlobalPosition).Flat();
				var d = to.Length();
				if ( d > radius || d < 1.2f ) continue;
				c.Pull( to / d * MathF.Min( d - 1.2f, 9f * dt ) );
			}
			if ( Clock.Now >= reversalAt ) { reversalAt = -1; Pulse( true ); }
		}
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
			sinceBounce += dt;
			controller.EyeDrop = MathX.Lerp( controller.EyeDrop, PinballFx.EyeDrop, 1f - MathF.Exp( -14f * dt ) );
			SpinShell( dt );
			RollHits();
			if ( rollLeft <= 0f || !(controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.IsAlive ?? true) ) EndRoll();
		}
	}
}
