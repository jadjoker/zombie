using Godot;

/// <summary>
/// The Abyss's darkness (user's design: hard to see): the sun is all but gone, the ambient light and the murk drop to
/// a deep navy, the caustics and the open sea's light shafts and leviathans go. Light comes from the shaft onto the
/// statue, the glowing moss down the main tunnels, the glow bulbs, the ally spider's headlamp and each robot's hip
/// lantern (<see cref="HipLantern"/>, kept on every player while this node is in the level). <see cref="Leave"/>
/// puts the open sea's light back (Atlantis).
/// </summary>
public partial class AbyssMood : Node
{
	public static AbyssMood Current { get; private set; }

	public static readonly Color Ambient = new( 0.1f, 0.14f, 0.24f );
	public const float AmbientEnergy = 0.55f;
	public static readonly Color Murk = new( 0.006f, 0.014f, 0.03f );
	public const float MurkEnd = 46f;
	public const float SunShare = 0.02f;

	sealed class Saved
	{
		public Color Ambient, FogColour, VolAlbedo, VolEmission;
		public float AmbientEnergy, FogBegin, FogEnd, VolDensity, SunEnergy;
		public Godot.Environment.BGMode Background;
		public Color BackgroundColour;
	}
	static Saved saved;

	static Godot.Environment Env( Node level ) =>
		level.FindChildren( "*", "WorldEnvironment", true, false ).OfType<WorldEnvironment>().FirstOrDefault()?.Environment;
	static DirectionalLight3D Sun( Node level ) => level.FindChildren( "*", "DirectionalLight3D", true, false ).OfType<DirectionalLight3D>().FirstOrDefault();

	public static void Enter( Node3D level )
	{
		if ( Current is not null && IsInstanceValid( Current ) ) return;
		var env = Env( level );
		var sun = Sun( level );
		if ( env is not null )
		{
			var dock = DockHouse.Current;
			saved = new Saved
			{
				Ambient = dock?.SeaAmbient?.Colour ?? env.AmbientLightColor,
				AmbientEnergy = dock?.SeaAmbient?.Energy ?? env.AmbientLightEnergy,
				FogColour = env.FogLightColor, FogBegin = env.FogDepthBegin, FogEnd = env.FogDepthEnd,
				VolDensity = env.VolumetricFogDensity, VolAlbedo = env.VolumetricFogAlbedo, VolEmission = env.VolumetricFogEmission,
				SunEnergy = sun?.LightEnergy ?? 1f, Background = env.BackgroundMode, BackgroundColour = env.BackgroundColor,
			};
			if ( dock is not null ) dock.SeaAmbient = (Ambient, AmbientEnergy);
			env.AmbientLightColor = Ambient;
			env.AmbientLightEnergy = AmbientEnergy;
			env.FogLightColor = Murk;
			env.FogDepthBegin = 1f;
			env.FogDepthEnd = MurkEnd;
			// Thicker murk that the lamps light up in cones and halos, and no glow of its own.
			env.VolumetricFogDensity = 0.03f;
			env.VolumetricFogAlbedo = new Color( 0.3f, 0.42f, 0.55f );
			env.VolumetricFogEmission = new Color( 0.002f, 0.004f, 0.009f );
			env.BackgroundMode = Godot.Environment.BGMode.Color;
			env.BackgroundColor = Murk;
		}
		if ( sun is not null ) sun.LightEnergy = saved.SunEnergy * SunShare;
		Caustics.SetStrength( 0f );
		if ( level.GetNodeOrNull<Node3D>( "Backdrop" ) is { } backdrop ) backdrop.Visible = false;
		Current = new AbyssMood { Name = "Abyss Mood" };
		level.AddChild( Current );
	}

	public static void Leave( Node3D level )
	{
		if ( Current is not null && IsInstanceValid( Current ) ) { Current.GetParent()?.RemoveChild( Current ); Current.Free(); }
		Current = null;
		HipLantern.RemoveAll();
		if ( saved is null ) return;
		var env = Env( level );
		if ( env is not null )
		{
			if ( DockHouse.Current is { } dock ) dock.SeaAmbient = (saved.Ambient, saved.AmbientEnergy);
			env.AmbientLightColor = saved.Ambient;
			env.AmbientLightEnergy = saved.AmbientEnergy;
			env.FogLightColor = saved.FogColour;
			env.FogDepthBegin = saved.FogBegin;
			env.FogDepthEnd = saved.FogEnd;
			env.VolumetricFogDensity = saved.VolDensity;
			env.VolumetricFogAlbedo = saved.VolAlbedo;
			env.VolumetricFogEmission = saved.VolEmission;
			env.BackgroundMode = saved.Background;
			env.BackgroundColor = saved.BackgroundColour;
		}
		if ( Sun( level ) is { } sun ) sun.LightEnergy = saved.SunEnergy;
		Caustics.SetStrength( Caustics.DefaultStrength );
		if ( level.GetNodeOrNull<Node3D>( "Backdrop" ) is { } backdrop ) backdrop.Visible = true;
		saved = null;
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
