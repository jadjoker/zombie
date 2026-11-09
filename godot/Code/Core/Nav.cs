using Godot;

/// <summary>
/// The navmesh (s&box Scene.NavMesh, agent height 64, radius 16, step 18, slope 40): baked at runtime from the
/// level's static colliders.
/// </summary>
public static class Nav
{
	public static NavigationRegion3D Region { get; private set; }

	public static void Bake( Node3D level )
	{
		level.AddToGroup( "navmesh_source" );
		var mesh = new NavigationMesh
		{
			AgentHeight = 64f * U.In,
			AgentRadius = 16f * U.In,
			AgentMaxClimb = 18f * U.In,
			AgentMaxSlope = 40f,
			CellSize = 0.1f,
			CellHeight = 0.05f,
			GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
			GeometryCollisionMask = Layers.World,
			GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.GroupsWithChildren,
			GeometrySourceGroupName = "navmesh_source",
		};
		Region = new NavigationRegion3D { Name = "NavMesh", NavigationMesh = mesh };
		level.AddChild( Region );
		Region.BakeNavigationMesh( false );
	}

	public static Rid Map( Node3D any ) => any.GetWorld3D().NavigationMap;

	/// <summary> The closest point on the navmesh. </summary>
	public static Vector3 Closest( Node3D any, Vector3 point ) => NavigationServer3D.MapGetClosestPoint( Map( any ), point );

	/// <summary> A random point on the navmesh near a spot (s&box NavMesh.GetRandomPoint). </summary>
	public static Vector3? RandomPoint( Node3D any, Vector3 around, float radius )
	{
		for ( var i = 0; i < 8; i++ )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var p = around + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * Rng.Float( 0f, radius );
			var c = Closest( any, p );
			if ( c.DistanceTo( p ) < 2f ) return c;
		}
		return null;
	}
}

/// <summary>
/// Port of the s&box NavMeshAgent as the game used it: MoveTo a goal, Stop, a top speed and acceleration, and it
/// walks its owner along a path over the navmesh, sticking to the ground. Velocity is in m/s.
/// </summary>
public sealed class NavAgent
{
	public float MaxSpeed = 200f * U.In;
	public float Acceleration = 600f * U.In;
	public bool Enabled = true;
	public Vector3 Velocity { get; private set; }
	public Vector3 AgentPosition => owner.GlobalPosition;

	readonly Node3D owner;
	Vector3[] path = Array.Empty<Vector3>();
	int index;
	Vector3? goal;

	public NavAgent( Node3D owner ) => this.owner = owner;

	public void MoveTo( Vector3 target )
	{
		goal = target;
		path = NavigationServer3D.MapGetPath( Nav.Map( owner ), owner.GlobalPosition, target, true );
		index = 0;
		if ( path.Length == 0 ) path = new[] { owner.GlobalPosition, target };
	}

	public void Stop()
	{
		goal = null;
		path = Array.Empty<Vector3>();
	}

	public void SetAgentPosition( Vector3 position )
	{
		owner.GlobalPosition = Nav.Closest( owner, position );
		if ( goal is { } g ) MoveTo( g );
	}

	/// <summary> Steps along the path. Call every frame. </summary>
	public void Update( float dt )
	{
		if ( !Enabled ) { Velocity = Vector3.Zero; return; }
		var wanted = Vector3.Zero;
		if ( goal is not null && path.Length > 0 )
		{
			while ( index < path.Length && owner.GlobalPosition.Flat().DistanceTo( path[index].Flat() ) < 8f * U.In ) index++;
			if ( index < path.Length )
			{
				var to = (path[index] - owner.GlobalPosition).Flat();
				var remaining = to.Length();
				for ( var i = index; i + 1 < path.Length; i++ ) remaining += path[i].DistanceTo( path[i + 1] );
				var speed = MathF.Min( MaxSpeed, MathF.Sqrt( 2f * Acceleration * remaining ) );
				wanted = to.SafeNormal() * speed;
			}
			else goal = null;
		}
		Velocity = Velocity.MoveToward( wanted, Acceleration * dt );
		var next = owner.GlobalPosition + Velocity * dt;
		// Stick to the navmesh (and so the ground).
		var snapped = Nav.Closest( owner, next );
		owner.GlobalPosition = snapped == Vector3.Zero && next.Length() > 1f ? next : snapped;
	}
}
