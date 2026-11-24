using Godot;

/// <summary> The Goo-Bot's numbers (what one skill point does, and the base kit). </summary>
public static class GooFx
{
	// The Goo Gun: each shot spends `AmmoPer` (60 a magazine: 6 shots). Blobs stick and build a goo ball, each blob on a
	// ball hitting harder than the last (`BlobStep` more), up to `MaxBlobs`. The ball pops `PopTime` s after its last
	// blob (each new blob restarts the clock), or at once on the 6th, for `PopPerBlob` a blob within `PopRadius`.
	public const int AmmoPer = 10, MaxBlobs = 6;
	public const float BlobSpeed = 34f, BlobGravity = 5f, BlobRadius = 0.12f, BlobHoming = 5f, BlobStep = 10f;
	public const float PopTime = 4f, PopPerBlob = 60f, PopBase = 2f, PopPerRadius = 0.35f, SlowPerBlob = 0.05f, MaxSlow = 0.6f;
	public static float PopRadius( int blobs ) => PopBase + PopPerRadius * blobs;
	public const float ThickPer = 0.12f, SlowSetPer = 0.5f, TarPitPer = 0.03f, HeavyPer = 0.1f, FeedbackPer = 0.08f, SplatterReach = 2f;
	public static float BoulderDamage( int n ) => 60f + 40f * n;
	public static float BoulderTime( int n ) => 2.5f + 0.5f * n;
	// The Jelly Cannon: a jelly that springs off each enemy it hits to the nearest one it hasn't, `Hops` times.
	public const float JellySpeed = 26f, JellyGravity = 7f, JellyRadius = 0.16f, HopReach = 6f, HopSpeed = 11f, HopKeep = 0.85f;
	public const int Hops = 3;
	public const float LongHopPer = 1.5f, JigglePer = 0.15f, FatPer = 0.03f, StickyTime = 3f, StickySlow = 0.35f, StickyRadius = 1.4f;
	public static float VolatileDamage( int n ) => 20f + 15f * n;
	public const float VolatileRadius = 3f;
	public static int MotherHops( int n ) => 2 + 2 * n;
	// The Splat Jump (mouse 4): bounce up as a blob of jelly, land in a splat that glues the enemies near you.
	public const float SplatCost = 30f, SplatUp = 8f, SplatForward = 2.5f, SlamSpeed = 20f, SplatRadius = 4f, SplatDamage = 35f, SplatGlue = 1f;
	public const float BouncePer = 0.12f, SoftPer = 0.06f, SoftTime = 3f, ShellPer = 0.15f, PuddleSlow = 0.25f, PuddleSlowPer = 0.05f, PuddleDps = 4f;
	public static float PuddleTime( int n ) => 3f + n;
	public static float QuakeReach( int n ) => 6f + n;
	public static float QuakeDamage( int n ) => 50f + 35f * n;
	public const float QuakeDelay = 0.3f;

	public static readonly Color Grape = new( 0.55f, 0.12f, 0.78f ), Magenta = new( 0.95f, 0.25f, 0.75f ), Pink = new( 1f, 0.62f, 0.92f ),
		Core = new( 0.2f, 0.03f, 0.34f ), Steel = new( 0.42f, 0.38f, 0.5f );

	/// <summary> Tests: how often each thing happened. </summary>
	public static int Blobs, BlobHits, Pops, BigPops, Jellies, JellyHits, JellyHops, Volatiles, Mothers, Splats, Quakes, Splatters, Boulders, BoulderHits, Refunds, Shells;
	public static int MostBlobs;

	// ---- The goo look: a cel-shaded translucent jelly with a dark core, a bright rim and bubbles inside, that wobbles ----

