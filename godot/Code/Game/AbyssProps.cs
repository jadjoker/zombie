using Godot;

/// <summary>
/// The Abyss's dressing, laid on a fixed seed so every machine has the same caves: glowing moss and dim lamps down the
/// main tunnels (brighter toward the hub, the way home), stalagmites by the walls, and scenery in the corridors:
/// treasure chests (<see cref="TreasureChest"/>; on the host and offline some are mimic octopuses instead), diver
/// skeletons in brass helmets slumped against the rock, bone piles, rusted anchors and a broken rowboat. Built
/// before the navmesh so enemies walk round what is solid. Metres.
/// </summary>
public static class AbyssProps
{
	public static readonly Color MossColour = new( 0.25f, 0.75f, 1f );
	public static int Chests, Mimics, Skeletons, Anchors, Boats, BonePiles, MossPatches, MossLights;

	static StandardMaterial3D bone, brass, glassDark, rust, plank;

	public static void Dress( Node3D root, bool joiner )
	{
		Chests = Mimics = Skeletons = Anchors = Boats = BonePiles = MossPatches = MossLights = 0;
		bone = new StandardMaterial3D { AlbedoColor = new Color( 0.82f, 0.78f, 0.66f ), Roughness = 0.8f };
		brass = new StandardMaterial3D { AlbedoColor = new Color( 0.62f, 0.45f, 0.2f ), Roughness = 0.45f, Metallic = 0.8f };
		glassDark = new StandardMaterial3D { AlbedoColor = new Color( 0.05f, 0.08f, 0.08f ), Roughness = 0.1f, Metallic = 0.3f };
		rust = new StandardMaterial3D { AlbedoColor = new Color( 0.36f, 0.2f, 0.12f ), Roughness = 0.85f, Metallic = 0.3f };
		plank = new StandardMaterial3D { AlbedoColor = new Color( 0.27f, 0.19f, 0.12f ), Roughness = 0.95f };
		var random = new Random( 5150 );
		var solid = new StaticBody3D { Name = "Cave Props Solid", CollisionLayer = Layers.World | Layers.Plant, CollisionMask = 0 };
		root.AddChild( solid );
		Moss( root, random );
		Stalagmites( root, solid, random );
		Scenery( root, solid, random, joiner );
		GD.Print( $"[abyss] props: {Chests} chests ({Mimics} mimics), {Skeletons} skeletons, {BonePiles} bone piles, {Anchors} anchors, {Boats} boats, {MossPatches} moss patches, {MossLights} moss lamps" );
	}

	/// <summary> How far a point is sideways from the nearest main tunnel's middle line (metres). </summary>
	public static float FromTunnel( Vector3 p )
	{
		var best = float.MaxValue;
		foreach ( var a in Abyss.MainTunnels )
		{
			var dir = new Vector3( MathF.Cos( MathX.Rad( a ) ), 0f, MathF.Sin( MathX.Rad( a ) ) );
			var along = p.Flat().Dot( dir );
			if ( along < 0f ) continue;
			best = MathF.Min( best, (p.Flat() - dir * along).Length() );
		}
		return best;
	}

	/// <summary> A spot on open floor in a cell, off the walls and the main tunnels' middles, or null. </summary>
	static Vector3? Spot( Random random, Vector3 centre, float wallMargin, float tunnelClear )
	{
		for ( var tries = 0; tries < 20; tries++ )
		{
			var p = centre + new Vector3( (float)(random.NextDouble() * 2 - 1) * 9f, 0f, (float)(random.NextDouble() * 2 - 1) * 9f );
			var r = p.Flat().Length();
			if ( r < Abyss.HubRadius + 2.5f || r > Abyss.Rings[3] - 2.5f ) continue;
			if ( Abyss.InWall( p, wallMargin ) || FromTunnel( p ) < tunnelClear ) continue;
			return p with { Y = SeaTerrain.HeightAt( p ) };
		}
		return null;
	}

