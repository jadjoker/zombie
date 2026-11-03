/// <summary>
/// Hit points for anything that can be hurt: players and zombies.
/// Anything that deals damage just calls OnDamage on it.
///
/// Networking: the object's owner is in charge of its health (the host for zombies, each player
/// for themselves). Damage dealt on another machine is forwarded to the owner, and the owner's
/// values are synced to everyone. Without a network it all just runs locally.
/// </summary>
public sealed class Health : Component, Component.IDamageable
{
	[Property, Sync] public float MaxHealth { get; set; } = 100f;

	[Sync] public float Current { get; set; }
	[Sync] public bool IsDead { get; set; }
	public float Fraction => MaxHealth > 0 ? Current / MaxHealth : 0f;
	public TimeSince TimeSinceDamaged { get; private set; } = 999f;

	/// <summary> Fired on the owner when damage lands. </summary>
	public event System.Action<DamageInfo> Damaged;
	/// <summary> Fired once on the owner when health runs out. </summary>
	public event System.Action Died;

	bool started;

	protected override void OnStart()
	{
		if ( IsProxy || started ) return;
		started = true;
		if ( Current <= 0f && !IsDead ) Current = MaxHealth;
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( IsDead || damage.Damage <= 0f ) return;

		// Not ours: ask the owner to take it.
		if ( IsProxy )
		{
			DamageOnOwner( damage.Damage, damage.Position, damage.Origin, damage.Attacker );
			return;
		}

		Apply( damage );
	}

	[Rpc.Owner]
	void DamageOnOwner( float amount, Vector3 position, Vector3 origin, GameObject attacker )
	{
		Apply( new DamageInfo { Damage = amount, Position = position, Origin = origin, Attacker = attacker } );
	}

	void Apply( DamageInfo damage )
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

	/// <summary> Sets a new max and fills up to it. Used by the spawner, and to bring players back. </summary>
	public void ResetTo( float maxHealth )
	{
		started = true;
		MaxHealth = maxHealth;
		Current = maxHealth;
		IsDead = false;
		TimeSinceDamaged = 999f;
	}
}
