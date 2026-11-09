using Godot;
using System.Text.Json;

/// <summary>
/// Builds a level converted from an s&box scene (Tools/godot/convert_scene.py writes levels/name.json): the sun,
/// ambient light, fog and sky, every box and model with its material and tint, colliders, kelp sway, enemy spawn
/// points, the wave director; then the player and the HUD. Root script of the level scenes.
/// </summary>
public partial class Level : Node3D
{
	[Export] public string LevelName = "water_level";

	public PlayerController Player { get; private set; }
	public static Level Current { get; private set; }

	static readonly Dictionary<string, Material> materials = new();
	readonly Dictionary<string, Node3D> byPath = new();
	static readonly Dictionary<string, PackedScene> models = new();
	static readonly Dictionary<string, ShaderMaterial> nature = new();
	ProceduralMap proceduralMap;

	/// <summary> A nature material (nature.gdshader) seen in this level, by its s&box name (bark, leaves...). </summary>
	public static ShaderMaterial NatureMaterial( string name ) => nature.TryGetValue( name, out var m ) ? m : null;

	public override void _Ready()
	{
		Current = this;
		var args = OS.GetCmdlineUserArgs();
		var i = Array.IndexOf( args, "--level" );
		if ( i >= 0 && i + 1 < args.Length ) LevelName = args[i + 1];

		var json = Godot.FileAccess.GetFileAsString( $"res://levels/{LevelName}.json" );
		using var doc = JsonDocument.Parse( json );
		var root = doc.RootElement;
		var settings = root.GetProperty( "settings" );
		BuildEnvironment( settings );

		foreach ( var o in root.GetProperty( "objects" ).EnumerateArray() ) Build( o );
		// The forest builds its procedural world in place of the hand-made arena (before the navmesh).
		proceduralMap?.Build( this, path => byPath.TryGetValue( path, out var n ) && IsInstanceValid( n ) ? n : null );
		Nav.Bake( this );

		var spawn = settings.TryGetProperty( "player_spawn", out var sp ) ? V3( sp ) : Vector3.Zero;
		Player = PlayerFactory.Create( this, spawn + Vector3.Up * 0.05f );
		if ( settings.TryGetProperty( "stylize", out var sty ) )
		{
			var ink = sty.GetProperty( "ink" );
			Player.Camera.AddChild( new GlobalStylize
			{
				Style = Enum.Parse<GlobalStylize.Look>( sty.GetProperty( "style" ).GetString() ),
				Strength = sty.GetProperty( "strength" ).GetSingle(),
				Outline = sty.GetProperty( "outline" ).GetSingle(),
				OutlineWidth = sty.GetProperty( "outline_width" ).GetSingle(),
				InkColor = new Color( ink[0].GetSingle(), ink[1].GetSingle(), ink[2].GetSingle() ),
				Bands = sty.GetProperty( "bands" ).GetSingle(),
				PixelSize = sty.GetProperty( "pixel_size" ).GetSingle(),
			} );
		}
		if ( WaveDirector.Current is null ) AddChild( new WaveDirector() );
		if ( !OS.GetCmdlineUserArgs().Contains( "--waves" ) && WaveDirector.Current is { } d ) d.SpawningPaused = true;
		if ( AutoTest.Requested ) AddChild( new AutoTest() );
	}

	static Vector3 V3( JsonElement e ) => new( e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle() );
	static Color Col( JsonElement e ) => new( e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e.GetArrayLength() > 3 ? e[3].GetSingle() : 1f );
	static Quaternion Quat( JsonElement e ) => new Quaternion( e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle() ).Normalized();