	static ShaderMaterial goo, jelly, puddle;
	public static ShaderMaterial Goo => goo ??= Make( Grape, Core, Pink, 0.8f );
	public static ShaderMaterial Jelly => jelly ??= Make( Magenta, Grape.Darkened( 0.3f ), Pink, 0.7f );
	public static ShaderMaterial PuddleLook => puddle ??= Make( Grape.Darkened( 0.25f ), Core, Magenta.Darkened( 0.2f ), 0.75f );
	static SphereMesh ball;
	public static SphereMesh Ball => ball ??= new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 };

	static ShaderMaterial Make( Color body, Color core, Color rim, float alpha )
	{
		var m = new ShaderMaterial { Shader = new Shader { Code = GooCode } };
		m.SetShaderParameter( "body", body );
		m.SetShaderParameter( "core", core );
		m.SetShaderParameter( "rim", rim );
		m.SetShaderParameter( "alpha", alpha );
		m.SetMeta( "no_caustics", true );
		return m;
	}

	/// <summary> A ball of goo (radius `r`) on its own node; `wobble` and `glow` are per instance. </summary>
	public static MeshInstance3D Blob( Node3D parent, Vector3 at, float r, Material look = null, string name = "Goo" )
	{
		var mi = new MeshInstance3D { Name = name, Mesh = Ball, MaterialOverride = look ?? Goo, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild( mi );
		mi.Position = at;
		mi.Scale = Vector3.One * r;
		mi.SetInstanceShaderParameter( "seed", Rng.Float( 0f, 10f ) );
		mi.SetInstanceShaderParameter( "wobble", 0.06f );
		return mi;
	}

	/// <summary> A splash of goo drops. </summary>
	public static void Splash( Vector3 at, int count, float speed, float size = 1.5f )
	{
		for ( var i = 0; i < count; i++ )
			GooDroplet.Spawn( at, (Rng.OnSphere() + Vector3.Up * 0.6f).SafeNormal() * Rng.Float( speed * 0.4f, speed ), Rng.Float() < 0.6f ? Grape : Magenta, Rng.Float( size * 0.6f, size * 1.3f ) );
	}

	/// <summary> The ground under a point (the world below it, else the sea floor). </summary>
	public static Vector3 Ground( Node3D context, Vector3 at )
	{
		var tr = Trace.Ray( context, at + Vector3.Up * 0.5f, at + Vector3.Down * 8f, Layers.World );
		return tr.Hit ? tr.HitPosition : at with { Y = SeaTerrain.HeightAt( at.X, at.Z ) };
	}

	/// <summary> Every live enemy within a radius of a point (by its nearest part). </summary>
	public static List<Creature> Near( Vector3 at, float radius, Creature skip = null )
	{
		var list = new List<Creature>();
		foreach ( var c in Creature.Everyone )
		{
			if ( c == skip || !GodotObject.IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			if ( SkillFx.NearestPart( c, at ).DistanceTo( at ) <= radius ) list.Add( c );
		}
		return list;
	}

	const string GooCode = @"
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_back, diffuse_toon, specular_toon;
uniform vec3 body : source_color = vec3( 0.55, 0.12, 0.78 );
uniform vec3 core : source_color = vec3( 0.2, 0.03, 0.34 );
uniform vec3 rim : source_color = vec3( 1.0, 0.62, 0.92 );
uniform float alpha = 0.8;
instance uniform float wobble = 0.06;
instance uniform float glow = 0.0;
instance uniform float seed = 0.0;
varying vec3 obj;
float hash( vec3 p ) { return fract( sin( dot( p, vec3( 12.9898, 78.233, 37.719 ) ) ) * 43758.5453 ); }
void vertex()
{
	obj = VERTEX;
	float t = TIME * 6.0 + seed * 13.0;
	float w = sin( VERTEX.y * 5.0 + t ) * 0.5 + sin( VERTEX.x * 4.0 - t * 1.3 ) * 0.35 + sin( VERTEX.z * 6.0 + t * 0.7 ) * 0.25;
	VERTEX += NORMAL * w * wobble * length( VERTEX );
}
void fragment()
{
	float f = 1.0 - clamp( dot( NORMAL, VIEW ), 0.0, 1.0 );
	vec3 c = mix( core, body, smoothstep( 0.0, 0.65, f ) );
	// Bubbles hanging inside: a few bright dots in object space cells.
	vec3 q = obj * 3.0 + seed;
	vec3 cell = floor( q );
	float b = step( 0.8, hash( cell ) ) * smoothstep( 0.3, 0.16, length( fract( q ) - 0.5 ) );
	c = mix( c, rim, b * 0.55 );
	float edge = smoothstep( 0.55, 0.95, f );
	c = mix( c, rim, edge * 0.8 );
	ALBEDO = c;
	EMISSION = c * ( 0.22 + glow * 2.2 ) + rim * edge * 0.5;
	ROUGHNESS = 0.12;
	SPECULAR = 0.9;
	ALPHA = mix( alpha, 1.0, edge );
}";
}

/// <summary>
/// The Goo-Bot's guns. The Goo Gun (slot 1): blobs of goo that stick to an enemy and build a wobbling goo ball on it
/// (`GooBall`): each blob on the ball hits harder than the last, and the ball pops in a splash 4 s after its last blob
/// (every blob restarts the clock), or at once on the 6th. Each shot is 10 of the 60 in the magazine. The Jelly Cannon
/// (slot 2): a jelly (`Jelly`) that squashes on an enemy and springs off to the next one near by; each jelly hit is a
/// blob on that enemy's ball too.
/// </summary>
public partial class PlayerWeapon
{
	public bool Goo, JellyGun;
	Node3D tankGoo;

	public static PlayerWeapon GooGun( int slot = 1 ) => new()
	{
		Name = "GooGun", DisplayName = "Goo Gun", Slot = slot, ModelName = "goo", Goo = true,
		Damage = 35f /* the first blob on a ball; each one after it 10 more */, FireRate = 3f, Range = 60f, SpreadDegrees = 0.6f, Recoil = 1f,
		MagazineSize = 60, StartingReserve = 240, ReloadTime = 1.8f, Automatic = true,
		MuzzleFlashScale = 0f, ShakePerShot = 0.1f, FovKickPerShot = 0.8f, ShootSound = "goo_shot",
	};

	public static PlayerWeapon JellyCannon( int slot = 2 ) => new()
	{
		Name = "JellyCannon", DisplayName = "Jelly Cannon", Slot = slot, ModelName = "jelly", JellyGun = true,
		Damage = 50f, FireRate = 1.2f, Range = 40f, SpreadDegrees = 0f, Recoil = 2f,
		MagazineSize = 4, StartingReserve = 32, ReloadTime = 2f, Automatic = false,
		MuzzleFlashScale = 0f, ShakePerShot = 0.25f, FovKickPerShot = 2f, ShootSound = "jelly_fire",
	};

	/// <summary> A shot for the guns that fire on their own terms (goo, jelly, pinball): ammo (`cost` a shot), sound, kick. </summary>
	bool BotShot( float volume = 0.9f, float pitch = 1f, int cost = 1 )
	{
		if ( IsReloading || TimeSinceShot < 1f / CurrentFireRate ) return false;
		if ( Ammo < cost && !InfiniteAmmo ) { if ( Reserve > 0 ) StartReload(); else Sfx.Play2D( "dry_fire" ); return false; }
		TimeSinceShot = 0f;
		if ( !InfiniteAmmo ) Ammo -= cost;
		Sfx.Play2D( ShootSound, volume, pitch, Sfx.GunBus );
		if ( ViewModel is not null && ViewModel.Visible ) ViewModel.Fire();
		juice?.Shake( ShakePerShot );
		juice?.KickFov( FovKickPerShot );
		Controls.Rumble( 0.25f );
		controller.EyeAngles += new Angles( -Recoil, Rng.Float( -Recoil, Recoil ) * 0.3f, 0f );
		return true;
	}

	Vector3 Aim => controller.Camera is { } cam ? -cam.GlobalBasis.Z : controller.EyeAngles.Forward;
	Vector3 Eye => controller.Camera?.GlobalPosition ?? controller.EyeTransform.Origin;

	/// <summary> Where a shot leaves from: the muzzle in view, the eye ahead of the robot from outside. </summary>
	Vector3 ShotFrom( Vector3 aim, float ahead ) => controller.SeenFromOutside ? controller.EyeTransform.Origin + aim * (ahead + 0.1f) : Eye + aim * ahead - controller.Camera.GlobalBasis.Y * 0.14f;

	/// <summary> Feedback: ammo back into the magazine (never past it). </summary>
	public void Refund( int ammo ) => Ammo = Math.Min( MagazineSize, Ammo + ammo );

	/// <summary> The Goo Gun and the Jelly Cannon each frame (their own triggers, no aiming down sights). </summary>
	void UpdateGoo( float dt, bool free )
	{
		if ( controller.In.JustPressed( "reload" ) ) StartReload();
		if ( Goo && !IsReloading && Ammo < GooFx.AmmoPer && Reserve > 0 && !InfiniteAmmo ) StartReload();
		var fire = Automatic ? controller.In.Pressed( "attack1" ) : controller.In.JustPressed( "attack1" );
		if ( free && fire && !IsReloading )
		{
			if ( Goo ) FireGoo();
			else FireJelly();
		}
		if ( Goo ) ShowTank( dt );
	}

	/// <summary> Testing aids: one shot as if left click were pressed. </summary>
	public void TestGoo() { TimeSinceShot = 999f; FireGoo(); }
	public void TestJelly() { TimeSinceShot = 999f; FireJelly(); }

	void FireGoo()
	{
		if ( !BotShot( 0.7f, Rng.Float( 0.92f, 1.08f ), GooFx.AmmoPer ) ) return;
		GooFx.Blobs++;
		var aim = (Aim + Rng.InSphere( 1f ) * MathF.Tan( MathX.Rad( SpreadDegrees ) )).Normalized();
		GooBlob.Fire( this, controller, ShotFrom( aim, 0.6f ), aim * GooFx.BlobSpeed );
	}

	void FireJelly()
	{
		if ( !BotShot( 0.8f, Rng.Float( 0.95f, 1.05f ) ) ) return;
		GooFx.Jellies++;
		var aim = Aim;
		global::Jelly.Fire( this, controller, ShotFrom( aim, 0.7f ), aim * GooFx.JellySpeed + Vector3.Up * 1.2f, CurrentDamage );
	}

	/// <summary> The Goo Gun's glass tank empties as the magazine does. </summary>
	void ShowTank( float dt )
	{
		if ( ViewModel is null ) return;
		if ( tankGoo is null || !IsInstanceValid( tankGoo ) ) tankGoo = ViewModel.FindChild( "Tank Goo", true, false ) as Node3D;
		if ( tankGoo is null ) return;
		var full = MathX.Clamp( Ammo / (float)MagazineSize, 0.04f, 1f );
		var z = MathX.Lerp( tankGoo.Scale.Z, full, 1f - MathF.Exp( -10f * dt ) );
		tankGoo.Scale = new Vector3( 1f, 1f, z );
	}

	/// <summary> A blob on a ball of this many blobs hits for this (the first one the gun's damage, each after it more). </summary>
	public float BlobDamage( int blob ) => (CurrentDamage + GooFx.BlobStep * ShotScale * (blob - 1)) * (1f + GooFx.ThickPer * Skill( SkillId.ThickGoo ));
}

/// <summary> A blob of goo in flight: nearly straight and fast, it sticks to the first enemy it meets and splats on the world. </summary>
public partial class GooBlob : BotBall
{
	MeshInstance3D body;

	public static GooBlob Fire( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 velocity )
	{
		var b = new GooBlob { Name = "Goo Blob" };
		b.Radius = GooFx.BlobRadius;
		b.Gravity = GooFx.BlobGravity;
		b.Drag = 0.05f;
		b.MaxBounces = 0;
		b.Life = 2.5f;
		b.Launch( weapon, owner, from, velocity );
		return b;
	}

	protected override void Build()
	{
		body = GooFx.Blob( look, Vector3.Zero, Radius * 1.3f );
		body.SetInstanceShaderParameter( "wobble", 0.15f );
	}

	protected override void Step( float dt )
	{
		// A gentle home onto the enemy part just off its line, so a near miss still sticks.
		var speed = Velocity.Length();
		if ( speed < 1f ) return;
		var dir = Velocity / speed;
		if ( GoreBall.NearestToLine( GlobalPosition, dir, 7f, 0.7f ) is { } target )
			Velocity = dir.Slerp( (target - GlobalPosition).Normalized(), MathF.Min( 1f, GooFx.BlobHoming * dt ) ) * speed;
	}

	public override void _Process( double delta )
	{
		// Stretched along its flight, like a thrown drop.
		if ( body is null || !IsInstanceValid( body ) ) return;
		var v = Velocity;
		if ( v.LengthSquared() < 0.01f ) return;
		var stretch = 1f + MathX.Clamp( v.Length() / 60f, 0f, 0.6f );
		var r = Radius * 1.3f;
		body.GlobalBasis = GunKit.Along( v.Normalized() ) * Basis.FromScale( new Vector3( r / MathF.Sqrt( stretch ), r * stretch, r / MathF.Sqrt( stretch ) ) );
		if ( (age % 0.06f) < (float)delta ) GooDroplet.Spawn( GlobalPosition, -v * 0.05f, GooFx.Grape, 0.8f, 0.5f );
	}

	protected override bool HitEnemy( Creature c, Node3D part, Vector3 at, float speed )
	{
		GooFx.BlobHits++;
		var blob = Math.Min( GooFx.MaxBlobs, (GooBall.Of( c )?.Blobs ?? 0) + 1 );
		Strike( c, part, at, weapon is not null && IsInstanceValid( weapon ) ? weapon.BlobDamage( blob ) : 35f );
		Sfx.Play( "goo_stick", at, 0.6f, 0.8f + 0.1f * blob );
		GooFx.Splash( at, 5, 2.5f );
		if ( c.IsDead ) weapon?.MarkKill(); else weapon?.MarkHit();
		GooBall.Add( c, at, owner, weapon );
		End();
		return false;
	}

	protected override void HitWorld( Vector3 at, Vector3 normal, float speed )
	{
		GooFx.Splash( at, 6, 2f );
		GooPuddle.Lay( owner, at, Rng.Float( 0.25f, 0.4f ), 2.5f, 0f, 0f );
		Sfx.Play( "goo_stick", at, 0.35f, 0.7f );
		End();
	}
}

/// <summary>
/// The goo on one enemy. Each blob is a lump stuck where it hit (carried by the nearest part, so it moves with the
/// limb), and they all swell with the count; the ball jiggles on a spring with each new blob, drips, and slows the
/// enemy (more with Tar Pit). `PopTime` s after its last blob, or at once on the 6th, it swells, glows and pops:
/// damage by the blobs to it and everything near, a shove, a splash (Splatter throws blobs onto the enemies near
/// by, Feedback refunds shots, Big Ball Energy turns a full ball into a rolling boulder). If the enemy dies under
/// it, it pops at once.
/// </summary>
public partial class GooBall : Node3D
{
	public static readonly List<GooBall> All = new();
	Creature victim;
	PlayerController owner;
	PlayerWeapon weapon;
	int blobs;
	float left, jiggle, jiggleSpeed, dripTick, tickSound;
	Vector3 last, centre;
	bool hasLast, popped;
	readonly List<MeshInstance3D> lumps = new();
	readonly List<float> lumpSize = new();

	public int Blobs => blobs;
	public float Left => left;
	public Creature Victim => victim;
	int Skill( SkillId id ) => Skills.Rank( owner, id );
	float Upgrade => weapon is not null && IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f;
	float ShotScale => weapon is not null && IsInstanceValid( weapon ) ? weapon.ShotScale : 1f;
	public float PopTime => GooFx.PopTime + GooFx.SlowSetPer * Skill( SkillId.SlowSet );

	public static GooBall Of( Creature c ) => c is null ? null : All.FirstOrDefault( b => b.victim == c && !b.popped && IsInstanceValid( b ) );

	/// <summary> A blob of goo lands on an enemy at this point: on its ball, or a new ball. Returns the ball. </summary>
	public static GooBall Add( Creature c, Vector3 at, PlayerController owner, PlayerWeapon weapon )
	{
		if ( c is null || !IsInstanceValid( c ) || !IsInstanceValid( owner ) ) return null;
		var ball = Of( c );
		if ( ball is null )
		{
			if ( c.IsDead ) return null;
			ball = new GooBall { Name = "Goo Ball", victim = c, owner = owner, weapon = weapon };
			owner.GetTree().CurrentScene.AddChild( ball );
			All.Add( ball );
		}
		if ( weapon is not null && IsInstanceValid( weapon ) && weapon.Goo ) ball.weapon = weapon;
		ball.Stick( at );
		return ball;
	}

	/// <summary> The Goo Gun the owner carries (for the pop's damage upgrades), if any. </summary>
	public static PlayerWeapon GunOf( PlayerController owner ) => owner?.GetNodeOrNull<WeaponInventory>( "WeaponInventory" )?.Weapons.FirstOrDefault( w => w.Goo );

	void Stick( Vector3 at )
	{
		blobs = Math.Min( GooFx.MaxBlobs, blobs + 1 );
		GooFx.MostBlobs = Math.Max( GooFx.MostBlobs, blobs );
		left = PopTime;
		jiggleSpeed += 7f;
		Lump( at );
		Grow();
		if ( blobs >= GooFx.MaxBlobs ) Pop();
	}

	void Lump( Vector3 at )
	{
		Node3D holder = victim;
		var bestD = float.MaxValue;
		foreach ( var p in CreaturePart.All )
		{
			if ( p.Creature != victim || !IsInstanceValid( p ) ) continue;
			var d = p.GlobalPosition.DistanceSquaredTo( at );
			if ( d < bestD ) { bestD = d; holder = p; }
		}
		if ( lumps.Count > 0 && IsInstanceValid( lumps[0] ) && lumps[0].GetParent() is Node3D heartHolder ) holder = heartHolder;
		var lump = GooFx.Blob( holder, Vector3.Zero, 0.1f, null, "Goo Lump" );
		// Blobs pile on the first one, so the ball builds up in one lump rather than coating the whole body.
		var heart = lumps.Count > 0 && IsInstanceValid( lumps[0] ) ? lumps[0].GlobalPosition : at;
		var spot = lumps.Count == 0 ? at : heart.Lerp( at, 0.35f ) + Rng.InSphere( 0.06f );
		lump.GlobalPosition = spot;
		lumps.Add( lump );
		lumpSize.Add( lumps.Count == 1 ? 0.1f : Rng.Float( 0.055f, 0.075f ) );
	}

	/// <summary> Every lump swells with the count, the first (the heart of the ball) most. </summary>
	void Grow()
	{
		for ( var i = lumps.Count - 1; i >= 0; i-- ) if ( !IsInstanceValid( lumps[i] ) ) { lumps.RemoveAt( i ); lumpSize.RemoveAt( i ); }
	}

	public override void _ExitTree() => All.Remove( this );

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		if ( popped ) return;
		if ( !IsInstanceValid( owner ) || victim is null || !IsInstanceValid( victim ) ) { Clear(); QueueFree(); return; }
		Grow();
		if ( lumps.Count > 0 )
		{
			var sum = Vector3.Zero;
			foreach ( var l in lumps ) sum += l.GlobalPosition;
			centre = sum / lumps.Count;
		}
		else centre = victim.GlobalPosition + Vector3.Up * 0.5f;
		GlobalPosition = centre;
		// It went down under the goo: the ball pops at once.
		if ( victim.IsDead ) { Pop(); return; }
		left -= dt;
		if ( left <= 0f ) { Pop(); return; }
		// The slow: hold back part of each step it took (its walking and pulls alike), like coral. Bosses are too big.
		var now = victim.GlobalPosition;
		if ( hasLast && victim.Pullable )
		{
			var step = now - last;
			var slow = MathF.Min( GooFx.MaxSlow, (GooFx.SlowPerBlob + GooFx.TarPitPer * Skill( SkillId.TarPit )) * blobs );
			if ( step.Length() < 2f ) victim.Pull( -step * slow );
		}
		last = victim.GlobalPosition;
		hasLast = true;
		Animate( dt );
	}

	/// <summary> The jiggle (an underdamped spring kicked by each blob), the warning swell and glow, the drips. </summary>
	void Animate( float dt )
	{
		jiggleSpeed += (-140f * jiggle - 5f * jiggleSpeed) * dt;
		jiggle += jiggleSpeed * dt;
		var warn = MathX.Clamp( 1f - left / 1.2f, 0f, 1f );
		var pulse = warn > 0f ? MathF.Sin( (float)Clock.Now * MathX.Lerp( 8f, 30f, warn ) ) * 0.5f + 0.5f : 0f;
		var swell = 1f + 0.35f * warn * warn + 0.08f * pulse * warn;
		var grow = MathF.Pow( blobs, 0.6f );
		var squash = MathX.Clamp( jiggle * 0.08f, -0.35f, 0.35f );
		for ( var i = 0; i < lumps.Count; i++ )
		{
			var l = lumps[i];
			var r = lumpSize[i] * grow * swell;
			l.GlobalBasis = Basis.FromScale( new Vector3( r * (1f + squash), r * (1f - squash), r * (1f + squash) ) );   // in metres, whatever the part's scale
			l.SetInstanceShaderParameter( "glow", warn * (0.5f + 0.5f * pulse) );
			l.SetInstanceShaderParameter( "wobble", 0.06f + 0.04f * blobs / 6f + MathF.Abs( squash ) * 0.4f + 0.1f * warn );
		}
		if ( warn > 0f && (tickSound -= dt) <= 0f ) { tickSound = MathX.Lerp( 0.35f, 0.1f, warn ); Sfx.Play( "goo_stick", centre, 0.25f, 1.2f + warn ); }
		if ( (dripTick -= dt) <= 0f && lumps.Count > 0 )
		{
			dripTick = Rng.Float( 0.15f, 0.35f ) / MathF.Max( 1f, blobs * 0.5f );
			var from = lumps[Rng.Int( 0, lumps.Count - 1 )];
			GooDroplet.Spawn( from.GlobalPosition + Vector3.Down * from.Scale.Y * 0.6f, Vector3.Down * Rng.Float( 0.3f, 1f ), GooFx.Grape, Rng.Float( 0.8f, 1.6f ), 1.4f );
		}
	}

	void Clear()
	{
		foreach ( var l in lumps ) if ( IsInstanceValid( l ) ) l.QueueFree();
		lumps.Clear();
		lumpSize.Clear();
	}

	/// <summary> Testing aid: pops it now. </summary>
	public void TestPop() => Pop();

	void Pop()
	{
		if ( popped ) return;
		popped = true;
		GooFx.Pops++;
		var n = blobs;
		var at = centre;
		Clear();
		QueueFree();
		if ( !IsInstanceValid( owner ) ) return;
		var heavy = 1f + GooFx.HeavyPer * Skill( SkillId.HeavyGoo );
		var radius = GooFx.PopRadius( n ) * heavy;
		var damage = GooFx.PopPerBlob * n * ShotScale * heavy;
		if ( n >= GooFx.MaxBlobs ) GooFx.BigPops++;
		if ( IsInstanceValid( victim ) && !victim.IsDead ) SkillFx.HitPart( victim, damage, at, owner );
		foreach ( var c in GooFx.Near( at, radius, victim ) )
		{
			var d = SkillFx.NearestPart( c, at ).DistanceTo( at );
			SkillFx.HitPart( c, damage * (1f - 0.5f * d / radius), at, owner );
			SkillFx.Shove( c, (c.GlobalPosition - at).Flat().SafeNormal() * (0.5f + 0.15f * n) + Vector3.Up * 0.3f );
			if ( !c.IsDead ) c.Stun( 0.3f );
		}
		weapon?.MarkHit();
		// The splash.
		GooFx.Splash( at, 6 + 5 * n, 2.5f + 0.7f * n, 1.6f + 0.15f * n );
		for ( var i = 0; i < 3 + n; i++ ) GpuFx.Bubble( at + Rng.InSphere( 0.4f ), Rng.Float( 0.05f, 0.14f ), Rng.Float( 0f, 0.2f ), Rng.Float( 0.8f, 1.6f ) );
		Shockwave.Spawn( GetTree().CurrentScene, at, radius, GooFx.Magenta );
		GooPuddle.Lay( owner, GooFx.Ground( owner, at ), 0.5f + 0.12f * n, 4f, 0f, 0f );
		Sfx.Play( "goo_pop", at, 0.6f + 0.07f * n, 1.25f - 0.06f * n );
		if ( owner.GlobalPosition.DistanceTo( at ) < 14f ) owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.06f * n );
		// Feedback: shots back into the Goo Gun's magazine (a chance for each blob).
		var feed = Skill( SkillId.Feedback );
		if ( feed > 0 && GunOf( owner ) is { } gun )
		{
			var back = 0;
			for ( var i = 0; i < n; i++ ) if ( Rng.Float() < GooFx.FeedbackPer * feed ) back++;
			if ( back > 0 ) { GooFx.Refunds += back; gun.Refund( back * GooFx.AmmoPer ); }
		}
		// Splatter: the splash throws a blob onto the enemies near by (restarting their own balls' clocks).
		var splat = Skill( SkillId.Splatter );
		if ( splat > 0 )
			foreach ( var c in GooFx.Near( at, radius + GooFx.SplatterReach, victim ).OrderBy( c => c.GlobalPosition.DistanceTo( at ) ).Take( splat ).ToList() )
			{
				GooFx.Splatters++;
				var to = SkillFx.NearestPart( c, at );
				GooStreak( at, to );
				Add( c, to, owner, weapon );
			}
		// Big Ball Energy: a full ball breaks off and rolls on as a boulder of goo.
		var big = Skill( SkillId.BigBallEnergy );
		if ( big > 0 && n >= GooFx.MaxBlobs )
		{
			var away = (at - owner.GlobalPosition).Flat().SafeNormal();
			if ( away == Vector3.Zero ) away = Vector3.Forward;
			GooBoulder.Roll( weapon, owner, at + Vector3.Up * 0.3f, away * 9f + Vector3.Up * 2f, big );
		}
	}

	/// <summary> A blob thrown from one point to another (Splatter): a short arc of drops. </summary>
	static void GooStreak( Vector3 from, Vector3 to )
	{
		for ( var i = 0; i < 6; i++ )
		{
			var t = i / 5f;
			var p = from.Lerp( to, t ) + Vector3.Up * MathF.Sin( t * MathF.PI ) * 0.8f;
			GooDroplet.Spawn( p, (to - from).SafeNormal() * 2f, GooFx.Magenta, 1.4f, 0.6f );
		}
	}
}

