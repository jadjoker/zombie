using Godot;

/// <summary>
/// Online: a joiner's copy of the Mermaid Queen follows the host's moves (each attack, the bite, the choke, the
/// ascension), draws her song rings, her gaze beam where the host's looks, and harmless copies of her pearls and
/// riptides; the charm on a joiner's own view comes from this copy. Only the host's do harm.
/// </summary>
public partial class MermaidQueen
{
	int seenPearls = -1, seenWaves = -1;
	/// <summary> Tests: pearls and riptides a joiner's copy drew for the host's. </summary>
	public static int PuppetPearls, PuppetWaves;

	public override void NetWrite( List<float> s )
	{
		s.Add( (int)act ); s.Add( (float)sinceAct ); s.Add( Ascended ? 1f : 0f );
		s.Add( gazeAt.X ); s.Add( gazeAt.Y ); s.Add( gazeAt.Z );
		s.Add( pearls ); s.Add( pearlFrom.X ); s.Add( pearlFrom.Y ); s.Add( pearlFrom.Z ); s.Add( pearlVelocity.X ); s.Add( pearlVelocity.Y ); s.Add( pearlVelocity.Z );
		s.Add( waves ); s.Add( waveFrom.X ); s.Add( waveFrom.Y ); s.Add( waveFrom.Z ); s.Add( waveDir.X ); s.Add( waveDir.Z );
		s.Add( floorY );
	}

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 20 || act == Act.Dead ) return;
		var next = (Act)(int)s[0];
		var changed = next != act;
		if ( changed && next != Act.Dead ) Begin( next );
		NetTimer( ref sinceAct, s[1], changed );
		Ascended = s[2] > 0.5f;
		gazeAt = new Vector3( s[3], s[4], s[5] );
		var p = (int)s[6];
		if ( seenPearls >= 0 && p > seenPearls )
		{
			var from = new Vector3( s[7], s[8], s[9] );
			var v = new Vector3( s[10], s[11], s[12] );
			QueenPearl pearl = null;
			while ( held.Count > 0 && pearl is null ) { var h = held[0]; held.RemoveAt( 0 ); if ( IsInstanceValid( h ) ) pearl = h; }
			pearl ??= QueenPearl.Grow( this, from, 0, 1 );
			if ( pearl is not null ) { pearl.GlobalPosition = from; pearl.Launch( v, 0f, PearlRadius ); PuppetPearls++; }
			Sfx.Play( "queen_pearl", from, 0.9f, Rng.Float( 0.9f, 1.2f ) );
		}
		seenPearls = p;
		var w = (int)s[13];
		if ( seenWaves >= 0 && w > seenWaves )
		{
			var from = new Vector3( s[14], s[15], s[16] );
			Waves( from, new Vector3( s[17], 0f, s[18] ), 0f );
			Sfx.Play( "queen_wave", from, 1.5f, 0.9f );
			PuppetWaves++;
		}
		seenWaves = w;
		floorY = s[19];
	}

	/// <summary> A joiner's copy, every frame: growing in, the ascension's ring, the pearls cleared when the storm ends. </summary>
	void PuppetActs()
	{
		var a = (float)sinceAct;
		shown = act == Act.Arrive ? MathX.Lerp( 0.05f, 1f, SmoothKit.Ease( a / 1.4f ) ) : 1f;
		if ( act == Act.Ascend && !struck && a >= 1.2f )
		{
			struck = true;
			QueenWave.Send( GetTree().CurrentScene, GlobalPosition with { Y = floorY }, 0f, MathF.PI, WaveSpeed, WaveReach * 0.8f, 0f, this, 0f );
			AlienBlast.Spawn( GetTree().CurrentScene, HeadAt, 1.6f );
		}
		if ( act != Act.Pearls && held.Count > 0 ) ClearHeld();
	}
}
