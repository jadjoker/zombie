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
	public static void Clear() => Totals.Clear();
}
