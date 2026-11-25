using Godot;

/// <summary>
/// The mimic octopus's looks: its disguise (a sea mine on its chain, built by <see cref="SeaMine.MakeBall"/>, or a glow
/// bulb), then the octopus: a bulbous mottled mantle leaning back, the classic alien eyes (glossy black almonds slanted
/// out at the top on a glowing green rim) under angry brows, a beak, a web skirt and eight skinned arms with pale suckers
/// that curl and crawl. The grabbing arm is drawn on its own, stretched out to the player and coiled round them. Its skin
/// flushes through colours as it gives itself away.
/// </summary>
public partial class MimicOctopus
{
	static readonly Color Blood = new( 0.3f, 0.5f, 1f );
	static readonly Color SkinColour = new( 0.62f, 0.16f, 0.42f );
	static readonly Color SuckerColour = new( 0.98f, 0.8f, 0.82f );
	static readonly StandardMaterial3D EyeBlack = new()
	{
		AlbedoColor = new Color( 0.01f, 0.01f, 0.015f ), Roughness = 0.06f, ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.04f, Metallic = 0.2f,
	};
	static readonly StandardMaterial3D EyeRimGlow = Sculpt.Glow( new Color( 0.3f, 1f, 0.35f ), 1.3f );
	public const float RevealTime = 0.9f;
	const int ArmJoints = 7;
	const float ArmStep = 0.15f;
	const int LashSegments = 18;

	public override GibRecipe Gibs => GibRecipes.Octopus( Blood, SkinColour, SuckerColour );

	static readonly Shader SkinShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 skin : source_color;
uniform vec3 under : source_color;
uniform vec3 spot : source_color;
uniform float flush = 0.0;   // giving itself away: colours run over it
uniform float hit = 0.0;
uniform float time_offset = 0.0;
varying vec3 lp;
float hash3(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
vec3 hue(float h) { return clamp(abs(fract(h + vec3(0.0, 0.667, 0.333)) * 6.0 - 3.0) - 1.0, 0.0, 1.0); }
void vertex() { lp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
	vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	float down = smoothstep(0.1, -0.6, n.y);
	vec3 col = mix(skin, under, down * 0.6);
	vec3 g = lp * 9.0;
	vec3 cell = floor(g);
	vec3 c = vec3(hash3(cell), hash3(cell + 3.1), hash3(cell + 5.7)) * 0.5 + 0.25;
	float dots = 1.0 - smoothstep(0.18, 0.3, length(fract(g) - c));
	col = mix(col, spot, dots * 0.55 * (1.0 - down));
	vec3 run = hue(lp.y * 0.6 + TIME * 1.6 + time_offset) * 0.9 + 0.1;
	col = mix(col, run, flush * 0.8);
	ALBEDO = mix(col, vec3(1.0, 0.9, 0.95), hit * 0.7);
	EMISSION = run * flush * 0.4;
	ROUGHNESS = 0.4;
	SPECULAR = 0.5;
}",
	};

	sealed class Arm
	{
		public Node3D Base;
		public Vector3 Out;   // body space, outward
		public readonly List<Node3D> Joints = new();
		public Skeleton3D Bones;
	}

	Node3D body, disguise, lashRoot;
	readonly List<Arm> arms = new();
	readonly List<Node3D> links = new();
	readonly List<CreaturePart> bodyParts = new();
	readonly List<MeshInstance3D> lashTubes = new(), lashKnots = new();
	readonly List<CreaturePart> lashParts = new();
	CreaturePart disguisePart;
	ShaderMaterial skin;

	/// <summary> The middle of its body. </summary>
	public Vector3 Centre => body is not null && IsInstanceValid( body ) ? body.GlobalTransform * new Vector3( 0f, 0.5f, 0f ) : GlobalPosition + Vector3.Up * 0.5f;
	Vector3 Mouth => body.GlobalTransform * new Vector3( 0f, 0.24f, -0.26f );
	/// <summary> Where the disguise floats. </summary>
	public Vector3 DisguiseAt => disguise is not null && IsInstanceValid( disguise ) ? disguise.GlobalPosition : GlobalPosition + Vector3.Up * DisguiseHeight;
	Vector3 ArmBase( int k ) => k >= 0 && k < arms.Count ? arms[k].Base.GlobalPosition : Centre;

