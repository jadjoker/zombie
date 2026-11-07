// Dresses the arena as a forest clearing: grass ground, trees, bushes, boulders, grass tufts and hills.
// Editor scene only. Visual only. Rebuilds the "Dressing" object under "Arena". Safe to rerun.
if ( Game.IsPlaying ) return "playing - not touching anything";
var sess = Editor.SceneEditorSession.Active;
var s = sess.Scene;
var arena = s.Children.First( g => g.Name == "Arena" );
foreach ( var old in arena.Children.Where( g => g.Name == "Dressing" ).ToList() ) old.Destroy();

// The collision floor stays hidden; the visible ground is a big grass plane added below.
var floorR = arena.Children.FirstOrDefault( g => g.Name == "Floor" )?.Components.GetAll<ModelRenderer>().FirstOrDefault();
if ( floorR is not null ) floorR.Enabled = false;

// Daylight: warm sun, brighter sky bounce, thin haze instead of deep-water fog.
var sun = s.GetAllComponents<DirectionalLight>().FirstOrDefault();
if ( sun is not null )
{
	sun.Intensity = 1.8f;
	sun.LightColor = new Color( 1f, 0.95f, 0.85f );
	sun.SkyColor = new Color( 0.4f, 0.6f, 0.9f );
}
var amb = s.GetAllComponents<AmbientLight>().FirstOrDefault();
if ( amb is not null ) amb.Color = new Color( 0.56f, 0.62f, 0.66f );
var fog = s.GetAllComponents<GradientFog>().FirstOrDefault();
if ( fog is not null )
{
	fog.Color = new Color( 0.74f, 0.84f, 0.93f );
	fog.StartDistance = 2800f;
	fog.EndDistance = 18000f;
	fog.Height = 400f;
}
var cam = s.GetAllComponents<CameraComponent>().FirstOrDefault();
if ( cam is not null ) cam.BackgroundColor = new Color( 0.55f, 0.75f, 0.93f );

var dressing = s.CreateObject();
dressing.Name = "Dressing";
dressing.SetParent( arena, false );

var rnd = new System.Random( 82611 );
float R( float a, float b ) => a + (float)rnd.NextDouble() * (b - a);
var box = Model.Load( "models/dev/box.vmdl" );
var sphere = Model.Load( "models/dev/sphere.vmdl" );
Vector3 FitBox( Vector3 size ) { var b = box.Bounds.Size; return new Vector3( size.x / MathF.Max( b.x, 0.01f ), size.y / MathF.Max( b.y, 0.01f ), size.z / MathF.Max( b.z, 0.01f ) ); }
float FitSphere( float dia ) { return dia / MathF.Max( sphere.Bounds.Size.x, 0.01f ); }

GameObject Put( Model model, Vector3 pos, Rotation rot, Vector3 scale, Color tint, string name )
{
	var go = s.CreateObject();
	go.Name = name;
	go.SetParent( dressing, false );
	go.WorldPosition = pos;
	go.WorldRotation = rot;
	go.WorldScale = scale;
	var r = go.Components.Create<ModelRenderer>();
	r.Model = model;
	r.Tint = tint;
	return go;
}

var blockers = new System.Collections.Generic.List<(Vector3 At, float Radius)>();
foreach ( var sp in s.Children.First( g => g.Name == "Zombie Spawns" ).Children ) blockers.Add( (sp.WorldPosition, 160f) );
foreach ( var bs in s.Children.First( g => g.Name == "Buy Stations" ).Children ) blockers.Add( (bs.WorldPosition, 140f) );
blockers.Add( (Vector3.Zero, 280f) );
bool Free( Vector3 p, float pad )
{
	if ( MathF.Abs( p.x ) > 1420 || MathF.Abs( p.y ) > 1420 ) return false;
	foreach ( var (at, radius) in blockers ) if ( p.WithZ( 0 ).Distance( at.WithZ( 0 ) ) < radius + pad ) return false;
	return true;
}
Vector3 Find( float minR, float maxR, float pad )
{
	for ( var i = 0; i < 200; i++ )
	{
		var a = R( 0, MathF.PI * 2 );
		var d = R( minR, maxR );
		var p = new Vector3( MathF.Cos( a ) * d, MathF.Sin( a ) * d, 0 );
		if ( Free( p, pad ) ) return p;
	}
	return Vector3.Zero;
}

