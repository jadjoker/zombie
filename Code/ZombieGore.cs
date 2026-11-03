/// <summary>
/// Hit zones and dismemberment for a zombie built from a hidden, animated citizen with a bone-merged mesh.
/// The body is split into segments (see BodySegments). Cutting a segment takes it and everything
/// below it off as a physics gib; the zombie shrinks those bones away (bone override while alive,
/// frozen ragdoll bodies once dead). Enough overkill bursts the whole body into separate pieces.
/// </summary>
public sealed class ZombieGore : Component
{
	[Property] public float HeadshotMultiplier { get; set; } = 2.5f;
	/// <summary> How much limb hits hurt the zombie overall. Low, so you can strip limbs before it drops. </summary>
	[Property] public float LimbMultiplier { get; set; } = 0.5f;
	/// <summary>
	/// Damage scale for arms and legs coming off. Fixed rather than tied to zombie health,
	/// so tougher zombies take longer to kill but still lose limbs just as easily.
	/// </summary>
	[Property] public float LimbToughness { get; set; } = 70f;
	/// <summary> Chance a torso hit knocks a chunk of flesh loose. </summary>
	[Property] public float TorsoChunkChance { get; set; } = 0.55f;
	/// <summary> When a head comes off, the chance it bursts instead of falling. </summary>
	[Property] public float HeadBurstChance { get; set; } = 0.5f;
	/// <summary> Most wound decals one zombie carries; the oldest go first. </summary>
	[Property] public int MaxWounds { get; set; } = 14;
	[Property] public float LimbChunkChance { get; set; } = 0.15f;
	/// <summary> Corpses come apart this much more easily. </summary>
	[Property] public float CorpseMultiplier { get; set; } = 2f;
	[Property] public float BulletImpulse { get; set; } = 180f;

	public BodySegments Map { get; private set; }
	public bool Exploded { get; private set; }
	public IReadOnlySet<Segment> Attached => attached;
	public bool IsCut( Segment s ) => !attached.Contains( s );

	readonly HashSet<Segment> attached = new( BodySegments.All );
	readonly Dictionary<Segment, float> damage = new();
	readonly Dictionary<Segment, List<Rigidbody>> frozenBodies = new();
	/// <summary> Mesh-only bones (jaw, pinkies) per cut, pinned to that cut's citizen root bone. </summary>
	readonly List<(string CitizenBone, int[] MeshBones)> meshPins = new();
	readonly List<Segment> cutRoots = new();

	float referenceHealth = 100f;
	float corpseDamage;
	bool ragdollCollapsed;
	bool pooled;
	TimeSince timeSinceDeath;
	bool sawDeath;
	(Vector3 Position, Vector3 Impulse)? pendingImpulse;

	SkinnedModelRenderer Citizen => GetComponent<SkinnedModelRenderer>();
	SkinnedModelRenderer Mesh => GameObject.Children.FirstOrDefault( c => c.Name == "Mesh" )?.GetComponent<SkinnedModelRenderer>();
	SkinnedModelRenderer Visible => Mesh ?? Citizen;
	ModelPhysics Ragdoll => GetComponent<ModelPhysics>();
	Health Health => GetComponent<Health>();
	GoreSystem Gore => GoreSystem.Get( Scene );

	protected override void OnStart()
	{
		referenceHealth = Health?.MaxHealth ?? 100f;
		Map = new BodySegments( Citizen.Model, Mesh?.Model );

		// Bone objects let wounds ride along on the animated body.
		Citizen.CreateBoneObjects = true;
	}

	// ---- Progressive damage -------------------------------------------------

	readonly Queue<GameObject> wounds = new();
	bool jawGone, gutsSpilled, ribsTorn;
	TimeSince timeSinceDrip;

