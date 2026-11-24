using Godot;

/// <summary>
/// The angler's gore (user's ask: "full, complete gore gibs, bleeding, all that"). Every hit that doesn't kill it punches
/// a wound into its hide (a dent, flesh knocked out), sprays goo and leaves the wound oozing; hurt badly it bleeds as it
/// swims and its scales darken and bloody (the skin's `bruise`). Parts come off on their own health: an eye is
/// burst (a socket left spurting), the lantern is shot off its stalk (its light dies, no more swings), the jaw is torn
/// away (no more bites or hooks; the stump gushes), a pectoral fin is ripped, and teeth are knocked out of the mouth.
/// On death the whole fish comes apart: the jaw, the lantern on its stalk, the fins and the eyes fly off as pieces,
/// the carcass itself floats up oozing, and a burst of its gibs and goo goes with it.
/// </summary>
public partial class Angler
{
	readonly List<MeshInstance3D> fangs = new();
	readonly List<Node3D> eyes = new();
	readonly List<Node3D> barbels = new();
	Node3D hull;
	CreaturePart lanternPart, jawPart;
	bool lanternGone, jawGone;
	readonly bool[] eyeGone = new bool[2];
	readonly Dictionary<int, float> partHealth = new();
	readonly float[] finHealth = { 1f, 1f };
	readonly List<Node3D> bleeds = new();
	float bleedTick;
	int mouthHits;

	/// <summary> Shares of its health each part can take before it comes off. </summary>
	public const float EyeShare = 0.12f, LanternShare = 0.2f, JawShare = 0.3f, FinShare = 0.1f, ToothChance = 0.35f;

	/// <summary> Tests: what has come off, how many wounds, and what flew off on deaths. </summary>
	public static int Wounds, TeethOut, EyesOut, LanternsOff, JawsOff, FinsOff, DeathPieces;
	public bool LanternGone => lanternGone;
	public bool JawGone => jawGone;
	public int EyesGone => (eyeGone[0] ? 1 : 0) + (eyeGone[1] ? 1 : 0);

	void GoreSetup()
	{
		var max = MathF.Max( 1f, health.MaxHealth );
		partHealth[2] = partHealth[3] = max * EyeShare;
		partHealth[4] = max * LanternShare;
		partHealth[5] = max * JawShare;
		finHealth[0] = finHealth[1] = max * FinShare;
	}

	/// <summary> A hit that doesn't kill it: the wound, the spray, and whatever comes off. </summary>
	void HitGore( int index, float dealt, Vector3 at, Vector3 direction )
	{
		if ( gore is null ) return;
		gore.GooBurst( at, -direction, GooColor, 9, 260f, 0.9f );
		gore.GooSpray( at, direction, 3, GooColor, 200f, 0.5f, 1.1f );
		GibKit.Chips( Gibs, at, -direction, direction, 0.07f, Rng.Float() < 0.4f ? 2 : 1 );
		// The hide: a crater and a wound that keeps oozing for a while.
		var onJaw = index == 5 && !jawGone;
		if ( index is 0 or 1 or 5 )
		{
			Wounds++;
			CreatureGore.AddWound( onJaw ? jaw : body, at, direction, Blue, GooColor, 7f );
			Bleed( at, onJaw ? jaw : body, 6f, 0.22f );
		}
		CreatureGore.Jiggle( hull, 0.7f );   // the hide only: squashing the body would squash its hit shapes too
		// Teeth: a hit near the mouth may knock one out.
		if ( index is 0 or 5 && (++mouthHits % 3 == 0 || Rng.Float() < ToothChance) ) KnockTooth( at, direction );
		// A fin near where it hit.
		for ( var i = 0; i < pectorals.Count && i < 2; i++ )
		{
			var fin = pectorals[i];
			if ( !IsInstanceValid( fin ) || fin.GetParent() != body || fin.GlobalPosition.DistanceTo( at ) > 0.6f ) continue;
			finHealth[i] -= dealt;
			if ( finHealth[i] <= 0f ) TearFin( i, direction );
		}
		// The parts with their own health.
		if ( !partHealth.ContainsKey( index ) ) return;
		partHealth[index] -= dealt;
		if ( partHealth[index] > 0f ) return;
		switch ( index )
		{
			case 2 or 3: PopEye( index - 2, direction ); break;
			case 4: ShootLanternOff( direction ); break;
			case 5: TearJawOff( direction ); break;
		}
	}

