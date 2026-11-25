using Godot;

/// <summary>
/// The hermit crab's shells (user's list): a toilet bowl, a diving helmet, a garbage can, a recycling bin, a tea cup,
/// a disco ball and a milk carton, built in code. The toilet, can, bin, cup and carton are worn upside down over the crab
/// (`SB.UpsideDown`), so it looks like it lives in them rather than carrying them. Each is cut into thick pieces with jagged edges (tiles of
/// its surfaces, plus its lid, handles and such), so it can crumble a piece at a time and fall apart like the Kraken's
/// dome. Built once per kind and shared (each crab gets its own materials, so its flashes are its own). Metres in the
/// shell's space: up +Y, the crab's front -Z, sitting on its back at Y 0.
/// </summary>
public partial class HermitCrab
{
	public enum ShellKind { Toilet, DivingHelmet, GarbageCan, RecyclingBin, TeaCup, DiscoBall, MilkCarton }
	public const int ShellCount = 7;
	/// <summary> The shells are drawn this much bigger than they are built (they have to read on a crab this size). </summary>
	public const float ShellScale = 1.8f;

	/// <summary> What a shell's pieces are made of: how they float or sink, the sound when it gives way, the drips. </summary>
	sealed record ShellLook( GibKind Stuff, string Breaks, float Pitch, Color Goo );
	ShellLook Look = new( GibKind.Shell, "glass_break", 1f, new Color( 0.8f, 0.8f, 0.8f ) );

	sealed class Frag
	{
		public Node3D Node;
		public Vector3 Middle;
		public float Reach;
		public readonly List<MeshInstance3D> Meshes = new();
		public bool Gone;
	}

	readonly List<Frag> frags = new();
	Node3D shellRoot;
	Vector3 shellMiddle;
	bool spins;

	// ---- Shared templates ----

	sealed class Piece { public Mesh Mesh; public Transform3D Local; public int Mat; }
	sealed class FragT { public Vector3 At; public float Reach; public readonly List<Piece> Pieces = new(); }
	sealed class Template
	{
		public readonly List<FragT> Frags = new();
		public readonly List<(Func<Material> Make, bool Tint, Color Colour)> Mats = new();
		public Basis Tilt = Basis.Identity;
	}

	static readonly Dictionary<ShellKind, Template> templates = new();

	void BuildShell( Part shell )
	{
		Look = Shell switch
		{
			ShellKind.Toilet => new( GibKind.Glass, "glass_break", 0.9f, new Color( 0.55f, 0.75f, 0.95f ) ),
			ShellKind.DivingHelmet => new( GibKind.Armour, "magnet_clang", 0.8f, new Color( 0.85f, 0.65f, 0.3f ) ),
			ShellKind.GarbageCan => new( GibKind.Armour, "magnet_clang", 1.15f, new Color( 0.45f, 0.5f, 0.3f ) ),
			ShellKind.RecyclingBin => new( GibKind.Shell, "hit_world", 0.8f, new Color( 0.3f, 0.5f, 0.95f ) ),
			ShellKind.TeaCup => new( GibKind.Glass, "glass_break", 1.35f, new Color( 0.6f, 0.35f, 0.15f ) ),
			ShellKind.DiscoBall => new( GibKind.Glass, "glass_break", 1.15f, new Color( 0.85f, 0.85f, 1f ) ),
			_ => new( GibKind.Flesh, "splash", 1.1f, new Color( 0.98f, 0.98f, 0.96f ) ),
		};
		spins = Shell == ShellKind.DiscoBall;
		if ( !templates.TryGetValue( Shell, out var t ) ) templates[Shell] = t = MakeTemplate( Shell );
		shellRoot = new Node3D { Name = $"Shell {Shell}" };
		shell.Go.AddChild( shellRoot );
		shellRoot.Basis = t.Tilt.Scaled( Vector3.One * ShellScale );
		var mats = t.Mats.Select( m => m.Make() ).ToList();
		var lo = Vector3.One * float.MaxValue;
		var hi = Vector3.One * float.MinValue;
		foreach ( var ft in t.Frags )
		{
			var node = new Node3D { Name = "Shell Piece", Position = ft.At };
			shellRoot.AddChild( node );
			var f = new Frag { Node = node, Reach = ft.Reach };
			foreach ( var p in ft.Pieces )
			{
				var mi = new MeshInstance3D { Name = "Shape", Mesh = p.Mesh, MaterialOverride = mats[p.Mat], Transform = p.Local };
				node.AddChild( mi );
				f.Meshes.Add( mi );
				if ( t.Mats[p.Mat].Tint ) shell.Renderers.Add( (mi, t.Mats[p.Mat].Colour) );
			}
			frags.Add( f );
			lo = lo.Min( ft.At - Vector3.One * ft.Reach );
			hi = hi.Max( ft.At + Vector3.One * ft.Reach );
		}
		shellMiddle = (lo + hi) * 0.5f;
		// One capsule round it all, along its longest side.
		var size = (hi - lo) * 0.9f;
		var along = size.X >= size.Y && size.X >= size.Z ? Vector3.Right : size.Y >= size.Z ? Vector3.Up : Vector3.Back;
		var length = size.Dot( along );
		var across = along == Vector3.Right ? MathF.Max( size.Y, size.Z ) : along == Vector3.Up ? MathF.Max( size.X, size.Z ) : MathF.Max( size.X, size.Y );
		var radius = across * 0.5f;
		var capsule = new CapsuleShape3D { Radius = radius, Height = MathF.Max( length, radius * 2f + 0.01f ) };
		capsule.Radius *= ShellScale;
		capsule.Height *= ShellScale;
		shell.Go.AddChild( new CollisionShape3D { Name = "Shell Hit", Shape = capsule, Transform = new Transform3D( t.Tilt * new Basis( new Quaternion( Vector3.Up, along ) ), t.Tilt * shellMiddle * ShellScale ) } );
	}

