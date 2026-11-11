using Godot;

/// <summary>
/// What shooting the Kraken does to it (playtest round 4). Every hit leaves a bullet hole that spurts goo in beats
/// and may knock a lump off. As its health falls, whole pieces of hide tear away where it was shot and leave raw
/// craters that keep pumping; near death they never stop.
/// </summary>
public partial class Kraken
{
	sealed class Wound
	{
		public Node3D At;
		public TimeSince Age;
		public float Life, Size;
		public bool Crater;
	}

	readonly List<Wound> wounds = new();
	TimeSince sinceWoundBeat, sinceHole;
	/// <summary> The share of its health at which the next piece of hide tears off. </summary>
	float nextCrater = 0.88f;
	const int MaxHoles = 14;
	static readonly Color Flesh = new( 0.72f, 0.28f, 0.4f );

	/// <summary> For tests: bullet holes bleeding now, and craters torn in its head so far. </summary>
	public int BleedingHoles => wounds.Count( w => !w.Crater && IsInstanceValid( w.At ) );
	public int Craters { get; private set; }

	/// <summary> A shot landed on a part: a spurting hole, and sometimes a lump knocked off. </summary>
	void Bloody( Node3D on, Vector3 position, Vector3 direction, Color skin, bool big )
	{
		var outward = -direction.SafeNormal();
		if ( Rng.Float() < (big ? 0.45f : 0.3f) )
		{
			var lump = gore.ThrowBlob( position, outward + Vector3.Up * 0.4f, Rng.Float() < 0.5f ? skin : Flesh, Goo, Rng.Float( 3f, 6.5f ) );
			if ( lump is not null ) lump.LinearVelocity *= 1.6f;
		}
		// A shotgun blast is one mess, not nine holes.
		if ( sinceHole < 0.07f ) return;
		sinceHole = 0f;
		var hole = CreatureGore.AddStump( on, position, outward, big ? 4.5f : 3.2f, skin, Goo, 4f );
		if ( hole is null ) return;
		wounds.Add( new Wound { At = hole, Age = 0f, Life = Rng.Float( 3.5f, 5.5f ), Size = big ? 0.45f : 0.3f } );
		// The oldest holes close up to make room.
		while ( wounds.Count( w => !w.Crater ) > MaxHoles )
		{
			var oldest = wounds.First( w => !w.Crater );
			if ( IsInstanceValid( oldest.At ) ) oldest.At.QueueFree();
			wounds.Remove( oldest );
		}
	}

	/// <summary> After a hit on the head or brain: has it lost enough to tear a piece of hide off where it was shot. </summary>
	void TearIfHurt( Vector3 position, Vector3 direction )
	{
		while ( !IsDead && health.Fraction < nextCrater && nextCrater > 0.05f )
		{
			nextCrater -= 0.11f;
			Tear( position + Rng.InSphere( 0.5f ), direction );
		}
	}

	/// <summary> A slab of hide rips away: a real chunk flies off, and a raw crater is left pumping goo. </summary>
	void Tear( Vector3 position, Vector3 direction )
	{
		if ( !Has( Head ) ) return;
		Craters++;
		var head = parts[Head].Go;
		var middle = head.GlobalTransform * new Vector3( 0f, 1.9f, 0f );
		var outward = (position - middle).SafeNormal();
		if ( outward == Vector3.Zero ) outward = -direction.SafeNormal();
		// Onto the skin there, not floating at the bullet's position.
		var at = middle + outward * 0.9f * Size;
		var crater = CreatureGore.AddStump( head, at, outward, Rng.Float( 15f, 21f ), Skin, Goo, 16f );
		if ( crater is not null ) wounds.Add( new Wound { At = crater, Age = 0f, Life = 22f, Size = 1f, Crater = true } );
		var basis = head.GlobalBasis.Orthonormalized() * new Basis( Rng.OnSphere(), Rng.Float( 0f, MathF.Tau ) );
		if ( SpawnChunk( Rng.Int( 6, 35 ), new Transform3D( basis.Scaled( Vector3.One * Size * 0.6f ), Vector3.Zero ), at + outward * 0.4f, 60f ) is { } torn )
		{
			torn.Gib.LinearVelocity = (outward * 2f + Vector3.Up) * Rng.Float( 2.5f, 4.5f );
			torn.Gib.AngularVelocity = Rng.InSphere( 7f );
		}
		for ( var i = 0; i < 3; i++ ) gore.ThrowBlob( at, outward + Rng.OnSphere() * 0.6f, i == 0 ? Skin : Flesh, Goo, Rng.Float( 4f, 7f ) );
		for ( var i = 0; i < 5; i++ ) gore.GooBurst( at, outward + Rng.InSphere( 0.5f ), Goo, 10, 460f, Rng.Float( 3f, 5.5f ) );
		gore.GooSpray( at, outward, 8, Goo, 420f, 1.6f, 3.4f );
		gore.Squelch( at, 1.5f, 0.7f );
		Sfx.Play( "gib", at, 1f, 0.6f );
		KickHead( direction, 110f );
		ShakeNear( 0.3f );
		for ( var k = 0; k < Tentacles; k++ ) Thrash( k, 120f );
	}

