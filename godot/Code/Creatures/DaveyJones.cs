using Godot;

/// <summary>
/// Davey ("Davey phone home"), the wave 20 boss on the wreck site: an alien ghost pirate. A big-headed alien face with
/// black almond eyes, a bicorne hat, a full beard, a belt, and a man's torso that trails off into a smooth curling tail; a
/// hook for one hand and a cutlass in the other. Translucent and glowing green (<see cref="GhostShader"/>).
/// His attacks:
/// - Vanish: he fades into thin air and 2 to 4 seconds later comes back behind the player and swipes at them;
/// - Cannons: three ghost cannons rise round the player, light their fuses one after another and fire a cannonball
///   each, then fade away;
/// - Sink: he slams the player into the ground; they sink slowly, taking 10 a second, until they mash jump
///   10 to 12 times to climb out.
/// At half health he grabs the player, lifts them up and takes them down to his locker room under the map
/// (<see cref="DaveyLocker"/>), where it is only the two of them; 6 seconds after he dies there the player is taken
/// back up. Built in code (Sculpt), metres, facing -Z; drawn at <see cref="Size"/>.
/// </summary>
public partial class DaveyJones : Creature
{
	public static readonly List<DaveyJones> All = new();
	/// <summary> A player is down in the locker room with him: spawning stops and every other enemy holds still. </summary>
	public static bool InLocker => DaveyLocker.Active;
	/// <summary> Davey with a player buried by his Sink (the HUD shows the mash prompt), else null. </summary>
	public static DaveyJones Sinking { get; private set; }

	public float Size = 1.25f;
	public float HoverHeight = 2.1f;      // his waist over the floor, metres
	public float KeepAway = 8f;
	public float SwimSpeed = 4.5f;
	public float SwipeDamage = 25f;
	public float SwipeReach = 3.8f;
	public float CannonDamage = 20f;
	public float SinkDamage = 10f;
	public float SinkTick = 1f;
	public float MaxBury = 1.05f;          // how far the player sinks, metres (their eyes stay just above the sand)
	public float BuryTime = 9f;            // seconds to sink all the way
	public float HeadMultiplier = 2f;
	public float TailMultiplier = 0.6f;
	/// <summary> Tests: he only attacks when told (ForceAttack); the half health grab still happens. </summary>
	public bool OnlyWhenTold;
	/// <summary> Tests: where he last came back after a vanish, and whether that was behind the player. </summary>
	public bool LastAppearBehind { get; private set; }
	public int Swipes { get; private set; }
	public int Mashes { get; private set; }
	public int MashesNeeded { get; private set; }
	public bool Hidden => fade < 0.05f;

	static readonly Color Glow = new( 0.35f, 1f, 0.42f );
	static readonly Color GooColor = new( 0.45f, 1f, 0.5f );

	enum Mode { Float, Vanish, Gone, Appear, Swipe, Cannons, SinkWindUp, Grab, Dead }

	Mode mode = Mode.Float;
	TimeSince inMode;
	TimeUntil nextAttack = 3f;
	string lastAttack = "";
	float goneFor;
	Vector3 velocity;
	float floorY;
	TimeSince sinceFloor = 99f;
	TimeSince sinceHit = 99f;
	/// <summary> The move he is doing, shown big on the HUD like MC-51's lines. </summary>
	public string Line { get; private set; } = "";
	public TimeSince TimeSinceLine { get; private set; } = 999f;
	void Say( string line ) { Line = line; TimeSinceLine = 0f; }
	float fade = 1f;
	float phase;

	// Sink
	PlayerSurvivor buried;
	Vector3 buryAt;
	TimeSince sinceBuried;
	TimeSince sinceTick;
	Node3D mound;

	// Grab
	bool lockerDone;
	PlayerSurvivor grabbed;
	Vector3 grabGround;
	Vector3 grabFrom;
	Vector3 liftFrom;
	bool fading, lifting;

	Node3D body, head, shoulderR, elbowR, shoulderL, elbowL, hat;
	readonly List<Node3D> tail = new();
	Skeleton3D tailBones;
	readonly List<ShaderMaterial> ghosts = new();
	readonly List<CreaturePart> hitParts = new();
	OmniLight3D light;

