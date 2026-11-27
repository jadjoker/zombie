using Godot;
using static SmoothKit;

/// <summary>
/// How the Mermaid Queen moves, on springs like the mermaids: a slow regal dolphin kick, the body leaning into her swim
/// and banking into turns, her head on the player (or on her gaze), her tentacle hair trailing and lifting as she sings,
/// the ruff rippling. Every attack has its own pose: arms spread to sing, the sceptre raised for the pearls and swung
/// with each throw, a coil and a lash of the tail for the riptide, both arms up to call the guard, a lean over the
/// beam, hands clutching her throat when she chokes, and an arch back with arms flung wide when she ascends. The four
/// petals of her mouth split open to sing and bite (and stay parted once she has ascended); her crown grows and blazes.
/// </summary>
public partial class MermaidQueen
{
	float swimPhase, finBeat;
	float lean, leanV, twist, twistV, bank, bankV, tailSide, tailSideV;
	float headYaw, headYawV, headPitch, headPitchV;
	float mawOpen, mawV, charge, chargeV, rise, riseV, spireGrow = 1f, curl;
	float lastYaw, yawRate;
	bool yawSeeded;

	void Animate( float t, float dt )
	{
		var a = (float)sinceAct;
		body.Scale = Vector3.One * Scale * MathF.Max( 0.02f, shown );
		var flash = MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f );
		skin.SetShaderParameter( "flash", flash );
		faceSkin.SetShaderParameter( "flash", flash );
		tailPaint.SetShaderParameter( "flash", flash );

		// Charging an attack: the veins and the tail's lights run bright.
		var windUp = act switch
		{
			Act.Song => a < SongWindUp ? 1f : 0.6f,
			Act.Pearls => a < 1.2f ? 1f : 0.4f,
			Act.Riptide => a < 0.9f ? 1f : 0f,
			Act.Guard => a < 1.4f ? 1f : 0f,
			Act.Gaze => a < GazeCharge ? 1f : 0.7f,
			Act.Ascend => 1.2f,
			_ => 0f,
		};
		Spring( ref charge, ref chargeV, windUp + (Ascended ? 0.25f : 0f), 2.2f, 0.8f, dt );
		var c = MathX.Clamp( charge, 0f, 1.4f );
		skin.SetShaderParameter( "charge", c );
		faceSkin.SetShaderParameter( "charge", c );
		tailPaint.SetShaderParameter( "charge", c );
		skin.SetShaderParameter( "glow", Ascended ? 0.9f : 0.5f );

		var moving = MathX.Clamp( velocity.Flat().Length() / SwimSpeed, 0f, 1.6f );
		var yaw = GlobalRotation.Y;
		if ( !yawSeeded ) { lastYaw = yaw; yawSeeded = true; }
		var turned = Mathf.Wrap( yaw - lastYaw, -MathF.PI, MathF.PI );
		lastYaw = yaw;
		yawRate = MathX.Lerp( yawRate, turned / MathF.Max( dt, 1e-4f ), 1f - MathF.Exp( -8f * dt ) );
		var flinch = (float)sinceHit < 0.5f ? MathF.Exp( -(float)sinceHit * 9f ) * (1f - MathF.Exp( -(float)sinceHit * 60f )) * 0.9f : 0f;

		var kick = act switch { Act.Dash => 3.2f, Act.Riptide => a > 0.9f && a < 1.4f ? 3f : 0.6f, Act.Song or Act.Gaze => 0.6f, Act.Choke => 0.3f, _ => 1f };
		swimPhase += dt * (1.6f + 2.4f * MathF.Min( moving, 1f )) * kick;
		finBeat += dt * (2f + 3f * MathF.Min( moving, 1f ) + (act == Act.Dash ? 5f : 0f));
		finPaint.SetShaderParameter( "beat", finBeat );
		veilPaint.SetShaderParameter( "beat", finBeat * 0.8f );
		ruffPaint.SetShaderParameter( "beat", finBeat * 1.3f );

