using Godot;
using System.Collections.Generic;
using System;
using System.Linq;

/// <summary>
/// Test-only glitch finder (--pops): watches every drawn mesh each frame and counts
/// spawn pops (first frame far from the second), one-frame spikes (jumps away and back),
/// and one-frame flashes (shown for a single frame). Prints a summary when the game quits.
/// </summary>
public partial class PopProbe : Node
{
	sealed class Track
	{
		public string Name;
		public Vector3 P0, P1;   // last two drawn positions (P1 newest)
		public int Frames;
		public bool Seen0, Seen1;   // visible on the last two frames
		public bool New;
	}

	readonly Dictionary<ulong, Track> tracks = new();
	readonly Dictionary<string, int> spawnPops = new(), spikes = new(), flashes = new();
	readonly Dictionary<string, float> worst = new();
	int frame;

	public override void _Ready()
	{
		RenderingServer.FramePreDraw += Sample;
	}

	public override void _ExitTree()
	{
		RenderingServer.FramePreDraw -= Sample;
		GD.Print( $"[pops] {frame} frames, {tracks.Count} meshes watched" );
		void Dump( string kind, Dictionary<string, int> d )
		{
			foreach ( var (name, count) in d.OrderByDescending( x => x.Value ).Take( 12 ) )
				GD.Print( $"[pops] {kind,-6} x{count,-4} {name} (worst {(worst.TryGetValue( kind + name, out var w ) ? w : 0f):0.00} m)" );
		}
		Dump( "spawn", spawnPops );
		Dump( "spike", spikes );
		Dump( "flash", flashes );
		if ( spawnPops.Count + spikes.Count + flashes.Count == 0 ) GD.Print( "[pops] none" );
		GD.Print( "[sounds] " + string.Join( ", ", Sfx.Requested.OrderBy( x => x.Key ).Select( x => $"{x.Key} x{x.Value.Count}{(x.Value.Found ? "" : " MISSING")}" ) ) );
	}

	static string Label( Node n )
	{
		// Owner chain like "Bloober/Body Part" without instance numbers.
		var parts = new List<string>();
		for ( var x = n; x is not null && parts.Count < 3 && x is not Window; x = x.GetParent() )
			parts.Insert( 0, System.Text.RegularExpressions.Regex.Replace( x.Name, @"@?\d+@?|^@", "" ) );
		return string.Join( "/", parts );
	}

	void Bump( Dictionary<string, int> d, string kind, string name, float size )
	{
		d[name] = d.GetValueOrDefault( name ) + 1;
		worst[kind + name] = MathF.Max( worst.GetValueOrDefault( kind + name ), size );
	}

	void Sample()
	{
		frame++;
		var seen = new HashSet<ulong>();
		foreach ( var node in GetTree().Root.FindChildren( "*", "GeometryInstance3D", true, false ) )
		{
			if ( node is not GeometryInstance3D g ) continue;
			var id = g.GetInstanceId();
			seen.Add( id );
			var visible = g.IsVisibleInTree() && g.GlobalBasis.Scale.Length() > 0.01f;   // shrunk to nothing counts as hidden
			var p = g.IsPhysicsInterpolatedAndEnabled() ? g.GetGlobalTransformInterpolated().Origin : g.GlobalPosition;
			if ( !tracks.TryGetValue( id, out var t ) )
			{
				tracks[id] = new Track { Name = Label( g ), P1 = p, Frames = 1, Seen1 = visible, New = frame > 30 };
				continue;
			}
			// Spawn pop: its first drawn frame is far off the path it then follows (not just moving fast).
			if ( t.Frames == 2 && t.New && t.Seen0 && t.Seen1 && visible )
			{
				var first = t.P1.DistanceTo( t.P0 );
				var next = p.DistanceTo( t.P1 );
				if ( first > 1.5f && first > next * 3f )
				{
					if ( !spawnPops.ContainsKey( t.Name ) )
					{
						Node owner = g; while ( owner is not null && owner.GetScript().Obj is null ) owner = owner.GetParent();
						GD.Print( $"[pops] detail spawn {t.Name}: {t.P0} -> {t.P1} -> {p}, script owner {owner?.GetType().Name} {owner?.Name}" );
					}
					Bump( spawnPops, "spawn", t.Name, first );
				}
			}
			// Spike: jumped away last frame and came back now.
			if ( t.Frames >= 2 && t.Seen1 )
			{
				var away = t.P1.DistanceTo( t.P0 );
				var back = p.DistanceTo( t.P0 );
				if ( away > 1f && back < away * 0.2f )
				{
					if ( !spikes.ContainsKey( t.Name ) ) GD.Print( $"[pops] detail spike {t.Name}: {t.P0} -> {t.P1} -> {p} at frame {frame}" );
					Bump( spikes, "spike", t.Name, away );
				}
			}
			// Flash: shown for exactly one frame between hidden frames (after its first frames).
			if ( t.Frames >= 3 && t.Seen1 && !t.Seen0 && !visible ) Bump( flashes, "flash", t.Name, 0f );
			t.P0 = t.P1; t.P1 = p;
			t.Seen0 = t.Seen1; t.Seen1 = visible;
			t.Frames++;
		}
		if ( frame % 120 == 0 )
			foreach ( var id in tracks.Keys.Where( k => !seen.Contains( k ) ).ToList() ) tracks.Remove( id );
	}
}
