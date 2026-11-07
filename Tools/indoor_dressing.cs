// Dresses the arena as a simple indoor level: concrete floor and ceiling, a perimeter of walls,
// a cross of interior walls with doorways (four rooms), pillars, crates and barrels, and ceiling lights.
// Editor scene only. Rebuilds the "Dressing" object under "Arena". Safe to rerun.
if ( Game.IsPlaying ) return "playing - not touching anything";
var sess = Editor.SceneEditorSession.Active;
if ( sess is null ) return "no scene session";
var s = sess.Scene;
var arena = s.Children.First( g => g.Name == "Arena" );
foreach ( var old in arena.Children.Where( g => g.Name == "Dressing" ).ToList() ) old.Destroy();

// The base arena's crates and barricades have colliders but hidden renderers (leftovers from the
// seafloor dressing). Remove them so they don't leave invisible walls on the floor.
foreach ( var junk in arena.Children.Where( g => g.Name.StartsWith( "Crate" ) || g.Name.StartsWith( "Barricade" ) ).ToList() )
	junk.Destroy();

// The collision floor stays hidden; we lay a visible floor over it.
var floorR = arena.Children.FirstOrDefault( g => g.Name == "Floor" )?.Components.GetAll<ModelRenderer>().FirstOrDefault();
if ( floorR is not null ) floorR.Enabled = false;

// Dim, indoorsy lighting. Ceiling lights below do the work.
var sun = s.GetAllComponents<DirectionalLight>().FirstOrDefault();
if ( sun is not null )
{
	sun.Intensity = 0.12f;
	sun.SkyColor = new Color( 0.05f, 0.05f, 0.07f );
}
var amb = s.GetAllComponents<AmbientLight>().FirstOrDefault();
if ( amb is not null ) amb.Color = new Color( 0.22f, 0.23f, 0.26f );
var fog = s.GetAllComponents<GradientFog>().FirstOrDefault();
if ( fog is not null ) fog.Enabled = false;
var cam = s.GetAllComponents<CameraComponent>().FirstOrDefault();
if ( cam is not null ) cam.BackgroundColor = new Color( 0.04f, 0.04f, 0.05f );

var dressing = s.CreateObject();
dressing.Name = "Dressing";
dressing.SetParent( arena, false );

var box = Model.Load( "models/dev/box.vmdl" );
var concrete = Material.Load( "materials/indoor/concrete.vmat" );
var crateA = Model.Load( "models/indoor/crate_a.vmdl" );
var crateB = Model.Load( "models/indoor/crate_b.vmdl" );
var barrel = Model.Load( "models/indoor/barrel_a.vmdl" );
var pillar = Model.Load( "models/indoor/pillar_a.vmdl" );
var shelf = Model.Load( "models/indoor/shelf_a.vmdl" );
var table = Model.Load( "models/indoor/table_a.vmdl" );
var locker = Model.Load( "models/indoor/locker_a.vmdl" );

var rnd = new System.Random( 5150 );
float R( float a, float b ) => a + (float)rnd.NextDouble() * (b - a);

var wallTint = new Color( 0.62f, 0.61f, 0.58f );
var floorTint = new Color( 0.4f, 0.4f, 0.42f );
var ceilTint = new Color( 0.3f, 0.3f, 0.33f );
var beamTint = new Color( 0.24f, 0.24f, 0.27f );
var woodTint = new Color( 0.52f, 0.37f, 0.22f );
var metalTint = new Color( 0.32f, 0.35f, 0.34f );
var stoneTint = new Color( 0.72f, 0.72f, 0.74f );

// A solid, collision-blocking block (walls, floor, ceiling). Size is in world units.
GameObject Solid( Vector3 centre, Vector3 size, Color tint, string name )
{
	var go = s.CreateObject();
	go.Name = name;
	go.SetParent( dressing, false );
	go.WorldPosition = centre;
	go.WorldScale = new Vector3( size.x / 50f, size.y / 50f, size.z / 50f );
	var r = go.Components.Create<ModelRenderer>();
	r.Model = box;
	r.MaterialOverride = concrete;
	r.Tint = tint;
	var col = go.Components.Create<BoxCollider>();
	col.Static = true;
	col.Scale = new Vector3( 50f, 50f, 50f );
	return go;
}

// A prop model, tinted, with a simple box collider sized to its bounds. (Mesh colliders on the
// assembled prop models were unreliable, so props use plain boxes.)
GameObject Prop( Model model, Vector3 pos, float yaw, Vector3 scale, Color tint, string name, bool collide = true )
{
	var go = s.CreateObject();
	go.Name = name;
	go.SetParent( dressing, false );
	go.WorldPosition = pos;
	go.WorldRotation = new Angles( 0, yaw, 0 ).ToRotation();
	go.WorldScale = scale;
	var r = go.Components.Create<ModelRenderer>();
	r.Model = model;
	r.Tint = tint;
	if ( collide )
	{
		var b = model.Bounds;
		var col = go.Components.Create<BoxCollider>();
		col.Static = true;
		col.Scale = b.Size;
		col.Center = b.Center;
	}
	return go;
}

