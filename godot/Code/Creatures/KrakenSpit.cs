using Godot;

/// <summary>
/// The Kraken's mouth attacks (issue #30, stage 4).
/// Bite: anyone too close to its mouth is bitten again and again, thrown back each time, and may come away
/// irradiated and poisoned.
/// Ink: now and then it rears back and spits three globs of glowing ink that arc to where the player is heading.
/// A glob that lands near a player does a little damage, blots their view and leaves them glowing green, which
/// slows how fast their stamina comes back.
/// </summary>
public partial class Kraken
{
	/// <summary> A player within this many metres of the mouth is bitten. </summary>
	public float BiteRange = 4.2f;
	public float BiteDamage = 18f, BiteInterval = 0.9f;
	/// <summary> The chance a bite irradiates and poisons, and for how long (seconds). </summary>
	public float BiteRadiationChance = 0.5f, BiteRadiationTime = 7f;
	/// <summary> Ink: seconds it rears back before spitting, damage of a glob, how wide it splashes (metres), how long the glow lasts. </summary>
	public float InkWindTime = 0.8f, InkDamage = 8f, InkSplash = 3.2f, InkRadiationTime = 8f;
	/// <summary> How often it picks the ink over the slam when both are possible. </summary>
	public float InkChance = 0.35f;

	/// <summary> For tests: bites landed and ink globs spat so far. </summary>
	public int Bites { get; private set; }
	public int InkSpat { get; private set; }

	TimeSince sinceBite = 99f;
	bool inkLaunched;

	/// <summary> Where its mouth is in the world. </summary>
	public Vector3 MouthWorld => MouthPosition;

	/// <summary> Dev menu and tests: spit ink now. </summary>
	public void ForceInk()
	{
		if ( State == Mode.Idle && Attacking == Attack.None && !IsDead ) Begin( Attack.Ink );
	}

	/// <summary> For tests: no attack of its own for this long (bites still happen). </summary>
	public void HoldAttacks( float seconds ) => nextAttack = seconds;

	/// <summary> Idle and not frozen: bites whoever is at its mouth. </summary>
	void UpdateBite()
	{
		if ( sinceBite < BiteInterval || Attacking is Attack.Slam or Attack.Hold ) return;
		var mouth = MouthPosition;
		var bit = false;
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive || player.Controller is not { } controller ) continue;
			var body = player.WorldPosition + Vector3.Up * 0.9f;
			if ( body.DistanceTo( mouth ) > BiteRange ) continue;
			bit = true;
			Bites++;
			player.Health?.OnDamage( new DamageInfo { Damage = BiteDamage, Attacker = this, Position = body, Origin = mouth } );
			if ( Rng.Float() < BiteRadiationChance ) player.Afflictions?.Irradiate( BiteRadiationTime, poison: true );
			// Thrown clear, so it is a bite and not a grinder.
			var away = (body - mouth).Flat().SafeNormal();
			if ( away == Vector3.Zero ) away = -GlobalBasis.Z;
			if ( !controller.Scripted ) controller.Velocity = away * 9f + Vector3.Up * 4f;
			player.Juice?.Shake( 0.7f );
			gore.GooBurst( body, away, Goo, 6, 260f, 2f );
		}
		if ( !bit ) return;
		sinceBite = 0f;
		Sfx.Play( "crab_snap", mouth, 1f, 0.55f );
		Sfx.Play( "creature_attack", mouth, 0.9f, 0.6f );
		// It lunges at them.
		headLeanSpeed += new Vector2( -70f, 0f );
	}

	/// <summary> Is the mouth held open by a bite just now. </summary>
	bool Biting => sinceBite < 0.4f;

	/// <summary> The ink attack, once begun: it rears back with its mouth open, spits, and settles. </summary>
	void UpdateInk( float dt )
	{
		if ( attackTime < InkWindTime )
		{
			// Rearing back, quivering.
			headLeanSpeed += new Vector2( 55f, MathF.Sin( time * 40f ) * 30f ) * dt;
			return;
		}
		if ( !inkLaunched )
		{
			inkLaunched = true;
			SpitInk();
			headLeanSpeed += new Vector2( -110f, 0f );
		}
		if ( attackTime >= InkWindTime + 0.7f ) Begin( Attack.None );
	}

	void SpitInk()
	{
		var mouth = MouthPosition;
		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var aim = target is not null ? target.WorldPosition : GlobalPosition - GlobalBasis.Z * 12f;
		var lead = target?.Controller?.Velocity.Flat() ?? Vector3.Zero;
		var side = (aim - mouth).Flat().SafeNormal().Cross( Vector3.Up );
		Sfx.Play( "ink_throw", mouth, 1f, 0.6f );
		Sfx.Play( "kraken_spit", mouth, 1f );
		for ( var i = 0; i < 3; i++ )
		{
			// One at where they are heading, one each side of it.
			var flight = MathX.Clamp( (aim - mouth).Flat().Length() / 13f, 0.8f, 1.7f ) + i * 0.12f;
			var at = aim + lead * flight * 0.7f + side * (i == 0 ? 0f : i == 1 ? 3.4f : -3.4f) + Rng.InSphere( 0.6f ).Flat();
			at.Y = GlobalPosition.Y + 0.2f;
			var velocity = (at - mouth) / flight - KrakenInk.Fall * 0.5f * flight;
			KrakenInk.Launch( this, mouth - GlobalBasis.Z * 0.6f, velocity );
			InkSpat++;
		}
		gore.GooBurst( mouth, -GlobalBasis.Z, KrakenInk.Ink, 10, 420f, 3f );
	}

	/// <summary> A glob of ink has landed: a splash, a stain, and anyone near it is inked. </summary>
	public void InkLanded( Vector3 at )
	{
		if ( gore is null ) return;
		for ( var i = 0; i < 4; i++ ) gore.GooBurst( at + Vector3.Up * 0.2f, Rng.OnSphere() + Vector3.Up, i % 2 == 0 ? KrakenInk.Ink : Green, 10, 360f, 1.6f );
		if ( gore.TraceWorld( at + Vector3.Up, at + Vector3.Down * 3f ) is { } floor )
		{
			gore.GooSplat( floor.Position, floor.Normal, 4.5f, KrakenInk.Ink );
			gore.GooSplat( floor.Position, floor.Normal, 2.4f, Green );
		}
		gore.Squelch( at, 1.2f, 0.8f );
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var body = player.WorldPosition + Vector3.Up * 0.9f;
			if ( body.DistanceTo( at ) > InkSplash ) continue;
			player.Health?.OnDamage( new DamageInfo { Damage = InkDamage, Attacker = this, Position = body, Origin = at } );
			player.Afflictions?.Cursed( PlayerAfflictions.Curse.Ink );
			player.Afflictions?.Irradiate( InkRadiationTime, poison: false );
			player.Afflictions?.Splattered( Green, 5 );
		}
	}
}

