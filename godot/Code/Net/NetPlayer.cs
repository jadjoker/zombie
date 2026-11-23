using Godot;

/// <summary>
/// One player's link to the other machines (<see cref="Net"/>). On the local player it sends where the robot is, its
/// speed, aim, ground contact, health and held weapon about 30 times a second, and every press and release of the
/// buttons that drive weapons and moves as it happens. On a remote player it eases the robot to what came in and feeds
/// the presses to its <see cref="RemoteInput"/>, so its own weapon code fires them.
/// </summary>
public partial class NetPlayer : Node
{
	/// <summary> A remote robot is being set from its owner's state (its respawn here isn't sent back). </summary>
	public static bool ApplyingState;
	PlayerController controller;
	PlayerSurvivor survivor;
	WeaponInventory inventory;
	float sendTimer;
	int lastSlot = -1;
	readonly bool[] sentDown = new bool[RemoteInput.Actions.Length];

	// Host, for a remote robot: what its owner said its speed was (anything else on it came from the host's creatures).
	Vector3 lastSetVelocity, pendingImpulse;
	bool hostHeld;
	float heldTimer, impulseTimer;
	// Local: held in place by the host's creature (a grab) until this time.
	double forcedUntil;
	bool forced;

	// Remote: the latest state.
	Vector3 targetPosition, targetVelocity;
	float targetPitch, targetYaw;
	bool hasState;
	double stateAt;

	/// <summary> The fields of a state packet, in order. </summary>
	enum F { X, Y, Z, VX, VY, VZ, Pitch, Yaw, Flags, MoveX, MoveY, Health, MaxHealth, Ammo, Points, Kills, Ping, Count }
	/// <summary> Remote: its owner's points, kills this run and ping to the host (the Tab scoreboard). </summary>
	public int Points, Kills, Ping;
	int loadoutVersion = -1;
	float loadoutTimer;

	public override void _Ready()
	{
		Name = "NetPlayer";
		controller = GetParent<PlayerController>();
		// Before the player's own code each frame, so a remote press is seen by its weapons the frame it lands.
		ProcessPriority = -500;
	}

	public override void _Process( double delta )
	{
		survivor ??= controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		inventory ??= controller.GetNodeOrNull<WeaponInventory>( "WeaponInventory" );
		if ( !Net.Online ) return;
		if ( controller.IsRemote ) Follow( (float)delta );
		else if ( !Net.Connected ) return;
		else Send( (float)delta );
	}

	// ---- Local: sending ----

	/// <summary> Can the local player's buttons act right now (no menu open, not standing on the dock with the weapons racked)? </summary>
	static bool ButtonsLive => !PlayerWeapon.MouseFree && !DevMenu.IsOpen && !PauseMenu.IsOpen && !ClassPicker.IsOpen;

	void Send( float dt )
	{
		var net = Net.Instance;
		// A grab whose messages stopped (the host's creature died mid-grab): free again.
		if ( forced && Clock.Real > forcedUntil ) { forced = false; controller.Scripted = false; }
		// Presses and releases, as they happen (reliable).
		var live = ButtonsLive;
		for ( var i = 0; i < RemoteInput.Actions.Length; i++ )
		{
			var action = RemoteInput.Actions[i];
			var tapped = live && Input.IsActionJustPressed( action );
			var down = live && Input.IsActionPressed( action );
			if ( tapped && !down && !sentDown[i] ) { net.SendEdge( i, true ); net.SendEdge( i, false ); continue; }   // a tap inside one frame
			if ( down == sentDown[i] ) continue;
			sentDown[i] = down;
			net.SendEdge( i, down );
		}
		// The weapon in hand.
		var slot = inventory?.Current?.Slot ?? -1;
		if ( slot != lastSlot && slot >= 0 ) { lastSlot = slot; net.SendWeapon( slot ); }

		sendTimer -= dt;
		if ( sendTimer > 0f ) return;
		sendTimer = 1f / Net.SendRate;
		var p = controller.GlobalPosition;
		var v = controller.Velocity;
		var move = live ? Input.GetVector( "left", "right", "backward", "forward" ) : Vector2.Zero;
		var flags = (controller.IsOnGround ? 1 : 0) | (controller.IsDucking ? 2 : 0) | (survivor is { IsAlive: true } ? 4 : 0) | (controller.Scripted ? 8 : 0);
		var state = new float[(int)F.Count];
		state[(int)F.X] = p.X; state[(int)F.Y] = p.Y; state[(int)F.Z] = p.Z;
		state[(int)F.VX] = v.X; state[(int)F.VY] = v.Y; state[(int)F.VZ] = v.Z;
		state[(int)F.Pitch] = controller.EyeAngles.Pitch; state[(int)F.Yaw] = controller.EyeAngles.Yaw;
		state[(int)F.Flags] = flags;
		state[(int)F.MoveX] = move.X; state[(int)F.MoveY] = move.Y;
		state[(int)F.Health] = survivor?.Health?.Current ?? 0f;
		state[(int)F.MaxHealth] = survivor?.Health?.MaxHealth ?? 100f;
		state[(int)F.Ammo] = inventory?.Current?.Ammo ?? 0;
		state[(int)F.Points] = survivor?.Points ?? 0;
		state[(int)F.Kills] = survivor?.RunKills ?? 0;
		state[(int)F.Ping] = Net.MyPing;
		net.SendState( state );

		// Skill points and the damage upgrades bought: when they change (and now and then, for a late joiner).
		loadoutTimer -= 1f / Net.SendRate;
		if ( loadoutVersion != PlayerSkills.Version || loadoutTimer <= 0f )
		{
			loadoutVersion = PlayerSkills.Version;
			loadoutTimer = 3f;
			var ranks = PlayerSkills.Of( controller )?.Ranks ?? Array.Empty<int>();
			var level = inventory?.Weapons.Count > 0 ? inventory.Weapons.Max( w => w.DamageLevel ) : 0;
			net.SendLoadout( ranks, level );
		}
	}

