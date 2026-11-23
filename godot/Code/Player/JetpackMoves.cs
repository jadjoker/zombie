using Godot;

/// <summary>
/// The Hook-Bot's and Coral-Bot's mouse 4 moves. The grapple casts a line along your aim up to `GrappleRange`: where
/// it bites (rock, wreck or an enemy) you zip along it (the blink's surge, stopping short of what it meets), and a zip
/// into an enemy ends in a boot (Boot Stomp, Leviathan Pull). The burrow digs you into the sand: untouchable and quick
/// for `BurrowTime` with the view at sand level, then you erupt (or tap again to come up early), hurting and throwing
/// enemies near you (Upheaval, Great Barrier).
/// </summary>
public partial class Jetpack
{
	bool zip;
	Creature zipTarget;
	Vector3 zipPoint;
	MeshInstance3D grappleLine;
	double zipEndedAt = -99;
	int killsAtZip;
	bool seaDogPaid = true;
	// Burrow
	public bool Burrowed { get; private set; }
	float burrowLeft, stillFor;
	double eruptedAt = -99, barrierAt = -99;
	TimeSince sinceSand;
	/// <summary> How long the player has stood still (Calcify). </summary>
	public float StillFor => stillFor;
	public bool Zipping => zip && Surging;
	/// <summary> For the third person rig: when the last eruption came. </summary>
	public double EruptedAt => eruptedAt;

	static StandardMaterial3D grappleRope;

	/// <summary> Testing aid: grapple as if mouse 4 were tapped. </summary>
	public void TestGrapple() => TryGrapple();
	/// <summary> Testing aid: burrow (or erupt) as if mouse 4 were tapped. </summary>
	public void TestBurrow() => ToggleBurrow();

	void TryGrapple()
	{
		if ( Surging || Burrowed ) return;
		var cost = HookFx.GrappleCost * (1f - HookFx.RechargePer * Skill( SkillId.ReelRecharge ));
		if ( Power < cost ) { TimeSinceRefused = 0f; return; }
		var camera = controller.Camera;
		var eye = camera?.GlobalPosition ?? controller.EyeTransform.Origin;
		var forward = camera is not null ? -camera.GlobalBasis.Z : controller.EyeAngles.Forward;
		var range = HookFx.GrappleRange + HookFx.LongCastPer * Skill( SkillId.LongCast );
		var tr = Trace.Ray( controller, eye, eye + forward * range, Layers.World | Layers.Creature, new Godot.Collections.Array<Rid> { controller.GetRid() } );
		if ( !tr.Hit ) { TimeSinceRefused = 0f; Sfx.Play2D( "dry_fire", 0.6f, 0.7f ); return; }
		zipPoint = tr.HitPosition;
		zipTarget = SkillFx.EnemyOf( tr.Node );
		var from = controller.GlobalPosition + Vector3.Up * 1f;
		var to = zipPoint - from;
		var distance = to.Length();
		if ( distance < 1.5f ) return;
		HookFx.Zips++;
		Power -= cost;
		blink = true;
		zip = true;
		surgeWasBlink = true;
		rammed.Clear();
		SurgeStrength = 0.8f;
		surgeDirection = to / distance;
		surgeSpeed = HookFx.GrappleSpeed;
		surgeTotal = surgeLeft = MathX.Clamp( (distance - 1.1f) / (HookFx.GrappleSpeed * U.In), 0.08f, 1.4f );
		timeSinceThrust = 0f;
		controller.PreventGrounding( 0.15f );
		grappleLine ??= GunKit.Line( controller, grappleRope ??= new StandardMaterial3D { AlbedoColor = new Color( 0.86f, 0.74f, 0.5f ), Roughness = 0.8f }, 0.01f );
		Sfx.Play( "harpoon", controller.GlobalPosition, 0.7f, 0.75f );
		Sfx.Play( "surge", controller.GlobalPosition, 0.5f, 1.2f );
		Juice?.Shake( 0.15f );
		Juice?.KickFov( 10f );
	}