	void BuildEnvironment( JsonElement s )
	{
		var skyTint = s.TryGetProperty( "sky_tint", out var st ) ? Col( st ) : new Color( 0.5f, 0.6f, 0.7f );
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial { SkyTopColor = skyTint * 0.8f, SkyHorizonColor = skyTint * 1.3f, GroundHorizonColor = skyTint, GroundBottomColor = skyTint * 0.4f, SunAngleMax = 30f } },
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = s.TryGetProperty( "ambient", out var amb ) ? Col( amb ) : new Color( 0.2f, 0.2f, 0.2f ),
			AmbientLightEnergy = 3f,
			TonemapMode = Godot.Environment.ToneMapper.Aces,
			// s&box ColorAdjustments on the main camera.
			AdjustmentEnabled = true,
			AdjustmentContrast = 1.08f,
			AdjustmentSaturation = 1.05f,
			GlowEnabled = true,
			GlowIntensity = 0.6f,
			GlowBloom = 0.08f,
			SsaoEnabled = true,
		};
		if ( s.TryGetProperty( "fog", out var fog ) )
		{
			env.FogEnabled = true;
			env.FogMode = Godot.Environment.FogModeEnum.Depth;
			env.FogLightColor = Col( fog.GetProperty( "color" ) );
			env.FogDepthBegin = fog.GetProperty( "start" ).GetSingle();
			env.FogDepthEnd = fog.GetProperty( "end" ).GetSingle();
			env.FogDepthCurve = fog.GetProperty( "falloff" ).GetSingle();
			env.FogDensity = 1f;
			env.FogSkyAffect = 1f;
		}
		AddChild( new WorldEnvironment { Environment = env } );
		if ( s.TryGetProperty( "sun", out var sun ) )
		{
			AddChild( new DirectionalLight3D
			{
				Quaternion = Quat( sun.GetProperty( "rot" ) ),
				LightColor = Col( sun.GetProperty( "color" ) ),
				ShadowEnabled = sun.GetProperty( "shadows" ).GetBoolean(),
				DirectionalShadowMaxDistance = 60f,
			} );
		}
	}

	void Build( JsonElement o )
	{
		var node = new Node3D { Name = o.GetProperty( "name" ).GetString() };
		var parentPath = o.TryGetProperty( "parent", out var pp ) ? pp.GetString() : "";
		( byPath.TryGetValue( parentPath, out var parent ) ? parent : this ).AddChild( node );
		byPath[o.GetProperty( "path" ).GetString()] = node;
		node.Transform = new Transform3D( new Basis( Quat( o.GetProperty( "rot" ) ) ) * Basis.FromScale( V3( o.GetProperty( "scale" ) ) ), V3( o.GetProperty( "pos" ) ) );

		var tint = o.TryGetProperty( "tint", out var t ) ? Col( t ) : Colors.White;
		var shadows = !o.TryGetProperty( "shadows", out var sh ) || sh.GetBoolean();
		JsonElement? mat = o.TryGetProperty( "material", out var m ) && m.ValueKind == JsonValueKind.Object ? m : null;

		if ( o.TryGetProperty( "box", out _ ) || o.TryGetProperty( "sphere", out _ ) )
		{
			var box = o.TryGetProperty( "box", out _ );
			var mesh = new MeshInstance3D
			{
				Mesh = box ? new BoxMesh { Size = Vector3.One * 50f * U.In } : new SphereMesh { Radius = 32f * U.In, Height = 64f * U.In },
				MaterialOverride = MakeMaterial( mat, tint, triplanar: true ),
				CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
			};
			node.AddChild( mesh );
			TintNature( mesh, tint );
		}
		if ( o.TryGetProperty( "model", out var model ) && model.ValueKind == JsonValueKind.String )
		{
			var path = model.GetString();
			if ( !models.TryGetValue( path, out var scene ) ) models[path] = scene = ResourceLoader.Exists( path ) ? GD.Load<PackedScene>( path ) : null;
			if ( scene is not null )
			{
				var instance = scene.Instantiate<Node3D>();
				instance.Scale = Vector3.One * 2.54f;   // FBX at 0.01 per inch -> metres
				node.AddChild( instance );
				var material = MakeMaterial( mat, tint, triplanar: false );
				foreach ( var mesh in instance.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
				{
					mesh.MaterialOverride = material;
					mesh.CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
					TintNature( mesh, tint );
				}
			}
		}
		if ( o.TryGetProperty( "collider", out var col ) )
		{
			var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
			node.AddChild( body );
			body.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = V3( col.GetProperty( "size" ) ).Abs() * U.In }, Position = V3( col.GetProperty( "center" ) ) } );
		}
		if ( o.TryGetProperty( "sway", out var sway ) )
			node.AddChild( new Sway { Amount = sway.GetProperty( "amount" ).GetSingle(), Period = sway.GetProperty( "period" ).GetSingle() } );
		if ( o.TryGetProperty( "spawn_point", out _ ) ) node.AddToGroup( "spawn_point" );
		if ( o.TryGetProperty( "buy_station", out var bs ) )
		{
			node.AddChild( new BuyStation
			{
				Name = "BuyStation",
				Kind = Enum.Parse<BuyStationKind>( bs.GetProperty( "Kind" ).GetString() ),
				Title = bs.GetProperty( "Title" ).GetString(),
				Cost = bs.GetProperty( "Cost" ).GetInt32(),
				CostIncrease = bs.GetProperty( "CostIncrease" ).GetInt32(),
				MaxPurchases = bs.GetProperty( "MaxPurchases" ).GetInt32(),
				HealthPerPurchase = bs.GetProperty( "HealthPerPurchase" ).GetSingle(),
			} );
			node.AddToGroup( "buy_station" );
		}
		if ( o.TryGetProperty( "marine_snow", out _ ) ) AddChild( new MarineSnow() );
		if ( o.TryGetProperty( "dock_house", out var dock ) )
		{
			var house = new DockHouse();
			if ( dock.TryGetProperty( "SurfaceHeight", out var surf ) ) house.SurfaceHeight = surf.GetSingle();
			if ( dock.TryGetProperty( "DiveBloobers", out var db ) ) house.DiveBloobers = db.GetInt32();
			if ( dock.TryGetProperty( "PierLength", out var pl ) ) house.PierLength = pl.GetSingle();
			AddChild( house );
		}
		if ( o.TryGetProperty( "wave_director", out var wd ) && WaveDirector.Current is null )
		{
			var director = new WaveDirector();
			// The scene's own settings (open world, spawn mix, health and pacing), by name.
			if ( wd.ValueKind == JsonValueKind.Object ) ApplySettings( director, wd );
			AddChild( director );
			director.GlobalPosition = node.GlobalPosition;
		}
		if ( o.TryGetProperty( "procedural_map", out var pm ) )
		{
			proceduralMap = new ProceduralMap { Name = "Procedural World" };
			ApplySettings( proceduralMap, pm );
			AddChild( proceduralMap );
		}
		if ( o.TryGetProperty( "text", out var text ) && text.GetProperty( "text" ).GetString() is { Length: > 0 } words )
		{
			// s&box TextRenderer: world text, facing the camera.
			var c = Col( text.GetProperty( "color" ) );
			node.AddChild( new Label3D
			{
				Text = words,
				Modulate = c,
				FontSize = text.TryGetProperty( "size", out var fs ) ? fs.GetInt32() : 32,
				PixelSize = (text.TryGetProperty( "scale", out var sc ) ? sc.GetSingle() : 1f) * U.In,
				Billboard = !text.TryGetProperty( "billboard", out var bb ) || bb.GetBoolean() ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled,
				OutlineSize = 6,
				OutlineModulate = new Color( 0, 0, 0, 0.6f ),
			} );
		}
		if ( o.TryGetProperty( "point_light", out var pl2 ) )
		{
			// s&box light colours carry the brightness (above 1); Godot splits colour and energy.
			var c = Col( pl2.GetProperty( "color" ) );
			var energy = MathF.Max( c.R, MathF.Max( c.G, c.B ) );
			node.AddChild( new OmniLight3D
			{
				LightColor = energy > 0f ? new Color( c.R / energy, c.G / energy, c.B / energy ) : Colors.White,
				LightEnergy = energy * 0.3f,   // s&box brightness is not Godot energy: tuned so a lit floor keeps its texture
				OmniRange = pl2.GetProperty( "radius" ).GetSingle(),
				OmniAttenuation = pl2.GetProperty( "attenuation" ).GetSingle(),
				ShadowEnabled = pl2.GetProperty( "shadows" ).GetBoolean(),
			} );
		}
	}

	/// <summary> Sets public fields of a node from an s&box component's saved properties (matching names). </summary>
	static void ApplySettings( object target, JsonElement values )
	{
		var type = target.GetType();
		foreach ( var prop in values.EnumerateObject() )
		{
			var field = type.GetField( prop.Name );
			if ( field is null ) continue;
			var v = prop.Value;
			try
			{
				object value = field.FieldType switch
				{
					var t when t == typeof( float ) => v.GetSingle(),
					var t when t == typeof( int ) => v.GetInt32(),
					var t when t == typeof( bool ) => v.GetBoolean(),
					var t when t == typeof( string ) => v.GetString(),
					var t when t.IsEnum => Enum.Parse( t, v.GetString() ),
					_ => null,
				};
				if ( value is not null ) field.SetValue( target, value );
			}
			catch ( Exception e ) { GD.PrintErr( $"Level: setting {prop.Name}: {e.Message}" ); }
		}
	}

	static void TintNature( MeshInstance3D mesh, Color tint )
	{
		if ( mesh.MaterialOverride is ShaderMaterial { Shader: { } sh } && sh.ResourcePath.EndsWith( "nature.gdshader" ) )
			mesh.SetInstanceShaderParameter( "tint", new Vector3( tint.R, tint.G, tint.B ) );
	}

	static ShaderMaterial MakeNature( string name, JsonElement n )
	{
		if ( nature.TryGetValue( name, out var cached ) ) return cached;
		var m = new ShaderMaterial { Shader = GD.Load<Shader>( "res://assets/shaders/nature.gdshader" ) };
		foreach ( var prop in n.EnumerateObject() )
		{
			var v = prop.Value;
			if ( v.ValueKind == JsonValueKind.String ) m.SetShaderParameter( prop.Name, GD.Load<Texture2D>( v.GetString() ) );
			else if ( v.ValueKind == JsonValueKind.Number ) m.SetShaderParameter( prop.Name, v.GetSingle() );
			else if ( v.ValueKind == JsonValueKind.Array ) m.SetShaderParameter( prop.Name, new Color( v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle() ) );
		}
		nature[name] = m;
		return m;
	}

	static Material MakeMaterial( JsonElement? m, Color tint, bool triplanar )
	{
		if ( m is { } nm && nm.TryGetProperty( "nature", out var nat ) ) return MakeNature( nm.GetProperty( "name" ).GetString(), nat );
		if ( m is { } fm && fm.TryGetProperty( "shader", out var fx ) && fx.GetString() is { } fxName && fxName.StartsWith( "fx_" ) )
		{
			Material f = fxName switch
			{
				"fx_ghost" => FxMaterials.Ghost( tint ),
				"fx_dissolve" => FxMaterials.Dissolve( tint ),
				"fx_hologram" => FxMaterials.Hologram(),
				"fx_iridescent" => FxMaterials.Iridescent( tint ),
				"fx_glowbulb" => FxMaterials.GlowBulb( tint ),
				"fx_invisible" => FxMaterials.Invisible,
				_ => null,
			};
			if ( f is not null ) return f;
		}
		var key = (m?.GetProperty( "name" ).GetString() ?? "plain") + tint.ToHtml() + triplanar;
		if ( materials.TryGetValue( key, out var cached ) ) return cached;
		var material = new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.85f };
		if ( m is { } mat )
		{
			if ( mat.TryGetProperty( "tint", out var mt ) ) material.AlbedoColor = tint * new Color( mt[0].GetSingle(), mt[1].GetSingle(), mt[2].GetSingle() );
			if ( mat.TryGetProperty( "metal", out var metal ) ) material.Metallic = metal.GetSingle();
			if ( mat.TryGetProperty( "albedo", out var a ) ) material.AlbedoTexture = GD.Load<Texture2D>( a.GetString() );
			if ( mat.TryGetProperty( "normal", out var n ) ) { material.NormalEnabled = true; material.NormalTexture = GD.Load<Texture2D>( n.GetString() ); }
			if ( mat.TryGetProperty( "rough", out var r ) ) { material.RoughnessTexture = GD.Load<Texture2D>( r.GetString() ); material.Roughness = 1f; }
			var uv = mat.TryGetProperty( "uv", out var u ) ? u.GetSingle() : 1f;
			if ( material.AlbedoTexture is not null )
			{
				// The s&box models carry UVs; the dev boxes and anything without get world-space triplanar mapping.
				if ( triplanar ) { material.Uv1Triplanar = true; material.Uv1WorldTriplanar = true; material.Uv1Scale = Vector3.One * 0.4f; }
				else material.Uv1Scale = new Vector3( uv, uv, 1f );
			}
			var name = mat.GetProperty( "name" ).GetString();
			if ( name is "slime" or "jelly" or "eelskin" ) material.Roughness = 0.35f;
		}
		materials[key] = material;
		return material;
	}
}

/// <summary> Port of s&box Sway: gently rocks an object around its base, like kelp in a current. </summary>
public partial class Sway : Node
{
	public float Amount = 6f;
	public float Period = 5f;
	Basis rest;
	float phase;

	public override void _Ready()
	{
		rest = GetParent<Node3D>().Basis;
		phase = Rng.Float( 0f, MathF.PI * 2f );
		Period *= Rng.Float( 0.8f, 1.25f );
	}

	public override void _Process( double delta )
	{
		var t = (float)Clock.Now * MathF.PI * 2f / MathF.Max( 0.5f, Period ) + phase;
		var pitch = MathF.Sin( t ) * Amount;
		var roll = MathF.Sin( t * 0.63f + 1.7f ) * Amount * 0.6f;
		GetParent<Node3D>().Basis = rest * new Angles( pitch, 0f, roll ).ToBasis();
	}
}
