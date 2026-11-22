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
	/// <summary> How far (m: no real limit) and how wide (cosine of the half angle) it sucks. </summary>
	public const float VacRange = 1000f, VacCone = 0.8f;
	/// <summary> The pull weakens out to this distance (m), then stays at half strength however far away. </summary>
	public const float FalloffReach = 30f;
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
	/// <summary> How long the suction has to hold a mine before its chain snaps, and how close it has to be (m). </summary>
	public const float MineTug = 0.6f, MineReach = 6f;
	/// <summary>
	/// What things are worth in the tank (100 is full): a gib about 0.4 to 1.2 by its size, a lump the gun threw itself
	/// 0.1, a puddle 0.25, a stone or shell 0.1, an enemy sucked apart 3. It takes a great many pieces to fill.
	/// </summary>
	public const float GibGunkMin = 0.4f, GibGunkMax = 1.2f, RecycledGunk = 0.1f, PuddleGunk = 0.25f, DryGunk = 0.1f, EnemyGunk = 3f;
	/// <summary> The tank is full: it beeps this often while sucking, and what goes in is blown out the back. </summary>
	const float FullBeepEvery = 0.8f;
	/// <summary> A caught piece shrinks over the last this many metres to the nozzle, and is swallowed this close. </summary>
	const float ShrinkReach = 2.2f, SwallowAt = 0.3f;
	/// <summary> How much slower a full tank makes you. </summary>
	public const float FullTankSlow = 0.22f;

	public bool Sucking { get; private set; }
	public bool Blowing { get; private set; }
	/// <summary> A sea mine is loaded: the next spit fires it. </summary>
	public bool HasMine => heldMine is not null && IsInstanceValid( heldMine );
	/// <summary> The colour of what is in the tank (a running mix of the goo sucked up). </summary>
	public Color TankColour { get; private set; } = GoreVacModel.Gunk;

	/// <summary> Takes gunk out of the tank for something else (Vac-Bot's blink). False if there isn't enough. </summary>
	public bool SpendGunk( int amount )
	{
		if ( !Vacuum || Ammo < amount ) return false;
		Ammo -= amount;
		return true;
	}
	/// <summary> For tests: hold the vacuum (or the blow) on without the mouse. </summary>
	public bool ForceSuck, ForceBlow;
	/// <summary> For tests: what it has taken in since the start. </summary>
	public static int Swallowed, Slurped, EnemiesSwallowed, LimbsRipped, PropsTorn, MinesTorn;

	public static PlayerWeapon GoreVac( int slot = 4 ) => new()
	{
		Name = "GoreVac", DisplayName = "Gore Vac", Slot = slot, ModelName = "gorevac", Vacuum = true,
		Damage = 120f, FireRate = 3.5f, MagazineSize = 100, StartingReserve = 0, Automatic = false, ChargeTime = 0.7f,
		Recoil = 2.6f, ShakePerShot = 0.4f, FovKickPerShot = 3f, MuzzleFlashScale = 0f,
	};

	TimeSince sinceFullWarning = 9f, sinceStreak, sinceGulp = 9f, sinceTrigger;
	/// <summary> Since the last piece went down the nozzle (the HUD's counter pops on it). </summary>
	public TimeSince SinceGulp { get; private set; } = 9f;
	readonly Dictionary<Gib, float> caught = new();
	// Reused every frame (the suck loop allocated fresh lists and sets each frame).
	readonly HashSet<Node> touchedBuffer = new();
	readonly List<Node> staleBuffer = new();
	readonly List<Gib> caughtBuffer = new();
	readonly List<Creature> creatureBuffer = new();
	readonly List<Node3D> propBuffer = new();
	TimeSince sinceProps = 9f;
	float gunkCarry, gulpWobble;
	int gulpStreak;
	bool triggerHeld;
	SuctionCone cone;
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
		using var perf = Perf.Time( "GoreVac update" );
		var camera = controller.Camera;
		if ( camera is null ) return;
		var from = camera.GlobalPosition;
		var forward = -camera.GlobalBasis.Z;
		var nozzle = Nozzle();

		// Right click (aim) sucks, reload blows, left click (fire) shoots.
		var wantSuck = allowed && (Input.IsActionPressed( "attack2" ) || ForceSuck);
		var wantBlow = allowed && !wantSuck && (Input.IsActionPressed( "reload" ) || ForceBlow);
		Sucking = wantSuck;
		Blowing = wantBlow;
		if ( wantSuck && Ammo >= MagazineSize && sinceFullWarning > FullBeepEvery )
		{
			// The full beep: two quick high ticks, and on it sucks (the excess goes out the back).
			sinceFullWarning = 0f;
			Sfx.Play2D( "ui_click", 0.6f, 2.2f, Sfx.GunBus );
			GetTree().CreateTimer( 0.09 ).Timeout += () => Sfx.Play2D( "ui_click", 0.6f, 2.6f, Sfx.GunBus );
		}
		var touched = touchedBuffer;
		touched.Clear();
		if ( Sucking ) Suck( dt, from, forward, nozzle, touched );
		else if ( caught.Count > 0 ) ReleaseCaught();
		if ( Blowing ) Blow( dt, from, forward, nozzle );
		if ( tugs.Count > 0 )
		{
			staleBuffer.Clear();
			foreach ( var key in tugs.Keys ) if ( !touched.Contains( key ) ) staleBuffer.Add( key );
			foreach ( var key in staleBuffer ) tugs.Remove( key );
		}
		UpdateIncomingMine( dt );
		UpdateLoopSounds();
		UpdateWeight( dt );

		// The shot: a pull of the trigger fires a ball at once. Keep holding and (with enough in the tank) the ring fills;
		// let go with it full for the huge one. Let go early and nothing more happens.
		if ( allowed )
		{
			var down = Input.IsActionPressed( "attack1" );
			if ( Input.IsActionJustPressed( "attack1" ) )
			{
				if ( (Ammo >= BallCost || HasMine) && TimeSinceShot >= 1f / CurrentFireRate ) { Spit( false ); triggerHeld = true; sinceTrigger = 0f; }
				else if ( Ammo < BallCost && !HasMine ) Sfx.Play2D( "dry_fire" );
			}
			if ( triggerHeld && down && !charging && sinceTrigger > 0.25f && Ammo >= BigBallMin && !HasMine ) { charging = true; sinceChargeStart = 0f; }
			if ( charging && Ammo < BigBallMin ) charging = false;
			if ( !down )
			{
				if ( charging && ChargeAmount >= 1f ) Spit( true );
				charging = false;
				triggerHeld = false;
			}
		}
		else { charging = false; triggerHeld = false; }
		cone ??= SuctionCone.Create( controller.Camera );
		cone.Show( dt, Nozzle(), -controller.Camera.GlobalBasis.Z, Sucking ? 1f : Blowing ? -1f : 0f, TankColour );
		UpdateTankModel( dt );
	}

	/// <summary> Pulls in gibs, puddles, enemies, loose limbs, floor props and mines in the cone. </summary>
	void Suck( float dt, Vector3 from, Vector3 forward, Vector3 nozzle, HashSet<Node> touched )
	{
		using var perf = Perf.Time( "GoreVac suck" );
		var gore = GoreSystem.Get( controller );
		var pull = 1f - MathF.Exp( -7f * dt );
		// Full or not, it keeps taking things in (when full, they are blown out the back: Vent).
		{
			// Pieces in the cone are caught, and stay caught while the trigger is held (sweep the gun and they follow).
			foreach ( var gib in gore.Gibs )
				if ( !caught.ContainsKey( gib ) && !gib.IsFading && InCone( from, forward, gib.GlobalPosition, VacRange, VacCone, out _ ) ) caught[gib] = 0f;
			caughtBuffer.Clear();
			caughtBuffer.AddRange( caught.Keys );
			foreach ( var gib in caughtBuffer )
			{
				if ( !IsInstanceValid( gib ) || gib.IsQueuedForDeletion() ) { caught.Remove( gib ); continue; }
				SuckIn( gib, caught[gib] += dt, dt, forward, nozzle );
			}
			// Puddles on the floor shrink as they are slurped up; the ones gone fly up the nozzle as goo.
			foreach ( var (at, colour) in gore.Slurp( from, forward, VacRange * 0.85f, VacCone - 0.1f, dt ) )
			{
				Slurped++;
				if ( Ammo >= MagazineSize && Rng.Float() < 0.5f ) Vent( colour with { A = 1f }, false );
				AddGunk( PuddleGunk, colour );
				for ( var i = 0; i < 4; i++ ) GpuFx.SandPuff( at + Rng.InSphere( 0.2f ), (nozzle - at).SafeNormal() * Rng.Float( 5f, 9f ) + Rng.InSphere( 1f ), colour, Rng.Float( 6f, 12f ), 0.7f );
			}
		}

		// Enemies: dragged in by weight; a light, weak one at the nozzle is sucked apart (its gibs come straight in).
		creatureBuffer.Clear();
		creatureBuffer.AddRange( Creature.Everyone );
		foreach ( var creature in creatureBuffer )
		{
			if ( !IsInstanceValid( creature ) || !creature.IsEnemy || creature.IsDead || !creature.Pullable ) continue;
			var centre = creature.GlobalPosition + Vector3.Up * 0.3f;
			if ( !InCone( from, forward, centre, VacRange, VacCone - 0.05f, out var distance ) ) continue;
			var toNozzle = nozzle - centre;
			var d = toNozzle.Length();
			if ( d < SwallowReach && creature.Weight <= SwallowWeight && creature.Health.Current <= SwallowHealth )
			{
				EnemiesSwallowed++;
				var shooter = controller;
				PlayerPerks.Hurt( creature, 9999f, centre, nozzle, shooter );
				AddGunk( EnemyGunk, TankColour );
				Sfx.Play2D( "gib", 0.9f, 0.8f, Sfx.GunBus );
				juice?.Shake( 0.25f );
				Controls.Rumble( 0.5f );
				continue;
			}
			// Limbs shot half off are torn away after a short tug.
			if ( creature is PartCreature body && d < 12f )
				foreach ( var limb in body.Limbs() )
				{
					if ( limb.Health > limb.MaxHealth * (RipBelow + SkillFx.ShredderPer * Skills.Rank( controller, SkillId.Shredder )) ) continue;
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
			if ( d < HoldOff ) continue;   // close enough: held in front of the nozzle (limbs above still tear)
			var strength = PullStrength * (1f + SkillFx.SuctionPer * Skills.Rank( controller, SkillId.IndustrialSuction )) / MathF.Max( 30f, creature.Weight ) * MathX.Lerp( 1f, 0.5f, MathX.Clamp( distance / FalloffReach, 0f, 1f ) );
			creature.Pull( toNozzle / d * MathF.Min( strength * dt, d - HoldOff ) );
		}

		// The small things on the floor: torn up and sucked in like gibs (stone and shell: hardly worth a thing).
		// (The floor props are listed afresh twice a second, not every frame.)
		if ( sinceProps > 0.5f || propBuffer.Count == 0 )
		{
			sinceProps = 0f;
			propBuffer.Clear();
			foreach ( var node in GetTree().GetNodesInGroup( SeaDressing.LooseGroup ) ) if ( node is Node3D n ) propBuffer.Add( n );
		}
		for ( var p = propBuffer.Count - 1; p >= 0; p-- )
		{
			var prop = propBuffer[p];
			if ( !IsInstanceValid( prop ) || !prop.IsInGroup( SeaDressing.LooseGroup ) ) { propBuffer.RemoveAt( p ); continue; }
			if ( !IsInstanceValid( prop ) || !InCone( from, forward, prop.GlobalPosition, VacRange, VacCone, out _ ) ) continue;
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
		{
			// The nearest mine in reach and in the cone gets the tug.
			SeaMine nearest = null;
			var best = float.MaxValue;
			foreach ( var mine in SeaMine.All )
			{
				if ( mine.Gone || !InCone( from, forward, mine.BallPosition, MineReach, VacCone - 0.05f, out var d ) || d >= best ) continue;
				best = d;
				nearest = mine;
			}
			if ( nearest is not null )
			{
				touched.Add( nearest );
				tugs[nearest] = tugs.GetValueOrDefault( nearest ) + dt;
				if ( tugs[nearest] >= MineTug )
				{
					tugs.Remove( nearest );
					incomingMine = nearest.TearLoose();
					incomingSpeed = 2f;
					MinesTorn++;
				}
			}
		}

		// The draw of the water itself: faint streaks rushing into the nozzle from the cone.
		// The draw: sand lifted off the floor where the cone meets it, and silt rushing in through the water.
		if ( sinceStreak > 0.03f )
		{
			sinceStreak = 0f;
			var dir = (forward + Rng.InSphere( 1f ) * 0.5f).Normalized();
			var floor = Trace.Ray( controller, from, from + dir * 14f, Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
			if ( floor.Hit && floor.HitPosition.DistanceTo( from ) > 1.5f )
				GpuFx.SandPuff( floor.HitPosition + floor.Normal * 0.1f, (nozzle - floor.HitPosition).SafeNormal() * Rng.Float( 4f, 7f ) + floor.Normal * 1.5f, new Color( 0.82f, 0.86f, 0.8f ), Rng.Float( 10f, 18f ), 0.9f );
			var at = from + dir * Rng.Float( 2f, 6f );
			GpuFx.SandPuff( at, (nozzle - at).SafeNormal() * Rng.Float( 6f, 10f ), new Color( 0.75f, 0.92f, 1f, 0.6f ), Rng.Float( 3f, 6f ), 0.6f );
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
			gib.LinearVelocity += (gib.GlobalPosition - from).SafeNormal() * 30f * (1f - MathX.Clamp( d / FalloffReach, 0f, 0.8f )) * dt;
		}
		foreach ( var creature in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( creature ) || !creature.IsEnemy || creature.IsDead || !creature.Pullable ) continue;
			var centre = creature.GlobalPosition + Vector3.Up * 0.3f;
			if ( !InCone( from, forward, centre, VacRange, VacCone - 0.1f, out var d ) ) continue;
			creature.Pull( (centre - from).SafeNormal() * PullStrength * 1.3f / MathF.Max( 30f, creature.Weight ) * (1f - MathX.Clamp( d / FalloffReach, 0f, 1f ) * 0.5f) * dt );
		}
		// The thrust: back from where it points, strongest pointing down (hover).
		var thrust = 13f + MathF.Max( 0f, -forward.Y ) * 9f;
		if ( controller.Velocity.Dot( -forward ) < 7f ) controller.Velocity -= forward * thrust * dt;
		if ( sinceStreak > 0.03f )
		{
			sinceStreak = 0f;
			var dir = (forward + Rng.InSphere( 1f ) * 0.35f).Normalized();
			GpuFx.SandPuff( nozzle + dir * 0.4f, dir * Rng.Float( 7f, 11f ), new Color( 0.75f, 0.92f, 1f, 0.6f ), Rng.Float( 3f, 6f ), 0.7f );
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

	/// <summary>
	/// One caught piece on its way in: it eases off the floor, then speeds up as it nears the nozzle, swirling round the
	/// aim with the vortex (tighter and faster the closer it gets), tumbling harder, stretched along its flight. Over the
	/// last couple of metres it stops colliding and shrinks to a speck, then goes down the nozzle.
	/// </summary>
	void SuckIn( Gib gib, float held, float dt, Vector3 forward, Vector3 nozzle )
	{
		var at = gib.GlobalPosition;
		var toNozzle = nozzle - at;
		var d = toNozzle.Length();
		if ( d < SwallowAt ) { Swallow( gib ); return; }
		var dir = toNozzle / d;
		// The swirl: round the aim line, strongest in the middle stretch, gone at the mouth.
		var offAxis = (at - nozzle) - forward * (at - nozzle).Dot( forward );
		var tangent = offAxis.LengthSquared() > 0.0001f ? forward.Cross( offAxis ).Normalized() : Vector3.Zero;
		var swirl = MathX.Clamp( d / 3f, 0f, 1f ) * MathX.Clamp( (8f - d) / 3f, 0f, 1f ) * 3.5f;
		// The pull: it eases in over the first moment (so it lifts off rather than snapping away), then accelerates as it nears.
		var ease = MathX.Clamp( held / 0.45f, 0f, 1f );
		ease *= ease;
		var near = 1f - MathX.Clamp( d / 8f, 0f, 1f );
		var speed = MathX.Lerp( 2.5f, MathX.Lerp( 9f, 24f, near * near ), ease ) * MathX.Lerp( 0.5f, 1f, 1f - MathX.Clamp( d / FalloffReach, 0f, 1f ) );
		var want = dir * speed + tangent * swirl * ease;
		gib.Sleeping = false;
		gib.LinearVelocity = gib.LinearVelocity.Lerp( want, 1f - MathF.Exp( -(6f + near * 14f) * dt ) );
		gib.AngularVelocity = gib.AngularVelocity.Lerp( Rng.InSphere( 1f ).Normalized() * (6f + near * 30f), dt * 4f );
		// The last stretch: no more bumping into things, and it shrinks away into the nozzle.
		if ( d < ShrinkReach )
		{
			var k = MathX.Clamp( (d - SwallowAt) / (ShrinkReach - SwallowAt), 0f, 1f );
			gib.Suck( 0.12f + 0.88f * k * k * (3f - 2f * k) );
		}
		// A thin wisp of its goo trailing behind it.
		if ( gib.Goo is Color goo && Rng.Float() < dt * 18f )
			GpuFx.SandPuff( at, -gib.LinearVelocity * 0.15f, goo, Rng.Float( 2f, 4f ) * MathX.Lerp( 1f, 0.4f, near ), 0.35f );
	}

	/// <summary> Lets go of everything caught (the trigger was released): they drop and regrow as they were. </summary>
	void ReleaseCaught()
	{
		foreach ( var gib in caught.Keys ) if ( IsInstanceValid( gib ) ) gib.Release();
		caught.Clear();
	}

	void Swallow( Gib gib )
	{
		caught.Remove( gib );
		var colour = gib.Goo ?? new Color( 0.6f, 0.6f, 0.62f );
		if ( Ammo >= MagazineSize ) { Vented++; Vent( colour, gib.Goo is null ); }
		if ( gib.Goo is null ) AddGunk( DryGunk, null );
		else AddGunk( gib.Recycled ? RecycledGunk : MathX.Clamp( gib.Radius / U.In * 0.4f, GibGunkMin, GibGunkMax ), colour );
		Swallowed++;
		gib.QueueFree();
		Gulp( colour, gib.Goo is null );
	}

	/// <summary>
	/// The feel of a piece going down: a wet gulp that climbs in pitch while they keep coming (a streak), the tank's
	/// goo bulging and sloshing, a spurt of goo into the mouth, a tick of rumble, the HUD counter popping.
	/// </summary>
	void Gulp( Color colour, bool dry )
	{
		SinceGulp = 0f;
		gulpWobble = 1f;
		gulpStreak = sinceGulp < 0.5f ? Math.Min( gulpStreak + 1, 14 ) : 0;
		if ( sinceGulp > 0.05f )
		{
			sinceGulp = 0f;
			var pitch = 1.1f + gulpStreak * 0.045f + Rng.Float( -0.04f, 0.04f );
			Sfx.Play2D( dry ? "mine_tink" : "splat", 0.5f, pitch, Sfx.GunBus );
			Sfx.Play2D( "bubble_pop", 0.35f, pitch * 1.1f, Sfx.GunBus );
			Controls.Rumble( 0.1f + gulpStreak * 0.01f );
		}
		var nozzle = Nozzle();
		var forward = -controller.Camera.GlobalBasis.Z;
		for ( var i = 0; i < 2; i++ ) GpuFx.SandPuff( nozzle + forward * 0.15f, -forward * Rng.Float( 1f, 2f ) + Rng.InSphere( 0.5f ), colour, Rng.Float( 3f, 5f ), 0.25f );
		juice?.Shake( 0.03f );
	}

	/// <summary> For tests: pieces blown out the back with the tank full. </summary>
	public static int Vented;
	TimeSince sinceVent = 9f;

	/// <summary>
	/// The tank is full, so what came in goes straight out the exhaust at the back of the gun: a spurt of goo (or grit)
	/// thrown back past your shoulder, falling and splatting behind you, with a wet pfft.
	/// </summary>
	void Vent( Color colour, bool dry )
	{
		var camera = controller.Camera;
		var basis = camera.GlobalBasis;
		var exhaust = camera.GlobalPosition + basis.X * 0.28f - basis.Y * 0.22f - basis.Z * 0.15f;
		var back = (basis.Z * 1f + basis.X * 0.6f + basis.Y * 0.35f).Normalized();
		for ( var i = 0; i < 5; i++ )
		{
			var v = (back + Rng.InSphere( 0.25f )).Normalized() * Rng.Float( 4f, 7f );
			if ( dry ) GpuFx.SandPuff( exhaust, v, colour, Rng.Float( 3f, 6f ), 0.5f );
			else GooDroplet.Spawn( exhaust, v, colour, Rng.Float( 0.5f, 0.9f ) );
		}
		GpuFx.SandPuff( exhaust, back * 3f, colour with { A = 0.7f }, Rng.Float( 6f, 10f ), 0.45f );
		if ( sinceVent > 0.12f )
		{
			sinceVent = 0f;
			Sfx.Play2D( "bubbles", 0.4f, Rng.Float( 1.5f, 1.8f ), Sfx.GunBus );
		}
	}

	/// <summary> Adds gunk to the tank (fractions carry over), mixing in its colour (null: a dry piece, no colour). </summary>
	void AddGunk( float amount, Color? colour )
	{
		gunkCarry += amount * (1f + SkillFx.GourmetPer * Skills.Rank( controller, SkillId.Gourmet ));
		var whole = (int)MathF.Floor( gunkCarry );
		gunkCarry -= whole;
		Ammo = Math.Min( MagazineSize, Ammo + whole );
		if ( colour is not Color c ) return;
		// Mixed, but kept rich: green, pink and blue goo would average to a dull grey otherwise.
		var mix = TankColour.Lerp( c with { A = 1f }, MathX.Clamp( amount / 4f, 0.05f, 0.4f ) );
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
		var radius = (mine ? GoreBall.MineRadius : big ? BallRadius * MathF.Min( 2.2f, 0.75f + scale * 0.15f ) : BallRadius) * (1f + SkillFx.BoomsPer * Skills.Rank( controller, SkillId.BiggerBooms ));
		var size = big ? MathF.Min( 0.55f, 0.16f + scale * 0.04f ) : 0.14f;
		// Aim at what is under the crosshair: the ball flies from the nozzle toward it. With no enemy right under it,
		// an enemy close beside the crosshair line is aimed at instead (aim assist: easy to land).
		var exclude = new Godot.Collections.Array<Rid> { controller.GetRid() };
		var aim = Trace.Ray( controller, camera.GlobalPosition, camera.GlobalPosition + forward * 80f, Layers.World | Layers.Creature, exclude );
		var target = aim.Hit ? aim.HitPosition : camera.GlobalPosition + forward * 80f;
		var reach = aim.Hit ? aim.HitPosition.DistanceTo( camera.GlobalPosition ) + 1f : 80f;
		if ( Shootables.Find( aim.Node )?.ShotOwner is not Creature && GoreBall.NearestToLine( camera.GlobalPosition, forward, reach, 1.4f ) is { } assisted ) target = assisted;
		var speed = mine ? 26f : big ? 30f : 42f;
		var direction = (target - nozzle).SafeNormal();
		var kind = mine ? GoreBall.Kind.Mine : big ? GoreBall.Kind.Big : GoreBall.Kind.Small;
		var ball = GoreBall.Launch( GetTree().CurrentScene, nozzle, direction * speed, damage, radius, size, TankColour, controller, this, kind );
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
		gulpWobble = MathF.Max( 0f, gulpWobble - dt * 5f );
		var slosh = (Sucking || Blowing ? MathF.Sin( (float)Clock.Now * 30f ) * 0.02f : lastSway.X * 0.03f) + MathF.Sin( (float)Clock.Now * 45f ) * 0.05f * gulpWobble;
		var bulge = 1f + gulpWobble * 0.07f;
		gunkFill.Scale = new Vector3( bulge, bulge, MathF.Max( 0.02f, shownFill + slosh ) );
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

/// <summary>
/// A ball of gore shot from the Gore Vac: fast and nearly straight, it curves a little onto an enemy near its path,
/// goes off when it passes close to one (a proximity fuse, so a near miss still counts), and goes off on whatever it
/// hits. Every burst is a mini explosion: a flash and a shock ring, goo everywhere, every enemy in the radius hurt and
/// shoved back, loose gibs blown outward. The huge one throws four bomblets out of its blast; a loaded sea mine flies
/// the same way and goes off as a full blast.
/// </summary>
public partial class GoreBall : Node3D
{
	public enum Kind { Small, Big, Mine, Bomblet }
	public const float MineDamage = 450f, MineRadius = 4f;
	/// <summary> It goes off this far (m) from an enemy part it passes. </summary>
	public const float Proximity = 0.9f;
	/// <summary> How hard it curves onto an enemy near its path (radians a second), and how far off its path it looks. </summary>
	const float HomingTurn = 3.5f, HomingCone = 0.9f, HomingRange = 40f;
	const float Gravity = 1.2f;

	Vector3 velocity;
	float damage, radius, size, fuse = -1f;
	Color colour;
	Kind kind;
	PlayerController shooter;
	PlayerWeapon weapon;
	TimeSince age, sinceDrip, sinceLook = 9f;
	MeshInstance3D blob;
	Creature homing;
	/// <summary> The burst at the end of a black hole: it doesn't open another one. </summary>
	bool fromHole;

	/// <summary> Goes off at once at a point, as if a ball had got there (the black hole's last act). </summary>
	public static void BurstAt( Node parent, Vector3 at, Vector3 normal, float damage, float radius, float size, Color colour, PlayerController shooter, PlayerWeapon weapon, Kind kind )
	{
		var ball = Launch( parent, at, Vector3.Zero, damage, radius, size, colour, shooter, weapon, kind );
		ball.fromHole = true;
		ball.Burst( at, normal );
	}
	public static int Bursts, Hits, Splits, MineBlasts, ProximityBursts;
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
		if ( kind == Kind.Bomblet ) ball.fuse = Rng.Float( 0.35f, 0.6f );
		if ( kind != Kind.Mine )
		{
			ball.blob = new MeshInstance3D { Name = "Blob", Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 }, MaterialOverride = GoreVacModel.GunkMaterial( colour ) };
			ball.AddChild( ball.blob );
			ball.blob.Scale = Vector3.One * size;
			for ( var i = 0; i < 4; i++ )
			{
				var lump = new MeshInstance3D { Mesh = ball.blob.Mesh, MaterialOverride = GoreVacModel.GunkMaterial( colour.Darkened( 0.25f ) ) };
				ball.blob.AddChild( lump );
				lump.Position = Rng.OnSphere() * 0.75f;
				lump.Scale = Vector3.One * Rng.Float( 0.3f, 0.5f );
			}
		}
		else ball.size = SeaMine.Radius * 0.8f;
		ball.AddChild( new OmniLight3D { LightColor = kind == Kind.Mine ? new Color( 0.4f, 1f, 0.5f ) : colour, LightEnergy = 1f, OmniRange = 2f + ball.size * 4f, ShadowEnabled = false } );
		return ball;
	}

	/// <summary> The point on the nearest enemy part within `width` of a line (from, along `dir`, up to `length`), if any. </summary>
	/// <summary>
	/// Where every live enemy's parts are this frame: gathered once per frame and shared by every ball in flight (each
	/// ball used to walk every creature part in the level itself, every frame).
	/// </summary>
	static readonly List<Vector3> enemyParts = new();
	static ulong enemyPartsFrame = ulong.MaxValue;
	static List<Vector3> EnemyParts()
	{
		var frame = Engine.GetProcessFrames();
		if ( frame == enemyPartsFrame ) return enemyParts;
		enemyPartsFrame = frame;
		enemyParts.Clear();
		foreach ( var part in CreaturePart.All )
			if ( GodotObject.IsInstanceValid( part ) && part.Creature is Creature { IsEnemy: true, IsDead: false } ) enemyParts.Add( part.GlobalPosition );
		return enemyParts;
	}

	public static Vector3? NearestToLine( Vector3 from, Vector3 dir, float length, float width )
	{
		Vector3? best = null;
		var bestOff = width;
		foreach ( var partAt in EnemyParts() )
		{
			var to = partAt - from;
			var along = to.Dot( dir );
			if ( along < 1f || along > length ) continue;
			var off = (to - dir * along).Length() - along * 0.012f;   // a little wider the further out
			if ( off < bestOff ) { bestOff = off; best = partAt; }
		}
		return best;
	}

	static bool EnemyNear( Vector3 at, float reach )
	{
		var reach2 = reach * reach;
		foreach ( var partAt in EnemyParts() )
			if ( partAt.DistanceSquaredTo( at ) < reach2 ) return true;
		return false;
	}

	public override void _Process( double delta )
	{
		using var perf = Perf.Time( "GoreBall" );
		var dt = (float)delta;
		var from = GlobalPosition;
		if ( fuse >= 0f && (fuse -= dt) <= 0f ) { Burst( from, Vector3.Up ); return; }

		// Homing: a gentle curve onto the enemy nearest its heading (not for bomblets, which scatter).
		if ( kind != Kind.Bomblet && sinceLook > 0.1f )
		{
			sinceLook = 0f;
			homing = null;
			var heading = velocity.SafeNormal();
			var bestScore = float.MaxValue;
			foreach ( var c in Creature.Everyone )
			{
				if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
				var to = c.GlobalPosition + Vector3.Up * 0.4f - from;
				var d = to.Length();
				if ( d > HomingRange || d < 0.01f ) continue;
				var along = heading.Dot( to / d );
				if ( along < HomingCone ) continue;
				var score = d * (2f - along);
				if ( score < bestScore ) { bestScore = score; homing = c; }
			}
		}
		if ( homing is not null && IsInstanceValid( homing ) && !homing.IsDead )
		{
			var want = (homing.GlobalPosition + Vector3.Up * 0.4f - from).SafeNormal();
			var speed = velocity.Length();
			var heading = velocity / MathF.Max( speed, 0.01f );
			var angle = heading.AngleTo( want );
			velocity = heading.Slerp( want, MathF.Min( 1f, HomingTurn * dt / MathF.Max( angle, 0.001f ) ) ).Normalized() * speed;
		}
		velocity += Vector3.Down * Gravity * dt;
		var to2 = from + velocity * dt;

		// The proximity fuse: passing close to an enemy is a hit.
		var reach = Proximity + size * (kind == Kind.Big ? 1.5f : 1f);
		if ( EnemyNear( to2, reach ) ) { ProximityBursts++; LastHitName = "near an enemy"; Burst( to2, -velocity.SafeNormal() ); return; }

		var exclude = shooter is not null && IsInstanceValid( shooter ) ? new Godot.Collections.Array<Rid> { shooter.GetRid() } : null;
		var hit = Trace.Ray( this, from, to2 + velocity.SafeNormal() * size, Layers.World | Layers.Creature, exclude );
		if ( age > 4f ) { Burst( to2, -velocity.SafeNormal() ); return; }
		if ( hit.Hit )
		{
			LastHitName = $"{hit.Node?.Name} ({hit.Node?.GetType().Name})";
			Burst( hit.HitPosition - velocity.SafeNormal() * MathF.Min( size, 0.1f ), hit.Normal );
			return;
		}
		GlobalPosition = to2;
		if ( kind == Kind.Mine ) { RotateObjectLocal( Vector3.Right, dt * 5f ); return; }
		// It wobbles like jelly and stretches along its flight, dripping as it goes.
		var t = (float)age * 18f;
		var dir = velocity.SafeNormal();
		if ( dir != Vector3.Zero ) blob.GlobalBasis = Shapes.LookAt( dir ) * Basis.FromScale( new Vector3( 1f + MathF.Sin( t ) * 0.1f, 1f + MathF.Sin( t + 2f ) * 0.1f, 1.35f ) * size );
		if ( sinceDrip > 0.04f )
		{
			sinceDrip = 0f;
			GooDroplet.Spawn( GlobalPosition, -velocity * 0.05f + Rng.InSphere( 0.8f ), colour, 0.5f + size * 2f );
		}
	}

	void Burst( Vector3 at, Vector3 normal )
	{
		// Black Hole (Vac-Bot's capstone): the huge shot tears a hole open here instead, and bursts when it collapses.
		var holeRank = kind == Kind.Big && !fromHole && shooter is not null && IsInstanceValid( shooter ) ? Skills.Rank( shooter, SkillId.BlackHole ) : 0;
		if ( holeRank > 0 )
		{
			BlackHole.Open( GetParent(), at + normal * 0.2f, damage, radius, size, colour, shooter, weapon, holeRank );
			QueueFree();
			return;
		}
		Bursts++;
		LastBurst = at;
		var gore = GoreSystem.Get( this );
		var who = shooter is not null && IsInstanceValid( shooter ) ? shooter : null;
		var hurt = PlayerPerks.HurtCreatures( at, radius, damage, who );
		Hits += hurt;
		if ( hurt > 0 && weapon is not null && IsInstanceValid( weapon ) ) weapon.MarkHit();
		// Everything in the radius is shoved back (heavier things less), and loose pieces fly.
		var shove = kind switch { Kind.Mine => 1.4f, Kind.Big => 1f, Kind.Bomblet => 0.3f, _ => 0.45f };
		foreach ( var creature in Creature.Everyone.ToList() )
			if ( IsInstanceValid( creature ) && creature.IsEnemy && !creature.IsDead && creature.Pullable && creature.GlobalPosition.DistanceTo( at ) < radius )
				creature.Pull( (creature.GlobalPosition - at).Flat().SafeNormal() * shove * 100f / MathF.Max( 60f, creature.Weight ) );
		foreach ( var gib in gore.Gibs.ToList() )
			if ( gib.GlobalPosition.DistanceTo( at ) < radius * 1.5f ) gib.LinearVelocity += (gib.GlobalPosition - at).SafeNormal() * 9f * shove;

		if ( kind == Kind.Mine )
		{
			MineBlasts++;
			AlienBlast.Spawn( GetTree().CurrentScene, at, 2.2f );
			Sfx.Play( "missile_boom", at, 1f );
			if ( who is not null && who.GlobalPosition.DistanceTo( at ) < radius )
				who.Velocity += ((who.GlobalPosition - at).Flat().SafeNormal() + Vector3.Up * 0.5f) * 7f;
			if ( who?.GetNodeOrNull<CameraJuice>( "CameraJuice" ) is { } boom && who.GlobalPosition.DistanceTo( at ) < radius * 4f ) boom.Shake( 0.8f );
			QueueFree();
			return;
		}

		// The mini explosion: a blast sized to the ball, then the goo.
		var k = size / 0.14f;
		var blast = kind switch { Kind.Big => 1.5f, Kind.Bomblet => 0.4f, _ => 0.6f };
		AlienBlast.Spawn( GetTree().CurrentScene, at + normal * 0.1f, blast );
		Sfx.Play( kind == Kind.Big ? "missile_boom" : "splat", at, kind == Kind.Big ? 0.9f : 1f, kind == Kind.Big ? 1.1f : Rng.Float( 0.75f, 0.9f ) );
		if ( kind != Kind.Big ) Sfx.Play( "missile_boom", at, 0.35f, Rng.Float( 1.6f, 1.9f ) );   // a small pop under the splat
		gore.GooBurst( at, normal, colour, (int)(14 * MathF.Min( 3f, k )), 340f * MathF.Min( 2f, k ), 1.1f * MathF.Min( 2.5f, k ) );
		gore.GooSpray( at, normal, (int)(5 * MathF.Min( 3f, k )), colour, 140f * radius / PlayerWeapon.BallRadius, 0.6f, 1.5f * MathF.Min( 2f, k ) );
		gore.GooSpray( at, Vector3.Down, 2, colour, 120f, 1f, 1.4f * MathF.Min( 2f, k ) );
		for ( var i = 0; i < Math.Min( 4, (int)k ); i++ )
		{
			var lump = gore.ThrowBlob( at + normal * 0.1f, normal, colour.Darkened( 0.2f ), colour, Rng.Float( 1.5f, 2.6f ) );
			if ( lump is not null ) lump.Recycled = true;
		}
		gore.Squelch( at, 1.3f, k > 2f ? 0.7f : 1f );
		// The huge one throws four bomblets out of its blast.
		if ( kind == Kind.Big )
		{
			Splits++;
			var up = normal == Vector3.Zero ? Vector3.Up : normal;
			var side = up.Cross( MathF.Abs( up.Y ) > 0.9f ? Vector3.Right : Vector3.Up ).Normalized();
			var other = up.Cross( side );
			var bomblets = 4 + (shooter is not null && IsInstanceValid( shooter ) ? Skills.Rank( shooter, SkillId.Clusterfunk ) : 0);
			for ( var i = 0; i < bomblets; i++ )
			{
				var a = i * MathF.Tau / bomblets + Rng.Float( -0.3f, 0.3f );
				var dir = (side * MathF.Cos( a ) + other * MathF.Sin( a ) + up * 0.9f).Normalized();
				Launch( GetParent(), at + up * 0.3f, dir * Rng.Float( 6f, 9f ), damage * 0.25f, PlayerWeapon.BallRadius * 0.8f, 0.1f, colour, shooter, weapon, Kind.Bomblet );
			}
		}
		if ( who?.GetNodeOrNull<CameraJuice>( "CameraJuice" ) is { } juice && who.GlobalPosition.DistanceTo( at ) < radius * 3f )
			juice.Shake( 0.25f * MathF.Min( 3f, k ) );
		QueueFree();
	}
}

/// <summary>
/// The suction you can see: a cone of water spiralling into the nozzle (out of it while blowing), drawn with a scrolling
/// spiral shader, and specks of silt whirling down it into the nozzle (flung out along it while blowing). Fades in and
/// out with the trigger, tinted a little toward the tank's goo. Lives under the camera.
/// </summary>
public partial class SuctionCone : MeshInstance3D
{
	const float Length = 4.5f, Wide = 1.5f, Narrow = 0.07f;
	const int Specks = 90;
	ShaderMaterial material;
	MultiMeshInstance3D specks;
	readonly float[] along = new float[Specks], angle = new float[Specks], spread = new float[Specks], rate = new float[Specks];
	float shown, flow = 1f;

	public static SuctionCone Create( Camera3D camera )
	{
		var cone = new SuctionCone
		{
			Name = "Suction Cone", CastShadow = ShadowCastingSetting.Off, Visible = false,
			Mesh = new CylinderMesh { TopRadius = Narrow, BottomRadius = Wide, Height = Length, RadialSegments = 40, Rings = 8, CapTop = false, CapBottom = false },
		};
		cone.material = new ShaderMaterial { Shader = new Shader { Code = Code } };
		cone.material.SetMeta( "no_caustics", true );
		cone.MaterialOverride = cone.material;
		camera.AddChild( cone );
		// The specks: one small stretched bead each, coloured per speck (for the fade).
		var bead = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 6, Rings = 3 };
		var speckMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = Colors.White,
		};
		speckMaterial.SetMeta( "no_caustics", true );
		bead.Material = speckMaterial;
		cone.specks = new MultiMeshInstance3D
		{
			Name = "Specks", CastShadow = ShadowCastingSetting.Off,
			Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = Specks, Mesh = bead },
		};
		cone.AddChild( cone.specks );
		for ( var i = 0; i < Specks; i++ ) cone.Respawn( i, Rng.Float( 0f, 1f ) );
		return cone;
	}

	/// <summary> A speck starting again at the open end (or the nozzle while blowing), at `t` along. </summary>
	void Respawn( int i, float t )
	{
		along[i] = t;
		angle[i] = Rng.Float( 0f, MathF.Tau );
		spread[i] = Rng.Float( 0.25f, 1f );
		rate[i] = Rng.Float( 0.5f, 1f );
	}

	/// <summary> `mode`: 1 sucking, -1 blowing, 0 off. </summary>
	public void Show( float dt, Vector3 nozzle, Vector3 forward, float mode, Color tint )
	{
		using var perf = Perf.Time( "SuctionCone" );
		if ( mode != 0f ) flow = mode;
		shown = MathX.Approach( shown, MathF.Abs( mode ), dt * (mode != 0f ? 5f : 3f) );
		Visible = shown > 0.01f && !DevFlags.HideWeapon;
		if ( !Visible ) return;
		// Its narrow top at the nozzle, opening out along the aim (local +Y points back at the nozzle).
		GlobalTransform = new Transform3D( Shapes.LookAt( forward ) * new Basis( Vector3.Right, MathF.PI * 0.5f ), nozzle + forward * Length * 0.5f );
		var water = new Color( 0.8f, 0.96f, 1f ).Lerp( tint, 0.3f );
		material.SetShaderParameter( "strength", shown );
		material.SetShaderParameter( "flow", flow );
		material.SetShaderParameter( "tint", water );

		// The specks whirl in, faster and tighter the nearer the nozzle (out and slowing while blowing).
		var mm = specks.Multimesh;
		for ( var i = 0; i < Specks; i++ )
		{
			var t = along[i];
			t += dt * rate[i] * (0.45f + t * 1.6f) * flow;
			if ( t >= 1f ) { Respawn( i, 0f ); t = 0f; }
			else if ( t <= 0f ) { Respawn( i, 1f ); t = 1f; }
			along[i] = t;
			angle[i] += dt * (1.5f + t * 7f) * flow;
			var y = -Length * 0.5f + Length * t;
			var r = MathX.Lerp( Wide * 0.95f, Narrow, t ) * spread[i];
			var at = new Vector3( MathF.Cos( angle[i] ) * r, y, MathF.Sin( angle[i] ) * r );
			var stretch = 0.035f + t * 0.05f;
			mm.SetInstanceTransform( i, new Transform3D( Basis.FromScale( new Vector3( 0.035f, stretch * 2.4f, 0.035f ) ), at ) );
			var fade = MathX.Clamp( t * 6f, 0f, 1f ) * MathX.Clamp( (1f - t) * 8f, 0f, 1f );
			mm.SetInstanceColor( i, (i % 5 == 0 ? tint : water) with { A = 0.5f * fade * shown } );
		}
	}

	const string Code = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
uniform float strength = 1.0;
uniform float flow = 1.0;
uniform vec4 tint : source_color = vec4(0.75, 0.95, 1.0, 1.0);

void fragment() {
	// UV.x runs round the cone, UV.y from the nozzle (0) to the open end (1).
	float along = UV.y;
	float spin = fract(UV.x * 4.0 + along * 3.0 + TIME * 1.1 * flow);
	float spiral = smoothstep(0.0, 0.1, spin) * smoothstep(0.38, 0.14, spin);
	float rush = fract(along * 6.0 + TIME * 3.0 * flow);
	float streaks = smoothstep(0.0, 0.06, rush) * smoothstep(0.3, 0.08, rush);
	float ends = smoothstep(0.0, 0.1, along) * smoothstep(1.0, 0.6, along);
	float edge = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 1.2);
	float a = (spiral * 0.75 + streaks * 0.4) * ends * (0.5 + 0.5 * edge) * strength;
	ALBEDO = tint.rgb;
	ALPHA = clamp(a * 0.2, 0.0, 0.22);   // faint: the pieces being sucked in should show through it
}";
}