	public override void _Process( double delta )
	{
		base._Process( delta );
		if ( spins && shellRoot is not null && IsInstanceValid( shellRoot ) ) shellRoot.RotateY( (float)delta * 0.9f );
	}

	// ---- The builder ----

	sealed class SB
	{
		public readonly Template T = new();
		readonly Random random;
		public SB( int seed ) => random = new Random( seed );
		public float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);

		public int Mat( Func<Material> make, bool tint = true, Color? colour = null )
		{
			T.Mats.Add( (make, tint, colour ?? Colors.White) );
			return T.Mats.Count - 1;
		}

		/// <summary> A piece on its own (a lid, a panel, the tea). </summary>
		public FragT Solid( Mesh mesh, Transform3D at, int mat, float reach )
		{
			var f = new FragT { At = at.Origin, Reach = reach };
			f.Pieces.Add( new Piece { Mesh = mesh, Local = new Transform3D( at.Basis, Vector3.Zero ), Mat = mat } );
			T.Frags.Add( f );
			return f;
		}

		/// <summary> A detail stuck on the nearest piece (it comes off with that piece). </summary>
		public void Extra( Mesh mesh, Transform3D at, int mat )
		{
			FragT best = null;
			var bestD = float.MaxValue;
			foreach ( var f in T.Frags )
			{
				var d = f.At.DistanceTo( at.Origin );
				if ( d < bestD ) { bestD = d; best = f; }
			}
			if ( best is null ) { Solid( mesh, at, mat, 0.05f ); return; }
			best.Pieces.Add( new Piece { Mesh = mesh, Local = new Transform3D( at.Basis, at.Origin - best.At ), Mat = mat } );
		}

		/// <summary>
		/// A surface P(u, v) cut into nu by nv thick tiles, their shared edges wavy (`jag`), each its own piece: the outside
		/// coloured by `outer`, the inside (what a bowl shows, or a broken face) by `inner`. Normals point away from `core`
		/// when given, else by the sign of dP/dv x dP/du. `gap` shrinks each tile (a disco ball's mirrors).
		/// </summary>
		public void Patch( Func<float, float, Vector3> P, int nu, int nv, Func<float, float, Color> outer, Func<float, float, Color> inner, int mat, float thick,
			bool wrapU = false, bool wrapV = false, float jag = 0.22f, float gap = 0f, int sub = 3, Func<float, float, Vector3> core = null, float sign = 1f )
		{
			var phaseU = R( 0f, 10f );
			var phaseV = R( 0f, 10f );
			float BaseU( int k ) => k / (float)nu;
			float BaseV( int k ) => k / (float)nv;
			float BU( int k, float v )
			{
				if ( jag <= 0f || (!wrapU && (k == 0 || k == nu)) ) return BaseU( k );
				var kk = wrapU ? k % nu : k;
				return BaseU( k ) + jag / nu * (0.6f * MathF.Sin( v * 9f + kk * 2.3f + phaseU ) + 0.4f * MathF.Sin( v * 23f + kk * 1.1f ));
			}
			float BV( int k, float u )
			{
				if ( jag <= 0f || (!wrapV && (k == 0 || k == nv)) ) return BaseV( k );
				var kk = wrapV ? k % nv : k;
				return BaseV( k ) + jag / nv * (0.6f * MathF.Sin( u * 11f + kk * 1.9f + phaseV ) + 0.4f * MathF.Sin( u * 27f + kk * 0.7f ));
			}
			Vector3 Normal( float u, float v, Vector3 at )
			{
				const float e = 1e-3f;
				var pu = P( MathX.Clamp( u + e, 0f, 1f ), v ) - P( MathX.Clamp( u - e, 0f, 1f ), v );
				var pv = P( u, MathX.Clamp( v + e, 0f, 1f ) ) - P( u, MathX.Clamp( v - e, 0f, 1f ) );
				var n = pv.Cross( pu );
				if ( n.LengthSquared() < 1e-14f ) n = core is not null ? at - core( u, v ) : Vector3.Up;
				n = n.Normalized() * sign;
				if ( core is not null && n.Dot( at - core( u, v ) ) < 0f ) n = -n;
				return n;
			}
			for ( var iu = 0; iu < nu; iu++ )
			for ( var iv = 0; iv < nv; iv++ )
			{
				var n1 = sub + 1;
				var O = new Vector3[n1, n1];
				var I = new Vector3[n1, n1];
				var N = new Vector3[n1, n1];
				var CO = new Color[n1, n1];
				var CI = new Color[n1, n1];
				var centre = Vector3.Zero;
				for ( var i = 0; i < n1; i++ )
				for ( var j = 0; j < n1; j++ )
				{
					var s = i / (float)sub;
					var t = j / (float)sub;
					var uLin = MathX.Lerp( BaseU( iu ), BaseU( iu + 1 ), s );
					var v = MathX.Clamp( MathX.Lerp( BV( iv, uLin ), BV( iv + 1, uLin ), t ), 0f, 1f );
					var u = MathX.Clamp( MathX.Lerp( BU( iu, v ), BU( iu + 1, v ), s ), wrapU ? -1f : 0f, wrapU ? 2f : 1f );
					var uw = wrapU ? ((u % 1f) + 1f) % 1f : u;
					var o = P( uw, v );
					var n = Normal( uw, v, o );
					O[i, j] = o;
					N[i, j] = n;
					I[i, j] = o - n * thick;
					CO[i, j] = outer( uw, v );
					CI[i, j] = inner( uw, v );
					centre += o + I[i, j];
				}
				centre /= n1 * n1 * 2;
				var reach = 0f;
				for ( var i = 0; i < n1; i++ )
				for ( var j = 0; j < n1; j++ )
				{
					if ( gap > 0f ) { O[i, j] = centre + (O[i, j] - centre) * (1f - gap); I[i, j] = centre + (I[i, j] - centre) * (1f - gap); }
					reach = MathF.Max( reach, O[i, j].DistanceTo( centre ) );
				}
				var st = new SurfaceTool();
				st.Begin( Mesh.PrimitiveType.Triangles );
				for ( var i = 0; i < sub; i++ )
				for ( var j = 0; j < sub; j++ )
				{
					var face = (N[i, j] + N[i + 1, j] + N[i + 1, j + 1] + N[i, j + 1]).Normalized();
					Quad( st, centre, O[i, j], O[i + 1, j], O[i + 1, j + 1], O[i, j + 1], N[i, j], N[i + 1, j], N[i + 1, j + 1], N[i, j + 1], CO[i, j], CO[i + 1, j], CO[i + 1, j + 1], CO[i, j + 1], face );
					Quad( st, centre, I[i, j], I[i + 1, j], I[i + 1, j + 1], I[i, j + 1], -N[i, j], -N[i + 1, j], -N[i + 1, j + 1], -N[i, j + 1], CI[i, j], CI[i + 1, j], CI[i + 1, j + 1], CI[i, j + 1], -face );
				}
				// The broken edges round the tile.
				void Side( int ai, int aj, int bi, int bj )
				{
					var mid = (O[ai, aj] + O[bi, bj] + I[ai, aj] + I[bi, bj]) * 0.25f;
					var facing = (mid - centre).SafeNormal();
					var c = CI[ai, aj].Lerp( Colors.White, 0.15f );
					Quad( st, centre, O[ai, aj], O[bi, bj], I[bi, bj], I[ai, aj], facing, facing, facing, facing, c, c, c, c, facing );
				}
				for ( var k = 0; k < sub; k++ )
				{
					Side( k, 0, k + 1, 0 );
					Side( k, sub, k + 1, sub );
					Side( 0, k, 0, k + 1 );
					Side( sub, k, sub, k + 1 );
				}
				var f = new FragT { At = centre, Reach = reach };
				f.Pieces.Add( new Piece { Mesh = st.Commit(), Local = Transform3D.Identity, Mat = mat } );
				T.Frags.Add( f );
			}
		}