	/// <summary> The zip is over: Momentum, and a boot (or Leviathan Pull's slam) into the enemy it was cast at. </summary>
	void ZipArrived( Node3D hitEnemy )
	{
		if ( !zip ) return;
		zip = false;
		zipEndedAt = Clock.Now;
		killsAtZip = controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.Kills ?? 0;
		seaDogPaid = false;
		if ( grappleLine is not null ) grappleLine.Visible = false;
		var momentum = Skill( SkillId.Momentum );
		if ( momentum > 0 && PlayerPerks.Of( controller ) is { } perks ) { perks.Boost = HookFx.MomentumPer * momentum; perks.BoostUntil = Clock.Now + HookFx.MomentumTime; }
		var target = zipTarget is { IsDead: false } t && IsInstanceValid( t ) && t.GlobalPosition.DistanceTo( controller.GlobalPosition ) < 4f ? t
			: hitEnemy is Creature { IsEnemy: true, IsDead: false } h ? h : null;
		zipTarget = null;
		if ( target is null ) return;
		HookFx.Kicks++;
		var at = controller.GlobalPosition + Vector3.Up;
		SkillFx.HitPart( target, HookFx.ZipKick + HookFx.StompPer * Skill( SkillId.BootStomp ), at, controller );
		SkillFx.Shove( target, surgeDirection.Flat().SafeNormal() * 1.6f );
		if ( !target.IsDead ) target.Stun( 0.3f );
		Sfx.Play( "axe_hit", at, 0.9f, 0.8f );
		Juice?.Shake( 0.35f );
		Controls.Rumble( 0.4f );
		var lev = Skill( SkillId.LeviathanPull );
		if ( lev <= 0 ) return;
		// Leviathan Pull: the boot becomes a slam that drags everything near into it.
		HookFx.Slams++;
		var centre = target.GlobalPosition;
		var damage = HookFx.LeviathanDamage( lev );
		SkillFx.HitPart( target, damage, at, controller );
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( c == target || !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var d = c.GlobalPosition.DistanceTo( centre );
			if ( d > HookFx.LeviathanRadius ) continue;
			if ( c.Pullable ) c.Pull( (centre - c.GlobalPosition) * 0.6f );
			SkillFx.HitPart( c, damage * 0.5f, centre, controller );
			if ( !c.IsDead ) c.Stun( 0.5f );
		}
		Shockwave.Spawn( GetTree().CurrentScene, centre + Vector3.Up * 0.5f, HookFx.LeviathanRadius, new Color( 0.35f, 0.25f, 0.75f ) );
		Jetpack.SlamBubbles( centre, Vector3.Up, 1f );
		Sfx.Play( "kraken_slam", centre, 0.9f, 1.2f );
		Juice?.Shake( 0.7f );
	}

