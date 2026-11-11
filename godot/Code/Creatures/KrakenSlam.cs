using Godot;

/// <summary>
/// The Kraken's tentacle slam (issue #30, stage 3). It sucks players toward its mouth for a second, rears up to
/// three tentacles over the nearest player, then whips them down. The strike lines show on the sea bed and lock a
/// moment before the hit, so running or dashing sideways gets you clear. A player high up off the sea bed is
/// swatted back down instead, for less damage. Tentacles stretch to reach anywhere inside the ring; ones that
/// have been shot short reach less far, and with none left there is no slam.
/// </summary>
public partial class Kraken
{
	public enum Attack { None, Suck, Raise, Slam, Hold, Recover }
	public Attack Attacking { get; private set; }

	/// <summary> Seconds of each part of the slam. </summary>
	public float SuckTime = 1f, RearTime = 0.9f, WhipTime = 0.16f, HoldTime = 0.55f, RecoverTime = 0.9f;
	/// <summary> The strike lines stop following the player this long before the hit. </summary>
	public float LockBefore = 0.3f;
	/// <summary> How hard the mouth pulls players in (metres per second, per second). </summary>
	public float SuckPull = 30f;
	/// <summary> Damage of a slam on a player on the sea bed, and of the swat on one high above it. </summary>
	public float SlamDamage = 35f, SwatDamage = 12f;
	/// <summary> A player this far above the sea bed (metres) is swatted down rather than crushed. </summary>
	public float SwatHeight = 3f;
	/// <summary> Half the width of a strike line (metres), and the angle between the tentacles of one slam. </summary>
	public float SlamHalfWidth = 1.3f, FanAngle = 13f;
	/// <summary> How far a tentacle can stretch (times its length), and seconds between attacks. </summary>
	public float MaxStretch = 3.4f, AttackGapMin = 5f, AttackGapMax = 8f;

	/// <summary> For tests: how many players the last slam hit, and how many of those were swatted out of the air. </summary>
	public int LastSlamHits { get; private set; }
	public int LastSlamSwats { get; private set; }

	const int MaxSlammers = 3;
	// Reared up over the player like a cobra, and laid out flat along the strike line.
	static readonly float[] RearBend = { 78, -4, -4, -4, -4, -4, -4, -4 };

	readonly List<int> slammers = new();
	readonly float[] slamBlend = new float[Tentacles];
	readonly float[] stretch = Enumerable.Repeat( 1f, Tentacles ).ToArray();
	readonly Basis?[] restBasis = new Basis?[Tentacles];
	readonly MeshInstance3D[] strips = new MeshInstance3D[MaxSlammers];
	float attackTime;
	TimeUntil nextAttack = 4f;
	Vector3 aimPoint;
	bool aimLocked;
	CpuParticles3D suction;

	Vector3 MouthPosition => rig.GlobalTransform * new Vector3( 0f, 0.9f, -1.1f );

	/// <summary> Dev menu and tests: start a slam now. </summary>
	public void ForceSlam()
	{
		if ( State == Mode.Idle && Attacking == Attack.None && !IsDead ) Begin( Attack.Suck );
	}

	/// <summary> Idle and not frozen: face the nearest player (unless committed to a strike) and pick the next attack. </summary>
	void Think( float dt )
	{
		if ( PlayerSurvivor.FindNearestAlive( GlobalPosition ) is not { } target ) return;
		if ( Attacking is not (Attack.Slam or Attack.Hold) && !aimLocked ) Face( target.WorldPosition - GlobalPosition, TurnRate * (Attacking == Attack.None ? 1f : 2.5f), dt );
		if ( Attacking == Attack.None && nextAttack <= 0f && TentaclesLeft > 0 ) Begin( Attack.Suck );
	}

