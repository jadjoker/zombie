/// <summary>
/// Gently rocks an object around its base, like kelp in a current. Purely visual and local.
/// </summary>
public sealed class Sway : Component
{
	/// <summary> Peak lean in degrees. </summary>
	[Property] public float Amount { get; set; } = 6f;
	/// <summary> Seconds per full sway. </summary>
	[Property] public float Period { get; set; } = 5f;

	Rotation rest;
	float phase;

	protected override void OnStart()
	{
		rest = LocalRotation;
		phase = Game.Random.Float( 0f, MathF.PI * 2f );
		Period *= Game.Random.Float( 0.8f, 1.25f );
	}

	protected override void OnUpdate()
	{
		var t = Time.Now * MathF.PI * 2f / MathF.Max( 0.5f, Period ) + phase;
		var pitch = MathF.Sin( t ) * Amount;
		var roll = MathF.Sin( t * 0.63f + 1.7f ) * Amount * 0.6f;
		LocalRotation = rest * new Angles( pitch, 0f, roll ).ToRotation();
	}
}
