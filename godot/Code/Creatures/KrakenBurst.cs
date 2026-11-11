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
		// Open for the suck, to spit ink, and for a moment at each bite.
		var open = Attacking == Attack.Suck || (Attacking == Attack.Ink && attackTime < InkWindTime + 0.35f) || Biting;
		var want = open && !IsDead ? 1f : 0f;
		mouthOpen = MathX.Approach( mouthOpen, want, dt * (want > mouthOpen ? (Biting ? 12f : 5f) : 3.5f) );
		mouthNode.Visible = mouthOpen > 0.02f;
		if ( !mouthNode.Visible ) return;
		// It pushes out with a little overshoot, and quivers while it pulls.
		var t = mouthOpen - 1f;
		var wide = 1f + 2.7f * t * t * t + 1.7f * t * t;
		var quiver = 1f + MathF.Sin( time * 26f ) * 0.05f * mouthOpen;
		mouthNode.Scale = new Vector3( wide * quiver, wide * quiver, 0.45f + 0.55f * wide );
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

	const float SwellTime = 0.7f;
	bool burst;
	TimeSince sinceThrash;
	Vector3 burstAt;

	/// <summary> For tests: loose pieces the burst threw out (chunks, teeth, guts, lumps), not counting tentacles. </summary>
	public static int LastBurstPieces { get; private set; }

	/// <summary> Dead: it swells and shudders, bursts, goo rains down for a while, and then the sand it pushed up settles. </summary>
	void Dying( float dt )
	{
		if ( !burst )
		{
			if ( !IsInstanceValid( rig ) ) { burst = true; return; }
			time += dt;
			var swell = MathX.Clamp( (float)timeSinceDeath / SwellTime, 0f, 1f );
			var shudder = Rng.InSphere( 0.14f * swell * Size );
			rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized().Scaled( Vector3.One * Size ), GlobalPosition + shudder );
			UpdateLife( dt );
			Animate( dt );
			UpdateWounds();
			// It bulges unevenly, as if something inside is pushing out.
			if ( headPivot is not null && IsInstanceValid( headPivot ) )
				headPivot.Scale = new Vector3( 1f + 0.26f * swell * swell + MathF.Sin( time * 37f ) * 0.05f * swell, 1f + 0.16f * swell * swell + MathF.Sin( time * 29f ) * 0.05f * swell, 1f + 0.26f * swell * swell + MathF.Sin( time * 43f ) * 0.05f * swell );
			foreach ( var mesh in glowing ) if ( IsInstanceValid( mesh ) && mesh.MaterialOverride is StandardMaterial3D m ) m.EmissionEnergyMultiplier = 1.5f + swell * 7f;
			if ( sinceThrash > 0.08f )
			{
				sinceThrash = 0f;
				for ( var k = 0; k < Tentacles; k++ ) Thrash( k, 280f );
				ShakeNear( 0.3f );
				// Seams split and squirt before it goes.
				var seam = parts[Head].Go.GlobalTransform * (new Vector3( 0f, 1.9f, 0f ) + Rng.OnSphere() * 0.9f);
				gore.GooBurst( seam, seam - GlobalPosition, Goo, 8, 380f, 3.5f );
				if ( Rng.Float() < 0.5f ) gore.ThrowBlob( seam, seam - GlobalPosition, Rng.Float() < 0.5f ? Skin : Flesh, Goo, Rng.Float( 3f, 6f ) );
			}
			if ( timeSinceDeath >= SwellTime ) Burst();
			return;
		}
		// Goo and bits keep raining down round where it stood.
		var since = (float)timeSinceDeath - SwellTime;
		if ( since < 2.6f )
		{
			for ( var i = 0; i < 2; i++ )
			{
				var over = burstAt + Rng.InSphere( 15f ).Flat() + Vector3.Up * Rng.Float( 5f, 10f );
				GooDroplet.Spawn( over, Vector3.Down * Rng.Float( 4f, 9f ) + Rng.InSphere( 1.5f ), Rng.Float() < 0.2f ? Flesh : Goo, Rng.Float( 2f, 4.5f ), 2.5f );
			}
		}
		var settle = MathX.Clamp( (since - 2.5f) / 2f, 0f, 1f );
		if ( IsInstanceValid( mound ) ) mound.Scale = Vector3.One * MathF.Max( 0.01f, 1f - Ease( settle ) );
		if ( settle >= 1f )
		{
			if ( IsInstanceValid( mound ) ) mound.QueueFree();
			QueueFree();
		}
	}

	/// <summary>
	/// The whole Kraken comes apart (playtest round 4: nastier, smaller, farther): every chunk of its hide, the
	/// collar, the brain, its teeth, ropes of gut, lumps of flesh and every tentacle in short lengths. Some pieces
	/// burst again in the air, and goo goes everywhere.
	/// </summary>
	void Burst()
	{
		burst = true;
		var head = parts[Head].Go;
		var frame = head.GlobalTransform;
		var core = frame * new Vector3( 0f, 1.9f, 0f );
		var away = lastShotDirection.SafeNormal();
		var mouth = MouthPosition;
		burstAt = core;
		gore.MaxGibs = Math.Max( gore.MaxGibs, 280 );   // room for all of it at once
		var pieces = 0;

		// The glow it built up while swelling goes out, so the loose pieces are not left blazing.
		foreach ( var mesh in glowing ) if ( IsInstanceValid( mesh ) && mesh.MaterialOverride is StandardMaterial3D m ) m.EmissionEnergyMultiplier = 1.1f;
		if ( HelmetOn ) ShatterHelmet( Vector3.Up );
		else BlowDome( Vector3.Up );
		for ( var k = 0; k < Tentacles; k++ )
		{
			if ( length[k] == 0 ) continue;
			var outward = (parts[Tentacle0 + k].Go.GlobalPosition - GlobalPosition).Flat().SafeNormal();
			CutAt( k, 0, outward, heavy: false, fling: 5.5f, snap: 0.55f );
		}

		// Its hide, collar and brain: every chunk from where it sat, thrown hard. Some burst again on the way.
		for ( var i = 0; ; i++ )
		{
			if ( !ResourceLoader.Exists( $"res://assets/models/creatures/kraken/chunk{i}.glb" ) ) break;
			if ( SpawnChunk( i, frame, null, 120f ) is not { } chunk ) continue;
			pieces++;
			var outward = (chunk.Middle - core).SafeNormal();
			if ( outward == Vector3.Zero ) outward = Vector3.Up;
			chunk.Gib.LinearVelocity = outward * Rng.Float( 11f, 24f ) + away * Rng.Float( 0f, 4f ) + Vector3.Up * Rng.Float( 3f, 9f );
			chunk.Gib.AngularVelocity = Rng.InSphere( 10f );
			gore.GooBurst( chunk.Middle, outward, Goo, 5, 460f, 5f );
			if ( Rng.Float() < 0.4f ) PopLater( chunk.Gib, Rng.Float( 0.35f, 1.1f ) );
		}

		// Teeth, spinning off like shrapnel.
		for ( var i = 0; i < 14; i++ )
		{
			var tooth = new Node3D { Name = "Kraken Tooth" };
			GetTree().CurrentScene.AddChild( tooth );
			tooth.GlobalPosition = mouth + Rng.InSphere( 0.6f );
			tooth.AddChild( new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.14f, Height = 0.55f, RadialSegments = 8 }, MaterialOverride = Shapes.Material( new Color( 0.93f, 0.9f, 0.78f ) ) } );
			var gib = Gib.Attach( tooth, gore, 4f, 30f, 0.5f, (Rng.OnSphere() + Vector3.Up * 0.6f).Normalized() * Rng.Float( 9f, 20f ), Goo );
			gib.AngularVelocity = Rng.InSphere( 20f );
			pieces++;
		}

		// Ropes of gut: links pinned end to end, so they whip and flop as they fly and drape where they land.
		for ( var rope = 0; rope < 5; rope++ )
		{
			var start = core + Rng.InSphere( 0.8f );
			var along = (Rng.OnSphere() + Vector3.Up * 0.3f).Normalized();
			var throwIt = (Rng.OnSphere() + Vector3.Up * 0.7f).Normalized() * Rng.Float( 8f, 17f );
			var color = rope % 2 == 0 ? Flesh : new Color( 0.85f, 0.45f, 0.5f );
			Gib previous = null;
			var links = Rng.Int( 6, 9 );
			for ( var i = 0; i < links; i++ )
			{
				var link = new Node3D { Name = "Kraken Gut" };
				GetTree().CurrentScene.AddChild( link );
				link.GlobalTransform = new Transform3D( Shapes.LookAt( along ), start + along * i * 0.42f );
				var fat = Rng.Float( 0.17f, 0.24f );
				link.AddChild( new MeshInstance3D { Mesh = Shapes.Sphere, Scale = new Vector3( fat * 2f, fat * 2f, 0.62f ), MaterialOverride = Shapes.Material( color ) } );
				var gib = Gib.Attach( link, gore, fat / U.In, 40f, 1.2f, throwIt + Rng.InSphere( 2.5f ), Goo );
				gib.AngularVelocity = Rng.InSphere( 3f );
				if ( previous is not null )
				{
					var joint = new PinJoint3D { Name = "Joint" };
					previous.AddChild( joint );
					joint.GlobalPosition = (previous.GlobalPosition + gib.GlobalPosition) * 0.5f;
					joint.NodeA = previous.GetPath();
					joint.NodeB = gib.GetPath();
				}
				previous = gib;
				pieces++;
			}
		}

		// Lumps of flesh, hide and brain, thrown wide.
		for ( var i = 0; i < 30; i++ )
		{
			var color = (i % 4) switch { 0 => Skin, 1 => Flesh, 2 => BrainPink, _ => Dark };
			var lump = gore.ThrowBlob( core + Rng.InSphere( 1.2f ), Rng.OnSphere() + Vector3.Up * 0.5f, color, Goo, Rng.Float( 3.5f, 10f ) );
			if ( lump is null ) continue;
			lump.LinearVelocity *= Rng.Float( 2.5f, 4.5f );
			pieces++;
		}
		LastBurstPieces = pieces;

		// Goo everywhere: a blast of it, a mist, streaks out across the sea bed, and a pool where it stood.
		for ( var i = 0; i < 18; i++ ) gore.GooBurst( core, Rng.OnSphere() + Vector3.Up * 0.4f, i % 3 == 0 ? BrainPink : Goo, 12, 640f, Rng.Float( 4f, 9f ) );
		GooMist( core );
		for ( var i = 0; i < 20; i++ )
		{
			var angle = i / 20f * MathF.Tau + Rng.Float( -0.15f, 0.15f );
			gore.GooSpray( core, new Vector3( MathF.Cos( angle ), -0.2f, MathF.Sin( angle ) ), 2, Goo, 720f, 2.5f, 5f, sag: 0.35f );
			var reach = new Vector3( MathF.Cos( angle ), 0f, MathF.Sin( angle ) ) * Rng.Float( 4f, 15f );
			if ( gore.TraceWorld( GlobalPosition + reach + Vector3.Up * 3f, GlobalPosition + reach + Vector3.Down * 6f ) is { } spot )
				gore.GooSplat( spot.Position, spot.Normal, Rng.Float( 2.5f, 6f ), Goo );
		}
		if ( gore.TraceWorld( core, core + Vector3.Down * 14f ) is { } floor ) gore.GooSplat( floor.Position, floor.Normal, 10f, Goo );
		gore.Squelch( core, 1.6f, 0.55f );
		Sfx.Play( "kraken_burst", core, 1f );
		Sfx.Play( "gib", core, 1f, 0.5f );
		SandBurst( GlobalPosition, 1.6f, 28, 3.5f );
		foreach ( var player in PlayerSurvivor.All )
		{
			var distance = player.WorldPosition.DistanceTo( core );
			if ( distance > 50f ) continue;
			player.Juice?.Shake( 1f - distance / 70f );
			if ( distance < 26f ) player.Afflictions?.Splattered( Goo, distance < 12f ? 10 : 5 );
		}
		// A beat of slow motion to take it in (left alone if the dev menu has changed the speed).
		if ( Engine.TimeScale == 1.0 )
		{
			Engine.TimeScale = 0.3;
			GetTree().CreateTimer( 0.4, true, false, true ).Timeout += () => { if ( Engine.TimeScale == 0.3 ) Engine.TimeScale = 1.0; };
		}
		rig.QueueFree();
	}

	/// <summary> A thrown piece bursts again after a moment: goo, and smaller lumps carrying on the way it was going. </summary>
	void PopLater( Gib gib, float seconds )
	{
		GetTree().CreateTimer( seconds ).Timeout += () =>
		{
			if ( !IsInstanceValid( gib ) || gib.IsFading ) return;
			var at = gib.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.GlobalPosition ?? gib.GlobalPosition;
			var going = gib.LinearVelocity;
			for ( var i = 0; i < 3; i++ )
			{
				var lump = GoreSystem.Get().ThrowBlob( at, going + Rng.InSphere( 3f ), i == 0 ? Skin : Flesh, Goo, Rng.Float( 3f, 6f ) );
				if ( lump is not null ) lump.LinearVelocity = going * Rng.Float( 0.5f, 1.1f ) + Rng.InSphere( 5f );
			}
			gib.Pulverize( at, going.SafeNormal() );
		};
	}

	/// <summary> A cloud of fine goo hanging in the water where it burst. </summary>
	void GooMist( Vector3 at )
	{
		var material = new StandardMaterial3D { AlbedoColor = new Color( 0.3f, 0.95f, 0.3f, 0.75f ), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
		var fade = new Gradient();
		fade.SetColor( 0, Colors.White );
		fade.SetColor( 1, new Color( 1f, 1f, 1f, 0f ) );
		var mist = new CpuParticles3D
		{
			Name = "Kraken Mist",
			Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 6, Rings = 3, Material = material },
			Amount = 160,
			Lifetime = 2.2f,
			OneShot = true,
			Explosiveness = 1f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = 1.5f,
			Direction = Vector3.Up,
			Spread = 180f,
			InitialVelocityMin = 5f,
			InitialVelocityMax = 22f,
			Gravity = new Vector3( 0f, -2.5f, 0f ),
			DampingMin = 3f,
			DampingMax = 5f,
			ScaleAmountMin = 0.6f,
			ScaleAmountMax = 2.2f,
			ColorRamp = fade,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		GetTree().CurrentScene.AddChild( mist );
		mist.GlobalPosition = at;
		mist.Emitting = true;
		Fx.Expire( mist, 3.5f );
	}
}
