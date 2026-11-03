/// <summary>
/// Switches between the PlayerWeapon components on the player: number keys pick a slot,
/// the scroll wheel cycles. Only the active weapon is enabled, so everything that asks the
/// player for "its weapon" gets the one in hand.
/// </summary>
public sealed class WeaponInventory : Component
{
	[Property] public float SwitchCooldown { get; set; } = 0.25f;

	public PlayerWeapon Current { get; private set; }
	/// <summary> Slot of the weapon in hand, synced so other players see the right gun. </summary>
	[Sync] public int CurrentSlot { get; set; } = 1;
	public IReadOnlyList<PlayerWeapon> Weapons => weapons;

	readonly List<PlayerWeapon> weapons = new();
	TimeSince timeSinceSwitch = 999f;

	static readonly string[] SlotActions = { "Slot1", "Slot2", "Slot3", "Slot4", "Slot5", "Slot6", "Slot7", "Slot8", "Slot9" };

	protected override void OnStart()
	{
		weapons.Clear();
		weapons.AddRange( Components.GetAll<PlayerWeapon>( FindMode.EverythingInSelf ).OrderBy( w => w.Slot ) );
		Select( weapons.FirstOrDefault( w => w.Enabled ) ?? weapons.FirstOrDefault(), force: true );
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || weapons.Count < 2 ) return;
		if ( !(GetComponent<PlayerSurvivor>()?.IsAlive ?? true) ) return;

		// Number keys: Slot1 = 1, Slot2 = 2, ...
		for ( var i = 0; i < SlotActions.Length; i++ )
		{
			if ( !Input.Pressed( SlotActions[i] ) ) continue;
			var slot = weapons.FirstOrDefault( w => w.Slot == i + 1 );
			if ( slot.IsValid() ) Select( slot );
		}

		// Scroll wheel: down = next, up = previous.
		var wheel = Input.MouseWheel.y;
		if ( wheel < 0f || Input.Pressed( "SlotNext" ) ) Cycle( 1 );
		else if ( wheel > 0f || Input.Pressed( "SlotPrev" ) ) Cycle( -1 );
	}

	public void Cycle( int step )
	{
		if ( weapons.Count == 0 ) return;
		var index = Math.Max( 0, weapons.IndexOf( Current ) );
		Select( weapons[(index + step + weapons.Count) % weapons.Count] );
	}

	public void Select( PlayerWeapon weapon, bool force = false )
	{
		if ( !weapon.IsValid() ) return;
		if ( !force && (weapon == Current || timeSinceSwitch < SwitchCooldown) ) return;
		timeSinceSwitch = 0f;

		foreach ( var w in weapons )
			w.Enabled = w == weapon;
		Current = weapon;
		if ( !IsProxy ) CurrentSlot = weapon.Slot;
	}
}