		// ---- The body ----
		float wantLean = -0.35f * MathF.Min( moving, 1f ), wantTwist = 0f, wantRise = 0f;
		switch ( act )
		{
			case Act.Song: wantLean = a < SongWindUp ? 0.28f * Ease( a / 0.5f ) : 0.14f + MathF.Sin( t * 1.7f ) * 0.05f; break;
			case Act.Bite: wantLean = -0.75f; break;
			case Act.Pearls: wantLean = a < 1.2f ? 0.18f : -0.1f; wantTwist = a < 1.2f ? -0.35f * Ease( a / 0.6f ) : (float)sinceThrow < 0.15f ? 0.3f : 0f; break;
			case Act.Riptide: wantLean = a < 0.9f ? 0.32f * Ease( a / 0.6f ) : -0.45f; wantTwist = a < 0.9f ? 0.4f : -0.5f; break;
			case Act.Guard: wantLean = 0.2f; wantRise = 0.5f * Ease( a / 0.8f ); break;
			case Act.Gaze: wantLean = a < GazeCharge ? 0.12f : -0.2f; break;
			case Act.Choke: wantLean = 0.3f + MathF.Sin( t * 13f ) * 0.08f; wantRise = -0.6f; break;
			case Act.Ascend: wantLean = 0.55f * Ease( a / 0.8f ) - (a > 1.2f ? 0.3f * Ease( (a - 1.2f) / 0.4f ) : 0f); wantRise = 1f; break;
			case Act.Dash: wantLean = -1f; break;
			case Act.Dead: wantLean = 0.5f; break;
		}
		wantLean += flinch * 0.35f;
		Spring( ref lean, ref leanV, wantLean, act is Act.Bite or Act.Riptide ? 3.5f : 1.6f, 0.55f, dt );
		Spring( ref twist, ref twistV, wantTwist, 2f, 0.5f, dt );
		Spring( ref bank, ref bankV, MathX.Clamp( yawRate * 0.18f, -0.45f, 0.45f ), 1.1f, 0.6f, dt );
		Spring( ref rise, ref riseV, wantRise, 1.5f, 0.7f, dt );
		var bob = MathF.Sin( swimPhase ) * 0.05f * (0.4f + MathF.Min( moving, 1f ));
		var shake = act is Act.Ascend or Act.Choke ? Rng.InSphere( 0.03f ) : Vector3.Zero;
		body.Rotation = new Vector3( lean + bob, 0f, bank + MathF.Sin( t * 1.1f ) * 0.04f ) + shake;
		if ( act != Act.Dead ) body.Position = new Vector3( 0f, MathF.Sin( swimPhase + 1f ) * 0.06f + rise, flinch * 0.15f );
		var chestPitch = -MathF.Sin( swimPhase - 0.7f ) * 0.04f;
		chest.Rotation = new Vector3( chestPitch, twist, -bank * 0.3f );

		// ---- The head: on the player, the beam's point or the bite; thrown back to scream ----
		var wantYaw = -twist;
		var wantPitch = -(lean + bob + chestPitch);
		Vector3? look = act switch
		{
			Act.Gaze => gazeAt,
			Act.Bite => Lap,
			Act.Choke or Act.Dead => null,
			_ => PlayerSurvivor.FindNearestAlive( GlobalPosition ) is { } p ? p.RealPosition + Vector3.Up * 1.4f : null,
		};
		if ( look is { } at && IsInstanceValid( head ) )
		{
			var d = GlobalBasis.Orthonormalized().Inverse() * (at - head.GlobalPosition);
			wantYaw += MathX.Clamp( MathF.Atan2( -d.X, -d.Z ), -0.8f, 0.8f );
			wantPitch += MathX.Clamp( MathF.Atan2( d.Y, new Vector2( d.X, d.Z ).Length() ), -0.75f, 0.6f );
		}
		if ( act == Act.Ascend ) wantPitch += 0.7f * Ease( a / 0.6f ) * (a < 1.6f ? 1f : 1f - Ease( (a - 1.6f) / 0.6f ));
		if ( act == Act.Song && a >= SongWindUp ) wantPitch += 0.12f;
		if ( act == Act.Choke ) { wantPitch += 0.45f; wantYaw += MathF.Sin( t * 17f ) * 0.25f; }
		wantPitch += flinch * 0.4f;
		Spring( ref headYaw, ref headYawV, wantYaw, 2.2f, 0.7f, dt );
		Spring( ref headPitch, ref headPitchV, wantPitch, 2.2f, 0.7f, dt );
		head.Rotation = new Vector3( headPitch, headYaw, MathF.Sin( t * 0.45f ) * 0.08f * (1f - MathF.Min( moving, 1f )) - bank * 0.35f );