	void Begin( Attack next )
	{
		Attacking = next;
		attackTime = 0f;
		switch ( next )
		{
			case Attack.Suck:
				aimLocked = false;
				Sfx.Play( "kraken_suck", MouthPosition, 1f );
				StartSuction();
				break;
			case Attack.Raise:
				StopSuction();
				PickSlammers();
				if ( slammers.Count == 0 ) { Begin( Attack.None ); return; }
				break;
			case Attack.Slam:
				Sfx.Play( "axe_swing", MouthPosition, 1f, 0.5f );
				break;
			case Attack.Hold:
				Impact();
				break;
			case Attack.None:
				StopSuction();
				slammers.Clear();
				aimLocked = false;
				nextAttack = Rng.Float( AttackGapMin, AttackGapMax );
				foreach ( var strip in strips ) if ( strip is not null && IsInstanceValid( strip ) ) strip.Visible = false;
				break;
		}
	}

	/// <summary> The tentacles nearest the mouth that are still there: the first aims at the player, the others fan out. </summary>
	void PickSlammers()
	{
		slammers.Clear();
		// Tentacle k sits at (k + 0.5) / 8 of a turn from straight ahead, so 0 and 7 flank the mouth, then 1 and 6.
		foreach ( var k in new[] { 0, 7, 1, 6, 2, 5 } )
			if ( length[k] > 0 && slammers.Count < MaxSlammers ) slammers.Add( k );
	}

	/// <summary> Carries an attack that has started through its parts (even with enemies frozen, so a forced one finishes). </summary>
	void UpdateAttack( float dt )
	{
		// Tentacles settle back whenever they are not part of a strike.
		for ( var k = 0; k < Tentacles; k++ )
		{
			var striking = slammers.Contains( k ) && Attacking is Attack.Raise or Attack.Slam or Attack.Hold;
			if ( !striking || !slammers.Contains( k ) )
			{
				slamBlend[k] = MathX.Approach( slamBlend[k], 0f, dt / RecoverTime );
				stretch[k] = MathX.Approach( stretch[k], 1f, dt * MaxStretch / RecoverTime );
			}
		}
		if ( Attacking == Attack.None ) return;
		attackTime += dt;
		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		switch ( Attacking )
		{
			case Attack.Suck:
				Suck( dt );
				if ( attackTime >= SuckTime ) Begin( Attack.Raise );
				break;
			case Attack.Raise:
				if ( !aimLocked && target is not null ) aimPoint = target.WorldPosition.WithY( GlobalPosition.Y );
				if ( attackTime >= RearTime - LockBefore ) aimLocked = true;
				foreach ( var k in slammers )
				{
					slamBlend[k] = MathX.Approach( slamBlend[k], 1f, dt / 0.4f );
					stretch[k] = MathX.Approach( stretch[k], WantedStretch( k ), dt * MaxStretch / (RearTime * 0.7f) );
				}
				ShowStrips();
				if ( attackTime >= RearTime ) Begin( Attack.Slam );
				break;
			case Attack.Slam:
				ShowStrips();
				if ( attackTime >= WhipTime ) Begin( Attack.Hold );
				break;
			case Attack.Hold:
				if ( attackTime >= HoldTime ) Begin( Attack.Recover );
				break;
			case Attack.Recover:
				if ( attackTime >= RecoverTime ) Begin( Attack.None );
				break;
		}
	}

	// ---- The pull ----

