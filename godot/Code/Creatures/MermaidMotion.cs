using Godot;
using static SmoothKit;

/// <summary>
/// How the mermaids move. Everything that settles does it on springs (<see cref="Spring"/>), so poses ease and overshoot
/// a little instead of snapping. Swimming is a dolphin kick: a wave runs down the tail, faster with speed, and the body
/// pitches against it. Turning banks the body into the turn and swings the tail out. The head keeps its eyes on the
/// player against the body's lean and twist. Attacks have a coil and a follow-through: a trident wind-up rears back,
/// twists the shoulders and loads the tail, the stab lunges with a hard kick; an archer turns side-on to draw and rocks
/// back on the release. A hit makes it flinch. Hair flows in a lagging wave and streams back with speed; fins ripple
/// (in their shader); gills flare as it breathes.
/// </summary>
public partial class Mermaid
{
	Vector3? lookAt;
	float swimPhase, finBeat;
	float leanV, bank, bankV, twist, twistV, tailSide, tailSideV;
	float headYaw, headYawV, headPitch, headPitchV;
	float lastYaw, yawRate;
	bool yawSeeded;

	void Animate( float t, float dt )
	{
		var flash = MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f );
		skin.SetShaderParameter( "flash", flash );
		handSkin.SetShaderParameter( "flash", flash );
		tailMaterial?.SetShaderParameter( "flash", flash );

		var moving = MathX.Clamp( velocity.Length() / Speed, 0f, 1f );
		var u = (float)inMode;
		var h = (float)sinceHit;
		// A hit: a quick jolt that dies away.
		var flinch = h < 0.6f ? MathF.Exp( -h * 9f ) * (1f - MathF.Exp( -h * 60f )) * 2.2f : 0f;

		// How fast it is turning, for the bank and the tail's swing.
		var yaw = GlobalRotation.Y;
		if ( !yawSeeded ) { lastYaw = yaw; yawSeeded = true; }
		var turned = Mathf.Wrap( yaw - lastYaw, -MathF.PI, MathF.PI );
		lastYaw = yaw;
		yawRate = MathX.Lerp( yawRate, turned / MathF.Max( dt, 1e-4f ), 1f - MathF.Exp( -8f * dt ) );

		// The beat: the tail kicks faster with speed, very hard in a stab, slow while coiling.
		var kick = mode switch { Mode.Stab => 3f, Mode.WindUp => 0.5f, _ => 1f };
		swimPhase += dt * (2.2f + 3.4f * moving) * kick;
		finBeat += dt * (2.4f + 4f * moving + (mode == Mode.Stab ? 4f : 0f));
		finPaint?.SetShaderParameter( "beat", finBeat );
		crestPaint?.SetShaderParameter( "beat", finBeat );

		// The body: lean (forward is negative), shoulder twist (negative brings the left shoulder forward), bank.
		float wantLean, wantTwist = 0f;
		switch ( mode )
		{
			case Mode.WindUp: wantLean = MathX.Lerp( -0.2f, 0.25f, Ease( u / 0.3f ) ); wantTwist = -0.5f * Ease( u / 0.3f ); break;
			case Mode.Stab: wantLean = u < 0.18f ? -0.65f : -0.3f; wantTwist = 0.4f; break;
			case Mode.Draw: wantLean = -0.08f; wantTwist = -0.6f * Ease( u / 0.35f ); break;
			default: wantLean = -0.5f * moving; break;
		}
		// An archer rocks back on the release.
		if ( Archer && mode == Mode.Swim && sinceAttack < 0.5f ) { var r = 1f - (float)sinceAttack / 0.5f; wantLean += 0.22f * r; wantTwist = -0.6f * r * r; }
		wantLean += flinch * 0.4f;
		Spring( ref lean, ref leanV, wantLean, mode == Mode.Stab ? 4f : 1.8f, 0.55f, dt );
		Spring( ref twist, ref twistV, wantTwist, mode == Mode.Stab ? 3.5f : 2f, 0.5f, dt );
		Spring( ref bank, ref bankV, MathX.Clamp( yawRate * 0.16f, -0.4f, 0.4f ), 1.2f, 0.6f, dt );
		var bob = MathF.Sin( swimPhase ) * 0.06f * (0.4f + moving);
		body.Rotation = new Vector3( lean + bob, 0f, bank + MathF.Sin( t * 1.3f ) * 0.04f * (1f - moving) );
		body.Position = new Vector3( 0f, MathF.Sin( swimPhase + 1f ) * 0.05f + MathF.Sin( t * 1.9f ) * 0.025f, flinch * 0.12f );
		var chestPitch = -MathF.Sin( swimPhase - 0.7f ) * 0.05f * (0.4f + moving);
		chest.Rotation = new Vector3( chestPitch, twist, -bank * 0.3f );