		// ---- The mouth: four petals split open on the throat ----
		var gape = act switch
		{
			Act.Song => a < SongWindUp ? 0.6f * Ease( a / SongWindUp ) : 1f,
			Act.Bite => a < 0.3f ? 1.15f : 0.4f,
			Act.Ascend => 1.2f,
			Act.Choke => 0.55f + 0.35f * MathF.Sin( t * 11f ),
			Act.Gaze => a >= GazeCharge ? 0.45f : 0.2f,
			Act.Dead => 1f,
			_ => 0.04f,
		};
		if ( Ascended ) gape = MathF.Max( gape, 0.32f );
		Spring( ref mawOpen, ref mawV, gape, 3f, 0.65f, dt );
		var openAngle = MathX.Clamp( mawOpen, 0f, 1.3f ) * 1.2f;
		for ( var i = 0; i < petals.Count; i++ ) petals[i].Hinge.Basis = new Basis( petals[i].Axis, openAngle + MathF.Sin( t * 3f + i ) * 0.03f * mawOpen );
		throatPaint.SetShaderParameter( "open", MathX.Clamp( mawOpen, 0f, 1f ) );
		if ( throat is not null && IsInstanceValid( throat ) ) throat.Collider.Disabled = !(ThroatOpen && mawOpen > 0.35f);

		// ---- Eyes, heart, crown, lights ----
		var gazing = act == Act.Gaze;
		eyeRim.EmissionEnergyMultiplier = (Ascended ? 2.4f : 1.6f) + c * 1.2f + (gazing ? (a < GazeCharge ? 5f * Ease( a / GazeCharge ) : 6f + MathF.Sin( t * 30f )) : 0f);
		heartFlash = MathF.Max( 0f, heartFlash - dt * 4f );
		heartGlow.EmissionEnergyMultiplier = 1.4f + heartFlash * 4f + (act == Act.Song ? 1.2f + MathF.Sin( t * 6f ) * 0.6f : 0f);
		spireGrow = MathX.Lerp( spireGrow, Ascended ? 1.5f : 1f, 1f - MathF.Exp( -2.5f * dt ) );
		for ( var i = 0; i < spires.Count; i++ ) spires[i].Scale = new Vector3( 1f, spireGrow * (1f + MathF.Sin( t * 2.4f + i ) * 0.03f * (spireGrow - 1f) * 2f), 1f );
		crystal.EmissionEnergyMultiplier = (Ascended ? 1.6f : 0.9f) + c * 0.6f + MathF.Sin( t * 3f ) * 0.2f;
		tipGlow.EmissionEnergyMultiplier = (Ascended ? 3f : 1.8f) + (act == Act.Song ? 1.5f + MathF.Sin( t * 8f ) : 0f);
		glowLight.LightEnergy = 1.3f + c * 1.2f + (Ascended ? 0.8f : 0f);
		glowLight.LightColor = gazing && a >= GazeCharge ? new Color( 0.45f, 1f, 0.5f ) : new Color( 0.7f, 0.5f, 1f );
		orbRings.RotateY( dt * (1.5f + (act == Act.Pearls ? 6f : 0f)) );
		orbRings.RotateX( dt * 0.7f );
		orbLight.LightEnergy = 1f + (act == Act.Pearls && a < 1.2f ? 2.5f : 0f) + MathF.Sin( t * 4f ) * 0.2f;