	/// <summary> Visible damage that builds up as a torso or head segment takes hits. </summary>
	void Progress( Segment segment, Vector3 position, Vector3 direction, float ratio )
	{
		var isHead = segment == Segment.Head;
		if ( !isHead && !Map.IsTorso( segment ) ) return;

		// Torso stages are measured against the zombie's health (not the much higher split threshold),
		// so they happen while it's still on its feet.
		var hurt = damage.GetValueOrDefault( segment ) / MathF.Max( 1f, referenceHealth );

		AddWound( segment, position, direction, MathX.Clamp( 1.2f + ratio * 0.8f, 1.2f, 2.2f ) );
		var gore = Gore;

		// Head: the first solid hit tears the jaw out and wrecks the face.
		if ( isHead && !jawGone && ratio >= 0.3f )
		{
			jawGone = true;
			TearJaw();
			var mouth = Citizen.SceneModel.GetBoneWorldTransform( "head" ).Position + Vector3.Down * 3f;
			gore?.Burst( mouth, direction, 1.2f );
			gore?.Spray( mouth, direction, 4, 160f );
			gore?.ThrowChunk( mouth, direction );
			AddWound( segment, position + Vector3.Down * 4f, direction, 1.4f );
		}

		// Belly: a badly hurt gut spills open while it keeps walking.
		if ( segment == Segment.Belly && !gutsSpilled && hurt >= 0.25f )
		{
			gutsSpilled = true;
			var belly = Citizen.SceneModel.GetBoneWorldTransform( "spine_0" ).Position;
			gore?.ThrowMeat( belly, direction, 2, organs: true );
			gore?.Burst( belly, direction, 1.5f );
			gore?.Spray( belly, Vector3.Down, 5, 120f, 0.8f, 1.6f );
		}

		// Chest: ribs tear out.
		if ( segment == Segment.Chest && !ribsTorn && hurt >= 0.25f )
		{
			ribsTorn = true;
			var chest = Citizen.SceneModel.GetBoneWorldTransform( "spine_2" ).Position;
			gore?.ThrowChunk( chest, direction );
			gore?.ThrowChunk( chest, direction + Vector3.Up );
			gore?.Burst( chest, direction, 1.4f );
		}
	}

	/// <summary> A blood stain with a flesh wound on top, stuck to the nearest bone so it moves with the body. </summary>
	void AddWound( Segment segment, Vector3 position, Vector3 direction, float size )
	{
		var gore = Gore;
		var citizen = Citizen;
		if ( gore is null || !citizen.IsValid() || direction.IsNearlyZero() ) return;

		var bones = segment switch
		{
			Segment.Head => new[] { "head" },
			Segment.Chest => new[] { "spine_2", "neck_0", "clavicle_L", "clavicle_R" },
			_ => new[] { "spine_0", "spine_1", "pelvis" },
		};
		var bone = bones
			.Select( b => citizen.GetBoneObject( b ) )
			.Where( b => b.IsValid() )
			.OrderBy( b => b.WorldPosition.DistanceSquared( position ) )
			.FirstOrDefault();
		if ( bone is null ) return;

		var dir = direction.Normal;
		void Stamp( List<DecalDefinition> set, float scale )
		{
			if ( set.Count == 0 ) return;
			var go = new GameObject( bone, true, "Wound" );
			go.WorldPosition = position - dir * 14f;
			go.WorldRotation = Rotation.LookAt( dir ) * Rotation.FromRoll( Game.Random.Float( 0f, 360f ) );
			var decal = go.AddComponent<Decal>();
			decal.Decals = new() { Game.Random.FromList( set ) };
			decal.Size = new Vector2( 1f, 1f );
			decal.Scale = scale;
			decal.Depth = 24f;
			wounds.Enqueue( go );
		}

		Stamp( gore.BloodDecals, size * Game.Random.Float( 1.0f, 1.3f ) );
		Stamp( gore.WoundDecals, size * Game.Random.Float( 0.4f, 0.55f ) );

		while ( wounds.Count > MaxWounds * 2 )
		{
			var old = wounds.Dequeue();
			if ( old.IsValid() ) old.Destroy();
		}
	}