/// <summary> A puddle of goo on the ground: it grows in, and fades by shrinking. One with a slow (Sticky Split, the Splat's Puddle) holds enemies back in it, and one with damage stings them. </summary>
public partial class GooPuddle : Node3D
{
	public static readonly List<GooPuddle> All = new();
	PlayerController owner;
	float radius, life, age, slow, dps, tick;
	MeshInstance3D disc;
	readonly Dictionary<Creature, Vector3> lastAt = new();

	public float Slow => slow;
	public bool Holds( Creature c ) => GodotObject.IsInstanceValid( c ) && (c.GlobalPosition - GlobalPosition).Flat().Length() < radius;

	public static GooPuddle Lay( PlayerController owner, Vector3 at, float radius, float life, float slow, float dps )
	{
		if ( owner is null || !IsInstanceValid( owner ) ) return null;
		// Only so many: the oldest plain ones go first.
		if ( All.Count > 40 ) All.FirstOrDefault( p => p.slow <= 0f )?.QueueFree();
		var p = new GooPuddle { Name = "Goo Puddle", owner = owner, radius = radius, life = life, slow = slow, dps = dps };
		owner.GetTree().CurrentScene.AddChild( p );
		p.GlobalPosition = at + Vector3.Up * 0.03f;
		p.disc = GooFx.Blob( p, Vector3.Zero, 0.01f, GooFx.PuddleLook, "Puddle" );
		p.disc.SetInstanceShaderParameter( "wobble", 0.03f );
		All.Add( p );
		return p;
	}

