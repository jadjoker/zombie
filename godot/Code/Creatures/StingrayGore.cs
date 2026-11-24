using Godot;

/// <summary>
/// The sting ray's gore: every hit that doesn't kill wounds the disc and oozes its teal-green blood, badly hurt it bloodies
/// and bleeds as it glides, an eye bursts on its own health, the tail can be shot off; on death it comes apart (tail, eyes
/// and the torn carcass as loose pieces, with a burst of its gibs).
/// </summary>
public partial class Stingray
{
	public const float EyeShare = 0.12f, TailShare = 0.18f;
	/// <summary> Tests: wounds made, eyes and tails off, pieces thrown on deaths. </summary>
	public static int Wounds, EyesOut, TailsOff, DeathPieces;
	readonly Dictionary<int, float> partHealth = new();
	readonly bool[] eyeGone = new bool[2];
	bool tailGone;
	float bleedTick;
	public bool TailGone => tailGone;
	public int EyesGone => (eyeGone[0] ? 1 : 0) + (eyeGone[1] ? 1 : 0);

	void GoreSetup()
	{
		var max = MathF.Max( 1f, health.MaxHealth );
		partHealth[2] = partHealth[3] = max * EyeShare;
		partHealth[4] = max * TailShare;
	}

	void HitGore( int index, float dealt, Vector3 at, Vector3 direction )
	{
		if ( gore is null ) return;
		if ( index == 7 ) { Sfx.Play( "mine_tink", at, 0.8f, Rng.Float( 0.9f, 1.2f ) ); return; }   // the king's crown: gold, it rings
		gore.GooBurst( at, -direction, GooColor, 8, 240f, 0.8f );
		gore.GooSpray( at, direction, 3, GooColor, 180f, 0.5f, 1.1f );
		GibKit.Chips( Gibs, at, -direction, direction, 0.06f * MathF.Sqrt( Size ), Rng.Float() < 0.4f ? 2 : 1 );
		if ( index is 0 or 1 or 5 or 6 )
		{
			Wounds++;
			CreatureGore.AddWound( body, at, direction, SandColour, GooColor, 6f * MathF.Sqrt( Size ) );
			var ooze = new GooOoze { Name = "Bleed", Color = GooColor, Lifetime = 5f, Interval = 0.25f };
			body.AddChild( ooze );
			ooze.GlobalPosition = at;
		}
		if ( !partHealth.ContainsKey( index ) ) return;
		partHealth[index] -= dealt;
		if ( partHealth[index] > 0f ) return;
		if ( index is 2 or 3 ) PopEye( index - 2 );
		else if ( index == 4 ) ShootTailOff( direction );
	}

	void GoreFrame( float dt )
	{
		var hurt = 1f - MathX.Clamp( health.Current / MathF.Max( 1f, health.MaxHealth ), 0f, 1f );
		if ( IsDead || hurt < 0.5f || gore is null ) return;
		bleedTick -= dt;
		if ( bleedTick > 0f ) return;
		bleedTick = MathX.Lerp( 0.55f, 0.18f, (hurt - 0.5f) * 2f );
		gore.GooDrip( body.GlobalTransform * new Vector3( Rng.Float( -0.6f, 0.6f ), -0.04f, Rng.Float( -0.4f, 0.4f ) ), GooColor );
		if ( Size > 1f ) gore.GooDrip( body.GlobalTransform * new Vector3( Rng.Float( -0.6f, 0.6f ), -0.04f, Rng.Float( -0.4f, 0.4f ) ), GooColor );
	}

	void PopEye( int which )
	{
		if ( eyeGone[which] || which >= eyes.Count ) return;
		eyeGone[which] = true;
		EyesOut++;
		var eye = eyes[which];
		var at = eye.GlobalPosition;
		eye.Visible = false;
		foreach ( var part in body.GetChildren().OfType<CreaturePart>().Where( p => p.Index == which + 2 ).ToList() ) part.QueueFree();
		gore.GooBurst( at, Vector3.Up, GooColor, 16, 280f, MathF.Sqrt( Size ) );
		GibKit.Burst( Gibs, at, Vector3.Up, 0.07f * Size, 4 );
		GibKit.Stump( Gibs, body, at, Vector3.Up, 0.07f * Size, 6f );
		Sfx.Play( "splat", at, 1f, 0.9f );
	}