	public static DaveyJones Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new DaveyJones { Name = "Davey" }, parent, position, maxHealth );

	protected override void Track( bool add )
	{
		if ( add ) All.Add( this );
		else
		{
			All.Remove( this );
			if ( Sinking == this ) Sinking = null;   // freed mid-sink (a restart): the next run must not see it
		}
	}

	public float HealthFraction => health?.Fraction ?? 0f;

	protected override void Setup()
	{
		BeginSetup( 11000f );
		phase = Rng.Float( 0f, MathF.Tau );
		floorY = Sculpt.FloorBelow( this, GlobalPosition, 80f );
		// He can't be taken past half health until he has dragged someone to his locker.
		health.Absorb = d => lockerDone ? d.Damage : MathF.Min( d.Damage, health.Current - health.MaxHealth * 0.5f );
		Build();
		fade = 0f;
		SetMode( Mode.Appear );
		Sfx.Play2D( "boss_spawn", 0.9f, 0.8f );
		Sfx.Play( "ghost_appear", GlobalPosition, 1f );
	}

	protected override void Cleanup()
	{
		EndSink( false );
		if ( grabbed?.Controller is { } c && IsInstanceValid( c ) && !InLocker ) c.Scripted = false;
	}

	void SetMode( Mode m )
	{
		mode = m;
		inMode = 0f;
	}

	PlayerSurvivor Target => PlayerSurvivor.FindNearestAlive( GlobalPosition );

	// ---- Looks ----

	/// <summary> See-through and glowing: brighter at the edges (fresnel), a slow shimmer rising through him. </summary>
	public static readonly Shader GhostShader = new()
	{
		Code = @"shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_always, unshaded;
uniform vec3 tint : source_color = vec3(0.3, 1.0, 0.4);
uniform vec3 rim_colour : source_color = vec3(0.8, 1.0, 0.7);
uniform float base_alpha = 0.32;
uniform float fade = 1.0;
uniform float flash = 0.0;
varying float height;
void vertex() { height = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y; }
void fragment() {
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
	float shimmer = 0.82 + 0.18 * sin(TIME * 3.0 - height * 5.0);
	vec3 c = tint * shimmer + rim_colour * rim * 0.9;
	ALBEDO = mix(c, vec3(2.0), flash);
	ALPHA = clamp((base_alpha + rim * 0.6) * fade, 0.0, 1.0);
}",
	};

	ShaderMaterial Ghost( Color tint, float alpha = 0.32f )
	{
		var m = new ShaderMaterial { Shader = GhostShader };
		m.SetShaderParameter( "tint", tint );
		m.SetShaderParameter( "base_alpha", alpha );
		ghosts.Add( m );
		return m;
	}

	void Build()
	{
		var skin = Ghost( new Color( 0.3f, 0.95f, 0.38f ) );
		var dark = Ghost( new Color( 0.12f, 0.5f, 0.2f ), 0.45f );
		var beard = Ghost( new Color( 0.2f, 0.78f, 0.3f ), 0.4f );
		var pale = Ghost( new Color( 0.75f, 1f, 0.72f ), 0.5f );
		var eyes = Ghost( new Color( 0.01f, 0.03f, 0.02f ), 0.95f );
		var shine = Ghost( new Color( 0.9f, 1f, 0.9f ), 0.9f );
		var cuff = Ghost( new Color( 0.5f, 0.95f, 0.85f ), 0.45f );

		body = new Node3D { Name = "Body", Scale = Vector3.One * Size };
		AddChild( body );

		// A man's torso, lofted smooth: waist, belly, broad chest, shoulders, neck. The belt round the waist, its buckle.
		var torso = SmoothKit.Smooth( new List<(float, float, float, float)>
		{
			(-0.08f, 0.36f, 0.28f, 0f), (0.12f, 0.4f, 0.3f, 0f), (0.4f, 0.43f, 0.31f, -0.01f), (0.75f, 0.5f, 0.33f, -0.03f),
			(1.02f, 0.53f, 0.31f, -0.02f), (1.18f, 0.44f, 0.26f, 0f), (1.3f, 0.17f, 0.15f, 0f), (1.46f, 0.13f, 0.13f, 0f),
		} );
		SmoothKit.Put( body, SmoothKit.Cached( "davey torso", () => SmoothKit.Loft( torso ) ), skin, "Chest" );
		foreach ( var side in new[] { -1f, 1f } ) SmoothKit.Ball( body, new Vector3( side * 0.52f, 1.08f, 0f ), new Vector3( 0.34f, 0.3f, 0.32f ), skin, name: "Shoulder" );
		var belt = new MeshInstance3D { Name = "Belt", Mesh = new TorusMesh { InnerRadius = 0.4f, OuterRadius = 0.48f, Rings = 32, RingSegments = 12 }, MaterialOverride = dark };
		body.AddChild( belt );
		belt.Position = new Vector3( 0f, 0.12f, 0f );
		belt.Scale = new Vector3( 1.0f, 1.4f, 0.75f );
		Sculpt.Block( body, new Vector3( 0f, 0.12f, -0.36f ), new Vector3( 0.26f, 0.2f, 0.06f ), pale, name: "Buckle" );
		Sculpt.Block( body, new Vector3( 0f, 0.12f, -0.39f ), new Vector3( 0.12f, 0.08f, 0.02f ), dark );

		// The tail: one smooth skinned mesh down a chain of joints that curls back, tapering to a point.
		Node3D link = body;
		var at = new Vector3( 0f, -0.05f, 0f );
		for ( var i = 0; i < 8; i++ )
		{
			var joint = Sculpt.Joint( link, at, $"Tail{i}" );
			tail.Add( joint );
			link = joint;
			at = new Vector3( 0f, -0.27f, 0f );
		}
		tailBones = SmoothKit.SkinChain( body, tail, 0.27f, 0.22f, 0.3f, t =>
		{
			var k = MathF.Pow( 1f - t, 0.85f );
			return (0.38f * k + 0.008f, 0.3f * k + 0.008f);
		}, skin, "davey tail", 48, 24 );

		// The head: an alien's, lofted smooth from a small chin to a big round crown; black almond eyes slanting up.
		head = Sculpt.Joint( body, new Vector3( 0f, 1.38f, 0f ), "Head" );
		var skull = SmoothKit.Smooth( new List<(float, float, float, float)>
		{
			(-0.1f, 0.03f, 0.03f, -0.12f), (-0.06f, 0.1f, 0.1f, -0.11f), (0.05f, 0.17f, 0.16f, -0.08f), (0.2f, 0.3f, 0.28f, -0.03f),
			(0.4f, 0.43f, 0.39f, 0f), (0.6f, 0.44f, 0.4f, 0.02f), (0.76f, 0.36f, 0.33f, 0.02f), (0.87f, 0.18f, 0.17f, 0.02f), (0.9f, 0.01f, 0.01f, 0.02f),
		} );
		SmoothKit.Put( head, SmoothKit.Cached( "davey head", () => SmoothKit.Loft( skull ) ), skin, "Crown" );
		foreach ( var side in new[] { -1f, 1f } )
		{
			var turn = new Basis( Vector3.Back, side * 0.5f ) * new Basis( Vector3.Up, -side * 0.38f );
			SmoothKit.Ball( head, new Vector3( side * 0.18f, 0.3f, -0.33f ), new Vector3( 0.34f, 0.17f, 0.1f ), eyes, turn, "Eye" );
			SmoothKit.Ball( head, new Vector3( side * 0.13f, 0.34f, -0.38f ), Vector3.One * 0.04f, shine, name: "Glint" );
			// Scowling brows.
			SmoothKit.Tube( head, new Vector3( side * 0.06f, 0.42f, -0.375f ), new Vector3( side * 0.3f, 0.5f, -0.32f ), 0.025f, 0.018f, dark, "Brow" );
		}
		// The mouth shut in a hard line, a moustache over it, and a full beard from the jaw to a point.
		foreach ( var side in new[] { -1f, 1f } )
		{
			SmoothKit.Tube( head, new Vector3( 0f, 0.085f, -0.262f ), new Vector3( side * 0.075f, 0.072f, -0.245f ), 0.013f, 0.011f, eyes, "Mouth" );
			SmoothKit.Tube( head, new Vector3( side * 0.02f, 0.135f, -0.29f ), new Vector3( side * 0.17f, 0.07f, -0.24f ), 0.035f, 0.016f, beard, "Moustache" );
		}
		SmoothKit.Ball( head, new Vector3( 0f, -0.06f, -0.13f ), new Vector3( 0.48f, 0.36f, 0.32f ), beard, name: "Beard" );
		for ( var i = 0; i < 5; i++ )
		{
			var a = MathX.Lerp( -0.9f, 0.9f, i / 4f );
			var root = new Vector3( MathF.Sin( a ) * 0.17f, -0.06f, -0.13f - MathF.Cos( a ) * 0.12f );
			var drop = 0.42f + 0.16f * MathF.Cos( a * 1.4f );
			var tip = new Vector3( root.X * 0.45f, root.Y - drop, root.Z - 0.08f );
			SmoothKit.Tube( head, root, tip, 0.1f, 0.022f, beard, "Beard" );
		}

		// The hat: a bicorne worn side to side, dark with gold trim, a skull and crossbones and a red feather, all ghostly.
		BuildHat( shine, eyes );

		// Arms: cuffed sleeves; the right fist holds a cutlass, the left arm ends in a hook.
		(shoulderR, elbowR) = Arm( 1f, skin, cuff );
		(shoulderL, elbowL) = Arm( -1f, skin, cuff );
		var fist = Sculpt.Joint( elbowR, new Vector3( 0f, -0.52f, 0f ), "Fist" );
		SmoothKit.Ball( fist, Vector3.Zero, new Vector3( 0.21f, 0.19f, 0.21f ), skin, name: "Hand" );
		Sculpt.Limb( fist, new Vector3( 0f, 0f, 0.1f ), new Vector3( 0f, 0f, -0.12f ), 0.035f, 0.035f, pale, "Grip" );
		Sculpt.Block( fist, new Vector3( 0f, 0f, -0.14f ), new Vector3( 0.06f, 0.24f, 0.05f ), pale, name: "Guard" );
		var blade = new Vector3( 0f, 0f, -0.16f );
		for ( var k = 0; k < 6; k++ )
		{
			var t0 = k / 6f;
			var t1 = (k + 1) / 6f;
			var p0 = new Vector3( 0f, 0.28f * t0 * t0, -0.16f - 1.0f * t0 );
			var p1 = new Vector3( 0f, 0.28f * t1 * t1, -0.16f - 1.0f * t1 );
			var width = k < 5 ? MathX.Lerp( 0.11f, 0.19f, t0 ) : 0.1f;
			var dir = (p1 - p0).Normalized();
			Sculpt.Block( fist, (p0 + p1) * 0.5f + Vector3.Up * width * 0.3f, new Vector3( 0.02f, width, (p1 - p0).Length() + 0.01f ), pale, new Basis( Vector3.Right, MathF.Atan2( dir.Y, -dir.Z ) ), "Blade" );
			blade = p1;
		}
		Sculpt.Cone( fist, blade + Vector3.Up * 0.03f, blade + new Vector3( 0f, 0.1f, -0.14f ), 0.05f, pale, 4, "Point" );

		var hookAt = Sculpt.Joint( elbowL, new Vector3( 0f, -0.52f, 0f ), "Hook" );
		Sculpt.Limb( hookAt, new Vector3( 0f, 0.05f, 0f ), new Vector3( 0f, -0.14f, 0f ), 0.1f, 0.06f, pale, "Hook Cup" );
		var centre = new Vector3( 0f, -0.3f, 0f );
		const float hookR = 0.15f;
		var last = new Vector3( 0f, -0.14f, 0f );
		for ( var k = 1; k <= 7; k++ )
		{
			var a = k / 7f * MathF.PI * 1.25f;
			var p = centre + new Vector3( 0f, hookR * MathF.Cos( a ), -hookR * MathF.Sin( a ) );
			if ( k < 7 ) Sculpt.Limb( hookAt, last, p, 0.03f, 0.028f, pale, "Hook" );
			else Sculpt.Cone( hookAt, last, p, 0.028f, pale, 6, "Hook Tip" );
			last = p;
		}

		// What can be shot: the head (the weak spot), the chest and the tail.
		AddHit( 0, new Vector3( 0f, 1.75f, 0f ) * Size, 0.55f * Size );
		AddHit( 1, new Vector3( 0f, 0.6f, 0f ) * Size, 0.65f * Size );
		AddHit( 2, new Vector3( 0f, -0.75f, 0.2f ) * Size, 0.42f * Size );

		light = new OmniLight3D { LightColor = Glow, LightEnergy = 1.4f, OmniRange = 7f, ShadowEnabled = false };
		body.AddChild( light );
		light.Position = new Vector3( 0f, 0.8f, -0.6f );
	}

	// The bicorne's shape: from the front a crescent with blunt points, from the side a wedge closing to a ridge.
	const float HatHalfWidth = 0.86f, HatHeight = 0.52f, HatDepth = 0.4f;
	static float HatBottom( float x ) { var q = 1f - (x / HatHalfWidth) * (x / HatHalfWidth); return 0.07f * q; }
	static float HatTop( float x ) { var q = MathF.Max( 0f, 1f - (x / HatHalfWidth) * (x / HatHalfWidth) ); return HatBottom( x ) + HatHeight * MathF.Pow( q, 0.75f ) + 0.05f; }
	static float HatHalfDepth( float x ) { var q = MathF.Max( 0f, 1f - (x / HatHalfWidth) * (x / HatHalfWidth) ); return HatDepth * MathF.Sqrt( q ) + 0.035f; }
	/// <summary> A point on the hat: across at x, up the face at v (0 the brim edge, 1 the ridge), on the front (-1) or back (1). </summary>
	static Vector3 HatPoint( float x, float v, float side ) =>
		new( x, MathX.Lerp( HatBottom( x ), HatTop( x ), v ), side * HatHalfDepth( x ) * (1f - v * v) );

	static ArrayMesh HatMesh()
	{
		const int across = 40, up = 14;
		var mesh = new ArrayMesh();
		// Surface 0 the felt, 1 the gold trim round the edge.
		foreach ( var trim in new[] { false, true } )
		{
			var st = new SurfaceTool();
			st.Begin( Mesh.PrimitiveType.Triangles );
			void Tri( Vector3 a, Vector3 b, Vector3 c, Vector3 outward )
			{
				// Godot's front faces wind clockwise seen from outside.
				if ( (b - a).Cross( c - a ).Dot( outward ) > 0f ) (b, c) = (c, b);
				st.AddVertex( a ); st.AddVertex( b ); st.AddVertex( c );
			}
			foreach ( var side in new[] { -1f, 1f } )
				for ( var i = 0; i < across; i++ )
					for ( var j = 0; j < up; j++ )
					{
						var v0 = j / (float)up;
						var v1 = (j + 1f) / up;
						var x0 = MathX.Lerp( -HatHalfWidth, HatHalfWidth, i / (float)across ) * 0.999f;
						var x1 = MathX.Lerp( -HatHalfWidth, HatHalfWidth, (i + 1f) / across ) * 0.999f;
						var edge = v0 >= 0.84f || MathF.Abs( (x0 + x1) * 0.5f ) > HatHalfWidth * 0.9f;
						if ( edge != trim ) continue;
						var p00 = HatPoint( x0, v0, side );
						var p10 = HatPoint( x1, v0, side );
						var p01 = HatPoint( x0, v1, side );
						var p11 = HatPoint( x1, v1, side );
						var outward = new Vector3( 0f, 0.3f, side );
						Tri( p00, p10, p11, outward );
						Tri( p00, p11, p01, outward );
					}
			if ( trim )
				foreach ( var end in new[] { -1f, 1f } )
					for ( var j = 0; j < up; j++ )
					{
						// The blunt gold points at each end.
						var x = end * HatHalfWidth * 0.999f;
						var v0 = j / (float)up;
						var v1 = (j + 1f) / up;
						var outward = new Vector3( end, 0f, 0f );
						Tri( HatPoint( x, v0, -1f ), HatPoint( x, v1, -1f ), HatPoint( x, v1, 1f ), outward );
						Tri( HatPoint( x, v0, -1f ), HatPoint( x, v1, 1f ), HatPoint( x, v0, 1f ), outward );
					}
			if ( !trim )
				for ( var i = 0; i < across; i++ )
				{
					// The underside, closed over the head.
					var x0 = MathX.Lerp( -HatHalfWidth, HatHalfWidth, i / (float)across ) * 0.999f;
					var x1 = MathX.Lerp( -HatHalfWidth, HatHalfWidth, (i + 1f) / across ) * 0.999f;
					Tri( HatPoint( x0, 0f, -1f ), HatPoint( x1, 0f, -1f ), HatPoint( x1, 0f, 1f ), Vector3.Down );
					Tri( HatPoint( x0, 0f, -1f ), HatPoint( x1, 0f, 1f ), HatPoint( x0, 0f, 1f ), Vector3.Down );
				}
			st.GenerateNormals();
			st.Commit( mesh );
		}
		return mesh;
	}

	void BuildHat( Material bone, Material socket )
	{
		var feltGhost = Ghost( new Color( 0.1f, 0.34f, 0.36f ), 0.55f );
		var gold = Ghost( new Color( 1f, 0.75f, 0.2f ), 0.6f );
		var red = Ghost( new Color( 0.95f, 0.18f, 0.15f ), 0.55f );
		hat = Sculpt.Joint( head, new Vector3( 0f, 0.6f, 0.02f ), "Hat" );
		hat.Rotation = new Vector3( 0.1f, 0f, 0.05f );
		var shell = SmoothKit.Put( hat, SmoothKit.Cached( "davey bicorne", HatMesh ), feltGhost, "Bicorne" );
		shell.SetSurfaceOverrideMaterial( 0, feltGhost );
		shell.SetSurfaceOverrideMaterial( 1, gold );
		shell.MaterialOverride = null;

		// Skull and crossbones on the front face, tipped back with its slope.
		var mid = HatPoint( 0f, 0.42f, -1f );
		var above = HatPoint( 0f, 0.52f, -1f );
		var slope = (above - mid).Normalized();
		var face = new Basis( Vector3.Right, MathF.Atan2( -slope.Z, slope.Y ) );
		var badge = Sculpt.Joint( hat, mid + new Vector3( 0f, 0f, -0.02f ), "Badge" );
		badge.Basis = face;
		SmoothKit.Ball( badge, new Vector3( 0f, 0.05f, 0f ), new Vector3( 0.15f, 0.13f, 0.05f ), bone, name: "Skull" );
		SmoothKit.Ball( badge, new Vector3( 0f, -0.02f, 0f ), new Vector3( 0.09f, 0.06f, 0.045f ), bone, name: "Jaw" );
		foreach ( var side in new[] { -1f, 1f } )
		{
			SmoothKit.Ball( badge, new Vector3( side * 0.034f, 0.05f, -0.022f ), new Vector3( 0.045f, 0.04f, 0.02f ), socket, name: "Socket" );
			SmoothKit.Tube( badge, new Vector3( side * 0.12f, -0.13f, 0f ), new Vector3( -side * 0.12f, -0.03f, 0f ), 0.016f, 0.016f, bone, "Bone" );
			SmoothKit.Ball( badge, new Vector3( side * 0.125f, -0.135f, 0f ), Vector3.One * 0.035f, bone, name: "Knuckle" );
			SmoothKit.Ball( badge, new Vector3( -side * 0.125f, -0.025f, 0f ), Vector3.One * 0.035f, bone, name: "Knuckle" );
		}

		// The feather: tucked in behind the right side, sweeping up and out.
		var quill = Sculpt.Joint( hat, HatPoint( 0.3f, 0.45f, 1f ) + new Vector3( 0f, 0f, -0.04f ), "Feather" );
		quill.Rotation = new Vector3( 0.15f, 0f, -0.55f );
		var plume = SmoothKit.Smooth( new List<(float, float, float, float)>
		{
			(0f, 0.015f, 0.012f, 0f), (0.12f, 0.06f, 0.014f, 0.01f), (0.3f, 0.085f, 0.015f, 0.04f), (0.48f, 0.07f, 0.013f, 0.09f),
			(0.6f, 0.035f, 0.01f, 0.14f), (0.66f, 0.005f, 0.005f, 0.17f),
		} );
		SmoothKit.Put( quill, SmoothKit.Cached( "davey feather", () => SmoothKit.Loft( plume ) ), red, "Plume" );
	}

	(Node3D Shoulder, Node3D Elbow) Arm( float side, Material skin, Material cuff )
	{
		var shoulder = Sculpt.Joint( body, new Vector3( side * 0.58f, 1.05f, 0f ), side > 0 ? "Shoulder R" : "Shoulder L" );
		SmoothKit.Tube( shoulder, Vector3.Zero, new Vector3( 0f, -0.52f, 0f ), 0.14f, 0.11f, skin, "Upper Arm" );
		var elbow = Sculpt.Joint( shoulder, new Vector3( 0f, -0.52f, 0f ), "Elbow" );
		SmoothKit.Tube( elbow, Vector3.Zero, new Vector3( 0f, -0.36f, 0f ), 0.11f, 0.095f, skin, "Forearm" );
		// The cuff: a wide band with two stripes.
		SmoothKit.Tube( elbow, new Vector3( 0f, -0.34f, 0f ), new Vector3( 0f, -0.48f, 0f ), 0.15f, 0.16f, cuff, "Cuff" );
		foreach ( var y in new[] { -0.38f, -0.44f } )
			Sculpt.Limb( elbow, new Vector3( 0f, y, 0f ), new Vector3( 0f, y - 0.015f, 0f ), 0.165f, 0.165f, skin, "Stripe" );
		return (shoulder, elbow);
	}

	/// <summary> A hit sphere on the (unscaled) creature node itself, so physics never sees a scaled body. </summary>
	void AddHit( int index, Vector3 at, float radius )
	{
		var part = CreaturePart.Create( this, index, $"Davey {index}", radius );
		AddChild( part );
		part.Position = at;
		hitParts.Add( part );
	}

	void Shootable( bool on )
	{
		foreach ( var p in hitParts ) if ( IsInstanceValid( p ) ) p.CollisionLayer = on ? Layers.Creature : 0;
	}

	// ---- Thinking ----

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = (float)delta;
		var t = (float)Clock.Now + phase;
		Animate( t, dt );
		if ( mode == Mode.Dead ) { Dying( dt ); return; }
		if ( buried is not null ) UpdateSink();
		if ( Frozen && mode is not Mode.Appear and not Mode.Grab ) return;
		if ( sinceFloor > 0.3f ) { sinceFloor = 0f; floorY = Sculpt.FloorBelow( this, GlobalPosition + Vector3.Up * 0.5f, 80f ); }
		if ( !lockerDone && health.Current <= health.MaxHealth * 0.5f + 0.5f && mode is not Mode.Grab ) StartGrab();

		var prey = Target;
		switch ( mode )
		{
			case Mode.Float:
				Drift( prey, dt );
				if ( prey is null || OnlyWhenTold || buried is not null ) break;
				if ( nextAttack <= 0f ) ChooseAttack();
				break;
			case Mode.Vanish:
				fade = MathX.Clamp( 1f - (float)inMode / 0.6f, 0f, 1f );
				body.RotateY( dt * 6f );
				if ( inMode >= 0.6f ) { Shootable( false ); goneFor = Rng.Float( 2f, 4f ); SetMode( Mode.Gone ); }
				break;
			case Mode.Gone:
				fade = 0f;
				if ( inMode >= goneFor ) ComeBackBehind( prey );
				break;
			case Mode.Appear:
				fade = MathX.Clamp( (float)inMode / 0.45f, 0f, 1f );
				body.Rotation = body.Rotation with { Y = MathX.Lerp( body.Rotation.Y, 0f, 1f - MathF.Exp( -8f * dt ) ) };
				if ( prey is not null ) Face( prey.WorldPosition - GlobalPosition, 10f, dt );
				if ( inMode >= 0.5f )
				{
					fade = 1f;
					body.Rotation = Vector3.Zero;
					Shootable( true );
					if ( swipeNext ) { swipeNext = false; SetMode( Mode.Swipe ); }
					else { SetMode( Mode.Float ); nextAttack = Rng.Float( 1.5f, 3f ); }
				}
				break;
			case Mode.Swipe:
				if ( prey is not null ) Face( prey.WorldPosition - GlobalPosition, 6f, dt );
				if ( !swiped && inMode >= SwipeWindUp ) Swipe( prey );
				if ( inMode >= SwipeWindUp + 0.6f ) { SetMode( Mode.Float ); nextAttack = Rng.Float( 2f, 3.5f ); }
				break;
			case Mode.Cannons:
				Hover( prey, dt );
				if ( !cannonsOut && inMode >= 0.8f ) { cannonsOut = true; RaiseCannons( prey ); }
				if ( inMode >= 1.6f ) { SetMode( Mode.Float ); nextAttack = Rng.Float( 4f, 5.5f ); }
				break;
			case Mode.SinkWindUp:
				Hover( prey, dt );
				if ( !slammed && inMode >= 0.9f ) { slammed = true; Slam( prey ); }
				if ( inMode >= 1.4f ) { SetMode( Mode.Float ); nextAttack = Rng.Float( 3f, 4.5f ); }
				break;
			case Mode.Grab:
				Grabbing( dt );
				break;
		}
	}

	const float SwipeWindUp = 0.45f;
	bool swipeNext, swiped, cannonsOut, slammed;

	/// <summary> Tests and the dev menu: "vanish", "cannons" or "sink", now. </summary>
	public void ForceAttack( string what )
	{
		if ( mode != Mode.Float || Target is null ) return;
		switch ( what )
		{
			case "vanish": Vanish(); break;
			case "cannons": Cannons(); break;
			case "sink": if ( buried is null ) SinkAttack(); break;
		}
	}

	void ChooseAttack()
	{
		var options = new List<string> { "vanish", "cannons", "sink" };
		options.Remove( lastAttack );
		var pick = options[Rng.Int( 0, options.Count - 1 )];
		lastAttack = pick;
		ForceAttack( pick );
	}

	/// <summary> Keeps a few metres off the player at his hover height, facing them. </summary>
	void Drift( PlayerSurvivor prey, float dt )
	{
		var wish = Vector3.Zero;
		if ( prey is not null )
		{
			var away = (GlobalPosition - prey.WorldPosition).Flat().SafeNormal();
			if ( away == Vector3.Zero ) away = Vector3.Back;
			var drift = new Basis( Vector3.Up, MathF.Sin( (float)Clock.Now * 0.3f + phase ) * 0.5f ) * away;
			var spot = Keep( prey.WorldPosition + drift * KeepAway );
			spot.Y = floorY + HoverHeight + MathF.Sin( (float)Clock.Now * 0.8f ) * 0.3f;
			var to = spot - GlobalPosition;
			wish = to.Length() > 0.3f ? to.Normalized() * MathF.Min( SwimSpeed, to.Length() * 1.6f ) : Vector3.Zero;
			Face( prey.WorldPosition - GlobalPosition, 3f, dt );
		}
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -2f * dt ) );
		GlobalPosition = Keep( GlobalPosition + velocity * dt );
	}

	void Hover( PlayerSurvivor prey, float dt )
	{
		velocity = velocity.Lerp( Vector3.Zero, 1f - MathF.Exp( -4f * dt ) );
		GlobalPosition = Keep( GlobalPosition + velocity * dt );
		if ( prey is not null ) Face( prey.WorldPosition - GlobalPosition, 5f, dt );
	}

	/// <summary> Inside the map, or inside the locker room while he is down there. </summary>
	static Vector3 Keep( Vector3 p ) => InLocker ? DaveyLocker.Inside( p ) : Sculpt.InBounds( p );

	// ---- Vanish ----

	void Vanish()
	{
		SetMode( Mode.Vanish );
		Say( "VANISH!" );
		velocity = Vector3.Zero;
		Sfx.Play( "ghost_vanish", GlobalPosition, 1f );
		gore?.GooBurst( GlobalPosition + Vector3.Up * Size, Vector3.Up, GooColor, 10, 200f, 0.8f );
	}

	/// <summary> Back out of thin air right behind the player (where they are not looking), sword already up. </summary>
	void ComeBackBehind( PlayerSurvivor prey )
	{
		if ( prey?.Controller is not { } c ) { SetMode( Mode.Appear ); return; }
		var look = c.EyeAngles.Forward.Flat().SafeNormal();
		if ( look == Vector3.Zero ) look = -c.GlobalBasis.Z.Flat().SafeNormal();
		var spot = Keep( c.GlobalPosition - look * 2.6f );
		var floor = Sculpt.FloorBelow( this, spot + Vector3.Up * 2f, 30f );
		GlobalPosition = spot with { Y = MathF.Max( floor, c.GlobalPosition.Y - 0.5f ) + 1.35f };
		GlobalBasis = Shapes.LookAt( (c.GlobalPosition - GlobalPosition).Flat().SafeNormal() is var d && d != Vector3.Zero ? d : look );
		var from = (GlobalPosition - c.GlobalPosition).Flat().SafeNormal();
		LastAppearBehind = from.Dot( look ) < -0.5f;
		swipeNext = true;
		swiped = false;
		SetMode( Mode.Appear );
		Sfx.Play( "ghost_appear", GlobalPosition, 1.1f );
		if ( AutoTest.Requested ) GD.Print( $"[autotest] davey comes back {GlobalPosition.DistanceTo( c.GlobalPosition ):0.0} m from the player, behind: {LastAppearBehind}" );
	}

	void Swipe( PlayerSurvivor prey )
	{
		swiped = true;
		Sfx.Play( "axe_swing", GlobalPosition, 1f, 0.7f );
		if ( prey?.Controller is not { } c ) return;
		var to = c.GlobalPosition + Vector3.Up * 1f - (GlobalPosition + Vector3.Up * 0.5f * Size);
		if ( to.Flat().Length() > SwipeReach || MathF.Abs( to.Y ) > 3f ) return;
		Swipes++;
		prey.Health?.OnDamage( new DamageInfo { Damage = SwipeDamage, Attacker = this, Position = c.GlobalPosition + Vector3.Up * 1.1f, Origin = GlobalPosition } );
		Sfx.Play( "hit_flesh", c.GlobalPosition, 1f, 0.8f );
		c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.8f );
		if ( !c.Scripted ) c.Velocity += to.Flat().SafeNormal() * 7f + Vector3.Up * 2f;
	}

	// ---- Cannons ----

	void Cannons()
	{
		SetMode( Mode.Cannons );
		Say( "CANNON BALLS!" );
		cannonsOut = false;
		Sfx.Play( "ghost_appear", GlobalPosition, 0.7f, 0.6f );
	}

	/// <summary> Three cannons round the player, on the floor, their fuses lit one after another. </summary>
	void RaiseCannons( PlayerSurvivor prey )
	{
		if ( prey?.Controller is not { } c ) return;
		var start = Rng.Float( 0f, MathF.Tau );
		var reach = InLocker ? 5.5f : 10f;
		for ( var i = 0; i < 3; i++ )
		{
			var a = start + i * MathF.Tau / 3f + Rng.Float( -0.35f, 0.35f );
			var spot = Keep( c.GlobalPosition + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * Rng.Float( reach * 0.85f, reach * 1.15f ) );
			if ( !InLocker && Nav.Closest( this, spot ) is var ground && ground != Vector3.Zero && ground.Flat().DistanceTo( spot.Flat() ) < 4f ) spot = ground;
			spot.Y = Sculpt.FloorBelow( this, spot + Vector3.Up * 3f, 30f );
			DaveyCannon.Raise( this, spot, prey, 0.5f + i * 0.9f, CannonDamage );
		}
	}

	// ---- Sink ----

	void SinkAttack()
	{
		SetMode( Mode.SinkWindUp );
		Say( "SINK!" );
		slammed = false;
		Sfx.Play( "ghost_vanish", GlobalPosition, 0.8f, 1.4f );
	}

	/// <summary> The player is slammed into the floor and starts to sink. </summary>
	void Slam( PlayerSurvivor prey )
	{
		if ( prey?.Controller is not { } c || buried is not null || c.Scripted ) return;
		buried = prey;
		Sinking = this;
		var floor = Sculpt.FloorBelow( this, c.GlobalPosition + Vector3.Up * 0.5f, 30f );
		buryAt = c.GlobalPosition with { Y = MathF.Min( c.GlobalPosition.Y, floor + 0.02f ) };
		if ( c.GlobalPosition.Y - floor > 0.3f ) buryAt.Y = floor;
		sinceBuried = 0f;
		sinceTick = 0f;
		Mashes = 0;
		MashesNeeded = Rng.Int( 10, 12 );
		c.Scripted = true;
		c.Velocity = Vector3.Zero;
		Sfx.Play( "dive_impact", c.GlobalPosition, 1f, 0.8f );
		c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 1.2f );
		Controls.Rumble( 0.6f );
		gore?.GooBurst( buryAt + Vector3.Up * 0.3f, Vector3.Up, GooColor, 8, 180f, 0.7f );
		// A mound of churned sand round them.
		mound = new Node3D { Name = "Davey Sink" };
		GetTree().CurrentScene.AddChild( mound );
		mound.GlobalPosition = buryAt;
		var sand = Sculpt.Paint( new Color( 0.62f, 0.56f, 0.42f ), 0.95f );
		var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.35f, OuterRadius = 0.85f, Rings = 20, RingSegments = 8 }, MaterialOverride = sand, Scale = new Vector3( 1f, 0.45f, 1f ) };
		mound.AddChild( ring );
		for ( var i = 0; i < 7; i++ )
		{
			var a = i * MathF.Tau / 7f + Rng.Float( -0.3f, 0.3f );
			Sculpt.Blob( mound, new Vector3( MathF.Cos( a ) * 0.75f, 0.05f, MathF.Sin( a ) * 0.75f ), new Vector3( 0.4f, 0.22f, 0.35f ) * Rng.Float( 0.8f, 1.3f ), sand );
		}
		if ( AutoTest.Requested ) GD.Print( $"[autotest] davey slams the player in: {MashesNeeded} presses to get out" );
	}

	/// <summary> One press of jump while buried (also what the jump action does). </summary>
	public void Mash()
	{
		if ( buried is null ) return;
		Mashes++;
		Sfx.Play2D( "ui_click", 0.6f, 0.8f + Mashes * 0.02f );
		if ( buried.Controller?.GetNodeOrNull<CameraJuice>( "CameraJuice" ) is { } juice ) juice.Shake( 0.15f );
		if ( Mashes >= MashesNeeded ) EndSink( true );
	}

	void UpdateSink()
	{
		var c = buried?.Controller;
		if ( c is null || !IsInstanceValid( c ) || !buried.IsAlive ) { EndSink( false ); return; }
		if ( buried == PlayerSurvivor.Local && Input.IsActionJustPressed( "jump" ) ) Mash();
		if ( buried is null ) return;
		// Deeper as time goes on, a little way back up with each press.
		var depth = MaxBury * MathX.Clamp( (float)sinceBuried / BuryTime, 0f, 1f ) * (1f - 0.6f * Mashes / (float)MashesNeeded);
		c.GlobalPosition = buryAt + Vector3.Down * (depth + 0.15f);
		c.Velocity = Vector3.Zero;
		if ( sinceTick >= SinkTick )
		{
			sinceTick = 0f;
			buried.Health?.OnDamage( new DamageInfo { Damage = SinkDamage, Attacker = this, Position = c.GlobalPosition + Vector3.Up * 1f, Origin = GlobalPosition } );
		}
	}

	void EndSink( bool climbedOut )
	{
		if ( buried?.Controller is { } c && IsInstanceValid( c ) )
		{
			c.Scripted = false;
			if ( climbedOut )
			{
				c.GlobalPosition = buryAt + Vector3.Up * 0.1f;
				c.Velocity = Vector3.Up * 6f;
				c.ResetPhysicsInterpolation();
				Sfx.Play( "dive_whoosh", c.GlobalPosition, 0.8f, 1.2f );
			}
		}
		if ( mound is not null && IsInstanceValid( mound ) ) mound.QueueFree();
		mound = null;
		buried = null;
		if ( Sinking == this ) Sinking = null;
	}

	// ---- The grab and the locker ----

	void StartGrab()
	{
		lockerDone = true;
		var prey = Target;
		if ( prey?.Controller is not { } c ) return;
		EndSink( false );
		grabbed = prey;
		grabGround = c.GlobalPosition;
		grabFrom = GlobalPosition;
		c.Scripted = true;
		c.Velocity = Vector3.Zero;
		health.Invulnerable = true;
		fade = 1f;
		body.Rotation = Vector3.Zero;
		Shootable( true );
		fading = false;
		lifting = false;
		SetMode( Mode.Grab );
		Say( "TO DAVEY'S LOCKER!" );
		Sfx.Play2D( "boss_spawn", 1f, 0.7f );
		if ( AutoTest.Requested ) GD.Print( "[autotest] davey grabs the player for his locker" );
	}

	/// <summary> He swoops in, lifts the player up, the view goes dark and they are both in his locker room. </summary>
	void Grabbing( float dt )
	{
		var c = grabbed?.Controller;
		if ( c is null || !IsInstanceValid( c ) || !grabbed.IsAlive ) { ReleaseGrab(); SetMode( Mode.Float ); return; }
		var t = (float)inMode;
		var front = -GlobalBasis.Z.Flat().SafeNormal();
		if ( t < 0.7f )
		{
			// Swoop to just in front of them.
			var to = c.GlobalPosition - front * 1.6f + Vector3.Up * 1.2f;
			GlobalPosition = grabFrom.Lerp( to, MathX.Clamp( t / 0.7f, 0f, 1f ) );
			Face( c.GlobalPosition - GlobalPosition, 8f, dt );
		}
		else
		{
			// Up into the water with them on his hook, turning.
			if ( !lifting ) { lifting = true; liftFrom = GlobalPosition; }
			var lift = MathX.Clamp( (t - 0.7f) / 1.1f, 0f, 1f );
			RotateY( dt * 2.5f * lift );
			GlobalPosition = liftFrom + Vector3.Up * 3.5f * lift * lift;
			c.GlobalPosition = GlobalPosition + -GlobalBasis.Z * 1.3f * Size + Vector3.Down * 1.6f;
			c.Velocity = Vector3.Zero;
			if ( !fading && t >= 1.2f )
			{
				fading = true;
				ScreenFade.Run( this, 0.45f, 0.35f, 0.6f, EnterLocker );
			}
		}
	}

	void EnterLocker()
	{
		if ( !IsInstanceValid( this ) || grabbed?.Controller is not { } c || !IsInstanceValid( c ) ) return;
		var ground = Nav.Closest( c, grabGround );
		DaveyLocker.Enter( c, ground == Vector3.Zero || ground.DistanceTo( grabGround ) > 6f ? grabGround : ground );
		GlobalPosition = DaveyLocker.DaveySpot + Vector3.Up * HoverHeight;
		GlobalBasis = Shapes.LookAt( Vector3.Back );
		floorY = DaveyLocker.Floor;
		velocity = Vector3.Zero;
		health.Invulnerable = false;
		ReleaseGrab();
		SetMode( Mode.Appear );
		fade = 0.3f;
		nextAttack = 2.5f;
		Sfx.Play2D( "ghost_appear", 1f, 0.8f );
	}

	void ReleaseGrab()
	{
		if ( grabbed?.Controller is { } c && IsInstanceValid( c ) ) c.Scripted = false;
		health.Invulnerable = false;
		grabbed = null;
	}

	// ---- Hits and death ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, damage, direction, position, origin, shooter, scoreHit ) ) return;   // online: the host takes the hit and sends it
		if ( IsDead || Hidden ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		var multiplier = index == 0 ? HeadMultiplier : index == 2 ? TailMultiplier : 1f;
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter } );
		gore?.GooBurst( position, -direction, GooColor, 2, 140f, 0.5f );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, index == 0, scoreHit );
	}

	protected override void OnDied()
	{
		EndSink( false );
		ReleaseGrab();
		SetMode( Mode.Dead );
		Shootable( false );
		RaiseKilled();
		Sfx.Play( "ghost_vanish", GlobalPosition, 1.2f, 0.6f );
		Sfx.Play2D( "boss_spawn", 0.7f, 0.5f );
		gore?.GooBurst( GlobalPosition + Vector3.Up * Size, Vector3.Up, GooColor, 24, 360f, 1.4f );
		// His hat comes off and drifts down.
		if ( hat is not null && IsInstanceValid( hat ) )
		{
			Gib.Attach( hat, gore, 0.5f / U.In, 60f, 3f, Vector3.Up * 2f + lastShotDirection.Flat() * 2f, GooColor ).AngularVelocity = Rng.InSphere( 3f );
			hat = null;
		}
		// Taken back up 6 seconds after he dies in the locker.
		if ( InLocker )
		{
			var tree = GetTree();
			tree.CreateTimer( 6.0 ).Timeout += () => { if ( tree.CurrentScene is { } scene ) ScreenFade.Run( scene, 0.45f, 0.35f, 0.6f, DaveyLocker.Leave ); };
		}
	}

	/// <summary> Dead: he spins up and thins out into nothing. </summary>
	void Dying( float dt )
	{
		var t = (float)timeSinceDeath;
		fade = MathX.Clamp( 1f - t / 1.8f, 0f, 1f );
		body.RotateY( dt * (2f + t * 6f) );
		GlobalPosition += Vector3.Up * dt * 0.8f;
		if ( Rng.Float() < 0.3f ) gore?.GooSpray( GlobalPosition + Rng.InSphere( Size ), Vector3.Up, 3, GooColor, 200f );
		if ( t > 2.2f ) QueueFree();
	}

	// ---- Animation ----

	void Animate( float t, float dt )
	{
		foreach ( var m in ghosts )
		{
			m.SetShaderParameter( "fade", fade );
			m.SetShaderParameter( "flash", MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f ) * 0.6f );
		}
		if ( light is not null ) light.LightEnergy = 1.4f * fade;
		body.Position = new Vector3( 0f, MathF.Sin( t * 1.6f ) * 0.12f, 0f );
		for ( var i = 0; i < tail.Count; i++ )
			tail[i].Rotation = new Vector3( -0.17f - 0.05f * MathF.Sin( t * 2f - i * 0.6f ), 0f, 0.16f * MathF.Sin( t * 1.7f - i * 0.7f ) );
		if ( tailBones is not null ) SmoothKit.PoseChain( tailBones, tail );
		head.Rotation = new Vector3( 0.05f * MathF.Sin( t * 1.1f ), 0.1f * MathF.Sin( t * 0.7f ), 0f );

		// Arms: idle (cutlass up, hook out), swipe (up and back, then across), cannons and sink (both up, then down).
		Vector3 r = new( 0.5f, 0.15f, 0.25f ), rFore = new( 1.1f, 0f, 0f );
		Vector3 l = new( 0.4f, -0.1f, -0.3f ), lFore = new( 1.0f, 0f, 0f );
		var k = (float)inMode;
		switch ( mode )
		{
			case Mode.Swipe:
				if ( k < SwipeWindUp ) { var u = k / SwipeWindUp; r = new Vector3( MathX.Lerp( 0.5f, 2.8f, u ), 0.3f, MathX.Lerp( 0.25f, 0.6f, u ) ); rFore = new Vector3( 0.6f, 0f, 0f ); }
				else { var u = MathX.Clamp( (k - SwipeWindUp) / 0.18f, 0f, 1f ); r = new Vector3( MathX.Lerp( 2.8f, 1.2f, u ), MathX.Lerp( 0.3f, 1.1f, u ), MathX.Lerp( 0.6f, -0.4f, u ) ); rFore = new Vector3( MathX.Lerp( 0.6f, 0.2f, u ), 0f, 0f ); }
				break;
			case Mode.Cannons:
				r = new Vector3( 2.6f, 0f, 0.5f ); l = new Vector3( 2.6f, 0f, -0.5f ); rFore = lFore = new Vector3( 0.3f, 0f, 0f );
				break;
			case Mode.SinkWindUp:
				var down = MathX.Clamp( (k - 0.75f) / 0.15f, 0f, 1f );
				r = new Vector3( MathX.Lerp( 2.9f, 1.0f, down ), 0f, 0.2f ); l = new Vector3( MathX.Lerp( 2.9f, 1.0f, down ), 0f, -0.2f );
				rFore = lFore = new Vector3( MathX.Lerp( 0.2f, 0.5f, down ), 0f, 0f );
				break;
			case Mode.Grab:
				l = new Vector3( 1.3f, 0f, -0.1f ); lFore = new Vector3( 0.4f, 0f, 0f );
				break;
		}
		var ease = 1f - MathF.Exp( -(mode == Mode.Swipe ? 30f : 8f) * dt );
		shoulderR.Rotation = shoulderR.Rotation.Lerp( r, ease );
		elbowR.Rotation = elbowR.Rotation.Lerp( rFore, ease );
		shoulderL.Rotation = shoulderL.Rotation.Lerp( l, ease );
		elbowL.Rotation = elbowL.Rotation.Lerp( lFore, ease );
	}
}

