using Godot;

/// <summary>
/// Online: a joiner's copy of the Lighthouse Hermit follows the host's moves, draws the same slam rings, eruption, beam,
/// trail and fog (harmless here: the host lands every hit).
/// </summary>
public partial class LighthouseHermit
{
	int fogs, seenFogs = -1;
	Vector3 fogAt;
	/// <summary> Tests: fogs and slams a joiner's copy drew for the host's. </summary>
	public static int PuppetFogs, PuppetSlams, PuppetBurrows;

	public override void NetWrite( List<float> s )
	{
		s.Add( (int)act ); s.Add( (float)sinceAct ); s.Add( Enraged ? 1f : 0f );
		s.Add( beamAt.X ); s.Add( beamAt.Y ); s.Add( beamAt.Z );
		s.Add( eruptAt.X ); s.Add( eruptAt.Y ); s.Add( eruptAt.Z );
		s.Add( fogs ); s.Add( fogAt.X ); s.Add( fogAt.Y ); s.Add( fogAt.Z );
		s.Add( risen ); s.Add( ground );
	}

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 15 || act == Act.Dead ) return;
		var next = (Act)(int)s[0];
		var changed = next != act;
		if ( changed && next != Act.Dead )
		{
			Begin( next );
			if ( next == Act.Slam ) PuppetSlams++;
			if ( next == Act.Dig ) PuppetBurrows++;
		}
		NetTimer( ref sinceAct, s[1], changed );
		Enraged = s[2] > 0.5f;
		beamAt = new Vector3( s[3], s[4], s[5] );
		eruptAt = new Vector3( s[6], s[7], s[8] );
		var f = (int)s[9];
		var at = new Vector3( s[10], s[11], s[12] );
		if ( seenFogs >= 0 && f > seenFogs )
		{
			FogBank.Roll( this, at, FogRadius, FogTime );
			HornFx( at );
			PuppetFogs++;
		}
		seenFogs = f;
		risen = s[13];
		ground = s[14];
	}

	/// <summary> A joiner's copy, every frame: the slam's sand and rings, the eruption, the beam's trail (all harmless). </summary>
	void PuppetActs()
	{
		var a = (float)sinceAct;
		switch ( act )
		{
			case Act.Slam:
				if ( !struck && a >= 0.9f ) { struck = true; SlamFx( SlamAt ); }
				var rings = SlamRings + (Enraged ? 1 : 0);
				while ( struck && ringsOut < rings && a >= 0.9f + ringsOut * RingGap )
				{
					ShockRing.Send( GetTree().CurrentScene, SlamAt, RingSpeed, RingReach, 0f, 0f, this, sand: true );
					ringsOut++;
				}
				break;
			case Act.Erupt:
				if ( !struck && a >= EruptCue ) { struck = true; EruptFx(); }
				break;
			case Act.Beam:
				if ( Burning ) LampTrail.Leave( this, BeamLine().To, 0f );
				break;
		}
	}
}
