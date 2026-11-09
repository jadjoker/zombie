using Godot;

public enum CardKind { Burn, Poison, Slow, Ink }

/// <summary>
/// Port of s&box Card: a playing card flung by MC61, spinning flat like a frisbee. Its colour says its curse: red
/// burns, green poisons, blue slows, purple inks your screen. It sticks into walls where it lands. Confetti cards
/// (a burst hand) are just for show and fall under gravity.
/// </summary>
public partial class Card : Node3D
{
	Vector3 velocity;   // s&box units/s
	CardKind kind;
	bool confetti;
	float damage;
	Node attacker;
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

	public static void Spawn( Vector3 position, Vector3 velocity, CardKind kind, float damage, Node attacker, bool confetti = false )
	{
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( root is null ) return;
		var card = new Card { Name = "Card", velocity = velocity, kind = kind, damage = damage, attacker = attacker, confetti = confetti };
		root.AddChild( card );
		card.GlobalPosition = position;
		Shapes.Part( card, "Face", Vector3.Zero, Shapes.BoxSize( 9f, 13f, 0.5f ), Colors.White, box: true, shadow: false );
		Shapes.Part( card, "Mark", Vector3.Zero, Shapes.BoxSize( 6.5f, 10f, 0.8f ), ColorOf( kind ), box: true, glow: true, shadow: false );
		card.spin = Rng.Float( 900f, 1400f ) * (Rng.Float() < 0.5f ? -1f : 1f);
		card.age = 0f;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( DevFlags.FreezeEnemies ) { age = age - dt; return; }
		var life = confetti ? 3f : 2.6f;
		if ( age > life + (stuck ? 2f : 0f) ) { QueueFree(); return; }
		if ( stuck ) return;

		if ( confetti )
		{
			velocity += Vector3.Down * 500f * dt;
			velocity *= MathF.Exp( -1.5f * dt );
			GlobalBasis = GlobalBasis * new Angles( spin * dt * 0.4f, spin * dt * 0.3f, 0f ).ToBasis();
			var next = GlobalPosition + velocity * U.In * dt;
			if ( GoreSystem.Get()?.TraceWorld( GlobalPosition, next ) is { } ground )
			{
				GlobalPosition = ground.Position + ground.Normal * 0.5f * U.In;
				stuck = true;
				return;
			}
			GlobalPosition = next;
			return;
		}

		var from = GlobalPosition;
		var to = from + velocity * U.In * dt;
		GlobalBasis = new Angles( 0f, age * spin, 0f ).ToBasis();
		var tr = Trace.Sphere( this, 7f * U.In, from, to, Layers.World | Layers.Player );
		if ( tr.Hit )
		{
			var survivor = (tr.Node as PlayerController)?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
			if ( survivor is not null )
			{
				if ( survivor.IsAlive )
				{
					survivor.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = attacker, Position = tr.HitPosition, Origin = from } );
					survivor.Afflictions?.Cursed( (PlayerAfflictions.Curse)(int)kind );
				}
				GoreSystem.Get()?.GooBurst( tr.HitPosition, -velocity, ColorOf( kind ), 8, 160f, 0.8f );
				QueueFree();
				return;
			}
			GlobalPosition = tr.HitPosition - velocity.SafeNormal() * 2f * U.In;
			GlobalBasis = Shapes.LookAt( velocity ) * new Angles( 0f, 0f, Rng.Float( -30f, 30f ) ).ToBasis();
			stuck = true;
			return;
		}
		GlobalPosition = to;
	}
}