	/// <summary>
	/// One of the head chunk models as a loose physics piece. Its model is in the head's own space, so under the
	/// head's transform it starts exactly where it sat; give a point to put its middle there instead. Returns the
	/// piece and where its middle is; the caller sets it moving.
	/// </summary>
	(Gib Gib, Vector3 Middle)? SpawnChunk( int index, Transform3D frame, Vector3? moveTo, float gibHealth )
	{
		if ( !ResourceLoader.Exists( $"res://assets/models/creatures/kraken/chunk{index}.glb" ) ) return null;
		var piece = new Node3D { Name = "Kraken Chunk" };
		GetTree().CurrentScene.AddChild( piece );
		var model = Model( $"chunk{index}" );
		piece.AddChild( model );
		Aabb? box = null;
		foreach ( var mesh in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			mesh.MaterialOverride = Paint( mesh.Name.ToString(), out _ );
			box = box is { } b ? b.Merge( mesh.GetAabb() ) : mesh.GetAabb();
		}
		if ( box is not { } bounds ) { piece.QueueFree(); return null; }
		var middle = frame * bounds.GetCenter();
		if ( moveTo is { } target ) { frame.Origin += target - middle; middle = target; }
		piece.GlobalTransform = frame;
		var radius = MathF.Max( 0.18f, bounds.Size.Length() * 0.22f * frame.Basis.Scale.X );
		var gib = Gib.Attach( piece, gore, radius / U.In, gibHealth, 4f + radius * 25f, Vector3.Zero, Goo, addCollider: false );
		var collider = new CollisionShape3D { Shape = new SphereShape3D { Radius = radius } };
		gib.AddChild( collider );
		collider.GlobalPosition = middle;
		return (gib, middle);
	}

	/// <summary> Every wound spurts in beats: bullet holes for a few seconds, craters for much longer (for good, near death). </summary>
	void UpdateWounds()
	{
		if ( wounds.Count == 0 || sinceWoundBeat < 0.1f ) return;
		sinceWoundBeat = 0f;
		var dying = IsDead || health.Fraction < 0.35f;
		wounds.RemoveAll( w => !IsInstanceValid( w.At ) || (w.Age > w.Life && !(w.Crater && dying)) );
		foreach ( var wound in wounds )
		{
			var age = (float)wound.Age;
			var beat = MathF.Max( 0f, MathF.Sin( age * 8f + wound.Size * 9f ) );
			var fade = wound.Crater && dying ? 1f : MathX.Clamp( 1f - age / wound.Life, 0f, 1f );
			if ( beat < 0.35f && !wound.Crater ) continue;
			var outward = -wound.At.GlobalBasis.Z.Normalized();
			var at = wound.At.GlobalPosition + outward * 0.06f;
			var drops = wound.Crater ? (beat > 0.6f ? 5 : 2) : (beat > 0.8f ? 3 : 1);
			gore.GooBurst( at, outward * 2.5f + Vector3.Up * 0.25f, Goo, drops, (wound.Crater ? 420f : 340f) * (0.3f + beat) * (0.35f + 0.65f * fade), wound.Crater ? 3.2f : 1.5f );
			if ( wound.Crater && beat > 0.9f && Rng.Float() < 0.35f && gore.TraceWorld( at, at + Vector3.Down * 14f ) is { } floor )
				gore.GooSplat( floor.Position + Rng.InSphere( 0.8f ).Flat(), floor.Normal, Rng.Float( 1.2f, 2.6f ), Goo );
		}
	}
}
