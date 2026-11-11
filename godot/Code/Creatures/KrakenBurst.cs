using Godot;

/// <summary>
/// Playtest round 3 for the Kraken: its mouth (hidden until it sucks, then it pushes out and opens), its dome as real
/// glass (panes that crack, pop off one by one as it weakens, then all blow out as tumbling shards), and its death
/// (it swells and bursts into chunks of hide, collar, brain and tentacle instead of sinking).
/// </summary>
public partial class Kraken
{
	// ---- The mouth ----

	Node3D mouthNode;
	float mouthOpen;
	/// <summary> 0 shut and hidden, 1 wide open. </summary>
	public float MouthOpen => mouthOpen;

	void BuildMouth( Part head )
	{
		// Low on the face under the eyes, opening forward and 30 degrees down.
		mouthNode = new Node3D { Name = "Mouth", Position = new Vector3( 0f, 1.41f, -0.87f ), Basis = new Basis( Vector3.Right, MathX.Rad( -30f ) ), Visible = false };
		headPivot.AddChild( mouthNode );
		Sculpt( head, mouthNode, "mouth" );
	}

	void UpdateMouth( float dt )
	{
		if ( mouthNode is null || !IsInstanceValid( mouthNode ) ) return;
		var want = Attacking == Attack.Suck && !IsDead ? 1f : 0f;
		mouthOpen = MathX.Approach( mouthOpen, want, dt * (want > mouthOpen ? 5f : 3.5f) );
		mouthNode.Visible = mouthOpen > 0.02f;
		if ( !mouthNode.Visible ) return;
		// It pushes out with a little overshoot, and quivers while it pulls.
		var t = mouthOpen - 1f;
		var open = 1f + 2.7f * t * t * t + 1.7f * t * t;
		var quiver = 1f + MathF.Sin( time * 26f ) * 0.05f * mouthOpen;
		mouthNode.Scale = new Vector3( open * quiver, open * quiver, 0.45f + 0.55f * open );
	}

	// ---- The dome, in panes ----

	sealed class Pane
	{
		public MeshInstance3D Mesh;
		public Vector3[] Points;     // its outline, about its own middle, in the dome's units
		public Vector3 Out;          // the way it faces, in the dome's space
		public bool Cracked, Gone;
	}

	readonly List<Pane> panes = new();
	StandardMaterial3D crackedGlass, looseGlass;
	int panesPopped;
	static readonly Vector3 DomeCentre = new( 0f, 0.1f, 0f ), DomeRadii = new( 1.13f, 1.22f, 1.13f );
	const float DomeFloor = 0.04f;

	/// <summary> How many panes of the dome are still in place. </summary>
	public int PanesLeft => panes.Count( p => !p.Gone );

