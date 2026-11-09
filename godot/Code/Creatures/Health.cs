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
		if ( DockHouse.Diving && GetParent() is PlayerController or PlayerSurvivor ) return;   // can't be hurt on the way down
		if ( DevFlags.OneShot && GetParent() is not PlayerController ) damage.Damage = MathF.Max( damage.Damage, Current );
		Current = MathF.Max( 0f, Current - damage.Damage );
		TimeSinceDamaged = 0f;
		Damaged?.Invoke( damage );
		if ( Current <= 0f && !IsDead )
		{
			IsDead = true;
			Died?.Invoke();
			if ( GetParent() is Node3D body && body is not PlayerController )
			{
				Sfx.Play( "creature_death", body.GlobalPosition, 0.9f, Rng.Float( 0.9f, 1.1f ) );
				if ( !DockHouse.Diving ) AmmoPickup.MaybeDrop( body );   // none mid-dive: they'd land far away
			}
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
	/// <summary> Any hit on an enemy kills it. </summary>
	public static bool OneShot;
	/// <summary> Enemies stop thinking; projectiles hang in the air. </summary>
	public static bool FreezeEnemies;
	/// <summary> Fly through walls (the dev menu drives the player). </summary>
	public static bool Noclip;
	public static bool HideHud;
	public static bool HideWeapon;
	/// <summary> Camera detached from the player and flown separately. </summary>
	public static bool FreeCam;
	public static Vector3 FreeCamPosition;
	public static Angles FreeCamAngles;
	/// <summary> Placing enemies: left click spawns at the crosshair, so the gun doesn't fire. </summary>
	public static bool Placing;
	/// <summary> Field of view override; 0 = the game's own. </summary>
	public static float Fov;
	public static bool ShowHitboxes;
	public static bool ShowStats;

	public static void Reset()
	{
		FreezeEnemies = Noclip = Placing = HideHud = HideWeapon = FreeCam = OneShot = ShowHitboxes = ShowStats = false;
		Fov = 0f;
	}
}
