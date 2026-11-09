using Godot;

/// <summary>
/// Porting helpers. The s&box game measured everything in inches with Z up; Godot is metres with Y up.
/// Ported tunings keep their original s&box numbers, multiplied by <see cref="U.In"/>, so they can be
/// checked against the s&box code side by side. All maths is done in Godot's convention (Y up, -Z forward).
/// </summary>
public static class U
{
	/// <summary> One s&box unit (an inch) in metres. </summary>
	public const float In = 0.0254f;

	/// <summary> s&box units per metre. </summary>
	public const float PerMetre = 39.37f;

	/// <summary> s&box physics gravity (850 in/s^2) in m/s^2. </summary>
	public const float Gravity = 850f * In;

	/// <summary>
	/// An s&box-space vector (inches; x forward, y left, z up) as a Godot one (metres; -Z forward, X right, Y up).
	/// For local offsets written in the s&box code, e.g. camera-space view model poses.
	/// </summary>
	public static Vector3 Sbox( float x, float y, float z ) => new( -y * In, z * In, -x * In );

	public static Vector3 Sbox( Vector3 v ) => Sbox( v.X, v.Y, v.Z );
}

public static class VecExt
{
	/// <summary> Horizontal part (s&box: WithZ( 0 )). </summary>
	public static Vector3 Flat( this Vector3 v ) => new( v.X, 0f, v.Z );

	public static Vector3 WithY( this Vector3 v, float y ) => new( v.X, y, v.Z );

	public static Vector3 ClampLength( this Vector3 v, float max )
	{
		var len = v.Length();
		return len > max && len > 0f ? v * (max / len) : v;
	}

	public static bool IsNearlyZero( this Vector3 v, float tolerance = 0.0001f ) => v.LengthSquared() <= tolerance * tolerance;

	/// <summary> Normal that tolerates zero length (s&box Vector3.Normal). </summary>
	public static Vector3 SafeNormal( this Vector3 v ) => v.LengthSquared() > 1e-12f ? v.Normalized() : Vector3.Zero;

	/// <summary> Adds without pushing past maxLength along the added direction (s&box AddClamped). </summary>
	public static Vector3 AddClamped( this Vector3 v, Vector3 add, float maxLength )
	{
		var dir = add.SafeNormal();
		var along = v.Dot( dir );
		var room = maxLength - along;
		if ( room <= 0f ) return v;
		return v + dir * MathF.Min( room, add.Length() );
	}
}

public static class MathX
{
	public static float Clamp( float v, float min, float max ) => v < min ? min : v > max ? max : v;
	public static float Lerp( float a, float b, float t ) => a + (b - a) * Clamp( t, 0f, 1f );
	public static float LerpUnclamped( float a, float b, float t ) => a + (b - a) * t;
	public static float Approach( float from, float to, float step ) => from < to ? MathF.Min( from + step, to ) : MathF.Max( from - step, to );
	public static float Deg( float radians ) => radians * 57.29578f;
	public static float Rad( float degrees ) => degrees * 0.01745329f;
}

/// <summary>
/// s&box style eye angles in degrees: pitch positive looks down, yaw positive turns left (counter-clockwise from above).
/// </summary>
public struct Angles
{
	public float Pitch, Yaw, Roll;

	public Angles( float pitch, float yaw, float roll ) { Pitch = pitch; Yaw = yaw; Roll = roll; }

	public Angles WithPitch( float pitch ) => new( pitch, Yaw, Roll );

	/// <summary> As a Godot basis (Y up, -Z forward). </summary>
	public Basis ToBasis() => Basis.FromEuler( new Vector3( MathX.Rad( -Pitch ), MathX.Rad( Yaw ), MathX.Rad( -Roll ) ) );

	public Vector3 Forward => -ToBasis().Z;
	public Vector3 Right => ToBasis().X;
	public Vector3 Left => -ToBasis().X;
	public Vector3 Up => ToBasis().Y;

	public static Angles operator +( Angles a, Angles b ) => new( a.Pitch + b.Pitch, a.Yaw + b.Yaw, a.Roll + b.Roll );
}

/// <summary> Seconds since it was set, in game time (s&box TimeSince). </summary>
public struct TimeSince
{
	double start;
	public static implicit operator float( TimeSince t ) => (float)(Clock.Now - t.start);
	public static implicit operator TimeSince( float seconds ) => new() { start = Clock.Now - seconds };
	public override string ToString() => ((float)this).ToString( "0.00" );
}

/// <summary> Seconds until it runs out, in game time (s&box TimeUntil). Positive while waiting. </summary>
public struct TimeUntil
{
	double end;
	public static implicit operator float( TimeUntil t ) => (float)(t.end - Clock.Now);
	public static implicit operator TimeUntil( float seconds ) => new() { end = Clock.Now + seconds };
}

/// <summary> Small random helpers (s&box Game.Random). </summary>
public static class Rng
{
	static readonly Random random = new();
	public static float Float( float min, float max ) => min + (float)random.NextDouble() * (max - min);
	public static float Float() => (float)random.NextDouble();
	public static int Int( int min, int maxInclusive ) => random.Next( min, maxInclusive + 1 );
	public static Vector3 InSphere( float radius )
	{
		Vector3 v;
		do v = new Vector3( Float( -1f, 1f ), Float( -1f, 1f ), Float( -1f, 1f ) );
		while ( v.LengthSquared() > 1f );
		return v * radius;
	}
	public static Vector3 OnSphere() => InSphere( 1f ).SafeNormal() is var n && n != Vector3.Zero ? n : Vector3.Up;
}

public static class QuatX
{
	/// <summary>
	/// Quaternion.Slerp that tolerates slightly unnormalized input (Godot's C# Slerp throws, which skipped the rest
	/// of a creature's animation for that frame). Falls back to the target if either side is degenerate.
	/// </summary>
	public static Quaternion SafeSlerp( this Quaternion from, Quaternion to, float weight )
	{
		var a = from.LengthSquared() > 1e-8f ? from.Normalized() : Quaternion.Identity;
		var b = to.LengthSquared() > 1e-8f ? to.Normalized() : a;
		if ( !a.IsFinite() || !b.IsFinite() ) return Quaternion.Identity;
		return a.Slerp( b, weight );
	}
}