	/// <summary> An ooze at a wound (it follows the body), dripping goo for `seconds`. </summary>
	void Bleed( Vector3 at, Node3D on, float seconds, float interval )
	{
		var ooze = new GooOoze { Name = "Bleed", Color = GooColor, Lifetime = seconds, Interval = interval };
		on.AddChild( ooze );
		ooze.GlobalPosition = at;
		bleeds.Add( ooze );
		if ( bleeds.Count > 12 ) { if ( IsInstanceValid( bleeds[0] ) ) bleeds[0].QueueFree(); bleeds.RemoveAt( 0 ); }
	}

	/// <summary> Every frame: the hide darkens and bloodies as it is hurt, and badly hurt it bleeds a trail as it swims. </summary>
	void GoreFrame( float dt )
	{
		var hurt = 1f - MathX.Clamp( health.Current / MathF.Max( 1f, health.MaxHealth ), 0f, 1f );
		skin.SetShaderParameter( "bruise", hurt );
		if ( IsDead || hurt < 0.5f || gore is null ) return;
		bleedTick -= dt;
		if ( bleedTick > 0f ) return;
		bleedTick = MathX.Lerp( 0.5f, 0.15f, (hurt - 0.5f) * 2f );
		var from = body.GlobalTransform * new Vector3( Rng.Float( -0.4f, 0.4f ), Rng.Float( -0.5f, 0.2f ), Rng.Float( -0.6f, 0.7f ) );
		gore.GooDrip( from, GooColor );
		if ( Rng.Float() < 0.35f ) gore.GooBurst( from, Vector3.Down, GooColor, 2, 60f, 0.6f );
	}

	void KnockTooth( Vector3 at, Vector3 direction )
	{
		MeshInstance3D best = null;
		var bestD = 1.2f;
		foreach ( var f in fangs )
		{
			if ( !IsInstanceValid( f ) || !f.Visible ) continue;
			var d = f.GlobalPosition.DistanceTo( at );
			if ( d < bestD ) { bestD = d; best = f; }
		}
		if ( best is null ) return;
		TeethOut++;
		Loose( best, 1.2f, 0.3f, (-direction + Rng.InSphere( 0.5f ) + Vector3.Up * 0.4f).SafeNormal() * Rng.Float( 3f, 6f ), twitch: false );
		gore.GooBurst( at, -direction, GooColor, 5, 200f, 0.6f );
		Sfx.Play( "mine_tink", at, 0.6f, Rng.Float( 1.4f, 1.8f ) );
	}

	void PopEye( int which, Vector3 direction )
	{
		if ( eyeGone[which] || which >= eyes.Count ) return;
		eyeGone[which] = true;
		EyesOut++;
		var eye = eyes[which];
		var at = eye.GlobalPosition;
		var outward = (at - body.GlobalPosition).SafeNormal();
		eye.Visible = false;
		gore.GooBurst( at, outward, GooColor, 18, 300f, 1.1f );
		gore.GooSpray( at, outward, 5, GooColor, 220f, 0.6f, 1.3f );
		GibKit.Burst( Gibs, at, outward, 0.09f, 5 );
		GibKit.Stump( Gibs, body, at, outward, 0.14f, 8f );
		Sfx.Play( "splat", at, 1f, 0.8f );
		Sfx.Play( "hit_flesh", GlobalPosition, 0.9f, 0.7f );
	}

	void ShootLanternOff( Vector3 direction )
	{
		if ( lanternGone || stalk.Count == 0 ) return;
		lanternGone = true;
		LanternsOff++;
		var root = stalk[0];
		var at = root.GlobalPosition;
		if ( IsInstanceValid( lanternPart ) ) lanternPart.QueueFree();
		if ( lanternLight is not null ) lanternLight.LightEnergy = 0.6f;
		if ( act == Act.Swing ) End();
		GibKit.Stump( Gibs, body, at, (at - body.GlobalPosition).SafeNormal(), 0.08f, 6f );
		gore.GooBurst( at, Vector3.Up, GooColor, 12, 240f, 0.9f );
		AlienBlast.Spawn( GetTree().CurrentScene, LanternWorld(), 0.3f );
		Loose( root, 8f, 2f, (direction.Flat().SafeNormal() * 2f + Vector3.Up * 3f) );
		Sfx.Play( "glass_break", at, 0.9f, 1.1f );
	}

	Vector3 LanternWorld() => lantern is not null && IsInstanceValid( lantern ) ? lantern.GlobalTransform * new Vector3( 0f, -0.34f, 0f ) : GlobalPosition;