		// The head: eyes on the player, against the body's lean and twist; a curious tilt when idle.
		var wantYaw = -twist;
		var wantPitch = -(lean + bob + chestPitch);
		if ( lookAt is { } target && IsInstanceValid( head ) )
		{
			var d = GlobalBasis.Orthonormalized().Inverse() * (target - head.GlobalPosition);
			wantYaw += MathX.Clamp( MathF.Atan2( -d.X, -d.Z ), -0.7f, 0.7f );
			wantPitch += MathX.Clamp( MathF.Atan2( d.Y, new Vector2( d.X, d.Z ).Length() ), -0.5f, 0.5f );
		}
		wantPitch += flinch * 0.5f;
		Spring( ref headYaw, ref headYawV, wantYaw, 2.4f, 0.7f, dt );
		Spring( ref headPitch, ref headPitchV, wantPitch, 2.4f, 0.7f, dt );
		head.Rotation = new Vector3( headPitch, headYaw, MathF.Sin( t * 0.5f ) * 0.09f * (1f - moving) - bank * 0.4f );

		// The mouth gapes on its teeth to attack (widest in the stab) and snaps back to a line.
		var gape = mode switch { Mode.Stab => 1f, Mode.WindUp => 0.75f, Mode.Draw => 0.5f, _ => 0f } + flinch * 0.4f;
		mawOpen = MathX.Lerp( mawOpen, MathF.Min( gape, 1f ), 1f - MathF.Exp( -12f * dt ) );
		if ( maw is not null ) maw.Scale = new Vector3( 1f, MathX.Lerp( 0.08f, 1f, mawOpen ), 1f );

		// Gills flare as it breathes.
		var breath = MathF.Max( 0f, MathF.Sin( t * 1.6f ) );
		for ( var i = 0; i < gills.Count; i++ )
			gills[i].Scale = new Vector3( 1f + breath * 0.8f, 1f + breath * 0.4f, 1f );

		// The tail: a wave running down it (bigger toward the fluke, harder in a stab), swung out on turns, coiled back to
		// strike, curled up behind when it would touch the floor.
		if ( tail.Count > 0 )
		{
			// From how much room the tail would have hanging straight (not from where the curled tip is now: that fed
			// back on itself, the tail flicking between curled and straight).
			var ground = MathF.Max( floorY, Sculpt.GroundAt( GlobalPosition ) );
			var room = GlobalPosition.Y - 4.2f * Scale * MathF.Cos( lean ) - ground;
			var want = MathX.Clamp( (0.3f - room) / 0.45f, 0f, 1f );
			curl = MathX.Lerp( curl, want, 1f - MathF.Exp( -4f * dt ) );
		}
		Spring( ref tailSide, ref tailSideV, MathX.Clamp( yawRate * 0.07f, -0.12f, 0.12f ), 1.4f, 0.45f, dt );
		var amp = (0.07f + 0.06f * moving) * (mode == Mode.Stab ? 1.6f : 1f);
		var coil = mode == Mode.WindUp ? -0.09f * Ease( u / 0.35f ) : 0f;
		for ( var i = 0; i < tail.Count; i++ )
		{
			var f = i / (float)Math.Max( 1, tail.Count - 1 );
			var wave = MathF.Sin( swimPhase - i * 0.62f ) * amp * (0.7f + 0.6f * f);
			tail[i].Rotation = new Vector3(
				wave * (1f - curl * 0.6f) + 0.05f - curl * 0.21f + coil - flinch * 0.06f,
				0f,
				tailSide * (0.3f + f) + MathF.Sin( t * 1.1f - i * 0.45f ) * 0.035f );
		}

		// Hair: a slow wave that lags down each lock, streaming back with speed and swinging out on turns.
		var stream = -0.4f * moving;
		for ( var i = 0; i < hair.Count; i++ )
		{
			var p = t * 1.9f + i * 0.9f;
			var side = MathF.Sin( t * 1.4f + i * 0.7f ) * 0.06f - bank * 0.3f;
			hair[i].Rotation = new Vector3( -0.28f + stream * 0.6f + MathF.Sin( p ) * 0.07f, 0f, side + (i - 3f) * 0.03f );
			if ( i < hairMid.Count ) hairMid[i].Rotation = new Vector3( stream * 0.4f + MathF.Sin( p - 0.9f ) * 0.12f, 0f, MathF.Sin( t * 1.4f + i * 0.7f - 0.8f ) * 0.08f );
			if ( i < hairTip.Count ) hairTip[i].Rotation = new Vector3( stream * 0.3f + MathF.Sin( p - 1.8f ) * 0.16f, 0f, MathF.Sin( t * 1.4f + i * 0.7f - 1.6f ) * 0.1f );
		}
		PoseChains();

