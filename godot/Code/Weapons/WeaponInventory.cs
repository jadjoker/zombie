using Godot;

/// <summary>
/// Port of s&box WeaponInventory: switches between the PlayerWeapon nodes on the player. Number keys pick a slot,
/// the scroll wheel cycles. Only the active weapon is enabled.
/// </summary>
public partial class WeaponInventory : Node
{
	public float SwitchCooldown = 0.25f;
	public PlayerWeapon Current { get; private set; }
	public IReadOnlyList<PlayerWeapon> Weapons => weapons;

	readonly List<PlayerWeapon> weapons = new();
	TimeSince timeSinceSwitch = 999f;

	public override void _Ready()
	{
		Name = "WeaponInventory";
		CallDeferred( MethodName.Gather );
	}

	void Gather()
	{
		weapons.Clear();
		weapons.AddRange( GetParent().GetChildren().OfType<PlayerWeapon>().OrderBy( w => w.Slot ) );
		Select( weapons.FirstOrDefault(), force: true );
	}

	public override void _UnhandledInput( InputEvent e )
	{
		if ( weapons.Count < 2 ) return;
		// The D-pad: left and right.
		if ( e.IsActionPressed( "weapon_next" ) ) Cycle( 1 );
		else if ( e.IsActionPressed( "weapon_prev" ) ) Cycle( -1 );
		if ( e is InputEventKey { Pressed: true, Echo: false } key && key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode <= Key.Key9 )
		{
			var slot = weapons.FirstOrDefault( w => w.Slot == (int)(key.PhysicalKeycode - Key.Key0) );
			if ( slot is not null ) Select( slot );
		}
		if ( e is InputEventMouseButton { Pressed: true } wheel )
		{
			if ( wheel.ButtonIndex == MouseButton.WheelDown ) Cycle( 1 );
			else if ( wheel.ButtonIndex == MouseButton.WheelUp ) Cycle( -1 );
		}
	}

	/// <summary>
	/// Gives the player a new weapon (the trident shooter from its station) and puts it in their hands. It joins at
	/// its slot, keeps the damage upgrades already bought (they apply to every weapon), and the melee swing hides it
	/// like the others.
	/// </summary>
	public void Add( PlayerWeapon weapon )
	{
		if ( weapon is null || weapons.Contains( weapon ) ) return;
		var controller = GetParent<PlayerController>();
		weapon.DamageLevel = weapons.Count > 0 ? weapons.Max( w => w.DamageLevel ) : 0;
		if ( controller.GetChildren().OfType<PlayerMelee>().FirstOrDefault() is { } melee ) weapon.MeleeActive = () => melee.IsSwinging;
		controller.AddChild( weapon );
		weapons.Add( weapon );
		weapons.Sort( ( a, b ) => a.Slot.CompareTo( b.Slot ) );
		Select( weapon, force: true );
		Sfx.Play2D( "weapon_switch", 0.7f );
	}

	/// <summary> Has the player got a weapon in this slot. </summary>
	public bool HasSlot( int slot ) => weapons.Any( w => w.Slot == slot );

	public void Cycle( int step )
	{
		if ( weapons.Count == 0 ) return;
		var index = Math.Max( 0, weapons.IndexOf( Current ) );
		Select( weapons[(index + step + weapons.Count) % weapons.Count] );
	}

	public void Select( PlayerWeapon weapon, bool force = false )
	{
		if ( weapon is null ) return;
		if ( !force && (weapon == Current || timeSinceSwitch < SwitchCooldown) ) return;
		timeSinceSwitch = 0f;
		foreach ( var w in weapons ) w.Active = w == weapon;
		Current = weapon;
		if ( !force ) Sfx.Play2D( "weapon_switch", 0.7f );
	}
}
