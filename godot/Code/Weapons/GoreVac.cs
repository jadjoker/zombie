using Godot;

/// <summary>
/// The Gore Vac (slot 4, bought at its station). Everything it does is a push or a pull:
/// - Hold fire: suck. Gibs and goo puddles in the cone fly into the tank on top (Ammo, 0 to 100 gunk; ammo stations do
///   not fill it). Enemies are dragged toward you by weight (light ones slide in, heavy ones barely budge, bosses not at
///   all); a weak small one that reaches the nozzle is sucked apart. Limbs already shot half off are torn away. Starfish,
///   shells and urchins are torn off the floor, and a sea mine is torn off its chain and loaded. You drift toward what
///   you suck.
/// - Tap aim: spit a ball of gore (10 gunk). It bounces off the ground (twice, splatting), sticks to an enemy it hits
///   and bursts a moment later, hurting every enemy near. Hold aim until the ring fills: the whole tank as one huge ball
///   that bursts into four more on the ground. A loaded mine goes first, and blows up on what it hits.
/// - Hold reload: blow. Water blasts out of the nozzle, shoving gibs and enemies away and you back (aim down to hover).
/// - Every spit kicks you back (a full-tank spit at the floor is a rocket jump), and a full tank is heavy: you move a
///   little slower and the view sways with the slosh.
/// The tank and the balls take the colour of the goo that went in.
/// </summary>
public partial class PlayerWeapon
{
	/// <summary> This is the Gore Vac (fire sucks, aim spits, reload blows). </summary>
	public bool Vacuum;
	/// <summary> How far (m) and how wide (cosine of the half angle) it sucks. </summary>
	public const float VacRange = 7.5f, VacCone = 0.8f;
	/// <summary> Gunk a small ball costs; the least a big one takes; what the tank starts with. </summary>
	public const int BallCost = 10, BigBallMin = 30, StartGunk = 25;
	/// <summary> How wide a small ball's burst is (m). </summary>
	public const float BallRadius = 2.6f;
	/// <summary> The pull on an enemy is this over its weight (m/s): a crab about 3, a Rocky Shark under 2. </summary>
	public const float PullStrength = 380f;
	/// <summary> Enemies this light, this weak and this close to the nozzle are sucked apart. </summary>
	public const float SwallowWeight = 150f, SwallowHealth = 45f, SwallowReach = 2.1f;
	/// <summary> Dragged enemies stop this far from the nozzle (not in your face). </summary>
	public const float HoldOff = 1.8f;
	/// <summary> A limb at or under this share of its health is torn off by the suction after a short tug. </summary>
	public const float RipBelow = 0.5f, RipTug = 0.35f;
	/// <summary> How long the suction has to hold a mine before its chain snaps. </summary>
	public const float MineTug = 0.6f;
	/// <summary> How much slower a full tank makes you. </summary>
	public const float FullTankSlow = 0.22f;

	public bool Sucking { get; private set; }
	public bool Blowing { get; private set; }
	/// <summary> A sea mine is loaded: the next spit fires it. </summary>
	public bool HasMine => heldMine is not null && IsInstanceValid( heldMine );
	/// <summary> The colour of what is in the tank (a running mix of the goo sucked up). </summary>
	public Color TankColour { get; private set; } = GoreVacModel.Gunk;
	/// <summary> For tests: hold the vacuum (or the blow) on without the mouse. </summary>
	public bool ForceSuck, ForceBlow;
	/// <summary> For tests: what it has taken in since the start. </summary>
	public static int Swallowed, Slurped, EnemiesSwallowed, LimbsRipped, PropsTorn, MinesTorn;

	public static PlayerWeapon GoreVac() => new()
	{
		Name = "GoreVac", DisplayName = "Gore Vac", Slot = 4, ModelName = "gorevac", Vacuum = true,
		Damage = 120f, FireRate = 2.5f, MagazineSize = 100, StartingReserve = 0, Automatic = false, ChargeTime = 0.7f,
		Recoil = 2.6f, ShakePerShot = 0.4f, FovKickPerShot = 3f, MuzzleFlashScale = 0f,
	};

	TimeSince sinceFullWarning = 9f, sinceStreak, sinceGulp = 9f;
	AudioStreamPlayer suckSound, blowSound;
	Node3D gunkFill, fan;
	StandardMaterial3D gunkMaterial;
	float shownFill = -1f, fanSpeed, swayTime;
	Vector2 lastSway;
	readonly Dictionary<Node, float> tugs = new();
	Node3D heldMine, incomingMine;
	float incomingSpeed;

	Vector3 Nozzle()
	{
		var camera = controller.Camera;
		if ( ViewModel is not null && ViewModel.Visible ) return ViewModel.Muzzle().Origin;
		return camera.GlobalPosition - camera.GlobalBasis.Z * 0.7f - camera.GlobalBasis.Y * 0.2f + camera.GlobalBasis.X * 0.15f;
	}