	/// <summary> Collapses the mesh's jaw (teeth and mouth) into the head, leaving a ruined lower face. </summary>
	void TearJaw()
	{
		var mesh = Mesh;
		if ( !mesh.IsValid() ) return;
		var jaw = mesh.Model.Bones.AllBones.Where( b => b.Name.StartsWith( "jaw" ) ).Select( b => b.Index ).ToArray();
		if ( jaw.Length > 0 ) meshPins.Add( ("head", jaw) );
	}

	// ---- Hit zones --------------------------------------------------------

	/// <summary> Which attached segment a citizen bone belongs to. Cut segments can't be hit (their hitboxes are shrunk). </summary>
	public Segment SegmentForBone( string boneName )
	{
		if ( Map is null || string.IsNullOrEmpty( boneName ) || !Citizen.Model.Bones.HasBone( boneName ) ) return Segment.Pelvis;
		var s = Map.SegmentForCitizenBone( boneName );
		while ( !attached.Contains( s ) && Map.Defs[s].Parent is { } parent ) s = parent;
		return s;
	}

	public float DamageMultiplier( Segment s ) => s switch
	{
		Segment.Head => HeadshotMultiplier,
		Segment.Pelvis or Segment.Belly or Segment.Chest => 1f,
		_ => LimbMultiplier,
	};

	/// <summary>
	/// Records a bullet on a segment. Cuts it once it has taken enough, and bursts the whole body
	/// on heavy overkill. Returns true if anything came off.
	/// </summary>
	public bool AddDamage( Segment segment, float amount, Vector3 position, Vector3 direction, bool killingShot )
	{
		if ( Map is null || Exploded ) return false;

		var gore = Gore;
		var isCorpse = Health?.IsDead ?? false;
		var onCorpse = isCorpse && !killingShot;

		pendingImpulse = (position, direction * BulletImpulse * (killingShot ? 2.5f : 1f));
		if ( onCorpse ) corpseDamage += amount * CorpseMultiplier;

		// Full gib: one huge shot, or a corpse that keeps getting shot.
		var bigShot = killingShot && amount >= referenceHealth * (gore?.ExplodeSingleShot ?? 1.4f);
		var shredded = corpseDamage >= referenceHealth * (gore?.ExplodeCorpseDamage ?? 3.2f);
		if ( bigShot || shredded )
		{
			Explode( position, direction );
			return true;
		}

		// Flesh gets knocked loose as you shoot, so there's gore with every burst, not just at the end.
		var torsoHit = Map.IsTorso( segment );
		if ( Game.Random.Float() < (torsoHit ? TorsoChunkChance : segment == Segment.Head ? 0f : LimbChunkChance) )
			gore?.ThrowChunk( position, direction );

		// The pelvis is the root and can't come off; shots there go towards splitting at the waist.
		if ( segment == Segment.Pelvis ) segment = Segment.Belly;
		if ( !attached.Contains( segment ) ) return false;

		// Headshots hit the head's own toughness harder, so one still takes it off.
		var goreAmount = segment == Segment.Head ? amount * HeadshotMultiplier : amount;
		damage[segment] = damage.GetValueOrDefault( segment ) + goreAmount * (onCorpse ? CorpseMultiplier : 1f);

		// Limbs use a fixed toughness; head and torso scale with the zombie's health, so splits stay finishers.
		var def = Map.Defs[segment];
		var scale = torsoHit || segment == Segment.Head ? referenceHealth : MathF.Min( referenceHealth, LimbToughness );
		var threshold = scale * def.HealthFraction;

		// Damage shows on the body before anything comes off: wounds, a torn-out jaw, spilled guts.
		Progress( segment, position, direction, damage[segment] / threshold );

		var overkill = killingShot && goreAmount >= threshold * 0.5f;
		if ( damage[segment] < threshold && !overkill ) return false;

		// Variety: sometimes the limb gives way one joint higher than where it was hit.
		if ( def.Parent is { } parent && !Map.IsTorso( parent ) && attached.Contains( parent )
			&& Game.Random.Float() < (gore?.CutHigherChance ?? 0f) )
		{
			segment = parent;
		}

		Cut( segment, position, direction );
		return true;
	}

