/// <summary>
/// A blob of ink thrown by a Squilien. Every machine flies its own copy for looks; only the host's copy
/// (authority) actually hurts and inks a player. Splats on whatever it hits.
/// </summary>
public sealed class InkBall : Component
{
	Vector3 velocity;
	float gravity;
	Color color;
	bool authority;
	float damage;
	GameObject attacker;
	TimeSince age;
	TimeSince lastDrip;

	const float Radius = 6f;

	public static void Spawn( Scene scene, Vector3 position, Vector3 velocity, float gravity, Color color, bool authority, float damage, GameObject attacker )
	{
		var go = new GameObject( true, "Ink Ball" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		go.WorldScale = Shapes.SphereScale( Radius );
		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = Shapes.Sphere;
		renderer.Tint = color;

		var ball = go.AddComponent<InkBall>();
		ball.velocity = velocity;
		ball.gravity = gravity;
		ball.color = color;
		ball.authority = authority;
		ball.damage = damage;
		ball.attacker = attacker;
		ball.age = 0f;
	}

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		if ( age > 5f ) { GameObject.Destroy(); return; }

		velocity += Vector3.Down * gravity * dt;
		var from = WorldPosition;
		var to = from + velocity * dt;

		var tr = Scene.Trace.Sphere( Radius, from, to )
			.WithoutTags( "creature", "gib", "zombieblocker", "viewmodel", "zombie" )
			.Run();

		if ( tr.Hit )
		{
			Splash( tr );
			return;
		}

		WorldPosition = to;

		// Wobbles as it flies, dripping a little.
		var wobble = 1f + 0.15f * MathF.Sin( age * 30f );
		WorldScale = new Vector3( wobble, 2f - wobble, 1f ) * Shapes.SphereScale( Radius );
		if ( lastDrip > 0.06f )
		{
			lastDrip = 0f;
			GooDroplet.Spawn( Scene, from, velocity * 0.1f + Vector3.Random * 30f, color, Game.Random.Float( 1f, 2f ) );
		}
	}

	void Splash( SceneTraceResult tr )
	{
		var gore = GoreSystem.Get( Scene );
		var player = tr.GameObject.IsValid() ? tr.GameObject.Components.Get<PlayerSurvivor>( FindMode.EverythingInSelfAndAncestors ) : null;

		gore?.GooBurst( tr.HitPosition, tr.Normal + velocity.Normal * -0.3f, color, 10, 200f, 1.2f );

		if ( player is not null )
		{
			if ( authority && player.IsAlive )
			{
				player.Health?.OnDamage( new DamageInfo
				{
					Damage = damage,
					Attacker = attacker,
					Position = tr.HitPosition,
					Origin = tr.HitPosition - velocity.Normal * 50f,
				} );
				player.GetComponent<PlayerAfflictions>()?.Inked();
			}
		}
		else
		{
			gore?.GooSplat( tr.HitPosition, tr.Normal, Game.Random.Float( 1.2f, 1.8f ), color );
			gore?.GooSpray( tr.HitPosition, -tr.Normal, 2, color, 120f, 0.4f, 0.8f );
		}

		GameObject.Destroy();
	}
}
