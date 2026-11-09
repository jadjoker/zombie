using Godot;
using Godot.Collections;

/// <summary> One trace result (s&box SceneTraceResult). Distances in metres. </summary>
public struct TraceResult
{
	public bool Hit;
	public bool StartedSolid;
	public Vector3 StartPosition;
	public Vector3 EndPosition;
	public Vector3 HitPosition;
	public Vector3 Normal;
	public float Distance;
	public float Fraction;
	public Node3D Node;
}

/// <summary>
/// Scene traces, wrapping Godot's space queries the way s&box's Scene.Trace read in the original code.
/// Collision layers stand in for s&box tags (see <see cref="Layers"/>).
/// </summary>
public static class Trace
{
	static PhysicsDirectSpaceState3D Space( Node3D from ) => from.GetWorld3D().DirectSpaceState;

	public static TraceResult Ray( Node3D context, Vector3 from, Vector3 to, uint mask = Layers.World, Array<Rid> exclude = null )
	{
		var query = PhysicsRayQueryParameters3D.Create( from, to, mask, exclude ?? new Array<Rid>() );
		query.HitFromInside = true;
		var hit = Space( context ).IntersectRay( query );
		var result = new TraceResult { StartPosition = from, EndPosition = to, Fraction = 1f, Distance = from.DistanceTo( to ) };
		if ( hit.Count == 0 ) return result;
		result.Hit = true;
		result.HitPosition = (Vector3)hit["position"];
		result.Normal = (Vector3)hit["normal"];
		result.Node = hit["collider"].As<Node3D>();
		result.Distance = from.DistanceTo( result.HitPosition );
		result.Fraction = result.Distance / MathF.Max( 0.0001f, from.DistanceTo( to ) );
		result.StartedSolid = result.Normal == Vector3.Zero;
		result.EndPosition = result.HitPosition;
		return result;
	}

	/// <summary> Sweeps a shape from one point to another; the first contact. </summary>
	public static TraceResult Shape( Node3D context, Shape3D shape, Vector3 from, Vector3 to, uint mask = Layers.World, Array<Rid> exclude = null )
	{
		var space = Space( context );
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = shape,
			Transform = new Transform3D( Basis.Identity, from ),
			Motion = to - from,
			CollisionMask = mask,
			Exclude = exclude ?? new Array<Rid>(),
		};
		var result = new TraceResult { StartPosition = from, EndPosition = to, Fraction = 1f, Distance = from.DistanceTo( to ) };
		var cast = space.CastMotion( query );
		if ( cast.Length < 2 ) return result;
		if ( cast[0] >= 1f && cast[1] >= 1f )
		{
			// Clear the whole way; still check the start for overlap.
			if ( space.IntersectShape( query, 1 ).Count > 0 ) { result.Hit = true; result.StartedSolid = true; result.HitPosition = from; result.Distance = 0f; result.Fraction = 0f; }
			return result;
		}
		if ( cast[0] <= 0f && cast[1] <= 0f ) { result.Hit = true; result.StartedSolid = true; result.HitPosition = from; result.Distance = 0f; result.Fraction = 0f; return result; }

		result.Hit = true;
		result.Fraction = cast[0];
		result.EndPosition = from + (to - from) * cast[0];
		result.HitPosition = result.EndPosition;
		result.Distance = from.DistanceTo( result.EndPosition );
		query.Transform = new Transform3D( Basis.Identity, from + (to - from) * cast[1] );
		var info = space.GetRestInfo( query );
		if ( info.Count > 0 )
		{
			result.Normal = (Vector3)info["normal"];
			result.HitPosition = (Vector3)info["point"];
			result.Node = GodotObject.InstanceFromId( (ulong)(long)info["collider_id"] ) as Node3D;
		}
		return result;
	}

	public static TraceResult Sphere( Node3D context, float radius, Vector3 from, Vector3 to, uint mask = Layers.World, Array<Rid> exclude = null )
		=> Shape( context, new SphereShape3D { Radius = radius }, from, to, mask, exclude );
}

/// <summary>
/// Collision layers standing in for the s&box tags the game used. World geometry is layer 1.
/// </summary>
public static class Layers
{
	public const uint World = 1 << 0;
	public const uint Player = 1 << 1;
	public const uint Creature = 1 << 2;
	public const uint Gib = 1 << 3;
	public const uint Trigger = 1 << 4;
	public const uint Debris = 1 << 5;
}