	void Suck( float dt )
	{
		var mouth = MouthPosition;
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive || player.Controller is not { Scripted: false } controller ) continue;
			var to = (mouth - player.WorldPosition).Flat();
			var distance = to.Length();
			if ( distance > FightRadius * 1.25f || distance < 2.5f ) continue;
			controller.Velocity += to / distance * SuckPull * dt;
			controller.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.35f * dt );
		}
	}

	static StandardMaterial3D suctionMaterial;

	void StartSuction()
	{
		suctionMaterial ??= new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.75f, 1f, 0.9f, 0.5f ),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		// Streaks of water rushing in to the mouth from all around.
		suction = new CpuParticles3D
		{
			Name = "Kraken Suction",
			Mesh = new SphereMesh { Radius = 0.05f, Height = 0.9f, RadialSegments = 6, Rings = 3, Material = suctionMaterial },
			Amount = 90,
			Lifetime = 0.75f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.SphereSurface,
			EmissionSphereRadius = 11f,
			Gravity = Vector3.Zero,
			RadialAccelMin = -42f,
			RadialAccelMax = -34f,
			ParticleFlagAlignY = true,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		GetTree().CurrentScene.AddChild( suction );
		suction.GlobalPosition = MouthPosition;
		suction.Emitting = true;
	}

	void StopSuction()
	{
		if ( suction is null || !IsInstanceValid( suction ) ) { suction = null; return; }
		suction.Emitting = false;
		Fx.Expire( suction, 1f );
		suction = null;
	}

	// ---- The strike ----

	Vector3 RootPosition( int k ) => parts[Tentacle0 + k].Go.GlobalPosition;

	/// <summary> The way tentacle i of the slam strikes: at the aim point, fanned out for the second and third. </summary>
	Vector3 StrikeDirection( int k )
	{
		var i = slammers.IndexOf( k );
		var offset = i <= 0 ? 0f : (i == 1 ? FanAngle : -FanAngle);
		var to = (aimPoint - RootPosition( k )).Flat().SafeNormal();
		if ( to == Vector3.Zero ) to = -GlobalBasis.Z;
		return to.Rotated( Vector3.Up, MathX.Rad( offset ) );
	}

	float WantedStretch( int k )
	{
		var natural = length[k] * SegLength;
		var needed = (aimPoint - RootPosition( k )).Flat().Length() + 0.6f;
		return MathX.Clamp( needed / MathF.Max( natural, 0.1f ), 1f, MaxStretch );
	}

	float Reach( int k ) => length[k] * SegLength * stretch[k];

	/// <summary> After the idle pose each frame: lays the striking tentacles over it, and stretches the segments. </summary>
	void ApplySlamPose()
	{
		for ( var k = 0; k < Tentacles; k++ )
		{
			if ( length[k] == 0 ) continue;
			var root = parts[Tentacle0 + k].Go;
			restBasis[k] ??= root.Basis;
			for ( var j = 0; j < length[k]; j++ )
			{
				var segment = segments[k, j];
				if ( j > 0 ) segment.Position = new Vector3( 0f, 0f, -SegLength * stretch[k] );
				if ( segment.GetChildCount() > 0 && segment.GetChild( 0 ) is Node3D model ) model.Scale = new Vector3( 1f, 1f, stretch[k] );
			}
			var blend = Ease( slamBlend[k] );
			if ( blend <= 0.001f ) { root.Basis = restBasis[k].Value; continue; }

			// Turned to point along its strike line (the rig only turns about the upright, so flat directions carry over).
			var local = rig.GlobalBasis.Inverse() * StrikeDirection( k );
			root.Basis = new Basis( restBasis[k].Value.GetRotationQuaternion().Slerp( Shapes.LookAt( local ).GetRotationQuaternion(), blend ) );
			// Reared up, then whipped down flat with the tip on the sea bed.
			var down = Attacking switch
			{
				Attack.Slam => MathF.Pow( MathX.Clamp( attackTime / WhipTime, 0f, 1f ), 2f ),
				Attack.Hold or Attack.Recover => 1f,
				_ => 0f,
			};
			var flat = -MathX.Deg( MathF.Atan2( 0.45f, MathF.Max( 1f, Reach( k ) ) ) );
			for ( var j = 0; j < length[k]; j++ )
			{
				var strike = MathX.Lerp( RearBend[j], j == 0 ? flat : 0f, down );
				var idle = segments[k, j].Basis.GetEuler();
				var bend = MathX.Lerp( MathX.Deg( idle.X ), strike, blend );
				segments[k, j].Basis = new Basis( Vector3.Up, idle.Y * (1f - blend) ) * new Basis( Vector3.Right, MathX.Rad( bend ) );
			}
		}
	}

	static StandardMaterial3D stripMaterial;

	/// <summary> The strike lines on the sea bed: faint while they follow the player, bright once they are locked. </summary>
	void ShowStrips()
	{
		stripMaterial ??= new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var pulse = 0.5f + 0.5f * MathF.Sin( time * 30f );
		stripMaterial.AlbedoColor = aimLocked ? new Color( 1f, 0.2f, 0.12f, 0.45f + pulse * 0.25f ) : new Color( 1f, 0.6f, 0.15f, 0.22f );
		for ( var i = 0; i < MaxSlammers; i++ )
		{
			if ( strips[i] is null || !IsInstanceValid( strips[i] ) )
			{
				strips[i] = new MeshInstance3D { Name = "Kraken Strike Line", Mesh = Shapes.Box, MaterialOverride = stripMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
				GetTree().CurrentScene.AddChild( strips[i] );
			}
			strips[i].Visible = i < slammers.Count;
			if ( i >= slammers.Count ) continue;
			var k = slammers[i];
			var from = RootPosition( k ).WithY( GlobalPosition.Y + 0.12f );
			var direction = StrikeDirection( k );
			var reach = length[k] * SegLength * WantedStretch( k );
			strips[i].GlobalTransform = new Transform3D( Shapes.LookAt( direction ) * Basis.FromScale( new Vector3( SlamHalfWidth * 2f, 0.04f, reach ) ), from + direction * reach * 0.5f );
		}
	}

	/// <summary> The tentacles land: sand, a jolt, and damage for anyone under one. </summary>
	void Impact()
	{
		foreach ( var strip in strips ) if ( strip is not null && IsInstanceValid( strip ) ) strip.Visible = false;
		LastSlamHits = LastSlamSwats = 0;
		var struck = new HashSet<PlayerSurvivor>();
		foreach ( var k in slammers )
		{
			if ( length[k] == 0 ) continue;
			var from = RootPosition( k ).WithY( GlobalPosition.Y );
			var direction = StrikeDirection( k );
			var reach = Reach( k );
			for ( var d = 1.5f; d < reach; d += 2.5f ) SandBurst( from + direction * d, 0.9f, 12, 2.6f );
			SandBurst( from + direction * reach, 1.4f, 26, 3.4f );
			foreach ( var player in PlayerSurvivor.All )
			{
				if ( !player.IsAlive || struck.Contains( player ) || player.Controller is not { } controller ) continue;
				var offset = (player.WorldPosition - from).Flat();
				var along = MathX.Clamp( offset.Dot( direction ), 0f, reach );
				if ( (offset - direction * along).Length() > SlamHalfWidth + controller.BodyRadius ) continue;
				struck.Add( player );
				LastSlamHits++;
				var high = player.WorldPosition.Y - GlobalPosition.Y > SwatHeight;
				if ( high )
				{
					// Swatted out of the water above: straight back down to the sea bed, but it only glanced them.
					LastSlamSwats++;
					if ( !controller.Scripted ) controller.Velocity = Vector3.Down * 22f + direction * 2f;
				}
				else if ( !controller.Scripted ) controller.Velocity += direction * 7f + Vector3.Up * 3.5f;
				player.Health?.OnDamage( new DamageInfo { Damage = high ? SwatDamage : SlamDamage, Attacker = this, Position = player.WorldPosition + Vector3.Up, Origin = from } );
				player.Juice?.Shake( high ? 0.5f : 0.9f );
			}
		}
		Sfx.Play( "kraken_slam", aimPoint, 1f );
		ShakeNear( 0.6f );
	}

	/// <summary> It died or left: nothing of the attack stays behind. </summary>
	void EndAttack()
	{
		// Freed outright: this also runs as the level is torn down, when there is no tree left to time a fade.
		if ( suction is not null && IsInstanceValid( suction ) ) suction.QueueFree();
		suction = null;
		Attacking = Attack.None;
		slammers.Clear();
		foreach ( var strip in strips ) if ( strip is not null && IsInstanceValid( strip ) ) strip.QueueFree();
	}
}
