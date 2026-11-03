/// <summary>
/// Hit points for anything that can be hurt: players and zombies.
/// Anything that deals damage just calls OnDamage on it.
/// </summary>
public sealed class Health : Component, Component.IDamageable
{
	[Property] public float MaxHealth { get; set; } = 100f;

	public float Current { get; private set; }
	public bool IsDead { get; private set; }
	public float Fraction => MaxHealth > 0 ? Current / MaxHealth : 0f;
	public TimeSince TimeSinceDamaged { get; private set; } = 999f;

	public event System.Action<DamageInfo> Damaged;
	public event System.Action Died;

	protected override void OnStart()
	{
		Current = MaxHealth;
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( IsDead || damage.Damage <= 0f ) return;

		Current = MathF.Max( 0f, Current - damage.Damage );
		TimeSinceDamaged = 0f;
		Damaged?.Invoke( damage );

		// Damaged handlers can kill us themselves (e.g. a severed head), so check before dying again.
		if ( Current <= 0f && !IsDead )
		{
			IsDead = true;
			Died?.Invoke();
		}
	}

	public void Heal( float amount )
	{
		if ( IsDead ) return;
		Current = MathF.Min( MaxHealth, Current + amount );
	}

	/// <summary> Raises max health and fills to it. Used by the health upgrade station. </summary>
	public void IncreaseMax( float amount )
	{
		MaxHealth += amount;
		Current = MaxHealth;
	}

	/// <summary> Sets a new max and fills up to it. Used by the spawner to scale zombies per wave. </summary>
	public void ResetTo( float maxHealth )
	{
		MaxHealth = maxHealth;
		Current = maxHealth;
		IsDead = false;
	}
}