	// ---- Cutting ----------------------------------------------------------

	public void Cut( Segment segment, Vector3 hitPosition, Vector3 direction )
	{
		if ( Map is null || segment == Segment.Pelvis || !attached.Contains( segment ) ) return;

		var citizen = Citizen;
		var def = Map.Defs[segment];
		var piece = Map.Subtree( segment, attached );

		var gib = SpawnGib( piece, direction, fling: 1f, allowMidAirSplit: true );

		// A head either falls off or bursts.
		if ( segment == Segment.Head && gib.IsValid() && Game.Random.Float() < HeadBurstChance )
		{
			var at = gib.WorldPosition;
			gib.Pulverize( at, direction );
			Gore?.Burst( at, Vector3.Up, 1.8f );
			Gore?.Spray( at, direction, 8, 220f, 0.6f, 1.5f );
			Gore?.ThrowChunk( at, direction );
			Gore?.ThrowChunk( at, Vector3.Up );
		}

		attached.ExceptWith( piece );
		cutRoots.Add( segment );
		PinMeshOnlyBones( piece, def.RootBone );

		if ( Ragdoll.IsValid() && Ragdoll.PhysicsWereCreated )
		{
			FreezeOnRagdoll( piece, def );
		}
		else
		{
			var bone = citizen.Model.Bones.GetBone( def.RootBone );
			var collapsed = citizen.SceneModel.GetBoneLocalTransform( bone.Index ).WithScale( 0.001f );
			citizen.SceneModel.SetBoneOverride( bone.Index, in collapsed );
		}

		var stump = StumpPosition( def );
		var gore = Gore;
		gore?.Burst( stump, -direction, 1.3f );
		gore?.Burst( stump, Vector3.Up, 1f );
		gore?.Spray( stump, direction, 5 );
		gore?.Spray( stump, Vector3.Up, 3, 120f );

		// Torso cuts spill organs.
		if ( segment is Segment.Belly or Segment.Chest )
		{
			gore?.ThrowMeat( stump, direction, Game.Random.Int( 1, 2 ), organs: true );
			if ( Game.Random.Float() < 0.6f ) gore?.ThrowMeat( stump, -direction, 1, organs: false );
		}

		if ( def.Lethal )
		{
			Kill( hitPosition, direction );
			return;
		}

		// Losing a leg doesn't stop it: it drops and crawls. Losing a foot makes it limp.
		var zombie = GetComponent<Zombie>();
		if ( zombie.IsValid() && !zombie.IsDead )
		{
			var lostLeg = piece.Any( s => s is Segment.ThighL or Segment.ThighR or Segment.ShinL or Segment.ShinR );
			var lostFoot = piece.Any( s => s is Segment.FootL or Segment.FootR );
			if ( lostLeg ) zombie.BecomeCrawler();
			else if ( lostFoot ) zombie.Limp();
		}
	}

	/// <summary> Bursts every remaining piece of the body apart at once. </summary>
	public void Explode( Vector3 position, Vector3 direction )
	{
		if ( Exploded || Map is null ) return;
		Exploded = true;

		var center = Citizen.SceneModel.GetBoneWorldTransform( "spine_0" ).Position;
		foreach ( var s in attached.ToList() )
			SpawnGib( new HashSet<Segment> { s }, direction, fling: 1.8f, allowMidAirSplit: false, burstFrom: center );
		attached.Clear();

		// Hide what's left of the zombie and stop it catching bullets.
		if ( Mesh.IsValid() ) Mesh.Enabled = false;
		Citizen.Enabled = false;
		var hitboxes = GetComponent<ModelHitboxes>();
		if ( hitboxes.IsValid() ) hitboxes.Enabled = false;
		if ( Ragdoll.IsValid() )
		{
			foreach ( var b in Ragdoll.Bodies.Where( b => b.Component.IsValid() ) )
			{
				b.Component.MotionEnabled = false;
				foreach ( var c in b.Component.GameObject.Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ) ) c.Enabled = false;
			}
		}

