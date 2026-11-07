/// <summary>
/// Screen shake and FOV kick for the local player's camera.
/// Sits on the player; the PlayerController calls PostCameraSetup every frame after placing the camera.
/// </summary>
public sealed class CameraJuice : Component, PlayerController.IEvents
{
	[Property] public float MaxShakeDegrees { get; set; } = 3f;
	[Property] public float ShakeDecay { get; set; } = 2.5f;
	[Property] public float FovKickDecay { get; set; } = 12f;

	float trauma;
	float fovKick;

	/// <summary> Add shake. 0.1 is a light bump, 0.5 is a big hit. Stacks up to 1. </summary>
	public void Shake( float amount ) => trauma = MathF.Min( 1f, trauma + amount );

	/// <summary> Briefly widens the view, e.g. on each shot. </summary>
	public void KickFov( float degrees ) => fovKick = MathF.Max( fovKick, degrees );

	void PlayerController.IEvents.PostCameraSetup( CameraComponent cam )
	{
		if ( IsProxy ) return;

		trauma = MathF.Max( 0f, trauma - ShakeDecay * Time.Delta );
		fovKick = MathX.Lerp( fovKick, 0f, Time.Delta * FovKickDecay );

		// Squaring trauma makes small hits subtle and big hits violent.
		var shake = trauma * trauma * MaxShakeDegrees;
		if ( shake > 0.001f )
		{
			var t = Time.Now * 25f;
			var offset = new Angles(
				(Noise( t, 0f ) * 2f - 1f) * shake,
				(Noise( t, 50f ) * 2f - 1f) * shake,
				(Noise( t, 100f ) * 2f - 1f) * shake * 0.5f );
			cam.WorldRotation *= offset.ToRotation();
		}

		// Zoom in while aiming down the sights: use the gun in hand, not a holstered one.
		var inventory = GetComponent<WeaponInventory>();
		var weapon = inventory is not null && inventory.Current.IsValid() ? inventory.Current : GetComponent<PlayerWeapon>();
		if ( weapon.IsValid() && weapon.AimAmount > 0.001f )
			cam.FieldOfView *= MathX.Lerp( 1f, weapon.AimFovScale, weapon.AimAmount );

		cam.FieldOfView += fovKick;

		// On death the view slumps to the floor and tips over.
		var survivor = GetComponent<PlayerSurvivor>();
		var dying = survivor.IsValid() && !survivor.IsAlive;
		deathAmount = MathX.Lerp( deathAmount, dying ? 1f : 0f, Time.Delta * 3f );
		if ( deathAmount > 0.001f )
		{
			cam.WorldPosition += Vector3.Down * 52f * deathAmount;
			cam.WorldRotation *= new Angles( 10f * deathAmount, 0f, 35f * deathAmount ).ToRotation();
		}
	}

	float deathAmount;

	static float Noise( float t, float seed ) => Sandbox.Utility.Noise.Perlin( t, seed );
}