	public override void _ExitTree() => All.Remove( this );

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		age += dt;
		if ( age > life || !IsInstanceValid( owner ) ) { QueueFree(); return; }
		var grow = MathX.Clamp( age / 0.25f, 0f, 1f );
		var fade = MathX.Clamp( (life - age) / 0.8f, 0f, 1f );
		var r = radius * (1f - (1f - grow) * (1f - grow)) * fade;
		disc.Scale = new Vector3( r, 0.035f + 0.02f * fade, r );
		if ( slow <= 0f && dps <= 0f ) return;
		tick -= dt;
		var sting = tick <= 0f;
		if ( sting ) tick = 0.5f;
		foreach ( var c in Creature.Everyone )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || !Holds( c ) ) { if ( c is not null ) lastAt.Remove( c ); continue; }
			if ( slow > 0f && c.Pullable && lastAt.TryGetValue( c, out var was ) )
			{
				var step = c.GlobalPosition - was;
				if ( step.Length() < 2f ) c.Pull( -step.Flat() * slow );
			}
			lastAt[c] = c.GlobalPosition;
			if ( sting && dps > 0f ) PlayerPerks.Hurt( c, dps * 0.5f, c.GlobalPosition, GlobalPosition, owner );
		}
	}
}

/// <summary>
/// The Jelly Cannon's jelly: it squashes flat on an enemy, does its damage, and springs off in an arc to the nearest
/// enemy within reach that it hasn't hit yet (`Hops` times, a little smaller and weaker each time). Each enemy it hits
/// gets a blob of goo on its ball. With nothing near it bounces about the world and wobbles to a stop. Jiggle stuns,
/// Sticky Split leaves a sticky puddle under each enemy, Volatile blows the last hop up, and Mother Jelly brings it to
/// life for more hops at twice the reach.
/// </summary>
public partial class Jelly : BotBall
{
	float damage, size = 1f, squash, squashSpeed, still;
	int hopsLeft;
	bool mothered;
	Node3D body;
	MeshInstance3D skin;
	readonly HashSet<Creature> hit = new();

