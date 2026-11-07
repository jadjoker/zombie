// Visual pass: one shared post-process stack on every scene's main camera, per-scene lighting,
// sky, fog and ground materials. Editor only, safe to rerun. Runs on every open scene session
// (indoor, forest, water_level); save them afterwards. Rerun it after any *_dressing.cs script. Ground textures come from Tools/textures/gen_ground.py.
if ( Game.IsPlaying ) return "playing - not touching anything";
var done = new System.Collections.Generic.List<string>();

T Ensure<T>( GameObject go ) where T : Component, new()
{
	var c = go.Components.Get<T>( true ) ?? go.Components.Create<T>();
	c.Enabled = true;
	return c;
}

foreach ( var sess in Editor.SceneEditorSession.All )
{
	var s = sess.Scene;
	var name = s.Name;
	if ( name is not ("indoor" or "forest" or "water_level") ) continue;

	var cam = s.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera );
	var sun = s.GetAllComponents<DirectionalLight>().FirstOrDefault();
	var amb = s.GetAllComponents<AmbientLight>().FirstOrDefault();
	var fog = s.GetAllComponents<GradientFog>().FirstOrDefault();
	if ( cam is null || sun is null ) continue;
	var cg = cam.GameObject;

	// ---- Shared post stack ----
	var tm = Ensure<Tonemapping>( cg );
	tm.Mode = Tonemapping.TonemappingMode.ACES;
	tm.AutoExposureEnabled = false;
	tm.ExposureCompensation = 0f;

	var bloom = Ensure<Bloom>( cg );
	bloom.Mode = SceneCamera.BloomAccessor.BloomMode.Additive;
	bloom.Threshold = 1.0f;
	bloom.Strength = 0.3f;

	var ao = Ensure<AmbientOcclusion>( cg );
	ao.Intensity = 1.0f;
	ao.Radius = 32;
	ao.FalloffRange = 1;

	var sharpen = Ensure<Sharpen>( cg );
	sharpen.Scale = 0.12f;

	var vig = Ensure<Vignette>( cg );
	vig.Color = Color.Black;
	vig.Intensity = 0.28f;
	vig.Smoothness = 0.85f;
	vig.Roundness = 0.6f;

	var grain = Ensure<FilmGrain>( cg );
	grain.Intensity = 0.03f;
	grain.Response = 0.6f;

	var adj = Ensure<ColorAdjustments>( cg );
	adj.Blend = 1f;
	adj.HueRotate = 0f;

	// Every scene gets a sky so reflections and sky light have something to sample.
	var skyGo = s.Children.FirstOrDefault( g => g.Name == "Sky" ) ?? s.CreateObject();
	skyGo.Name = "Sky";
	var sky = Ensure<SkyBox2D>( skyGo );

	foreach ( var r in s.GetAllComponents<ModelRenderer>() )
	{
		var b = r.Bounds.Size;
		string mat = null;
		if ( name == "forest" && r.GameObject.Name == "Ground" ) mat = "forest_ground";
		if ( name == "water_level" && r.GameObject.Name == "Sand" ) mat = "seabed";
		if ( name == "indoor" && r.MaterialOverride?.ResourcePath is { } p && (p.Contains( "concrete" ) || p.Contains( "ground/" )) )
		{
			if ( r.GameObject.Name == "Floor" ) mat = "indoor_floor";
			else if ( r.GameObject.Name == "Ceiling" ) mat = "indoor_ceiling";
			else if ( b.z > 400 && MathF.Max( b.x, b.y ) > 2000 ) mat = "indoor_wall_long";
			else if ( b.z > 400 ) mat = "indoor_wall_short";
			else if ( r.GameObject.Name.StartsWith( "Trim" ) ) mat = "indoor_trim";
		}
		if ( mat is null ) continue;
		r.MaterialOverride = Material.Load( $"materials/ground/{mat}.vmat" );
		r.Tint = mat == "indoor_trim" ? new Color( 0.45f, 0.45f, 0.5f ) : Color.White;
	}

	if ( name == "forest" )
	{
		// Late-morning sun: warm key, cool sky fill, crisp shadows, soft blue haze.
		sun.WorldRotation = Rotation.From( 48f, 35f, 0f );
		sun.LightColor = new Color( 1f, 0.9f, 0.76f );
		sun.Intensity = 3.4f;
		sun.SkyColor = new Color( 0.30f, 0.42f, 0.58f );
		sun.Shadows = true;
		sun.ShadowCascadeCount = 4;
		sun.ShadowHardness = 0.3f;
		if ( amb is not null ) amb.Color = new Color( 0.42f, 0.46f, 0.52f );
		sky.SkyMaterial = Material.Load( "materials/skybox/skybox_day_01.vmat" );
		sky.Tint = Color.White;
		sky.SkyIndirectLighting = true;
		cam.BackgroundColor = new Color( 0.62f, 0.76f, 0.9f );
		if ( fog is not null )
		{
			fog.Enabled = true;
			fog.Color = new Color( 0.66f, 0.76f, 0.86f );
			fog.Height = 900;
			fog.StartDistance = 1200;
			fog.EndDistance = 11000;
			fog.FalloffExponent = 1.4f;
			fog.VerticalFalloffExponent = 1.2f;
		}
		adj.Saturation = 1.06f;
		adj.Contrast = 1.05f;
		adj.Brightness = 1.0f;
	}
	else if ( name == "indoor" )
	{
		// Interior: no sun, low cool ambient, warm ceiling lights doing the work, dusty air.
		sun.Intensity = 0f;
		sun.Shadows = false;
		sun.SkyColor = new Color( 0.03f, 0.035f, 0.045f );
		if ( amb is not null ) amb.Color = new Color( 0.30f, 0.31f, 0.36f );
		sky.SkyMaterial = Material.Load( "materials/skybox/skybox_dark_01.vmat" );
		sky.Tint = new Color( 0.35f, 0.35f, 0.38f );
		sky.SkyIndirectLighting = false;
		cam.BackgroundColor = new Color( 0.02f, 0.02f, 0.025f );
		foreach ( var pl in s.GetAllComponents<PointLight>() )
		{
			pl.LightColor = new Color( 1f, 0.86f, 0.68f ) * 4f;
			pl.Radius = 1500;
			pl.Attenuation = 1f;
			pl.Shadows = true;
		}
		if ( fog is not null )
		{
			fog.Enabled = true;
			fog.Color = new Color( 0.10f, 0.10f, 0.11f );
			fog.Height = 600;
			fog.StartDistance = 300;
			fog.EndDistance = 5000;
			fog.FalloffExponent = 1.5f;
			fog.VerticalFalloffExponent = 1f;
		}
		// Box-projected reflections so the polished floor picks up the lights.
		var probeGo = s.Children.FirstOrDefault( g => g.Name == "Reflections" ) ?? s.CreateObject();
		probeGo.Name = "Reflections";
		probeGo.WorldPosition = new Vector3( 0, 0, 200 );
		var probe = Ensure<EnvmapProbe>( probeGo );
		probe.Mode = EnvmapProbe.EnvmapProbeMode.Realtime;
		probe.Projection = SceneCubemap.ProjectionMode.Box;
		probe.Bounds = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 3000, 3000, 420 ) );
		probe.UpdateStrategy = EnvmapProbe.CubemapDynamicUpdate.OnEnabled;
		probe.Feathering = 0.05f;
		adj.Saturation = 0.95f;
		adj.Contrast = 1.06f;
		adj.Brightness = 1.0f;
	}
	else if ( name == "water_level" )
	{
		// Underwater: light from straight above, cyan cast, short blue-green visibility.
		sun.WorldRotation = Rotation.From( 72f, 35f, 0f );
		sun.LightColor = new Color( 0.78f, 0.95f, 1f );
		sun.Intensity = 2.0f;
		sun.SkyColor = new Color( 0.10f, 0.28f, 0.36f );
		sun.Shadows = true;
		sun.ShadowHardness = 0f;
		if ( amb is not null ) amb.Color = new Color( 0.07f, 0.16f, 0.2f );
		sky.SkyMaterial = Material.Load( "materials/skybox/skybox_overcast_01.vmat" );
		sky.Tint = new Color( 0.12f, 0.38f, 0.48f );
		sky.SkyIndirectLighting = true;
		cam.BackgroundColor = new Color( 0.05f, 0.24f, 0.32f );
		if ( fog is not null )
		{
			fog.Enabled = true;
			fog.Color = new Color( 0.05f, 0.26f, 0.34f );
			fog.Height = 6000;
			fog.StartDistance = 150;
			fog.EndDistance = 3800;
			fog.FalloffExponent = 0.8f;
			fog.VerticalFalloffExponent = 1f;
		}
		bloom.Strength = 0.4f;
		adj.Saturation = 1.05f;
		adj.Contrast = 1.08f;
		adj.Brightness = 1.0f;
	}

	sess.HasUnsavedChanges = true;
	done.Add( name );
}
return "visual pass applied (not saved): " + string.Join( ", ", done );
