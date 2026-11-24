using Godot;

/// <summary>
/// The garden eel's looks (after the user's photos of spotted garden eels): a long thin body (green, purple or orange) covered in small black
/// spots with a couple of big black blotches, one smooth skinned tube standing up out of its hole and curling over at the
/// top so the head looks forward; a blunt head with big yellow eyes, and angry (user's ask): low slanted brows, a frown
/// and an underbite of little teeth on a jaw that gapes to bite.
/// </summary>
public partial class GardenEel
{
	/// <summary> The skin between the spots (user's ask: not white): green, purple or orange, one picked per eel. </summary>
	public static readonly Color[] Skins = { new( 0.42f, 0.8f, 0.28f ), new( 0.6f, 0.36f, 0.85f ), new( 0.97f, 0.53f, 0.16f ) };
	int tint;
	/// <summary> Which of <see cref="Skins"/> it wears (sent to joiners, so their copy matches). </summary>
	public int Tint
	{
		get => tint;
		set
		{
			tint = Math.Clamp( value, 0, Skins.Length - 1 );
			bodyPaint?.SetShaderParameter( "cream", Skins[tint] );
			foreach ( var m in headPaints ) m.SetShaderParameter( "cream", Skins[tint] );
		}
	}
	static readonly Color SpotBlack = new( 0.06f, 0.05f, 0.05f );
	static readonly Color Blood = new( 0.72f, 0.1f, 0.1f );

	static readonly Shader SpotShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 cream : source_color = vec3(0.93, 0.9, 0.8);
uniform vec3 spot : source_color = vec3(0.06, 0.05, 0.05);
uniform float head = 0.0;          // 0 the body (spots from its UV), 1 a part of the head (spots from where it is)
uniform vec3 size = vec3(1.0);     // the head part's size, so its spots are round
uniform float flash = 0.0;
varying vec3 lp;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float hash3(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
void vertex() { lp = VERTEX * size; }
void fragment() {
	float dots = 0.0;
	if (head < 0.5) {
		// Round the body 9 cells, along it 40 (about square on a 1.5 m body 0.35 m round).
		vec2 g = vec2(UV.x * 9.0, UV.y * 40.0);
		vec2 cell = floor(g);
		vec2 c = vec2(hash(cell), hash(cell + 7.3)) * 0.5 + 0.25;
		dots = 1.0 - smoothstep(0.24, 0.3, length(fract(g) - c));
		// The big blotches: one each side behind the head and one half way down (after the photos).
		vec2 b1 = vec2(min(abs(UV.x - 0.25), abs(UV.x - 0.75)) * 9.0, (UV.y - 0.84) * 40.0);
		vec2 b2 = vec2(min(abs(UV.x - 0.25), abs(UV.x - 0.75)) * 9.0, (UV.y - 0.56) * 40.0);
		dots = max(dots, 1.0 - smoothstep(0.85, 1.0, length(b1 * vec2(0.9, 0.55))));
		dots = max(dots, 1.0 - smoothstep(0.75, 0.9, length(b2 * vec2(1.0, 0.6))));
	} else {
		vec3 g = lp * 60.0;
		vec3 cell = floor(g);
		vec3 c = vec3(hash3(cell), hash3(cell + 3.1), hash3(cell + 5.7)) * 0.5 + 0.25;
		dots = 1.0 - smoothstep(0.26, 0.32, length(fract(g) - c));
	}
	vec3 col = mix(cream, spot, dots);
	ALBEDO = mix(col, vec3(2.0), flash);
	ROUGHNESS = 0.45;
	SPECULAR = 0.45;
}",
	};

	Node3D body, rising, head, jaw;
	readonly List<Node3D> joints = new();
	Skeleton3D bones;
	MeshInstance3D holeMark;
	ShaderMaterial bodyPaint;
	readonly List<ShaderMaterial> headPaints = new();
	readonly List<CreaturePart> parts = new();

	ShaderMaterial Spotted( bool onHead, Vector3 size )
	{
		var m = new ShaderMaterial { Shader = SpotShader };
		m.SetShaderParameter( "cream", Skins[tint] );
		m.SetShaderParameter( "spot", SpotBlack );
		m.SetShaderParameter( "head", onHead ? 1f : 0f );
		m.SetShaderParameter( "size", size );
		if ( onHead ) headPaints.Add( m );
		return m;
	}

