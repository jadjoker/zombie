using Godot;

/// <summary>
/// The shield a cleaner wrasse gives the enemy it is cleaning (user's design): a mostly see-through bubble round the
/// whole enemy, teal-green at its edge with faint hexagons, clear in the middle. It fills from nothing to its full
/// strength (the wrasse's <see cref="CleanerWrasse.ShieldMax"/>, growing with the wave) in <see cref="FillTime"/>, and
/// takes hits before the enemy's health does (<see cref="NetCreatures"/> runs every shot and health hit through
/// <see cref="Soak"/>). It ripples where it is hit, fades as it weakens, and shatters at nothing; broken, it only grows
/// back after <see cref="RecoverTime"/>, and a hit stops it growing for <see cref="RegrowDelay"/>. It stays
/// <see cref="Linger"/> after the link breaks, then fades away. Online the host's soaks it; a joiner's copy shows the
/// host's strength (<see cref="FromHost"/>).
/// </summary>
public partial class CleanerWard : Node3D
{
	/// <summary> From nothing to full (s): user's pick. </summary>
	public const float FillTime = 2f;
	public const float Linger = 3f;
	public const float RecoverTime = 3f;
	public const float RegrowDelay = 0.8f;

	/// <summary> Tests: hits soaked, shields broken. </summary>
	public static int Soaked, Breaks;

	public Creature Patient;
	public float Amount, Max = 100f;
	/// <summary> A joiner's copy: drawn from the host's strength, never soaks. </summary>
	public bool Remote;
	public float Fraction => Max > 0f ? MathX.Clamp( Amount / Max, 0f, 1f ) : 0f;
	public bool Up => Amount > 0f;

	TimeSince sinceFed = 0f, sinceBroken = 99f, sinceHit = 99f, sinceMeasured = 99f, sinceRipple = 99f, sinceTink = 99f;
	Vector3 localCentre = Vector3.Up * 0.5f, half = Vector3.One * 0.6f;
	MeshInstance3D shell;
	ShaderMaterial paint;
	float shown, grow;

	/// <summary> The middle of the shield, in the world. </summary>
	public Vector3 Centre => Frame * localCentre;
	/// <summary> Its biggest half size (m). </summary>
	public float Radius => MathF.Max( half.X, MathF.Max( half.Y, half.Z ) );
	Transform3D Frame => new( Patient.GlobalBasis.Orthonormalized(), Patient.GlobalPosition );

	/// <summary> The middle of a creature (its shield's middle if it has one). </summary>
	public static Vector3 CentreOf( Creature c ) =>
		c.Ward is { } w && IsInstanceValid( w ) ? w.Centre : c.GlobalPosition + Vector3.Up * 0.6f;

	/// <summary> The creature's shield, made (empty) if it has none. </summary>
	public static CleanerWard For( Creature c )
	{
		if ( c.Ward is { } w && IsInstanceValid( w ) ) return w;
		w = new CleanerWard { Name = "Cleaner Shield", Patient = c, Amount = 0f };
		c.GetTree().CurrentScene.AddChild( w );
		c.Ward = w;
		w.Build();
		w.Measure();
		return w;
	}

	/// <summary> A joiner: the host's strength for this creature's shield (0 to 1). </summary>
	public static void FromHost( Creature c, float fraction )
	{
		if ( fraction <= 0f && (c.Ward is null || !IsInstanceValid( c.Ward )) ) return;
		var w = For( c );
		w.Remote = true;
		w.Max = 1f;
		if ( fraction <= 0f && w.Amount > 0f ) w.Break();
		else if ( fraction < w.Amount - 0.02f ) { w.sinceHit = 0f; w.sinceRipple = 0f; }
		w.Amount = fraction;
		if ( fraction > 0f ) w.sinceFed = 0f;
	}

