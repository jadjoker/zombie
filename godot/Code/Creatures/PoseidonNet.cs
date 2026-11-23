using Godot;

/// <summary>
/// Online: a joiner's copy of Poseidon follows the host's moves (wind-ups, throws, summons, vanishing, the stab), throws
/// harmless copies of his tridents, and shows his thrown whirlpool where the host's is.
/// </summary>
public partial class Poseidon
{
	int tridents, seenTridents = -1;
	Vector3 tridentFrom, tridentVelocity;
	PoseidonVortex thrown, shownVortex;
	Vector3 vortexAt;
	bool vortexOut;
	/// <summary> Tests: tridents a joiner's copy threw for the host's. </summary>
	public static int PuppetTridents;

	Vector3? LiveVortex => grabbedIn is not null && IsInstanceValid( grabbedIn ) ? grabbedIn.GlobalPosition
		: thrown is not null && IsInstanceValid( thrown ) && thrown.Live ? thrown.GlobalPosition : null;

	public override void NetWrite( List<float> s )
	{
		s.Add( (int)mode ); s.Add( (float)inMode ); s.Add( tridents );
		s.Add( tridentFrom.X ); s.Add( tridentFrom.Y ); s.Add( tridentFrom.Z );
		s.Add( tridentVelocity.X ); s.Add( tridentVelocity.Y ); s.Add( tridentVelocity.Z );
		var v = LiveVortex;
		s.Add( v.HasValue ? 1f : 0f );
		var at = v ?? Vector3.Zero;
		s.Add( at.X ); s.Add( at.Y ); s.Add( at.Z );
	}

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 13 || mode == Mode.Dead ) return;
		var next = (Mode)(int)s[0];
		var changed = next != mode;
		if ( changed && next != Mode.Dead )
		{
			if ( next == Mode.Vanish )
			{
				PoseidonVortex.Burst( this, GlobalPosition + Vector3.Down * HoverHeight * 0.8f );
				Sfx.Play( "whirlpool", GlobalPosition, 1f, 0.8f );
			}
			if ( next == Mode.Appear ) PoseidonVortex.Burst( this, GlobalPosition + Vector3.Down * HoverHeight * 0.8f );
			SetMode( next );
		}
		if ( next != Mode.Dead ) NetTimer( ref inMode, s[1], changed );
		var count = (int)s[2];
		if ( seenTridents >= 0 && count > seenTridents )
		{
			var from = new Vector3( s[3], s[4], s[5] );
			PoseidonTrident.Throw( this, from, new Vector3( s[6], s[7], s[8] ), 0f );
			PuppetTridents++;
			heldTrident.Visible = false;
			GetTree().CreateTimer( 0.9 ).Timeout += () => { if ( IsInstanceValid( heldTrident ) ) heldTrident.Visible = true; };
			Sfx.Play( "harpoon", from, 1f, 0.7f );
		}
		seenTridents = count;
		vortexOut = s[9] > 0.5f;
		vortexAt = new Vector3( s[10], s[11], s[12] );
		if ( vortexOut && (shownVortex is null || !IsInstanceValid( shownVortex )) )
		{
			shownVortex = PoseidonVortex.Shown( this, vortexAt );
			Sfx.Play( "whirlpool", GlobalPosition, 0.9f, 1.1f );
		}
		else if ( !vortexOut && shownVortex is not null && IsInstanceValid( shownVortex ) ) { shownVortex.Finish(); shownVortex = null; }
	}

	/// <summary> A joiner's copy, every frame: shrinking into and growing out of his whirlpools, his whirlpool moving. </summary>
	void PuppetShow( float dt )
	{
		var k = MathX.Clamp( (float)inMode / 0.8f, 0f, 1f );
		shown = mode switch
		{
			Mode.Vanish => MathX.Lerp( 1f, 0.05f, k ),
			Mode.Appear => MathX.Lerp( 0.05f, 1f, k ),
			_ => 1f,
		};
		if ( shownVortex is not null && IsInstanceValid( shownVortex ) )
			shownVortex.GlobalPosition = shownVortex.GlobalPosition.Lerp( vortexAt, 1f - MathF.Exp( -12f * dt ) );
	}
}
