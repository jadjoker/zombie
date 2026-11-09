using Godot;

/// <summary>
/// Port of s&box CameraJuice: screen shake and FOV kick for the local player's camera.
/// (The death slump comes back with the health port.)
/// </summary>
public partial class CameraJuice : Node, ICameraModifier
{
	public float MaxShakeDegrees = 3f;
	public float ShakeDecay = 2.5f;
	public float FovKickDecay = 12f;

	float trauma;
	float fovKick;

	public override void _Ready() => Name = "CameraJuice";

	/// <summary> Add shake. 0.1 is a light bump, 0.5 is a big hit. Stacks up to 1. </summary>
	public void Shake( float amount ) => trauma = MathF.Min( 1f, trauma + amount );

	/// <summary> Briefly widens the view, e.g. on each shot. </summary>
	public void KickFov( float degrees ) => fovKick = MathF.Max( fovKick, degrees );

	public void PostCameraSetup( Camera3D cam )
	{
		var dt = (float)GetProcessDeltaTime();
		trauma = MathF.Max( 0f, trauma - ShakeDecay * dt );
		fovKick = MathX.Lerp( fovKick, 0f, dt * FovKickDecay );

		var shake = trauma * trauma * MaxShakeDegrees;
		if ( shake > 0.001f )
		{
			var t = (float)Clock.Now * 25f;
			var offset = new Angles(
				(Noise( t, 0f ) * 2f - 1f) * shake,
				(Noise( t, 50f ) * 2f - 1f) * shake,
				(Noise( t, 100f ) * 2f - 1f) * shake * 0.5f );
			cam.GlobalBasis = cam.GlobalBasis * offset.ToBasis();
		}
		// Zoom in while aiming down the sights (the gun in hand); a scope zooms further and breathes.
		var weapon = GetParent().GetNodeOrNull<WeaponInventory>( "WeaponInventory" )?.Current;
		if ( weapon is not null && weapon.AimAmount > 0.001f )
		{
			cam.Fov *= MathX.Lerp( 1f, weapon.AimFovScale, weapon.AimAmount );
			if ( weapon.ScopeAmount > 0.001f )
			{
				cam.Fov *= MathX.Lerp( 1f, weapon.ScopeFovScale / MathF.Max( 0.05f, weapon.AimFovScale ), weapon.ScopeAmount );
				// Breathing: the reticle drifts in a slow figure-eight (it's real: shots go where it points).
				var tt = (float)Clock.Now;
				var drift = new Angles( MathF.Sin( tt * 0.9f ) * 0.12f * weapon.ScopeAmount, MathF.Sin( tt * 0.45f ) * 0.16f * weapon.ScopeAmount, 0f );
				cam.GlobalBasis = cam.GlobalBasis * drift.ToBasis();
			}
		}

		cam.Fov += fovKick;

		// Tumbling in the dive (or ragdolled by the landing): the view spins with the body.
		if ( DockHouse.Diving ) cam.GlobalBasis = cam.GlobalBasis * DockHouse.CameraTumble;
	}

	static readonly FastNoiseLite noise = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 1f };

	/// <summary> Smooth noise in 0..1 (s&box Noise.Perlin). </summary>
	static float Noise( float x, float y ) => noise.GetNoise2D( x, y ) * 0.5f + 0.5f;
}