/// <summary>
/// One of Davey's ghost cannons: it rises out of the floor facing the player, its fuse fizzes (cannon_fuse) and burns
/// down, it fires one cannonball at them with a boom and a kick, then fades away.
/// </summary>
public partial class DaveyCannon : Node3D
{
	public static readonly List<DaveyCannon> All = new();
	/// <summary> Tests: when each cannon fired (Clock time). </summary>
	public static readonly List<float> FireTimes = new();
	public const float FuseTime = 1.25f;

	Creature owner;
	PlayerSurvivor target;
	float lightAt;
	float damage;
	TimeSince alive = 0f;
	bool lit, fired;
	TimeSince sinceFired;
	Node3D barrel, spark;
	OmniLight3D sparkLight;
	readonly List<StandardMaterial3D> paints = new();

	public static void Raise( Creature owner, Vector3 at, PlayerSurvivor target, float lightAfter, float damage )
	{
		var cannon = new DaveyCannon { Name = "Davey Cannon", owner = owner, target = target, lightAt = lightAfter, damage = damage };
		owner.GetTree().CurrentScene.AddChild( cannon );
		cannon.GlobalPosition = at;
		cannon.Build();
	}

	public override void _EnterTree() => All.Add( this );
	public override void _ExitTree() => All.Remove( this );