var trunkTints = new[] { new Color( 0.32f, 0.22f, 0.13f ), new Color( 0.28f, 0.2f, 0.15f ), new Color( 0.4f, 0.28f, 0.17f ) };
var leafTints = new[] { new Color( 0.2f, 0.5f, 0.18f ), new Color( 0.28f, 0.6f, 0.22f ), new Color( 0.16f, 0.42f, 0.16f ), new Color( 0.4f, 0.62f, 0.24f ), new Color( 0.24f, 0.48f, 0.3f ) };
var rockTints = new[] { new Color( 0.46f, 0.47f, 0.45f ), new Color( 0.54f, 0.52f, 0.48f ), new Color( 0.4f, 0.42f, 0.4f ) };
var grassTints = new[] { new Color( 0.3f, 0.6f, 0.22f ), new Color( 0.44f, 0.7f, 0.32f ), new Color( 0.26f, 0.5f, 0.2f ) };
var pineTints = new[] { new Color( 0.14f, 0.38f, 0.2f ), new Color( 0.18f, 0.44f, 0.24f ), new Color( 0.12f, 0.34f, 0.19f ) };
var flowerTints = new[] { new Color( 0.95f, 0.45f, 0.7f ), new Color( 0.98f, 0.85f, 0.35f ), new Color( 0.9f, 0.9f, 0.95f ), new Color( 0.7f, 0.5f, 0.95f ), new Color( 1f, 0.6f, 0.4f ) };

// Blender-made models (Tools/blender/forest.py).
var trunkA = Model.Load( "models/forest/tree_trunk_a.vmdl" );
var trunkB = Model.Load( "models/forest/tree_trunk_b.vmdl" );
var trunkC = Model.Load( "models/forest/tree_trunk_c.vmdl" );
var canopyA = Model.Load( "models/forest/tree_canopy_a.vmdl" );
var canopyB = Model.Load( "models/forest/tree_canopy_b.vmdl" );
var canopyC = Model.Load( "models/forest/tree_canopy_c.vmdl" );
var rockModelA = Model.Load( "models/forest/rock_a.vmdl" );
var rockModelB = Model.Load( "models/forest/rock_b.vmdl" );
var rockModelC = Model.Load( "models/forest/rock_c.vmdl" );
var bushModel = Model.Load( "models/forest/bush_a.vmdl" );
var grassModel = Model.Load( "models/forest/grass_a.vmdl" );
var logModel = Model.Load( "models/forest/log_a.vmdl" );
var stumpModel = Model.Load( "models/forest/stump_a.vmdl" );
var fernModel = Model.Load( "models/forest/fern_a.vmdl" );
var flowerModel = Model.Load( "models/forest/flowerbush_a.vmdl" );

// Plants one tree: broadleaf (a/b) most of the time, a pine (c) now and then.
void PlantTree( Vector3 p, float scale )
{
	var kind = rnd.Next( 3 );
	if ( kind == 2 )
	{
		Put( trunkC, p, new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), new Vector3( scale, scale, scale * R( 0.9f, 1.15f ) ), trunkTints[rnd.Next( trunkTints.Length )], "Pine Trunk" );
		Put( canopyC, p + Vector3.Up * 560f * scale, new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), Vector3.One * scale * R( 0.9f, 1.15f ), pineTints[rnd.Next( pineTints.Length )], "Pine Canopy" );
	}
	else
	{
		var useB = kind == 1;
		var trunkTop = useB ? 500f : 385f;
		Put( useB ? trunkB : trunkA, p, new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), new Vector3( scale, scale, scale * R( 0.92f, 1.12f ) ), trunkTints[rnd.Next( trunkTints.Length )], "Tree Trunk" );
		Put( useB ? canopyB : canopyA, p + Vector3.Up * trunkTop * scale, new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), Vector3.One * scale * R( 0.85f, 1.1f ), leafTints[rnd.Next( leafTints.Length )], "Canopy" );
	}
	blockers.Add( (p, 66f * scale) );
}

// Ground: a big grass plane, wide enough to reach past the hills.
var groundGo = s.CreateObject();
groundGo.Name = "Ground";
groundGo.SetParent( dressing, false );
groundGo.WorldPosition = new Vector3( 0, 0, -10f );
groundGo.WorldScale = FitBox( new Vector3( 6800f, 6800f, 20f ) );
var groundR = groundGo.Components.Create<ModelRenderer>();
groundR.Model = box;
groundR.MaterialOverride = Material.Load( "materials/ground/forest_ground.vmat" );
groundR.Tint = Color.White;

// Trees: a dense forest edge, then a few scattered singles, so the middle stays open.
for ( var i = 0; i < 46; i++ )
{
	var p = Find( 900, 1420, 70 );
	if ( p != Vector3.Zero ) PlantTree( p, R( 0.85f, 1.4f ) );
}
for ( var i = 0; i < 15; i++ )
{
	var p = Find( 430, 880, 200 );
	if ( p != Vector3.Zero ) PlantTree( p, R( 0.8f, 1.25f ) );
}