	/// <summary> The way facing the nearest wall from a spot (for something slumped against it). </summary>
	static float YawToWall( Vector3 p )
	{
		var best = 0f;
		var near = float.MaxValue;
		for ( var i = 0; i < 16; i++ )
		{
			var a = i / 16f * MathF.Tau;
			var dir = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			for ( var d = 1f; d < 8f; d += 0.5f )
				if ( Abyss.InWall( p + dir * d, -0.6f ) ) { if ( d < near ) { near = d; best = a; } break; }
		}
		return MathF.Atan2( MathF.Cos( best ), MathF.Sin( best ) );
	}

	// ---- Moss down the main tunnels ----

	static void Moss( Node3D root, Random random )
	{
		var glow = new StandardMaterial3D { AlbedoColor = MossColour, EmissionEnabled = true, Emission = MossColour, EmissionEnergyMultiplier = 1.6f, Roughness = 0.6f };
		glow.SetMeta( "no_caustics", true );
		var blob = Clump( glow, 11 );
		var transforms = new List<Transform3D>();
		foreach ( var a in Abyss.MainTunnels )
		{
			var dir = new Vector3( MathF.Cos( MathX.Rad( a ) ), 0f, MathF.Sin( MathX.Rad( a ) ) );
			var side = dir.Cross( Vector3.Up );
			var lamp = 0;
			for ( var r = Abyss.HubRadius + 1f; r < Abyss.Rings[3] + 6f; r += 2.2f )
			{
				// Thicker toward the hub: follow it brighter to get home.
				var home = 1f - (r - Abyss.HubRadius) / (Abyss.Rings[3] - Abyss.HubRadius);
				foreach ( var s in new[] { -1f, 1f } )
				{
					var count = 1 + (int)(home * 3f) + random.Next( 0, 2 );
					for ( var k = 0; k < count; k++ )
					{
						var p = dir * (r + (float)random.NextDouble() * 2f) + side * s * (5.5f + (float)random.NextDouble() * 2.5f);
						if ( Abyss.InWall( p, -0.4f ) ) continue;
						p.Y = SeaTerrain.HeightAt( p ) + 0.02f;
						var size = 0.25f + (float)random.NextDouble() * 0.45f;
						transforms.Add( new Transform3D( new Basis( Vector3.Up, (float)random.NextDouble() * MathF.Tau ) * Basis.FromScale( Vector3.One * size * 1.4f ), p ) );
					}
				}
				// A dim teal lamp every so often, alternating sides.
				if ( (int)((r - Abyss.HubRadius) / 2.2f) % 6 == 3 )
				{
					var s = lamp++ % 2 == 0 ? 1f : -1f;
					var light = new OmniLight3D { Name = "Moss Glow", LightColor = MossColour, LightEnergy = 0.6f + home * 0.5f, OmniRange = 7f, OmniAttenuation = 1.3f, ShadowEnabled = false, LightVolumetricFogEnergy = 0.4f };
					root.AddChild( light );
					var at = dir * r + side * s * 6f;
					light.GlobalPosition = at with { Y = SeaTerrain.HeightAt( at ) + 0.6f };
					MossLights++;
				}
			}
		}
		// A few faint purple patches off in the side caves, so they aren't pitch black.
		var dim = new StandardMaterial3D { AlbedoColor = new Color( 0.55f, 0.3f, 0.9f ), EmissionEnabled = true, Emission = new Color( 0.5f, 0.25f, 0.95f ), EmissionEnergyMultiplier = 1.4f, Roughness = 0.6f };
		var dimBlob = Clump( dim, 12 );
		var side2 = new List<Transform3D>();
		foreach ( var c in Abyss.CellCentres )
			for ( var k = 0; k < 4; k++ )
				if ( Spot( random, c, -0.2f, 4f ) is { } p )
				{
					var size = 0.2f + (float)random.NextDouble() * 0.35f;
					side2.Add( new Transform3D( new Basis( Vector3.Up, (float)random.NextDouble() * MathF.Tau ) * Basis.FromScale( Vector3.One * size * 1.3f ), p ) );
				}
		MossPatches = transforms.Count + side2.Count;
		Batch( root, "Glow Moss", blob, transforms );
		Batch( root, "Side Moss", dimBlob, side2 );
	}

