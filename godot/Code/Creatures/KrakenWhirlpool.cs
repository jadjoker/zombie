using Godot;

/// <summary>
/// The Kraken's whirlpool (issue #30, stage 2). It fights where it came up, so players are kept inside its fighting
/// radius, marked by a ring of lights on the sea bed. Step outside and a whirlpool starts to gather round you with
/// a 5 second warning on screen; step back in and it dies away. Run out the warning and it picks you up, spins
/// you round and drops you back inside the ring.
/// </summary>
public partial class Kraken
{
	/// <summary> How far from the Kraken players may go (metres, on the flat). </summary>
	public static float FightRadius = 20f;
	/// <summary> Seconds of warning before the whirlpool takes a player who is outside. </summary>
	public float WarningSeconds = 5f;
	/// <summary> Seconds the whirlpool takes to carry a player back. </summary>
	public float PullSeconds = 2.4f;
	/// <summary> Where a carried player is put down, as a fraction of the fighting radius. </summary>
	public float DropAt = 0.55f;

	sealed class Leash
	{
		public float Outside;          // seconds spent outside, counting up to the warning time
		public bool Pulling;
		public float Pulled;           // seconds into the pull
		public Vector3 From, To;
		public float StartAngle, StartDistance;
		public int LastBeep = -1;
		public Node3D Funnel, Spinner;
		public float Shown;            // 0 to 1: how strong the whirlpool round them looks
	}

	readonly Dictionary<PlayerSurvivor, Leash> leashes = new();
	Node3D ring;
	StandardMaterial3D ringMaterial;
	static StandardMaterial3D streakMaterial;

	/// <summary>
	/// For the HUD: null when the player is fine, otherwise the seconds of warning left (0 while being carried).
	/// </summary>
	public static float? WhirlpoolWarning( PlayerSurvivor player )
	{
		foreach ( var kraken in All )
		{
			if ( kraken.IsDead || !kraken.leashes.TryGetValue( player, out var leash ) ) continue;
			if ( leash.Pulling ) return 0f;
			if ( leash.Outside > 0f ) return MathF.Max( 0f, kraken.WarningSeconds - leash.Outside );
		}
		return null;
	}

	/// <summary> Is this player being carried by a whirlpool right now. </summary>
	public static bool Carrying( PlayerSurvivor player ) => All.Any( k => k.leashes.TryGetValue( player, out var l ) && l.Pulling );

	protected override void Cleanup()
	{
		base.Cleanup();
		ReleaseAll();
		EndAttack();
		if ( ring is not null && IsInstanceValid( ring ) ) ring.QueueFree();
	}

	/// <summary> Lets go of everyone (it died or left the scene). </summary>
	void ReleaseAll()
	{
		foreach ( var (player, leash) in leashes )
		{
			if ( leash.Pulling && IsInstanceValid( player ) && player.Controller is { } c ) { c.Scripted = false; c.Velocity = Vector3.Zero; }
			if ( leash.Funnel is not null && IsInstanceValid( leash.Funnel ) ) leash.Funnel.QueueFree();
		}
		leashes.Clear();
		if ( ring is not null && IsInstanceValid( ring ) ) ring.Visible = false;
	}

