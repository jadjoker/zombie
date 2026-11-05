// Dresses the arena as a sea floor: sand, cliffs, boulders, reefs, coral, kelp, pebbles.
// Editor scene only. Visual only: the original boxes keep their colliders (their renderers are hidden).
// Safe to rerun: it rebuilds the "Dressing" object each time.
if ( Game.IsPlaying ) return "playing - not touching anything";
var sess = Editor.SceneEditorSession.Active;
var s = sess.Scene;
var arena = s.Children.First( g => g.Name == "Arena" );
foreach ( var old in arena.Children.Where( g => g.Name == "Dressing" ).ToList() ) old.Destroy();
var dressing = s.CreateObject();
dressing.Name = "Dressing";
dressing.SetParent( arena, false );
var rnd = new System.Random( 1234 );
float R( float a, float b ) => a + (float)rnd.NextDouble() * (b - a);
var count = 0;

GameObject Put( string model, Vector3 pos, float yaw, Vector3 scale, Color tint, string name )
{
	var go = s.CreateObject();
	go.Name = name;
	go.SetParent( dressing, false );
	go.WorldPosition = pos;
	go.WorldRotation = new Angles( 0, yaw, 0 ).ToRotation();
	go.WorldScale = scale;
	var r = go.Components.Create<ModelRenderer>();
	r.Model = Model.Load( $"models/arena/{model}.vmdl" );
	r.Tint = tint;
	count++;
	return go;
}

var rockTints = new[] { new Color( 0.62f, 0.72f, 0.74f ), new Color( 0.7f, 0.66f, 0.6f ), new Color( 0.55f, 0.66f, 0.62f ) };
var blockers = new System.Collections.Generic.List<(Vector3 At, float Radius)>();
foreach ( var sp in s.Children.First( g => g.Name == "Zombie Spawns" ).Children ) blockers.Add( (sp.WorldPosition, 160f) );
foreach ( var bs in s.Children.First( g => g.Name == "Buy Stations" ).Children ) blockers.Add( (bs.WorldPosition, 140f) );
blockers.Add( (Vector3.Zero, 220f) ); // player spawn

foreach ( var r in arena.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ).ToList() )
{
	if ( r.GameObject.Parent == dressing || r.GameObject.IsDescendant( dressing ) ) continue;
	var name = r.GameObject.Name;
	var b = r.Bounds;
	if ( name == "Floor" )
	{
		r.Enabled = false;
		Put( "sand_floor", new Vector3( 0, 0, 0 ), 0, Vector3.One, Color.White, "Sand" );
	}
	else if ( name.StartsWith( "Wall" ) )
	{
		r.Enabled = false;
		// Cliffs stand just outside each wall, their inner face a little inside it.
		var alongX = b.Size.x > b.Size.y;
		var inward = alongX ? new Vector3( 0, -MathF.Sign( b.Center.y ), 0 ) : new Vector3( -MathF.Sign( b.Center.x ), 0, 0 );
		var yaw = Rotation.LookAt( inward ).Angles().yaw;
		for ( var i = -3; i <= 3; i++ )
		{
			var along = i * 480f + R( -40, 40 );
			var basePos = b.Center.WithZ( 0 ) - inward * 60f + (alongX ? new Vector3( along, 0, 0 ) : new Vector3( 0, along, 0 ));
			var sc = R( 0.85f, 1.15f );
			Put( (i & 1) == 0 ? "cliff0" : "cliff1", basePos, yaw + R( -10, 10 ), new Vector3( sc, sc, sc * R( 0.9f, 1.25f ) ), rockTints[rnd.Next( 3 )], "Cliff" );
		}
	}
	else if ( name.StartsWith( "Crate" ) )
	{
		r.Enabled = false;
		var size = b.Size.x;
		var sc = size / 100f;
		Put( $"boulder{rnd.Next( 3 )}", b.Center - Vector3.Up * size * 0.06f, R( 0, 360 ), new Vector3( sc, sc, sc * R( 0.9f, 1.1f ) ), rockTints[rnd.Next( 3 )], "Boulder" );
		blockers.Add( (b.Center.WithZ( 0 ), size * 0.8f) );
	}
	else if ( name.StartsWith( "Barricade" ) )
	{
		r.Enabled = false;
		var alongX = b.Size.x > b.Size.y;
		var length = MathF.Max( b.Size.x, b.Size.y );
		Put( "reef", b.Center.WithZ( 0 ), alongX ? 0 : 90, new Vector3( length / 300f, 0.85f, 1f ), rockTints[rnd.Next( 3 )], "Reef" );
		blockers.Add( (b.Center.WithZ( 0 ), length * 0.55f) );
	}
}

