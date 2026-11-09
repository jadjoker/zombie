using Godot;

/// <summary>
/// Builds the local player the way the s&box Player Template plus PlayerSurvivor.OnStart did: controller, movement
/// kit, camera juice, weapon inventory with the rifle and shotgun, axe melee, survivor (health, points, afflictions),
/// plus the HUD layers.
/// </summary>
public static class PlayerFactory
{
	public static PlayerController Create( Node parent, Vector3 position )
	{
		var player = new PlayerController { Name = "Player" };
		parent.AddChild( player );
		player.GlobalPosition = position;
		player.AddChild( new PlayerMovement() );
		player.AddChild( new CameraJuice() );
		player.AddChild( new WeaponInventory() );
		player.AddChild( PlayerWeapon.Rifle() );
		player.AddChild( PlayerWeapon.Shotgun() );
		player.AddChild( new PlayerMelee() );
		player.AddChild( new WaterFeel() );
		player.AddChild( new Jetpack() );
		player.AddChild( new AllyCommander() );
		player.AddChild( new PlayerUse() );
		player.AddChild( new PlayerRig() );
		var survivor = new PlayerSurvivor();
		player.AddChild( survivor );

		var hud = new CanvasLayer { Name = "Hud" };
		parent.AddChild( hud );
		hud.AddChild( HudOverlays.Create( survivor ) );
		if ( parent.GetTree().Root.FindChild( "DevMenu", true, false ) is null ) parent.AddChild( new DevMenu() );
		hud.AddChild( WeaponHud.Create( player ) );
		hud.AddChild( GameHud.Create( survivor ) );
		return player;
	}
}