	/// <summary> Every frame once it has arrived: the ring, the warnings and the carrying. </summary>
	void UpdateWhirlpool( float dt )
	{
		if ( State != Mode.Idle ) return;
		if ( ring is null ) BuildRing();
		var anyOutside = false;
		foreach ( var player in PlayerSurvivor.All.ToList() )
		{
			if ( !leashes.TryGetValue( player, out var leash ) ) leashes[player] = leash = new Leash();
			var controller = player.Controller;
			if ( controller is null || !player.IsAlive ) { Drop( controller, leash ); continue; }
			if ( leash.Pulling ) { Carry( player, leash, dt ); anyOutside = true; continue; }

			var away = (player.WorldPosition - GlobalPosition).Flat().Length();
			// Flying about with the dev noclip is left alone.
			var outside = away > FightRadius && !DevFlags.Noclip && !controller.Scripted;
			if ( outside )
			{
				anyOutside = true;
				leash.Outside += dt;
				var left = WarningSeconds - leash.Outside;
				var beep = (int)MathF.Ceiling( left );
				if ( beep != leash.LastBeep && left > 0f )
				{
					leash.LastBeep = beep;
					Sfx.Play2D( "whirl_warn", 0.8f, 1f + (WarningSeconds - beep) * 0.06f );
				}
				if ( left <= 0f ) StartPull( player, leash );
			}
			else
			{
				// Back inside: the warning runs down twice as fast as it built.
				leash.Outside = MathF.Max( 0f, leash.Outside - dt * 2f );
				if ( leash.Outside <= 0f ) leash.LastBeep = -1;
			}
			ShowFunnel( controller, leash, leash.Outside / WarningSeconds * 0.7f, dt );
		}
		foreach ( var gone in leashes.Keys.Where( p => !IsInstanceValid( p ) ).ToList() ) leashes.Remove( gone );
		ringMaterial.EmissionEnergyMultiplier = anyOutside ? 2.2f + MathF.Sin( time * 12f ) * 1.2f : 0.9f + MathF.Sin( time * 2f ) * 0.3f;
	}

