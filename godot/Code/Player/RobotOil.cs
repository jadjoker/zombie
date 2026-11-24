using Godot;

/// <summary>
/// The robot bleeds oil (user's ask: "since we are robots"). Every hit sprays black oil and sparks from where it landed;
/// a hard hit splatters oil across your visor; badly hurt (under `LeakBelow`) the robot leaks a trail of oil and spits
/// sparks as it goes. On death the ragdoll comes apart (`RobotRagdoll.BreakApart`): limbs and the head torn off,
/// gushing oil from both ends.
/// </summary>
public partial class RobotOil : Node
{
	public static readonly Color Oil = new( 0.035f, 0.03f, 0.028f );
	public const float LeakBelow = 0.35f, VisorFrom = 12f;
	/// <summary> Tests: oil sprays from hits, and drips leaked. </summary>
	public static int Sprays, Leaks;

	PlayerController controller;
	PlayerSurvivor survivor;
	Health hooked;
	float leakTick, sparkTick;

	public override void _Ready() => controller = GetParent() as PlayerController;

	public override void _Process( double delta )
	{
		survivor ??= controller?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		if ( survivor?.Health is not { } h ) return;
		if ( hooked != h ) { if ( hooked is not null ) hooked.Damaged -= OnHurt; hooked = h; h.Damaged += OnHurt; }
		if ( !survivor.IsAlive || h.Fraction >= LeakBelow || GoreSystem.Get( controller ) is not { } gore ) return;
		var dt = (float)delta;
		// Badly hurt: a trail of oil and a spit of sparks now and then.
		leakTick -= dt;
		if ( leakTick <= 0f )
		{
			leakTick = MathX.Lerp( 0.15f, 0.45f, h.Fraction / LeakBelow );
			Leaks++;
			var at = controller.GlobalPosition + Vector3.Up * Rng.Float( 0.6f, 1.3f ) + Rng.InSphere( 0.25f ).Flat();
			gore.GooDrip( at, Oil );
			if ( Rng.Float() < 0.4f ) gore.GooBurst( at, Vector3.Down, Oil, 2, 50f, 0.6f );
		}
		sparkTick -= dt;
		if ( sparkTick <= 0f )
		{
			sparkTick = Rng.Float( 0.6f, 1.6f );
			Fx.ImpactWorld( controller.GlobalPosition + Vector3.Up * Rng.Float( 0.9f, 1.4f ) + Rng.InSphere( 0.3f ), Rng.OnSphere() );
		}
	}

	public override void _ExitTree() { if ( hooked is not null ) hooked.Damaged -= OnHurt; }

	void OnHurt( DamageInfo d )
	{
		if ( d.Attacker is PlayerAfflictions || d.Damage <= 0.5f || GoreSystem.Get( controller ) is not { } gore ) return;
		var body = controller.GlobalPosition + Vector3.Up * 1.1f;
		var at = d.Position == Vector3.Zero || d.Position.DistanceTo( body ) > 1.5f ? body + Rng.InSphere( 0.3f ) : d.Position;
		var from = d.Origin == Vector3.Zero ? at + Rng.OnSphere() : d.Origin;
		var away = (at - from).SafeNormal();
		if ( away == Vector3.Zero ) away = Rng.OnSphere();
		Sprays++;
		var n = (int)MathX.Clamp( 4f + d.Damage / 4f, 4f, 18f );
		gore.GooBurst( at, -away + Vector3.Up * 0.3f, Oil, n, 240f, 0.9f );
		gore.GooSpray( at, away, 2, Oil, 160f, 0.5f, 1.1f );
		Fx.ImpactWorld( at, -away );
		// A hard hit: oil across the visor (your own robot only).
		if ( !controller.IsRemote && d.Damage >= VisorFrom ) survivor.Afflictions?.Splattered( Oil, d.Damage >= 30f ? 2 : 1 );
	}
}
