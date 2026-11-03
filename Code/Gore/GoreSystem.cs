/// <summary>
/// Scene-wide gore settings and helpers: gib budget, blood decals and meat chunks.
/// Put one in the scene (on the Game object). Asset fields are set in the inspector
/// so the scene records which packages it uses.
/// </summary>
public sealed class GoreSystem : Component
{
	[Property, Group( "Gibs" )] public float GibLifetime { get; set; } = 20f;
	[Property, Group( "Gibs" )] public int MaxGibs { get; set; } = 120;
	[Property, Group( "Gibs" )] public float FadeTime { get; set; } = 1.2f;
	/// <summary> Chance a freshly cut multi-part gib breaks again in mid-air. </summary>
	[Property, Group( "Gibs" )] public float MidAirSplitChance { get; set; } = 0.35f;
	/// <summary> Chance a limb cut happens one joint higher than where it was hit. </summary>
	[Property, Group( "Gibs" )] public float CutHigherChance { get; set; } = 0.25f;
	/// <summary> Corpse damage, as a multiple of the zombie's health, that bursts it into every piece. </summary>
	[Property, Group( "Gibs" )] public float ExplodeCorpseDamage { get; set; } = 3.2f;
	/// <summary> A single shot this big (times health) bursts the zombie outright. </summary>
	[Property, Group( "Gibs" )] public float ExplodeSingleShot { get; set; } = 1.4f;

	[Property, Group( "Blood" )] public GameObject BloodBurst { get; set; }
	[Property, Group( "Blood" )] public List<DecalDefinition> BloodDecals { get; set; } = new();
	[Property, Group( "Blood" )] public List<DecalDefinition> WoundDecals { get; set; } = new();
	[Property, Group( "Blood" )] public float DecalLifetime { get; set; } = 40f;
	[Property, Group( "Blood" )] public int MaxDecals { get; set; } = 200;

	[Property, Group( "Meat" )] public List<Model> OrganModels { get; set; } = new();
	[Property, Group( "Meat" )] public List<Model> MeatModels { get; set; } = new();

	readonly List<Gib> gibs = new();
	readonly Queue<GameObject> decals = new();

	public static GoreSystem Get( Scene scene ) => scene?.Get<GoreSystem>();

	// ---- Gib budget -------------------------------------------------------

	public void Register( Gib gib )
	{
		gibs.RemoveAll( g => !g.IsValid() );
		gibs.Add( gib );

		// Over budget: fade the oldest pieces that aren't already going.
		var excess = gibs.Count( g => !g.IsFading ) - MaxGibs;
		foreach ( var old in gibs.Where( g => !g.IsFading ).Take( Math.Max( 0, excess ) ).ToList() )
			old.BeginFade();
	}

	public int LiveGibs => gibs.Count( g => g.IsValid() );

	// ---- Blood ------------------------------------------------------------

	public void Burst( Vector3 position, Vector3 direction, float scale = 1f )
	{
		if ( !BloodBurst.IsValid() ) return;
		var fx = BloodBurst.Clone( position, Rotation.LookAt( direction.IsNearlyZero() ? Vector3.Up : direction ) );
		fx.WorldScale = scale;
		fx.AddComponent<SelfDestruct>().Lifetime = 3f;
	}

	/// <summary> Sprays blood from a point: decals wherever the droplets land on the world. </summary>
	public void Spray( Vector3 origin, Vector3 direction, int drops, float reach = 180f, float sizeMin = 0.5f, float sizeMax = 1.3f )
	{
		for ( var i = 0; i < drops; i++ )
		{
			// A cone around the direction, pulled down by gravity.
			var dir = (direction.Normal + Vector3.Random * 0.7f + Vector3.Down * Game.Random.Float( 0.2f, 0.9f )).Normal;
			if ( TraceWorld( origin, origin + dir * reach ) is { } hit )
				Splat( hit.Position, hit.Normal, Game.Random.Float( sizeMin, sizeMax ) );
		}
	}

	/// <summary> A pool spreading under a body. </summary>
	public void Pool( Vector3 position )
	{
		if ( TraceWorld( position + Vector3.Up * 20f, position + Vector3.Down * 120f ) is { } hit )
			Splat( hit.Position, hit.Normal, Game.Random.Float( 2.2f, 3.4f ) );
	}

	/// <summary> A small drip straight down, for trails behind flying chunks. </summary>
	public void Drip( Vector3 position )
	{
		if ( TraceWorld( position, position + Vector3.Down * 200f ) is { } hit )
			Splat( hit.Position, hit.Normal, Game.Random.Float( 0.25f, 0.55f ) );
	}

	public void Splat( Vector3 position, Vector3 normal, float scale, bool wound = false )
	{
		var set = wound && WoundDecals.Count > 0 ? WoundDecals : BloodDecals;
		if ( set.Count == 0 ) return;

		var go = new GameObject( true, "Blood" );
		go.WorldPosition = position + normal * 1f;
		go.WorldRotation = Rotation.LookAt( -normal ) * Rotation.FromRoll( Game.Random.Float( 0f, 360f ) );

		var decal = go.AddComponent<Decal>();
		decal.Decals = new() { Game.Random.FromList( set ) };
		decal.Size = new Vector2( 1f, 1f );
		decal.Scale = scale;
		decal.Depth = 10f;
		decal.Transient = true;
		go.AddComponent<SelfDestruct>().Lifetime = DecalLifetime;

		decals.Enqueue( go );
		while ( decals.Count > MaxDecals )
		{
			var old = decals.Dequeue();
			if ( old.IsValid() ) old.Destroy();
		}
	}

	/// <summary> Traces against static world geometry only (not zombies, gibs, players or effects). </summary>
	public (Vector3 Position, Vector3 Normal)? TraceWorld( Vector3 from, Vector3 to )
	{
		var tr = Scene.Trace.Ray( from, to )
			.WithoutTags( "zombie", "zombieblocker", "gib", "player", "viewmodel" )
			.IgnoreDynamic()
			.Run();
		if ( !tr.Hit ) return null;
		return (tr.HitPosition, tr.Normal);
	}

	// ---- Meat -------------------------------------------------------------

	/// <summary> Throws loose organs or meat chunks out from a wound. </summary>
	public void ThrowMeat( Vector3 origin, Vector3 direction, int count, bool organs )
	{
		var models = organs && OrganModels.Count > 0 ? OrganModels : MeatModels;
		if ( models.Count == 0 ) return;

		for ( var i = 0; i < count; i++ )
		{
			var model = Game.Random.FromList( models );
			var velocity = (direction.Normal + Vector3.Random * 0.8f).Normal * Game.Random.Float( 120f, 260f ) + Vector3.Up * Game.Random.Float( 80f, 200f );
			Gib.CreateProp( Scene, this, model, origin + Vector3.Random * 4f, Rotation.Random, velocity );
		}
	}
}
