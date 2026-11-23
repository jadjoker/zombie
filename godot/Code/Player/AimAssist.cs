using Godot;

/// <summary>
/// Aim assist for the controller (issue #33; reworked after the user found it "pretty awful"), only while a pad is the
/// device in use, at the strength of the setting (GameSettings.AimAssist). It works on whole enemies, not on single
/// limbs (it used to hop from leg to leg): each enemy is aimed at the middle of its parts, with a bubble round it as
/// big as the enemy is wide. Inside the bubble:
///  - Friction: the stick slows toward the middle, so you can stay on it.
///  - Tracking: when you or it move, the aim follows it a good share of the way (the big one on a console: strafe and
///    the crosshair stays on).
///  - Pull: while the stick is moving or you fire (gently while you only walk), the aim is drawn toward its middle,
///    harder on the sights.
/// It sticks to the enemy it is helping with until that one leaves a wider bubble, so it never jitters between two.
/// Going onto the sights snaps toward the nearest enemy near the crosshair (`AimSnap`).
/// </summary>
public static class AimAssist
{
	/// <summary> The most it slows the stick when right on a target (a share of normal speed). </summary>
	public const float SlowestScale = 0.42f;
	/// <summary> The pull toward a target at full strength, degrees a second (on the sights x1.5). </summary>
	public const float PullDegreesPerSecond = 120f;
	/// <summary> The share of a target's motion across the view the aim follows at full strength. </summary>
	public const float Tracking = 0.9f;
	/// <summary> How far away it works (metres). </summary>
	public const float Range = 70f;
	/// <summary> The bubble: the enemy's half width plus this (metres), as an angle between these (degrees). </summary>
	public const float Pad = 0.55f, MinBubble = 3.5f, MaxBubble = 16f;
	/// <summary> Going onto the sights snaps to an enemy this near the crosshair (degrees), over `SnapTime`. </summary>
	public const float SnapAngle = 14f, SnapTime = 0.16f;

	public struct Result
	{
		/// <summary> A share of the normal stick speed (1 when nothing is near the crosshair). </summary>
		public float Slow;
		/// <summary> Degrees to turn this frame (x yaw, y pitch). </summary>
		public Vector2 Pull;
		/// <summary> How near the crosshair is to the target's middle: 0 outside its bubble, 1 on it. </summary>
		public float Closeness;
	}

	/// <summary> For tests: the enemy it is helping with, and one of its parts. </summary>
	public static global::Creature LastEnemy { get; private set; }
	public static CreaturePart LastTarget { get; private set; }

	public static Result None => new() { Slow = 1f };

	static global::Creature sticky;
	static Vector2 lastAngles;   // the target's yaw and pitch from the eye, last frame
	static ulong lastFrame;
	static float wasAiming, snapLeft;

	/// <summary> Where an enemy is aimed at (the middle of its parts) and how wide it is (metres). </summary>
	static bool Centre( global::Creature c, out Vector3 at, out float halfWidth )
	{
		var sum = Vector3.Zero;
		var n = 0;
		foreach ( var p in CreaturePart.All )
		{
			if ( !GodotObject.IsInstanceValid( p ) || p.IsDead || p.Creature != c ) continue;
			sum += p.GlobalPosition;
			n++;
		}
		at = n > 0 ? sum / n : c.GlobalPosition + Vector3.Up * 0.5f;
		halfWidth = 0.3f;
		if ( n == 0 ) return true;
		foreach ( var p in CreaturePart.All )
			if ( GodotObject.IsInstanceValid( p ) && !p.IsDead && p.Creature == c ) halfWidth = MathF.Max( halfWidth, p.GlobalPosition.DistanceTo( at ) );
		halfWidth = MathF.Min( halfWidth, 2.5f );   // a boss is aimed at its middle, not anywhere on it
		return true;
	}

	static Vector2 AnglesTo( Vector3 d ) => new( MathX.Deg( MathF.Atan2( -d.X, -d.Z ) ), MathX.Deg( -MathF.Asin( MathX.Clamp( d.Y, -1f, 1f ) ) ) );

