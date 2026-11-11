using Godot;

/// <summary>
/// What makes the Kraken look alive rather than posed (playtest round 1): its head bobs, breathes and sways on a
/// spring, lurches when shot and lags when it turns; every tentacle segment rides its own spring, so a hit, a
/// turn, a cut or a slam sends a ripple down the tentacle; and the tentacles are held above the sea bed.
/// </summary>
public partial class Kraken
{
	/// <summary> How big it is drawn: the model is built at half this size. </summary>
	public const float Size = 2f;

	// The head's lean (degrees: x tips it back, y rolls it to its left) and how fast that is changing.
	Vector2 headLean, headLeanSpeed;
	Node3D headPivot, headModel;
	float lastYaw;
	bool yawKnown;

	// Each segment's springy offset from its pose (degrees: bend up, swing sideways) and its speed.
	readonly float[,] wigBend = new float[Tentacles, Segments], wigSwing = new float[Tentacles, Segments];
	readonly float[,] wigBendSpeed = new float[Tentacles, Segments], wigSwingSpeed = new float[Tentacles, Segments];

	const float HeadSpring = 38f, HeadDamping = 4.2f, MaxLean = 15f;
	const float SegSpring = 26f, SegDamping = 2.6f, MaxWiggle = 55f;

	/// <summary> For tests: how far the head is leaning off its spring (degrees). </summary>
	public float HeadLean => headLean.Length();
	/// <summary> For tests: the biggest springy offset on a tentacle right now (degrees). </summary>
	public float Wiggle( int k ) => Enumerable.Range( 0, length[k] ).Select( j => MathF.Max( MathF.Abs( wigBend[k, j] ), MathF.Abs( wigSwing[k, j] ) ) ).DefaultIfEmpty( 0f ).Max();
	/// <summary> For tests: how far the lowest tentacle surface is above the sea bed (metres; negative is buried). </summary>
	public float LowestTentacle()
	{
		var lowest = float.MaxValue;
		for ( var k = 0; k < Tentacles; k++ )
			for ( var j = 0; j < length[k]; j++ )
			{
				var end = segments[k, j].GlobalTransform * new Vector3( 0f, 0f, -SegLength * stretch[k] );
				lowest = MathF.Min( lowest, end.Y - SegRadius( j + 1 ) * Size - GlobalPosition.Y );
			}
		return lowest;
	}
	/// <summary> For tests: the sand it pushed up. </summary>
	public Node3D Mound => mound;

	/// <summary> Every frame: the head's spring, bob and breathing, and the tentacle springs. </summary>
	void UpdateLife( float dt )
	{
		dt = MathF.Min( dt, 0.05f );
		// How fast the body is turning: the head rolls into the turn and the tentacles trail behind it.
		var yaw = MathX.Deg( GlobalBasis.GetEuler().Y );
		var turning = yawKnown ? Mathf.Wrap( yaw - lastYaw, -180f, 180f ) / MathF.Max( dt, 0.001f ) : 0f;
		lastYaw = yaw;
		yawKnown = true;
		turning = MathX.Clamp( turning, -120f, 120f );

		headLeanSpeed += (-headLean * HeadSpring - headLeanSpeed * HeadDamping) * dt;
		headLeanSpeed.Y += turning * 0.9f * dt;
		headLean += headLeanSpeed * dt;
		headLean = new Vector2( MathX.Clamp( headLean.X, -MaxLean, MaxLean ), MathX.Clamp( headLean.Y, -MaxLean, MaxLean ) );
		if ( Has( Head ) )
		{
			var head = parts[Head].Go;
			var back = headLean.X + MathF.Sin( time * 0.45f ) * 2.2f;
			var roll = headLean.Y + MathF.Sin( time * 0.33f + 1.3f ) * 2.8f;
			head.Basis = new Basis( Vector3.Right, MathX.Rad( back ) ) * new Basis( Vector3.Back, MathX.Rad( roll ) );
			head.Position = Vector3.Up * (State == Mode.Idle ? MathF.Sin( time * 0.8f ) * 0.07f : 0f);
			// Breathing: it swells and settles.
			var breath = MathF.Sin( time * 1.15f ) * 0.022f;
			headPivot.Scale = new Vector3( 1f + breath, 1f - breath * 0.5f, 1f + breath );
		}

		for ( var k = 0; k < Tentacles; k++ )
		{
			for ( var j = 0; j < length[k]; j++ )
			{
				// Each segment is pulled toward the one before it, so a kick travels out to the tip.
				var towardBend = j > 0 ? wigBend[k, j - 1] * 0.85f : 0f;
				var towardSwing = j > 0 ? wigSwing[k, j - 1] * 0.85f : 0f;
				wigBendSpeed[k, j] += (-(wigBend[k, j] - towardBend) * SegSpring - wigBendSpeed[k, j] * SegDamping) * dt;
				wigSwingSpeed[k, j] += (-(wigSwing[k, j] - towardSwing) * SegSpring - wigSwingSpeed[k, j] * SegDamping) * dt;
				wigSwingSpeed[k, j] -= turning * (0.35f + j * 0.12f) * dt * 6f;
				wigBend[k, j] = MathX.Clamp( wigBend[k, j] + wigBendSpeed[k, j] * dt, -MaxWiggle, MaxWiggle );
				wigSwing[k, j] = MathX.Clamp( wigSwing[k, j] + wigSwingSpeed[k, j] * dt, -MaxWiggle, MaxWiggle );
			}
		}
	}