	static bool InCone( Vector3 from, Vector3 forward, Vector3 at, float range, float cos, out float distance )
	{
		var to = at - from;
		distance = to.Length();
		return distance <= range && distance > 0.01f && forward.Dot( to / distance ) >= cos;
	}

	void UpdateVacuum( float dt, bool allowed )
	{
		var camera = controller.Camera;
		if ( camera is null ) return;
		var from = camera.GlobalPosition;
		var forward = -camera.GlobalBasis.Z;
		var nozzle = Nozzle();

		var wantSuck = allowed && (Input.IsActionPressed( "attack1" ) || ForceSuck);
		var wantBlow = allowed && !wantSuck && (Input.IsActionPressed( "reload" ) || ForceBlow);
		Sucking = wantSuck;
		Blowing = wantBlow;
		if ( wantSuck && Ammo >= MagazineSize && sinceFullWarning > 1.2f )
		{
			sinceFullWarning = 0f;
			Sfx.Play2D( "deny", 0.35f, 1.3f );
		}
		var touched = new HashSet<Node>();
		if ( Sucking ) Suck( dt, from, forward, nozzle, touched );
		if ( Blowing ) Blow( dt, from, forward, nozzle );
		foreach ( var key in tugs.Keys.Where( k => !touched.Contains( k ) ).ToList() ) tugs.Remove( key );
		UpdateIncomingMine( dt );
		UpdateLoopSounds();
		UpdateWeight( dt );

		// The spit: tap aim for a ball; hold it until the ring fills (and there is enough in the tank) for a huge one.
		if ( allowed )
		{
			if ( Input.IsActionJustPressed( "attack2" ) )
			{
				if ( (Ammo >= BallCost || HasMine) && TimeSinceShot >= 1f / CurrentFireRate ) { charging = true; sinceChargeStart = 0f; }
				else if ( Ammo < BallCost ) Sfx.Play2D( "dry_fire" );
			}
			if ( charging && (Ammo < BigBallMin || HasMine) ) sinceChargeStart = 0f;   // not enough for a big one (or a mine goes first): the ring stays empty
			if ( charging && !Input.IsActionPressed( "attack2" ) )
			{
				charging = false;
				Spit( ChargeAmount >= 1f && Ammo >= BigBallMin );
			}
		}
		else charging = false;
		UpdateTankModel( dt );
	}