/// <summary> One glob of the Kraken's ink in flight: it arcs through the water trailing drips and bursts where it lands. </summary>
public partial class KrakenInk : Node3D
{
	/// <summary> How the water lets it fall (metres per second, per second). </summary>
	public static readonly Vector3 Fall = new( 0f, -7f, 0f );
	public static readonly Color Ink = new( 0.04f, 0.2f, 0.07f );

	Kraken owner;
	Vector3 velocity;
	float age;
	TimeSince sinceDrip;

	public static KrakenInk Launch( Kraken owner, Vector3 from, Vector3 velocity )
	{
		var glob = new KrakenInk { Name = "Kraken Ink", owner = owner, velocity = velocity };
		glob.AddToGroup( "kraken_ink" );
		owner.GetTree().CurrentScene.AddChild( glob );
		glob.GlobalPosition = from;
		// A dark glob with a glowing green heart.
		glob.AddChild( new MeshInstance3D { Mesh = Shapes.Sphere, Scale = Vector3.One * 1.1f, MaterialOverride = new StandardMaterial3D { AlbedoColor = Ink, Roughness = 0.2f, ClearcoatEnabled = true, Clearcoat = 1f }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		glob.AddChild( new MeshInstance3D { Mesh = Shapes.Sphere, Scale = Vector3.One * 0.55f, Position = new Vector3( 0.18f, 0.2f, 0f ), MaterialOverride = Shapes.Material( new Color( 0.3f, 1f, 0.35f ), glow: true ), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		glob.AddChild( new OmniLight3D { LightColor = new Color( 0.3f, 1f, 0.35f ), LightEnergy = 1.2f, OmniRange = 5f, ShadowEnabled = false } );
		return glob;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		age += dt;
		var from = GlobalPosition;
		velocity += Fall * dt;
		var to = from + velocity * dt;
		// It wobbles as it flies, stretched along its way.
		Scale = Vector3.One * (1f + MathF.Sin( age * 18f ) * 0.12f);
		if ( sinceDrip > 0.06f )
		{
			sinceDrip = 0f;
			GooDroplet.Spawn( from, velocity * 0.2f + Rng.InSphere( 1f ), Rng.Float() < 0.5f ? Ink : new Color( 0.3f, 1f, 0.35f ), Rng.Float( 1.5f, 3f ) );
		}
		var hit = Trace.Ray( this, from, to, Layers.World );
		var struck = hit.Hit;
		var at = struck ? hit.HitPosition : to;
		if ( !struck )
		{
			foreach ( var player in PlayerSurvivor.All )
				if ( player.IsAlive && (player.WorldPosition + Vector3.Up * 0.9f).DistanceTo( to ) < 1.3f ) { struck = true; break; }
		}
		if ( struck || age > 6f )
		{
			if ( owner is not null && IsInstanceValid( owner ) ) owner.InkLanded( at );
			QueueFree();
			return;
		}
		GlobalPosition = to;
	}
}
