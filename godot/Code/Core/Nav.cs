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
		// Baked again when the level changes (the teleporter): the old region goes.
		if ( Region is not null && GodotObject.IsInstanceValid( Region ) ) { Region.GetParent()?.RemoveChild( Region ); Region.Free(); }
		var mesh = new NavigationMesh
		{
			// s&box 64 / 16 / 18 inches, snapped to whole cells the way Godot snaps them anyway (it warned on every bake).
			AgentHeight = 1.7f,    // 64 in = 1.63 m, up to 17 cells of 0.1
			AgentRadius = 0.5f,    // 16 in = 0.41 m, up to 5 cells of 0.1
			AgentMaxClimb = 0.4f,  // 18 in = 0.46 m, down to 4 cells of 0.1
			AgentMaxSlope = 40f,
			CellSize = 0.1f,
			// 0.1 m steps: the baker keeps 8191 steps of height, so 0.05 m capped it at 409 m and squashed the water
			// level's dock (813 m up) onto a phantom floor at 409 m that spawns snapped to.
			CellHeight = 0.1f,
			GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
			GeometryCollisionMask = Layers.World | Layers.Plant,   // enemies walk round the solid plants and rocks too
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

	double pathAt = -1;
	Vector3 pathGoal;
	/// <summary> A path is planned again at most this often (seconds) while the goal stays put... </summary>
	public const float RepathEvery = 0.3f;
	/// <summary> ...or as soon as the goal has moved this far (metres). </summary>
	public const float RepathMove = 1.5f;
	/// <summary> Tests: paths planned. </summary>
	public static int Planned;

	/// <summary>
	/// Heads for a spot. Called every frame by the creatures that chase: planning a path is costly (it was most of a
	/// crab's frame), so the path is only planned again every `RepathEvery` or when the goal moved `RepathMove`;
	/// meanwhile the last corner just follows the goal.
	/// </summary>
	public void MoveTo( Vector3 target, bool force = false )
	{
		goal = target;
		var now = Clock.Now;
		if ( !force && pathAt >= 0 && path.Length > 0 && now - pathAt < RepathEvery && target.DistanceSquaredTo( pathGoal ) < RepathMove * RepathMove )
		{
			path[^1] = target;
			return;
		}
		// Spread the planning of a crowd over frames: each agent's next plan comes a little early or late.
		pathAt = now - Rng.Float( 0f, RepathEvery * 0.3f );
		pathGoal = target;
		Planned++;
		path = NavigationServer3D.MapGetPath( Nav.Map( owner ), owner.GlobalPosition, target, true );
		index = 0;
		if ( path.Length == 0 ) path = new[] { owner.GlobalPosition, target };
	}

	public void Stop()
	{
		goal = null;
		path = Array.Empty<Vector3>();
	}

	/// <summary> Puts the owner on the navmesh now (at spawn), so its first step does not hop there. </summary>
	public void Snap()
	{
		var snapped = Nav.Closest( owner, owner.GlobalPosition );
		if ( snapped != Vector3.Zero && snapped.DistanceTo( owner.GlobalPosition ) < 4f ) owner.GlobalPosition = snapped;
	}

	/// <summary> Online: a puppet's legs walk at the host copy's speed. </summary>
	public void SetPuppetVelocity( Vector3 velocity ) => Velocity = velocity;

	/// <summary> How often (seconds) an agent is put back on the navmesh exactly. </summary>
	public const float SnapEvery = 0.25f;
	float snapIn = Rng.Float( 0f, SnapEvery );
	float lift;
	bool liftKnown;

	/// <summary> The navmesh's height over the floor where the agent stands now (it stands on the mesh when it spawns). </summary>
	void LearnLift()
	{
		if ( liftKnown ) return;
		liftKnown = true;
		if ( FloorUnder( owner.GlobalPosition ) is float f ) lift = MathX.Clamp( owner.GlobalPosition.Y - f, -0.2f, 0.6f );
	}

	float? FloorUnder( Vector3 at )
	{
		var floor = Trace.Ray( owner, at + Vector3.Up * 0.6f, at + Vector3.Down * 1.5f, Layers.World );
		return floor.Hit ? floor.HitPosition.Y : null;
	}

	/// <summary>
	/// Moved from outside (shoved, dragged by the Gore Vac): every frame while dragged, so the costly navmesh lookup is
	/// left to the regular snap (Update) and the feet just follow the floor here.
	/// </summary>
	public void SetAgentPosition( Vector3 position )
	{
		LearnLift();   // shoved before its first snap: it would drop by the mesh's height (and shots aimed at it miss)
		if ( FloorUnder( position ) is float floorY ) position.Y = floorY + lift;
		owner.GlobalPosition = position;
		snapIn = MathF.Min( snapIn, 0.1f );   // back onto the mesh soon
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
		// Stick to the navmesh (and so the ground). The navmesh's closest point costs about half a millisecond on the
		// big sea floor (it was most of every crab's and spider's frame), so it is only asked a few times a second;
		// between, a short ray down keeps the feet on the floor while the path keeps it on the mesh.
		snapIn -= dt;
		if ( snapIn <= 0f || Velocity.LengthSquared() < 0.0001f && snapIn < SnapEvery * 0.5f )
		{
			snapIn = SnapEvery * Rng.Float( 0.8f, 1.2f );
			var snapped = Nav.Closest( owner, next );
			owner.GlobalPosition = snapped == Vector3.Zero && next.Length() > 1f ? next : snapped;
			// The navmesh sits a little over the floor: keep that height between snaps, or the feet bob up and down.
			if ( snapped != Vector3.Zero && FloorUnder( snapped ) is float f ) { lift = MathX.Clamp( snapped.Y - f, -0.2f, 0.6f ); liftKnown = true; }
			return;
		}
		LearnLift();
		if ( FloorUnder( next ) is float floorY ) next.Y = floorY + lift;
		owner.GlobalPosition = next;
	}
}