	int Skill( SkillId id ) => Skills.Rank( owner, id );
	public int HopsDone { get; private set; }

	public static Jelly Fire( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 velocity, float damage )
	{
		var j = new Jelly { Name = "Jelly", damage = damage };
		j.Radius = GooFx.JellyRadius;
		j.Gravity = GooFx.JellyGravity;
		j.Drag = 0f;
		j.Restitution = 0.55f;
		j.BumperGain = 0.6f;
		j.MaxBounces = 4;
		j.Life = 6f;
		j.HitGap = 99f;
		j.hopsLeft = GooFx.Hops + Skills.Rank( owner, SkillId.ExtraHop );
		j.Launch( weapon, owner, from, velocity );
		return j;
	}

	protected override void Build()
	{
		body = new Node3D { Name = "Body" };
		look.AddChild( body );
		skin = GooFx.Blob( body, Vector3.Zero, 1f, GooFx.Jelly, "Jelly" );
		skin.SetInstanceShaderParameter( "wobble", 0.1f );
		GooFx.Blob( body, new Vector3( 0.1f, 0.15f, 0f ), 0.45f, GooFx.Goo, "Core" );
		var shine = GunKit.Paint( new Color( 1f, 0.95f, 1f ), 0.1f, 0f, 2f );
		for ( var i = 0; i < 4; i++ ) GunKit.Ball( body, Rng.InSphere( 0.55f ), Rng.Float( 0.06f, 0.12f ), shine );
	}