	/// <summary> The wrasse is cleaning it this frame: it grows toward full. </summary>
	public void Feed( float max, float dt )
	{
		Max = MathF.Max( 1f, max );
		sinceFed = 0f;
		if ( sinceBroken > RecoverTime && sinceHit > RegrowDelay ) Amount = MathF.Min( Max, Amount + Max / FillTime * dt );
	}

	/// <summary> A hit: the shield takes what it can; returns what gets through. </summary>
	public float Soak( float damage, Vector3 at )
	{
		if ( Remote || Amount <= 0f || damage <= 0f ) return damage;
		var take = MathF.Min( Amount, damage );
		Amount -= take;
		sinceHit = 0f;
		Soaked++;
		Ripple( at );
		if ( Amount <= 0.01f ) { Amount = 0f; Break(); }
		else if ( sinceTink > 0.08f ) { sinceTink = 0f; Sfx.Play( "glass_hit", at, 0.4f, Rng.Float( 1.4f, 1.7f ) ); }
		return damage - take;
	}

	void Ripple( Vector3 at )
	{
		sinceRipple = 0f;
		if ( paint is null || !IsInsideTree() ) return;
		var local = (GlobalTransform.AffineInverse() * at).SafeNormal();
		paint.SetShaderParameter( "ripple_at", local == Vector3.Zero ? Vector3.Forward : local );
	}

	void Break()
	{
		sinceBroken = 0f;
		Breaks++;
		if ( !IsInsideTree() ) return;
		var c = Centre;
		BubbleFilm.Shards( c, Radius );
		Sfx.Play( "glass_break", c, 0.8f, Rng.Float( 1.2f, 1.4f ) );
		Sfx.Play( "bubble_burst", c, 0.6f, 1.2f );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( Patient is null || !IsInstanceValid( Patient ) || Patient.IsDead || !Patient.IsInsideTree() )
		{
			if ( Patient is not null && IsInstanceValid( Patient ) && Patient.Ward == this ) Patient.Ward = null;
			if ( Amount > 0f && Patient is not null && IsInstanceValid( Patient ) ) Break();
			QueueFree();
			return;
		}
		// Left alone it fades away (a joiner's copy goes when the host's does).
		if ( sinceFed > (Remote ? 1f : Linger) )
		{
			Amount = MathF.Max( 0f, Amount - Max * 2f * dt );
			if ( Amount <= 0f && shown < 0.01f ) { Patient.Ward = null; QueueFree(); return; }
		}
		if ( sinceMeasured > 0.5f ) Measure();
		var target = Amount > 0f ? 0.45f + 0.55f * Fraction : 0f;
		shown = MathX.Approach( shown, target, dt * 3f );
		grow = MathX.Approach( grow, Amount > 0f ? 1f : 0f, dt * 2.5f );
		var scale = half * (0.82f + 0.18f * SmoothKit.Ease( grow ));
		GlobalTransform = new Transform3D( Frame.Basis.Scaled( scale ), Centre );
		shell.Visible = shown > 0.005f;
		paint.SetShaderParameter( "strength", shown );
		paint.SetShaderParameter( "ripple_age", (float)sinceRipple );
		paint.SetShaderParameter( "cleaning", sinceFed < 0.2f ? 1f : 0f );
	}

	/// <summary> Fits the bubble round everything the creature is drawn with (its own node and its parts' rigs). </summary>
	void Measure()
	{
		sinceMeasured = 0f;
		var inverse = Frame.AffineInverse();
		var box = new Aabb();
		var any = false;
		var roots = new List<Node> { Patient };
		foreach ( var part in CreaturePart.All )
			if ( IsInstanceValid( part ) && part.Creature == Patient ) roots.Add( part );
		var seen = new HashSet<MeshInstance3D>();
		foreach ( var root in roots )
			foreach ( var node in root.FindChildren( "*", "MeshInstance3D", true, false ) )
			{
				if ( node is not MeshInstance3D mesh || !seen.Add( mesh ) || mesh.Mesh is null || !mesh.IsVisibleInTree() ) continue;
				if ( Floating( mesh, root ) ) continue;
				var b = inverse * (mesh.GlobalTransform * mesh.GetAabb());
				box = any ? box.Merge( b ) : b;
				any = true;
			}
		if ( !any ) return;
		localCentre = box.GetCenter();
		var size = box.Size * 0.5f;
		// Round it out a little, room round the body, never too thin or too huge.
		var mean = (size.X + size.Y + size.Z) / 3f;
		half = new Vector3( Fit( size.X, mean ), Fit( size.Y, mean ), Fit( size.Z, mean ) );
	}

