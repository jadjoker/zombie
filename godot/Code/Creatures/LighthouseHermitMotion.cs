using Godot;
using static SmoothKit;

/// <summary>
/// The Lighthouse Hermit's motion: legs that plant and step on their own in two alternating sets, a pose for each attack
/// (both claws reared and slammed, the tower rocking; digging; thrown up out of the sand; hugging in and trembling as the
/// horn winds up; reared up out of the tower for the beam with his belly bared), the claws' fingers working, eye stalks and
/// antennae following the player, and the tower creaking over when he dies. Springs (<see cref="SmoothKit.Spring"/>) carry
/// it all so nothing snaps.
/// </summary>
public partial class LighthouseHermit
{
	float bigPitch, bigPitchV, smallPitch, smallPitchV, spread, spreadV, lean, leanV, lift, liftV, roll, rollV, bob, finger;
	float stepClock;

	void Animate( float t, float dt )
	{
		var a = (float)sinceAct;
		// Targets for this act.
		float bigTo = 0f, smallTo = 0f, spreadTo = 0f, leanTo = 0f, liftTo = 0f, rollTo = 0f, open = 0.25f + 0.2f * MathF.Sin( t * 1.7f );
		var tremble = 0f;
		switch ( act )
		{
			case Act.Slam:
				if ( a < 0.8f ) { var k = Ease( a / 0.7f ); bigTo = smallTo = 1.35f * k; leanTo = 0.14f * k; spreadTo = 0.25f * k; open = 0.9f; }
				else if ( a < 1.5f ) { bigTo = smallTo = -0.42f; leanTo = -0.12f; spreadTo = 0.1f; open = 0.1f; }
				break;
			case Act.Dig:
			case Act.Buried:
				bigTo = 0.4f + MathF.Sin( t * 9f ) * 0.5f;
				smallTo = 0.4f + MathF.Sin( t * 9f + MathF.PI ) * 0.5f;
				leanTo = -0.1f;
				tremble = 0.02f;
				break;
			case Act.Erupt:
				if ( a >= EruptCue ) { bigTo = smallTo = 1.5f; spreadTo = 0.5f; open = 1f; leanTo = 0.1f; }
				break;
			case Act.Horn:
				spreadTo = -0.25f;
				bigTo = smallTo = 0.25f;
				leanTo = 0.05f * Ease( a / HornWindUp );
				tremble = 0.015f + 0.03f * a / HornWindUp;
				open = 0.05f;
				break;
			case Act.Jammed:
				bigTo = smallTo = -0.3f;
				rollTo = 0.12f;
				leanTo = -0.08f;
				open = 0.6f;
				break;
			case Act.Beam:
			{
				var up = a < BeamRear + BeamCharge + BeamTime ? Ease( a / BeamRear ) : 1f - Ease( (a - (BeamRear + BeamCharge + BeamTime)) / 0.7f );
				liftTo = 1.25f * up;
				leanTo = 0.32f * up;
				spreadTo = 0.55f * up;
				bigTo = smallTo = 0.5f * up;
				open = 0.8f;
				if ( Burning ) tremble = 0.01f;
				break;
			}
			case Act.Dead:
				bigTo = smallTo = -0.5f;
				leanTo = 0.1f + dying * 0.45f;
				rollTo = dying * 0.35f;
				open = 1f;
				break;
		}
		// The claws spring to their marks; the slam's drop is fast.
		var armFreq = act == Act.Slam && a >= 0.8f ? 22f : 7f;
		Spring( ref bigPitch, ref bigPitchV, bigTo, armFreq, 0.6f, dt );
		Spring( ref smallPitch, ref smallPitchV, smallTo, armFreq * 1.05f, 0.6f, dt );
		Spring( ref spread, ref spreadV, spreadTo, 5f, 0.7f, dt );
		Spring( ref lean, ref leanV, leanTo, act == Act.Slam && a >= 0.8f ? 12f : 4f, 0.45f, dt );
		Spring( ref lift, ref liftV, liftTo, 3.5f, 0.75f, dt );
		Spring( ref roll, ref rollV, rollTo, 3f, 0.6f, dt );
		finger = MathX.Lerp( finger, open, 1f - MathF.Exp( -8f * dt ) );
		var walking = velocity.Flat().Length();
		bob = MathF.Sin( stepClock * 2f ) * 0.06f * MathX.Clamp( walking / 3f, 0f, 1f );

		body.Position = new Vector3( Rng.Float( -1f, 1f ) * tremble, bob + Rng.Float( -1f, 1f ) * tremble, 0f );
		if ( bigArm is not null )
		{
			bigArm.Basis = new Basis( Vector3.Up, -0.15f - spread ) * new Basis( Vector3.Right, bigPitch );
			smallArm.Basis = new Basis( Vector3.Up, 0.15f + spread ) * new Basis( Vector3.Right, smallPitch );
			// The hands turn a little in toward each other; the moving fingers open and shut.
			bigClaw.Basis = new Basis( Vector3.Up, 0.3f ) * new Basis( Vector3.Right, -0.25f * bigPitch );
			smallClaw.Basis = new Basis( Vector3.Up, -0.3f ) * new Basis( Vector3.Right, -0.25f * smallPitch );
			bigFinger.Basis = new Basis( Vector3.Back, -finger * 0.6f );
			smallFinger.Basis = new Basis( Vector3.Back, finger * 0.6f );
		}
		// The tower rocks with him and rears up for the beam, baring the belly under its lip.
		tower.Position = TowerBase + new Vector3( 0f, lift, lift * 0.35f );
		tower.Basis = new Basis( Vector3.Back, roll + MathF.Sin( t * 0.7f ) * 0.015f ) * new Basis( Vector3.Right, TowerTilt + lean );
		var bulge = 1f + (BellyOpen ? 0.12f + 0.05f * MathF.Sin( t * 6f ) : 0f);
		bellyBulge.Scale = new Vector3( 1.9f, 1.15f, 1.6f ) * bulge;
		abdomen.Position = new Vector3( 0f, 1.45f + lift * 0.4f / CrabScale, 0.35f );
		// Eye stalks and antennae: on the nearest player, swaying.
		var prey = Nearest;
		for ( var i = 0; i < stalks.Count; i++ )
		{
			var stalk = stalks[i];
			var s = i == 0 ? -1f : 1f;
			var sway = MathF.Sin( t * 1.3f + i * 2f ) * 0.08f;
			var yaw = 0f;
			var pitch = 0f;
			if ( prey is not null && act != Act.Dead )
			{
				var local = stalk.GlobalTransform.AffineInverse() * (prey.RealPosition + Vector3.Up * 1.4f);
				yaw = MathX.Clamp( MathF.Atan2( -local.X, -local.Z ), -0.8f, 0.8f );
				pitch = MathX.Clamp( MathF.Atan2( local.Y - 0.9f, new Vector2( local.X, local.Z ).Length() ), -0.5f, 0.5f );
			}
			stalk.Rotation = new Vector3( -0.1f + sway, 0f, s * 0.12f + sway * 0.5f );
			if ( stalk.GetNodeOrNull<Node3D>( "Eye" ) is { } eye ) eye.Rotation = new Vector3( pitch, yaw, 0f );
		}
		for ( var i = 0; i < antennae.Count; i++ )
		{
			var s = i == 0 ? -1f : 1f;
			antennae[i].Basis = new Basis( Vector3.Up, -s * (0.5f + 0.2f * MathF.Sin( t * 1.1f + i )) ) * new Basis( Vector3.Right, -1.1f + 0.15f * MathF.Sin( t * 1.7f + i * 1.3f ) );
		}
		StepLegs( dt );
	}