		// Arms (Euler YXZ like Poseidon's: Z swings an arm out from the side, X forward; elbows bend forward with +X).
		var k = 1f - MathF.Exp( -12f * dt );
		Vector3 rArm, rFore, lArm, lFore, rHand = Vector3.Zero, lHand = Vector3.Zero;
		var sway = MathF.Sin( swimPhase - 1f ) * 0.06f;
		if ( Archer )
		{
			// The bow held out in the left hand; the right hand sculls the water, or draws the string back to the cheek.
			lArm = new Vector3( 1.45f + sway * 0.5f, 0f, -0.15f );
			lFore = new Vector3( 0.05f, 0f, 0f );
			var draw = mode == Mode.Draw ? Ease( u / 0.6f ) : 0f;
			var scull = new Vector3( 0.45f + MathF.Sin( t * 2.2f ) * 0.15f, 0f, 0.4f + MathF.Cos( t * 2.2f ) * 0.08f );
			rArm = scull.Lerp( new Vector3( 1.4f, 0f, 0.2f ), draw );
			rFore = new Vector3( 0.35f + MathF.Sin( t * 2.2f - 0.6f ) * 0.2f, 0f, 0f ).Lerp( new Vector3( 2.2f, 0f, 0f ), draw );
			rHand = new Vector3( MathF.Sin( t * 2.2f - 1.2f ) * 0.25f * (1f - draw), 0f, 0f );
			// The release: the drawing hand flicks back and open.
			if ( mode == Mode.Swim && sinceAttack < 0.35f ) { var r = 1f - (float)sinceAttack / 0.35f; rArm += new Vector3( 0.3f, 0f, 0.4f ) * r; rFore -= new Vector3( 1.2f, 0f, 0f ) * r; }
			weapon.Rotation = new Vector3( -1.45f, 0f, 0f );   // the bow upright, its back to the target
		}
		else
		{
			// The shield arm out in front, forearm forward; the trident arm cocked, swaying with the kick.
			lArm = new Vector3( 0.6f + sway * 0.5f, 0f, -0.12f + MathF.Sin( t * 2f ) * 0.04f );
			lFore = new Vector3( 1.0f, 0f, 0f );
			rArm = new Vector3( 0.1f + sway, 0f, 0.5f );
			rFore = new Vector3( 1.3f, 0f, 0f );
			if ( mode == Mode.WindUp ) { var w = Ease( u / 0.35f ); rArm = rArm.Lerp( new Vector3( -0.7f, 0f, 0.5f ), w ); rFore = rFore.Lerp( new Vector3( 1.9f, 0f, 0f ), w ); lArm = lArm.Lerp( new Vector3( 0.9f, 0f, -0.05f ), w ); }
			if ( mode == Mode.Stab )
			{
				// A fast thrust that overshoots a little, then eases back.
				var x = MathX.Clamp( u / 0.12f, 0f, 1f );
				var thrust = 1f - MathF.Pow( 1f - x, 3f ) + MathF.Sin( MathX.Clamp( (u - 0.12f) / 0.25f, 0f, 1f ) * MathF.PI ) * 0.12f;
				rArm = new Vector3( -0.7f, 0f, 0.5f ).Lerp( new Vector3( 1.5f, 0f, 0.1f ), thrust );
				rFore = new Vector3( 1.9f, 0f, 0f ).Lerp( Vector3.Zero, thrust );
				k = 1f - MathF.Exp( -30f * dt );
			}
		}
		armR.Rotation = armR.Rotation.Lerp( rArm, k );
		foreR.Rotation = foreR.Rotation.Lerp( rFore, k );
		armL.Rotation = armL.Rotation.Lerp( lArm, k );
		foreL.Rotation = foreL.Rotation.Lerp( lFore, k );
		handR.Rotation = handR.Rotation.Lerp( rHand, k );
		handL.Rotation = handL.Rotation.Lerp( lHand, k );

		if ( shield is not null && IsInstanceValid( shield ) && shield.GetParent() == this )
		{
			// In the left fist, upright, facing the way the chest faces.
			var chestBasis = chest.GlobalBasis;
			shield.GlobalTransform = new Transform3D( chestBasis, handL.GlobalPosition - chestBasis.Z.Normalized() * 0.04f );
			foreach ( var cell in cells )
			{
				if ( cell.SinceHit > 0.35f ) { if ( cell.Lit ) { cell.Lit = false; cell.Material.SetShaderParameter( "flash", 0f ); } continue; }
				cell.Lit = true;
				cell.Material.SetShaderParameter( "flash", MathX.Clamp( 1f - (float)cell.SinceHit / 0.3f, 0f, 1f ) );
			}
		}

		if ( !Archer && IsInstanceValid( weapon ) && weapon.GetParent() == this )
		{
			// The trident in the right fist: upright at rest, levelled at the target to stab. Kept off the forearm.
			var forward = -GlobalBasis.Z.Normalized();
			var point = mode is Mode.Stab or Mode.WindUp ? (forward + Vector3.Down * 0.15f).Normalized() : (Vector3.Up + forward * 0.15f).Normalized();
			var forearm = (handR.GlobalPosition - foreR.GlobalPosition).SafeNormal();
			var side = point - forearm * point.Dot( forearm );
			if ( side.Length() < 0.5f ) point = (point + GlobalBasis.X.Normalized() * 0.5f).Normalized();
			weapon.GlobalTransform = new Transform3D( Shapes.LookAt( point, GlobalBasis.Z ).Scaled( Vector3.One * 0.95f ), handR.GlobalPosition + forward * 0.05f );
		}
	}
}
