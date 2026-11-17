using Godot;
using static SmoothKit;

/// <summary>
/// How Poseidon moves. Poses settle on springs (<see cref="SmoothKit.Spring"/>), heavy and slow for a giant, so he eases
/// and overshoots instead of snapping. He swims with a dolphin kick (a wave down the tail, the body pitching against it),
/// leans into his swim, banks into turns with the tail swinging out, breathes, and keeps his eyes on the player. To throw
/// he coils (leans back, turns the trident shoulder away, aims with his left fist, tail loaded), then whips round with a
/// kick and follows through. To summon he rears up and rises, both fists high, head back, roaring. To stab he coils as
/// the whirlpool brings the victim, then lunges. Spinning into a whirlpool flings his hair and beard out. A heavy hit
/// makes him flinch. His brows knit and his jaw drops when he attacks; hair and beard flow in lagging waves.
/// </summary>
public partial class Poseidon
{
	float swimPhase, finBeat;
	float lean, leanV, twist, twistV, bank, bankV, rise, riseV, tailSide, tailSideV;
	float headYaw, headYawV, headPitch, headPitchV, jawOpen, anger = 0.3f;
	float lastYaw, yawRate, spin;
	bool yawSeeded;
	TimeSince flinchAt = 99f;

	void Animate( float t, float dt )
	{
		body.Scale = Vector3.One * Size * MathF.Max( 0.02f, shown );
		var flash = MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f );
		skin.SetShaderParameter( "flash", flash );
		handSkin.SetShaderParameter( "flash", flash );
		skin.SetShaderParameter( "centre", body.GlobalPosition );
		rig?.TailPaint?.SetShaderParameter( "flash", flash );
		finBeat += dt * (2f + 3f * MathX.Clamp( velocity.Length() / SwimSpeed, 0f, 1f ));
		finPaint?.SetShaderParameter( "beat", finBeat );
		if ( mode == Mode.Dead )
		{
			// Dying (Sink poses him): hair and beard thrash.
			Locks( t, 0f, 1f, 0f, dt );
			rig?.Pose();
			return;
		}

		var moving = MathX.Clamp( velocity.Length() / SwimSpeed, 0f, 1f );
		var u = (float)inMode;
		// A flinch at most every second or so (he is shot all the time; a twitch on every bullet would jitter).
		if ( sinceHit < 0.05f && flinchAt > 1.1f ) flinchAt = 0f;
		var fh = (float)flinchAt;
		var flinch = fh < 0.8f ? MathF.Exp( -fh * 6f ) * (1f - MathF.Exp( -fh * 40f )) * 1.6f : 0f;

		// Turning, for the bank and the tail's swing; the spin of a whirlpool exit or entry, for the hair.
		var yaw = GlobalRotation.Y;
		if ( !yawSeeded ) { lastYaw = yaw; yawSeeded = true; }
		var turned = Mathf.Wrap( yaw - lastYaw, -MathF.PI, MathF.PI );
		lastYaw = yaw;
		var rate = turned / MathF.Max( dt, 1e-4f );
		yawRate = MathX.Lerp( yawRate, MathX.Clamp( rate, -3f, 3f ), 1f - MathF.Exp( -6f * dt ) );
		spin = MathX.Lerp( spin, mode is Mode.Vanish or Mode.Appear ? MathX.Clamp( MathF.Abs( rate ) / 9f, 0f, 1f ) : 0f, 1f - MathF.Exp( -5f * dt ) );

		// The beat: faster with speed, a hard kick on the throw and the stab, slow and big while he summons.
		var thrust = mode == Mode.Stab ? MathX.Clamp( (u - carryTime) / 0.25f, 0f, 1f ) : 0f;
		var kick = mode switch { Mode.Throw => 2.6f, Mode.WindUp => 0.5f, Mode.Summon => 0.6f, Mode.Stab => thrust > 0f ? 2.6f : 0.8f, _ => 1f };
		swimPhase += dt * (1.7f + 2.2f * moving) * kick;