	/// <summary>
	/// Builds the dome under the helmet part as about thirty irregular panes of glass (a grid over the dome, shared out
	/// between scattered points), each its own mesh so it can crack and come away on its own.
	/// </summary>
	void BuildDome( Node3D under )
	{
		const int rings = 14, around = 40, count = 32;
		var random = new Random( 30 );
		var seeds = new Vector3[count];
		for ( var i = 0; i < count; i++ )
		{
			// Evenly spread up the dome (a spiral), then nudged, so no pane is a sliver.
			var up = (i + 0.5f) / count;
			var turn = i * 2.39996f + (float)random.NextDouble() * 0.5f;
			var flat = MathF.Sqrt( MathF.Max( 0f, 1f - up * up ) );
			seeds[i] = new Vector3( MathF.Cos( turn ) * flat, up, MathF.Sin( turn ) * flat ).Normalized();
		}
		var low = MathF.Asin( (DomeFloor - DomeCentre.Y) / DomeRadii.Y );
		Vector3 Direction( int ring, int step )
		{
			var rise = MathX.Lerp( low, MathF.PI * 0.5f, ring / (float)rings );
			var angle = step / (float)around * MathF.Tau;
			return new Vector3( MathF.Cos( rise ) * MathF.Cos( angle ), MathF.Sin( rise ), MathF.Cos( rise ) * MathF.Sin( angle ) );
		}
		Vector3 At( Vector3 direction, float scale ) => DomeCentre + direction * DomeRadii * scale;

		var quads = new List<(Vector3 A, Vector3 B, Vector3 C, Vector3 D)>[count];
		for ( var i = 0; i < count; i++ ) quads[i] = new();
		for ( var ring = 0; ring < rings; ring++ )
			for ( var step = 0; step < around; step++ )
			{
				var a = Direction( ring, step ); var b = Direction( ring, step + 1 );
				var c = Direction( ring + 1, step ); var d = Direction( ring + 1, step + 1 );
				var middle = (a + b + c + d).Normalized();
				var owner = 0;
				for ( var i = 1; i < count; i++ ) if ( seeds[i].Dot( middle ) > seeds[owner].Dot( middle ) ) owner = i;
				quads[owner].Add( (a, b, c, d) );
			}

		crackedGlass = (StandardMaterial3D)glassMaterial.Duplicate();
		crackedGlass.AlbedoColor = new Color( 0.92f, 0.98f, 1f, 0.62f );
		crackedGlass.EmissionEnergyMultiplier = 0.5f;
		looseGlass = (StandardMaterial3D)glassMaterial.Duplicate();
		looseGlass.AlbedoColor = new Color( 0.8f, 0.95f, 1f, 0.6f );
		looseGlass.EmissionEnergyMultiplier = 0.45f;

		for ( var i = 0; i < count; i++ )
		{
			if ( quads[i].Count == 0 ) continue;
			var middle = Vector3.Zero;
			foreach ( var q in quads[i] ) middle += At( q.A, 1f ) + At( q.B, 1f ) + At( q.C, 1f ) + At( q.D, 1f );
			middle /= quads[i].Count * 4;
			var tool = new SurfaceTool();
			tool.Begin( Mesh.PrimitiveType.Triangles );
			var points = new List<Vector3>();
			// Two skins a little apart, so a pane has some thickness when it tumbles.
			foreach ( var scale in new[] { 1f, 0.965f } )
				foreach ( var q in quads[i] )
				{
					var a = At( q.A, scale ) - middle; var b = At( q.B, scale ) - middle;
					var c = At( q.C, scale ) - middle; var d = At( q.D, scale ) - middle;
					tool.AddVertex( a ); tool.AddVertex( c ); tool.AddVertex( b );
					tool.AddVertex( b ); tool.AddVertex( c ); tool.AddVertex( d );
					points.Add( a ); points.Add( b ); points.Add( c ); points.Add( d );
				}
			tool.GenerateNormals();
			var mesh = new MeshInstance3D { Name = "Pane", Mesh = tool.Commit(), MaterialOverride = glassMaterial, Position = middle, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			under.AddChild( mesh );
			panes.Add( new Pane { Mesh = mesh, Points = points.ToArray(), Out = (middle - DomeCentre).Normalized() } );
		}
	}

	/// <summary> A shot on the dome: the panes round the hit craze over, and as it weakens the nearest ones break away. </summary>
	void CrackDome( Vector3 hit, Vector3 direction )
	{
		if ( !HelmetOn ) return;
		var dome = parts[Helmet].Go;
		var local = dome.GlobalTransform.AffineInverse() * hit;
		var nearest = panes.Where( p => !p.Gone ).OrderBy( p => p.Mesh.Position.DistanceSquaredTo( local ) ).ToList();
		foreach ( var pane in nearest.Take( 3 ) )
		{
			pane.Cracked = true;
			pane.Mesh.MaterialOverride = crackedGlass;
		}
		// Up to a third of the panes are knocked out before the rest go at once.
		var wanted = (int)((1f - HelmetFraction) * panes.Count * 0.34f);
		foreach ( var pane in nearest )
		{
			if ( panesPopped >= wanted ) break;
			Pop( pane, direction, 0.8f );
			Sfx.Play( "glass_hit", hit, 0.9f, Rng.Float( 1.2f, 1.5f ) );
		}
	}

	/// <summary> One pane comes away as a loose shard: it flies, tumbles, sinks slowly, bounces on the sea bed and fades. </summary>
	void Pop( Pane pane, Vector3 blow, float strength )
	{
		if ( pane.Gone || !IsInstanceValid( pane.Mesh ) ) return;
		pane.Gone = true;
		panesPopped++;
		var mesh = pane.Mesh;
		var world = mesh.GlobalTransform;
		var turn = world.Basis.Orthonormalized();
		var body = new RigidBody3D
		{
			Name = "Kraken Glass Shard",
			Mass = 0.6f,
			// Under water: it sinks slowly and the water drags on it.
			GravityScale = 0.4f,
			LinearDamp = 0.9f,
			AngularDamp = 0.8f,
			CollisionLayer = Layers.Debris,
			CollisionMask = Layers.World,
			ContinuousCd = true,
			PhysicsMaterialOverride = new PhysicsMaterial { Bounce = 0.3f, Friction = 0.7f },
		};
		body.AddToGroup( "kraken_glass" );
		GetTree().CurrentScene.AddChild( body );
		body.GlobalTransform = new Transform3D( turn, world.Origin );
		mesh.Reparent( body, true );
		mesh.MaterialOverride = looseGlass;
		// Its real outline at the Kraken's size (a body cannot be scaled).
		body.AddChild( new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = pane.Points.Select( p => p * Size ).ToArray() } } );
		var outward = (turn * pane.Out).Normalized();
		body.LinearVelocity = (outward * Rng.Float( 3f, 7f ) + blow.SafeNormal() * Rng.Float( 1f, 3.5f ) + Vector3.Up * Rng.Float( 0f, 2.5f )) * strength;
		body.AngularVelocity = Rng.InSphere( 9f );
		var tween = body.CreateTween();
		tween.TweenInterval( Rng.Float( 8f, 11f ) );
		tween.TweenProperty( mesh, "scale", mesh.Scale * 0.01f, 1.2f );
		tween.TweenCallback( Callable.From( body.QueueFree ) );
	}

	/// <summary> Every pane still on the dome is blown out at once. </summary>
	void BlowDome( Vector3 blow, float strength = 1.5f )
	{
		foreach ( var pane in panes.ToList() ) Pop( pane, blow, strength * Rng.Float( 0.7f, 1.3f ) );
	}

	// ---- Dying ----

	const int ChunkCount = 16;
	const float SwellTime = 0.55f;
	bool burst;
	TimeSince sinceThrash;

	/// <summary> Dead: it swells and shudders for half a second, bursts, and then only the sand it pushed up is left to settle. </summary>
	void Dying( float dt )
	{
		if ( !burst )
		{
			if ( !IsInstanceValid( rig ) ) { burst = true; return; }
			time += dt;
			var swell = MathX.Clamp( (float)timeSinceDeath / SwellTime, 0f, 1f );
			var shudder = Rng.InSphere( 0.12f * swell * Size );
			rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized().Scaled( Vector3.One * Size ), GlobalPosition + shudder );
			UpdateLife( dt );
			Animate( dt );
			if ( headPivot is not null && IsInstanceValid( headPivot ) ) headPivot.Scale = Vector3.One * (1f + 0.22f * swell * swell);
			foreach ( var mesh in glowing ) if ( IsInstanceValid( mesh ) && mesh.MaterialOverride is StandardMaterial3D m ) m.EmissionEnergyMultiplier = 1.5f + swell * 7f;
			if ( sinceThrash > 0.1f )
			{
				sinceThrash = 0f;
				for ( var k = 0; k < Tentacles; k++ ) Thrash( k, 260f );
				ShakeNear( 0.25f );
				gore.GooBurst( BrainPosition, Rng.OnSphere(), Goo, 6, 300f, 3f );
			}
			if ( timeSinceDeath >= SwellTime ) Burst();
			return;
		}
		// The sand settles, then it is all cleared away.
		var settle = MathX.Clamp( ((float)timeSinceDeath - SwellTime - 2f) / 2f, 0f, 1f );
		if ( IsInstanceValid( mound ) ) mound.Scale = Vector3.One * MathF.Max( 0.01f, 1f - Ease( settle ) );
		if ( settle >= 1f )
		{
			if ( IsInstanceValid( mound ) ) mound.QueueFree();
			QueueFree();
		}
	}

	/// <summary> The whole Kraken comes apart: dome, brain, collar, hide in chunks, every tentacle, and a lot of goo. </summary>
	void Burst()
	{
		burst = true;
		var head = parts[Head].Go;
		var frame = head.GlobalTransform;
		var core = frame * new Vector3( 0f, 1.9f, 0f );
		var away = lastShotDirection.SafeNormal();

		// The glow it built up while swelling goes out, so the loose pieces are not left blazing.
		foreach ( var mesh in glowing ) if ( IsInstanceValid( mesh ) && mesh.MaterialOverride is StandardMaterial3D m ) m.EmissionEnergyMultiplier = 1.1f;
		if ( HelmetOn ) ShatterHelmet( Vector3.Up );
		else BlowDome( Vector3.Up );
		for ( var k = 0; k < Tentacles; k++ )
		{
			if ( length[k] == 0 ) continue;
			var outward = (parts[Tentacle0 + k].Go.GlobalPosition - GlobalPosition).Flat().SafeNormal();
			CutAt( k, 0, outward, heavy: false, fling: 3.2f );
		}

		for ( var i = 0; i < ChunkCount; i++ )
		{
			var piece = new Node3D { Name = "Kraken Chunk" };
			GetTree().CurrentScene.AddChild( piece );
			piece.GlobalTransform = frame;
			var model = Model( $"chunk{i}" );
			piece.AddChild( model );
			Aabb? box = null;
			foreach ( var mesh in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
			{
				mesh.MaterialOverride = Paint( mesh.Name.ToString(), out _ );
				box = box is { } b ? b.Merge( mesh.GetAabb() ) : mesh.GetAabb();
			}
			if ( box is not { } bounds ) { piece.QueueFree(); continue; }
			var middle = frame * bounds.GetCenter();
			var radius = MathF.Max( 0.25f, bounds.Size.Length() * 0.22f * Size );
			var outward = (middle - core).SafeNormal();
			if ( outward == Vector3.Zero ) outward = Vector3.Up;
			var velocity = outward * Rng.Float( 6f, 12f ) + away * Rng.Float( 0f, 3f ) + Vector3.Up * Rng.Float( 2.5f, 6f );
			var gib = Gib.Attach( piece, gore, radius / U.In, 160f, 8f + radius * 25f, velocity, Goo, addCollider: false );
			var collider = new CollisionShape3D { Shape = new SphereShape3D { Radius = radius } };
			gib.AddChild( collider );
			collider.GlobalPosition = middle;
			gib.AngularVelocity = Rng.InSphere( 6f );
			CreatureGore.Twitch( gib, Rng.Float( 1.5f, 3f ), 0.25f );
			gore.GooBurst( middle, outward, Goo, 6, 420f, 5f );
		}

		// Goo everywhere.
		for ( var i = 0; i < 14; i++ ) gore.GooBurst( core, Rng.OnSphere() + Vector3.Up * 0.4f, i % 3 == 0 ? BrainPink : Goo, 12, 560f, Rng.Float( 4f, 8f ) );
		for ( var i = 0; i < 16; i++ )
		{
			var angle = i / 16f * MathF.Tau;
			gore.GooSpray( core, new Vector3( MathF.Cos( angle ), -0.25f, MathF.Sin( angle ) ), 2, Goo, 620f, 2.5f, 4.5f, sag: 0.4f );
		}
		for ( var i = 0; i < 10; i++ ) gore.ThrowBlob( core, Rng.OnSphere() + Vector3.Up * 0.5f, i % 2 == 0 ? Fold : Dark, Goo, Rng.Float( 6f, 11f ) );
		if ( gore.TraceWorld( core, core + Vector3.Down * 14f ) is { } floor ) gore.GooSplat( floor.Position, floor.Normal, 8f, Goo );
		gore.Squelch( core, 1.6f, 0.55f );
		Sfx.Play( "kraken_burst", core, 1f );
		SandBurst( GlobalPosition, 1.6f, 28, 3.5f );
		foreach ( var player in PlayerSurvivor.All )
		{
			var distance = player.WorldPosition.DistanceTo( core );
			if ( distance > 45f ) continue;
			player.Juice?.Shake( 1f - distance / 60f );
			if ( distance < 14f ) player.Afflictions?.Splattered( Goo, distance < 8f ? 8 : 4 );
		}
		rig.QueueFree();
	}
}
