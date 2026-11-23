using Godot;

/// <summary> The emotes (T, or hold the pad's D-pad down): what each is called and how long it plays. </summary>
public enum Emote { None, Griddy, Wave, Wank }

public static class Emotes
{
	public static readonly (Emote Id, string Name, float Seconds)[] All =
	{
		(Emote.Griddy, "The Griddy", 4.2f),
		(Emote.Wave, "Wave hi", 2.4f),
		(Emote.Wank, "Wanker", 3f),
	};

	public static float Length( Emote e ) { foreach ( var x in All ) if ( x.Id == e ) return x.Seconds; return 0f; }
	public static string Name( Emote e ) { foreach ( var x in All ) if ( x.Id == e ) return x.Name; return ""; }
}

/// <summary>
/// The emotes on the robot rig, in the same character space as the mouse 4 move poses (inches; x forward, y to the
/// left, z up). The Griddy: heel taps from foot to foot with the arms pumping, a bounce, and the hands up round the eyes
/// like glasses on the last bars. Wave hi: the right arm up, the hand swinging side to side. Wanker: the old rude
/// taunt, a loose fist shaken in front of the chest. The gun is put away while one plays. Started by
/// <see cref="PlayerController.PlayEmote"/>; online the others see it too (Net).
/// </summary>
public partial class PlayerRig
{
	float emoteBlend;

	/// <summary> The emote playing on this robot and how far into it (0 when none). </summary>
	Emote Playing => controller.Emote;
	float EmoteTime => (float)(Clock.Now - controller.EmoteAt);

	void EmoteBlends( float dt )
	{
		var on = Playing != Emote.None && (survivor?.IsAlive ?? true);
		emoteBlend = MathX.Approach( emoteBlend, on ? 1f : 0f, dt * (on ? 7f : 5f) );
	}

	bool EmoteHidesGun => emoteBlend > 0.2f;

	/// <summary> The body: the Griddy bounces and leans with the taps. </summary>
	void EmoteFrame( float dt, Basis yawRot, Vector3 feet, float hips, ref Basis rot, ref Vector3 pos )
	{
		EmoteBlends( dt );
		if ( emoteBlend < 0.001f || Playing != Emote.Griddy ) return;
		var t = EmoteTime;
		var beat = t * 2.6f * MathF.PI;   // about 2.6 taps a second
		var e = Ease( emoteBlend );
		var bounce = MathF.Abs( MathF.Sin( beat ) ) * 0.06f * e;
		var lean = new Angles( 6f * e, MathF.Sin( beat ) * 8f * e, MathF.Sin( beat ) * 4f * e ).ToBasis();
		rot = yawRot * lean;
		pos = feet + Vector3.Up * (hips - 0.06f * e + bounce) - rot * (Vector3.Up * hips);
	}

	Basis EmoteSpine( Basis spineRot )
	{
		if ( emoteBlend < 0.001f ) return spineRot;
		var t = EmoteTime;
		return Playing switch
		{
			Emote.Griddy => SAng( 10f * emoteBlend, MathF.Sin( t * 2.6f * MathF.PI ) * 10f * emoteBlend, 0f ) * spineRot,
			Emote.Wank => SAng( 4f * emoteBlend, 0f, MathF.Sin( t * 14f ) * 2f * emoteBlend ) * spineRot,
			_ => spineRot,
		};
	}

	Basis EmoteHead( Basis head )
	{
		if ( emoteBlend < 0.001f ) return head;
		var t = EmoteTime;
		return Playing switch
		{
			Emote.Griddy => SAng( -6f * emoteBlend, MathF.Sin( t * 1.3f * MathF.PI ) * 15f * emoteBlend, 0f ) * head,
			Emote.Wave => SAng( -5f * emoteBlend, 0f, MathF.Sin( t * 6f ) * 6f * emoteBlend ) * head,
			Emote.Wank => SAng( 0f, 0f, 10f * emoteBlend ) * head,   // a cocky tilt
			_ => head,
		};
	}

	void EmoteArm( string s, float side, Vector3 shoulder, float reach, ref Vector3 wristRef, ref Vector3 poleRef )
	{
		if ( emoteBlend < 0.001f ) return;
		var t = EmoteTime;
		var w = Ease( emoteBlend );
		var wrist = wristRef; var pole = poleRef;
		void To( Vector3 target, Vector3 bend )
		{
			wrist = wrist.Lerp( target, w );
			pole = pole.Normalized().Lerp( bend.Normalized(), w );
		}
		switch ( Playing )
		{
			case Emote.Griddy:
			{
				var length = Emotes.Length( Emote.Griddy );
				if ( t > length - 1.3f )
				{
					// The glasses: both hands up round the eyes, finger and thumb in a ring.
					To( shoulder + new Vector3( 5f, -side * 1.5f, 6.5f ), new Vector3( 0.2f, side, -0.8f ) );
				}
				else
				{
					// Pumping: one forward and up, the other back, swapping with each tap.
					var swing = MathF.Sin( t * 2.6f * MathF.PI + (s == "L" ? 0f : MathF.PI) );
					To( shoulder + new Vector3( 6f * swing, side * 3f, -reach * 0.55f + 4f * MathF.Max( 0f, swing ) ), new Vector3( -0.3f, side, -1f ) );
				}
				break;
			}
			case Emote.Wave:
				if ( s == "R" )
				{
					var sway = MathF.Sin( t * 9f );
					To( shoulder + new Vector3( 2f, side * (3f + 4f * sway), reach * 0.8f ), new Vector3( 0f, side, -0.6f ) );
				}
				break;
			case Emote.Wank:
				if ( s == "R" )
				{
					// A loose fist in front of the chest, shaken up and down.
					var shake = MathF.Sin( t * 22f ) * 2.2f;
					To( shoulder + new Vector3( reach * 0.55f, -side * 3f, -reach * 0.35f + shake ), new Vector3( -0.5f, side, -0.6f ) );
				}
				else To( shoulder + new Vector3( 1f, side * 4f, -reach * 0.9f ), new Vector3( -0.3f, side, -1f ) );   // the other hand on the hip
				break;
		}
		wristRef = wrist; poleRef = pole;
	}

	void EmoteLeg( string s, float side, Vector3 hip, float length, ref Vector3 ankleRef, ref Vector3 poleRef )
	{
		if ( emoteBlend < 0.001f || Playing != Emote.Griddy ) return;
		var t = EmoteTime;
		var w = Ease( emoteBlend );
		// Heel taps: the tapping foot kicks forward with the toes up, the other stays under the hip.
		var phase = MathF.Sin( t * 2.6f * MathF.PI + (s == "L" ? 0f : MathF.PI) );
		var tap = MathF.Max( 0f, phase );
		var target = hip + new Vector3( 7f * tap, side * 2.5f, -length * (0.95f - 0.05f * tap) );
		ankleRef = ankleRef.Lerp( target, w );
		poleRef = poleRef.Lerp( new Vector3( 30f, side * 4f, 0f ), w );
	}
}