	// ---- Remote: applying ----

	public void ApplyState( float[] s )
	{
		if ( s is null || s.Length < (int)F.Count || !controller.IsRemote ) return;
		var position = new Vector3( s[(int)F.X], s[(int)F.Y], s[(int)F.Z] );
		if ( !hasState || position.DistanceTo( controller.GlobalPosition ) > 8f ) { controller.GlobalPosition = position; controller.ResetPhysicsInterpolation(); }
		hasState = true;
		stateAt = Clock.Real;
		targetPosition = position;
		targetVelocity = new Vector3( s[(int)F.VX], s[(int)F.VY], s[(int)F.VZ] );
		targetPitch = s[(int)F.Pitch];
		targetYaw = s[(int)F.Yaw];
		var flags = (int)s[(int)F.Flags];
		controller.SetRemoteGround( (flags & 1) != 0, (flags & 2) != 0 );
		if ( !Net.IsHost ) controller.Scripted = (flags & 8) != 0;   // on the host a remote robot is only held by the host's own creatures
		if ( controller.In is RemoteInput input ) input.Move = new Vector2( s[(int)F.MoveX], s[(int)F.MoveY] );
		inventory?.Current?.SetNetAmmo( (int)s[(int)F.Ammo] );
		Points = (int)s[(int)F.Points];
		Kills = (int)s[(int)F.Kills];
		Ping = (int)s[(int)F.Ping];
		if ( survivor?.Health is { } h )
		{
			h.MaxHealth = s[(int)F.MaxHealth];
			var alive = (flags & 4) != 0;
			if ( alive && h.IsDead ) { ApplyingState = true; try { survivor.Respawn( position, fullReset: false ); } finally { ApplyingState = false; } }
			if ( !alive && !h.IsDead ) h.Kill();
			else if ( alive ) h.Current = s[(int)F.Health];
		}
	}

	void Follow( float dt )
	{
		if ( controller.In is RemoteInput input ) input.Apply();
		if ( !hasState ) return;
		if ( Net.IsHost && HostSide( dt ) ) return;
		// A little ahead along its speed (the packet is a few frames old), eased so it never jumps.
		var age = (float)Math.Min( 0.15, Clock.Real - stateAt );
		var want = targetPosition + targetVelocity * age;
		controller.GlobalPosition = controller.GlobalPosition.Lerp( want, 1f - MathF.Exp( -18f * dt ) );
		controller.Velocity = targetVelocity;
		lastSetVelocity = targetVelocity;
		var e = controller.EyeAngles;
		e.Pitch = MathX.Lerp( e.Pitch, targetPitch, 1f - MathF.Exp( -25f * dt ) );
		e.Yaw = LerpAngle( e.Yaw, targetYaw, 1f - MathF.Exp( -25f * dt ) );
		controller.EyeAngles = e;
	}

	/// <summary>
	/// The host's view of another player's robot. A creature that shoved it (its speed changed since this set it) sends
	/// the shove to the owner; one that grabbed it (Scripted: the Kraken's whirlpool, the angler's hook, Davey) holds it
	/// here and sends where it is, so the owner is carried along. True while held (it isn't eased to its owner's spot).
	/// </summary>
	bool HostSide( float dt )
	{
		var shove = controller.Velocity - lastSetVelocity;
		if ( !controller.Scripted && shove.LengthSquared() > 0.25f ) pendingImpulse += shove;
		impulseTimer -= dt;
		if ( impulseTimer <= 0f && pendingImpulse.LengthSquared() > 0.25f )
		{
			impulseTimer = 0.05f;
			Net.Instance.SendImpulse( controller.PeerId, pendingImpulse );
			pendingImpulse = Vector3.Zero;
		}
		if ( controller.Scripted )
		{
			hostHeld = true;
			heldTimer -= dt;
			if ( heldTimer <= 0f ) { heldTimer = 1f / Net.SendRate; Net.Instance.SendHeld( controller.PeerId, controller.GlobalPosition, true ); }
			lastSetVelocity = controller.Velocity;
			return true;
		}
		if ( hostHeld ) { hostHeld = false; Net.Instance.SendHeld( controller.PeerId, controller.GlobalPosition, false ); }
		return false;
	}

	/// <summary> The robot's own code (its gun, moves) changed its speed: that isn't a shove from a creature. </summary>
	public void AbsorbOwnMotion( Vector3 change ) => lastSetVelocity += change;

	/// <summary> Local: a host creature is holding this robot (or let go). </summary>
	public void Held( Vector3 at, bool held )
	{
		forced = held;
		forcedUntil = held ? Clock.Real + 0.4 : 0;
		controller.Scripted = held;
		if ( !held ) return;   // let go where it is (it may have been taken somewhere else meanwhile, like Davey's locker)
		controller.GlobalPosition = at;
		controller.Velocity = Vector3.Zero;
		controller.ResetPhysicsInterpolation();
	}

	/// <summary> Remote: its owner's skill points and damage upgrades. </summary>
	public void ApplyLoadout( int[] ranks, int damageLevel )
	{
		PlayerSkills.Of( controller )?.SetRanks( ranks );
		foreach ( var w in controller.GetChildren().OfType<PlayerWeapon>() ) w.DamageLevel = damageLevel;
	}

	static float LerpAngle( float from, float to, float t )
	{
		var d = ((to - from) % 360f + 540f) % 360f - 180f;
		return from + d * t;
	}
}