	/// <summary> Pulls in gibs, puddles, enemies, loose limbs, floor props and mines in the cone. </summary>
	void Suck( float dt, Vector3 from, Vector3 forward, Vector3 nozzle, HashSet<Node> touched )
	{
		var gore = GoreSystem.Get( controller );
		var pull = 1f - MathF.Exp( -7f * dt );
		var full = Ammo >= MagazineSize;
		if ( !full )
		{
			foreach ( var gib in gore.Gibs.ToList() )
			{
				if ( !InCone( from, forward, gib.GlobalPosition, VacRange, VacCone, out _ ) ) continue;
				var toNozzle = nozzle - gib.GlobalPosition;
				var d = toNozzle.Length();
				if ( d < 0.6f ) { Swallow( gib ); continue; }
				var speed = MathX.Lerp( 16f, 5f, MathX.Clamp( d / VacRange, 0f, 1f ) );
				gib.Sleeping = false;
				gib.LinearVelocity = gib.LinearVelocity.Lerp( toNozzle / d * speed, pull );
				gib.AngularVelocity += Rng.InSphere( 20f ) * dt;
			}
			// Puddles on the floor shrink as they are slurped up; the ones gone fly up the nozzle as goo.
			foreach ( var (at, colour) in gore.Slurp( from, forward, VacRange * 0.85f, VacCone - 0.1f, dt ) )
			{
				Slurped++;
				AddGunk( 2f, colour );
				for ( var i = 0; i < 3; i++ ) VacStreak.Fly( GetTree().CurrentScene, at + Rng.InSphere( 0.15f ), Nozzle, colour with { A = 1f }, 0.05f );
			}
		}

		// Enemies: dragged in by weight; a light, weak one at the nozzle is sucked apart (its gibs come straight in).
		foreach ( var creature in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( creature ) || !creature.IsEnemy || creature.IsDead || !creature.Pullable ) continue;
			var centre = creature.GlobalPosition + Vector3.Up * 0.3f;
			if ( !InCone( from, forward, centre, VacRange + 1.5f, VacCone - 0.05f, out var distance ) ) continue;
			var toNozzle = nozzle - centre;
			var d = toNozzle.Length();
			if ( d < SwallowReach && creature.Weight <= SwallowWeight && creature.Health.Current <= SwallowHealth )
			{
				EnemiesSwallowed++;
				var shooter = controller;
				PlayerPerks.Hurt( creature, 9999f, centre, nozzle, shooter );
				AddGunk( 12f, TankColour );
				Sfx.Play2D( "gib", 0.9f, 0.8f, Sfx.GunBus );
				juice?.Shake( 0.25f );
				Controls.Rumble( 0.5f );
				continue;
			}
			if ( d < HoldOff ) continue;   // close enough: held in front of the nozzle
			var strength = PullStrength / MathF.Max( 30f, creature.Weight ) * MathX.Lerp( 1f, 0.5f, MathX.Clamp( distance / (VacRange + 1.5f), 0f, 1f ) );
			creature.Pull( toNozzle / d * MathF.Min( strength * dt, d - HoldOff ) );

			// Limbs shot half off are torn away after a short tug.
			if ( creature is PartCreature body && d < VacRange * 0.8f )
				foreach ( var limb in body.Limbs() )
				{
					if ( limb.Health > limb.MaxHealth * RipBelow ) continue;
					touched.Add( limb.Go );
					tugs[limb.Go] = tugs.GetValueOrDefault( limb.Go ) + dt;
					if ( tugs[limb.Go] < RipTug ) continue;
					tugs.Remove( limb.Go );
					LimbsRipped++;
					var at = limb.Go.GlobalPosition;
					body.ReceiveShot( limb.Index, limb.Health + 1f, (nozzle - at).SafeNormal(), at, nozzle, controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ), true );
					MarkHit();
					Sfx.Play( "gib", at, 1f, 0.75f );
				}
		}

		// The small things on the floor: torn up and sucked in like gibs (stone and shell: hardly worth a thing).
		foreach ( var prop in GetTree().GetNodesInGroup( SeaDressing.LooseGroup ).OfType<Node3D>().ToList() )
		{
			if ( !IsInstanceValid( prop ) || !InCone( from, forward, prop.GlobalPosition, VacRange * 0.75f, VacCone, out _ ) ) continue;
			touched.Add( prop );
			tugs[prop] = tugs.GetValueOrDefault( prop ) + dt;
			if ( tugs[prop] < 0.25f ) continue;
			tugs.Remove( prop );
			prop.RemoveFromGroup( SeaDressing.LooseGroup );
			foreach ( var solid in prop.FindChildren( "*", "StaticBody3D", true, false ) ) solid.QueueFree();
			foreach ( var sway in prop.FindChildren( "*", "", true, false ).OfType<Sway>() ) sway.QueueFree();
			var loose = Gib.Attach( prop, gore, 3f, 5f, 0.4f, ((nozzle - prop.GlobalPosition).SafeNormal() + Vector3.Up) * 3f, Colors.Gray );
			loose.MakeDry();
			loose.Recycled = true;
			loose.Stuff = GibKind.Shell;
			PropsTorn++;
		}

		// A sea mine: held a moment, its chain snaps, and it flies into the nozzle (one at a time).
		if ( !HasMine && incomingMine is null )
			foreach ( var mine in SeaMine.All.ToList() )
			{
				if ( mine.Gone || !InCone( from, forward, mine.BallPosition, VacRange, VacCone - 0.05f, out _ ) ) continue;
				touched.Add( mine );
				tugs[mine] = tugs.GetValueOrDefault( mine ) + dt;
				if ( tugs[mine] < MineTug ) continue;
				tugs.Remove( mine );
				incomingMine = mine.TearLoose();
				incomingSpeed = 2f;
				MinesTorn++;
				break;
			}

		// The draw of the water itself: faint streaks rushing into the nozzle from the cone.
		if ( sinceStreak > 0.035f )
		{
			sinceStreak = 0f;
			var dir = (forward + Rng.InSphere( 1f ) * 0.55f).Normalized();
			VacStreak.Fly( GetTree().CurrentScene, from + dir * Rng.Float( 2.5f, 6f ), Nozzle, new Color( 0.85f, 0.97f, 1f, 0.35f ), 0.03f );
		}
		// The suction drags you a little toward what you point it at.
		if ( controller.Velocity.Dot( forward ) < 2.2f ) controller.Velocity += forward * 3f * dt;
		juice?.Shake( 0.012f );
	}

	/// <summary> The torn-off mine flying up the nozzle; loaded when it gets there. </summary>
	void UpdateIncomingMine( float dt )
	{
		if ( incomingMine is null ) return;
		if ( !IsInstanceValid( incomingMine ) ) { incomingMine = null; return; }
		var to = Nozzle() - incomingMine.GlobalPosition;
		var d = to.Length();
		incomingSpeed += dt * 18f;
		if ( d < 0.5f )
		{
			heldMine = incomingMine;
			incomingMine = null;
			heldMine.Visible = false;
			Sfx.Play2D( "mine_tink", 0.9f, 0.8f, Sfx.GunBus );
			Sfx.Play2D( "mag_in", 0.7f );
			Controls.Rumble( 0.4f );
			return;
		}
		incomingMine.GlobalPosition += to / d * MathF.Min( d, incomingSpeed * dt );
		incomingMine.RotateY( dt * 6f );
	}

	/// <summary> Reload held: water blasts out, shoving gibs and enemies away and the player back. </summary>
	void Blow( float dt, Vector3 from, Vector3 forward, Vector3 nozzle )
	{
		foreach ( var gib in GoreSystem.Get( controller ).Gibs.ToList() )
		{
			if ( !InCone( from, forward, gib.GlobalPosition, VacRange, VacCone - 0.1f, out var d ) ) continue;
			gib.Sleeping = false;
			gib.LinearVelocity += (gib.GlobalPosition - from).SafeNormal() * 30f * (1f - d / VacRange) * dt;
		}
		foreach ( var creature in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( creature ) || !creature.IsEnemy || creature.IsDead || !creature.Pullable ) continue;
			var centre = creature.GlobalPosition + Vector3.Up * 0.3f;
			if ( !InCone( from, forward, centre, VacRange, VacCone - 0.1f, out var d ) ) continue;
			creature.Pull( (centre - from).SafeNormal() * PullStrength * 1.3f / MathF.Max( 30f, creature.Weight ) * (1f - d / VacRange * 0.5f) * dt );
		}
		// The thrust: back from where it points, strongest pointing down (hover).
		var thrust = 13f + MathF.Max( 0f, -forward.Y ) * 9f;
		if ( controller.Velocity.Dot( -forward ) < 7f ) controller.Velocity -= forward * thrust * dt;
		if ( sinceStreak > 0.03f )
		{
			sinceStreak = 0f;
			var dir = (forward + Rng.InSphere( 1f ) * 0.35f).Normalized();
			var end = nozzle + dir * Rng.Float( 3f, 6f );
			VacStreak.Fly( GetTree().CurrentScene, nozzle, () => end, new Color( 0.85f, 0.97f, 1f, 0.45f ), 0.035f );
		}
		juice?.Shake( 0.02f );
	}

	/// <summary> A full tank is heavy: a little slower, and the view sways with the slosh as you move. </summary>
	void UpdateWeight( float dt )
	{
		var fill = Ammo / (float)MagazineSize;
		var baseWalk = movement?.MoveSpeed ?? controller.WalkSpeed;
		var baseRun = movement?.SprintSpeed ?? controller.RunSpeed;
		var scale = 1f - FullTankSlow * fill;
		controller.WalkSpeed = baseWalk * scale;
		controller.RunSpeed = baseRun * scale;
		var moving = MathX.Clamp( controller.Velocity.Flat().Length() / 4f, 0f, 1f );
		swayTime += dt * (1f + moving * 2f);
		var sway = new Vector2( MathF.Sin( swayTime * 2.3f ), MathF.Sin( swayTime * 1.7f + 1f ) ) * 0.6f * fill * moving;
		var change = sway - lastSway;
		lastSway = sway;
		controller.EyeAngles += new Angles( change.Y * 0.5f, change.X, 0f );
	}

	void Swallow( Gib gib )
	{
		var colour = gib.Goo ?? new Color( 0.6f, 0.6f, 0.62f );
		var value = gib.Recycled ? 1f : MathX.Clamp( gib.Radius / U.In * 1.6f, 3f, 14f );
		if ( gib.Goo is not null ) AddGunk( value, colour );
		else Ammo = Math.Min( MagazineSize, Ammo + 1 );
		Swallowed++;
		gib.QueueFree();
		if ( sinceGulp > 0.07f )
		{
			sinceGulp = 0f;
			Sfx.Play2D( gib.Goo is null ? "mine_tink" : "splat", 0.45f, Rng.Float( 1.3f, 1.6f ), Sfx.GunBus );
			Controls.Rumble( 0.12f );
		}
	}

	void AddGunk( float amount, Color colour )
	{
		Ammo = Math.Min( MagazineSize, Ammo + (int)MathF.Ceiling( amount ) );
		// Mixed, but kept rich: green, pink and blue goo would average to a dull grey otherwise.
		var mix = TankColour.Lerp( colour with { A = 1f }, MathX.Clamp( amount / 12f, 0.05f, 0.5f ) );
		TankColour = Color.FromHsv( mix.H, MathF.Max( mix.S, 0.75f ), MathF.Max( mix.V, 0.85f ) );
	}

	/// <summary> Testing aid: spits a ball (or the huge one) as if aim was tapped (or held). </summary>
	public void TestSpit( bool big ) => Spit( big );

	void Spit( bool big )
	{
		var mine = HasMine;
		if ( !mine && Ammo < BallCost ) return;
		var cost = mine ? 0 : big ? Ammo : BallCost;
		Ammo -= cost;
		TimeSinceShot = 0f;
		var camera = controller.Camera;
		var forward = -camera.GlobalBasis.Z;
		var nozzle = Nozzle();
		// A big ball: damage and size grow with the gunk in it (the whole tank is about six small balls' worth).
		var scale = big ? cost / (float)BallCost : 1f;
		var damage = mine ? GoreBall.MineDamage : CurrentDamage * (big ? 0.6f * scale : 1f);
		var radius = mine ? GoreBall.MineRadius : big ? BallRadius * MathF.Min( 2.2f, 0.75f + scale * 0.15f ) : BallRadius;
		var size = big ? MathF.Min( 0.55f, 0.16f + scale * 0.04f ) : 0.14f;
		// Aim at what is under the crosshair: the ball flies from the nozzle toward it.
		var exclude = new Godot.Collections.Array<Rid> { controller.GetRid() };
		var aim = Trace.Ray( controller, camera.GlobalPosition, camera.GlobalPosition + forward * 60f, Layers.World | Layers.Creature, exclude );
		var target = aim.Hit ? aim.HitPosition : camera.GlobalPosition + forward * 60f;
		var speed = mine ? 18f : big ? 17f : 24f;
		var direction = (target - nozzle).SafeNormal();
		var kind = mine ? GoreBall.Kind.Mine : big ? GoreBall.Kind.Big : GoreBall.Kind.Small;
		var ball = GoreBall.Launch( GetTree().CurrentScene, nozzle, direction * speed + Vector3.Up * 1f, damage, radius, size, TankColour, controller, this, kind );
		if ( mine )
		{
			heldMine.Reparent( ball, false );
			heldMine.Position = Vector3.Zero;
			heldMine.Visible = true;
			heldMine = null;
		}
		Sfx.Play2D( mine ? "missile_launch" : "kraken_spit", big || mine ? 1f : 0.8f, big ? 0.75f : 1.25f, Sfx.GunBus );
		Controls.Rumble( big || mine ? 0.7f : 0.3f );
		ViewModel?.Fire();
		juice?.Shake( ShakePerShot * (big ? 2.5f : 1f) );
		juice?.KickFov( FovKickPerShot * (big ? 2f : 1f) );
		var recoil = Recoil * (big ? 2.5f : 1f);
		controller.EyeAngles += new Angles( -recoil, Rng.Float( -recoil, recoil ) * 0.3f, 0f );
		// The kick: a spit shoves you back the other way (a full-tank spit at the floor is a rocket jump).
		var kick = mine ? 6f : big ? 3f + scale * 0.9f : 2.5f;
		controller.Velocity -= direction * kick;
		LastKick = kick;
	}

	/// <summary> For tests: how hard the last spit kicked the player back (m/s). </summary>
	public float LastKick { get; private set; }

	/// <summary> The tank's goo level and colour, and the fan in the nozzle (spinning up while it sucks, back while it blows). </summary>
	void UpdateTankModel( float dt )
	{
		if ( ViewModel is null ) return;
		if ( gunkFill is null || !IsInstanceValid( gunkFill ) )
		{
			gunkFill = ViewModel.FindChild( "GunkFill", true, false ) as Node3D;
			fan = ViewModel.FindChild( "Fan", true, false ) as Node3D;
			gunkMaterial = (gunkFill?.FindChild( "Gunk", true, false ) as MeshInstance3D)?.MaterialOverride as StandardMaterial3D;
			shownFill = -1f;
		}
		if ( gunkFill is null ) return;
		var want = Ammo / (float)MagazineSize;
		shownFill = shownFill < 0f ? want : MathX.Lerp( shownFill, want, 1f - MathF.Exp( -8f * dt ) );
		var slosh = Sucking || Blowing ? MathF.Sin( (float)Clock.Now * 30f ) * 0.02f : lastSway.X * 0.03f;
		gunkFill.Scale = new Vector3( 1f, 1f, MathF.Max( 0.02f, shownFill + slosh ) );
		gunkFill.Visible = shownFill > 0.01f;
		if ( gunkMaterial is not null )
		{
			gunkMaterial.AlbedoColor = TankColour;
			gunkMaterial.Emission = TankColour;
		}
		fanSpeed = MathX.Lerp( fanSpeed, Sucking ? 40f : Blowing ? -40f : charging ? 12f : 0f, 1f - MathF.Exp( -4f * dt ) );
		if ( fan is not null && IsInstanceValid( fan ) ) fan.RotateObjectLocal( Vector3.Right, fanSpeed * dt );
	}

	/// <summary> The roar of the suction (the Kraken's suck, pitched up) and the gush of the blow, looped while held. </summary>
	void UpdateLoopSounds()
	{
		suckSound = Loop( suckSound, "Suck Sound", "kraken_suck", 1.6f, -9f, Sucking );
		blowSound = Loop( blowSound, "Blow Sound", "bubbles", 0.8f, -6f, Blowing );
	}

	AudioStreamPlayer Loop( AudioStreamPlayer player, string name, string sound, float pitch, float volume, bool on )
	{
		if ( player is null )
		{
			if ( !on ) return null;
			player = new AudioStreamPlayer { Name = name, Stream = GD.Load<AudioStream>( $"res://assets/sounds/{sound}.wav" ), Bus = Sfx.GunBus, PitchScale = pitch, VolumeDb = volume };
			AddChild( player );
			var p = player;
			p.Finished += () => { if ( p.GetMeta( "on", false ).AsBool() ) p.Play( 0.15f ); };
		}
		player.SetMeta( "on", on );
		if ( on && !player.Playing ) player.Play();
		else if ( !on && player.Playing ) player.Stop();
		return player;
	}
}

