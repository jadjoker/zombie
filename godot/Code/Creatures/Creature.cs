using Godot;

/// <summary>
/// What every creature shares: its Health node, dying (once), the Killed event, the list of everything alive, and the
/// spawn and setup steps. Each creature builds its own body and brain on top.
/// A new creature: derive from this (or <see cref="PartCreature"/> if it is built from limbs that can be shot off),
/// write a static Spawn that calls <see cref="Place{T}"/>, and override Setup, OnDied and ReceiveShot.
/// </summary>
public abstract partial class Creature : Node3D, ICreature
{
	/// <summary> Every creature in the scene, of any kind (allies included). </summary>
	public static readonly List<Creature> Everyone = new();

	public bool IsDead => health?.IsDead ?? false;
	public Health Health => health;
	/// <summary> Allies are left alone by the things that hunt creatures (the jetpack ram, the ally spider). </summary>
	public virtual bool IsEnemy => true;
	public event Action<Creature> Killed;

	protected Health health;
	protected GoreSystem gore;
	protected bool diedLocally;
	protected TimeSince timeSinceDeath;
	protected Vector3 lastShotDirection = Vector3.Forward;
	protected bool lastShotPointBlank;

	/// <summary> Puts a new creature in the scene with its Health (the shared half of every Spawn). </summary>
	protected static T Place<T>( T creature, Node parent, Vector3 position, float maxHealth ) where T : Creature
	{
		parent.AddChild( creature );
		creature.GlobalPosition = position;
		var h = new Health();
		h.ResetTo( maxHealth );
		creature.AddChild( h );
		return creature;
	}

	public override void _Ready()
	{
		Everyone.Add( this );
		Track( true );
		// Deferred: Spawn adds the Health and sets hue, size and so on after the node enters the tree.
		CallDeferred( MethodName.RunSetup );
	}

	public override void _ExitTree()
	{
		Everyone.Remove( this );
		Track( false );
		Cleanup();
	}

	void RunSetup() => Setup();

	/// <summary> Adds this to (or removes it from) its own kind's list. </summary>
	protected abstract void Track( bool add );

	/// <summary> Builds the creature. Call <see cref="BeginSetup"/> first. </summary>
	protected abstract void Setup();

	/// <summary> Leaving the scene: free anything that lives outside this node (a top-level rig, beams). </summary>
	protected virtual void Cleanup() { }

	/// <summary> Health ran out. Raise Killed and fall apart. </summary>
	protected abstract void OnDied();

	public abstract void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit );

	/// <summary> Finds the Health that Spawn added (or makes one), the gore system, and listens for death. </summary>
	protected void BeginSetup( float defaultHealth )
	{
		health = GetNodeOrNull<Health>( "Health" );
		if ( health is null ) { health = new Health(); health.ResetTo( defaultHealth ); AddChild( health ); }
		gore = GoreSystem.Get();
		health.Died += () => { timeSinceDeath = 0f; OnDied(); };
	}

	protected void RaiseKilled() => Killed?.Invoke( this );

	/// <summary> Remembers where the last shot came from (the death uses it to throw the pieces). </summary>
	protected void NoteShot( Vector3 direction, Vector3 position, Vector3 origin )
	{
		lastShotDirection = direction;
		lastShotPointBlank = (position - origin).Length() < CreatureGore.OverkillRange * U.In;
	}

	/// <summary> Turns to face a direction on the ground, at a rate (per second). </summary>
	protected void Face( Vector3 direction, float rate, float dt )
	{
		var flat = direction.Flat();
		if ( flat.IsNearlyZero() ) return;
		var wanted = Shapes.LookAt( flat ).GetRotationQuaternion();
		GlobalBasis = new Basis( GlobalBasis.Orthonormalized().GetRotationQuaternion().SafeSlerp( wanted, MathX.Clamp( dt * rate, 0f, 1f ) ) );
	}

	/// <summary> The angle of an eyelid's edge for how much of the eye it covers (0 open, 1 shut). </summary>
	protected static float LidEdge( float coverage ) => MathX.Deg( MathF.Asin( 1f - 2f * MathX.Clamp( coverage, 0f, 1f ) ) );
}

/// <summary>
/// A creature built from a tree of parts (the crab, the spider, the boss): each part is its own shootable body with
/// its own health, hangs off a parent part, and can be shot off along with everything below it.
/// </summary>
public abstract partial class PartCreature : Creature
{
	protected sealed class Part
	{
		public int Index;
		public int Parent = -1;
		public CreaturePart Go;
		public float Health;
		public float MaxHealth;
		public bool Gone;
		public float Hurt;
		public TimeSince Flash = 99f;
		public readonly List<(MeshInstance3D Renderer, Color Tint)> Renderers = new();
		public Vector3 LastPosition;
		public Vector3 Velocity;
		public Basis Rest = Basis.Identity;
	}

	/// <summary> Bit per part index: set once that part has been shot off. </summary>
	public int GoneMask;

	/// <summary> Every part by index (a null slot is a part this creature was built without). </summary>
	protected Part[] parts;
	/// <summary> The top-level node the parts hang from. </summary>
	protected Node3D rig;

	/// <summary> Prefix for part and piece names ("Crab"). </summary>
	protected abstract string Kind { get; }
	/// <summary> Collision layer of the parts (allies can't be shot: 0). </summary>
	protected virtual uint PartLayer => Layers.Creature;
	/// <summary> The colour of what's inside. </summary>
	protected abstract Color GibGoo { get; }

