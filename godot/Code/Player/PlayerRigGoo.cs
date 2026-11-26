using Godot;

/// <summary>
/// The Goo-Bot's suit: grape purple running with magenta goo drips and bubbles (`GooTexture`), and its own gear on the
/// rig: a glass tank of sloshing goo on its back with chrome caps and two hoses over the shoulders, and a goo drop
/// wobbling on an antenna on its head. The gear rides the spine and head bones (built in s&box character space,
/// inches, and placed from the bones' rest poses, so it moves with them).
/// </summary>
public partial class PlayerRig
{
	Node3D gooGear;
	Node3D gooSlosh;
	readonly List<Node> gooAttachments = new();

	/// <summary>
	/// One tile of the Goo-Bot's suit: glossy purple with a band of goo poured over the top that runs down in drips of
	/// different lengths with round beads at their ends, a shine down each drip, and bubbles here and there (wraps both ways).
	/// </summary>
	public static ImageTexture GooTexture( Color purple, Color goo, Color shine )
	{
		const int size = 256, drips = 6;
		var image = Image.CreateEmpty( size, size, false, Image.Format.Rgba8 );
		var centre = new float[drips]; var width = new float[drips]; var length = new float[drips];
		for ( var i = 0; i < drips; i++ )
		{
			centre[i] = (i + 0.2f + 0.6f * Hash( i, 1, 41 )) / drips;
			width[i] = 0.04f + 0.03f * Hash( i, 2, 41 );
			length[i] = 0.25f + 0.55f * Hash( i, 3, 41 );
		}
		for ( var y = 0; y < size; y++ )
			for ( var x = 0; x < size; x++ )
			{
				var u = x / (float)size;
				var v = y / (float)size;
				// A soft sheen across the purple.
				var c = purple.Lerp( purple.Lightened( 0.12f ), 0.5f + 0.5f * MathF.Sin( (u + v * 0.3f) * MathF.Tau * 2f ) );
				// The poured band along the top, with a wavy lower edge.
				var band = 0.11f + 0.025f * MathF.Sin( u * MathF.Tau * 3f ) + 0.015f * MathF.Sin( u * MathF.Tau * 7f + 1f );
				var inGoo = v < band;
				var edge = 0f;
				for ( var i = 0; i < drips && !inGoo; i++ )
				{
					var du = MathF.Abs( u - centre[i] );
					du = MathF.Min( du, 1f - du );
					var w = width[i] * (1f - 0.35f * v / length[i]);
					if ( v < length[i] && du < w ) { inGoo = true; edge = du / w; }
					// The bead at the end of the drip.
					var dv = v - length[i];
					var r = width[i] * 1.5f;
					if ( du * du + dv * dv < r * r ) { inGoo = true; edge = MathF.Sqrt( du * du + dv * dv ) / r; }
				}
				if ( inGoo )
				{
					c = goo;
					if ( edge > 0.75f ) c = goo.Darkened( 0.25f );
					else if ( edge < 0.3f && edge > 0.05f ) c = goo.Lerp( shine, 0.55f );
				}
				else
				{
					// Bubbles stuck in the gloss.
					var gx = u * 9f; var gy = v * 9f;
					int cx = (int)gx, cy = (int)gy;
					if ( Hash( cx, cy, 43 ) < 0.18f )
					{
						var bx = gx - cx - 0.5f; var by = gy - cy - 0.5f;
						var br = 0.12f + 0.18f * Hash( cx, cy, 44 );
						var d = MathF.Sqrt( bx * bx + by * by );
						if ( d < br ) c = purple.Lightened( 0.35f );
						if ( d < br && d > br * 0.7f ) c = purple.Lightened( 0.55f );
						if ( MathF.Abs( bx + br * 0.35f ) < br * 0.18f && MathF.Abs( by + br * 0.35f ) < br * 0.18f ) c = Colors.White;
					}
				}
				image.SetPixel( x, y, c );
			}
		image.GenerateMipmaps();
		return ImageTexture.CreateFromImage( image );
	}

