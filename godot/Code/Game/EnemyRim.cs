using Godot;

/// <summary>
/// Enemy readability (the clutter pass): every living enemy's meshes get <c>fx_enemy_rim.gdshader</c> as their
/// material overlay (a soft warm rim in plain sight, a faint silhouette when behind a plant or rock). Checks twice a
/// second, so meshes a creature builds or swaps later are covered too; a dead one loses it. Lives in the level.
/// </summary>
public partial class EnemyRim : Node
{
	static ShaderMaterial overlay;
	public static ShaderMaterial Overlay => overlay ??= new ShaderMaterial { Shader = GD.Load<Shader>( "res://assets/shaders/fx_enemy_rim.gdshader" ) };
	/// <summary> Tests: meshes given the overlay so far. </summary>
	public static int Applied;
	TimeSince sinceScan = 99f;

	public override void _Process( double delta )
	{
		if ( sinceScan < 0.5f ) return;
		sinceScan = 0f;
		foreach ( var creature in Creature.Everyone )
		{
			if ( !IsInstanceValid( creature ) || !creature.IsEnemy ) continue;
			var dead = creature.IsDead;
			// A creature's meshes hang under it or under its shootable parts (which can live elsewhere in the scene).
			var roots = CreaturePart.All.Where( p => IsInstanceValid( p ) && p.Creature == creature ).Cast<Node>().Prepend( creature );
			foreach ( var mesh in roots.SelectMany( r => r.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() ).Distinct() )
			{
				if ( dead ) { if ( mesh.MaterialOverlay == Overlay ) mesh.MaterialOverlay = null; continue; }
				if ( mesh.MaterialOverlay is not null || !mesh.Visible ) continue;
				mesh.MaterialOverlay = Overlay;
				Applied++;
			}
		}
	}
}
