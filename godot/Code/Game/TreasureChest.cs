using Godot;

/// <summary>
/// A treasure chest in the Abyss's caves: an old iron-bound sea chest, gold glinting through the crack of its lid. A
/// player who walks up to it opens it: the lid swings up, gold coins burst out, it pays <see cref="Points"/> (the host
/// pays, online) and leaves an ammo box. Each opens once. Some of the chests laid are mimic octopuses
/// (<see cref="MimicOctopus.Disguise.Chest"/>), built from the same model by <see cref="Model"/>.
/// </summary>
public partial class TreasureChest : Node3D
{
	public static readonly List<TreasureChest> All = new();
	public const float OpenRange = 2.2f;
	public const int Points = 400;
	/// <summary> The chance a chest laid is a mimic instead. </summary>
	public const float MimicShare = 0.2f;
	public static int Opened;

	public bool IsOpen { get; private set; }
	Node3D lid;
	OmniLight3D glint;
	float openFor;

	static StandardMaterial3D wood, iron, gold, goldGlow;
	static void Paints()
	{
		if ( wood is not null ) return;
		wood = new StandardMaterial3D { AlbedoColor = new Color( 0.32f, 0.2f, 0.11f ), Roughness = 0.9f };
		iron = new StandardMaterial3D { AlbedoColor = new Color( 0.22f, 0.2f, 0.19f ), Roughness = 0.6f, Metallic = 0.7f };
		gold = new StandardMaterial3D { AlbedoColor = new Color( 1f, 0.78f, 0.25f ), Roughness = 0.3f, Metallic = 1f, EmissionEnabled = true, Emission = new Color( 0.6f, 0.4f, 0.08f ), EmissionEnergyMultiplier = 0.6f };
		goldGlow = new StandardMaterial3D { AlbedoColor = new Color( 1f, 0.8f, 0.3f ), EmissionEnabled = true, Emission = new Color( 1f, 0.72f, 0.2f ), EmissionEnergyMultiplier = 2.5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
	}

	/// <summary>
	/// The chest's model under `parent` (about 1.1 m long), its back hinge at the lid node's origin. Returns the lid.
	/// </summary>
	public static Node3D Model( Node3D parent )
	{
		Paints();
		const float w = 1.1f, d = 0.7f, h = 0.55f;
		var body = new Node3D { Name = "Chest" };
		parent.AddChild( body );
		GunKit.Part( body, new BoxMesh { Size = new Vector3( w, h, d ) }, wood, new Vector3( 0f, h * 0.5f, 0f ) );
		// Gold heaped inside, seen when the lid lifts (and a sliver of glow along the crack while shut).
		GunKit.Part( body, new SphereMesh { Radius = w * 0.45f, Height = 0.3f, RadialSegments = 12, Rings = 6 }, gold, new Vector3( 0f, h - 0.02f, 0f ) );
		GunKit.Part( body, new BoxMesh { Size = new Vector3( w * 0.92f, 0.02f, 0.02f ) }, goldGlow, new Vector3( 0f, h + 0.005f, d * 0.5f - 0.01f ) );
		foreach ( var x in new[] { -0.38f, 0.38f } )
			GunKit.Part( body, new BoxMesh { Size = new Vector3( 0.08f, h + 0.02f, d + 0.04f ) }, iron, new Vector3( x, h * 0.5f, 0f ) );
		GunKit.Part( body, new BoxMesh { Size = new Vector3( 0.16f, 0.18f, 0.05f ) }, iron, new Vector3( 0f, h - 0.08f, d * 0.5f + 0.02f ) );
		var lid = new Node3D { Name = "Lid", Position = new Vector3( 0f, h, -d * 0.5f ) };
		body.AddChild( lid );
		// A rounded lid: half a cylinder along the chest's length.
		GunKit.Part( lid, new CylinderMesh { TopRadius = d * 0.5f, BottomRadius = d * 0.5f, Height = w, RadialSegments = 16, Rings = 1 }, wood,
			new Vector3( 0f, 0f, d * 0.5f ), new Basis( Vector3.Back, MathF.PI * 0.5f ) * Basis.FromScale( new Vector3( 0.55f, 1f, 1f ) ) );
		foreach ( var x in new[] { -0.38f, 0.38f } )
			GunKit.Part( lid, new CylinderMesh { TopRadius = d * 0.52f, BottomRadius = d * 0.52f, Height = 0.08f, RadialSegments = 16, Rings = 1 }, iron,
				new Vector3( x, 0f, d * 0.5f ), new Basis( Vector3.Back, MathF.PI * 0.5f ) * Basis.FromScale( new Vector3( 0.55f, 1f, 1f ) ) );
		return lid;
	}

	public static TreasureChest Place( Node3D parent, Vector3 at, float yaw )
	{
		var chest = new TreasureChest { Name = "Treasure Chest" };
		parent.AddChild( chest );
		chest.GlobalPosition = at;
		chest.Rotation = new Vector3( 0f, yaw, 0f );
		chest.lid = Model( chest );
		chest.lid.Rotation = new Vector3( -0.04f, 0f, 0f );
		var solid = new StaticBody3D { Name = "Chest Solid", CollisionLayer = Layers.World | Layers.Plant, CollisionMask = 0 };
		solid.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3( 1.1f, 0.75f, 0.7f ) }, Position = new Vector3( 0f, 0.37f, 0f ) } );
		chest.AddChild( solid );
		chest.glint = new OmniLight3D { Name = "Gold Glint", LightColor = new Color( 1f, 0.75f, 0.3f ), LightEnergy = 0.25f, OmniRange = 2.5f, ShadowEnabled = false };
		chest.AddChild( chest.glint );
		chest.glint.Position = new Vector3( 0f, 0.8f, 0.3f );
		return chest;
	}

	public override void _EnterTree() => All.Add( this );
	public override void _ExitTree() => All.Remove( this );

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( IsOpen )
		{
			openFor += dt;
			var t = MathX.Clamp( openFor / 0.55f, 0f, 1f );
			// Up with a little bounce at the top.
			var swing = SmoothKit.Ease( t ) * 1.95f + MathF.Sin( MathX.Clamp( (openFor - 0.55f) / 0.4f, 0f, 1f ) * MathF.PI ) * 0.08f;
			lid.Rotation = new Vector3( -swing, 0f, 0f );
			glint.LightEnergy = MathX.Lerp( 2.2f, 0.6f, MathX.Clamp( openFor / 3f, 0f, 1f ) );
			return;
		}
		foreach ( var p in PlayerSurvivor.All )
			if ( p.IsAlive && p.RealPosition.DistanceTo( GlobalPosition ) < OpenRange ) { Open( p ); break; }
	}

	public void Open( PlayerSurvivor by )
	{
		if ( IsOpen ) return;
		IsOpen = true;
		Opened++;
		var at = GlobalPosition + Vector3.Up * 0.6f;
		Sfx.Play( "buy", at, 1f, 0.8f );
		Sfx.Play( "bubble_burst", at, 0.6f, 1.3f );
		Coins( at );
		if ( !Net.IsClient && by is not null ) by.RewardPoints( Points );
		AmmoPickup.Drop( this );
		GD.Print( $"[chest] opened, {Points} points" );
	}

	/// <summary> A burst of gold coins that tumble down and fade. </summary>
	void Coins( Vector3 at )
	{
		Paints();
		var coins = new CpuParticles3D
		{
			Name = "Coins", OneShot = true, Amount = 26, Lifetime = 1.6f, Explosiveness = 0.95f,
			Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.012f, RadialSegments = 10, Rings = 1, Material = gold },
			Direction = Vector3.Up, Spread = 40f, InitialVelocityMin = 2.5f, InitialVelocityMax = 4.5f, Gravity = new Vector3( 0f, -5f, 0f ),
			AngularVelocityMin = -400f, AngularVelocityMax = 400f, ParticleFlagRotateY = true, ScaleAmountMin = 0.8f, ScaleAmountMax = 1.3f,
		};
		GetTree().CurrentScene.AddChild( coins );
		coins.GlobalPosition = at;
		coins.Emitting = true;
		GetTree().CreateTimer( 2.5 ).Timeout += () => { if ( IsInstanceValid( coins ) ) coins.QueueFree(); };
	}
}