bool Free( Vector3 p, float pad )
{
	if ( MathF.Abs( p.x ) > 1420 || MathF.Abs( p.y ) > 1420 ) return false;
	foreach ( var (at, radius) in blockers ) if ( p.WithZ( 0 ).Distance( at.WithZ( 0 ) ) < radius + pad ) return false;
	return true;
}
Vector3 Find( float minR, float maxR, float pad )
{
	for ( var tries = 0; tries < 200; tries++ )
	{
		var a = R( 0, MathF.PI * 2 );
		var d = R( minR, maxR );
		var p = new Vector3( MathF.Cos( a ) * d, MathF.Sin( a ) * d, 0 );
		if ( Free( p, pad ) ) return p;
	}
	return Vector3.Zero;
}

// Kelp forests: clumps toward the edges, a couple further in.
var kelpTints = new[] { new Color( 0.35f, 0.62f, 0.28f ), new Color( 0.55f, 0.55f, 0.2f ), new Color( 0.3f, 0.5f, 0.35f ) };
for ( var c = 0; c < 11; c++ )
{
	var centre = Find( c < 8 ? 950 : 500, c < 8 ? 1400 : 900, 60 );
	if ( centre == Vector3.Zero ) continue;
	var n = rnd.Next( 3, 7 );
	for ( var k = 0; k < n; k++ )
	{
		var p = centre + new Vector3( R( -70, 70 ), R( -70, 70 ), -2 );
		if ( !Free( p, 20 ) ) continue;
		var sc = R( 0.7f, 1.3f );
		var kelp = Put( $"kelp{rnd.Next( 2 )}", p, R( 0, 360 ), new Vector3( sc, sc, sc * R( 0.8f, 1.3f ) ), kelpTints[rnd.Next( 3 )], "Kelp" );
		var sway = kelp.Components.Create<Sway>();
		sway.Amount = R( 4, 8 );
		sway.Period = R( 4, 7 );
	}
	blockers.Add( (centre, 90f) );
}

// Coral: bright pops of colour around the arena.
var coral = new[] { new Color( 1f, 0.42f, 0.5f ), new Color( 1f, 0.6f, 0.25f ), new Color( 0.62f, 0.4f, 0.95f ), new Color( 0.25f, 0.85f, 0.75f ), new Color( 1f, 0.35f, 0.75f ), new Color( 1f, 0.85f, 0.3f ) };
var coralModels = new[] { "coral_branch0", "coral_branch1", "coral_brain", "sponge" };
for ( var c = 0; c < 34; c++ )
{
	var p = Find( 300, 1400, 30 );
	if ( p == Vector3.Zero ) continue;
	var sc = R( 0.8f, 1.6f );
	Put( coralModels[rnd.Next( coralModels.Length )], p + Vector3.Down * 2, R( 0, 360 ), sc, coral[rnd.Next( coral.Length )], "Coral" );
	blockers.Add( (p, 40f * sc) );
}

// Pebble patches and loose small rocks.
for ( var c = 0; c < 30; c++ )
{
	var p = Find( 150, 1420, 10 );
	if ( p == Vector3.Zero ) continue;
	var sc = R( 0.7f, 1.5f );
	Put( "pebbles", p, R( 0, 360 ), new Vector3( sc, sc, sc ), rockTints[rnd.Next( 3 )], "Pebbles" );
}

sess.HasUnsavedChanges = true;
sess.Save( false );
return $"dressing placed: {count} pieces";