		var gore = Gore;
		gore?.Burst( center, direction, 2.2f );
		gore?.Burst( center, Vector3.Up, 1.8f );
		gore?.Burst( center, -direction, 1.5f );
		gore?.Spray( center, direction, 14, 260f, 0.7f, 2f );
		gore?.Spray( center, Vector3.Up, 6, 200f );
		gore?.ThrowMeat( center, direction, Game.Random.Int( 2, 3 ), organs: true );
		gore?.ThrowMeat( center, direction, Game.Random.Int( 2, 4 ), organs: false );
		gore?.Pool( center );

		Kill( position, direction );
	}

	void Kill( Vector3 position, Vector3 direction )
	{
		if ( Health is { IsDead: false } health )
		{
			health.OnDamage( new DamageInfo
			{
				Damage = health.Current + 1f,
				Position = position,
				Origin = position - direction * 50f,
				Attacker = GameObject,
			} );
		}
	}

	/// <summary> Captures the zombie's current pose and spawns those segments as a gib. </summary>
	Gib SpawnGib( HashSet<Segment> piece, Vector3 direction, float fling, bool allowMidAirSplit, Vector3? burstFrom = null )
	{
		var visible = Visible;
		var citizen = Citizen;
		if ( visible?.SceneModel is null || piece.Count == 0 ) return null;

		var boneCount = visible.Model.Bones.AllBones.Count();
		var meshWorld = new Transform[boneCount];
		for ( var i = 0; i < boneCount; i++ ) meshWorld[i] = visible.SceneModel.GetBoneWorldTransform( i );
		var caps = BodySegments.All.ToDictionary( s => s, s => Map.Capsule( s, citizen.SceneModel ) );

		var root = Map.RootOf( piece );
		var centre = (caps[root].From + caps[root].To) * 0.5f;
		var outward = burstFrom is { } from ? (centre - from).WithZ( 0 ).Normal : Vector3.Zero;
		var velocity = ((direction.WithZ( 0 ).Normal + outward * 1.5f).Normal * Game.Random.Float( 140f, 260f )
			+ Vector3.Up * Game.Random.Float( 110f, 230f )) * fling;
		var spin = Vector3.Random * Game.Random.Float( 6f, 16f ) * fling;

		return Gib.CreateSkinned( Scene, Gore, Map, visible.Tint, meshWorld, caps, piece, velocity, spin, allowMidAirSplit );
	}

	Vector3 StumpPosition( SegmentDef def )
	{
		var citizen = Citizen;
		var parentBone = citizen.Model.Bones.GetBone( def.RootBone )?.Parent;
		return citizen.SceneModel.GetBoneWorldTransform( parentBone?.Name ?? def.RootBone ).Position;
	}

	/// <summary> Freezes a cut piece's ragdoll bodies at the stump and shrinks them away. </summary>
	void FreezeOnRagdoll( HashSet<Segment> piece, SegmentDef rootDef )
	{
		var ragdoll = Ragdoll;
		if ( !ragdoll.IsValid() ) return;

		var names = piece.SelectMany( s => Map.Defs[s].RagdollBodies ).ToHashSet();
		bool InPiece( ModelPhysics.Body b ) => b.Component.IsValid() && names.Contains( b.Component.GameObject.Name );

		foreach ( var joint in ragdoll.Joints )
		{
			if ( joint.Component.IsValid() && (InPiece( joint.Body1 ) || InPiece( joint.Body2 )) )
				joint.Component.Enabled = false;
		}

		var stump = StumpPosition( rootDef );
		var list = frozenBodies.TryGetValue( rootDef.Segment, out var existing ) ? existing : frozenBodies[rootDef.Segment] = new();
		foreach ( var b in ragdoll.Bodies.Where( InPiece ) )
		{
			var rb = b.Component;
			list.Add( rb );
			rb.MotionEnabled = false;
			foreach ( var c in rb.GameObject.Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ) ) c.Enabled = false;
			rb.GameObject.WorldPosition = stump;
			rb.GameObject.WorldScale = 0.001f;
		}
	}

	void PinMeshOnlyBones( HashSet<Segment> piece, string citizenRootBone )
	{
		var mesh = Mesh;
		if ( !mesh.IsValid() ) return;
		var indices = mesh.Model.Bones.AllBones
			.Where( b => !Citizen.Model.Bones.HasBone( b.Name ) && piece.Contains( Map.MeshBoneSegment[b.Index] ) )
			.Select( b => b.Index )
			.ToArray();
		if ( indices.Length > 0 ) meshPins.Add( (citizenRootBone, indices) );
	}

	// ---- Per frame --------------------------------------------------------

	protected override void OnUpdate()
	{
		if ( Map is null ) return;

		if ( Health is { IsDead: true } && !sawDeath )
		{
			sawDeath = true;
			timeSinceDeath = 0f;
		}

		// Badly hurt zombies leave a blood trail.
		var badlyHurt = jawGone || gutsSpilled || ribsTorn || wounds.Count >= 6 || cutRoots.Count > 0;
		if ( !sawDeath && badlyHurt && timeSinceDrip > 0.35f )
		{
			timeSinceDrip = 0f;
			Gore?.Drip( WorldPosition + Vector3.Up * 30f + Vector3.Random.WithZ( 0 ) * 8f );
		}

		if ( sawDeath && !pooled && timeSinceDeath > 1.2f )
		{
			pooled = true;
			if ( !Exploded ) Gore?.Pool( Citizen.SceneModel.GetBoneWorldTransform( "pelvis" ).Position );
		}

		var ragdoll = Ragdoll;
		if ( !ragdoll.IsValid() || !ragdoll.PhysicsWereCreated || Exploded ) return;

		// The ragdoll takes over the bones, so pieces cut while alive are collapsed again on it.
		if ( !ragdollCollapsed )
		{
			ragdollCollapsed = true;
			foreach ( var root in cutRoots ) FreezeOnRagdoll( Map.Subtree( root, BodySegments.All.ToHashSet() ), Map.Defs[root] );
		}

		if ( pendingImpulse is { } push )
		{
			pendingImpulse = null;
			var nearest = ragdoll.Bodies
				.Where( b => b.Component.IsValid() && b.Component.MotionEnabled )
				.OrderBy( b => b.Component.WorldPosition.DistanceSquared( push.Position ) )
				.Select( b => b.Component.PhysicsBody )
				.FirstOrDefault();
			nearest?.ApplyImpulseAt( push.Position, push.Impulse * nearest.Mass );
		}

		// Frozen bodies don't follow the corpse, so keep them glued to their stumps.
		foreach ( var (segment, bodies) in frozenBodies )
		{
			var stump = StumpPosition( Map.Defs[segment] );
			foreach ( var rb in bodies )
				if ( rb.IsValid() ) rb.GameObject.WorldPosition = stump;
		}
	}

	protected override void OnPreRender()
	{
		if ( meshPins.Count == 0 || Exploded || Map is null ) return;
		var mesh = Mesh;
		var citizen = Citizen;
		if ( !mesh.IsValid() || mesh.SceneModel is null || !citizen.IsValid() || citizen.SceneModel is null ) return;

		// Bone merge drives shared bones, but mesh-only ones keep their own pose; pin them to the cut.
		foreach ( var (bone, indices) in meshPins )
		{
			var at = citizen.SceneModel.GetBoneWorldTransform( bone );
			var local = mesh.WorldTransform.ToLocal( new Transform( at.Position, at.Rotation, 0.0005f ) );
			foreach ( var i in indices ) mesh.SceneModel.SetBoneOverride( i, in local );
		}
	}
}
