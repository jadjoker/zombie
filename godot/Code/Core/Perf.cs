using Godot;
using System.Diagnostics;

/// <summary>
/// A tiny code timer for the --perf test: `using var _ = Perf.Time( "label" );` round a hot path adds its time to that
/// label. Off (and nearly free) unless `Enabled`; the test reads `Totals` and clears them per scenario.
/// </summary>
public static class Perf
{
	public static bool Enabled;
	public static readonly Dictionary<string, (double Ms, int Calls)> Totals = new();

	public readonly struct Scope : IDisposable
	{
		readonly string label;
		readonly long start;
		public Scope( string label ) { this.label = label; start = Stopwatch.GetTimestamp(); }
		public void Dispose()
		{
			if ( label is null ) return;
			var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
			Totals[label] = Totals.TryGetValue( label, out var t ) ? (t.Ms + ms, t.Calls + 1) : (ms, 1);
		}
	}

	public static Scope Time( string label ) => Enabled ? new Scope( label ) : default;

	static long markAt;
	static string markLabel;
	/// <summary> Charges the time since the last mark to that mark's label (rough sectioning of one long method). </summary>
	public static void Mark( string label )
	{
		if ( !Enabled ) return;
		var now = Stopwatch.GetTimestamp();
		if ( markLabel is not null )
		{
			var ms = (now - markAt) * 1000.0 / Stopwatch.Frequency;
			if ( ms < 500 ) Totals[markLabel] = Totals.TryGetValue( markLabel, out var t ) ? (t.Ms + ms, t.Calls + 1) : (ms, 1);
		}
		markLabel = label;
		markAt = now;
	}
	public static void Clear() => Totals.Clear();
}
