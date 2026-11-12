using Godot;

/// <summary>
/// Aim assist for the controller (issue #33): only while a pad is the device in use. When an enemy is close to the
/// crosshair (and in view) the right stick slows down so it is easy to stay on, and while you are using the stick or
/// firing the aim is drawn gently toward the nearest part of it. How strong is a setting (GameSettings.AimAssist).
/// </summary>
public static class AimAssist
{
	/// <summary> The most it slows the stick when right on a target (a share of normal speed). </summary>
	public const float SlowestScale = 0.38f;
	/// <summary> The pull toward a target at full strength, degrees a second when dead centre of its cone. </summary>
	public const float PullDegreesPerSecond = 75f;
	/// <summary> How far away it works (metres). </summary>
	public const float Range = 80f;

	public struct Result
	{
		/// <summary> A share of the normal stick speed (1 when nothing is near the crosshair). </summary>
		public float Slow;
		/// <summary> Degrees to turn this frame, toward the target. </summary>
		public Vector2 Pull;
		/// <summary> How near the crosshair is to the target's middle: 0 outside its cone, 1 on it. </summary>
		public float Closeness;
	}

	/// <summary> For tests: the part it is helping with, if any. </summary>
	public static CreaturePart LastTarget { get; private set; }

	public static Result None => new() { Slow = 1f };

	/// <summary> What to do this frame. `stickActive` is the right stick off its dead zone; `firing` the trigger held. </summary>
	public static Result Evaluate( PlayerController c, Angles eyes, float dt, bool stickActive, bool firing, float aiming )
	{
		LastTarget = null;
		var strength = MathX.Clamp( GameSettings.AimAssist, 0f, 1f );
		if ( !Controls.IsController || strength <= 0f || c?.Camera is not { } camera ) return None;
		var from = camera.GlobalPosition;
		var forward = -camera.GlobalBasis.Z;

		CreaturePart best = null;
		var bestScore = 2f;
		var bestAt = Vector3.Zero;
		var bestDistance = 0f;
		var bestCloseness = 0f;
		foreach ( var part in CreaturePart.All )
		{
			if ( !GodotObject.IsInstanceValid( part ) || part.IsDead ) continue;
			if ( part.Creature is global::Creature { IsEnemy: false } ) continue;   // never help you aim at an ally
			var shape = part.GetChildren().OfType<CollisionShape3D>().FirstOrDefault();
			var at = shape?.GlobalPosition ?? part.GlobalPosition;
			var toPart = at - from;
			var distance = toPart.Length();
			if ( distance < 0.8f || distance > Range ) continue;
			// A cone about as wide as the thing: wider on the sights, and not smaller than a little way off.
			var radiusMetres = MathX.Lerp( 1.0f, 1.5f, aiming );
			var cone = MathX.Clamp( MathX.Deg( MathF.Atan( radiusMetres / distance ) ), 2.2f, 9f );
			var angle = MathX.Deg( forward.AngleTo( toPart / distance ) );
			if ( angle > cone ) continue;
			var score = angle / cone;
			if ( score >= bestScore ) continue;
			bestScore = score;
			best = part;
			bestAt = at;
			bestDistance = distance;
			bestCloseness = 1f - score;
		}
		if ( best is null ) return None;
		// Not through a wall.
		var tr = Trace.Ray( c, from, bestAt, Layers.World );
		if ( tr.Hit && tr.Distance < bestDistance - 0.6f ) return None;
		LastTarget = best;

		var result = new Result { Closeness = bestCloseness, Slow = 1f - (1f - SlowestScale) * bestCloseness * strength };
		if ( !stickActive && !firing ) return result;
		var d = (bestAt - from) / bestDistance;
		var yawTarget = MathX.Deg( MathF.Atan2( -d.X, -d.Z ) );
		var pitchTarget = MathX.Deg( -MathF.Asin( d.Y ) );
		var delta = new Vector2( Mathf.Wrap( yawTarget - eyes.Yaw, -180f, 180f ), pitchTarget - eyes.Pitch );
		var rate = PullDegreesPerSecond * strength * MathF.Pow( bestCloseness, 0.7f ) * (firing || aiming > 0.5f ? 1.4f : 1f);
		var step = MathF.Min( delta.Length(), rate * dt );
		result.Pull = delta.Length() > 0.0001f ? delta.Normalized() * step : Vector2.Zero;
		return result;
	}
}