	/// <summary> A clump of glowing moss: little round cushions and a few taller nubs on stalks, about a metre across. </summary>
	static ArrayMesh Clump( Material material, int seed )
	{
		var random = new Random( seed );
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		var ball = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 7, Rings = 4 };
		for ( var i = 0; i < 7; i++ )
		{
			var a = R( 0f, MathF.Tau );
			var r = MathF.Sqrt( R( 0f, 1f ) ) * 0.45f;
			var size = R( 0.12f, 0.26f ) * (1.2f - r);
			// Cushions sat low, half sunk.
			st.AppendFrom( ball, 0, new Transform3D( Basis.FromScale( new Vector3( size, size * 0.6f, size ) ), new Vector3( MathF.Cos( a ) * r, size * 0.1f, MathF.Sin( a ) * r ) ) );
		}
		for ( var i = 0; i < 4; i++ )
		{
			var a = R( 0f, MathF.Tau );
			var r = R( 0f, 0.35f );
			var h = R( 0.12f, 0.3f );
			var at = new Vector3( MathF.Cos( a ) * r, 0f, MathF.Sin( a ) * r );
			st.AppendFrom( new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.02f, Height = h, RadialSegments = 4, Rings = 1, CapTop = false, CapBottom = false }, 0, new Transform3D( Basis.Identity, at + Vector3.Up * h * 0.5f ) );
			st.AppendFrom( ball, 0, new Transform3D( Basis.FromScale( Vector3.One * R( 0.05f, 0.08f ) ), at + Vector3.Up * h ) );
		}
		st.SetMaterial( material );
		var mesh = st.Commit();
		mesh.SurfaceSetMaterial( 0, material );
		return mesh;
	}

	static void Batch( Node3D root, string name, Mesh mesh, List<Transform3D> transforms )
	{
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = transforms.Count };
		for ( var i = 0; i < transforms.Count; i++ ) mm.SetInstanceTransform( i, transforms[i] );
		root.AddChild( new MultiMeshInstance3D { Name = name, Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
	}

	// ---- Stalagmites ----

	static void Stalagmites( Node3D root, StaticBody3D solid, Random random )
	{
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		var made = 0;
		for ( var i = 0; i < 160; i++ )
		{
			var c = Abyss.CellCentres[random.Next( Abyss.CellCentres.Count )];
			if ( Spot( random, c, -0.3f, 5f ) is not { } p ) continue;
			// Near a wall only (the corridors' middles stay open).
			if ( !Abyss.InWall( p, 1.8f ) ) continue;
			var clump = random.Next( 1, 4 );
			for ( var k = 0; k < clump; k++ )
			{
				var q = p + new Vector3( (float)(random.NextDouble() - 0.5) * 1.6f, 0f, (float)(random.NextDouble() - 0.5) * 1.6f );
				q.Y = SeaTerrain.HeightAt( q ) - 0.3f;
				var height = (0.8f + (float)random.NextDouble() * 2.6f) * (k == 0 ? 1f : 0.6f);
				var width = height * (0.22f + (float)random.NextDouble() * 0.1f);
				Abyss.Spike( st, q, Vector3.Up, height, width, random );
				if ( height > 1.2f ) solid.AddChild( new CollisionShape3D { Shape = new CylinderShape3D { Radius = width * 0.7f, Height = height * 0.7f }, Position = q + Vector3.Up * height * 0.35f } );
				made++;
			}
		}
		root.AddChild( new MeshInstance3D { Name = "Stalagmites", Mesh = st.Commit(), MaterialOverride = Abyss.RockPaint } );
		GD.Print( $"[abyss] {made} stalagmites" );
	}

	// ---- Scenery ----

	static void Scenery( Node3D root, StaticBody3D solid, Random random, bool joiner )
	{
		var cells = Abyss.CellCentres.ToList();
		var boats = 0;
		for ( var i = 0; i < cells.Count; i++ )
		{
			var c = cells[i];
			var items = 1 + random.Next( 0, 3 );
			for ( var k = 0; k < items; k++ )
			{
				var roll = random.NextDouble();
				if ( roll < 0.3 )
				{
					if ( Spot( random, c, 0.8f, 3.5f ) is not { } p ) continue;
					var yaw = YawToWall( p ) + MathF.PI;   // its front away from the wall
					// A share are mimics (the host's and offline; joiners get the host's octopus).
					if ( random.NextDouble() < TreasureChest.MimicShare )
					{
						if ( !joiner && root.GetParent() is Node3D level )
						{
							var m = MimicOctopus.Disguised( level, p, MimicOctopus.Disguise.Chest, 0f );
							m.Rotation = new Vector3( 0f, yaw, 0f );
							Mimics++;
						}
						continue;
					}
					TreasureChest.Place( root, p, yaw );
					Chests++;
				}
				else if ( roll < 0.6 )
				{
					if ( Spot( random, c, 0.3f, 3f ) is not { } p ) continue;
					DiverSkeleton( root, p, YawToWall( p ), random );
					Skeletons++;
				}
				else if ( roll < 0.8 )
				{
					if ( Spot( random, c, 0.2f, 2.5f ) is not { } p ) continue;
					BonePile( root, p, random );
					BonePiles++;
				}
				else if ( roll < 0.9 )
				{
					if ( Spot( random, c, 1.2f, 4f ) is not { } p ) continue;
					Anchor( root, solid, p, (float)random.NextDouble() * MathF.Tau, random );
					Anchors++;
				}
				else if ( boats < 3 && c.Flat().Length() > Abyss.Rings[1] )
				{
					if ( Spot( random, c, 2.5f, 6f ) is not { } p ) continue;
					Rowboat( root, solid, p, (float)random.NextDouble() * MathF.Tau );
					boats++;
					Boats++;
				}
			}
		}
	}

	/// <summary> A diver's skeleton in an old brass helmet, sat slumped against the rock, legs out, hands in its lap. </summary>
	static void DiverSkeleton( Node3D root, Vector3 at, float yawToWall, Random random )
	{
		var s = new Node3D { Name = "Diver Skeleton", Scale = Vector3.One * 1.2f };
		root.AddChild( s );
		// Its back to the wall: it faces away from it, set a little out from the rock.
		var away = new Vector3( MathF.Sin( yawToWall ), 0f, MathF.Cos( yawToWall ) );
		s.GlobalPosition = at;
		s.Rotation = new Vector3( 0f, yawToWall + MathF.PI + (float)(random.NextDouble() - 0.5) * 0.5f, 0f );
		_ = away;
		// Facing +Z, sitting: pelvis on the floor, back leant back against the wall behind (-Z).
		var lean = new Basis( Vector3.Right, -0.5f );
		var pelvis = new Vector3( 0f, 0.13f, 0f );
		SmoothKit.Ball( s, pelvis, new Vector3( 0.3f, 0.15f, 0.2f ), bone, null, "Pelvis" );
		var spineTop = pelvis + lean * new Vector3( 0f, 0.52f, 0f );
		for ( var i = 0; i < 7; i++ )
			SmoothKit.Ball( s, pelvis.Lerp( spineTop, i / 6f ), new Vector3( 0.07f, 0.05f, 0.07f ), bone, null, "Vertebra" );
		SmoothKit.Tube( s, pelvis, spineTop, 0.03f, 0.028f, bone, "Spine" );
		// The ribcage: hoops round from the spine to the breastbone.
		var sternumTop = spineTop + lean * new Vector3( 0f, -0.06f, 0.17f );
		var sternumLow = pelvis + lean * new Vector3( 0f, 0.26f, 0.16f );
		SmoothKit.Tube( s, sternumLow, sternumTop, 0.02f, 0.025f, bone, "Breastbone" );
		for ( var i = 0; i < 6; i++ )
		{
			var y = 0.24f + i * 0.045f;
			var w = 0.16f - MathF.Abs( i - 2.5f ) * 0.014f;
			foreach ( var side in new[] { -1f, 1f } )
			{
				var prev = pelvis + lean * new Vector3( 0f, y, -0.02f );
				for ( var k = 1; k <= 6; k++ )
				{
					var a = k / 6f * MathF.PI;
					var p = pelvis + lean * new Vector3( side * MathF.Sin( a ) * w, y - k * 0.008f, 0.08f - MathF.Cos( a ) * 0.1f );
					SmoothKit.Tube( s, prev, p, 0.013f, 0.013f, bone, "Rib" );
					prev = p;
				}
			}
		}
		// Collarbones and shoulder blades.
		foreach ( var side in new[] { -1f, 1f } )
			SmoothKit.Tube( s, sternumTop, spineTop + new Vector3( side * 0.18f, -0.03f, 0.02f ), 0.017f, 0.015f, bone, "Collarbone" );
		// The helmet, slumped forward and to a side, its corselet resting on the shoulders; a skull inside the front port.
		var helmet = new Node3D { Name = "Helmet", Position = spineTop + new Vector3( 0f, 0.16f, 0.05f ), Rotation = new Vector3( 0.4f, (float)(random.NextDouble() - 0.5) * 0.7f, (float)(random.NextDouble() - 0.5) * 0.45f ) };
		s.AddChild( helmet );
		SmoothKit.Ball( helmet, Vector3.Zero, new Vector3( 0.36f, 0.38f, 0.36f ), brass, null, "Dome" );
		SmoothKit.Ball( helmet, new Vector3( 0f, -0.17f, 0f ), new Vector3( 0.5f, 0.1f, 0.44f ), brass, null, "Corselet" );
		SmoothKit.Ball( helmet, new Vector3( 0f, -0.01f, 0.02f ), new Vector3( 0.22f, 0.24f, 0.24f ), bone, null, "Skull" );
		foreach ( var (dir, size) in new[] { (new Vector3( 0f, 0.02f, 1f ), 0.17f), (new Vector3( 1f, 0.05f, 0.1f ), 0.11f), (new Vector3( -1f, 0.05f, 0.1f ), 0.11f), (new Vector3( 0f, 1f, 0.2f ), 0.08f) } )
		{
			var d = dir.Normalized();
			var basis = Shapes.LookAt( -d ) * new Basis( Vector3.Right, MathF.PI * 0.5f );
			GunKit.Part( helmet, new TorusMesh { InnerRadius = size * 0.4f, OuterRadius = size * 0.56f, Rings = 16, RingSegments = 6 }, brass, d * 0.17f, basis, "Port Rim" );
			// The front port's glass is gone: the skull grins out of it. The side ones are fogged dark.
			if ( dir.Z < 0.9f ) GunKit.Part( helmet, new CylinderMesh { TopRadius = size * 0.42f, BottomRadius = size * 0.42f, Height = 0.02f, RadialSegments = 14, Rings = 1 }, glassDark, d * 0.175f, basis, "Port Glass" );
		}
		foreach ( var side in new[] { -1f, 1f } )
			SmoothKit.Ball( helmet, new Vector3( side * 0.045f, 0.01f, 0.115f ), new Vector3( 0.05f, 0.05f, 0.03f ), glassDark, null, "Eye Socket" );
		SmoothKit.Ball( helmet, new Vector3( 0f, -0.07f, 0.11f ), new Vector3( 0.1f, 0.04f, 0.03f ), glassDark, null, "Grin" );
		// Arms down, the hands in its lap; legs out along the floor, a little apart, boots toppled outward.
		foreach ( var side in new[] { -1f, 1f } )
		{
			var shoulder = spineTop + new Vector3( side * 0.19f, -0.04f, 0.02f );
			var elbow = shoulder + new Vector3( side * 0.06f, -0.27f, 0.02f );
			var hand = pelvis + new Vector3( side * 0.12f, 0.02f, 0.25f );
			SmoothKit.Tube( s, shoulder, elbow, 0.026f, 0.023f, bone, "Arm Bone" );
			SmoothKit.Ball( s, elbow, Vector3.One * 0.055f, bone, null, "Elbow" );
			SmoothKit.Tube( s, elbow, hand, 0.021f, 0.019f, bone, "Forearm Bone" );
			for ( var f = 0; f < 4; f++ )
				SmoothKit.Tube( s, hand, hand + new Vector3( side * (f - 1.5f) * 0.02f, -0.03f, 0.08f ), 0.008f, 0.006f, bone, "Finger" );
			var hip = pelvis + new Vector3( side * 0.11f, -0.02f, 0.04f );
			var knee = hip + new Vector3( side * 0.1f, 0.06f, 0.42f );
			var foot = knee + new Vector3( side * 0.07f, -0.07f, 0.4f );
			SmoothKit.Tube( s, hip, knee, 0.034f, 0.03f, bone, "Thigh Bone" );
			SmoothKit.Ball( s, knee, new Vector3( 0.07f, 0.065f, 0.07f ), bone, null, "Knee" );
			SmoothKit.Tube( s, knee, foot, 0.028f, 0.024f, bone, "Shin Bone" );
			// Heavy diving boots, toppled outward, brass toe caps.
			var boot = new Basis( Vector3.Back, side * 0.7f );
			SmoothKit.Ball( s, foot + new Vector3( side * 0.02f, 0.03f, 0.06f ), new Vector3( 0.14f, 0.13f, 0.27f ), plank, boot, "Boot" );
			SmoothKit.Ball( s, foot + new Vector3( side * 0.02f, 0.02f, 0.17f ), new Vector3( 0.12f, 0.09f, 0.09f ), brass, boot, "Toe Cap" );
		}
		// The air hose, perished, coiling off the helmet across the floor.
		var hose = GunKit.Paint( new Color( 0.15f, 0.13f, 0.11f ), 0.8f );
		var last = s.ToGlobal( spineTop + new Vector3( 0.15f, 0.1f, -0.1f ) );
		for ( var i = 1; i <= 8; i++ )
		{
			var a = i * 0.7f;
			var p = s.ToGlobal( new Vector3( 0.3f + MathF.Sin( a ) * 0.4f + i * 0.12f, 0.04f, -0.05f + MathF.Cos( a ) * 0.3f + i * 0.05f ) );
			p.Y = MathF.Max( p.Y, SeaTerrain.HeightAt( p ) + 0.04f );
			SmoothKit.Tube( s, s.ToLocal( last ), s.ToLocal( p ), 0.03f, 0.03f, hose, "Hose" );
			last = p;
		}
	}

	/// <summary> A heap of bones and a skull or two. </summary>
	static void BonePile( Node3D root, Vector3 at, Random random )
	{
		var pile = new Node3D { Name = "Bone Pile" };
		root.AddChild( pile );
		pile.GlobalPosition = at;
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		for ( var i = 0; i < 9; i++ )
		{
			var a = new Vector3( R( -0.5f, 0.5f ), R( 0.02f, 0.15f ), R( -0.5f, 0.5f ) );
			var dir = new Vector3( R( -1f, 1f ), R( -0.15f, 0.15f ), R( -1f, 1f ) ).Normalized();
			var length = R( 0.25f, 0.45f );
			SmoothKit.Tube( pile, a, a + dir * length, 0.022f, 0.02f, bone, "Bone" );
			SmoothKit.Ball( pile, a, Vector3.One * 0.06f, bone, null, "Knob" );
			SmoothKit.Ball( pile, a + dir * length, Vector3.One * 0.055f, bone, null, "Knob" );
		}
		for ( var i = 0; i < random.Next( 1, 3 ); i++ )
		{
			var skull = new Node3D { Name = "Skull", Position = new Vector3( R( -0.3f, 0.3f ), 0.1f, R( -0.3f, 0.3f ) ), Rotation = new Vector3( R( -0.3f, 0.3f ), R( 0f, MathF.Tau ), R( -0.3f, 0.3f ) ) };
			pile.AddChild( skull );
			SmoothKit.Ball( skull, Vector3.Zero, new Vector3( 0.17f, 0.17f, 0.2f ), bone, null, "Cranium" );
			SmoothKit.Ball( skull, new Vector3( 0f, -0.07f, 0.06f ), new Vector3( 0.11f, 0.07f, 0.1f ), bone, null, "Jaw" );
			foreach ( var side in new[] { -1f, 1f } )
				SmoothKit.Ball( skull, new Vector3( side * 0.04f, 0f, 0.085f ), new Vector3( 0.045f, 0.05f, 0.03f ), glassDark, null, "Socket" );
		}
	}

	/// <summary> A rusted ship's anchor lying on its side, its chain trailing off. </summary>
	static void Anchor( Node3D root, StaticBody3D solid, Vector3 at, float yaw, Random random )
	{
		var a = new Node3D { Name = "Anchor" };
		root.AddChild( a );
		a.GlobalPosition = at;
		a.Rotation = new Vector3( 0f, yaw, 0f );
		var lie = new Node3D { Rotation = new Vector3( 0f, 0f, 1.35f ), Position = new Vector3( 0f, 0.18f, 0f ) };
		a.AddChild( lie );
		SmoothKit.Tube( lie, new Vector3( 0f, -0.9f, 0f ), new Vector3( 0f, 1.1f, 0f ), 0.09f, 0.08f, rust, "Shank" );
		SmoothKit.Tube( lie, new Vector3( 0f, 0.95f, -0.8f ), new Vector3( 0f, 0.95f, 0.8f ), 0.06f, 0.06f, rust, "Stock" );
		GunKit.Part( lie, new TorusMesh { InnerRadius = 0.1f, OuterRadius = 0.18f, Rings = 12, RingSegments = 8 }, rust, new Vector3( 0f, 1.25f, 0f ), new Basis( Vector3.Back, MathF.PI * 0.5f ), "Ring" );
		foreach ( var side in new[] { -1f, 1f } )
		{
			var tip = new Vector3( side * 0.7f, -0.45f, 0f );
			SmoothKit.Tube( lie, new Vector3( 0f, -0.9f, 0f ), new Vector3( side * 0.45f, -0.85f, 0f ), 0.09f, 0.08f, rust, "Crown" );
			SmoothKit.Tube( lie, new Vector3( side * 0.45f, -0.85f, 0f ), tip, 0.08f, 0.05f, rust, "Arm" );
			SmoothKit.Ball( lie, tip, new Vector3( 0.22f, 0.3f, 0.06f ), rust, new Basis( Vector3.Back, -side * 0.5f ), "Fluke" );
		}
		solid.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3( 2.2f, 0.5f, 1.6f ) }, Transform = new Transform3D( new Basis( Vector3.Up, yaw ), at + Vector3.Up * 0.2f ) } );
		_ = random;
	}

	/// <summary> A small wooden rowboat, half sunk in the silt and broken open, planks gone from one side. </summary>
	static void Rowboat( Node3D root, StaticBody3D solid, Vector3 at, float yaw )
	{
		var b = new Node3D { Name = "Rowboat" };
		root.AddChild( b );
		b.GlobalPosition = at + Vector3.Down * 0.2f;
		b.Rotation = new Vector3( 0.1f, yaw, 0.22f );
		const float length = 4.2f, beam = 1.5f, depth = 0.8f;
		// The hull: planked rows from the keel up to the gunwale, each a strip of quads, some missing on one side.
		var wood = new StandardMaterial3D { AlbedoColor = new Color( 0.3f, 0.21f, 0.13f ), Roughness = 0.95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		Vector3 Hull( float t, float u, float side )
		{
			// t along (0 bow, 1 stern), u up from keel (0) to gunwale (1).
			var bow = MathF.Pow( MathF.Abs( t * 2f - 1f ), 2.4f );
			var half = beam * 0.5f * MathF.Sqrt( MathF.Max( 0f, 1f - bow ) ) * MathF.Sin( u * MathF.PI * 0.5f + 0.15f );
			var y = depth * (u + 0.25f * bow * (1f - u));
			return new Vector3( side * half, y, (t - 0.5f) * length );
		}
		const int rows = 6, cols = 18;
		var rnd = new Random( 77 );
		foreach ( var side in new[] { -1f, 1f } )
			for ( var r = 0; r < rows; r++ )
			{
				// A gap between planks: each row a little narrower than its band.
				var u0 = r / (float)rows + 0.008f;
				var u1 = (r + 1) / (float)rows - 0.008f;
				var shade = 0.85f + (float)rnd.NextDouble() * 0.3f;
				st.SetColor( new Color( shade, shade, shade ) );
				for ( var c = 0; c < cols; c++ )
				{
					var t0 = c / (float)cols;
					var t1 = (c + 1) / (float)cols;
					// The breach: planks stove in on one side, amidships.
					if ( side > 0f && r is 2 or 3 && c > 6 && c < 12 ) continue;
					var a = Hull( t0, u0, side ); var bq = Hull( t1, u0, side ); var cq = Hull( t1, u1, side ); var d = Hull( t0, u1, side );
					st.AddVertex( a ); st.AddVertex( bq ); st.AddVertex( cq );
					st.AddVertex( a ); st.AddVertex( cq ); st.AddVertex( d );
				}
			}
		st.GenerateNormals();
		var hull = new MeshInstance3D { Name = "Hull", Mesh = st.Commit(), MaterialOverride = wood };
		b.AddChild( hull );
		// Ribs inside, the keel, the gunwales, thwarts and a broken oar.
		for ( var i = 1; i < 8; i++ )
		{
			var t = i / 8f;
			var prev = Hull( t, 0f, -1f );
			for ( var k = 1; k <= 8; k++ )
			{
				var u = k / 4f - 1f;
				var p = Hull( t, MathF.Abs( u ), MathF.Sign( u ) == 0 ? 1f : MathF.Sign( u ) );
				SmoothKit.Tube( b, prev + Vector3.Up * 0.03f, p + Vector3.Up * 0.03f, 0.03f, 0.03f, plank, "Rib" );
				prev = p;
			}
		}
		SmoothKit.Tube( b, Hull( 0.02f, 0f, 1f ), Hull( 0.98f, 0f, 1f ), 0.06f, 0.06f, plank, "Keel" );
		foreach ( var side in new[] { -1f, 1f } )
			for ( var i = 0; i < 10; i++ )
				SmoothKit.Tube( b, Hull( i / 10f, 1f, side ), Hull( (i + 1) / 10f, 1f, side ), 0.045f, 0.045f, plank, "Gunwale" );
		foreach ( var t in new[] { 0.3f, 0.52f, 0.72f } )
		{
			var w = Hull( t, 0.8f, 1f ).X * 2f;
			GunKit.Part( b, new BoxMesh { Size = new Vector3( w, 0.06f, 0.26f ) }, plank, new Vector3( 0f, depth * 0.8f, (t - 0.5f) * length ), null, "Thwart" );
		}
		SmoothKit.Tube( b, new Vector3( 0.4f, depth * 0.85f, -1.3f ), new Vector3( 1.6f, 0.1f, 0.9f ), 0.035f, 0.035f, plank, "Oar" );
		GunKit.Part( b, new BoxMesh { Size = new Vector3( 0.16f, 0.03f, 0.5f ) }, plank, new Vector3( 1.75f, 0.06f, 1.15f ), new Basis( Vector3.Up, 0.5f ), "Oar Blade" );
		solid.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3( 1.4f, 0.8f, 3.8f ) }, Transform = new Transform3D( new Basis( Vector3.Up, yaw ), at + Vector3.Up * 0.3f ) } );
	}
}
