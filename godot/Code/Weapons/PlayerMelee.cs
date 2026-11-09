using Godot;

/// <summary>
/// Port of s&box PlayerMelee: press Melee (mouse 5) to swing the fire axe. Works with any gun in hand, hides the gun
/// during the swing and cleaves everything in front of you with a fan of sphere sweeps. Swings alternate right and
/// left, each playing the rigged axe model's own animation.
/// </summary>
public partial class PlayerMelee : Node
{
	public float Damage = 70f;
	public float Reach = 110f * U.In;
	public float Radius = 16f * U.In;
	public float ArcDegrees = 38f;
	public float Impulse = 320f * U.In;
	public float Cooldown = 0.55f;
	public float WindUp = 0.24f;

	public bool IsSwinging => Swing < Cooldown;
	TimeSince Swing = 999f;
	bool struck;
	bool leftSwing;
	ViewModelAxe axe;
	readonly HashSet<object> hitThisSwing = new();

	PlayerController controller;
	WeaponInventory inventory;
	PlayerWeapon Weapon => inventory?.Current;

	public override void _Ready()
	{
		Name = "PlayerMelee";
		controller = GetParent<PlayerController>();
		inventory = controller.GetNodeOrNull<WeaponInventory>( "WeaponInventory" );
		CallDeferred( MethodName.HookWeapons );
	}

	void HookWeapons()
	{
		foreach ( var w in controller.GetChildren().OfType<PlayerWeapon>() ) w.MeleeActive = () => IsSwinging;
	}

	public override void _Process( double delta )
	{
		if ( Input.IsActionJustPressed( "melee" ) && Swing >= Cooldown && !DevFlags.Placing && !DevFlags.FreeCam ) TestSwing();
		if ( !struck && Swing >= WindUp && Swing < Cooldown )
		{
			struck = true;
			Strike();
		}
		UpdateView();
	}

	/// <summary> Swings as if the button was pressed (also the testing hook). </summary>
	public void TestSwing()
	{
		Swing = 0f;
		struck = false;
		leftSwing = !leftSwing;
		hitThisSwing.Clear();
		axe?.Play( leftSwing );
	}

	void Strike()
	{
		var camera = controller.Camera;
		var start = camera.GlobalPosition;
		var basis = camera.GlobalBasis;
		var anyHit = false;
		var exclude = new Godot.Collections.Array<Rid> { controller.GetRid() };
		for ( var i = -2; i <= 2; i++ )
		{
			var direction = -(basis * Basis.FromEuler( new Vector3( 0f, MathX.Rad( i * ArcDegrees * 0.5f ), 0f ) )).Z;
			var tr = Trace.Sphere( controller, Radius, start, start + direction * Reach, Layers.World | Layers.Creature | Layers.Gib, exclude );
			if ( tr.Hit && Hit( tr, start, direction ) ) anyHit = true;
		}
		var juice = controller.GetNodeOrNull<CameraJuice>( "CameraJuice" );
		juice?.Shake( anyHit ? 0.45f : 0.12f );
		juice?.KickFov( anyHit ? 2.5f : 0.8f );
		if ( anyHit ) Weapon?.MarkHit();
	}

	bool Hit( TraceResult tr, Vector3 start, Vector3 direction )
	{
		if ( tr.Node is null ) return false;
		var shootable = Shootables.Find( tr.Node );
		if ( shootable is not null && shootable.ShotOwner is not null )
		{
			if ( shootable.IsDead || !hitThisSwing.Add( tr.Node ) ) return false;
			var wasAlive = !shootable.IsDead;
			Fx.ImpactFlesh( tr.HitPosition, -direction, new Color( 0.9f, 0.3f, 0.2f ) );
			shootable.ReceiveShot( Damage, tr.Node, direction, tr.HitPosition, start, controller, hitThisSwing.Add( shootable.ShotOwner ) );
			if ( wasAlive && shootable.IsDead ) Weapon?.MarkKill();
			return true;
		}
		if ( tr.Node is Gib gib )
		{
			if ( !hitThisSwing.Add( gib ) ) return false;
			gib.TakeHit( tr.HitPosition, direction, Damage, Impulse * 0.5f / U.In );
			return true;
		}
		if ( tr.Node is RigidBody3D body && !body.Freeze && hitThisSwing.Add( body ) )
			body.ApplyImpulse( direction * Impulse * body.Mass * 0.3f, tr.HitPosition - body.GlobalPosition );
		return false;
	}

	void UpdateView()
	{
		var show = !controller.ThirdPerson && !DevFlags.HideWeapon && !DevFlags.FreeCam && Swing < Cooldown;
		if ( !show ) { if ( axe is not null ) axe.Visible = false; return; }
		if ( axe is null )
		{
			axe = new ViewModelAxe();
			controller.Camera.AddChild( axe );
			axe.Play( leftSwing );
		}
		axe.Visible = true;
	}
}

/// <summary> The rigged first-person axe (gloved hands and all), playing its swing animations. </summary>
public partial class ViewModelAxe : Node3D
{
	AnimationPlayer player;
	Node3D model;
	string skin;

	public override void _Ready()
	{
		Name = "Melee Axe";
		var (root, anim, _) = WeaponModels.Create( "axe" );
		model = root;
		player = anim;
		AddChild( root );
	}

	public void Play( bool left )
	{
		if ( player is null ) return;
		var name = left ? "swing_l" : "swing_r";
		player.Stop();
		player.Play( name, 0f, 1f );
	}

	public override void _Process( double delta )
	{
		if ( skin != WeaponSkins.Current ) { skin = WeaponSkins.Current; WeaponSkins.Apply( model, skin ); }
	}
}
