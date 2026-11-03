/// <summary> Deletes its GameObject after a delay. Used for one-shot effects. </summary>
public sealed class SelfDestruct : Component
{
	[Property] public float Lifetime { get; set; } = 2f;

	TimeSince timeSinceStart;

	protected override void OnStart() => timeSinceStart = 0f;

	protected override void OnUpdate()
	{
		if ( timeSinceStart > Lifetime ) GameObject.Destroy();
	}
}