		static void Quad( SurfaceTool st, Vector3 centre, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Color ca, Color cb, Color cc, Color cd, Vector3 facing )
		{
			Tri( st, centre, a, b, c, na, nb, nc, ca, cb, cc, facing );
			Tri( st, centre, a, c, d, na, nc, nd, ca, cc, cd, facing );
		}

		/// <summary> A triangle wound to face `facing` (Godot's front faces are clockwise seen from the front), around the tile's centre. </summary>
		static void Tri( SurfaceTool st, Vector3 centre, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Color ca, Color cb, Color cc, Vector3 facing )
		{
			if ( (b - a).Cross( c - a ).Dot( facing ) > 0f ) { (b, c) = (c, b); (nb, nc) = (nc, nb); (cb, cc) = (cc, cb); }
			st.SetNormal( na ); st.SetColor( ca ); st.AddVertex( a - centre );
			st.SetNormal( nb ); st.SetColor( cb ); st.AddVertex( b - centre );
			st.SetNormal( nc ); st.SetColor( cc ); st.AddVertex( c - centre );
		}

		/// <summary>
		/// Turns everything built so far upside down (about the front to back axis, so the front stays the front) and sets it
		/// back down with its old lowest point `sink` (shell metres) lower than before, so the open end goes down over the crab.
		/// </summary>
		public void UpsideDown( float sink )
		{
			var lo = float.MaxValue;
			var hi = float.MinValue;
			foreach ( var f in T.Frags )
			foreach ( var p in f.Pieces )
			{
				var box = p.Local * p.Mesh.GetAabb();
				lo = MathF.Min( lo, f.At.Y + box.Position.Y );
				hi = MathF.Max( hi, f.At.Y + box.End.Y );
			}
			var turn = new Basis( Vector3.Back, MathF.PI );
			var lift = Vector3.Up * (hi + lo - sink);
			foreach ( var f in T.Frags )
			{
				f.At = turn * f.At + lift;
				foreach ( var p in f.Pieces ) p.Local = new Transform3D( turn * p.Local.Basis, turn * p.Local.Origin );
			}
		}