	/// <summary> The arm still on that points most toward a direction (flat, world). </summary>
	int ArmToward( Vector3 flat )
	{
		var best = -1;
		var bestDot = float.MinValue;
		for ( var k = 0; k < arms.Count; k++ )
		{
			if ( (GoneMask & (1 << k)) != 0 ) continue;
			var d = (GlobalBasis * arms[k].Out).Dot( flat.SafeNormal() );
			if ( d > bestDot ) { bestDot = d; best = k; }
		}
		return best;
	}

	void Build()
	{
		skin = new ShaderMaterial { Shader = SkinShader };
		skin.SetShaderParameter( "skin", SkinColour );
		skin.SetShaderParameter( "under", new Color( 0.95f, 0.62f, 0.66f ) );
		skin.SetShaderParameter( "spot", new Color( 0.3f, 0.05f, 0.2f ) );
		skin.SetShaderParameter( "time_offset", phase );

		body = new Node3D { Name = "Body", Visible = false };
		AddChild( body );
		// The mantle leaning back, the head between the eyes, the web skirt.
		SmoothKit.Ball( body, new Vector3( 0f, 0.66f, 0.14f ), new Vector3( 0.62f, 0.74f, 0.76f ), skin, new Basis( Vector3.Right, -0.45f ), "Mantle" );
		SmoothKit.Ball( body, new Vector3( 0f, 0.4f, -0.04f ), new Vector3( 0.54f, 0.38f, 0.5f ), skin, null, "Head" );
		SmoothKit.Ball( body, new Vector3( 0f, 0.24f, 0f ), new Vector3( 0.78f, 0.16f, 0.78f ), skin, null, "Web" );

		// The eyes, under angry brows.
		var blinker = Blinker.Add( this );
		var brow = Sculpt.Paint( SkinColour * 0.8f, 0.5f );
		foreach ( var side in new[] { -1f, 1f } )
		{
			var gaze = new Vector3( side * 0.5f, 0.12f, -0.86f ).Normalized();
			var at = new Vector3( side * 0.16f, 0.44f, -0.22f );
			var eye = new Node3D { Name = "Alien Eye", Position = at };
			body.AddChild( eye );
			var x = Vector3.Up.Cross( gaze ).Normalized();
			var y = gaze.Cross( x ).Normalized();
			eye.Basis = new Basis( x, y, gaze ) * new Basis( Vector3.Back, side * MathX.Rad( 38f ) );
			SmoothKit.Ball( eye, new Vector3( 0f, 0f, -0.03f ), new Vector3( 0.16f, 0.25f, 0.1f ), skin, null, "Eye Bulb" );
			var rim = Sculpt.Blob( eye, new Vector3( 0f, 0f, 0.016f ), new Vector3( 0.12f, 0.225f, 0.036f ), EyeRimGlow, name: "Eye Rim" );
			var black = Sculpt.Blob( eye, new Vector3( 0f, 0f, 0.021f ), new Vector3( 0.11f, 0.208f, 0.04f ), EyeBlack, name: "Eye" );
			blinker.Eye( rim, black );
			Sculpt.Blob( body, at + new Vector3( -side * 0.02f, 0.13f, -0.02f ), new Vector3( 0.17f, 0.045f, 0.07f ), brow, new Basis( Vector3.Back, side * 0.4f ), "Brow" );
			var part = CreaturePart.Create( this, 1, side < 0f ? "Mimic Eye L" : "Mimic Eye R", 0.09f );
			body.AddChild( part );
			part.Position = at;
			bodyParts.Add( part );
		}
		var beak = Sculpt.Paint( new Color( 0.12f, 0.08f, 0.06f ), 0.3f );
		Sculpt.Blob( body, new Vector3( 0f, 0.26f, -0.24f ), new Vector3( 0.09f, 0.06f, 0.07f ), beak, name: "Beak" );
		Sculpt.Cone( body, new Vector3( 0f, 0.26f, -0.26f ), new Vector3( 0f, 0.22f, -0.31f ), 0.025f, beak, 6, "Beak Tip" );

		var mantle = CreaturePart.Create( this, 0, "Mimic Body", 0.36f );
		body.AddChild( mantle );
		mantle.Position = new Vector3( 0f, 0.5f, 0.05f );
		bodyParts.Add( mantle );

		// Eight arms round the skirt, each a skinned chain of joints, suckers underneath.
		var sucker = Sculpt.Paint( SuckerColour, 0.35f );
		for ( var k = 0; k < Arms; k++ )
		{
			var a = (k + 0.5f) / Arms * MathF.Tau;
			var dir = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var basis = new Basis( Vector3.Up.Cross( dir ).Normalized(), -dir, Vector3.Up.Cross( dir ).Normalized().Cross( -dir ).Normalized() );
			var root = new Node3D { Name = $"Arm {k}", Position = dir * 0.24f + Vector3.Up * 0.22f, Basis = basis };
			body.AddChild( root );
			var arm = new Arm { Base = root, Out = dir };
			Node3D parent = root;
			for ( var i = 0; i < ArmJoints; i++ )
			{
				var j = Sculpt.Joint( parent, i == 0 ? Vector3.Zero : new Vector3( 0f, -ArmStep, 0f ), $"Arm {k} {i}" );
				arm.Joints.Add( j );
				parent = j;
				if ( i > 0 )
				{
					var r = MathX.Lerp( 0.075f, 0.016f, i / (float)ArmJoints );
					foreach ( var sx in new[] { -1f, 1f } )
						Sculpt.Blob( j, new Vector3( sx * r * 0.45f, ArmStep * 0.5f, -r * 0.85f ), new Vector3( r * 0.55f, r * 0.55f, r * 0.25f ), sucker, name: "Sucker" );
				}
			}
			arm.Bones = SmoothKit.SkinChain( root, arm.Joints, ArmStep, 0.04f, 0.08f, t => (MathX.Lerp( 0.075f, 0.014f, t ), MathX.Lerp( 0.075f, 0.014f, t )), skin, "octo arm", 30, 10 );
			arms.Add( arm );
		}

		// The grabbing arm, drawn on its own from its root to the player (top level, laid out each frame).
		lashRoot = new Node3D { Name = "Mimic Lash", TopLevel = true, Visible = false };
		AddChild( lashRoot );
		lashRoot.GlobalTransform = Transform3D.Identity;
		var tube = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 10, Rings = 1 };
		var knot = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 5 };
		for ( var i = 0; i < LashSegments; i++ )
		{
			var m = new MeshInstance3D { Name = "Lash", Mesh = tube, MaterialOverride = skin };
			lashRoot.AddChild( m );
			lashTubes.Add( m );
		}
		for ( var i = 0; i <= LashSegments; i++ )
		{
			var m = new MeshInstance3D { Name = "Lash Knot", Mesh = knot, MaterialOverride = i % 2 == 0 ? skin : sucker };
			lashRoot.AddChild( m );
			lashKnots.Add( m );
		}
		for ( var i = 0; i < 3; i++ )
		{
			var p = CreaturePart.Create( this, 2, "Mimic Arm", 0.13f );
			lashRoot.AddChild( p );
			p.CollisionLayer = 0;
			lashParts.Add( p );
		}

		BuildDisguise();
		foreach ( var p in bodyParts ) p.CollisionLayer = 0;
	}

	void BuildDisguise()
	{
		disguise = new Node3D { Name = "Disguise", Position = Vector3.Up * DisguiseHeight };
		AddChild( disguise );
		if ( Look == Disguise.Mine )
		{
			SeaMine.MakeBall( disguise );
			links.AddRange( SeaMine.MakeChain( this, DisguiseHeight ) );
			disguisePart = CreaturePart.Create( this, 3, "Mimic Disguise", SeaMine.Radius + 0.12f );
		}
		else
		{
			var c = new Color( 0.62f, 0.38f, 1f );
			var glow = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 1.5f, Roughness = 0.3f, RimEnabled = true, Rim = 0.5f };
			var frond = new StandardMaterial3D { AlbedoColor = new Color( 0.2f, 0.06f, 0.3f ), Roughness = 0.45f, RimEnabled = true, Rim = 0.6f, RimTint = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
			const float size = 0.24f;
			disguise.AddChild( new MeshInstance3D { Mesh = new SphereMesh { Radius = size, Height = size * 2.2f, RadialSegments = 12, Rings = 7 }, MaterialOverride = glow } );
			disguise.AddChild( new MeshInstance3D { Mesh = new SphereMesh { Radius = size * 1.15f, Height = size * 1.1f, RadialSegments = 12, Rings = 5, IsHemisphere = true }, MaterialOverride = frond, Position = Vector3.Up * size * 0.35f } );
			for ( var i = 0; i < 4; i++ )
			{
				var a = i * 1.7f;
				var tilt = new Basis( new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ), 0.2f );
				var len = 0.45f;
				disguise.AddChild( new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = size * 0.1f, BottomRadius = 0.006f, Height = len, RadialSegments = 4, Rings = 1 }, MaterialOverride = frond, Transform = new Transform3D( tilt, tilt * (Vector3.Down * (size * 0.6f + len * 0.5f)) ) } );
			}
			disguisePart = CreaturePart.Create( this, 3, "Mimic Disguise", size * 1.4f );
		}
		disguise.AddChild( disguisePart );
	}

	void RevealFx()
	{
		var at = DisguiseAt;
		InkCloud( at, 8 );
		Sfx.Play( "bubble_burst", at, 0.9f, 0.7f );
		Sfx.Play( "creature_attack", at, 0.8f, 0.6f );
		foreach ( var l in links ) if ( IsInstanceValid( l ) ) l.Visible = false;
		foreach ( var n in GetChildren().OfType<MeshInstance3D>() ) n.Visible = false;   // the chain's anchor
		if ( disguisePart is not null && IsInstanceValid( disguisePart ) ) disguisePart.CollisionLayer = 0;
		foreach ( var p in bodyParts ) p.CollisionLayer = Layers.Creature;
		body.Visible = true;
		// Down to the sand where it was floating (the creature's node is on the floor under it).
		var floor = Sculpt.FloorBelow( this, at, 12f );
		GlobalPosition = new Vector3( at.X, floor, at.Z );
		if ( disguise is not null ) disguise.GlobalPosition = at;
		if ( PlayerSurvivor.FindNearestAlive( at ) is { } prey && (prey.RealPosition - at).Flat().SafeNormal() is var d && d != Vector3.Zero ) GlobalBasis = Shapes.LookAt( d );
		revealFrom = at.Y - floor;
	}

	float revealFrom;

	static void InkCloud( Vector3 at, int puffs )
	{
		for ( var i = 0; i < puffs; i++ )
			GpuFx.SandPuff( at + Rng.InSphere( 0.3f ), Rng.InSphere( 1f ) * 1.4f, new Color( 0.06f, 0.03f, 0.1f, 0.85f ), Rng.Float( 6f, 11f ), Rng.Float( 1.1f, 2f ) );
	}

	/// <summary> Arm `k` cut off: the arm gone, a torn stump, pieces of the grabbing arm falling away, a gush and some ink. </summary>
	void CutFx( int k )
	{
		if ( k < 0 || k >= arms.Count ) return;
		var a = arms[k];
		var at = a.Base.GlobalPosition;
		var outward = (a.Base.GlobalBasis * Vector3.Down).SafeNormal();
		a.Base.Visible = false;
		CreatureGore.AddStump( body, at, outward, 3f, SkinColour, Blood, 4f, Gibs );
		gore?.GooSpray( at, outward, 4, Blood, 220f, 0.5f, 1.2f );
		Sfx.Play( "octo_cut", at, 1f, Rng.Float( 0.92f, 1.08f ) );
		InkCloud( at, 4 );
		// The grabbing arm falls away as a few twitching pieces.
		if ( lashRoot.Visible )
		{
			for ( var i = 2; i < lashTubes.Count; i += 4 )
			{
				var seg = lashTubes[i];
				var piece = new Node3D { Name = "Mimic Arm Piece" };
				GetTree().CurrentScene.AddChild( piece );
				piece.GlobalTransform = new Transform3D( Basis.Identity, seg.GlobalPosition );
				var len = MathF.Max( 0.15f, seg.GlobalBasis.Y.Length() * 3f );
				var r = MathF.Max( 0.02f, seg.GlobalBasis.X.Length() );
				var along = seg.GlobalBasis.Y.Normalized();
				SmoothKit.Tube( piece, -along * len * 0.5f, along * len * 0.5f, r, r * 0.8f, skin, "Arm Piece" );
				Gib.Attach( piece, gore, 3f, 15f, 0.3f, Rng.InSphere( 1.5f ) + Vector3.Up * 0.8f, Blood );
				CreatureGore.Twitch( piece, Rng.Float( 2f, 4f ), 1f );
			}
			lashRoot.Visible = false;
			foreach ( var p in lashParts ) p.CollisionLayer = 0;
		}
	}

	void Pose( float dt )
	{
		var now = (float)Clock.Now + phase;
		var a = (float)sinceAct;
		// The disguise: a mine sways on its chain, a bulb bobs; it shrinks away as it gives itself away.
		if ( disguise is not null && IsInstanceValid( disguise ) )
		{
			if ( act == Act.Disguised )
			{
				var sway = new Vector3( MathF.Sin( now * 0.35f ) * 0.3f, MathF.Sin( now * 0.45f ) * 0.18f, MathF.Sin( now * 0.3f + 1f ) * 0.3f );
				disguise.Position = Vector3.Up * DisguiseHeight + (Look == Disguise.Mine ? sway : sway * 0.5f);
				disguise.Rotation = new Vector3( sway.Z * 0.25f, now * 0.08f, -sway.X * 0.25f );
				if ( Look == Disguise.Mine ) SeaMine.LayChain( links, disguise.Position + Vector3.Down * (SeaMine.Radius + 0.08f) );
			}
			else
			{
				var shrink = 1f - MathX.Clamp( a / 0.3f, 0f, 1f );
				disguise.Scale = Vector3.One * MathF.Max( 0.001f, shrink );
				if ( shrink <= 0f ) { disguise.QueueFree(); disguise = null; }
			}
		}
		if ( act == Act.Disguised ) return;

		// The octopus drops out of the disguise to the sand, swelling to full size with a bounce.
		var revealing = act == Act.Reveal ? MathX.Clamp( a / RevealTime, 0f, 1f ) : 1f;
		var fall = SmoothKit.Ease( revealing * 1.3f );
		body.Position = Vector3.Up * revealFrom * (1f - fall);
		var grow = revealing < 1f ? 0.3f + 0.7f * fall + MathF.Sin( revealing * MathF.PI ) * 0.15f : 1f;
		var breathe = 1f + MathF.Sin( now * 1.8f ) * 0.025f;
		body.Scale = Vector3.One * grow * (holding ? 1f + MathF.Sin( now * 9f ) * 0.02f : breathe);
		skin.SetShaderParameter( "flush", act == Act.Reveal ? 1f - revealing * 0.7f : act == Act.Windup ? 0.35f : 0f );
		skin.SetShaderParameter( "hit", MathF.Max( 0f, 1f - (float)sinceHit / 0.1f ) );

		// Arms: tight curls unfurling as it appears, then lying out on the sand curling up at the tips, waving as it crawls.
		var crawl = MathX.Clamp( velocity.Length() / Speed, 0f, 1f );
		for ( var k = 0; k < arms.Count; k++ )
		{
			var limb = arms[k];
			if ( (GoneMask & (1 << k)) != 0 ) continue;
			var grabbing = k == arm && act is Act.Lash or Act.Hold;
			limb.Base.Visible = !grabbing;
			if ( grabbing ) continue;
			var raised = k == arm && act == Act.Windup;
			for ( var i = 0; i < limb.Joints.Count; i++ )
			{
				var u = i / (float)(limb.Joints.Count - 1);
				var rest = i == 0 ? 0.62f : -0.08f - 0.22f * u;
				var wave = MathF.Sin( now * (1.4f + 2.4f * crawl) + k * 0.9f - i * 0.6f ) * (0.1f + 0.22f * crawl);
				var angle = rest + wave;
				if ( revealing < 1f ) angle = MathX.Lerp( 1.1f, angle, SmoothKit.Ease( revealing * 1.2f ) );
				if ( raised ) angle = i == 0 ? -0.5f : -0.25f + MathF.Sin( now * 30f + i ) * 0.06f;
				var sway = MathF.Sin( now * 0.9f + k * 1.3f + i * 0.4f ) * 0.08f;
				limb.Joints[i].Basis = new Basis( Vector3.Right, angle ) * new Basis( Vector3.Back, sway );
			}
			SmoothKit.PoseChain( limb.Bones, limb.Joints );
		}
		DrawLash();
	}

	/// <summary> The grabbing arm: out along its line as it lashes, or round the held player in a coil. </summary>
	void DrawLash()
	{
		var show = (act == Act.Lash || holding) && arm >= 0 && (GoneMask & (1 << arm)) == 0;
		lashRoot.Visible = show;
		foreach ( var p in lashParts ) p.CollisionLayer = show && holding ? Layers.Creature : 0;
		if ( !show ) return;
		var from = ArmBase( arm );
		var points = new Vector3[LashSegments + 1];
		if ( holding )
		{
			// To the near side of the player, then a coil and a half round their middle.
			var chest = lashTarget;
			var axis = heldAt;
			var toward = (from - axis).Flat().SafeNormal();
			if ( toward == Vector3.Zero ) toward = Vector3.Forward;
			var phi0 = MathF.Atan2( toward.Z, toward.X );
			const float radius = 0.38f;
			var coilStart = new Vector3( axis.X + MathF.Cos( phi0 ) * radius, chest.Y - 0.25f, axis.Z + MathF.Sin( phi0 ) * radius );
			var approach = (int)(LashSegments * 0.55f);
			var control = (from + coilStart) * 0.5f + Vector3.Up * 0.35f;
			for ( var i = 0; i <= approach; i++ )
			{
				var t = i / (float)approach;
				points[i] = from.Lerp( control, t ).Lerp( control.Lerp( coilStart, t ), t );
			}
			var spin = (float)Clock.Now * 0.6f;
			for ( var i = approach + 1; i <= LashSegments; i++ )
			{
				var t = (i - approach) / (float)(LashSegments - approach);
				var phi = phi0 + t * MathF.Tau * 1.35f + MathF.Sin( spin ) * 0.05f;
				points[i] = new Vector3( axis.X + MathF.Cos( phi ) * radius, chest.Y - 0.25f + 0.5f * t, axis.Z + MathF.Sin( phi ) * radius );
			}
		}
		else
		{
			var tip = lashTarget;
			var control = (from + tip) * 0.5f + Vector3.Up * 0.12f * from.DistanceTo( tip );
			for ( var i = 0; i <= LashSegments; i++ )
			{
				var t = i / (float)LashSegments;
				points[i] = from.Lerp( control, t ).Lerp( control.Lerp( tip, t ), t ) + Vector3.Up * MathF.Sin( t * 9f - (float)Clock.Now * 14f ) * 0.04f * t;
			}
		}
		for ( var i = 0; i < LashSegments; i++ )
		{
			var a = points[i];
			var b = points[i + 1];
			var r = MathX.Lerp( 0.075f, 0.028f, i / (float)LashSegments );
			var axisDir = b - a;
			var len = MathF.Max( 0.01f, axisDir.Length() );
			var turn = new Basis( new Quaternion( Vector3.Up, axisDir / len ) );
			lashTubes[i].GlobalTransform = new Transform3D( turn * Basis.FromScale( new Vector3( r, len, r ) ), (a + b) * 0.5f );
			lashKnots[i].GlobalTransform = new Transform3D( Basis.FromScale( Vector3.One * r ), a );
		}
		lashKnots[^1].GlobalTransform = new Transform3D( Basis.FromScale( Vector3.One * 0.028f ), points[^1] );
		for ( var i = 0; i < lashParts.Count; i++ ) lashParts[i].GlobalPosition = points[(int)(LashSegments * (0.25f + 0.12f * i))];
	}
}