/// <summary> A little blob of goo (or a fleck of water) rushing into the Gore Vac's nozzle, gone when it gets there. </summary>
public partial class VacStreak : MeshInstance3D
{
	static readonly SphereMesh Ball = new() { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 };
	Func<Vector3> target;
	float speed = 2f;
	TimeSince age;

	public static void Fly( Node parent, Vector3 from, Func<Vector3> target, Color colour, float size )
	{
		var streak = new VacStreak
		{
			Name = "Vac Streak", Mesh = Ball, target = target, CastShadow = ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = colour.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
			},
		};
		parent.AddChild( streak );
		streak.age = 0f;
		streak.GlobalPosition = from;
		streak.Scale = Vector3.One * size;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var to = target() - GlobalPosition;
		var d = to.Length();
		speed += dt * 40f;
		if ( d < 0.12f || age > 1.2f ) { QueueFree(); return; }
		var step = MathF.Min( d, speed * dt );
		GlobalPosition += to / d * step;
		// Stretched along the way it flies, thinner the faster it goes.
		var dir = to / d;
		var up = MathF.Abs( dir.Dot( Vector3.Up ) ) > 0.95f ? Vector3.Right : Vector3.Up;
		var size = Scale.X == 0f ? 0.03f : MathF.Min( Scale.X, Scale.Y );
		Basis = Basis.LookingAt( dir, up ).Scaled( new Vector3( size, size, size * MathF.Min( 4f, 1f + speed * 0.2f ) ) );
	}
}