// A visual-only block (no collider): ceiling beams, trim, and other decoration.
GameObject Deco( Vector3 centre, Vector3 size, Color tint, string name )
{
	var go = s.CreateObject();
	go.Name = name;
	go.SetParent( dressing, false );
	go.WorldPosition = centre;
	go.WorldScale = new Vector3( size.x / 50f, size.y / 50f, size.z / 50f );
	var r = go.Components.Create<ModelRenderer>();
	r.Model = box;
	r.MaterialOverride = concrete;
	r.Tint = tint;
	return go;
}

// Floor and ceiling.
Solid( new Vector3( 0, 0, -10f ), new Vector3( 3400, 3400, 20 ), floorTint, "Floor" );
var ceiling = Solid( new Vector3( 0, 0, 430f ), new Vector3( 3400, 3400, 20 ), ceilTint, "Ceiling" );
ceiling.Components.GetAll<BoxCollider>().FirstOrDefault()?.Destroy(); // visual only; don't add nav mesh

// Perimeter walls, 420 tall.
Solid( new Vector3( 0, 1500f, 210f ), new Vector3( 3000, 40, 420 ), wallTint, "Wall North" );
Solid( new Vector3( 0, -1500f, 210f ), new Vector3( 3000, 40, 420 ), wallTint, "Wall South" );
Solid( new Vector3( -1500f, 0, 210f ), new Vector3( 40, 3000, 420 ), wallTint, "Wall West" );
Solid( new Vector3( 1500f, 0, 210f ), new Vector3( 40, 3000, 420 ), wallTint, "Wall East" );

// A cross of interior walls, each arm split with a wide doorway: four rooms off a central crossing.
Solid( new Vector3( 0, 870f, 210f ), new Vector3( 40, 1220, 420 ), wallTint, "Divider N" );
Solid( new Vector3( 0, -870f, 210f ), new Vector3( 40, 1220, 420 ), wallTint, "Divider S" );
Solid( new Vector3( 870f, 0, 210f ), new Vector3( 1220, 40, 420 ), wallTint, "Divider E" );
Solid( new Vector3( -870f, 0, 210f ), new Vector3( 1220, 40, 420 ), wallTint, "Divider W" );

// A few short cover stubs out in the rooms.
var stubs = new[] { new Vector3( 760, 640, 0 ), new Vector3( -760, -640, 0 ), new Vector3( 640, -760, 0 ), new Vector3( -640, 760, 0 ) };
foreach ( var st in stubs )
{
	var len = R( 320f, 520f );
	var across = rnd.Next( 2 ) == 0;
	Solid( st + Vector3.Up * 210f, across ? new Vector3( len, 40, 420 ) : new Vector3( 40, len, 420 ), wallTint, "Cover" );
}

// Ceiling beams and a baseboard trim, so the shell reads as a built space.
for ( var x = -1200f; x <= 1201f; x += 600f )
	Deco( new Vector3( x, 0, 408f ), new Vector3( 56, 2960, 24 ), beamTint, "Beam" );
for ( var y = -1200f; y <= 1201f; y += 600f )
	Deco( new Vector3( 0, y, 408f ), new Vector3( 2960, 56, 24 ), beamTint, "Beam" );
Deco( new Vector3( 0, 1466f, 12f ), new Vector3( 2960, 20, 24 ), beamTint, "Trim N" );
Deco( new Vector3( 0, -1466f, 12f ), new Vector3( 2960, 20, 24 ), beamTint, "Trim S" );
Deco( new Vector3( -1466f, 0, 12f ), new Vector3( 20, 2960, 24 ), beamTint, "Trim W" );
Deco( new Vector3( 1466f, 0, 12f ), new Vector3( 20, 2960, 24 ), beamTint, "Trim E" );

// Pillars at the room centres.
var pillarPositions = new[] { new Vector3( 900, 900, 0 ), new Vector3( -900, 900, 0 ), new Vector3( 900, -900, 0 ), new Vector3( -900, -900, 0 ) };
var solids = new System.Collections.Generic.List<(Vector3 At, float Radius)>();
foreach ( var pp in pillarPositions )
{
	Prop( pillar, pp, R( 0, 360 ), Vector3.One * R( 0.9f, 1.1f ), stoneTint, "Pillar" );
	solids.Add( (pp, 90f) );
}
foreach ( var st in stubs ) solids.Add( (st, 280f) );

// Wall furniture goes down first and reserves its footprint, so the loose props below never
// spawn on top of it.
var shelfSpots = new (Vector3 At, float Yaw)[] {
	(new Vector3( -1330, 760, 0 ), -90f), (new Vector3( -1330, -760, 0 ), -90f),
	(new Vector3( 1330, 760, 0 ), 90f), (new Vector3( 1330, -760, 0 ), 90f),
	(new Vector3( 760, 1330, 0 ), 180f), (new Vector3( -760, 1330, 0 ), 180f),
	(new Vector3( 760, -1330, 0 ), 0f), (new Vector3( -760, -1330, 0 ), 0f) };