// Bushes: a Blender leafy mound.
for ( var i = 0; i < 45; i++ )
{
	var p = Find( 190, 1400, 60 );
	if ( p == Vector3.Zero ) continue;
	Put( bushModel, p, new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), Vector3.One * R( 0.7f, 1.35f ), leafTints[rnd.Next( leafTints.Length )], "Bush" );
}

// Grass tufts: Blender clumps scattered in loose patches.
for ( var i = 0; i < 60; i++ )
{
	var p = Find( 150, 1400, 22 );
	if ( p == Vector3.Zero ) continue;
	for ( var g = rnd.Next( 3, 6 ); g > 0; g-- )
	{
		var at = p + new Vector3( R( -80f, 80f ), R( -80f, 80f ), 0 );
		if ( !Free( at, 6 ) ) continue;
		Put( grassModel, at, new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), Vector3.One * R( 0.8f, 1.5f ), grassTints[rnd.Next( grassTints.Length )], "Grass" );
	}
}

// Boulders and scattered rocks (Blender models). Their origin is the middle, so sit them in a little.
for ( var i = 0; i < 58; i++ )
{
	var p = Find( 150, 1400, 620 );
	if ( p == Vector3.Zero ) continue;
	var sc = R( 0.6f, 1.7f );
	Put( rnd.NextDouble() < 0.5 ? rockModelA : rockModelB, p + Vector3.Up * (50f * sc), new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), new Vector3( sc, sc, sc * R( 0.8f, 1.1f ) ), rockTints[rnd.Next( rockTints.Length )], "Boulder" );
	blockers.Add( (p, 55f * sc) );
}

// Fallen logs and stumps: woodland clutter.
for ( var i = 0; i < 10; i++ )
{
	var p = Find( 340, 1380, 130 );
	if ( p == Vector3.Zero ) continue;
	var sc = R( 0.8f, 1.4f );
	Put( logModel, p, new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), Vector3.One * sc, trunkTints[rnd.Next( trunkTints.Length )], "Log" );
	blockers.Add( (p, 95f * sc) );
}
for ( var i = 0; i < 11; i++ )
{
	var p = Find( 260, 1380, 110 );
	if ( p == Vector3.Zero ) continue;
	Put( stumpModel, p, new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), Vector3.One * R( 0.7f, 1.3f ), trunkTints[rnd.Next( trunkTints.Length )], "Stump" );
}

// Understory ferns.
for ( var i = 0; i < 60; i++ )
{
	var p = Find( 230, 1400, 50 );
	if ( p == Vector3.Zero ) continue;
	Put( fernModel, p, new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), Vector3.One * R( 0.7f, 1.35f ), leafTints[rnd.Next( leafTints.Length )], "Fern" );
}

// Flowering shrubs dotted across the clearing.
for ( var i = 0; i < 75; i++ )
{
	var p = Find( 180, 1400, 45 );
	if ( p == Vector3.Zero ) continue;
	Put( flowerModel, p, new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), Vector3.One * R( 0.7f, 1.25f ), flowerTints[rnd.Next( flowerTints.Length )], "Flowerbush" );
}

// Small stones.
for ( var i = 0; i < 46; i++ )
{
	var p = Find( 150, 1400, 40 );
	if ( p == Vector3.Zero ) continue;
	var sc = R( 0.7f, 1.5f );
	Put( rockModelC, p + Vector3.Up * (28f * sc), new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), Vector3.One * sc, rockTints[rnd.Next( rockTints.Length )], "Stone" );
}

// Hills: big green mounds ringing the clearing, just past the walls, so the horizon is soft.
for ( var i = 0; i < 15; i++ )
{
	var ang = i / 15f * MathF.PI * 2f + R( -0.08f, 0.08f );
	var dist = R( 2300f, 2950f );
	var centre = new Vector3( MathF.Cos( ang ) * dist, MathF.Sin( ang ) * dist, 0 );
	var w = R( 1400f, 2200f );
	var hh = R( 650f, 1150f );
	Put( rockModelB, centre + Vector3.Up * (hh * 0.05f), new Angles( 0, R( 0, 360 ), 0 ).ToRotation(), new Vector3( w / 210f, w / 210f, hh / 170f ), new Color( 0.34f, 0.5f, 0.32f ), "Hill" );
}

sess.HasUnsavedChanges = true;
sess.Save( false );
return "forest dressing placed";