	static readonly Shader HoleShader = new()
	{
		Code = @"shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back;
uniform sampler2D sand_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform float textured = 1.0;      // the sea bed's sand texture (the wreck site), or a flat colour (Atlantis's canals)
uniform vec3 tint : source_color = vec3(0.86, 0.85, 0.83);
varying vec3 wp;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
void vertex() { wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
	float r = UV.x;
	// The floor's own sand where it is, mapped and shaded as the ground is, so the edge melts into it.
	vec3 sand = textured > 0.5 ? texture(sand_tex, wp.xz * 0.12).rgb * tint : tint;
	// Churned sand round the mouth: a little darker and grainier toward the hole.
	float g = hash(floor(wp.xz * 160.0));
	vec3 c = sand * mix(0.8, 1.0, smoothstep(0.25, 0.8, r)) * mix(1.0, 0.9 + 0.2 * g, 1.0 - smoothstep(0.3, 0.9, r));
	// The mouth: dark and soft edged, as if it goes down into shadow.
	c = mix(c, sand * 0.1, 1.0 - smoothstep(0.16, 0.3, r));
	ALBEDO = c;
	ROUGHNESS = 1.0;
	ALPHA = 1.0 - smoothstep(0.55, 1.0, r);
}",
	};

	const float HoleRadius = 0.24f;

