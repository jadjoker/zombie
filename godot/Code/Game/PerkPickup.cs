using Godot;

/// <summary>
/// A pickup a creature dropped (see <see cref="PlayerPerks"/>): its icon turning over a glowing ring on the floor, in
/// its own colour. Walk over it to take it. Fades away after a while, like the ammo box.
/// Drops: one roll for each creature that dies, so no creature ever drops two things: 30% ammo (as before), then the
/// pickups by their own small chances.
/// </summary>
public partial class PerkPickup : Node3D
{
	const float Life = 45f;
	const float Reach = 1.5f;   // metres
	public PerkKind Kind;
	TimeSince age;
	float baseY;
	Node3D turn;
	Sprite3D front, back;
	OmniLight3D light;
	static readonly Dictionary<string, Texture2D> icons = new();

	public static Texture2D Icon( string name )
	{
		if ( icons.TryGetValue( name, out var t ) ) return t;
		var path = $"res://assets/textures/pickups/{name}.png";
		return icons[name] = ResourceLoader.Exists( path ) ? GD.Load<Texture2D>( path ) : null;
	}

	/// <summary> A creature died: maybe one thing drops (an ammo box, or one of the pickups; never two). </summary>
	public static void MaybeDrop( Node3D at )
	{
		if ( at is null || !GodotObject.IsInstanceValid( at ) || at is HomingMissile ) return;
		var roll = Rng.Float();
		if ( roll < AmmoPickup.DropChance ) { AmmoPickup.Drop( at ); return; }
		roll -= AmmoPickup.DropChance;
		foreach ( var entry in PlayerPerks.Table )
		{
			if ( !PlayerPerks.Drops( entry.Kind ) ) continue;
			if ( roll < entry.Chance * ChanceScale ) { Drop( at, entry.Kind ); return; }
			roll -= entry.Chance * ChanceScale;
		}
	}

	/// <summary> Dev menu and tests: multiplies every pickup's chance (1 in play). </summary>
	public static float ChanceScale = 1f;

	public static PerkPickup Drop( Node3D at, PerkKind kind ) => DropAt( at.GetTree()?.CurrentScene, at, at.GlobalPosition, kind );

	public static PerkPickup DropAt( Node root, Node3D context, Vector3 where, PerkKind kind )
	{
		if ( root is null ) return null;
		var from = where + Vector3.Up * 0.5f;
		var down = Trace.Ray( context, from, from + Vector3.Down * 80f, Layers.World );
		var spot = down.Hit ? down.HitPosition + Vector3.Up * 0.55f : where;
		var pickup = new PerkPickup { Name = $"{kind} Pickup", Kind = kind, Position = spot };
		root.AddChild( pickup );
		return pickup;
	}

	public override void _Ready()
	{
		age = 0f;
		baseY = GlobalPosition.Y;
		var entry = PlayerPerks.Table.First( e => e.Kind == Kind );
		turn = new Node3D { Name = "Turn" };
		AddChild( turn );
		// The icon, on both faces of a card that turns.
		var texture = Icon( entry.Icon );
		front = new Sprite3D { Texture = texture, PixelSize = 0.0042f, Shaded = false, DoubleSided = false };
		back = new Sprite3D { Texture = texture, PixelSize = 0.0042f, Shaded = false, DoubleSided = false, Rotation = new Vector3( 0f, MathF.PI, 0f ) };
		turn.AddChild( front );
		turn.AddChild( back );
		// A glowing ring under it and a light in its colour.
		var glow = Sculpt.Glow( entry.Colour, 2.6f );
		AddChild( new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.3f, OuterRadius = 0.36f, Rings = 24, RingSegments = 6 }, MaterialOverride = glow, Position = Vector3.Down * 0.42f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		light = new OmniLight3D { LightColor = entry.Colour, LightEnergy = 1.6f, OmniRange = 3f, ShadowEnabled = false };
		AddChild( light );
	}

	public override void _Process( double delta )
	{
		var t = (float)age;
		if ( t > Life ) { QueueFree(); return; }
		GlobalPosition = GlobalPosition with { Y = baseY + MathF.Sin( t * 2.5f ) * 0.08f };
		turn.Rotation = new Vector3( 0f, t * 1.8f, 0f );
		// Blinks for its last six seconds, then shrinks away.
		var blink = t > Life - 6f ? 0.5f + 0.5f * MathF.Max( 0f, MathF.Sin( t * 12f ) ) : 1f;
		light.LightEnergy = 1.6f * blink;
		front.Modulate = back.Modulate = new Color( 1f, 1f, 1f, 0.4f + 0.6f * blink );
		turn.Scale = Vector3.One * MathX.Clamp( (Life - t) / 0.4f, 0.01f, 1f );

		foreach ( var survivor in PlayerSurvivor.All )
		{
			if ( !survivor.IsAlive || (survivor.Controller.GlobalPosition + Vector3.Up * 0.8f).DistanceTo( GlobalPosition ) > Reach ) continue;
			var perks = PlayerPerks.Of( survivor.Controller );
			if ( perks is null ) continue;
			perks.Give( Kind );
			var entry = PlayerPerks.Table.First( e => e.Kind == Kind );
			GameHud.Announce( entry.Name, entry.Colour );
			Sfx.Play2D( "buy", 0.8f, 1.3f );
			Sfx.Play2D( "heal", 0.5f, 1.4f );
			QueueFree();
			return;
		}
	}
}
