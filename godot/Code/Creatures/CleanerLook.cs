using Godot;

/// <summary>
/// The cleaner wrasse's looks, after the bluestreak cleaner wrasse: a slim body built in code, pale yellow-white at the
/// front going electric blue to the tail, a bold black stripe from the snout through the eye that widens to the tail
/// with a glowing blue edge, the classic alien eyes (small here), a long low dorsal fin, fluttering pectorals and a fan
/// tail. The link is a green beam from its mouth to its patient with bubbles running along it.
/// </summary>
public partial class CleanerWrasse
{
	static readonly Color GooColour = new( 0.35f, 0.85f, 1f );
	static readonly Color BlueColour = new( 0.12f, 0.45f, 1f );
	static readonly Color PaleColour = new( 0.97f, 0.93f, 0.72f );
	static readonly Color StripeColour = new( 0.02f, 0.02f, 0.04f );
	static readonly Color EdgeGlow = new( 0.3f, 0.75f, 1f );
	public static readonly Color BeamColour = new( 0.35f, 1f, 0.55f );
	static readonly StandardMaterial3D EyeBlack = new()
	{
		AlbedoColor = new Color( 0.01f, 0.01f, 0.015f ), Roughness = 0.06f, ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.04f, Metallic = 0.2f,
	};
	static readonly StandardMaterial3D EyeRimGlow = Sculpt.Glow( new Color( 0.3f, 1f, 0.35f ), 1.3f );

	public override GibRecipe Gibs => GibRecipes.Cleaner( GooColour, BlueColour, PaleColour );