		// ---- The tail: a wave down it, swung out on turns, coiled and lashed for the riptide, curled up off the floor ----
		var ground = MathF.Max( floorY, Sculpt.GroundAt( GlobalPosition ) );
		var room = GlobalPosition.Y + body.Position.Y * Scale - (TailStep * TailJoints + 0.5f) * Scale * MathF.Cos( lean ) - ground;
		curl = MathX.Lerp( curl, MathX.Clamp( (0.4f - room) / 1.2f, 0f, 1f ), 1f - MathF.Exp( -4f * dt ) );
		Spring( ref tailSide, ref tailSideV, MathX.Clamp( yawRate * 0.08f, -0.14f, 0.14f ), 1.3f, 0.45f, dt );
		var amp = (0.06f + 0.05f * MathF.Min( moving, 1f )) * (act == Act.Dash ? 1.8f : 1f);
		var coil = 0f;
		if ( act == Act.Riptide ) coil = a < 0.9f ? -0.11f * Ease( a / 0.7f ) : 0.16f * MathF.Exp( -(a - 0.9f) * 5f ) * MathF.Cos( (a - 0.9f) * 18f );
		if ( act == Act.Choke ) amp *= 0.3f;
		for ( var i = 0; i < tail.Count; i++ )
		{
			var f = i / (float)Math.Max( 1, tail.Count - 1 );
			var wave = MathF.Sin( swimPhase - i * 0.55f ) * amp * (0.6f + 0.7f * f);
			tail[i].Rotation = new Vector3(
				wave * (1f - curl * 0.6f) + MathX.Lerp( -0.04f, 0.1f, f ) - curl * 0.17f + coil * (0.5f + f) - flinch * 0.05f,
				0f,
				tailSide * (0.3f + f) + MathF.Sin( t * 0.9f - i * 0.4f ) * 0.03f );
		}

		// ---- Tentacle hair: trailing and lagging, streaming back with speed; lifting and splaying when she sings or ascends ----
		var stream = -0.45f * MathF.Min( moving, 1f );
		var lift = act switch { Act.Song => a < SongWindUp ? Ease( a / SongWindUp ) : 1f, Act.Ascend => 1.3f, _ => Ascended ? 0.35f : 0f };
		for ( var i = 0; i < hair.Count; i++ )
		{
			var p = t * 1.5f + i * 0.8f;
			var spread = (i - 4f) * (0.06f + 0.12f * lift);
			hair[i].Rotation = new Vector3( -0.3f + stream * 0.6f - lift * 0.9f + MathF.Sin( p ) * 0.08f, 0f, spread + MathF.Sin( t * 1.2f + i * 0.7f ) * 0.07f - bank * 0.3f );
			hairMid[i].Rotation = new Vector3( stream * 0.4f - lift * 0.3f + MathF.Sin( p - 0.9f ) * 0.16f, 0f, MathF.Sin( t * 1.2f + i * 0.7f - 0.8f ) * 0.1f );
			hairLow[i].Rotation = new Vector3( stream * 0.3f + MathF.Sin( p - 1.8f ) * 0.2f, 0f, MathF.Sin( t * 1.2f + i * 0.7f - 1.6f ) * 0.12f );
			hairTip[i].Rotation = new Vector3( MathF.Sin( p - 2.7f ) * 0.26f, 0f, MathF.Sin( t * 1.2f + i * 0.7f - 2.4f ) * 0.15f );
		}
		PoseChains();
		for ( var i = 0; i < ruff.Count; i++ ) ruff[i].Rotation = new Vector3( MathF.Sin( t * 2.6f + i * 0.9f ) * 0.12f, 0f, 0f );

