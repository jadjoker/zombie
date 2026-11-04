/// <summary>
/// Dries a goo splat: eases its tint from fresh and bright to darker over a few seconds, so new
/// splatter stands out from old.
/// </summary>
public sealed class GooDry : Component
{
	public Color Wet { get; set; }
	public Color Dry { get; set; }
	[Property] public float DryTime { get; set; } = 10f;

	TimeSince age;
	Decal decal;

	protected override void OnStart()
	{
		age = 0f;
		decal = Components.Get<Decal>();
	}

	protected override void OnUpdate()
	{
		if ( !decal.IsValid() ) { Destroy(); return; }
		var t = MathX.Clamp( age / DryTime, 0f, 1f );
		decal.ColorTint = Color.Lerp( Wet, Dry, t * t * (3f - 2f * t) );
		if ( t >= 1f ) Destroy();
	}
}