	/// <summary> What to do this frame. `stickActive` is the right stick off its dead zone; `firing` the trigger held; `aiming` how far onto the sights. </summary>
	public static Result Evaluate( PlayerController c, Angles eyes, float dt, bool stickActive, bool firing, float aiming )
	{
		LastTarget = null;
		LastEnemy = null;
		var strength = MathX.Clamp( GameSettings.AimAssist, 0f, 1f );
		if ( !Controls.IsController || strength <= 0f || c?.Camera is not { } camera ) { sticky = null; return None; }
		var from = camera.GlobalPosition;
		var forward = -camera.GlobalBasis.Z;
		var bubbleScale = MathX.Lerp( 1f, 1.35f, aiming );

		// The enemy to help with: the one it already has while it stays in a wider bubble, else the best in view.
		global::Creature best = null;
		var bestAt = Vector3.Zero;
		var bestScore = float.MaxValue;
		var bestCloseness = 0f;
		var bestDistance = 0f;
		foreach ( var e in global::Creature.Everyone )
		{
			if ( !GodotObject.IsInstanceValid( e ) || !e.IsEnemy || e.IsDead ) continue;
			Centre( e, out var at, out var half );
			var to = at - from;
			var distance = to.Length();
			if ( distance < 0.8f || distance > Range ) continue;
			var bubble = MathX.Clamp( MathX.Deg( MathF.Atan( (half + Pad) / distance ) ), MinBubble, MaxBubble ) * bubbleScale;
			if ( e == sticky ) bubble *= 1.5f;
			var angle = MathX.Deg( forward.AngleTo( to / distance ) );
			if ( angle > bubble ) continue;
			// The nearest to the crosshair wins, a near one a little ahead of a far one; the held one wins ties.
			var score = angle / bubble + distance * 0.004f - (e == sticky ? 0.25f : 0f);
			if ( score >= bestScore ) continue;
			bestScore = score;
			best = e;
			bestAt = at;
			bestDistance = distance;
			bestCloseness = 1f - MathX.Clamp( angle / bubble, 0f, 1f );
		}
		var frame = Engine.GetProcessFrames();
		if ( best is null ) { sticky = null; wasAiming = aiming; return None; }
		// Not through a wall.
		var tr = Trace.Ray( c, from, bestAt, Layers.World );
		if ( tr.Hit && tr.Distance < bestDistance - 0.8f ) { sticky = null; wasAiming = aiming; return None; }

		var angles = AnglesTo( (bestAt - from) / bestDistance );
		var fresh = best != sticky || frame - lastFrame > 2;
		var moved = fresh ? Vector2.Zero : new Vector2( Mathf.Wrap( angles.X - lastAngles.X, -180f, 180f ), angles.Y - lastAngles.Y );
		sticky = best;
		lastAngles = angles;
		lastFrame = frame;
		LastEnemy = best;
		LastTarget = CreaturePart.All.FirstOrDefault( p => GodotObject.IsInstanceValid( p ) && p.Creature == best );

		var result = new Result { Closeness = bestCloseness };
		// Friction: smooth, strongest on the middle.
		var ease = bestCloseness * bestCloseness * (3f - 2f * bestCloseness);
		result.Slow = 1f - (1f - SlowestScale) * ease * strength;

		var playerMoving = c.Velocity.Flat().Length() > 0.5f;
		var engaged = stickActive || firing || playerMoving;
		var pull = Vector2.Zero;
		// Tracking: follow the target's motion across the view (from either side moving).
		if ( engaged && moved.Length() < 20f ) pull += moved * MathF.Min( 1f, Tracking * (0.5f + strength * 0.7f) ) * MathF.Min( 1f, bestCloseness * 5f );
		// Pull toward the middle while the stick moves or the trigger is down.
		var delta = new Vector2( Mathf.Wrap( angles.X - eyes.Yaw, -180f, 180f ), angles.Y - eyes.Pitch );
		if ( stickActive || firing || playerMoving )
		{
			var rate = PullDegreesPerSecond * strength * MathF.Pow( bestCloseness, 0.5f ) * (aiming > 0.5f ? 1.5f : 1f) * (firing ? 1.2f : stickActive ? 1f : 0.5f);
			var step = MathF.Min( delta.Length(), rate * dt );
			if ( delta.Length() > 0.0001f ) pull += delta.Normalized() * step;
		}
		// Onto the sights: a quick snap to it (once each time the sights come up).
		if ( aiming > 0.2f && wasAiming <= 0.2f ) snapLeft = SnapTime;
		if ( snapLeft > 0f )
		{
			snapLeft -= dt;
			var angleOff = delta.Length();
			if ( angleOff < SnapAngle * bubbleScale ) pull += delta * MathF.Min( 1f, dt / MathF.Max( 0.01f, snapLeft + dt ) ) * strength;
		}
		wasAiming = aiming;
		result.Pull = pull;
		return result;
	}
}