	public override void _Process( double delta )
	{
		// Squash and stretch: stretched along its flight, flattened on each hit and springing back with an overshoot.
		var dt = (float)delta;
		squashSpeed += (-220f * squash - 9f * squashSpeed) * dt;
		squash += squashSpeed * dt;
		if ( body is null || !IsInstanceValid( body ) ) return;
		var v = Velocity;
		var dir = v.LengthSquared() > 0.04f ? v.Normalized() : Vector3.Up;
		var along = 1f + MathX.Clamp( v.Length() / 70f, 0f, 0.3f ) - MathX.Clamp( squash, -0.45f, 0.6f );
		along = MathF.Max( 0.4f, along );
		var across = 1f / MathF.Sqrt( along );
		var r = Radius * size;
		body.GlobalBasis = GunKit.Along( dir ) * Basis.FromScale( new Vector3( r * across, r * along, r * across ) );
	}

	void Squash( float amount ) => squashSpeed += amount;

	protected override void Step( float dt )
	{
		// Rolling slowly on the floor: it wobbles to a stop and melts into a puddle.
		var slow = Velocity.Length() < 1.2f && age > 0.4f;
		still = slow ? still + dt : 0f;
		if ( still > 0.6f ) End();
	}

	protected override bool HitEnemy( Creature c, Node3D part, Vector3 at, float speed )
	{
		GooFx.JellyHits++;
		hit.Add( c );
		// A soft blow over the whole body, wherever it lands (a leg or a shell would turn most of a shot).
		SkillFx.HitPart( c, damage, at - Velocity.SafeNormal() * 0.3f, owner );
		Squash( 9f );
		Sfx.Play( "jelly_boing", at, 0.8f, 0.9f + 0.12f * HopsDone );
		GooFx.Splash( at, 6, 2.5f );
		if ( c.IsDead ) weapon?.MarkKill();
		else
		{
			weapon?.MarkHit();
			var jiggle = Skill( SkillId.Jiggle );
			if ( jiggle > 0 ) c.Stun( GooFx.JigglePer * jiggle );
		}
		// Each jelly hit is a blob on the enemy's goo ball (and restarts its clock).
		GooBall.Add( c, at, owner, GooBall.GunOf( owner ) ?? weapon );
		var sticky = Skill( SkillId.StickySplit );
		if ( sticky > 0 ) GooPuddle.Lay( owner, GooFx.Ground( owner, at ), GooFx.StickyRadius + 0.2f * sticky, GooFx.StickyTime, GooFx.StickySlow + 0.05f * sticky, 0f );
		// Mother Jelly: out of hops, it comes alive and hunts on, reaching twice as far.
		var mother = Skill( SkillId.MotherJelly );
		if ( hopsLeft <= 0 && mother > 0 && !mothered ) { mothered = true; hopsLeft = GooFx.MotherHops( mother ); GooFx.Mothers++; Sfx.Play( "jelly_boing", at, 1f, 0.6f ); }
		if ( hopsLeft > 0 && Next( at ) is { } next )
		{
			// Spring off in an arc that lands on the next one.
			hopsLeft--;
			HopsDone++;
			GooFx.JellyHops++;
			var keep = MathF.Min( 1f, GooFx.HopKeep + GooFx.FatPer * Skill( SkillId.FatJelly ) );
			damage *= keep;
			size = MathF.Max( 0.55f, size * keep );
			// Aimed at its middle from above (a limb or a shell turns most of a blow).
			var target = SkillFx.NearestPart( next, next.GlobalPosition + Vector3.Up * 0.9f );
			var delta = target - GlobalPosition;
			var time = MathX.Clamp( delta.Length() / GooFx.HopSpeed, 0.35f, 0.8f );
			Velocity = delta / time + Vector3.Up * 0.5f * Gravity * time;
			Life = age + 4f;
			return false;
		}
		// The end of the chain: Volatile blows it up.
		var vol = Skill( SkillId.VolatileJelly );
		if ( vol > 0 ) { Explode( at, vol ); return false; }
		return true;
	}

