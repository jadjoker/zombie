public enum CardKind
{
	Burn,
	Poison,
	Slow,
	Ink,
}

/// <summary>
/// A playing card flung by MC61, spinning flat like a frisbee. Its colour says its curse: red burns,
/// green poisons, blue slows, purple inks your screen. It sticks into walls where it lands.
/// Every machine flies its own copy; only the host's (authority) copy hurts anyone.
/// Confetti cards (a burst briefcase) are just for show and fall under gravity.
/// </summary>
public sealed class Card : Component
{
	Vector3 velocity;
	CardKind kind;
	bool authority;
	bool confetti;
	float damage;
	GameObject attacker;
	TimeSince age;
	bool stuck;
	float spin;

	public static Color ColorOf( CardKind kind ) => kind switch
	{
		CardKind.Burn => new Color( 1f, 0.3f, 0.05f ),
		CardKind.Poison => new Color( 0.3f, 1f, 0.15f ),
		CardKind.Slow => new Color( 0.25f, 0.75f, 1f ),
		_ => new Color( 0.55f, 0.15f, 0.95f ),
	};

	public static void Spawn( Scene scene, Vector3 position, Vector3 velocity, CardKind kind, bool authority, float damage, GameObject attacker, bool confetti = false )
	{
		var go = new GameObject( true, "Card" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;

		// White card with a coloured face.
		var white = new GameObject( go, true, "Face" );
		var scale = Shapes.BoxScale( new Vector3( 9f, 13f, 0.5f ) );
		white.LocalScale = scale;
		white.LocalPosition = -Shapes.Box.Bounds.Center * scale;
		var r = white.AddComponent<ModelRenderer>();
		r.Model = Shapes.Box;
		r.Tint = Color.White;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;

		var mark = new GameObject( go, true, "Mark" );
		var markScale = Shapes.BoxScale( new Vector3( 6.5f, 10f, 0.8f ) );
		mark.LocalScale = markScale;
		mark.LocalPosition = -Shapes.Box.Bounds.Center * markScale;
		var m = mark.AddComponent<ModelRenderer>();
		m.Model = Shapes.Box;
		m.Tint = ColorOf( kind ) * 1.6f;
		m.RenderType = ModelRenderer.ShadowRenderType.Off;

		var card = go.AddComponent<Card>();
		card.velocity = velocity;
		card.kind = kind;
		card.authority = authority;
		card.damage = damage;
		card.attacker = attacker;
		card.confetti = confetti;
		card.spin = Game.Random.Float( 900f, 1400f ) * (Game.Random.Float() < 0.5f ? -1f : 1f);
		card.age = 0f;
	}

	protected override void OnUpdate()
	{
		if ( DevFlags.FreezeEnemies ) { age = age - Time.Delta; return; }   // hangs in the air, and doesn't age
		var dt = Time.Delta;
		var life = confetti ? 3f : 2.6f;
		if ( age > life + (stuck ? 2f : 0f) )
		{
			GameObject.Destroy();
			return;
		}
		if ( stuck ) return;

		if ( confetti )
		{
			velocity += Vector3.Down * 500f * dt;
			velocity *= MathF.Exp( -1.5f * dt );
			WorldRotation *= Rotation.From( spin * dt * 0.4f, spin * dt * 0.3f, 0f );
			var next = WorldPosition + velocity * dt;
			if ( GoreSystem.Get( Scene )?.TraceWorld( WorldPosition, next ) is { } ground )
			{
				WorldPosition = ground.Position + ground.Normal * 0.5f;
				stuck = true;
				return;
			}
			WorldPosition = next;
			return;
		}

		// Flat, spinning, straight out.
		var from = WorldPosition;
		var to = from + velocity * dt;
		WorldRotation = Rotation.FromYaw( age * spin );

		var tr = Scene.Trace.Sphere( 7f, from, to )
			.WithoutTags( "creature", "gib", "zombieblocker", "viewmodel", "zombie" )
			.Run();
		if ( tr.Hit )
		{
			var player = tr.GameObject.IsValid() ? tr.GameObject.Components.Get<PlayerSurvivor>( FindMode.EverythingInSelfAndAncestors ) : null;
			if ( player is not null )
			{
				if ( authority && player.IsAlive )
				{
					player.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = attacker, Position = tr.HitPosition, Origin = from } );
					player.GetComponent<PlayerAfflictions>()?.Cursed( kind );
				}
				GoreSystem.Get( Scene )?.GooBurst( tr.HitPosition, -velocity, ColorOf( kind ), 8, 160f, 0.8f );
				GameObject.Destroy();
				return;
			}

			// Stuck in the wall, edge first.
			WorldPosition = tr.HitPosition - velocity.Normal * 2f;
			WorldRotation = Rotation.LookAt( velocity.Normal ) * Rotation.FromRoll( Game.Random.Float( -30f, 30f ) );
			stuck = true;
			return;
		}

		WorldPosition = to;
	}
}