	void StartPull( PlayerSurvivor player, Leash leash )
	{
		var controller = player.Controller;
		var offset = (player.WorldPosition - GlobalPosition).Flat();
		var bearing = offset.SafeNormal();
		if ( bearing == Vector3.Zero ) bearing = -GlobalBasis.Z;
		leash.Pulling = true;
		leash.Pulled = 0f;
		leash.Outside = 0f;
		leash.LastBeep = -1;
		leash.From = controller.GlobalPosition;
		leash.StartAngle = MathF.Atan2( bearing.X, bearing.Z );
		leash.StartDistance = offset.Length();
		// Put down on the navmesh (so never inside a rock), part way in along the way they left.
		leash.To = Nav.Closest( this, GlobalPosition + bearing * FightRadius * DropAt ) + Vector3.Up * 0.1f;
		controller.Scripted = true;
		controller.Velocity = Vector3.Zero;
		Sfx.Play2D( "whirlpool", 1f );
		controller.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.5f );
	}

	/// <summary> The ride: up off the sea bed, round the Kraken in a tightening spiral, and down inside the ring. </summary>
	void Carry( PlayerSurvivor player, Leash leash, float dt )
	{
		var controller = player.Controller;
		leash.Pulled += dt;
		var t = MathX.Clamp( leash.Pulled / PullSeconds, 0f, 1f );
		var ease = Ease( t );
		var endOffset = (leash.To - GlobalPosition).Flat();
		var distance = MathX.Lerp( leash.StartDistance, endOffset.Length(), ease );
		// Three quarters of a turn round the Kraken, ending on the bearing they left by.
		var angle = leash.StartAngle + (1f - ease) * MathF.Tau * 0.75f;
		var flat = GlobalPosition + new Vector3( MathF.Sin( angle ), 0f, MathF.Cos( angle ) ) * distance;
		var height = MathX.Lerp( leash.From.Y, leash.To.Y, ease ) + MathF.Sin( t * MathF.PI ) * 3.2f;
		controller.GlobalPosition = new Vector3( flat.X, height, flat.Z );
		controller.Velocity = Vector3.Zero;
		controller.ResetPhysicsInterpolation();
		controller.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.9f * dt );
		ShowFunnel( controller, leash, 1f, dt );
		if ( t < 1f ) return;
		controller.GlobalPosition = leash.To;
		Drop( controller, leash );
		Sfx.Play( "land", leash.To, 0.9f, 0.8f );
	}

	static void Drop( PlayerController controller, Leash leash )
	{
		if ( leash.Pulling && controller is not null )
		{
			controller.Scripted = false;
			controller.Velocity = Vector3.Zero;
		}
		leash.Pulling = false;
		leash.Outside = 0f;
		leash.LastBeep = -1;
	}

	/// <summary> The swirl of water round a player: streaks on a spinning helix, stronger and faster as it builds. </summary>
	void ShowFunnel( PlayerController controller, Leash leash, float strength, float dt )
	{
		leash.Shown = MathX.Approach( leash.Shown, strength, dt * (strength > leash.Shown ? 3f : 1.5f) );
		if ( leash.Shown <= 0.01f )
		{
			if ( leash.Funnel is not null && IsInstanceValid( leash.Funnel ) ) leash.Funnel.Visible = false;
			return;
		}
		if ( leash.Funnel is null || !IsInstanceValid( leash.Funnel ) ) BuildFunnel( leash );
		leash.Funnel.Visible = true;
		leash.Funnel.GlobalPosition = controller.GlobalPosition;
		leash.Funnel.Scale = new Vector3( 0.6f + leash.Shown * 0.6f, 0.3f + leash.Shown * 0.9f, 0.6f + leash.Shown * 0.6f );
		leash.Spinner.RotateY( dt * (2.5f + leash.Shown * 11f) );
		streakMaterial.AlbedoColor = new Color( 0.7f, 1f, 0.95f, 0.2f + leash.Shown * 0.4f );
	}

	void BuildFunnel( Leash leash )
	{
		streakMaterial ??= new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.8f, 1f, 0.95f, 0.4f ),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		leash.Funnel = new Node3D { Name = "Kraken Whirlpool" };
		GetTree().CurrentScene.AddChild( leash.Funnel );
		leash.Spinner = new Node3D { Name = "Spinner" };
		leash.Funnel.AddChild( leash.Spinner );
		// Three arms winding up from the feet, widening to the top.
		const int arms = 4, perArm = 22;
		for ( var a = 0; a < arms; a++ )
		{
			for ( var i = 0; i < perArm; i++ )
			{
				var f = i / (perArm - 1f);
				var angle = a * MathF.Tau / arms + f * MathF.Tau * 1.25f;
				var radius = 1.3f + f * 1.2f;
				var streak = new MeshInstance3D
				{
					Mesh = Shapes.Sphere,
					MaterialOverride = streakMaterial,
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
					Position = new Vector3( MathF.Sin( angle ) * radius, f * 2.6f, MathF.Cos( angle ) * radius ),
					// Long along the way it travels, thin across.
					Basis = new Basis( Vector3.Up, angle + MathF.PI * 0.5f ) * Basis.FromScale( new Vector3( 0.05f + f * 0.03f, 0.035f, 0.45f + f * 0.3f ) ),
				};
				leash.Spinner.AddChild( streak );
			}
		}
	}

	/// <summary> The edge of the fight: glowing markers on the sea bed all the way round. </summary>
	void BuildRing()
	{
		ring = new Node3D { Name = "Kraken Ring" };
		GetTree().CurrentScene.AddChild( ring );
		ringMaterial = Shapes.Material( Green, glow: true );
		var count = Math.Max( 24, (int)(FightRadius * MathF.Tau / 2.2f) );
		for ( var i = 0; i < count; i++ )
		{
			var angle = i / (float)count * MathF.Tau;
			var at = GlobalPosition + new Vector3( MathF.Sin( angle ), 0f, MathF.Cos( angle ) ) * FightRadius;
			var down = Trace.Ray( this, at + Vector3.Up * 12f, at + Vector3.Down * 12f, Layers.World );
			if ( !down.Hit ) continue;
			var marker = new MeshInstance3D
			{
				Mesh = Shapes.Sphere,
				MaterialOverride = ringMaterial,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Scale = new Vector3( 0.5f, 0.16f, 0.5f ),
			};
			ring.AddChild( marker );
			marker.GlobalPosition = down.HitPosition + Vector3.Up * 0.05f;
		}
		builtRadius = FightRadius;
	}

	float builtRadius;

	/// <summary> The dev menu changed the radius: lay the ring out again. </summary>
	void RefreshRing()
	{
		if ( ring is null || builtRadius == FightRadius ) return;
		ring.QueueFree();
		ring = null;
	}
}