	/// <summary> The nearest live enemy within hop reach that this jelly hasn't hit. </summary>
	Creature Next( Vector3 at )
	{
		var reach = (GooFx.HopReach + GooFx.LongHopPer * Skill( SkillId.LongHop )) * (mothered ? 2f : 1f);
		Creature best = null;
		var bestD = reach;
		foreach ( var c in Creature.Everyone )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || hit.Contains( c ) ) continue;
			var d = SkillFx.NearestPart( c, at ).DistanceTo( at );
			if ( d < bestD ) { bestD = d; best = c; }
		}
		return best;
	}

	void Explode( Vector3 at, int n )
	{
		GooFx.Volatiles++;
		var scale = weapon is not null && IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f;
		foreach ( var c in GooFx.Near( at, GooFx.VolatileRadius ) )
		{
			SkillFx.HitPart( c, GooFx.VolatileDamage( n ) * scale, at, owner );
			SkillFx.Shove( c, (c.GlobalPosition - at).Flat().SafeNormal() * 1.2f + Vector3.Up * 0.4f );
		}
		GooFx.Splash( at, 30, 5f, 2f );
		Shockwave.Spawn( GetTree().CurrentScene, at, GooFx.VolatileRadius, GooFx.Magenta );
		Sfx.Play( "goo_pop", at, 0.9f, 0.8f );
		Sfx.Play( "missile_boom", at, 0.4f, 1.5f );
		End();
	}

	protected override void HitWorld( Vector3 at, Vector3 normal, float speed )
	{
		Squash( MathX.Clamp( speed * 0.6f, 2f, 9f ) );
		if ( speed > 3f ) Sfx.Play( "jelly_boing", at, MathX.Clamp( speed / 20f, 0.15f, 0.5f ), 0.7f + Rng.Float( 0f, 0.15f ) );
		if ( speed > 5f ) GooFx.Splash( at, 3, 1.5f );
	}

	protected override void Finish()
	{
		GooFx.Splash( GlobalPosition, 10, 2f );
		GooPuddle.Lay( owner, GooFx.Ground( owner, GlobalPosition ), 0.35f * size + 0.15f, 3f, 0f, 0f );
	}
}

/// <summary> Big Ball Energy: a full goo ball breaks off as a boulder of goo that rolls through enemies (each hit is a blob on them too), growing as it goes, then splats. </summary>
public partial class GooBoulder : BotBall
{
	int rank;
	float grow = 1f;
	MeshInstance3D skin;

	public static GooBoulder Roll( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 velocity, int rank )
	{
		GooFx.Boulders++;
		var b = new GooBoulder { Name = "Goo Boulder", rank = rank };
		b.Radius = 0.5f;
		b.Gravity = 9f;
		b.Drag = 0.05f;
		b.Restitution = 0.3f;
		b.MaxBounces = 8;
		b.Life = GooFx.BoulderTime( rank );
		b.HitGap = 0.6f;
		b.Launch( weapon, owner, from, velocity );
		return b;
	}

	protected override void Build()
	{
		skin = GooFx.Blob( look, Vector3.Zero, Radius, GooFx.Goo, "Boulder" );
		skin.SetInstanceShaderParameter( "wobble", 0.12f );
		for ( var i = 0; i < 6; i++ ) GooFx.Blob( look, Rng.OnSphere() * Radius * 0.8f, Radius * Rng.Float( 0.3f, 0.45f ), GooFx.Jelly, "Lump" );
	}

	protected override void Step( float dt )
	{
		// It keeps rolling on: pushed along the way it goes (water drag would stop it).
		var flat = Velocity.Flat();
		if ( flat.Length() < 7f ) Velocity += flat.SafeNormal() * 6f * dt;
		if ( (age % 0.1f) < dt ) GooDroplet.Spawn( GlobalPosition + Vector3.Down * Radius * 0.8f, Vector3.Up * 0.5f, GooFx.Grape, 2f, 0.8f );
	}

