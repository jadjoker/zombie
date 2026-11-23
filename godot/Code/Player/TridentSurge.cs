using Godot;

/// <summary>
/// Trident-Bot's surge: the weapon in its hand does the thrust. Winding up cocks it back; the surge drives it forward
/// (a lunge with an overshoot) and holds it out ahead as if it pulls the robot along, then it comes back. The spear
/// thrusts with the spear, the trident shooter with the shooter. The head glows with the charged trident's violet aura
/// and light, and sparkles stream off it. First person moves the view model, third person the gun in the robot's hands.
/// </summary>
public partial class TridentSurge : Node
{
	const float FadeTime = 0.3f;       // seconds to bring the weapon back after the surge
	static readonly Color Violet = new( 0.6f, 0.45f, 1f );
	/// <summary> Where the spear's head is in its body (s&box units, the body's own frame). </summary>
	static readonly Vector3 SpearHead = new( 48f, 0f, 0.6f );

	PlayerController controller;
	Jetpack jetpack;
	Node3D glow;
	OmniLight3D wash;
	float show, pose;
	bool wasSurging;
	TimeSince sinceSurge = 99f;
	TimeSince sinceSparkle = 0f;
	PlayerWeapon posed;
	/// <summary> Tests: frames the surge glow was out. </summary>
	public static int Shown;
	/// <summary> Tests: the glow on the weapon's head. </summary>
	public Node3D Glow => glow;

	public override void _Ready()
	{
		Name = "TridentSurge";
		controller = GetParent<PlayerController>();
		glow = new Node3D { Name = "Surge Glow", Visible = false, TopLevel = true };
		var aura = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.7f, 0.4f, 1f, 0.3f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color( 0.75f, 0.45f, 1f ), EmissionEnergyMultiplier = 3f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		aura.SetMeta( "no_caustics", true );
		glow.AddChild( new MeshInstance3D
		{
			Name = "Magic Aura", Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 }, MaterialOverride = aura,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Scale = Vector3.One * 0.16f,
		} );
		glow.AddChild( new OmniLight3D { LightColor = Violet, LightEnergy = 2.5f, OmniRange = 3f, ShadowEnabled = false } );
		AddChild( glow );
		wash = new OmniLight3D { Name = "Surge Wash", LightColor = Violet, LightEnergy = 0f, OmniRange = 4f, ShadowEnabled = false, TopLevel = true };
		AddChild( wash );
	}

	public override void _Process( double delta )
	{
		using var remoteSounds = Sfx.Remote( controller );   // online: another player's robot is heard where it is
		var dt = (float)delta;
		jetpack ??= controller.GetNodeOrNull<Jetpack>( "Jetpack" );
		var held = controller.GetNodeOrNull<WeaponInventory>( "WeaponInventory" )?.Current;
		var on = controller.Robot == RobotClass.TridentBot && jetpack is not null && !jetpack.Blinking && (jetpack.Charging || jetpack.Surging)
			&& controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) is { IsAlive: true } && held is not null;
		show = on ? MathX.Clamp( show + dt / 0.12f, 0f, 1f ) : MathX.Clamp( show - dt / FadeTime, 0f, 1f );

		// The pose: cocked back while winding up, a lunge that overshoots as the surge goes, held out, then back.
		var surging = on && jetpack.Surging;
		if ( surging && !wasSurging ) sinceSurge = 0f;
		wasSurging = surging;
		float want;
		if ( surging )
		{
			var t = (float)sinceSurge;
			want = t < 0.07f ? MathX.Lerp( -0.35f, 1.2f, t / 0.07f ) : MathX.Lerp( 1.2f, 1f, MathX.Clamp( (t - 0.07f) / 0.1f, 0f, 1f ) );
			pose = want;
		}
		else
		{
			want = on ? -0.35f * Smooth( jetpack.Charge ) : 0f;
			pose = MathX.Lerp( pose, want, 1f - MathF.Exp( -(on ? 20f : 9f) * dt ) );
		}
		if ( posed is not null && posed != held && IsInstanceValid( posed ) ) posed.SurgePose = 0f;
		posed = held;
		if ( held is not null ) held.SurgePose = MathF.Abs( pose ) < 0.002f ? 0f : pose;

		var visible = show > 0.01f;
		glow.Visible = visible;
		wash.LightEnergy = 2.5f * show;
		if ( !visible ) return;
		Shown++;

		// The glow sits on the weapon's head: the spear's prongs or the shooter's muzzle, in whichever view shows.
		if ( Head( held ) is not { } head ) { glow.Visible = false; return; }
		glow.GlobalPosition = head;
		glow.Scale = Vector3.One * MathX.Lerp( 0.3f, 1f, show ) * (1f + 0.08f * MathF.Sin( (float)Clock.Now * 18f ));
		var forward = jetpack.Surging ? jetpack.SurgeDirection : controller.EyeAngles.Forward;
		wash.GlobalPosition = head - forward * 0.4f;
		if ( sinceSparkle > (jetpack.Surging ? 0.03f : 0.08f) )
		{
			sinceSparkle = 0f;
			TridentBolt.Sparkles( GetTree(), head, head + forward * 0.5f );
		}
	}

	static float Smooth( float k ) { k = MathX.Clamp( k, 0f, 1f ); return k * k * (3f - 2f * k); }

	/// <summary> The weapon's head in the world: in first person on the view model, in third person on the gun in hand. </summary>
	Vector3? Head( PlayerWeapon weapon )
	{
		Node3D root = !controller.SeenFromOutside ? weapon.ViewModel : controller.FindChild( $"World {weapon.DisplayName}", true, false ) as Node3D;
		if ( root is null || !root.IsVisibleInTree() ) return null;
		if ( weapon.Spear && root.FindChild( "Spear", true, false ) is Node3D spear ) return spear.GlobalTransform * SpearHead;
		if ( root.FindChild( "Muzzle", true, false ) is Node3D muzzle ) return muzzle.GlobalPosition;
		return null;
	}
}
