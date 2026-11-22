using Godot;

/// <summary>
/// Builds the local player the way the s&box Player Template plus PlayerSurvivor.OnStart did: controller, movement
/// kit, camera juice, weapon inventory with the robot's weapons (RobotClass), axe melee, survivor (health, points, afflictions),
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
		foreach ( var weapon in RobotClass.Current.Kit() ) player.AddChild( weapon );   // the picked robot's weapons
		player.AddChild( new PlayerMelee() );
		player.AddChild( new PlayerPerks() );
		player.AddChild( new PlayerSkills() );
		player.AddChild( new WaterFeel() );
		player.AddChild( new Jetpack() );
		player.AddChild( new TridentSurge() );   // Trident-Bot: the magic trident that leads its surge
		player.AddChild( new AllyCommander() );
		player.AddChild( new PlayerUse() );
		player.AddChild( new MoveModeFloat() );
		player.AddChild( new PlayerRig() );
		player.AddChild( new HealPlecos() );   // third person: little fish feeding on the robot while it heals
		var survivor = new PlayerSurvivor();
		player.AddChild( survivor );

		var hud = new CanvasLayer { Name = "Hud" };
		parent.AddChild( hud );
		hud.AddChild( VisorOverlay.Create( survivor ) );   // the faceplate: under every HUD piece
		hud.AddChild( HudOverlays.Create( survivor ) );
		hud.AddChild( EnemyBars.Create() );   // health bars and damage numbers over enemies
		if ( parent.GetTree().Root.FindChild( "DevMenu", true, false ) is null ) parent.AddChild( new DevMenu() );
		if ( parent.GetTree().Root.FindChild( "PauseMenu", true, false ) is null ) parent.AddChild( new PauseMenu() );
		if ( ClassPicker.Find( parent.GetTree() ) is null ) parent.AddChild( new ClassPicker() );   // after the pause menu: it sees Esc first
		hud.AddChild( WeaponHud.Create( player ) );
		hud.AddChild( GameHud.Create( survivor ) );
		return player;
	}
}