	protected override bool HitEnemy( Creature c, Node3D part, Vector3 at, float speed )
	{
		GooFx.BoulderHits++;
		var scale = weapon is not null && IsInstanceValid( weapon ) ? weapon.UpgradeScale : 1f;
		SkillFx.HitPart( c, GooFx.BoulderDamage( rank ) * scale, at, owner );
		SkillFx.Shove( c, (Velocity.Flat().SafeNormal() + (c.GlobalPosition - GlobalPosition).Flat().SafeNormal()).SafeNormal() * 1.6f + Vector3.Up * 0.7f );
		if ( !c.IsDead ) { c.Stun( 0.5f ); GooBall.Add( c, at, owner, weapon ); }
		Sfx.Play( "goo_stick", at, 0.8f, 0.6f );
		owner.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.1f );
		// Picking up goo as it goes.
		grow = MathF.Min( 1.8f, grow + 0.12f );
		Radius = 0.5f * grow;
		look.Scale = Vector3.One * grow;
		return false;
	}

	protected override void Finish()
	{
		var at = GlobalPosition;
		GooFx.Splash( at, 24, 4f, 2f );
		Shockwave.Spawn( GetTree().CurrentScene, at, 2.5f * grow, GooFx.Magenta );
		GooPuddle.Lay( owner, GooFx.Ground( owner, at ), 0.8f * grow, 4f, 0f, 0f );
		Sfx.Play( "goo_pop", at, 0.8f, 0.7f );
	}
}

/// <summary> The Goo-Bot's gun models, on the Blaster's rig like the trident shooter. </summary>
public static class GooModels
{
	public static readonly Vector3 GooMuzzle = new( 27f, 0f, 3.2f ), JellyMuzzle = new( 24f, 0f, 3.4f );
	static readonly Color Dark = new( 0.16f, 0.12f, 0.22f );

	public static void GooGun( Node3D gun )
	{
		var dark = GunKit.Paint( Dark, 0.5f, 0.4f );
		var plum = GunKit.Paint( GooFx.Grape.Darkened( 0.35f ), 0.4f, 0.3f );
		var chrome = GunKit.Paint( new Color( 0.82f, 0.82f, 0.88f ), 0.2f, 0.9f );
		var glass = new StandardMaterial3D { AlbedoColor = new Color( 0.85f, 0.9f, 1f, 0.22f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f, Metallic = 0.1f, RimEnabled = true, Rim = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), dark, 18f );
		// A plum body with chrome bands, a long nozzle that drips goo, and a glass tank of sloshing goo on top.
		GunKit.AlongX( gun, new Vector3( 5f, 0f, 3.2f ), 2.3f, 11f, plum, 2f, 18 );
		foreach ( var x in new[] { 1.2f, 5f, 8.8f } ) GunKit.AlongX( gun, new Vector3( x, 0f, 3.2f ), 2.5f, 0.6f, chrome, -1f, 18 );
		GunKit.AlongX( gun, new Vector3( 17f, 0f, 3.2f ), 1.1f, 13f, dark, 0.75f, 14 );
		GunKit.AlongX( gun, new Vector3( 23.8f, 0f, 3.2f ), 1.25f, 1.2f, chrome, -1f, 14 );
		GooFx.Blob( gun, new Vector3( 24.6f, 0f, 2.6f ), 0.9f, GooFx.Goo, "Drip" );
		var tank = new Node3D { Name = "Tank" };
		gun.AddChild( tank );
		tank.Position = new Vector3( 6f, 0f, 5.2f );
		GunKit.Rod( tank, Vector3.Zero, new Vector3( 0f, 0f, 5.2f ), 2.1f, glass, 18 );
		var fill = new Node3D { Name = "Tank Goo" };
		tank.AddChild( fill );
		var goo = GooFx.Blob( fill, new Vector3( 0f, 0f, 2.5f ), 1f, GooFx.Goo, "Goo" );
		goo.Scale = new Vector3( 1.8f, 1.8f, 2.5f );
		goo.SetInstanceShaderParameter( "wobble", 0.1f );
		GunKit.AlongX( tank, new Vector3( 0f, 0f, 5.4f ), 2.3f, 0.5f, chrome, -1f, 18 ).RotateY( MathF.PI / 2f );
		GunKit.Rod( gun, new Vector3( 9.5f, 0f, 6.5f ), new Vector3( 13f, 0f, 3.8f ), 0.45f, chrome, 8 );
		GunKit.AlongX( gun, new Vector3( -3.4f, 0f, 2.5f ), 1.1f, 3.8f, dark, 1.3f, 12 );
	}

	public static void JellyCannon( Node3D gun )
	{
		var dark = GunKit.Paint( Dark, 0.5f, 0.4f );
		var plum = GunKit.Paint( GooFx.Grape.Darkened( 0.35f ), 0.4f, 0.3f );
		var pink = GunKit.Paint( GooFx.Magenta, 0.35f, 0.1f );
		var chrome = GunKit.Paint( new Color( 0.82f, 0.82f, 0.88f ), 0.2f, 0.9f );
		GunKit.Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), dark, 18f );
		// A stubby cannon: a round plum breech, a short wide pink bell, and the next jelly wobbling in its mouth.
		GunKit.Ball( gun, new Vector3( 3f, 0f, 3.4f ), 3.2f, plum );
		GunKit.AlongX( gun, new Vector3( 12f, 0f, 3.4f ), 2.4f, 12f, plum, 3.3f, 20 );
		GunKit.AlongX( gun, new Vector3( 19.5f, 0f, 3.4f ), 3.4f, 3f, pink, 4.2f, 20 );
		foreach ( var x in new[] { 8f, 14f } ) GunKit.AlongX( gun, new Vector3( x, 0f, 3.4f ), x < 10f ? 2.6f : 2.9f, 0.7f, chrome, -1f, 20 );
		var loaded = new Node3D { Name = "Loaded" };
		gun.AddChild( loaded );
		var j = GooFx.Blob( loaded, new Vector3( 21.5f, 0f, 3.4f ), 2.6f, GooFx.Jelly, "Jelly" );
		j.SetInstanceShaderParameter( "wobble", 0.12f );
		// A pump on top: a chrome hose from the breech to a little bulb.
		GunKit.Ball( gun, new Vector3( 4f, 0f, 7.2f ), 1.3f, pink );
		GunKit.Rod( gun, new Vector3( 4f, 0f, 7.2f ), new Vector3( 11f, 0f, 5.8f ), 0.35f, chrome, 8 );
		GunKit.AlongX( gun, new Vector3( -3.4f, 0f, 2.5f ), 1.1f, 3.8f, dark, 1.3f, 12 );
	}
}
