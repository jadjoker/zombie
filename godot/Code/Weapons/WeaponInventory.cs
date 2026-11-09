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
