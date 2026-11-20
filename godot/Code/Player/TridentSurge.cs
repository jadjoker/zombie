using Godot;

/// <summary>
/// Trident-Bot's surge: a magic trident (the charged trident's violet aura, light and sparkles) appears in front of the
/// robot while it winds up, then leads the surge on a glowing tether as if it pulls the robot along. Shown in first
/// person (ahead of the camera) and third person (ahead of the body).
/// </summary>
public partial class TridentSurge : Node
{
	const float Reach = 2.2f;          // metres ahead of the robot (third person)
	const float FirstReach = 2.6f;     // metres ahead of the view (first person: clear of the gun)
	const float FadeTime = 0.3f;       // seconds to fade out after the surge
	static readonly Color Violet = new( 0.6f, 0.45f, 1f );

	PlayerController controller;
	Jetpack jetpack;
	Node3D trident;
	MeshInstance3D tether;
	StandardMaterial3D tetherMaterial;
	OmniLight3D wash;
	float show;
	TimeSince sinceSparkle = 0f;
	/// <summary> Tests: frames the trident was out. </summary>
	public static int Shown;
	/// <summary> Tests: the trident and the tether. </summary>
	public Node3D Trident => trident;

	public override void _Ready()
	{
		Name = "TridentSurge";
		controller = GetParent<PlayerController>();
		trident = new Node3D { Name = "Surge Trident", Visible = false, TopLevel = true };
		var model = new Node3D { Name = "Model", Scale = Vector3.One * 1.6f };
		trident.AddChild( model );
		TridentModel.BuildTrident( model );
		TridentBolt.Enchant( model );
		AddChild( trident );
		tetherMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.75f, 0.45f, 1f, 0.55f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color( 0.75f, 0.45f, 1f ), EmissionEnergyMultiplier = 3f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		tetherMaterial.SetMeta( "no_caustics", true );
		tether = new MeshInstance3D
		{
			Name = "Surge Tether", Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.02f, Height = 1f, RadialSegments = 8, Rings = 1 },
			MaterialOverride = tetherMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false, TopLevel = true,
		};
		AddChild( tether );
		wash = new OmniLight3D { Name = "Surge Glow", LightColor = Violet, LightEnergy = 0f, OmniRange = 4f, ShadowEnabled = false, TopLevel = true };
		AddChild( wash );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		jetpack ??= controller.GetNodeOrNull<Jetpack>( "Jetpack" );
		var on = RobotClass.Current == RobotClass.TridentBot && jetpack is not null && !jetpack.Blinking && (jetpack.Charging || jetpack.Surging)
			&& !PlayerSurvivor.LocalDead;
		show = on ? MathX.Clamp( show + dt / 0.12f, 0f, 1f ) : MathX.Clamp( show - dt / FadeTime, 0f, 1f );
		var visible = show > 0.01f;
		trident.Visible = tether.Visible = visible;
		wash.LightEnergy = 3f * show;
		if ( !visible ) return;
		Shown++;

		// Where it leads: the surge's way once it goes, the aim while winding up (level in third person, so it stays in view).
		var camera = controller.Camera;
		var first = !controller.ThirdPerson && camera is not null;
		var aim = first ? -camera.GlobalBasis.Z : controller.EyeAngles.Forward.Flat().SafeNormal();
		var direction = jetpack.Surging ? jetpack.SurgeDirection : aim;
		if ( direction == Vector3.Zero ) direction = aim;
		var charge = jetpack.Surging ? 1f : jetpack.Charge;

		// First person: from just under the view, the trident out ahead and a little low of the crosshair;
		// third person: from the robot's chest.
		Vector3 holdAt;
		float reach;
		if ( first )
		{
			holdAt = camera.GlobalPosition - camera.GlobalBasis.Y * 0.28f;
			reach = FirstReach;
		}
		else
		{
			holdAt = controller.GlobalPosition + Vector3.Up * 40f * U.In;
			reach = Reach;
		}

		// It pulls ahead: further out and steadier as the charge builds, straining at full surge.
		reach *= MathX.Lerp( 0.75f, 1f, charge ) * MathX.Lerp( 0.6f, 1f, show );
		var shake = jetpack.Surging ? 0.015f : 0.04f * (1f - charge);
		var tip = holdAt + direction * reach + Rng.InSphere( shake );
		trident.GlobalTransform = new Transform3D( Shapes.LookAt( direction ) * new Basis( Vector3.Back, (float)Clock.Now * 4f ), tip );
		trident.Scale = Vector3.One * MathX.Lerp( 0.4f, 1f, show * MathX.Lerp( 0.6f, 1f, charge ) );

		// The tether from the robot to the trident's butt.
		var butt = tip - direction * 0.55f * trident.Scale.X * 1.6f;
		var span = butt - holdAt;
		var length = span.Length();
		if ( length > 0.05f )
		{
			var up = span / length;
			var side = MathF.Abs( up.Dot( Vector3.Up ) ) > 0.95f ? Vector3.Right : Vector3.Up.Cross( up ).Normalized();
			tether.GlobalTransform = new Transform3D( new Basis( side, up * length, side.Cross( up ) ), holdAt + span * 0.5f );
		}
		tetherMaterial.AlbedoColor = new Color( 0.75f, 0.45f, 1f, 0.55f * show );
		wash.GlobalPosition = holdAt + direction * reach * 0.5f;

		if ( sinceSparkle > (jetpack.Surging ? 0.03f : 0.08f) )
		{
			sinceSparkle = 0f;
			TridentBolt.Sparkles( GetTree(), holdAt, tip );
		}
	}
}