	/// <summary>
	/// The hole, once it has dug out: a dark mouth sunk in a low lip of churned sand, laid on the ground it sits on
	/// (heights from the floor under each point) and drawn in the floor's own sand, fading out at its edge.
	/// </summary>
	void ShowHole()
	{
		if ( holeMark is null )
		{
			var centre = GlobalPosition;
			var st = new SurfaceTool();
			st.Begin( Mesh.PrimitiveType.Triangles );
			const int rings = 9, segs = 28;
			for ( var i = 0; i <= rings; i++ )
				for ( var k = 0; k < segs; k++ )
				{
					var r = HoleRadius * i / rings;
					var a = MathF.Tau * k / segs;
					var flat = new Vector3( MathF.Cos( a ) * r, 0f, MathF.Sin( a ) * r );
					var ground = Sculpt.FloorBelow( this, centre + flat + Vector3.Up * 0.4f, 1.2f );
					if ( ground < centre.Y - 0.6f ) ground = centre.Y;
					// Sunk in the middle, a low lip of thrown up sand round it, back to the ground at the edge.
					var lip = 0.018f * MathF.Exp( -MathF.Pow( (r - 0.09f) / 0.03f, 2f ) );
					var sink = -0.03f * MathF.Max( 0f, 1f - r / 0.065f );
					st.SetUV( new Vector2( r / HoleRadius, 0f ) );
					st.AddVertex( new Vector3( flat.X, ground - centre.Y + 0.006f + lip + sink, flat.Z ) );
				}
			for ( var i = 0; i < rings; i++ )
				for ( var k = 0; k < segs; k++ )
				{
					int a = i * segs + k, b = i * segs + (k + 1) % segs, c = a + segs, d = b + segs;
					st.AddIndex( a ); st.AddIndex( c ); st.AddIndex( b );
					st.AddIndex( b ); st.AddIndex( c ); st.AddIndex( d );
				}
			st.GenerateNormals();
			var m = new ShaderMaterial { Shader = HoleShader };
			var canal = SeaTerrain.Area == 3;
			if ( !canal && Stingray.Sand() is { } tex ) m.SetShaderParameter( "sand_tex", tex );
			m.SetShaderParameter( "textured", canal ? 0f : 1f );
			m.SetShaderParameter( "tint", canal ? Atlantis.CanalSand : SeaTerrain.ShadeAt( centre ) );
			holeMark = new MeshInstance3D { Name = "Hole", Mesh = st.Commit(), MaterialOverride = m, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			AddChild( holeMark );
			holeMark.GlobalTransform = new Transform3D( Basis.Identity, centre );
		}
		holeMark.Visible = true;
	}

	void Build()
	{
		body = new Node3D { Name = "Body" };
		AddChild( body );

		// The body: a chain of joints standing up out of the hole (built hanging down, turned over), one skinned tube.
		rising = new Node3D { Name = "Rising" };
		body.AddChild( rising );
		var flip = new Node3D { Name = "Upright", Rotation = new Vector3( 0f, 0f, MathF.PI ) };
		rising.AddChild( flip );
		Node3D parent = flip;
		for ( var i = 0; i < Joints; i++ )
		{
			var j = Sculpt.Joint( parent, i == 0 ? Vector3.Zero : new Vector3( 0f, -Step, 0f ), $"Eel {i}" );
			joints.Add( j );
			parent = j;
		}
		bodyPaint = Spotted( false, Vector3.One );
		bones = SmoothKit.SkinChain( flip, joints, Step, 0.04f, 0.06f, t => (Radius * MathX.Lerp( 0.9f, 1.05f, t ), Radius * MathX.Lerp( 0.9f, 1.05f, t )), bodyPaint, "garden eel", 36, 12 );

		// The head, on the last joint: its snout (-Z) along the chain, its top (+Y) to the back of the body.
		head = new Node3D { Name = "Eel Head" };
		joints[^1].AddChild( head );
		head.Transform = new Transform3D( new Basis( new Vector3( -1f, 0f, 0f ), new Vector3( 0f, 0f, 1f ), new Vector3( 0f, 1f, 0f ) ), new Vector3( 0f, -0.05f, 0f ) );
		var skull = new Vector3( 0.13f, 0.12f, 0.19f );
		SmoothKit.Ball( head, new Vector3( 0f, 0f, -0.045f ), skull, Spotted( true, skull ), null, "Skull" );
		// Eyes: big and round, yellow with a black pupil, set high on the sides looking forward.
		var iris = Sculpt.Paint( new Color( 1f, 0.78f, 0.08f ), 0.15f );
		iris.EmissionEnabled = true;
		iris.Emission = new Color( 1f, 0.7f, 0.05f );
		iris.EmissionEnergyMultiplier = 0.25f;
		var pupil = Sculpt.Paint( new Color( 0.01f, 0.01f, 0.012f ), 0.05f );
		pupil.ClearcoatEnabled = true;
		pupil.Clearcoat = 1f;
		var brow = Sculpt.Paint( new Color( 0.16f, 0.13f, 0.11f ), 0.7f );
		foreach ( var x in new[] { -1f, 1f } )
		{
			var at = new Vector3( x * 0.052f, 0.028f, -0.085f );
			var turn = new Basis( new Quaternion( Vector3.Back, new Vector3( x * 0.75f, 0.1f, -0.65f ).Normalized() ) );
			SmoothKit.Ball( head, at, new Vector3( 0.05f, 0.05f, 0.05f ), iris, null, "Eye" );
			SmoothKit.Ball( head, at + turn * new Vector3( 0f, 0f, 0.018f ), new Vector3( 0.03f, 0.03f, 0.02f ), pupil, turn, "Pupil" );
			// The angry brow: a dark ridge over the eye, its inner end dragged down toward the snout.
			var tilt = new Basis( Vector3.Back, x * MathX.Rad( 28f ) ) * new Basis( Vector3.Up, -x * MathX.Rad( 20f ) );
			SmoothKit.Ball( head, at + new Vector3( -x * 0.004f, 0.03f, -0.006f ), new Vector3( 0.062f, 0.016f, 0.03f ), brow, tilt, "Brow" );
		}
		// The frown: the mouth's corners turned down.
		var lip = Sculpt.Paint( new Color( 0.2f, 0.12f, 0.1f ), 0.6f );
		foreach ( var x in new[] { -1f, 1f } )
			SmoothKit.Ball( head, new Vector3( x * 0.045f, -0.035f, -0.075f ), new Vector3( 0.012f, 0.012f, 0.06f ), lip, new Basis( Vector3.Up, x * MathX.Rad( 18f ) ) * new Basis( Vector3.Right, MathX.Rad( 22f ) ), "Frown" );
		// The jaw: an underbite jutting past the snout, little teeth standing up from it.
		jaw = Sculpt.Joint( head, new Vector3( 0f, -0.035f, -0.01f ), "Jaw" );
		var chin = new Vector3( 0.105f, 0.045f, 0.17f );
		SmoothKit.Ball( jaw, new Vector3( 0f, -0.012f, -0.075f ), chin, Spotted( true, chin ), null, "Chin" );
		SmoothKit.Ball( jaw, new Vector3( 0f, 0.004f, -0.08f ), new Vector3( 0.08f, 0.02f, 0.13f ), Sculpt.Paint( new Color( 0.55f, 0.15f, 0.18f ), 0.5f ), null, "Mouth" );
		var tooth = Sculpt.Paint( new Color( 0.97f, 0.96f, 0.9f ), 0.3f );
		for ( var k = 0; k < 7; k++ )
		{
			var a = (k - 3f) / 3f;
			var foot = new Vector3( a * 0.034f, 0.01f, -0.15f + MathF.Abs( a ) * 0.035f );
			Sculpt.Cone( jaw, foot, foot + new Vector3( 0f, 0.022f, -0.004f ), 0.006f, tooth, 5, "Tooth" );
		}

		// What can be shot: the head (0) and three along the body.
		var hp = CreaturePart.Create( this, 0, "Eel Head", 0.1f );
		head.AddChild( hp );
		hp.Position = new Vector3( 0f, 0f, -0.05f );
		parts.Add( hp );
		for ( var k = 1; k <= 3; k++ )
		{
			var p = CreaturePart.Create( this, k, $"Eel Body {k}", 0.085f );
			joints[Joints - 1 - k * 2].AddChild( p );
			parts.Add( p );
		}
	}

	/// <summary> How many times an eel bent or held back to keep off a neighbour (the test reads it). </summary>
	public static int Squeezes;
	/// <summary> Its body's points (the joints, the head, the snout's tip) where it was posed this frame, for its neighbours. </summary>
	public readonly Vector3[] Spine = new Vector3[Joints + 2];
	bool spineSet;
	readonly List<GardenEel> near = new();
	const float BodyGap = 0.13f, HeadGap = 0.17f;
	/// <summary> About how far the head reaches round its centre line (the skull is a little wider than the body). </summary>
	const float HeadSize = 0.075f;

	/// <summary> The other eels close enough to touch it, standing out of their holes. </summary>
	void Neighbours()
	{
		near.Clear();
		if ( rise < 0.2f ) return;
		foreach ( var e in All )
			if ( e != this && IsInstanceValid( e ) && e.spineSet && e.rise > 0.2f && !e.IsDead && e.GlobalPosition.DistanceSquaredTo( GlobalPosition ) < 3.2f * 3.2f ) near.Add( e );
	}

	/// <summary> Lean forward by `forward` over the lower joints and sideways by `side`, the top two curling the head over. </summary>
	void Bend( float t, float forward, float side )
	{
		var leaners = Joints - 3;
		var curl = 0.55f * MathX.Clamp( rise, 0f, 1f );
		for ( var i = 1; i < Joints; i++ )
		{
			var sway = MathF.Sin( t * 1.4f - i * 0.6f ) * 0.05f * (IsOut ? 1f : 0.4f) + (i <= leaners ? side / leaners : 0f);
			var f = i <= leaners ? forward / leaners : curl - (i == Joints - 1 ? forward * 0.25f : 0f);
			f += MathF.Sin( t * 1.1f - i * 0.5f ) * 0.025f;
			joints[i].Rotation = new Vector3( f, 0f, sway );
		}
	}

	/// <summary>
	/// How far into a neighbour its body or head goes as posed (0 when clear), and which way to bend away from the
	/// worst one (+1 or -1, the sideways bend's sign).
	/// </summary>
	float Crowded( out float away )
	{
		away = 1f;
		var worst = 0f;
		for ( var i = 2; i <= Joints + 1; i++ )
		{
			var p = i < Joints ? joints[i].GlobalPosition : i == Joints ? head.GlobalPosition : Snout();
			var gap = i < Joints ? BodyGap : HeadGap;
			var q = i < Joints ? (joints[i].GlobalPosition + joints[i - 1].GlobalPosition) * 0.5f : i == Joints ? p : (p + head.GlobalPosition) * 0.5f;
			foreach ( var e in near )
				for ( var k = 1; k <= Joints + 1; k++ )
				{
					var a = e.Spine[k - 1];
					var b = e.Spine[k];
					var into = MathF.Max( gap - PointToSegment( p, a, b ), gap - PointToSegment( q, a, b ) );
					if ( into <= worst ) continue;
					worst = into;
					// Bend away from it: the side of this eel's facing it is on, flipped.
					var right = GlobalBasis.X;
					away = (e.GlobalPosition - GlobalPosition).Dot( right ) > 0f ? 1f : -1f;
				}
		}
		return worst;
	}

	static float PointToSegment( Vector3 p, Vector3 a, Vector3 b )
	{
		var ab = b - a;
		var l = ab.LengthSquared();
		var u = l < 1e-6f ? 0f : MathX.Clamp( (p - a).Dot( ab ) / l, 0f, 1f );
		return p.DistanceTo( a + ab * u );
	}

	Vector3 Snout() => head.GlobalTransform * new Vector3( 0f, 0f, -0.13f );

	void SaveSpine()
	{
		for ( var i = 0; i < Joints; i++ ) Spine[i] = joints[i].GlobalPosition;
		Spine[Joints] = head.GlobalPosition;
		Spine[Joints + 1] = Snout();
		spineSet = rise > 0.2f;
	}

	/// <summary> The smallest gap between any two eels' skins (negative: one goes into the other), across all beds; the test reads it. </summary>
	public static float ClosestPair()
	{
		var best = float.MaxValue;
		foreach ( var a in All )
			foreach ( var b in All )
			{
				if ( a == b || a.GetInstanceId() > b.GetInstanceId() || !a.spineSet || !b.spineSet ) continue;
				if ( a.GlobalPosition.DistanceSquaredTo( b.GlobalPosition ) > 9f ) continue;
				for ( var i = 1; i <= Joints + 1; i++ )
					for ( var k = 1; k <= Joints + 1; k++ )
					{
						var d = SegmentToSegment( a.Spine[i - 1], a.Spine[i], b.Spine[k - 1], b.Spine[k] ) - (i >= Joints ? HeadSize : Radius) - (k >= Joints ? HeadSize : Radius);
						if ( d < best ) best = d;
					}
			}
		return best;
	}

	static float SegmentToSegment( Vector3 a, Vector3 b, Vector3 c, Vector3 d )
	{
		var best = float.MaxValue;
		for ( var s = 0; s <= 4; s++ )
		{
			var p = a.Lerp( b, s / 4f );
			best = MathF.Min( best, PointToSegment( p, c, d ) );
			var q = c.Lerp( d, s / 4f );
			best = MathF.Min( best, PointToSegment( q, a, b ) );
		}
		return best;
	}

	/// <summary>
	/// Each frame: how far it stands out of its hole, its sway in the current, the lean to its target (back for the
	/// wind-up, out for the bite), the curl that keeps its head looking forward, and the jaw.
	/// </summary>
	void Pose( float dt )
	{
		var t = (float)Clock.Now + phase;
		var a = (float)sinceAct;
		float riseWant, riseFreq = 2.2f, leanWant = 0.12f, leanFreq = 2f, jawWant = 0f;
		switch ( act )
		{
			case Act.Hidden: riseWant = 0f; break;
			case Act.Dig: riseWant = SmoothKit.Ease( MathX.Clamp( a / DigTime, 0f, 1f ) ); riseFreq = 3f; leanWant = 0f; break;
			case Act.Windup: riseWant = 1f; leanWant = -0.3f; leanFreq = 4f; jawWant = MathX.Clamp( a / WindupTime, 0f, 1f ) * 0.75f; break;
			case Act.Lunge:
			{
				riseWant = 1f;
				var d = target == Vector3.Zero ? 1f : (target - GlobalPosition).Flat().Length();
				leanWant = MathX.Clamp( MathF.Atan2( d, 0.8f ), 0.35f, 1.15f );
				leanFreq = 7f;
				jawWant = struck ? 0f : 0.75f;
				break;
			}
			case Act.Duck: riseWant = 0.05f; riseFreq = 5f; leanWant = 0f; break;
			case Act.Sink: riseWant = 0f; riseFreq = 0.8f; leanWant = 0f; break;
			default: riseWant = 1f; break;
		}
		if ( dt > 0f )
		{
			SmoothKit.Spring( ref rise, ref riseVel, riseWant, riseFreq, 0.85f, dt );
			SmoothKit.Spring( ref lean, ref leanVel, leanWant, leanFreq, 0.6f, dt );
		}
		else rise = riseWant;
		rise = MathX.Clamp( rise, 0f, 1.08f );
		look = MathX.Lerp( look, jawWant, 1f - MathF.Exp( -18f * MathF.Max( dt, 0.001f ) ) );

		// Out of the hole: the whole chain slides up out of the sand.
		var length = (Joints - 1) * Step + 0.3f;
		rising.Position = new Vector3( 0f, -Below - (1f - rise) * length, 0f );
		var showing = rise > 0.01f;
		rising.Visible = showing;
		if ( act != Act.Hidden ) ShowHole();
		foreach ( var p in parts ) if ( IsInstanceValid( p ) && p.Collider is not null ) p.Collider.Disabled = rise < 0.35f;

		// The bends, kept off the bed's other eels (they stand close, all leaning at the same player): try the lean as
		// wanted, then bent round the eel in the way, then less and less of it, until the body and head are clear.
		Neighbours();
		var bend = lean;
		var dodge = 0f;
		Bend( t, bend, 0f );
		if ( near.Count > 0 && rise > 0.2f )
		{
			for ( var tries = 0; tries < 8 && Crowded( out var away ) > 0f; tries++ )
			{
				Squeezes++;
				if ( tries < 2 ) dodge = away * (tries + 1) * 0.3f;
				else bend *= 0.7f;
				Bend( t, bend, dodge );
			}
		}
		SaveSpine();
		SmoothKit.PoseChain( bones, joints );
		jaw.Rotation = new Vector3( -look, 0f, 0f );   // open: the chin drops
		var flash = MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f );
		bodyPaint.SetShaderParameter( "flash", flash );
		foreach ( var m in headPaints ) m.SetShaderParameter( "flash", flash );
	}
}
