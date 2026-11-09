using Godot;

/// <summary> Damage dealt to something (s&box DamageInfo). </summary>
public struct DamageInfo
{
	public float Damage;
	public Node Attacker;
	public Vector3 Position;
	public Vector3 Origin;
}

/// <summary> Port of s&box Health: hit points for players and enemies. Damage just calls OnDamage. </summary>
public partial class Health : Node
{
	public float MaxHealth = 100f;
	public float Current;
	public bool IsDead;
	/// <summary> Dev tool: ignore all damage while set. </summary>
	public bool Invulnerable;
	public float Fraction => MaxHealth > 0f ? Current / MaxHealth : 0f;
	public TimeSince TimeSinceDamaged { get; private set; } = 999f;

	public event Action<DamageInfo> Damaged;
	public event Action Died;

	public override void _Ready()
	{
		Name = "Health";
		if ( Current <= 0f && !IsDead ) Current = MaxHealth;
	}

	public void OnDamage( DamageInfo damage )
	{
		if ( IsDead || damage.Damage <= 0f || Invulnerable ) return;
		if ( DevFlags.OneShot && GetParent() is not PlayerController ) damage.Damage = MathF.Max( damage.Damage, Current );
		Current = MathF.Max( 0f, Current - damage.Damage );
		TimeSinceDamaged = 0f;
		Damaged?.Invoke( damage );
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

	public void IncreaseMax( float amount )
	{
		MaxHealth += amount;
		Current = MaxHealth;
	}

	public void ResetTo( float maxHealth )
	{
		MaxHealth = maxHealth;
		Current = maxHealth;
		IsDead = false;
		TimeSinceDamaged = 999f;
	}
}

/// <summary> Port of s&box DevFlags: switches the dev menu flips. </summary>
public static class DevFlags
{
	public static bool OneShot;
	public static bool FreezeEnemies;
	public static bool Noclip;
	public static bool HideWeapon;
	public static bool FreeCam;
	public static bool Placing;
	public static float Fov;
}