	void TearJawOff( Vector3 direction )
	{
		if ( jawGone || jaw is null ) return;
		jawGone = true;
		JawsOff++;
		if ( act is Act.Chomp or Act.Hook ) { ReleaseHook(); End(); }
		var at = jaw.GlobalPosition;
		if ( IsInstanceValid( jawPart ) ) jawPart.QueueFree();
		var down = -body.GlobalBasis.Y.Normalized();
		Loose( jaw, 14f, 4f, (direction.Flat().SafeNormal() * 2.5f + down * 2f) );
		var stumpAt = body.GlobalTransform * new Vector3( 0f, -0.42f, -0.5f );
		GibKit.Stump( Gibs, body, stumpAt, down, 0.32f, 10f );
		gore.GooBurst( stumpAt, down, GooColor, 26, 320f, 1.3f );
		gore.GooSpray( stumpAt, down, 8, GooColor, 260f, 0.8f, 1.6f );
		Bleed( stumpAt, body, 12f, 0.08f );
		Sfx.Play( "stab_squish_1", at, 1f, 0.8f );
		Sfx.Play( "hit_flesh", GlobalPosition, 1f, 0.6f );
	}

	void TearFin( int which, Vector3 direction )
	{
		var fin = pectorals[which];
		if ( !IsInstanceValid( fin ) || fin.GetParent() != body ) return;
		FinsOff++;
		var at = fin.GlobalPosition;
		Loose( fin, 6f, 0.8f, (direction + Vector3.Up).SafeNormal() * 2.5f );
		GibKit.Stump( Gibs, body, at, (at - body.GlobalPosition).SafeNormal(), 0.1f, 5f );
		gore.GooBurst( at, direction, GooColor, 8, 200f, 0.8f );
	}

	/// <summary> A piece of it comes away as a physics gib (anything shootable on it is taken off first). </summary>
	Gib Loose( Node3D piece, float radiusInches, float mass, Vector3 velocity, bool twitch = true )
	{
		foreach ( var part in piece.FindChildren( "*", "", true, false ).OfType<CreaturePart>().ToList() ) part.QueueFree();
		if ( piece is CreaturePart cp ) cp.QueueFree();
		var gib = Gib.Attach( piece, gore, radiusInches, 30f, mass, velocity, GooColor );
		if ( twitch ) CreatureGore.Twitch( piece, Rng.Float( 2f, 4f ), 0.8f );
		return gib;
	}

	/// <summary> Death: it comes apart. </summary>
	void FallApart( Vector3 direction )
	{
		if ( gore is null || body is null ) return;
		direction = direction == Vector3.Zero ? -GlobalBasis.Z : direction.Normalized();
		var centre = body.GlobalPosition;
		var count = 0;
		gore.GooBurst( centre, direction, GooColor, 34, 360f, 1.4f );
		gore.GooSpray( centre, direction, 10, GooColor, 300f, 0.8f, 1.8f );
		gore.GooSpray( centre, Vector3.Down, 6, GooColor, 200f, 1f, 1.8f );
		GibKit.Burst( Gibs, centre, direction, 0.28f, 16 );
		// The jaw, the lantern on its stalk, the fins and the eyes fly off; the tail fan with them.
		Vector3 Fling( float up ) => (direction * Rng.Float( 2f, 4f ) + Rng.InSphere( 2f ) + Vector3.Up * up);
		if ( !jawGone && jaw is not null ) { Loose( jaw, 14f, 4f, Fling( 1f ) ); count++; }
		if ( !lanternGone && stalk.Count > 0 ) { if ( lanternLight is not null ) lanternLight.LightEnergy = 0.8f; Loose( stalk[0], 8f, 2f, Fling( 3f ) ); count++; }
		foreach ( var fin in pectorals.ToList() ) if ( IsInstanceValid( fin ) && fin.GetParent() == body ) { Loose( fin, 6f, 0.8f, Fling( 2f ) ); count++; }
		if ( tailFin is not null && IsInstanceValid( tailFin ) ) { Loose( tailFin, 8f, 1f, Fling( 1.5f ) ); count++; }
		for ( var i = 0; i < eyes.Count; i++ )
		{
			if ( eyeGone[i] || !IsInstanceValid( eyes[i] ) ) continue;
			Loose( eyes[i], 6f, 0.5f, Fling( 2.5f ) );
			count++;
		}
		// The carcass: torn open at the front, floating up and oozing.
		var front = body.GlobalTransform * new Vector3( 0f, -0.3f, -0.6f );
		var outward = (front - centre).SafeNormal();
		GibKit.Stump( Gibs, body, front, outward, 0.4f, 10f );
		Bleed( front, body, 14f, 0.1f );
		Loose( body, 22f, 12f, direction * 1.5f + Vector3.Up * 1f );
		count++;
		DeathPieces += count;
		Sfx.Play( "stab_squish_1", centre, 1f, 0.7f );
		Sfx.Play( "creature_death", centre, 1f, 0.6f );
	}
}