	/// <summary> Body space of the lofted mesh: y runs nose (+) to tail (-), z is up. </summary>
	static readonly Shader SkinShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 blue : source_color;
uniform vec3 pale : source_color;
uniform vec3 stripe : source_color;
uniform vec3 edge : source_color;
uniform float hit = 0.0;
varying vec3 lp;
void vertex() { lp = VERTEX; }
void fragment() {
	float along = clamp((0.36 - lp.y) / 0.68, 0.0, 1.0);   // 0 at the snout, 1 at the tail
	float h = lp.z;
	float w = mix(0.011, 0.03, along);                       // the stripe widens to the tail
	float d = abs(h + 0.004);
	float band = 1.0 - smoothstep(w, w + 0.005, d);
	float rim = (1.0 - smoothstep(w + 0.004, w + 0.016, d)) * (1.0 - band);
	vec3 upper = mix(pale, blue, smoothstep(0.2, 0.6, along));
	vec3 lower = mix(vec3(0.95, 0.97, 1.0), mix(blue, vec3(1.0), 0.45), smoothstep(0.35, 0.9, along));
	vec3 col = h > 0.0 ? upper : lower;
	col = mix(col, stripe, band);
	ALBEDO = mix(col, vec3(1.0), hit * 0.7);
	EMISSION = edge * rim * (0.6 + 0.9 * along) + blue * 0.12 * along;
	ROUGHNESS = 0.4;
	SPECULAR = 0.55;
}",
	};

	static readonly Shader BeamShader = new()
	{
		Code = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec3 colour : source_color;
uniform float strength = 1.0;
void fragment() {
	float band = 0.55 + 0.45 * sin(UV.y * 40.0 - TIME * 14.0);
	float f = pow(clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 1.5);
	ALBEDO = colour * band * f * strength * 1.8;
}",
	};

	Node3D look, tailJoint, mouth;
	ShaderMaterial skin;
	readonly List<(Node3D Pivot, float Side)> fins = new();
	MeshInstance3D beam, beamCore;
	readonly List<MeshInstance3D> beamBubbles = new();
	ShaderMaterial beamPaint;
	float flinch, beamShown;
	Vector3 lastVelocity;

	static readonly IReadOnlyList<(float Y, float Rx, float Rz, float Z)> BodyRings = new[]
	{
		(-0.30f, 0.016f, 0.03f, 0f),
		(-0.22f, 0.03f, 0.055f, 0f),
		(-0.10f, 0.048f, 0.078f, 0.002f),
		(0.02f, 0.055f, 0.086f, 0.004f),
		(0.14f, 0.052f, 0.078f, 0f),
		(0.24f, 0.04f, 0.058f, -0.006f),
		(0.31f, 0.025f, 0.036f, -0.01f),
		(0.355f, 0.008f, 0.012f, -0.014f),
	};

	void Build()
	{
		skin = new ShaderMaterial { Shader = SkinShader };
		skin.SetShaderParameter( "blue", BlueColour );
		skin.SetShaderParameter( "pale", PaleColour );
		skin.SetShaderParameter( "stripe", StripeColour );
		skin.SetShaderParameter( "edge", EdgeGlow );

		look = new Node3D { Name = "Look" };
		AddChild( look );
		var bodyMesh = SmoothKit.Cached( "cleaner_body", () => SmoothKit.Loft( SmoothKit.Smooth( BodyRings, 4 ), 0f, 1f, 24 ) );
		// The loft runs along y with z up: turned so the nose points -Z and z points up.
		var bodyNode = SmoothKit.Put( look, bodyMesh, skin, "Wrasse Body" );
		bodyNode.Basis = new Basis( Vector3.Right, -MathF.PI * 0.5f );

		// The eyes: the classic alien eyes, small, on the stripe.
		var blinker = Blinker.Add( this );
		foreach ( var side in new[] { -1f, 1f } )
		{
			var gaze = new Vector3( side * 0.8f, 0.15f, -0.55f ).Normalized();
			var eye = new Node3D { Name = "Alien Eye", Position = new Vector3( side * 0.033f, 0.012f, -0.25f ) };
			look.AddChild( eye );
			var x = Vector3.Up.Cross( gaze ).Normalized();
			var y = gaze.Cross( x ).Normalized();
			eye.Basis = new Basis( x, y, gaze ) * new Basis( Vector3.Back, side * MathX.Rad( 38f ) );
			SmoothKit.Ball( eye, new Vector3( 0f, 0f, -0.01f ), new Vector3( 0.05f, 0.066f, 0.03f ), skin, null, "Eye Bulb" );
			var rim = Sculpt.Blob( eye, new Vector3( 0f, 0f, 0.004f ), new Vector3( 0.038f, 0.058f, 0.014f ), EyeRimGlow, name: "Eye Rim" );
			var black = Sculpt.Blob( eye, new Vector3( 0f, 0f, 0.006f ), new Vector3( 0.034f, 0.053f, 0.015f ), EyeBlack, name: "Eye" );
			blinker.Eye( rim, black );
		}
		mouth = Sculpt.Joint( look, new Vector3( 0f, -0.012f, -0.36f ), "Mouth" );
		Sculpt.Blob( mouth, Vector3.Zero, new Vector3( 0.02f, 0.014f, 0.018f ), Sculpt.Paint( new Color( 0.25f, 0.06f, 0.1f ), 0.5f ), name: "Lips" );

		// Fins: a long low dorsal and anal fin, fluttering pectorals, and a fan tail on a joint that wags.
		var finPaint = SmoothKit.FinMaterial( BlueColour.Lerp( Colors.White, 0.3f ), StripeColour, 0.8f, 0.3f );
		var darkFin = SmoothKit.FinMaterial( StripeColour.Lerp( BlueColour, 0.35f ), BlueColour, 0.85f, 0.3f );
		SmoothKit.Fin( look, new Vector3( 0f, 0.07f, -0.08f ), SmoothKit.Aim( new Vector3( 0f, 0.28f, 1f ), Vector3.Right ), 0.24f, 0.055f, darkFin, 0.04f );
		SmoothKit.Fin( look, new Vector3( 0f, -0.07f, 0.02f ), SmoothKit.Aim( new Vector3( 0f, -0.3f, 1f ), Vector3.Right ), 0.17f, 0.045f, darkFin, 0.04f );
		foreach ( var side in new[] { -1f, 1f } )
		{
			var pivot = Sculpt.Joint( look, new Vector3( side * 0.045f, -0.02f, -0.15f ), "Pectoral" );
			SmoothKit.Fin( pivot, Vector3.Zero, SmoothKit.Aim( new Vector3( side * 0.7f, -0.15f, 0.7f ), Vector3.Up ), 0.08f, 0.06f, finPaint, 0.08f );
			fins.Add( (pivot, side) );
		}
		tailJoint = Sculpt.Joint( look, new Vector3( 0f, 0f, 0.29f ), "Tail" );
		SmoothKit.Fin( tailJoint, Vector3.Zero, SmoothKit.Aim( Vector3.Back, Vector3.Up ), 0.13f, 0.15f, darkFin, 0.05f );

		// What can be shot (a little bigger than the fish, so it can be hit): head (0, the weak spot), body (1), tail (2).
		foreach ( var (index, name, z, r) in new[] { (0, "Wrasse Head", -0.22f, 0.13f), (1, "Wrasse Body", 0f, 0.15f), (2, "Wrasse Tail", 0.22f, 0.11f) } )
		{
			var part = CreaturePart.Create( this, index, name, r );
			look.AddChild( part );
			part.Position = new Vector3( 0f, 0f, z );
		}

		// The link: a glowing beam with a bright core, and bubbles running along it to the patient.
		beamPaint = new ShaderMaterial { Shader = BeamShader };
		beamPaint.SetShaderParameter( "colour", BeamColour );
		beamPaint.SetMeta( "no_caustics", true );
		beam = new MeshInstance3D { Name = "Cleaner Beam", Mesh = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.045f, Height = 1f, RadialSegments = 8, Rings = 1, CapTop = false, CapBottom = false }, MaterialOverride = beamPaint, TopLevel = true, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( beam );
		beamCore = new MeshInstance3D { Name = "Cleaner Beam Core", Mesh = new CylinderMesh { TopRadius = 0.014f, BottomRadius = 0.014f, Height = 1f, RadialSegments = 6, Rings = 1, CapTop = false, CapBottom = false }, MaterialOverride = Sculpt.Glow( BeamColour.Lerp( Colors.White, 0.6f ), 3f ), TopLevel = true, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( beamCore );
		var bubble = new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 10, Rings = 5 };
		var bubblePaint = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.75f, 1f, 0.85f, 0.55f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			EmissionEnabled = true, Emission = BeamColour, EmissionEnergyMultiplier = 1.2f, Roughness = 0.1f,
		};
		for ( var i = 0; i < 7; i++ )
		{
			var b = new MeshInstance3D { Name = "Beam Bubble", Mesh = bubble, MaterialOverride = bubblePaint, TopLevel = true, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			AddChild( b );
			beamBubbles.Add( b );
		}
	}

	void Pose( float dt )
	{
		var now = (float)Clock.Now;
		flinch = MathF.Max( 0f, flinch - dt * 3f );
		var v = Puppet ? (GlobalPosition - lastPosition) / MathF.Max( dt, 0.001f ) : velocity;
		lastPosition = GlobalPosition;
		lastVelocity = lastVelocity.Lerp( v, 1f - MathF.Exp( -6f * dt ) );
		var speed = lastVelocity.Length();
		var rate = Darting ? 30f : 9f + speed * 2.5f;
		// The swim: the tail wags harder with speed, the body sways against it, a little pitch with climbing.
		var wag = MathF.Sin( now * rate + phase );
		tailJoint.Basis = new Basis( Vector3.Up, wag * (0.35f + MathF.Min( speed, 6f ) * 0.06f) );
		var local = GlobalBasis.Orthonormalized().Inverse() * lastVelocity;
		var pitch = MathX.Clamp( local.Y * 0.12f, -0.4f, 0.4f );
		look.Basis = new Basis( Vector3.Up, -wag * 0.08f ) * new Basis( Vector3.Right, pitch ) * new Basis( Vector3.Forward, flinch * 0.4f * MathF.Sin( now * 40f ) );
		look.Position = new Vector3( 0f, MathF.Sin( now * 1.7f + phase ) * 0.02f, 0f );
		foreach ( var (pivot, side) in fins )
			pivot.Basis = new Basis( Vector3.Up, side * MathF.Sin( now * 14f + phase + side ) * 0.45f );
		skin.SetShaderParameter( "hit", MathF.Max( 0f, 1f - (float)sinceHit / 0.1f ) );
	}

	Vector3 lastPosition;

	/// <summary> The beam from its mouth to the patient's middle, with bubbles running along it (fades in and out). </summary>
	void DrawBeam( float dt )
	{
		var target = Linked ? Patient : null;
		beamShown = MathX.Approach( beamShown, target is not null ? 1f : 0f, dt * 5f );
		var show = beamShown > 0.01f && target is not null;
		beam.Visible = beamCore.Visible = show;
		foreach ( var b in beamBubbles ) b.Visible = show;
		if ( !show ) return;
		var from = mouth.GlobalPosition;
		var to = CleanerWard.CentreOf( target );
		var along = to - from;
		var length = along.Length();
		if ( length < 0.05f ) return;
		var dir = along / length;
		var basis = new Basis( new Quaternion( Vector3.Up, dir ) );
		var mid = from + along * 0.5f;
		var pulse = 0.85f + 0.15f * MathF.Sin( (float)Clock.Now * 9f );
		beam.GlobalTransform = new Transform3D( basis.Scaled( new Vector3( beamShown, length, beamShown ) * new Vector3( pulse, 1f, pulse ) ), mid );
		beamCore.GlobalTransform = new Transform3D( basis.Scaled( new Vector3( beamShown, length, beamShown ) ), mid );
		beamPaint.SetShaderParameter( "strength", beamShown );
		var side = dir.Cross( Vector3.Up ).SafeNormal();
		if ( side == Vector3.Zero ) side = Vector3.Right;
		var up = side.Cross( dir );
		var now = (float)Clock.Now;
		for ( var i = 0; i < beamBubbles.Count; i++ )
		{
			var t = (now * 0.55f + i / (float)beamBubbles.Count) % 1f;
			var wobble = side * MathF.Sin( now * 6f + i * 2.1f ) * 0.04f + up * MathF.Cos( now * 5f + i * 1.3f ) * 0.04f;
			var size = MathF.Sin( t * MathF.PI ) * beamShown * (0.7f + 0.5f * ((i * 37) % 5) / 5f);
			beamBubbles[i].GlobalTransform = new Transform3D( Basis.Identity.Scaled( Vector3.One * MathF.Max( 0.01f, size ) ), from + along * t + wobble );
		}
	}

	protected override void Cleanup()
	{
		foreach ( var n in new Node3D[] { beam, beamCore } ) if ( n is not null && IsInstanceValid( n ) ) n.QueueFree();
		foreach ( var b in beamBubbles ) if ( IsInstanceValid( b ) ) b.QueueFree();
	}
}
