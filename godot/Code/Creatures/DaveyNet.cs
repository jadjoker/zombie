using Godot;

/// <summary>
/// Online: a joiner's copy of Davey follows the host's moves (vanishing and coming back, the swipe, the cannons, the
/// sink), raises harmless copies of his cannons, and shows the sand mound and the MASH prompt when this machine's
/// player is the one buried (its jump presses reach the host's Davey through its robot there).
/// </summary>
public partial class DaveyJones
{
	int cannonVolleys, seenVolleys = -1;
	readonly Vector3[] cannonAt = new Vector3[3];
	long cannonTarget;
	long buriedPeer;

	public override void NetWrite( List<float> s )
	{
		s.Add( (int)mode ); s.Add( (float)inMode ); s.Add( cannonVolleys ); s.Add( cannonTarget );
		foreach ( var p in cannonAt ) { s.Add( p.X ); s.Add( p.Y ); s.Add( p.Z ); }
		s.Add( buried?.Controller?.NetPeer ?? 0 );
		s.Add( buryAt.X ); s.Add( buryAt.Y ); s.Add( buryAt.Z );
		s.Add( Mashes ); s.Add( MashesNeeded );
	}

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 19 || mode == Mode.Dead ) return;
		var next = (Mode)(int)s[0];
		var changed = next != mode;
		if ( changed && next != Mode.Dead )
		{
			switch ( next )
			{
				case Mode.Vanish:
					Say( "VANISH!" );
					Sfx.Play( "ghost_vanish", GlobalPosition, 1f );
					gore?.GooBurst( GlobalPosition + Vector3.Up * Size, Vector3.Up, GooColor, 10, 200f, 0.8f );
					break;
				case Mode.Appear: Sfx.Play( "ghost_appear", GlobalPosition, 1.1f ); break;
				case Mode.Swipe: Sfx.Play( "axe_swing", GlobalPosition, 1f, 0.7f ); break;
				case Mode.Cannons: Say( "CANNON BALLS!" ); Sfx.Play( "ghost_appear", GlobalPosition, 0.7f, 0.6f ); break;
				case Mode.SinkWindUp: Say( "SINK!" ); Sfx.Play( "ghost_vanish", GlobalPosition, 0.8f, 1.4f ); break;
			}
			SetMode( next );
			Shootable( next != Mode.Gone );
		}
		if ( next != Mode.Dead ) NetTimer( ref inMode, s[1], changed );

		var volleys = (int)s[2];
		if ( seenVolleys >= 0 && volleys > seenVolleys )
		{
			var target = NetCreatures.PlayerOf( (long)s[3] )?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
			for ( var i = 0; i < 3; i++ )
				DaveyCannon.Raise( this, new Vector3( s[4 + i * 3], s[5 + i * 3], s[6 + i * 3] ), target, 0.5f + i * 0.9f, 0f );
		}
		seenVolleys = volleys;

		var peer = (long)s[13];
		var at = new Vector3( s[14], s[15], s[16] );
		if ( peer != buriedPeer )
		{
			if ( mound is not null && IsInstanceValid( mound ) ) mound.QueueFree();
			mound = null;
			if ( Sinking == this ) Sinking = null;
			buriedPeer = peer;
			if ( peer != 0 )
			{
				buryAt = at;
				BuildMound();
				Sfx.Play( "dive_impact", at, 1f, 0.8f );
				gore?.GooBurst( at + Vector3.Up * 0.3f, Vector3.Up, GooColor, 8, 180f, 0.7f );
				if ( peer == Net.Me ) { Sinking = this; Controls.Rumble( 0.6f ); }
			}
		}
		Mashes = (int)s[17];
		MashesNeeded = (int)s[18];
	}

	/// <summary> A joiner's copy, every frame: fading out and back in, spinning away. </summary>
	void PuppetShow( float dt )
	{
		switch ( mode )
		{
			case Mode.Vanish:
				fade = MathX.Clamp( 1f - (float)inMode / 0.6f, 0f, 1f );
				body.RotateY( dt * 6f );
				break;
			case Mode.Gone:
				fade = 0f;
				break;
			case Mode.Appear:
				fade = MathX.Clamp( (float)inMode / 0.45f, 0f, 1f );
				body.Rotation = body.Rotation with { Y = MathX.Lerp( body.Rotation.Y, 0f, 1f - MathF.Exp( -8f * dt ) ) };
				break;
			default:
				fade = 1f;
				body.Rotation = Vector3.Zero;
				break;
		}
	}
}