	static float Fit( float s, float mean ) => MathX.Clamp( MathX.Lerp( s, mean, 0.3f ) * 1.15f + 0.15f, 0.45f, 3.5f );

	/// <summary> A mesh under a top-level node (a beam, a lure, light spots on the sand) isn't the body. </summary>
	static bool Floating( Node mesh, Node root )
	{
		for ( Node n = mesh; n is not null && n != root; n = n.GetParent() )
		{
			if ( n is Node3D { TopLevel: true } ) return true;
			if ( n is TridentBolt or CleanerWard ) return true;
		}
		return false;
	}

	static readonly SphereMesh Ball = new() { Radius = 1f, Height = 2f, RadialSegments = 40, Rings = 20 };

	static readonly Shader ShellShader = new()
	{
		Code = @"shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never, shadows_disabled;
uniform vec3 colour : source_color = vec3(0.3, 1.0, 0.82);
uniform float strength = 0.0;
uniform vec3 ripple_at = vec3(0.0, 0.0, -1.0);
uniform float ripple_age = 9.0;
uniform float cleaning = 0.0;
varying vec3 op;
void vertex() { op = VERTEX; }
float hex_dist(vec2 p) { p = abs(p); return max(dot(p, normalize(vec2(1.0, 1.732))), p.x); }
void fragment() {
	vec3 n = normalize(op);
	float fres = pow(1.0 - clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 2.4);
	// Faint hexagons over the bubble.
	vec2 uv = vec2(atan(n.x, n.z) / 6.2832 * 22.0, acos(clamp(n.y, -1.0, 1.0)) / 3.1416 * 11.0);
	vec2 r = vec2(1.0, 1.732);
	vec2 a = mod(uv, r) - r * 0.5;
	vec2 b = mod(uv - r * 0.5, r) - r * 0.5;
	vec2 g = dot(a, a) < dot(b, b) ? a : b;
	float hex = smoothstep(0.4, 0.48, hex_dist(g));
	float sweep = 0.5 + 0.5 * sin(op.y * 5.0 - TIME * 2.2);
	// A ring running out from the last hit, and a flash where it landed.
	float d = acos(clamp(dot(n, normalize(ripple_at)), -1.0, 1.0));
	float life = clamp(1.0 - ripple_age / 0.6, 0.0, 1.0);
	float ring = exp(-abs(d - ripple_age * 5.0) * 10.0) * life;
	float spot = exp(-d * 5.0) * clamp(1.0 - ripple_age / 0.15, 0.0, 1.0);
	float pulse = cleaning * (0.5 + 0.5 * sin(TIME * 6.0)) * 0.08;
	float a1 = strength * (0.05 + fres * 0.6 + hex * (0.07 + 0.07 * sweep) + pulse) + (ring * 0.55 + spot * 0.5) * max(strength, 0.4);
	ALBEDO = colour * (1.0 + ring * 1.2 + spot * 1.5 + fres * 0.4);
	ALPHA = clamp(a1, 0.0, 0.85);
}",
	};

	void Build()
	{
		paint = new ShaderMaterial { Shader = ShellShader };
		paint.SetMeta( "no_caustics", true );
		shell = new MeshInstance3D { Name = "Shield Bubble", Mesh = Ball, MaterialOverride = paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
		AddChild( shell );
	}
}