foreach ( var (at, yaw) in shelfSpots )
{
	Prop( shelf, at, yaw, Vector3.One, woodTint * R( 0.9f, 1.1f ), "Shelf" );
	solids.Add( (at, 85f) );
}

var lockerSpots = new (Vector3 At, float Yaw)[] {
	(new Vector3( -1330, 420, 0 ), -90f), (new Vector3( -1330, 320, 0 ), -90f), (new Vector3( -1330, 220, 0 ), -90f),
	(new Vector3( 1330, -420, 0 ), 90f), (new Vector3( 1330, -320, 0 ), 90f), (new Vector3( 1330, -220, 0 ), 90f) };
foreach ( var (at, yaw) in lockerSpots )
{
	Prop( locker, at, yaw, Vector3.One, metalTint * R( 0.9f, 1.1f ), "Locker" );
	solids.Add( (at, 70f) );
}

// Finds a clear spot that fits a prop of the given footprint, away from walls and the central cross.
Vector3 Spot( float radius )
{
	for ( var i = 0; i < 400; i++ )
	{
		var p = new Vector3( R( -1240f, 1240f ), R( -1240f, 1240f ), 0 );
		if ( MathF.Abs( p.x ) < 350f + radius || MathF.Abs( p.y ) < 350f + radius ) continue; // keep the cross open
		var ok = true;
		foreach ( var (at, r) in solids ) if ( p.WithZ( 0 ).Distance( at.WithZ( 0 ) ) < r + radius ) { ok = false; break; }
		if ( ok ) return p;
	}
	return Vector3.Zero;
}

// Free-standing cover: crates, barrels, tables. Each reserves its footprint before the next.
for ( var i = 0; i < 20; i++ )
{
	var sc = R( 0.8f, 1.3f );
	var radius = 56f * sc;
	var p = Spot( radius );
	if ( p == Vector3.Zero ) continue;
	Prop( rnd.Next( 2 ) == 0 ? crateA : crateB, p, R( 0, 360 ), Vector3.One * sc, woodTint * R( 0.85f, 1.1f ), "Crate" );
	solids.Add( (p, radius) );
	if ( rnd.NextDouble() < 0.3 )
		Prop( crateB, p + Vector3.Up * 72f * sc, R( 0, 360 ), Vector3.One * sc * 0.8f, woodTint * R( 0.85f, 1.1f ), "Crate" );
}
for ( var i = 0; i < 12; i++ )
{
	var sc = R( 0.9f, 1.2f );
	var radius = 48f * sc;
	var p = Spot( radius );
	if ( p == Vector3.Zero ) continue;
	Prop( barrel, p, R( 0, 360 ), Vector3.One * sc, metalTint * R( 0.85f, 1.1f ), "Barrel" );
	solids.Add( (p, radius) );
}
for ( var i = 0; i < 6; i++ )
{
	var radius = 125f;
	var p = Spot( radius );
	if ( p == Vector3.Zero ) continue;
	Prop( table, p, R( 0, 360 ), Vector3.One * R( 0.95f, 1.1f ), woodTint * R( 0.9f, 1.1f ), "Table" );
	solids.Add( (p, radius) );
}

// Ceiling lights: shadow-casting over each room, soft fills in between.
void Lamp( Vector3 at, float radius, float intensity, bool shadows )
{
	var go = s.CreateObject();
	go.Name = "Ceiling Light";
	go.SetParent( dressing, false );
	go.WorldPosition = at + Vector3.Up * 400f;
	var pl = go.Components.Create<PointLight>();
	pl.Radius = radius;
	pl.Intensity = intensity;
	pl.LightColor = new Color( 1f, 0.93f, 0.8f );
	pl.Shadows = shadows;
}
foreach ( var at in new[] { new Vector3( 760, 760, 0 ), new Vector3( -760, 760, 0 ), new Vector3( 760, -760, 0 ), new Vector3( -760, -760, 0 ) } )
	Lamp( at, 1700f, 10f, true );
foreach ( var at in new[] { Vector3.Zero, new Vector3( 900, 0, 0 ), new Vector3( -900, 0, 0 ), new Vector3( 0, 900, 0 ), new Vector3( 0, -900, 0 ) } )
	Lamp( at, 1400f, 5f, false );

// Put the spawn points in the rooms, so nothing spawns inside a wall.
var spawnSpots = new[] {
	new Vector3( 1050, 1050, 0 ), new Vector3( 520, 1250, 0 ),
	new Vector3( 1050, -1050, 0 ), new Vector3( 520, -1250, 0 ),
	new Vector3( -1050, 1050, 0 ), new Vector3( -520, 1250, 0 ),
	new Vector3( -1050, -1050, 0 ), new Vector3( -520, -1250, 0 ) };
var spawns = s.Children.FirstOrDefault( g => g.Name == "Zombie Spawns" )?.Children.ToList();
if ( spawns is not null )
	for ( var i = 0; i < spawns.Count; i++ )
		spawns[i].WorldPosition = spawnSpots[i % spawnSpots.Length];

sess.HasUnsavedChanges = true;
sess.Save( false );
return "indoor dressing placed";