	StandardMaterial3D Paint( Color c, float rough, float metal, float glow )
	{
		var m = Sculpt.Paint( c, rough, metal );
		m.EmissionEnabled = true;
		m.Emission = new Color( 0.3f, 1f, 0.4f );
		m.EmissionEnergyMultiplier = glow;
		m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		paints.Add( m );
		return m;
	}

	void Build()
	{
		var iron = Paint( new Color( 0.12f, 0.14f, 0.13f ), 0.4f, 0.7f, 0.15f );
		var wood = Paint( new Color( 0.3f, 0.2f, 0.12f ), 0.85f, 0f, 0.1f );
		var rope = Paint( new Color( 0.55f, 0.45f, 0.3f ), 0.9f, 0f, 0f );
		// The carriage and its wheels.
		Sculpt.Block( this, new Vector3( 0f, 0.32f, 0.1f ), new Vector3( 0.7f, 0.32f, 1.2f ), wood );
		foreach ( var x in new[] { -0.4f, 0.4f } )
			foreach ( var z in new[] { -0.35f, 0.5f } )
				Sculpt.Limb( this, new Vector3( x - 0.06f, 0.24f, z ), new Vector3( x + 0.06f, 0.24f, z ), 0.24f, 0.24f, wood, "Wheel" );
		// The barrel on its trunnions, pitched by `barrel`; the fuse on top of the breech.
		barrel = Sculpt.Joint( this, new Vector3( 0f, 0.62f, 0.2f ), "Barrel" );
		Sculpt.Limb( barrel, new Vector3( 0f, 0f, 0.55f ), new Vector3( 0f, 0f, -1.1f ), 0.27f, 0.21f, iron, "Tube" );
		Sculpt.Blob( barrel, new Vector3( 0f, 0f, 0.62f ), Vector3.One * 0.36f, iron, name: "Breech" );
		Sculpt.Limb( barrel, new Vector3( 0f, 0f, -1.02f ), new Vector3( 0f, 0f, -1.16f ), 0.26f, 0.26f, iron, "Muzzle" );
		Sculpt.Limb( barrel, new Vector3( 0f, 0f, -0.2f ), new Vector3( 0f, 0f, -0.3f ), 0.29f, 0.29f, iron, "Band" );
		Sculpt.Limb( barrel, new Vector3( 0f, 0.2f, 0.45f ), new Vector3( 0f, 0.38f, 0.55f ), 0.025f, 0.02f, rope, "Fuse" );
		spark = Sculpt.Joint( barrel, new Vector3( 0f, 0.38f, 0.55f ), "Spark" );
		Sculpt.Blob( spark, Vector3.Zero, Vector3.One * 0.09f, Sculpt.Glow( new Color( 1f, 0.7f, 0.2f ), 4f ) );
		sparkLight = new OmniLight3D { LightColor = new Color( 1f, 0.6f, 0.2f ), LightEnergy = 0f, OmniRange = 3f };
		spark.AddChild( sparkLight );
		spark.Visible = false;
		Scale = Vector3.One * 0.01f;
		Aim( 1f );
		Sfx.Play( "bubbles", GlobalPosition, 0.6f, 0.7f );
	}