	/// <summary> A segment's bend and sideways swing this frame (degrees): its pose, its slow idle wave and its spring. </summary>
	(float Bend, float Swing) SegmentPose( int k, int j, float unfurl )
	{
		var p = phase[k];
		// A slow wave running out along the tentacle, with the tip curling and uncurling on its own time.
		var wave = MathF.Sin( time * 0.62f - j * 0.75f + p ) * (3f + j * 1.7f);
		var curl = j >= 5 ? MathF.Sin( time * 0.31f + p * 2.1f ) * 9f * (j - 4) : 0f;
		var swing = (MathF.Sin( time * 0.44f - j * 0.6f + p * 1.7f ) * (3f + j * 1.9f) + MathF.Sin( time * 0.17f + p ) * 6f) * unfurl;
		var bend = MathX.Lerp( CurlBend[j], RestBend[j], unfurl ) + (wave + curl) * (0.3f + 0.7f * unfurl);
		return (bend + wigBend[k, j], swing + wigSwing[k, j]);
	}

	/// <summary> Shot in the head: it lurches away from the shot and wobbles. </summary>
	void KickHead( Vector3 direction, float strength )
	{
		var local = rig.GlobalBasis.Orthonormalized().Inverse() * direction.SafeNormal();
		headLeanSpeed += new Vector2( local.Z, -local.X ) * strength;
		if ( headModel is not null ) CreatureGore.Jiggle( headModel, 0.55f );
	}

	/// <summary> A tentacle segment is knocked: it and its neighbours whip away from the blow. </summary>
	void KickSegment( int k, int j, Vector3 direction, float strength )
	{
		if ( j >= length[k] ) return;
		var basis = segments[k, j].GlobalBasis.Orthonormalized();
		var bend = direction.Dot( basis.Y ) * strength;
		var swing = -direction.Dot( basis.X ) * strength;
		for ( var n = Math.Max( 0, j - 1 ); n < length[k]; n++ )
		{
			var share = n == j ? 1f : n < j ? 0.4f : 0.7f / (n - j);
			wigBendSpeed[k, n] += bend * share;
			wigSwingSpeed[k, n] += swing * share;
		}
		if ( segments[k, j].GetChildCount() > 0 && segments[k, j].GetChild( 0 ) is Node3D model ) CreatureGore.Jiggle( model, 0.8f );
	}

	/// <summary> A whole tentacle thrashes (it was cut, or it just hit the sea bed). </summary>
	void Thrash( int k, float strength )
	{
		for ( var j = 0; j < length[k]; j++ )
		{
			wigBendSpeed[k, j] += Rng.Float( 0.3f, 1f ) * strength;
			wigSwingSpeed[k, j] += Rng.Float( -1f, 1f ) * strength;
		}
	}

	/// <summary>
	/// Keeps a tentacle out of the sea bed: any segment whose far end would dip under the sand is bent up until it
	/// rests on it. Run after the pose is set, once the Kraken is up.
	/// </summary>
	void KeepAboveGround( int k )
	{
		var floor = GlobalPosition.Y;
		for ( var j = 0; j < length[k]; j++ )
		{
			var segment = segments[k, j];
			var reach = SegLength * stretch[k];
			for ( var pass = 0; pass < 2; pass++ )
			{
				var end = segment.GlobalTransform * new Vector3( 0f, 0f, -reach );
				var need = floor + SegRadius( j + 1 ) * Size + 0.04f;
				if ( end.Y >= need ) break;
				var lift = MathF.Asin( MathX.Clamp( (need - end.Y) / (reach * Size), 0f, 0.95f ) );
				segment.Basis *= new Basis( Vector3.Right, lift );
			}
		}
	}
}
