/// <summary>
/// Keeps the player's ally spider and gives it orders. Press Command (Z):
///   looking at an enemy   -> it attacks that enemy
///   looking at the ground -> it goes there and guards the spot
///   looking at the spider, or at nothing -> it comes back and follows you
/// Added to the local player by PlayerSurvivor; no scene setup needed.
/// </summary>
public sealed class AllyCommander : Component
{
	SeaSpider ally;
	TimeUntil nextCheck;

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		var survivor = GetComponent<PlayerSurvivor>();
		if ( !survivor.IsValid() || !survivor.IsAlive ) return;

		// Keep an ally around (it's lost on game over, or if the dev menu removed it).
		if ( nextCheck )
		{
			nextCheck = 2f;
			ally = Scene.GetAllComponents<SeaSpider>().FirstOrDefault( s => s.IsAlly && s.OwnerPlayer == GameObject && !s.IsDead );
			if ( !ally.IsValid() && (!Networking.IsActive || Networking.IsHost) )
				ally = SeaSpider.SpawnAlly( Scene, survivor );
		}

		if ( ally.IsValid() && Input.Pressed( "Command" ) && !DevFlags.Placing && !DevFlags.FreeCam )
			Issue();
	}

	void Issue()
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		var start = camera.WorldPosition;
		var forward = camera.WorldRotation.Forward;
		var end = start + forward * 4000f;

		// An enemy under the crosshair (floating ones are triggers, so look for those too).
		var tr = Scene.Trace.Sphere( 18f, start, end )
			.IgnoreGameObjectHierarchy( GameObject )
			.UseHitboxes()
			.WithoutTags( "zombieblocker", "viewmodel", "gore" )
			.Run();
		var creature = Scene.Trace.Sphere( 18f, start, end ).HitTriggersOnly().WithTag( "creature" ).Run();
		if ( creature.Hit && (!tr.Hit || creature.Distance < tr.Distance) ) tr = creature;

		if ( tr.Hit && tr.GameObject.IsValid() )
		{
			GameObject enemy = null;
			var part = tr.GameObject.Components.Get<CreaturePart>( FindMode.EverythingInSelfAndAncestors );
			if ( part.IsValid() && part.Creature.IsValid() && !(part.Creature is SeaSpider { IsAlly: true }) ) enemy = part.Creature.GameObject;
			else if ( tr.GameObject.Components.Get<Zombie>( FindMode.EverythingInSelfAndAncestors ) is { } zombie ) enemy = zombie.GameObject;
			if ( enemy.IsValid() )
			{
				ally.Command( SeaSpider.ModeAttack, enemy.WorldPosition, enemy );
				Marker( enemy.WorldPosition, new Color( 1f, 0.25f, 0.2f ), 22f );
				return;
			}
		}

		// Looking at the spider itself: call it back.
		var toAlly = ally.WorldPosition + Vector3.Up * 40f - start;
		if ( toAlly.Length < 3000f && Vector3.Cross( forward, toAlly ).Length < 60f && Vector3.Dot( forward, toAlly ) > 0f )
		{
			ally.Command( SeaSpider.ModeFollow, Vector3.Zero, null );
			Marker( ally.WorldPosition, new Color( 0.3f, 1f, 0.5f ), 20f );
			return;
		}

		// The ground: go there and guard it.
		var ground = Scene.Trace.Ray( start, end )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "creature", "zombieblocker", "gib", "player", "viewmodel", "gore" )
			.Run();
		if ( ground.Hit )
		{
			ally.Command( SeaSpider.ModeHold, ground.HitPosition, null );
			Marker( ground.HitPosition, new Color( 0.25f, 0.9f, 1f ), 18f );
			return;
		}

		ally.Command( SeaSpider.ModeFollow, Vector3.Zero, null );
		Marker( ally.WorldPosition, new Color( 0.3f, 1f, 0.5f ), 20f );
	}

	/// <summary> A glowing ring that fades after a moment, showing where the order went. </summary>
	void Marker( Vector3 position, Color color, float radius )
	{
		var go = new GameObject( true, "Command Marker" );
		go.WorldPosition = position + Vector3.Up * 2f;
		go.LocalScale = new Vector3( radius, radius, radius * 0.12f ) * Shapes.SphereScale( 1f );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Shapes.Sphere;
		r.Tint = color * 3f;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		go.AddComponent<SelfDestruct>().Lifetime = 1.4f;
	}
}
