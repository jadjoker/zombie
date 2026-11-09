using Godot;

/// <summary> A shootable dummy for the test range: flashes when hit, falls over at 0 health, gets back up. </summary>
public partial class TestTarget : StaticBody3D, IShootable
{
	public float MaxHealth = 150f;
	float health;
	TimeSince flash = 999f;
	TimeSince died = 999f;
	StandardMaterial3D material;
	Node3D body;

	public Node ShotOwner => this;
	public bool IsDead => health <= 0f;

	public override void _Ready()
	{
		health = MaxHealth;
		CollisionLayer = Layers.Creature;
		CollisionMask = 0;
		material = new StandardMaterial3D { AlbedoColor = new Color( 0.85f, 0.75f, 0.3f ) };
		body = new Node3D();
		AddChild( body );
		var size = new Vector3( 28f, 72f, 14f ) * U.In;
		body.AddChild( new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = Vector3.Up * size.Y * 0.5f } );
		body.AddChild( new MeshInstance3D { Mesh = new SphereMesh { Radius = 9f * U.In, Height = 18f * U.In }, MaterialOverride = material, Position = Vector3.Up * (size.Y + 9f * U.In) } );
		AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = size + Vector3.Up * 18f * U.In }, Position = Vector3.Up * (size.Y + 18f * U.In) * 0.5f } );
	}

	public void ReceiveShot( float damage, Node3D hitPart, Vector3 direction, Vector3 position, Vector3 origin, Node attacker, bool scoreHit )
	{
		if ( IsDead ) return;
		health -= damage;
		flash = 0f;
		if ( IsDead ) died = 0f;
	}

	public override void _Process( double delta )
	{
		var hit = MathX.Clamp( 1f - flash / 0.1f, 0f, 1f );
		material.AlbedoColor = new Color( 0.85f, 0.75f, 0.3f ).Lerp( Colors.White, hit );
		var tip = IsDead ? MathX.Clamp( died / 0.35f, 0f, 1f ) : 0f;
		body.RotationDegrees = new Vector3( -85f * tip * tip, 0f, 0f );
		if ( IsDead && died > 2.5f ) health = MaxHealth;
	}
}