/// <summary>
/// A ball of gore spat by the Gore Vac, flown by its own simple physics: it drops a little, bounces off the ground and
/// walls (a small one twice, splatting goo each time, then bursts once it has slowed), and sticks to an enemy it hits,
/// bursting a moment later. A big one bursts on the ground into four small ones thrown outward. A loaded sea mine flies
/// the same way and blows up on the first thing it touches. Bursting hurts every enemy near, sprays goo, and flings a
/// few lumps (which can be sucked up again, for a little).
/// </summary>
public partial class GoreBall : Node3D
{
	public enum Kind { Small, Big, Mine }
	public const float MineDamage = 450f, MineRadius = 4f;
	/// <summary> How long a stuck ball holds on before it bursts (s). </summary>
	public const float StickFuse = 0.55f;
	const float Gravity = 6f, Bounce = 0.55f;

	Vector3 velocity;
	float damage, radius, size;
	Color colour;
	Kind kind;
	PlayerController shooter;
	PlayerWeapon weapon;
	TimeSince age, sinceDrip;
	MeshInstance3D blob;
	int bounces;
	Node3D stuckTo;
	Vector3 stuckAt;
	float fuse = -1f;
	public static int Bursts, Hits, Bounces, Sticks, Splits, MineBlasts;
	/// <summary> For tests: where the last one burst, and on what. </summary>
	public static Vector3 LastBurst;
	public static string LastHitName = "";