	void ShootTailOff( Vector3 direction )
	{
		if ( tailGone || tail.Count < 2 ) return;
		tailGone = true;
		TailsOff++;
		var root = tail[1];
		var at = root.GlobalPosition;
		if ( IsInstanceValid( tailPart ) ) tailPart.QueueFree();
		Loose( root, 5f * Size, 0.6f * Size, direction.Flat().SafeNormal() * 2f + Vector3.Up * 1.5f );
		GibKit.Stump( Gibs, body, at, (at - body.GlobalPosition).SafeNormal(), 0.05f * Size, 6f );
		gore.GooBurst( at, direction, GooColor, 10, 220f, 0.8f * MathF.Sqrt( Size ) );
		Sfx.Play( "stab_squish_0", at, 0.9f, 1.1f );
	}

	Gib Loose( Node3D piece, float radiusInches, float mass, Vector3 velocity )
	{
		foreach ( var part in piece.FindChildren( "*", "", true, false ).OfType<CreaturePart>().ToList() ) part.QueueFree();
		var gib = Gib.Attach( piece, gore, radiusInches, 30f, mass, velocity, GooColor );
		CreatureGore.Twitch( piece, Rng.Float( 2f, 4f ), 0.8f );
		return gib;
	}

	/// <summary> Death: the tail and the eyes fly off, the carcass floats up torn open, and a burst of its pieces. </summary>
	void FallApart( Vector3 direction )
	{
		if ( gore is null || body is null ) return;
		direction = direction == Vector3.Zero ? -GlobalBasis.Z : direction.Normalized();
		var centre = body.GlobalPosition;
		var count = 0;
		var big = (int)MathF.Round( Size );
		gore.GooBurst( centre, direction, GooColor, 30 * big, 340f, 1.3f * MathF.Sqrt( Size ) );
		gore.GooSpray( centre, direction, 8 * big, GooColor, 280f, 0.8f, 1.6f );
		GibKit.Burst( Gibs, centre, direction, 0.24f * MathF.Sqrt( Size ), 14 * big );
		Vector3 Fling( float up ) => direction * Rng.Float( 2f, 4f ) + Rng.InSphere( 2f ) + Vector3.Up * up;
		if ( !tailGone && tail.Count > 1 ) { Loose( tail[1], 5f * Size, 0.6f * Size, Fling( 1.5f ) ); count++; }
		for ( var i = 0; i < eyes.Count; i++ )
		{
			if ( eyeGone[i] || !IsInstanceValid( eyes[i] ) ) continue;
			Loose( eyes[i], 4f * Size, 0.3f * Size, Fling( 2.5f ) );
			count++;
		}
		// The carcass: torn open at the head, oozing.
		var front = body.GlobalTransform * new Vector3( 0f, 0f, -0.3f );
		GibKit.Stump( Gibs, body, front, (front - centre).SafeNormal(), 0.3f * Size, 10f );
		var ooze = new GooOoze { Name = "Bleed", Color = GooColor, Lifetime = 12f, Interval = 0.1f };
		body.AddChild( ooze );
		ooze.GlobalPosition = front;
		// Freeze the wings where they are (the shader keeps its last pose).
		foreach ( var m in new[] { topSkin, bellySkin } ) { m.SetShaderParameter( "charge", 0f ); m.SetShaderParameter( "alert", 0f ); }
		if ( King ) count += CrownOff( Fling( 3f ) );
		Loose( body, 20f * Size, 8f * Size, direction * 1.5f + Vector3.Up * 1f );
		count++;
		DeathPieces += count;
		Sfx.Play( "stab_squish_1", centre, 1f, 0.8f );
		Sfx.Play( "creature_death", centre, 1f, 0.75f );
	}
}