	/// <summary>
	/// The legs: each foot stays planted on the sand until its rest spot (ahead along his walk) has moved off it, then steps,
	/// two at a time (one set, then the other). Under the sand they just hang at rest.
	/// </summary>
	void StepLegs( float dt )
	{
		var walking = velocity.Flat();
		stepClock += dt * (1.5f + walking.Length() * 1.2f);
		var under = act is Act.Arrive or Act.Dig or Act.Buried || (act == Act.Erupt && (float)sinceAct < EruptCue + 0.3f) || risen < 0.95f;
		var stepping = legs.Exists( l => l.Step >= 0f );
		var stepTime = MathX.Clamp( 0.34f - walking.Length() * 0.025f, 0.18f, 0.34f );
		foreach ( var leg in legs )
		{
			var hip = body.GlobalTransform * leg.Hip;
			var rest = GlobalTransform * leg.Rest + walking * 0.28f;
			var sand = Sculpt.GroundAt( rest );
			rest.Y = sand != float.MinValue && !under ? sand : (GlobalTransform * leg.Rest).Y;
			Vector3 foot;
			if ( under || act == Act.Dead && dying > 0.9f )
			{
				leg.Plant = rest;
				leg.Step = -1f;
				leg.Planted = false;
				foot = rest;
			}
			else
			{
				if ( !leg.Planted ) { leg.Plant = rest; leg.Planted = true; }
				if ( leg.Step < 0f && leg.Plant.DistanceTo( rest ) > 0.85f && !stepping && act != Act.Dead )
				{
					// This set's turn: step the legs of this foot's group together.
					var group = leg.Group;
					foreach ( var other in legs )
						if ( other.Group == group && other.Step < 0f ) { other.Step = 0f; other.From = other.Plant; }
					stepping = true;
				}
				if ( leg.Step >= 0f )
				{
					leg.To = rest;
					leg.Step += dt / stepTime;
					var k = Ease( leg.Step );
					foot = leg.From.Lerp( leg.To, k ) + Vector3.Up * MathF.Sin( MathF.Min( 1f, leg.Step ) * MathF.PI ) * 0.55f;
					if ( leg.Step >= 1f ) { leg.Plant = leg.To; leg.Step = -1f; foot = leg.Plant; if ( walking.Length() > 0.5f && Rng.Float() < 0.5f ) GpuFx.SandPuff( leg.Plant + Vector3.Up * 0.05f, Vector3.Up * 0.5f, SeaStir.SiltColour( leg.Plant ), Rng.Float( 6f, 10f ), 0.6f ); }
				}
				else foot = leg.Plant;
			}
			// Hip out and up to the knee, down and out to the ankle, then the pointed tip into the sand.
			var span = foot - hip;
			var outward = span.Flat().SafeNormal();
			var reach = span.Flat().Length();
			var knee = hip + outward * reach * 0.42f + Vector3.Up * MathX.Clamp( 1.5f - reach * 0.12f, 0.5f, 1.3f );
			var ankle = foot + Vector3.Up * 1.05f - outward * 0.2f;
			Place( leg.Upper, hip, knee );
			Place( leg.Lower, knee, ankle );
			Place( leg.Tip, ankle, foot );
			leg.Knee.GlobalPosition = knee;
			leg.Ankle.GlobalPosition = ankle;
		}
	}

	/// <summary> A unit tube along +Y set from one point to another. </summary>
	static void Place( MeshInstance3D m, Vector3 from, Vector3 to )
	{
		var axis = to - from;
		var length = MathF.Max( 0.05f, axis.Length() );
		var turn = new Basis( new Quaternion( Vector3.Up, axis / length ) );
		m.GlobalTransform = new Transform3D( turn * Basis.FromScale( new Vector3( 1f, length, 1f ) ), from );
	}
}
