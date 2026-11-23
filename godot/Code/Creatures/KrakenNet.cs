using Godot;

/// <summary>
/// Online: a joiner's copy of the Kraken follows the host's slam and ink (the move, how far into it, where it aims, the
/// globs it threw) and only shows them; the host's Kraken lands the hits.
/// </summary>
public partial class Kraken
{
	bool netBegin;
	int inkShots, seenInk = -1;
	Vector3 inkFrom;
	readonly Vector3[] inkVelocity = new Vector3[3];

	public override void NetWrite( List<float> s )
	{
		s.Add( (int)Attacking ); s.Add( attackTime ); s.Add( aimLocked ? 1f : 0f );
		s.Add( aimPoint.X ); s.Add( aimPoint.Y ); s.Add( aimPoint.Z );
		s.Add( inkShots );
		s.Add( inkFrom.X ); s.Add( inkFrom.Y ); s.Add( inkFrom.Z );
		foreach ( var v in inkVelocity ) { s.Add( v.X ); s.Add( v.Y ); s.Add( v.Z ); }
	}

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 19 ) return;
		var next = (Attack)(int)s[0];
		if ( next != Attacking && State == Mode.Idle )
		{
			netBegin = true;
			try { Begin( next ); }
			finally { netBegin = false; }
		}
		if ( MathF.Abs( attackTime - s[1] ) > 0.2f ) attackTime = s[1];
		aimLocked = s[2] > 0.5f;
		aimPoint = new Vector3( s[3], s[4], s[5] );
		var count = (int)s[6];
		if ( seenInk >= 0 && count > seenInk )
		{
			var from = new Vector3( s[7], s[8], s[9] );
			Sfx.Play( "ink_throw", from, 1f, 0.6f );
			Sfx.Play( "kraken_spit", from, 1f );
			for ( var i = 0; i < 3; i++ ) KrakenInk.Launch( this, from, new Vector3( s[10 + i * 3], s[11 + i * 3], s[12 + i * 3] ) );
			gore?.GooBurst( from, -GlobalBasis.Z, KrakenInk.Ink, 10, 420f, 3f );
		}
		seenInk = count;
	}
}
