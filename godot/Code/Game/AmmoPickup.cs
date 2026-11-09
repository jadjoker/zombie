using Godot;
using System.Linq;

/// <summary>
/// Playtest 1 asked for a way to gain ammo between buy stations: creatures sometimes drop a glowing ammo box.
/// Walking over it gives every gun one magazine back into its reserve (up to twice its starting reserve).
/// It bobs and spins on the floor and fades away after a while.
/// </summary>
public partial class AmmoPickup : Node3D
{
	public static float DropChance = 0.3f;
	const float Life = 30f;
	const float Reach = 60f;   // s&box units

	TimeSince age;
	float baseY;
	MeshInstance3D box;
	StandardMaterial3D glow;

	/// <summary> Maybe drops one where a creature died (lands on the floor below). </summary>
	public static void MaybeDrop( Node3D at )
	{
		if ( at is null || !GodotObject.IsInstanceValid( at ) || Rng.Float() > DropChance ) return;
		var root = at.GetTree()?.CurrentScene;
		if ( root is null ) return;
		var from = at.GlobalPosition + Vector3.Up * 20f * U.In;
		var down = Trace.Ray( at, from, from + Vector3.Down * 3000f * U.In, Layers.World );
		var spot = down.Hit ? down.HitPosition + Vector3.Up * 14f * U.In : at.GlobalPosition;
		var pickup = new AmmoPickup { Name = "Ammo Pickup", Position = spot };
		root.AddChild( pickup );
	}

	static PackedScene model;

	public override void _Ready()
	{
		age = 0f;
		baseY = GlobalPosition.Y;
		box = new MeshInstance3D();   // spins; holds the Blender model (Tools/blender/ammo_box.py)
		AddChild( box );
		model ??= ResourceLoader.Exists( "res://assets/models/props/ammo_box.glb" ) ? GD.Load<PackedScene>( "res://assets/models/props/ammo_box.glb" ) : null;
		glow = new StandardMaterial3D { AlbedoColor = new Color( 1f, 0.72f, 0.18f ), EmissionEnabled = true, Emission = new Color( 1f, 0.7f, 0.15f ), EmissionEnergyMultiplier = 2.2f };
		if ( model?.Instantiate() is Node3D can )
		{
			box.AddChild( can );
			can.Position = Vector3.Down * 0.11f;   // origin at its base: centre it on the bob point
			foreach ( var mesh in can.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
			{
				mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
				for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
					if ( mesh.Mesh.SurfaceGetMaterial( i )?.ResourceName == "AmmoGlow" ) mesh.SetSurfaceOverrideMaterial( i, glow );
			}
		}
		else
		{
			box.Mesh = new BoxMesh { Size = new Vector3( 14f, 9f, 9f ) * U.In };
			box.MaterialOverride = glow;
		}
		AddChild( new OmniLight3D { LightColor = new Color( 1f, 0.75f, 0.3f ), LightEnergy = 1.2f, OmniRange = 90f * U.In } );
	}

	public override void _Process( double delta )
	{
		var t = (float)age;
		if ( t > Life ) { QueueFree(); return; }
		GlobalPosition = GlobalPosition with { Y = baseY + MathF.Sin( t * 2.5f ) * 3f * U.In };
		box.Rotation = new Vector3( 0f, t * 1.6f, 0f );
		// Blinks for its last six seconds, then shrinks away.
		glow.EmissionEnergyMultiplier = t > Life - 6f ? 1.2f + 1.6f * MathF.Max( 0f, MathF.Sin( t * 12f ) ) : 2.2f;
		box.Scale = Vector3.One * 1.5f * MathX.Clamp( (Life - t) / 0.4f, 0.01f, 1f );   // 1.5x real size so it reads

		foreach ( var survivor in PlayerSurvivor.All )
		{
			if ( !survivor.IsAlive || survivor.Controller.GlobalPosition.DistanceTo( GlobalPosition ) > Reach * U.In ) continue;
			var inventory = survivor.Controller.GetNodeOrNull<WeaponInventory>( "WeaponInventory" );
			if ( inventory is null ) continue;
			var took = false;
			foreach ( var weapon in inventory.Weapons )
			{
				var room = weapon.StartingReserve * 2 - weapon.Reserve;
				if ( room <= 0 ) continue;
				weapon.AddAmmo( Math.Min( room, weapon.MagazineSize ) );
				took = true;
			}
			if ( !took ) continue;   // full up: leave it for later
			Sfx.Play( "mag_in", GlobalPosition, 0.8f, 1.1f );
			Sfx.Play2D( "buy", 0.5f, 1.2f );
			QueueFree();
			return;
		}
	}
}
