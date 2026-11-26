using Godot;

/// <summary>
/// The Abyss's darkness (user's design: hard to see): the sun is all but gone, the ambient light and the murk drop to
/// a deep navy, the caustics and the open sea's light shafts and leviathans go. Light comes from the shaft onto the
/// statue, the glowing moss down the main tunnels, the glow bulbs, the ally spider's headlamp and each robot's hip
/// lantern (<see cref="HipLantern"/>, kept on every player while this node is in the level). <see cref="Leave"/>
/// puts the open sea's light back (Atlantis). <see cref="Darken"/> also sets the wreck site's half-way gloom.
/// </summary>
public partial class AbyssMood : Node
{
	public static AbyssMood Current { get; private set; }

	public static readonly Color Ambient = new( 0.1f, 0.14f, 0.24f );
	public const float AmbientEnergy = 0.55f;
	public static readonly Color Murk = new( 0.006f, 0.014f, 0.03f );
	public const float MurkEnd = 46f;
	public const float SunShare = 0.02f;

	/// <summary> How far toward the Abyss's darkness the wreck site goes (user's ask: half as dark, deeper each stage). </summary>
	public const float WreckDepth = 0.5f;
	/// <summary> The volumetric murk at full depth. </summary>
	const float MurkDensity = 0.03f;
	static readonly Color MurkAlbedo = new( 0.3f, 0.42f, 0.55f );
	static readonly Color MurkEmission = new( 0.002f, 0.004f, 0.009f );

	sealed class Saved
	{
		public Color Ambient, FogColour, VolAlbedo, VolEmission;
		public float AmbientEnergy, FogBegin, FogEnd, VolDensity, SunEnergy;
		public Godot.Environment.BGMode Background;
		public Color BackgroundColour;
	}
	/// <summary> The open sea's light (the shore's), taken the first time a stage darkens it. </summary>
	static Saved shore;

	static Godot.Environment Env( Node level ) =>
		level.FindChildren( "*", "WorldEnvironment", true, false ).OfType<WorldEnvironment>().FirstOrDefault()?.Environment;
	static DirectionalLight3D Sun( Node level ) => level.FindChildren( "*", "DirectionalLight3D", true, false ).OfType<DirectionalLight3D>().FirstOrDefault();

	/// <summary> A new level load starts in the open sea's light. </summary>
	public static void Reset() => shore = null;

	/// <summary>
	/// Sets how deep the light feels: 0 is the shore's, 1 the Abyss's, in between a mix (the wreck site is
	/// <see cref="WreckDepth"/>). Ambient, murk, sun and caustics all go the same share of the way.
	/// </summary>
	public static void Darken( Node3D level, float depth )
	{
		var env = Env( level );
		var sun = Sun( level );
		if ( shore is null && env is not null )
		{
			var dock = DockHouse.Current;
			shore = new Saved
			{
				Ambient = dock?.SeaAmbient?.Colour ?? env.AmbientLightColor,
				AmbientEnergy = dock?.SeaAmbient?.Energy ?? env.AmbientLightEnergy,
				FogColour = env.FogLightColor, FogBegin = env.FogDepthBegin, FogEnd = env.FogDepthEnd,
				VolDensity = env.VolumetricFogDensity, VolAlbedo = env.VolumetricFogAlbedo, VolEmission = env.VolumetricFogEmission,
				SunEnergy = sun?.LightEnergy ?? 1f, Background = env.BackgroundMode, BackgroundColour = env.BackgroundColor,
			};
		}
		if ( shore is null ) return;
		var t = Mathf.Clamp( depth, 0f, 1f );
		if ( env is not null )
		{
			var ambient = shore.Ambient.Lerp( Ambient, t );
			var energy = Mathf.Lerp( shore.AmbientEnergy, AmbientEnergy, t );
			if ( DockHouse.Current is { } dock ) dock.SeaAmbient = (ambient, energy);
			env.AmbientLightColor = ambient;
			env.AmbientLightEnergy = energy;
			env.FogLightColor = shore.FogColour.Lerp( Murk, t );
			env.FogDepthBegin = Mathf.Lerp( shore.FogBegin, 1f, t );
			env.FogDepthEnd = Mathf.Lerp( shore.FogEnd, MurkEnd, t );
			// Thicker murk that the lamps light up in cones and halos, and no glow of its own.
			env.VolumetricFogDensity = Mathf.Lerp( shore.VolDensity, MurkDensity, t );
			env.VolumetricFogAlbedo = shore.VolAlbedo.Lerp( MurkAlbedo, t );
			env.VolumetricFogEmission = shore.VolEmission.Lerp( MurkEmission, t );
			// The sky only goes once it is fully dark.
			env.BackgroundMode = t >= 1f ? Godot.Environment.BGMode.Color : shore.Background;
			env.BackgroundColor = t >= 1f ? Murk : shore.BackgroundColour;
		}
		if ( sun is not null ) sun.LightEnergy = shore.SunEnergy * Mathf.Lerp( 1f, SunShare, t );
		Caustics.SetStrength( Caustics.DefaultStrength * ( 1f - t ) );
	}

	public static void Enter( Node3D level )
	{
		if ( Current is not null && IsInstanceValid( Current ) ) return;
		Darken( level, 1f );
		if ( level.GetNodeOrNull<Node3D>( "Backdrop" ) is { } backdrop ) backdrop.Visible = false;
		Current = new AbyssMood { Name = "Abyss Mood" };
		level.AddChild( Current );
	}

	/// <summary> Out of the Abyss (to Atlantis): the open sea's light comes back, the lanterns go. </summary>
	public static void Leave( Node3D level )
	{
		// Held in a local: leaving the tree clears Current.
		if ( Current is { } mood && IsInstanceValid( mood ) ) { mood.GetParent()?.RemoveChild( mood ); mood.Free(); }
		Current = null;
		HipLantern.RemoveAll();
		if ( shore is null ) return;
		Darken( level, 0f );
		if ( level.GetNodeOrNull<Node3D>( "Backdrop" ) is { } backdrop ) backdrop.Visible = true;
	}

	public override void _ExitTree()
	{
		if ( Current == this ) Current = null;
	}

	public override void _Process( double delta )
	{
		// Every robot carries its lantern down here (joiners' and teammates' too).
		foreach ( var p in PlayerSurvivor.All )
			if ( p.Controller is { } c && IsInstanceValid( c ) ) HipLantern.Ensure( c );
	}
}