	/// <summary> Builds the Goo-Bot's tank and antenna on the rig (or takes them off for another robot). </summary>
	void GooGear( bool on )
	{
		if ( !on )
		{
			if ( gooGear is not null && IsInstanceValid( gooGear ) ) gooGear.QueueFree();
			foreach ( var a in gooAttachments ) if ( IsInstanceValid( a ) ) a.QueueFree();
			gooAttachments.Clear();
			gooGear = null;
			return;
		}
		if ( gooGear is not null && IsInstanceValid( gooGear ) || skeleton is null ) return;
		gooGear = new Node3D { Name = "Goo Gear" };
		skeleton.AddChild( gooGear );
		var chrome = GunKit.Paint( new Color( 0.84f, 0.84f, 0.9f ), 0.2f, 0.9f );
		var plum = GunKit.Paint( new Color( 0.14f, 0.07f, 0.2f ), 0.5f, 0.3f );
		var glass = new StandardMaterial3D { AlbedoColor = new Color( 0.85f, 0.9f, 1f, 0.2f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f, RimEnabled = true, Rim = 0.7f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

		// The tank: a glass capsule on the back, goo inside, chrome caps, a plum strap frame and two hoses to the shoulders.
		var back = OnBone( "spine", new Vector3( -12.5f, 0f, 10f ) );   // behind the jetpack box
		if ( back is not null )
		{
			GunKit.Rod( back, new Vector3( 0f, 0f, -6.5f ), new Vector3( 0f, 0f, 6.5f ), 4.2f, glass, 20 );
			gooSlosh = new Node3D { Name = "Slosh" };
			back.AddChild( gooSlosh );
			var g = GooFx.Blob( gooSlosh, new Vector3( 0f, 0f, -1f ), 1f, GooFx.Goo, "Tank Goo" );
			g.Scale = new Vector3( 3.7f, 3.7f, 5.6f );
			g.SetInstanceShaderParameter( "wobble", 0.12f );
			foreach ( var z in new[] { -7f, 7f } ) GunKit.Rod( back, new Vector3( 0f, 0f, z - 1.2f ), new Vector3( 0f, 0f, z + 1.2f ), 4.6f, chrome, 20 );
			GunKit.Ball( back, new Vector3( 0f, 0f, 8.6f ), 1.6f, chrome );
			foreach ( var z in new[] { -3.5f, 3.5f } ) GunKit.Rod( back, new Vector3( 4f, -4.4f, z ), new Vector3( 4f, 4.4f, z ), 0.9f, plum, 8 );
			foreach ( var side in new[] { -1f, 1f } )
			{
				// A hose from the top cap over each shoulder, sagging a little.
				var from = new Vector3( 0f, side * 2.5f, 8f );
				var mid = new Vector3( 6f, side * 5.5f, 11f );
				var to = new Vector3( 13f, side * 7.5f, 7f );
				GunKit.Rod( back, from, mid, 0.75f, plum, 8 );
				GunKit.Rod( back, mid, to, 0.75f, plum, 8 );
				GunKit.Ball( back, mid, 0.85f, chrome );
			}
		}
		// The antenna: a thin chrome stalk off the top of the head with a drop of goo on the tip.
		var head = OnBone( "head", new Vector3( -1f, -2.5f, 8f ) );
		if ( head is not null )
		{
			GunKit.Rod( head, Vector3.Zero, new Vector3( -1f, 0f, 6f ), 0.35f, chrome, 8 );
			var drop = GooFx.Blob( head, new Vector3( -1.2f, 0f, 7.6f ), 1.5f, GooFx.Jelly, "Antenna Goo" );
			drop.Scale = new Vector3( 1.5f, 1.5f, 1.9f );
			drop.SetInstanceShaderParameter( "wobble", 0.2f );
		}
	}

	/// <summary>
	/// A node on a bone, placed `offset` inches from the bone in character space at rest and turned to character space,
	/// so what is built under it in character inches (X ahead, Y left, Z up) sits there and moves with the bone.
	/// </summary>
	Node3D OnBone( string bone, Vector3 offset, List<Node> into = null )
	{
		var b = skeleton.FindBone( bone );
		if ( b < 0 ) return null;
		// A bone attachment only follows its bone as a direct child of the skeleton.
		var attach = new BoneAttachment3D { Name = $"Goo {bone}", BoneName = bone };
		skeleton.AddChild( attach );
		(into ?? gooAttachments).Add( attach );
		var rest = skeleton.GetBoneGlobalRest( b );
		// Rig root space: the bone at rest, and where the gear should be (character inches turned into Godot metres).
		var restRig = K * rest;
		var want = new Transform3D( M * Basis.FromScale( Vector3.One * U.In ), restRig.Origin + M * offset * U.In );
		var node = new Node3D { Name = "Gear" };
		attach.AddChild( node );
		node.Transform = restRig.AffineInverse() * want;
		return node;
	}

	/// <summary> The tank's goo sloshes against the robot's moves. </summary>
	void SloshGoo( float dt )
	{
		if ( gooSlosh is null || !IsInstanceValid( gooSlosh ) ) return;
		var t = (float)Clock.Now;
		var speed = controller.Velocity.Length();
		gooSlosh.Rotation = new Vector3( MathF.Sin( t * 3.1f ) * 0.04f * (1f + speed * 0.2f), 0f, MathF.Sin( t * 2.3f ) * 0.05f * (1f + speed * 0.2f) );
	}
}