		// ---- Arms (Euler like the mermaids': Z swings an arm out from the side, X forward; elbows bend forward with +X) ----
		var sway = MathF.Sin( swimPhase - 1f ) * 0.06f;
		Vector3 rArm = new( 0.35f + sway, 0f, 0.35f ), rFore = new( 1.0f, 0f, 0f );
		Vector3 lArm = new( 0.25f + MathF.Sin( t * 1.8f ) * 0.15f, 0f, -0.35f + MathF.Cos( t * 1.8f ) * 0.08f ), lFore = new( 0.5f + MathF.Sin( t * 1.8f - 0.6f ) * 0.2f, 0f, 0f );
		var k = 1f - MathF.Exp( -8f * dt );
		switch ( act )
		{
			case Act.Song:
			{
				var w = Ease( a / 0.6f );
				var wave = MathF.Sin( t * 1.6f ) * 0.12f;
				rArm = rArm.Lerp( new Vector3( 0.9f + wave, 0f, 1.1f ), w ); rFore = rFore.Lerp( new Vector3( 0.3f, 0f, 0f ), w );
				lArm = lArm.Lerp( new Vector3( 0.9f - wave, 0f, -1.1f ), w ); lFore = lFore.Lerp( new Vector3( 0.3f, 0f, 0f ), w );
				break;
			}
			case Act.Pearls:
				if ( a < 1.2f ) { var w = Ease( a / 0.5f ); rArm = rArm.Lerp( new Vector3( 2.8f, 0f, 0.3f ), w ); rFore = rFore.Lerp( new Vector3( 0.2f, 0f, 0f ), w ); lArm = lArm.Lerp( new Vector3( 1.2f, 0f, -0.6f ), w ); }
				else
				{
					var s = MathX.Clamp( (float)sinceThrow / 0.2f, 0f, 1f );
					rArm = new Vector3( 1.5f, 0f, 0.3f ).Lerp( new Vector3( 2.6f, 0f, 0.3f ), Ease( s ) ); rFore = new Vector3( 0.3f, 0f, 0f );
					lArm = new Vector3( 1.2f, 0f, -0.6f ); k = 1f - MathF.Exp( -18f * dt );
				}
				break;
			case Act.Riptide:
				rArm = new Vector3( -0.4f, 0f, 0.95f ); lArm = new Vector3( -0.4f, 0f, -0.95f ); rFore = lFore = new Vector3( 0.3f, 0f, 0f );
				break;
			case Act.Guard:
			{
				var w = Ease( a / 0.7f );
				rArm = rArm.Lerp( new Vector3( 2.6f, 0f, 0.9f ), w ); lArm = lArm.Lerp( new Vector3( 2.6f, 0f, -0.9f ), w );
				rFore = rFore.Lerp( new Vector3( 0.2f, 0f, 0f ), w ); lFore = lFore.Lerp( new Vector3( 0.2f, 0f, 0f ), w );
				break;
			}
			case Act.Gaze:
				rArm = new Vector3( 0.15f, 0f, 0.7f ); lArm = new Vector3( 0.5f, 0f, -1f ); rFore = new Vector3( 0.6f, 0f, 0f ); lFore = new Vector3( 0.4f, 0f, 0f );
				break;
			case Act.Choke:
				rArm = new Vector3( 1.4f, 0f, -0.2f ); lArm = new Vector3( 1.4f, 0f, 0.2f ); rFore = lFore = new Vector3( 2.1f + MathF.Sin( t * 14f ) * 0.1f, 0f, 0f );
				break;
			case Act.Ascend:
			case Act.Dead:
				rArm = new Vector3( 1.3f, 0f, 2.1f ); lArm = new Vector3( 1.3f, 0f, -2.1f ); rFore = lFore = new Vector3( 0.2f, 0f, 0f );
				break;
			case Act.Dash:
				rArm = new Vector3( -0.35f, 0f, 0.18f ); lArm = new Vector3( -0.35f, 0f, -0.18f ); rFore = lFore = new Vector3( 0.15f, 0f, 0f );
				break;
			case Act.Bite:
				rArm = new Vector3( 1.4f, 0f, 0.55f ); lArm = new Vector3( 1.4f, 0f, -0.55f ); rFore = lFore = new Vector3( 0.6f, 0f, 0f ); k = 1f - MathF.Exp( -16f * dt );
				break;
		}
		armR.Rotation = armR.Rotation.Lerp( rArm, k );
		foreR.Rotation = foreR.Rotation.Lerp( rFore, k );
		armL.Rotation = armL.Rotation.Lerp( lArm, k );
		foreL.Rotation = foreL.Rotation.Lerp( lFore, k );

		// The sceptre stands up out of her fist whatever the arm does (tipped forward a little when she throws).
		if ( sceptre is not null && IsInstanceValid( sceptre ) && sceptre.GetParent() == handR )
		{
			var forward = -GlobalBasis.Z.Normalized();
			var up = (Vector3.Up + forward * (act == Act.Pearls && a >= 1.2f ? 0.5f : 0.12f) + GlobalBasis.X.Normalized() * 0.08f).Normalized();
			var s = Scale * MathF.Max( 0.02f, shown );
			sceptre.GlobalTransform = new Transform3D( new Basis( new Quaternion( Vector3.Up, up ) ).Scaled( Vector3.One * s ), handR.GlobalPosition );
		}
	}
}