		// The body: lean (forward is negative), shoulder twist (positive brings the right, trident shoulder forward),
		// rise (metres up), how angry he looks and how far his jaw drops.
		float wantLean, wantTwist = 0f, wantRise = 0f, wantAnger = 0.3f, wantJaw = 0f, look = 0f;
		switch ( mode )
		{
			case Mode.WindUp:
				var coil = Ease( u / 0.5f );
				wantLean = 0.22f * coil; wantTwist = -0.6f * coil; wantAnger = 1f; wantJaw = 0.35f * coil;
				break;
			case Mode.Throw:
				wantLean = -0.4f; wantTwist = 0.5f; wantAnger = 1f; wantJaw = u < 0.3f ? 1f : 0.3f;
				break;
			case Mode.Summon:
				var up = Ease( u / 0.6f );
				wantLean = 0.28f * up; wantRise = 0.7f * up; wantAnger = 1f; wantJaw = up; look = 0.4f * up;
				break;
			case Mode.Stab:
				var carry = Ease( u / MathF.Max( 0.1f, carryTime ) );
				wantLean = MathX.Lerp( 0.15f * carry, -0.45f, thrust ); wantTwist = MathX.Lerp( -0.4f * carry, 0.45f, thrust );
				wantAnger = 1f; wantJaw = MathX.Lerp( 0.3f, 1f, thrust );
				break;
			case Mode.Vanish:
				wantLean = 0.1f; wantRise = -0.4f * Ease( u / 0.8f ); wantAnger = 0.6f;
				break;
			case Mode.Appear:
				wantLean = 0.15f; wantRise = 0.4f * (1f - Ease( u / 0.85f )); wantJaw = u > 0.4f ? 0.8f : 0f; wantAnger = 1f; look = 0.2f;
				break;
			default:
				wantLean = -0.3f * moving;
				break;
		}
		wantLean += flinch * 0.22f;
		wantJaw = MathF.Max( wantJaw, flinch * 0.5f );
		var heavy = mode is Mode.Throw || thrust > 0f ? 2.6f : 1.2f;
		Spring( ref lean, ref leanV, wantLean, heavy, 0.55f, dt );
		Spring( ref twist, ref twistV, wantTwist, heavy, 0.5f, dt );
		Spring( ref rise, ref riseV, wantRise, 1.1f, 0.6f, dt );
		Spring( ref bank, ref bankV, MathX.Clamp( yawRate * 0.18f, -0.3f, 0.3f ), 0.9f, 0.6f, dt );
		anger = MathX.Lerp( anger, wantAnger, 1f - MathF.Exp( -5f * dt ) );
		jawOpen = MathX.Lerp( jawOpen, wantJaw, 1f - MathF.Exp( -10f * dt ) );

		var bob = MathF.Sin( swimPhase ) * 0.05f * (0.5f + moving);
		var breath = MathF.Sin( t * 0.9f );
		body.Rotation = new Vector3( lean + bob, 0f, bank + MathF.Sin( t * 0.9f ) * 0.035f * (1f - moving) );
		body.Position = new Vector3( 0f, MathF.Sin( swimPhase + 1f ) * 0.12f + rise, flinch * 0.25f );
		var chestPitch = -MathF.Sin( swimPhase - 0.7f ) * 0.04f * (0.5f + moving) + breath * 0.015f;
		chest.Rotation = new Vector3( chestPitch, twist, -bank * 0.3f );