		/// <summary> A thick triangle (the milk carton's gable ends): `a, b, c` and the same moved along `depth`. </summary>
		public static Mesh Prism( Vector3 a, Vector3 b, Vector3 c, Vector3 depth, Color colour )
		{
			var st = new SurfaceTool();
			st.Begin( Mesh.PrimitiveType.Triangles );
			var centre = (a + b + c) / 3f + depth * 0.5f;
			var n = (b - a).Cross( c - a ).Normalized();
			if ( n.Dot( depth ) > 0f ) n = -n;
			Tri( st, centre, a, b, c, n, n, n, colour, colour, colour, n );
			Tri( st, centre, a + depth, b + depth, c + depth, -n, -n, -n, colour, colour, colour, -n );
			foreach ( var (p, q) in new[] { (a, b), (b, c), (c, a) } )
			{
				var mid = (p + q) * 0.5f + depth * 0.5f;
				var facing = (mid - centre).SafeNormal();
				Quad( st, centre, p, q, q + depth, p + depth, facing, facing, facing, facing, colour, colour, colour, colour, facing );
			}
			var mesh = st.Commit();
			return mesh;
		}
	}

	static Func<Material> Coloured( float roughness, float metallic = 0f, float clearcoat = 0f ) => () =>
	{
		var m = new StandardMaterial3D { VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White, Roughness = roughness, Metallic = metallic, RimEnabled = true, Rim = 0.25f, RimTint = 0.5f };
		if ( clearcoat > 0f ) { m.ClearcoatEnabled = true; m.Clearcoat = clearcoat; m.ClearcoatRoughness = 0.1f; }
		return m;
	};
	static Func<Material> Plain( Color c, float roughness = 0.5f, float metallic = 0f ) => () => new StandardMaterial3D { AlbedoColor = c, Roughness = roughness, Metallic = metallic, RimEnabled = true, Rim = 0.25f, RimTint = 0.5f };
	static Func<Material> Glass( Color c ) => () => new StandardMaterial3D
	{
		AlbedoColor = c with { A = 0.45f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f, Metallic = 0.2f,
		ClearcoatEnabled = true, Clearcoat = 1f, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.25f,
	};

	static Transform3D Turn( Vector3 at, Vector3 axis ) => new( new Basis( new Quaternion( Vector3.Up, axis.Normalized() ) ), at );
	static Transform3D Sized( Vector3 at, Vector3 scale, Basis? turn = null ) => new( (turn ?? Basis.Identity) * Basis.FromScale( scale ), at );
	static float SPow( float x, float p ) => MathF.Sign( x ) * MathF.Pow( MathF.Abs( x ), p );

	static Template MakeTemplate( ShellKind kind )
	{
		var b = new SB( 1000 + (int)kind );
		switch ( kind )
		{
			case ShellKind.Toilet: Toilet( b ); break;
			case ShellKind.DivingHelmet: Helmet( b ); break;
			case ShellKind.GarbageCan: GarbageCan( b ); break;
			case ShellKind.RecyclingBin: RecyclingBin( b ); break;
			case ShellKind.TeaCup: TeaCup( b ); break;
			case ShellKind.DiscoBall: DiscoBall( b ); break;
			default: MilkCarton( b ); break;
		}
		return b.T;
	}

	// ---- The seven shells ----

	/// <summary> A white porcelain toilet worn upside down: the bowl and seat over the crab, the foot up top, the tank and its handle at the back. </summary>
	static void Toilet( SB b )
	{
		var porcelain = b.Mat( Coloured( 0.18f, 0f, 0.6f ) );
		var white = new Color( 0.95f, 0.96f, 0.97f );
		static float R( float v ) => v < 0.25f ? MathX.Lerp( 0.1f, 0.085f, v / 0.25f ) : 0.085f + 0.15f * MathF.Pow( SmoothKit.Ease( (v - 0.25f) / 0.75f ), 0.8f );
		Vector3 Bowl( float u, float v ) { var a = u * MathF.Tau; var r = R( v ); return new Vector3( MathF.Cos( a ) * r * 0.82f, 0.02f + 0.34f * v, MathF.Sin( a ) * r * 1.08f - 0.03f ); }
		b.Patch( Bowl, 8, 3, ( u, v ) => white, ( u, v ) => white * 0.88f, porcelain, 0.028f, wrapU: true );
		// The seat round the rim: a flattened ring.
		Vector3 Seat( float u, float v )
		{
			var a = u * MathF.Tau; var f = v * MathF.Tau;
			var radial = new Vector3( MathF.Cos( a ) * 0.82f, 0f, MathF.Sin( a ) * 1.08f );
			return new Vector3( 0f, 0.37f, -0.03f ) + radial * 0.215f + radial.Normalized() * MathF.Cos( f ) * 0.032f + Vector3.Up * MathF.Sin( f ) * 0.018f;
		}
		Vector3 SeatCore( float u, float v ) { var a = u * MathF.Tau; return new Vector3( 0f, 0.37f, -0.03f ) + new Vector3( MathF.Cos( a ) * 0.82f, 0f, MathF.Sin( a ) * 1.08f ) * 0.215f; }
		b.Patch( Seat, 4, 1, ( u, v ) => white, ( u, v ) => white * 0.85f, porcelain, 0.012f, wrapU: true, wrapV: true, sub: 4, core: SeatCore );
		// The foot it stands on (on top once it is turned over), and the tank.
		b.Patch( ( u, v ) => { var a = u * MathF.Tau; return new Vector3( MathF.Cos( a ) * 0.1f * v * 0.82f, 0.02f, MathF.Sin( a ) * 0.1f * v * 1.08f - 0.03f ); }, 3, 1, ( u, v ) => white * 0.93f, ( u, v ) => white * 0.85f, porcelain, 0.028f, wrapU: true, core: ( u, v ) => new Vector3( 0f, 0.15f, -0.03f ) );
		var tank = b.Mat( Plain( white, 0.18f ), colour: white );
		// The tank, level with the bowl so it stays tucked against it once turned over, closed on both ends.
		const float ty = 0.25f;
		b.Solid( new BoxMesh { Size = new Vector3( 0.34f, 0.3f, 0.02f ) }, new Transform3D( Basis.Identity, new Vector3( 0f, ty, 0.27f ) ), tank, 0.18f );
		b.Solid( new BoxMesh { Size = new Vector3( 0.34f, 0.3f, 0.02f ) }, new Transform3D( Basis.Identity, new Vector3( 0f, ty, 0.39f ) ), tank, 0.18f );
		b.Solid( new BoxMesh { Size = new Vector3( 0.02f, 0.3f, 0.12f ) }, new Transform3D( Basis.Identity, new Vector3( -0.17f, ty, 0.33f ) ), tank, 0.16f );
		b.Solid( new BoxMesh { Size = new Vector3( 0.02f, 0.3f, 0.12f ) }, new Transform3D( Basis.Identity, new Vector3( 0.17f, ty, 0.33f ) ), tank, 0.16f );
		b.Solid( new BoxMesh { Size = new Vector3( 0.37f, 0.03f, 0.16f ) }, new Transform3D( Basis.Identity, new Vector3( 0f, ty + 0.165f, 0.33f ) ), tank, 0.19f );
		b.Solid( new BoxMesh { Size = new Vector3( 0.34f, 0.02f, 0.14f ) }, new Transform3D( Basis.Identity, new Vector3( 0f, ty - 0.15f, 0.33f ) ), tank, 0.18f );
		var chrome = b.Mat( Plain( new Color( 0.85f, 0.87f, 0.9f ), 0.12f, 0.9f ), colour: new Color( 0.85f, 0.87f, 0.9f ) );
		b.Extra( new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.08f, RadialSegments = 8, Rings = 1 }, Turn( new Vector3( -0.11f, ty + 0.1f, 0.25f ), Vector3.Right ), chrome );
		b.Extra( new SphereMesh { Radius = 0.02f, Height = 0.04f }, new Transform3D( Basis.Identity, new Vector3( -0.15f, ty + 0.1f, 0.25f ) ), chrome );
		b.UpsideDown( 0.12f );
	}

	/// <summary> An old brass diving helmet: the dome, a copper breastplate with bolts, portholes with glass and the front one's grill, an air valve. </summary>
	static void Helmet( SB b )
	{
		var brass = b.Mat( Coloured( 0.3f, 0.75f ) );
		var c = new Vector3( 0f, 0.28f, 0f );
		const float r = 0.25f;
		var gold = new Color( 0.86f, 0.62f, 0.26f );
		Vector3 Dome( float u, float v ) { var a = u * MathF.Tau; var f = (1f - v) * 0.78f * MathF.PI; return c + new Vector3( MathF.Sin( f ) * MathF.Cos( a ), MathF.Cos( f ), MathF.Sin( f ) * MathF.Sin( a ) ) * r; }
		b.Patch( Dome, 8, 4, ( u, v ) => v < 0.07f ? gold * 0.7f : gold.Lerp( new Color( 1f, 0.85f, 0.5f ), 0.25f * MathF.Max( 0f, v - 0.6f ) ), ( u, v ) => new Color( 0.3f, 0.22f, 0.12f ), brass, 0.025f, wrapU: true );
		Vector3 Collar( float u, float v ) { var a = u * MathF.Tau; var rr = MathX.Lerp( 0.34f, 0.2f, v ); return new Vector3( MathF.Cos( a ) * rr, MathX.Lerp( 0.02f, 0.1f, v ), MathF.Sin( a ) * rr * 0.9f ); }
		var copper = new Color( 0.78f, 0.46f, 0.26f );
		b.Patch( Collar, 6, 1, ( u, v ) => copper, ( u, v ) => copper * 0.5f, brass, 0.02f, wrapU: true );
		var ring = b.Mat( Plain( gold, 0.25f, 0.8f ), colour: gold );
		var glass = b.Mat( Glass( new Color( 0.45f, 0.85f, 0.8f ) ), tint: false );
		foreach ( var dir in new[] { Vector3.Forward, Vector3.Left, Vector3.Right, new Vector3( 0f, 0.78f, -0.62f ) } )
		{
			var d = dir.Normalized();
			var big = dir == Vector3.Forward;
			var size = big ? 1.25f : 0.8f;
			b.Extra( new TorusMesh { InnerRadius = 0.07f * size, OuterRadius = 0.095f * size, Rings = 20, RingSegments = 8 }, Turn( c + d * (r + 0.008f), d ), ring );
			b.Extra( new SphereMesh { Radius = 0.075f * size, Height = 0.15f * size, RadialSegments = 16, Rings = 8 }, new Transform3D( new Basis( new Quaternion( Vector3.Up, d ) ) * Basis.FromScale( new Vector3( 1f, 0.22f, 1f ) ), c + d * (r + 0.004f) ), glass );
			if ( big )
				for ( var k = -1; k <= 1; k++ )
					b.Extra( new CylinderMesh { TopRadius = 0.007f, BottomRadius = 0.007f, Height = 0.18f, RadialSegments = 6, Rings = 1 }, new Transform3D( Basis.Identity, c + d * (r + 0.022f) + Vector3.Right * k * 0.04f ), ring );
		}
		for ( var k = 0; k < 8; k++ )
		{
			var a = k / 8f * MathF.Tau + 0.2f;
			b.Extra( new SphereMesh { Radius = 0.017f, Height = 0.034f, RadialSegments = 8, Rings = 4 }, new Transform3D( Basis.Identity, new Vector3( MathF.Cos( a ) * 0.29f, 0.05f, MathF.Sin( a ) * 0.29f * 0.9f ) ), ring );
		}
		b.Extra( new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.03f, Height = 0.08f, RadialSegments = 10, Rings = 1 }, Turn( c + new Vector3( 0.16f, 0.1f, 0.15f ), new Vector3( 1f, 0.6f, 0.6f ) ), ring );
		b.Extra( new TorusMesh { InnerRadius = 0.025f, OuterRadius = 0.04f, Rings = 12, RingSegments = 6 }, Turn( c + new Vector3( 0.2f, 0.13f, 0.18f ), new Vector3( 1f, 0.6f, 0.6f ) ), ring );
	}

	/// <summary> A ribbed tin garbage can worn upside down: the bottom on top, the open end over the crab, handles on the sides. </summary>
	static void GarbageCan( SB b )
	{
		var tin = b.Mat( Coloured( 0.4f, 0.3f ) );
		var grey = new Color( 0.72f, 0.75f, 0.78f );
		Vector3 Can( float u, float v ) { var a = u * MathF.Tau; var rr = MathX.Lerp( 0.2f, 0.225f, v ); return new Vector3( MathF.Cos( a ) * rr, MathX.Lerp( 0.02f, 0.5f, v ), MathF.Sin( a ) * rr ); }
		Vector3 Axis( float u, float v ) => new( 0f, MathX.Lerp( 0.02f, 0.5f, v ), 0f );
		Color Ribs( float u, float v ) => (v * 5f % 1f) < 0.16f ? grey * 0.72f : v > 0.96f ? grey * 0.85f : grey;
		b.Patch( Can, 8, 3, Ribs, ( u, v ) => grey * 0.45f, tin, 0.02f, wrapU: true, core: Axis );
		Vector3 Bottom( float u, float v ) { var a = u * MathF.Tau; return new Vector3( MathF.Cos( a ) * 0.2f * v, 0.02f, MathF.Sin( a ) * 0.2f * v ); }
		b.Patch( Bottom, 3, 1, ( u, v ) => grey * 0.9f, ( u, v ) => grey * 0.45f, tin, 0.02f, wrapU: true, core: ( u, v ) => new Vector3( 0f, 0.2f, 0f ) );
		var handle = b.Mat( Plain( grey * 0.95f, 0.4f, 0.3f ), colour: grey * 0.95f );
		foreach ( var x in new[] { -1f, 1f } )
			b.Extra( new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.01f, Height = 0.12f, RadialSegments = 6, Rings = 1 }, new Transform3D( new Basis( Vector3.Right, MathF.PI * 0.5f ), new Vector3( x * 0.24f, 0.42f, 0f ) ), handle );
		b.UpsideDown( 0.15f );
	}

	/// <summary> A blue recycling bin worn upside down, the white chasing arrows on the front. </summary>
	static void RecyclingBin( SB b )
	{
		var plastic = b.Mat( Coloured( 0.45f ) );
		var blue = new Color( 0.13f, 0.42f, 0.88f );
		Vector3 Bin( float u, float v ) { var a = u * MathF.Tau + MathF.PI * 0.25f; var s = MathX.Lerp( 0.17f, 0.21f, v ); return new Vector3( SPow( MathF.Cos( a ), 0.45f ) * s, 0.02f + 0.46f * v, SPow( MathF.Sin( a ), 0.45f ) * s ); }
		b.Patch( Bin, 8, 3, ( u, v ) => v > 0.92f ? blue * 0.65f : blue, ( u, v ) => blue * 0.55f, plastic, 0.022f, wrapU: true, core: ( u, v ) => new Vector3( 0f, 0.02f + 0.46f * v, 0f ) );
		Vector3 Base( float u, float v ) { var a = u * MathF.Tau + MathF.PI * 0.25f; return new Vector3( SPow( MathF.Cos( a ), 0.45f ) * 0.17f * v, 0.02f, SPow( MathF.Sin( a ), 0.45f ) * 0.17f * v ); }
		b.Patch( Base, 4, 1, ( u, v ) => blue * 0.85f, ( u, v ) => blue * 0.55f, plastic, 0.022f, wrapU: true, core: ( u, v ) => new Vector3( 0f, 0.2f, 0f ) );
		// The three chasing arrows round a triangle, on the front.
		var white = b.Mat( Plain( new Color( 0.97f, 0.97f, 0.97f ), 0.5f ), colour: new Color( 0.97f, 0.97f, 0.97f ) );
		var front = new Vector3( 0f, 0.27f, -0.205f );
		for ( var k = 0; k < 3; k++ )
		{
			var a0 = MathX.Rad( 90f + k * 120f + 14f );
			var a1 = MathX.Rad( 90f + k * 120f + 106f );
			var p0 = front + new Vector3( MathF.Cos( a0 ), MathF.Sin( a0 ), 0f ) * 0.07f;
			var p1 = front + new Vector3( MathF.Cos( a1 ), MathF.Sin( a1 ), 0f ) * 0.07f;
			var along = (p1 - p0).Normalized();
			b.Extra( new CylinderMesh { TopRadius = 0.011f, BottomRadius = 0.011f, Height = p0.DistanceTo( p1 ) * 0.8f, RadialSegments = 6, Rings = 1 }, Turn( p0.Lerp( p1, 0.4f ), along ), white );
			b.Extra( new CylinderMesh { TopRadius = 0f, BottomRadius = 0.026f, Height = 0.04f, RadialSegments = 3, Rings = 1 }, Turn( p1 - along * 0.015f, along ), white );
		}
		b.UpsideDown( 0.15f );
	}

	/// <summary> An empty china tea cup worn upside down: gold rim, pink band and flowers, the handle out the side, its foot on top. </summary>
	static void TeaCup( SB b )
	{
		var china = b.Mat( Coloured( 0.15f, 0f, 1f ) );
		var white = new Color( 0.97f, 0.97f, 0.96f );
		var gold = new Color( 0.95f, 0.75f, 0.26f );
		var pink = new Color( 0.95f, 0.55f, 0.7f );
		Vector3 Cup( float u, float v ) { var a = u * MathF.Tau; var r = v < 0.06f ? 0.09f : 0.1f + 0.12f * MathF.Sqrt( v ); return new Vector3( MathF.Cos( a ) * r, 0.05f + 0.26f * v, MathF.Sin( a ) * r ); }
		Color CupPaint( float u, float v ) => v > 0.95f ? gold : v > 0.74f && v < 0.8f ? pink : white;
		b.Patch( Cup, 8, 3, CupPaint, ( u, v ) => v > 0.97f ? gold : white * 0.95f, china, 0.018f, wrapU: true );
		// Its foot: a gold-ringed base.
		b.Patch( ( u, v ) => { var a = u * MathF.Tau; return new Vector3( MathF.Cos( a ) * 0.09f * v, 0.05f, MathF.Sin( a ) * 0.09f * v ); }, 3, 1, ( u, v ) => v > 0.8f ? gold : white, ( u, v ) => white * 0.9f, china, 0.018f, wrapU: true, core: ( u, v ) => new Vector3( 0f, 0.15f, 0f ) );
		// The handle: a loop out of the side.
		var hc = new Vector3( 0.25f, 0.19f, 0f );
		Vector3 Ring( float u ) { var a = MathX.Lerp( -1.75f, 1.75f, u ); return hc + new Vector3( MathF.Cos( a ) * 0.075f, MathF.Sin( a ) * 0.075f, 0f ); }
		Vector3 Handle( float u, float v ) { var c = Ring( u ); var radial = (c - hc).Normalized(); var f = v * MathF.Tau; return c + (radial * MathF.Cos( f ) + Vector3.Back * MathF.Sin( f )) * 0.02f; }
		b.Patch( Handle, 2, 1, ( u, v ) => white, ( u, v ) => white, china, 0.01f, wrapV: true, sub: 4, core: ( u, v ) => Ring( u ) );
		// Pink flowers round the cup.
		var petal = b.Mat( Plain( pink, 0.3f ), colour: pink );
		var middle = b.Mat( Plain( new Color( 1f, 0.85f, 0.3f ), 0.3f ), colour: new Color( 1f, 0.85f, 0.3f ) );
		for ( var k = 0; k < 6; k++ )
		{
			var a = (k + 0.5f) / 6f * MathF.Tau;
			if ( MathF.Abs( MathF.Cos( a ) - 1f ) < 0.2f ) continue;   // not under the handle
			var at = Cup( (k + 0.5f) / 6f, 0.45f );
			var n = new Vector3( MathF.Cos( a ), -0.25f, MathF.Sin( a ) ).Normalized();
			var turn = new Basis( new Quaternion( Vector3.Up, n ) );
			b.Extra( new SphereMesh { Radius = 0.012f, Height = 0.024f, RadialSegments = 8, Rings = 4 }, new Transform3D( turn, at + n * 0.004f ), middle );
			for ( var p = 0; p < 5; p++ )
			{
				var pa = p / 5f * MathF.Tau;
				var off = turn * new Vector3( MathF.Cos( pa ), 0f, MathF.Sin( pa ) ) * 0.022f;
				b.Extra( new SphereMesh { Radius = 0.014f, Height = 0.028f, RadialSegments = 8, Rings = 4 }, new Transform3D( turn * Basis.FromScale( new Vector3( 1f, 0.3f, 1f ) ), at + off ), petal );
			}
		}
		b.UpsideDown( 0.08f );
	}

	/// <summary> A spinning disco ball of little square mirrors (some tinted blue or pink) on a dark core, its loop on top. </summary>
	static void DiscoBall( SB b )
	{
		var mirror = b.Mat( Coloured( 0.12f, 0.55f, 1f ) );
		var c = new Vector3( 0f, 0.3f, 0f );
		const float r = 0.25f;
		Vector3 Ball( float u, float v ) { var a = u * MathF.Tau; var f = MathX.Lerp( 0.95f, 0.05f, v ) * MathF.PI; return c + new Vector3( MathF.Sin( f ) * MathF.Cos( a ), MathF.Cos( f ), MathF.Sin( f ) * MathF.Sin( a ) ) * r; }
		Color Mirror( float u, float v )
		{
			var h = MathF.Abs( MathF.Sin( MathF.Floor( u * 12f ) * 12.9898f + MathF.Floor( v * 6f ) * 78.233f ) * 43758.5453f ) % 1f;
			return h < 0.12f ? new Color( 0.7f, 0.8f, 1f ) : h < 0.22f ? new Color( 1f, 0.78f, 0.9f ) : h < 0.6f ? new Color( 0.92f, 0.93f, 0.95f ) : new Color( 0.78f, 0.8f, 0.84f );
		}
		b.Patch( Ball, 12, 6, Mirror, ( u, v ) => new Color( 0.3f, 0.3f, 0.32f ), mirror, 0.028f, wrapU: true, jag: 0f, gap: 0.07f, sub: 2 );
		var core = new Color( 0.12f, 0.12f, 0.14f );
		b.Solid( new SphereMesh { Radius = r - 0.03f, Height = (r - 0.03f) * 2f, RadialSegments = 20, Rings = 10 }, new Transform3D( Basis.Identity, c ), b.Mat( Plain( core, 0.7f ), colour: core ), r - 0.03f );
		// Sparkles: little glints on some mirrors.
		var glint = b.Mat( () => Sculpt.Glow( new Color( 1f, 1f, 0.95f ), 3f ), tint: false );
		for ( var k = 0; k < 10; k++ )
		{
			var d = new Vector3( b.R( -1f, 1f ), b.R( -0.6f, 1f ), b.R( -1f, 1f ) ).Normalized();
			b.Extra( new SphereMesh { Radius = 0.014f, Height = 0.028f, RadialSegments = 6, Rings = 3 }, new Transform3D( Basis.Identity, c + d * (r + 0.006f) ), glint );
		}
		var metal = new Color( 0.6f, 0.62f, 0.66f );
		b.Extra( new TorusMesh { InnerRadius = 0.02f, OuterRadius = 0.035f, Rings = 12, RingSegments = 6 }, new Transform3D( new Basis( Vector3.Right, MathF.PI * 0.5f ), c + Vector3.Up * (r + 0.03f) ), b.Mat( Plain( metal, 0.3f, 0.8f ), colour: metal ) );
	}

	/// <summary> An opened milk carton worn upside down: white, a blue panel with MILK on it, its top flaps opened out round the crab. </summary>
	static void MilkCarton( SB b )
	{
		var card = b.Mat( Coloured( 0.7f ) );
		const float hx = 0.14f, hz = 0.14f, y0 = 0.02f, h = 0.4f, flap = 0.09f;
		var white = new Color( 0.97f, 0.97f, 0.95f );
		var blue = new Color( 0.16f, 0.42f, 0.86f );
		var middle = new Vector3( 0f, y0 + h * 0.5f, 0f );
		var inside = new Color( 0.75f, 0.72f, 0.65f );
		Color Panel( float u, float v ) => v > 0.42f && v < 0.86f ? blue : white;
		void Side( Vector3 o, Vector3 U, Vector3 V ) => b.Patch( ( u, v ) => o + U * u + V * v, 2, 3, Panel, ( u, v ) => inside, card, 0.016f, core: ( u, v ) => middle );
		Side( new Vector3( -hx, y0, -hz ), new Vector3( 2f * hx, 0f, 0f ), new Vector3( 0f, h, 0f ) );
		Side( new Vector3( -hx, y0, hz ), new Vector3( 2f * hx, 0f, 0f ), new Vector3( 0f, h, 0f ) );
		Side( new Vector3( -hx, y0, -hz ), new Vector3( 0f, 0f, 2f * hz ), new Vector3( 0f, h, 0f ) );
		Side( new Vector3( hx, y0, -hz ), new Vector3( 0f, 0f, 2f * hz ), new Vector3( 0f, h, 0f ) );
		// The bottom (on top once it is turned over).
		b.Patch( ( u, v ) => new Vector3( MathX.Lerp( -hx, hx, u ), y0, MathX.Lerp( -hz, hz, v ) ), 2, 2, ( u, v ) => white * 0.92f, ( u, v ) => inside, card, 0.016f, core: ( u, v ) => middle );
		// The top torn open: four flaps folded out.
		var top = y0 + h;
		foreach ( var (o, U, n) in new[] {
			(new Vector3( -hx, top, -hz ), new Vector3( 2f * hx, 0f, 0f ), Vector3.Forward ),
			(new Vector3( -hx, top, hz ), new Vector3( 2f * hx, 0f, 0f ), Vector3.Back ),
			(new Vector3( -hx, top, -hz ), new Vector3( 0f, 0f, 2f * hz ), Vector3.Left ),
			(new Vector3( hx, top, -hz ), new Vector3( 0f, 0f, 2f * hz ), Vector3.Right ) } )
		{
			var V = (Vector3.Up * 0.45f + n).Normalized() * flap;
			b.Patch( ( u, v ) => o + U * u + V * v, 1, 1, ( u, v ) => white, ( u, v ) => inside, card, 0.014f, jag: 0.4f, core: ( u, v ) => middle );
		}
		// The writing, built turned over so it reads the right way up once the carton is.
		var ink = b.Mat( Plain( white, 0.5f ), colour: white );
		b.Extra( new TextMesh { Text = "MILK", FontSize = 64, PixelSize = 0.0021f, Depth = 0.004f }, new Transform3D( new Basis( Vector3.Back, MathF.PI ) * new Basis( Vector3.Up, MathF.PI ), new Vector3( 0f, y0 + h * 0.64f, -hz - 0.012f ) ), ink );
		b.Extra( new TextMesh { Text = "MOO", FontSize = 48, PixelSize = 0.0021f, Depth = 0.004f }, new Transform3D( new Basis( Vector3.Right, MathF.PI ) * new Basis( Vector3.Up, MathF.PI * 0.5f ), new Vector3( hx + 0.012f, y0 + h * 0.64f, 0f ) ), ink );
		b.UpsideDown( 0.12f );
	}
}