	public static GoreBall Launch( Node parent, Vector3 from, Vector3 velocity, float damage, float radius, float size, Color colour, PlayerController shooter, PlayerWeapon weapon, Kind kind = Kind.Small )
	{
		var ball = new GoreBall { Name = kind == Kind.Mine ? "Spat Mine" : "Gore Ball", velocity = velocity, damage = damage, radius = radius, size = size, colour = colour, shooter = shooter, weapon = weapon, kind = kind };
		parent.AddChild( ball );
		ball.age = 0f;
		ball.sinceDrip = 0f;
		ball.GlobalPosition = from;
		if ( kind != Kind.Mine )
		{
			ball.blob = new MeshInstance3D { Name = "Blob", Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 }, MaterialOverride = GoreVacModel.GunkMaterial( colour ) };
			ball.AddChild( ball.blob );
			ball.blob.Scale = Vector3.One * size;
			// A few lumps stuck to it.
			for ( var i = 0; i < 4; i++ )
			{
				var lump = new MeshInstance3D { Mesh = ball.blob.Mesh, MaterialOverride = GoreVacModel.GunkMaterial( colour.Darkened( 0.25f ) ) };
				ball.blob.AddChild( lump );
				lump.Position = Rng.OnSphere() * 0.75f;
				lump.Scale = Vector3.One * Rng.Float( 0.3f, 0.5f );
			}
		}
		else ball.size = SeaMine.Radius * 0.8f;
		ball.AddChild( new OmniLight3D { LightColor = kind == Kind.Mine ? new Color( 0.4f, 1f, 0.5f ) : colour, LightEnergy = 0.6f, OmniRange = 1.5f + ball.size * 4f, ShadowEnabled = false } );
		return ball;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( stuckTo is not null ) { Stuck( dt ); return; }
		var from = GlobalPosition;
		velocity += Vector3.Down * Gravity * dt;
		var to = from + velocity * dt;
		var exclude = shooter is not null && IsInstanceValid( shooter ) ? new Godot.Collections.Array<Rid> { shooter.GetRid() } : null;
		var hit = Trace.Ray( this, from, to + velocity.SafeNormal() * size, Layers.World | Layers.Creature, exclude );
		if ( age > 6f ) { Burst( to, -velocity.SafeNormal() ); return; }
		if ( hit.Hit )
		{
			LastHitName = $"{hit.Node?.Name} ({hit.Node?.GetType().Name})";
			var at = hit.HitPosition - velocity.SafeNormal() * MathF.Min( size, 0.1f );
			var creature = Shootables.Find( hit.Node )?.ShotOwner is Creature c && !c.IsDead ? c : null;
			if ( kind == Kind.Mine ) { Burst( at, hit.Normal ); return; }
			if ( creature is not null && hit.Node is Node3D part )
			{
				// Splat onto the enemy and hold on, wobbling, then burst (a big one goes almost at once).
				Sticks++;
				stuckTo = part;
				stuckAt = part.ToLocal( at );
				fuse = kind == Kind.Big ? 0.2f : StickFuse;
				GoreSystem.Get( this ).Squelch( at, 1f, 1.2f );
				GoreSystem.Get( this ).GooBurst( at, hit.Normal, colour, 6, 160f, 0.8f );
				return;
			}
			if ( kind == Kind.Big ) { Split( at, hit.Normal ); return; }
			var speed = velocity.Length();
			if ( bounces < 2 && speed > 5f )
			{
				// Bounce: off the surface, losing half its speed, leaving a splat.
				bounces++;
				Bounces++;
				velocity = velocity.Bounce( hit.Normal ) * Bounce;
				GlobalPosition = at + hit.Normal * 0.02f;
				var gore = GoreSystem.Get( this );
				gore.GooSplat( hit.HitPosition, hit.Normal, 0.7f, colour );
				gore.Squelch( hit.HitPosition, 0.6f, 1.3f );
				blob.Scale = new Vector3( 1.4f, 0.6f, 1.4f ) * size;   // squashed on the bounce
				return;
			}
			Burst( at, hit.Normal );
			return;
		}
		GlobalPosition = to;
		if ( kind == Kind.Mine ) { RotateObjectLocal( Vector3.Right, dt * 5f ); return; }
		// It wobbles like jelly as it flies, and springs back from a bounce's squash.
		var t = (float)age * 18f;
		var wobble = new Vector3( 1f + MathF.Sin( t ) * 0.12f, 1f + MathF.Sin( t + 2f ) * 0.12f, 1f + MathF.Sin( t + 4f ) * 0.12f ) * size;
		blob.Scale = blob.Scale.Lerp( wobble, 1f - MathF.Exp( -10f * dt ) );
		if ( sinceDrip > 0.06f )
		{
			sinceDrip = 0f;
			GooDroplet.Spawn( GlobalPosition, velocity * 0.2f + Rng.InSphere( 1f ), colour, 0.6f + size * 2f );
		}
	}

	/// <summary> Held on an enemy: rides along with it, pulsing, until the fuse runs out (or what it held is gone). </summary>
	void Stuck( float dt )
	{
		if ( !IsInstanceValid( stuckTo ) || !stuckTo.IsInsideTree() ) { Burst( GlobalPosition, Vector3.Up ); return; }
		GlobalPosition = stuckTo.ToGlobal( stuckAt );
		fuse -= dt;
		var pulse = 1f + MathF.Sin( (float)age * 40f ) * 0.18f * (1f - MathF.Max( 0f, fuse ) / StickFuse);
		if ( blob is not null ) blob.Scale = Vector3.One * size * pulse;
		if ( fuse <= 0f ) Burst( GlobalPosition, (GlobalPosition - stuckTo.GlobalPosition).SafeNormal() );
	}

	/// <summary> A big ball hitting the ground: one burst where it lands, and four small balls thrown out round it. </summary>
	void Split( Vector3 at, Vector3 normal )
	{
		Splits++;
		var side = normal.Cross( MathF.Abs( normal.Y ) > 0.9f ? Vector3.Right : Vector3.Up ).Normalized();
		var other = normal.Cross( side );
		for ( var i = 0; i < 4; i++ )
		{
			var a = i * MathF.PI * 0.5f + Rng.Float( -0.3f, 0.3f );
			var dir = (side * MathF.Cos( a ) + other * MathF.Sin( a ) + normal * 1.1f).Normalized();
			Launch( GetParent(), at + normal * 0.25f, dir * Rng.Float( 7f, 10f ), damage * 0.2f, PlayerWeapon.BallRadius, 0.12f, colour, shooter, weapon );
		}
		damage *= 0.6f;
		Burst( at, normal );
	}

	void Burst( Vector3 at, Vector3 normal )
	{
		Bursts++;
		LastBurst = at;
		var gore = GoreSystem.Get( this );
		var who = shooter is not null && IsInstanceValid( shooter ) ? shooter : null;
		var hurt = PlayerPerks.HurtCreatures( at, radius, damage, who );
		Hits += hurt;
		if ( hurt > 0 && weapon is not null && IsInstanceValid( weapon ) ) weapon.MarkHit();
		if ( kind == Kind.Mine )
		{
			// The mine goes off: the blast, and everything near is shoved away (the player too, if too close).
			MineBlasts++;
			AlienBlast.Spawn( GetTree().CurrentScene, at, 2.2f );
			Sfx.Play( "missile_boom", at, 1f );
			foreach ( var creature in Creature.Everyone.ToList() )
				if ( IsInstanceValid( creature ) && creature.IsEnemy && !creature.IsDead && creature.Pullable && creature.GlobalPosition.DistanceTo( at ) < radius )
					creature.Pull( (creature.GlobalPosition - at).Flat().SafeNormal() * 1.2f );
			foreach ( var gib in gore.Gibs.ToList() )
				if ( gib.GlobalPosition.DistanceTo( at ) < radius * 1.5f ) gib.LinearVelocity += (gib.GlobalPosition - at).SafeNormal() * 12f;
			if ( who is not null && who.GlobalPosition.DistanceTo( at ) < radius )
				who.Velocity += ((who.GlobalPosition - at).Flat().SafeNormal() + Vector3.Up * 0.5f) * 7f;
			if ( who?.GetNodeOrNull<CameraJuice>( "CameraJuice" ) is { } boom && who.GlobalPosition.DistanceTo( at ) < radius * 4f ) boom.Shake( 0.8f );
			QueueFree();
			return;
		}
		var k = size / 0.14f;
		gore.GooBurst( at, normal, colour, (int)(16 * MathF.Min( 3f, k )), 320f * MathF.Min( 2f, k ), 1.2f * MathF.Min( 2.5f, k ) );
		gore.GooSpray( at, normal, (int)(6 * MathF.Min( 3f, k )), colour, 140f * radius / PlayerWeapon.BallRadius, 0.6f, 1.5f * MathF.Min( 2f, k ) );
		gore.GooSpray( at, Vector3.Down, 3, colour, 120f, 1f, 1.6f * MathF.Min( 2f, k ) );
		for ( var i = 0; i < Math.Min( 6, 1 + (int)k ); i++ )
		{
			var lump = gore.ThrowBlob( at + normal * 0.1f, normal, colour.Darkened( 0.2f ), colour, Rng.Float( 1.5f, 2.6f ) );
			if ( lump is not null ) lump.Recycled = true;
		}
		// Gibs near the burst are blown outward.
		foreach ( var gib in gore.Gibs.ToList() )
			if ( !gib.Recycled && gib.GlobalPosition.DistanceTo( at ) < radius ) gib.LinearVelocity += (gib.GlobalPosition - at).SafeNormal() * 6f;
		gore.Squelch( at, 1.5f, k > 2f ? 0.7f : 1f );
		Sfx.Play( k > 2f ? "kraken_burst" : "splat", at, k > 2f ? 0.9f : 1f, k > 2f ? 1.3f : 0.85f );
		// A big one shakes the view if you are close.
		if ( who?.GetNodeOrNull<CameraJuice>( "CameraJuice" ) is { } juice && who.GlobalPosition.DistanceTo( at ) < radius * 3f )
			juice.Shake( 0.3f * MathF.Min( 3f, k ) );
		QueueFree();
	}
}