	Vector3 TargetPoint => target?.Controller is { } c && IsInstanceValid( c ) ? c.GlobalPosition + Vector3.Up * 1f : GlobalPosition + -GlobalBasis.Z * 10f;

	void Aim( float rate )
	{
		var to = TargetPoint - GlobalPosition;
		if ( to.Flat().IsNearlyZero() ) return;
		var want = Shapes.LookAt( to.Flat().SafeNormal() );
		GlobalBasis = new Basis( GlobalBasis.Orthonormalized().GetRotationQuaternion().SafeSlerp( want.GetRotationQuaternion(), MathX.Clamp( rate, 0f, 1f ) ) ).Scaled( Scale );
		var muzzle = barrel.GlobalPosition;
		var pitch = MathF.Atan2( TargetPoint.Y - muzzle.Y, (TargetPoint - muzzle).Flat().Length() );
		barrel.Rotation = new Vector3( pitch, 0f, 0f );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var t = (float)alive;
		// Rising out of the floor with a pop.
		var grow = MathX.Clamp( t / 0.35f, 0f, 1f );
		var s = grow < 1f ? 0.01f + grow * (1.15f - 0.15f * grow) : 1f;
		Scale = Vector3.One * s;
		if ( !fired ) Aim( dt * 4f );
		if ( !lit && t >= lightAt )
		{
			lit = true;
			spark.Visible = true;
			Sfx.Play( "cannon_fuse", spark.GlobalPosition, 1f );
		}
		if ( lit && !fired )
		{
			var burn = MathX.Clamp( (t - lightAt) / FuseTime, 0f, 1f );
			spark.Position = new Vector3( 0f, 0.38f - 0.18f * burn, 0.55f - 0.1f * burn );
			spark.Scale = Vector3.One * (0.8f + 0.5f * Rng.Float());
			sparkLight.LightEnergy = 1.5f + Rng.Float();
			if ( burn >= 1f ) Fire();
		}
		if ( fired )
		{
			// Recoil, then fade away.
			barrel.Position = new Vector3( 0f, 0.62f, 0.2f + 0.25f * MathF.Exp( -(float)sinceFired * 8f ) );
			var a = MathX.Clamp( 1f - ((float)sinceFired - 0.5f) / 0.6f, 0f, 1f );
			foreach ( var m in paints ) m.AlbedoColor = m.AlbedoColor with { A = a };
			if ( sinceFired > 1.1f ) QueueFree();
		}
	}