	void ToggleBurrow()
	{
		if ( Burrowed ) { Erupt(); return; }
		if ( Surging ) return;
		var cost = CoralFx.BurrowCost * (1f - CoralFx.SandstormPer * Skill( SkillId.Sandstorm ));
		if ( Power < cost || !controller.IsOnGround || Clock.Now - eruptedAt < CoralFx.BurrowCooldown ) { TimeSinceRefused = 0f; return; }
		CoralFx.Burrows++;
		Power -= cost;
		timeSinceThrust = 0f;
		Burrowed = true;
		burrowLeft = CoralFx.BurrowTime + CoralFx.DeepDigPer * Skill( SkillId.DeepDig );
		if ( controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) is { } survivor ) survivor.UntouchableUntil = Clock.Now + burrowLeft + 0.15f;
		if ( PlayerPerks.Of( controller ) is { } perks ) { perks.Boost = CoralFx.BurrowBoost; perks.BoostUntil = Clock.Now + burrowLeft; }
		Sfx.Play( "slide", controller.GlobalPosition, 0.8f, 0.7f );
		SandBurst( 18 );
		Juice?.Shake( 0.2f );
	}

	void Erupt()
	{
		if ( !Burrowed ) return;
		Burrowed = false;
		burrowLeft = 0f;
		eruptedAt = Clock.Now;
		timeSinceThrust = 0f;
		CoralFx.Eruptions++;
		if ( PlayerPerks.Of( controller ) is { } perks ) perks.BoostUntil = 0;
		if ( controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) is { } survivor ) survivor.UntouchableUntil = Clock.Now + 0.15f;
		var at = controller.GlobalPosition;
		var up = 1f + CoralFx.UpheavalPer * Skill( SkillId.Upheaval );
		var radius = CoralFx.EruptRadius * up;
		var damage = CoralFx.EruptDamage * up;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var d = c.GlobalPosition.DistanceTo( at );
			if ( d > radius ) continue;
			SkillFx.HitPart( c, damage * (1f - 0.4f * d / radius), at, controller );
			SkillFx.Shove( c, ((c.GlobalPosition - at).Flat().SafeNormal() * 1.8f) + Vector3.Up * 0.6f );
			if ( !c.IsDead ) c.Stun( 0.5f );
		}
		var heal = Skill( SkillId.Regrowth );
		if ( heal > 0 ) controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.Health?.Heal( CoralFx.RegrowthPer * heal );
		var barrier = Skill( SkillId.GreatBarrier );
		if ( barrier > 0 && Clock.Now - barrierAt >= CoralFx.BarrierTime( barrier ) ) { barrierAt = Clock.Now; GreatBarrier( at, barrier ); }
		controller.Velocity = Vector3.Up * 260f * U.In;
		controller.PreventGrounding( 0.15f );
		SandBurst( 40 );
		GibKit.Burst( CoralFx.Gibs, at + Vector3.Up * 0.3f, Vector3.Up, 0.1f, 6, 1.3f );
		Shockwave.Spawn( GetTree().CurrentScene, at + Vector3.Up * 0.3f, radius, new Color( 0.9f, 0.8f, 0.6f ) );
		Sfx.Play( "dive_impact", at, 0.9f, 1.1f );
		Juice?.Shake( 0.5f );
		Juice?.KickFov( 8f );
		Controls.Rumble( 0.5f );
	}

	/// <summary> Great Barrier: a ring of coral walls round the eruption, spearing what is caught inside. </summary>
	void GreatBarrier( Vector3 at, int n )
	{
		CoralFx.Barriers++;
		var weapon = controller.GetChildren().OfType<PlayerWeapon>().FirstOrDefault( w => w.Seeds );
		const int segments = 8;
		var r = CoralFx.BarrierRadius;
		var width = 2f * r * MathF.Tan( MathF.PI / segments ) + 0.3f;
		for ( var i = 0; i < segments; i++ )
		{
			var a = i * MathF.Tau / segments;
			var outward = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var spot = at + outward * r;
			var floor = Trace.Ray( controller, spot + Vector3.Up * 2f, spot + Vector3.Down * 6f, Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
			CoralWall.Grow( controller, weapon, floor.Hit ? floor.HitPosition : spot, outward, CoralFx.BarrierTime( n ), width, false );
		}
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			if ( (c.GlobalPosition - at).Flat().Length() > r ) continue;
			var part = SkillFx.NearestPart( c, at );
			PlayerWeapon.SpineStreak( GetTree().CurrentScene, part + Vector3.Down * 1.2f, part );
			SkillFx.HitPart( c, CoralFx.BarrierDamage( n ), part + Vector3.Down, controller );
		}
	}

	void SandBurst( int count )
	{
		var at = controller.GlobalPosition;
		for ( var i = 0; i < count; i++ )
		{
			var dir = (Rng.OnSphere().Flat().SafeNormal() * 0.8f + Vector3.Up * Rng.Float( 0.4f, 1.2f )).SafeNormal();
			GpuFx.SandPuff( at + Vector3.Up * 0.15f, dir * Rng.Float( 2f, 6f ), new Color( 0.82f, 0.74f, 0.56f, 0.85f ), Rng.Float( 6f, 14f ), Rng.Float( 0.6f, 1.2f ) );
		}
	}

	/// <summary> Each frame: the grapple's line, the burrow's timer and sand, Calcify's stillness, Sea Dog's heal. </summary>
	void MoveSkills( float dt )
	{
		if ( zip && grappleLine is not null )
		{
			var hand = controller.GetChildren().OfType<PlayerWeapon>().FirstOrDefault( w => w.Active )?.LinePoint() ?? controller.GlobalPosition + Vector3.Up;
			var end = zipTarget is { IsDead: false } t && IsInstanceValid( t ) ? SkillFx.NearestPart( t, hand ) : zipPoint;
			GunKit.Lay( grappleLine, hand, end );
		}
		if ( Burrowed )
		{
			burrowLeft -= dt;
			controller.EyeDrop = MathX.Lerp( controller.EyeDrop, CoralFx.EyeDrop, 1f - MathF.Exp( -14f * dt ) );
			if ( sinceSand > 0.04f )
			{
				sinceSand = 0f;
				GpuFx.SandPuff( controller.GlobalPosition + Vector3.Up * 0.1f + Rng.InSphere( 0.3f ), Vector3.Up * Rng.Float( 0.5f, 1.5f ) + Rng.InSphere( 0.8f ), new Color( 0.82f, 0.74f, 0.56f, 0.8f ), Rng.Float( 8f, 14f ), 0.9f );
			}
			if ( burrowLeft <= 0f ) Erupt();
		}
		else if ( Rolling ) { }   // the roll keeps the view low (JetpackToys)
		else if ( controller.EyeDrop > 0.001f ) controller.EyeDrop = MathX.Lerp( controller.EyeDrop, 0f, 1f - MathF.Exp( -10f * dt ) );
		else controller.EyeDrop = 0f;
		stillFor = controller.Velocity.Length() < 0.4f && controller.IsOnGround ? stillFor + dt : 0f;
		var seaDog = Skill( SkillId.SeaDog );
		if ( !seaDogPaid && seaDog > 0 && Clock.Now - zipEndedAt < HookFx.SeaDogTime && controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) is { } s && s.Kills > killsAtZip )
		{
			seaDogPaid = true;
			HookFx.SeaDogHeals++;
			s.Health?.Heal( HookFx.SeaDogPer * seaDog );
		}
	}
}
