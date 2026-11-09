using Godot;

/// <summary>
/// Port of s&box CameraJuice: screen shake and FOV kick for the local player's camera.
/// (Aim zoom, scope drift and the death slump come back with the weapons and health ports.)
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
		cam.Fov += fovKick;
	}

	static readonly FastNoiseLite noise = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 1f };

	/// <summary> Smooth noise in 0..1 (s&box Noise.Perlin). </summary>
	static float Noise( float x, float y ) => noise.GetNoise2D( x, y ) * 0.5f + 0.5f;
}