	protected override void Cleanup()
	{
		if ( rig is not null && IsInstanceValid( rig ) ) rig.QueueFree();
	}

	protected bool Has( int index ) => parts[index] is { Gone: false };

	protected Part AddPart( int index, int parent, Node3D parentGo, string name, Vector3 sboxPosition, Basis rotation, float hp )
	{
		var go = new CreaturePart { Name = $"{Kind} {name}", Creature = this, Index = index, CollisionLayer = PartLayer, CollisionMask = 0 };
		parentGo.AddChild( go );
		go.Transform = new Transform3D( rotation, U.Sbox( sboxPosition ) );
		var part = new Part { Index = index, Parent = parent, Go = go, Health = hp, MaxHealth = hp };
		parts[index] = part;
		return part;
	}

	/// <summary> A hit capsule on a part, between two points in the part's s&box space (inches). </summary>
	protected void Trigger( Part part, Vector3 from, Vector3 to, float radius )
	{
		var a = U.Sbox( from );
		var b = U.Sbox( to );
		var axis = b - a;
		var length = axis.Length();
		var shape = new CapsuleShape3D { Radius = radius * U.In, Height = length + radius * 2f * U.In };
		var basis = length > 1e-5f ? new Basis( new Quaternion( Vector3.Up, axis / length ) ) : Basis.Identity;
		part.Go.AddChild( new CollisionShape3D { Shape = shape, Transform = new Transform3D( basis, (a + b) * 0.5f ) } );
	}

	/// <summary> The bits for a part and everything hanging below it. </summary>
	protected int SubtreeMask( int index )
	{
		var mask = 1 << index;
		for ( var i = 0; i < parts.Length; i++ )
		{
			if ( i == index || parts[i] is null ) continue;
			for ( var p = parts[i].Parent; p >= 0; p = parts[p].Parent )
				if ( p == index ) { mask |= 1 << i; break; }
		}
		return mask;
	}

	protected void MarkGone( int index )
	{
		var mask = SubtreeMask( index );
		for ( var i = 0; i < parts.Length; i++ )
			if ( (mask & (1 << i)) != 0 && parts[i] is not null ) parts[i].Gone = true;
	}

	/// <summary> Can the parts be tinted yet (the boss waits until it has finished appearing). </summary>
	protected virtual bool TintReady => true;
	/// <summary> After a renderer got its tint this frame (the boss re-lights its eye here). </summary>
	protected virtual void Tinted( Part part, MeshInstance3D renderer, Color tint ) { }

	/// <summary> Every frame: darkens hurt parts, flashes ones just hit, and tracks how fast each part is moving. </summary>
	protected void TintParts( float dt )
	{
		foreach ( var part in parts )
		{
			if ( part is null || part.Gone ) continue;
			var flash = MathX.Clamp( 1f - part.Flash / 0.1f, 0f, 1f );
			foreach ( var (renderer, tint) in part.Renderers )
			{
				if ( !IsInstanceValid( renderer ) || !TintReady ) continue;
				var color = tint.Lerp( tint * 0.5f, part.Hurt ) with { A = 1f };
				Shapes.Tint( renderer, color.Lerp( Colors.White * 3f, flash ) );
				Tinted( part, renderer, tint );
			}
			var position = part.Go.GlobalPosition;
			part.Velocity = (position - part.LastPosition) / MathF.Max( dt, 0.0001f );
			part.LastPosition = position;
		}
	}

	/// <summary>
	/// A part (and what hangs off it) comes loose as a physics piece, its hit capsules made solid. Push in s&box
	/// units/s; the gib's radius is in inches.
	/// </summary>
	protected Node3D Detach( Part part, Vector3 push, float spin, float gibRadius, float gibHealth, float gibMass, bool squareUp = true )
	{
		var source = part.Go;
		var piece = new Node3D { Name = $"{Kind} Piece" };
		GetTree().CurrentScene.AddChild( piece );
		piece.GlobalTransform = squareUp ? source.GlobalTransform.Orthonormalized() : source.GlobalTransform;
		var shapes = new List<(Shape3D Shape, Transform3D Global)>();
		foreach ( var shape in source.FindChildren( "*", "CollisionShape3D", true, false ).OfType<CollisionShape3D>() )
			shapes.Add( (shape.Shape, shape.GlobalTransform) );
		foreach ( var child in source.GetChildren().OfType<Node3D>().ToList() )
			if ( child is not CollisionShape3D ) child.Reparent( piece, true );
		// Sub-parts (a shin under a thigh) are bodies themselves: strip their bodies, keep their meshes.
		foreach ( var sub in piece.FindChildren( "*", "StaticBody3D", true, false ).OfType<CreaturePart>().ToList() )
		{
			var holder = new Node3D { Name = sub.Name };
			sub.GetParent().AddChild( holder );
			holder.GlobalTransform = sub.GlobalTransform;
			foreach ( var c in sub.GetChildren().OfType<Node3D>().ToList() ) if ( c is not CollisionShape3D ) c.Reparent( holder, true );
			sub.QueueFree();
		}
		source.QueueFree();
		var gib = Gib.Attach( piece, gore, gibRadius, gibHealth, gibMass, part.Velocity * 0.4f + push * U.In, GibGoo, addCollider: shapes.Count == 0 );
		foreach ( var (shape, global) in shapes )
		{
			var cs = new CollisionShape3D { Shape = shape };
			gib.AddChild( cs );
			cs.GlobalTransform = global.Orthonormalized();
		}
		gib.AngularVelocity = Rng.InSphere( spin );
		return piece;
	}
}