		// The head: eyes on the player against the lean and twist; thrown back to summon; a jolt when hit.
		var wantYaw = -twist * 0.9f;
		var wantPitch = -(lean + bob + chestPitch) + look;
		if ( Target is { } prey && IsInstanceValid( head ) )
		{
			var d = GlobalBasis.Orthonormalized().Inverse() * (prey.WorldPosition + Vector3.Up * 1.5f - head.GlobalPosition);
			wantYaw += MathX.Clamp( MathF.Atan2( -d.X, -d.Z ), -0.6f, 0.6f );
			if ( look <= 0f ) wantPitch += MathX.Clamp( MathF.Atan2( d.Y, new Vector2( d.X, d.Z ).Length() ), -0.45f, 0.3f );
		}
		wantPitch += flinch * 0.35f;
		Spring( ref headYaw, ref headYawV, wantYaw, 1.6f, 0.75f, dt );
		Spring( ref headPitch, ref headPitchV, wantPitch, 1.6f, 0.75f, dt );
		head.Rotation = new Vector3( headPitch, headYaw, MathF.Sin( t * 0.45f ) * 0.05f * (1f - moving) - bank * 0.3f );

		// Face: brows knit down toward the nose with his anger; the jaw drops when he roars.
		if ( rig is not null )
		{
			for ( var s = 0; s < 2; s++ )
			{
				var side = s == 0 ? -1f : 1f;
				if ( rig.Brows[s] is { } brow ) brow.Rotation = new Vector3( 0f, 0f, side * (0.15f + 0.3f * anger) );
			}
			if ( rig.Jaw is { } jaw ) jaw.Rotation = new Vector3( -0.32f * jawOpen + MathF.Sin( t * 30f ) * 0.02f * jawOpen, 0f, 0f );
		}

		// The tail: a wave running down it, bigger toward the fluke; swung out on turns; loaded to throw; curled up
		// behind him when it would touch the floor.
		if ( tail.Count > 0 )
		{
			// From the room the tail would have hanging straight (measuring the curled tip fed back on itself).
			var ground = MathF.Max( floorY, Sculpt.GroundAt( GlobalPosition ) );
			var room = GlobalPosition.Y - 4.2f * Size * MathF.Max( 0.02f, shown ) - ground;
			var want = MathX.Clamp( (0.5f - room) / 1.5f, 0f, 1f );
			curl = MathX.Lerp( curl, want, 1f - MathF.Exp( -4f * dt ) );
		}
		Spring( ref tailSide, ref tailSideV, MathX.Clamp( yawRate * 0.07f, -0.1f, 0.1f ), 1f, 0.45f, dt );
		var amp = (0.07f + 0.05f * moving) * (kick > 2f ? 1.5f : mode == Mode.Summon ? 1.3f : 1f);
		var load = mode == Mode.WindUp ? -0.07f * Ease( u / 0.5f ) : 0f;
		for ( var i = 0; i < tail.Count; i++ )
		{
			var f = i / (float)Math.Max( 1, tail.Count - 1 );
			var wave = MathF.Sin( swimPhase - i * 0.6f ) * amp * (0.7f + 0.6f * f);
			tail[i].Rotation = new Vector3(
				wave * (1f - curl * 0.6f) + 0.05f - curl * 0.21f + load - flinch * 0.04f,
				0f,
				tailSide * (0.3f + f) + MathF.Sin( t * 0.9f - i * 0.4f ) * 0.03f );
		}

		Locks( t, moving, spin, jawOpen, dt );
		rig?.Pose();