	void Fire()
	{
		fired = true;
		sinceFired = 0f;
		spark.Visible = false;
		FireTimes.Add( (float)Clock.Now );
		var muzzle = barrel.GlobalPosition + barrel.GlobalBasis.Z.Normalized() * -1.2f;
		var aim = TargetPoint;
		Cannonball.Shoot( owner, muzzle, (aim - muzzle).SafeNormal() * 20f, damage, target );
		Sfx.Play( "cannon_fire", muzzle, 1.1f, Rng.Float( 0.9f, 1.05f ) );
		AlienBlast.Spawn( GetTree().CurrentScene, muzzle, 0.35f );
		if ( target?.Controller?.GetNodeOrNull<CameraJuice>( "CameraJuice" ) is { } juice ) juice.Shake( 0.3f );
	}
}

/// <summary> A ghostly iron cannonball: flies straight, hurts the player it reaches, bursts on the world. </summary>
public partial class Cannonball : Node3D
{
	public static int Hits;
	Vector3 velocity;
	float damage;
	Creature owner;
	TimeSince alive = 0f;

	public static void Shoot( Creature owner, Vector3 from, Vector3 velocity, float damage, PlayerSurvivor aimedAt )
	{
		var ball = new Cannonball { Name = "Cannonball", velocity = velocity, damage = damage, owner = owner };
		owner.GetTree().CurrentScene.AddChild( ball );
		ball.GlobalPosition = from;
		var iron = Sculpt.Paint( new Color( 0.1f, 0.12f, 0.11f ), 0.35f, 0.8f );
		iron.EmissionEnabled = true;
		iron.Emission = new Color( 0.3f, 1f, 0.4f );
		iron.EmissionEnergyMultiplier = 0.3f;
		Sculpt.Blob( ball, Vector3.Zero, Vector3.One * 0.36f, iron );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var from = GlobalPosition;
		var to = from + velocity * dt;
		// The player it reaches (any living one along the step).
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c ) continue;
			var chest = c.GlobalPosition + Vector3.Up * 1f;
			if ( DistanceToSegment( chest, from, to ) > 0.8f ) continue;
			Hits++;
			p.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = IsInstanceValid( owner ) ? owner : null, Position = chest, Origin = from } );
			c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.9f );
			if ( !c.Scripted ) c.Velocity += velocity.Flat().SafeNormal() * 5f + Vector3.Up * 2f;
			Burst( chest );
			return;
		}
		var hit = Trace.Ray( this, from, to, Layers.World );
		if ( hit.Hit ) { Burst( hit.HitPosition ); return; }
		GlobalPosition = to;
		Rotation += new Vector3( 8f, 3f, 0f ) * dt;
		if ( alive > 4f ) QueueFree();
	}

	void Burst( Vector3 at )
	{
		AlienBlast.Spawn( GetTree().CurrentScene, at, 0.5f );
		Sfx.Play( "hit_world", at, 1f, 0.6f );
		QueueFree();
	}

	static float DistanceToSegment( Vector3 p, Vector3 a, Vector3 b )
	{
		var ab = b - a;
		var t = ab.LengthSquared() > 1e-6f ? MathX.Clamp( (p - a).Dot( ab ) / ab.LengthSquared(), 0f, 1f ) : 0f;
		return p.DistanceTo( a + ab * t );
	}
}

/// <summary> A fade to black and back, with something done while the screen is dark. </summary>
public static class ScreenFade
{
	public static void Run( Node any, float outSeconds, float hold, float inSeconds, Action atBlack )
	{
		var scene = any.GetTree().CurrentScene;
		var layer = new CanvasLayer { Layer = 50, Name = "Screen Fade" };
		scene.AddChild( layer );
		var rect = new ColorRect { Color = new Color( 0.01f, 0.06f, 0.03f, 0f ), MouseFilter = Control.MouseFilterEnum.Ignore };
		rect.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		layer.AddChild( rect );
		var tween = layer.CreateTween();
		tween.TweenProperty( rect, "color:a", 1f, outSeconds );
		tween.TweenCallback( Callable.From( () => atBlack?.Invoke() ) );
		tween.TweenInterval( hold );
		tween.TweenProperty( rect, "color:a", 0f, inSeconds );
		tween.TweenCallback( Callable.From( layer.QueueFree ) );
	}
}