		// Arms (Euler YXZ: Z swings an arm out and up from his side, X forward; elbows bend forward with +X): the trident
		// stood upright in his fist like a staff; drawn back over the shoulder to throw and whipped forward to let go,
		// the left fist aiming; both arms raised to call up the sea; thrust out in front to stab; flung out to spin.
		var raiseR = RestArmR + new Vector3( MathF.Sin( swimPhase - 1f ) * 0.04f, 0f, breath * 0.02f );
		var foreR0 = RestForeR;
		var raiseL = new Vector3( 0.2f + MathF.Sin( t * 0.7f ) * 0.08f, 0f, -0.5f - breath * 0.02f );     // left fist out
		var foreL0 = new Vector3( 0.9f + MathF.Sin( t * 0.7f + 1f ) * 0.1f, 0f, 0f );
		var w = Ease( u / 0.5f );
		var point = Vector3.Up + -GlobalBasis.Z * 0.08f;   // the trident: upright, a touch forward
		var target = Target;
		var aim = target is not null ? (target.WorldPosition + Vector3.Up - HandAt).SafeNormal() : -GlobalBasis.Z;
		var snap = 1f - MathF.Exp( -6f * dt );   // heavy arms, unless he is striking
		switch ( mode )
		{
			case Mode.WindUp:
				raiseR = raiseR.Lerp( new Vector3( 0.85f, 0f, 2.75f ), w );   // up and back over the shoulder
				foreR0 = foreR0.Lerp( new Vector3( 1.2f, 0f, 0f ), w );
				raiseL = raiseL.Lerp( new Vector3( 1.35f, 0f, -0.2f ), w );   // the left fist points at the target
				foreL0 = foreL0.Lerp( new Vector3( 0.1f, 0f, 0f ), w );
				point = point.Lerp( (aim + Vector3.Up * 0.35f).SafeNormal(), w );   // levelled at the target like a javelin
				break;
			case Mode.Throw:
				var swing = Ease( u / 0.15f );
				raiseR = new Vector3( 0.85f, 0f, 2.75f ).Lerp( new Vector3( -0.9f, 0f, 2.1f ), swing );   // whipped forward
				foreR0 = new Vector3( 1.2f, 0f, 0f ).Lerp( Vector3.Zero, swing );
				raiseL = new Vector3( 1.35f, 0f, -0.2f ).Lerp( new Vector3( 0.1f, 0f, -0.8f ), swing );   // and the left arm sweeps back
				foreL0 = new Vector3( 0.1f, 0f, 0f ).Lerp( new Vector3( 0.6f, 0f, 0f ), swing );
				point = aim;
				snap = 1f - MathF.Exp( -22f * dt );
				break;
			case Mode.Summon:
				var shake = MathF.Sin( t * 13f ) * 0.05f * w;
				raiseR = raiseR.Lerp( new Vector3( 0.1f, 0f, 2.9f + shake ), w );
				raiseL = raiseL.Lerp( new Vector3( 0.1f, 0f, -2.9f - shake ), w );
				foreR0 = foreR0.Lerp( new Vector3( 0.3f, 0f, 0f ), w );
				foreL0 = foreL0.Lerp( new Vector3( 0.3f, 0f, 0f ), w );
				break;
			case Mode.Stab:
				raiseR = raiseR.Lerp( new Vector3( -0.4f, 0f, 0.6f ), Ease( u / MathF.Max( 0.1f, carryTime ) ) * (1f - thrust) );   // drawn back
				raiseR = raiseR.Lerp( new Vector3( 1.45f, 0f, 0.15f ), thrust );   // straight out in front
				foreR0 = foreR0.Lerp( Vector3.Zero, thrust );
				raiseL = raiseL.Lerp( new Vector3( -0.3f, 0f, -0.7f ), thrust );
				point = point.Lerp( (aim + Vector3.Down * 0.2f).SafeNormal(), MathX.Clamp( u / carryTime, 0f, 1f ) );   // points at the victim
				if ( thrust > 0f ) snap = 1f - MathF.Exp( -20f * dt );
				break;
			case Mode.Vanish:
			case Mode.Appear:
				raiseR = raiseR.Lerp( new Vector3( 0.3f, 0f, 1.5f ), spin );
				raiseL = raiseL.Lerp( new Vector3( 0.3f, 0f, -1.5f ), spin );
				foreL0 = foreL0.Lerp( new Vector3( 0.3f, 0f, 0f ), spin );
				break;
		}
		HoldTrident( point, dt );
		armR.Rotation = armR.Rotation.Lerp( raiseR, snap );
		foreR.Rotation = foreR.Rotation.Lerp( foreR0, snap );
		armL.Rotation = armL.Rotation.Lerp( raiseL, snap );
		foreL.Rotation = foreL.Rotation.Lerp( foreL0, snap );
	}

	/// <summary>
	/// Hair down his back and the beard: a slow wave lagging down each lock, streaming back with speed, swinging out on
	/// turns, flung out in a spin (`fling`), shaking as he roars (`roar`).
	/// </summary>
	void Locks( float t, float moving, float fling, float roar, float dt )
	{
		if ( rig is null ) return;
		var stream = -0.35f * moving;
		for ( var i = 0; i < rig.Hair.Count; i++ )
		{
			var joints = rig.Hair[i];
			var side = (i - (rig.Hair.Count - 1) * 0.5f) / Math.Max( 1f, rig.Hair.Count - 1 );
			var p = t * 1.5f + i * 0.9f;
			for ( var j = 0; j < joints.Count; j++ )
			{
				if ( !IsInstanceValid( joints[j] ) ) continue;
				var x = (j == 0 ? -0.35f - fling * 0.7f : 0f) + stream * (0.6f - j * 0.15f) + MathF.Sin( p - j * 0.9f ) * (0.06f + j * 0.04f);
				var z = side * (0.12f + fling * 1.1f) * (j == 0 ? 1f : 0.3f) - bank * 0.3f + MathF.Sin( t * 1.2f + i * 0.7f - j * 0.8f ) * 0.05f;
				joints[j].Rotation = new Vector3( x, 0f, z );
			}
		}
		for ( var i = 0; i < rig.Beard.Count; i++ )
		{
			var joints = rig.Beard[i];
			var side = (i - (rig.Beard.Count - 1) * 0.5f) / Math.Max( 1f, rig.Beard.Count - 1 );
			var p = t * 1.7f + i * 1.1f;
			for ( var j = 0; j < joints.Count; j++ )
			{
				if ( !IsInstanceValid( joints[j] ) ) continue;
				var x = (j == 0 ? 0.08f + fling * 0.5f : 0f) + stream * 0.5f + MathF.Sin( p - j * 0.9f ) * (0.04f + j * 0.03f) + MathF.Sin( t * 25f + i ) * 0.03f * roar;
				var z = side * (0.1f + fling * 0.8f) * (j == 0 ? 1f : 0.3f) + MathF.Sin( t * 1.3f + i - j * 0.7f ) * 0.04f;
				joints[j].Rotation = new Vector3( x, 0f, z );
			}
		}
	}

	/// <summary>
	/// The trident in his fist, gripped at the middle of the shaft, swinging smoothly to where it should point, and
	/// never through his own arm: when it would lie along the forearm it is leant away (the lower end outward).
	/// </summary>
	void HoldTrident( Vector3 point, float dt )
	{
		tridentPoint = tridentPoint.Slerp( point.SafeNormal(), 1f - MathF.Exp( -12f * dt ) ).SafeNormal();
		if ( tridentPoint == Vector3.Zero ) tridentPoint = Vector3.Up;
		var forearm = (HandAt - foreR.GlobalPosition).SafeNormal();
		var held = tridentPoint;
		var along = held.Dot( forearm );
		var side = held - forearm * along;
		var outward = GlobalBasis.X.Normalized() * (along >= 0f ? -1f : 1f) - GlobalBasis.Z.Normalized() * 0.4f;
		outward = (outward - forearm * outward.Dot( forearm )).SafeNormal();
		if ( side.Length() < 0.55f && outward != Vector3.Zero )
			held = (forearm * along + side + outward * (0.55f - side.Length())).SafeNormal();
		var palm = -GlobalBasis.Z.Normalized();
		palm = (palm - forearm * palm.Dot( forearm )).SafeNormal();
		if ( palm == Vector3.Zero ) palm = outward;
		var grip = HandAt + palm * 0.14f * Size * MathF.Max( 0.02f, shown );
		heldTrident.GlobalTransform = new Transform3D( Shapes.LookAt( held, GlobalBasis.Z ).Scaled( Vector3.One * 3.2f * Size * MathF.Max( 0.02f, shown ) ), grip );
	}
}
