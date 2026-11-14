using Godot;

/// <summary>
/// Tiny red health bars over every enemy, with the health left as a number (playtest), and a damage number over it
/// when it is hit: hits within a second of each other add up into one number, which rises a little and fades out a
/// second after the last hit. Drawn on the HUD at each enemy's screen spot, smaller with distance, hidden when the
/// enemy is out of view, too far, or behind solid ground. Follows Health.Damaged, so every creature works the same.
/// </summary>
public partial class EnemyBars : Control
{
	/// <summary> How far off a bar is still shown (metres). </summary>
	public const float MaxDistance = 55f;
	/// <summary> How long a damage number stays after the last hit (seconds); hits inside it stack. </summary>
	public const float NumberLife = 1f;

	static readonly Color BarBack = new( 0f, 0f, 0f, 0.6f );
	static readonly Color BarFill = new( 0.92f, 0.12f, 0.1f );
	static readonly Color BarLag = new( 1f, 0.75f, 0.7f, 0.85f );
	static readonly Color DamageColour = new( 1f, 0.86f, 0.3f );

	sealed class Bar
	{
		public Creature Creature;
		public Health Health;
		public Action<DamageInfo> OnDamaged;
		/// <summary> The health it had last we looked (what a hit took is the drop from this). </summary>
		public float Known;
		/// <summary> A pale bar easing down behind the red one after a hit. </summary>
		public float Lag;
		/// <summary> Where the bar sits, from the creature's origin: just over the top of its body. </summary>
		public Vector3 Offset = Vector3.Up;
		public TimeSince SinceMeasured = 99f;
		public TimeSince SinceLook = 99f;
		public bool InSight = true;
		public Vector3 Anchor => Creature.GlobalPosition + Offset;
	}

	sealed class Number
	{
		public Creature Creature;
		public Vector3 Anchor;
		public float Total;
		public TimeSince SinceHit;
	}

	readonly Dictionary<Creature, Bar> bars = new();
	readonly List<Number> numbers = new();

	public static EnemyBars Create()
	{
		var bars = new EnemyBars { Name = "Enemy Bars", MouseFilter = MouseFilterEnum.Ignore };
		bars.SetAnchorsPreset( LayoutPreset.FullRect );
		return bars;
	}

	public override void _ExitTree()
	{
		foreach ( var bar in bars.Values ) Unhook( bar );
		bars.Clear();
	}

	static void Unhook( Bar bar )
	{
		if ( GodotObject.IsInstanceValid( bar.Health ) ) bar.Health.Damaged -= bar.OnDamaged;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		// New enemies get a bar once their health is set up; the dead and the gone lose theirs.
		foreach ( var creature in Creature.Everyone )
		{
			if ( !creature.IsEnemy || creature.IsDead || creature.Health is null || bars.ContainsKey( creature ) ) continue;
			var bar = new Bar { Creature = creature, Health = creature.Health, Known = creature.Health.Current, Lag = creature.Health.Current };
			bar.OnDamaged = damage => Hit( bar, damage );
			bar.Health.Damaged += bar.OnDamaged;
			bars[creature] = bar;
		}
		foreach ( var (creature, bar) in bars.ToList() )
		{
			if ( IsInstanceValid( creature ) && IsInstanceValid( bar.Health ) && !creature.IsDead && creature.IsEnemy ) continue;
			Unhook( bar );
			bars.Remove( creature );
		}

		var camera = GetViewport().GetCamera3D();
		Dictionary<Node3D, List<CreaturePart>> partsByCreature = null;
		foreach ( var bar in bars.Values )
		{
			// Healing (or a reset) moves the health up without a hit.
			if ( bar.Health.Current > bar.Known ) bar.Known = bar.Health.Current;
			bar.Lag = bar.Lag > bar.Health.Current ? MathX.Approach( bar.Lag, bar.Health.Current, MathF.Max( 1f, bar.Health.MaxHealth ) * 0.6f * dt ) : bar.Health.Current;
			if ( bar.SinceMeasured > 0.25f )
			{
				partsByCreature ??= PartsByCreature();
				Measure( bar, partsByCreature.GetValueOrDefault( bar.Creature ) );
			}
			if ( camera is not null && bar.SinceLook > 0.15f )
			{
				bar.SinceLook = 0f;
				bar.InSight = !Trace.Ray( camera, camera.GlobalPosition, bar.Anchor, Layers.World ).Hit;
			}
		}

		// Numbers follow their enemy while it lives, then stay where it was until they fade.
		numbers.RemoveAll( n => n.SinceHit > NumberLife );
		foreach ( var n in numbers )
			if ( IsInstanceValid( n.Creature ) && bars.TryGetValue( n.Creature, out var bar ) ) n.Anchor = bar.Anchor;
		QueueRedraw();
	}

	/// <summary> Every creature's shootable parts (many creatures keep them in a rig under the scene root, not under themselves). </summary>
	static Dictionary<Node3D, List<CreaturePart>> PartsByCreature()
	{
		var map = new Dictionary<Node3D, List<CreaturePart>>();
		foreach ( var part in CreaturePart.All )
		{
			if ( !IsInstanceValid( part ) || part.Creature is null ) continue;
			if ( !map.TryGetValue( part.Creature, out var list ) ) map[part.Creature] = list = new();
			list.Add( part );
		}
		return map;
	}

	/// <summary> Puts the bar just over the top of everything the creature is drawn with (its own node and its parts). </summary>
	static void Measure( Bar bar, List<CreaturePart> parts )
	{
		bar.SinceMeasured = 0f;
		var origin = bar.Creature.GlobalPosition;
		var top = float.MinValue;
		var sum = Vector3.Zero;
		var count = 0;
		var roots = new List<Node>( (IEnumerable<Node>)parts ?? Array.Empty<Node>() ) { bar.Creature };
		var seen = new HashSet<MeshInstance3D>();   // parts hang off parts: each mesh once
		foreach ( var mesh in roots.SelectMany( r => r.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() ) )
		{
			if ( !seen.Add( mesh ) || !mesh.IsVisibleInTree() || mesh.Mesh is null ) continue;
			if ( mesh.GetParent() is TridentBolt ) continue;   // a trident stuck in it isn't its body
			var box = mesh.GlobalTransform * mesh.GetAabb();
			top = MathF.Max( top, box.End.Y );
			sum += box.GetCenter();
			count++;
		}
		if ( count == 0 ) return;
		var middle = sum / count;
		bar.Offset = new Vector3( middle.X - origin.X, top - origin.Y + 0.3f, middle.Z - origin.Z );
	}

	void Hit( Bar bar, DamageInfo damage )
	{
		var health = bar.Health;
		// What the hit really took (a killing blow can't take more than was left).
		var taken = bar.Known - health.Current;
		if ( taken <= 0f ) taken = MathF.Min( damage.Damage, bar.Known );
		bar.Known = health.Current;
		if ( taken <= 0f ) return;
		var anchor = IsInstanceValid( bar.Creature ) ? bar.Anchor : damage.Position;
		var number = numbers.LastOrDefault( n => n.Creature == bar.Creature && n.SinceHit < NumberLife );
		if ( number is null )
		{
			number = new Number { Creature = bar.Creature };
			numbers.Add( number );
		}
		number.Total += taken;
		number.SinceHit = 0f;
		number.Anchor = anchor;
	}

	public override void _Draw()
	{
		if ( DevFlags.HideHud || PlayerSurvivor.LocalDead ) return;
		var camera = GetViewport().GetCamera3D();
		if ( camera is null ) return;
		var font = ThemeDB.FallbackFont;

		foreach ( var bar in bars.Values )
		{
			if ( !bar.InSight || !OnScreen( camera, bar.Anchor, out var at, out var scale ) ) continue;
			var width = 54f * scale;
			var height = MathF.Max( 3f, 6f * scale );
			var rect = new Rect2( at.X - width * 0.5f, at.Y - height, width, height );
			var max = MathF.Max( 1f, bar.Health.MaxHealth );
			DrawRect( rect.Grow( 1f ), BarBack );
			DrawRect( new Rect2( rect.Position, new Vector2( width * MathX.Clamp( bar.Lag / max, 0f, 1f ), height ) ), BarLag );
			DrawRect( new Rect2( rect.Position, new Vector2( width * MathX.Clamp( bar.Health.Current / max, 0f, 1f ), height ) ), BarFill );
			var size = (int)MathF.Round( MathX.Clamp( 13f * scale, 9f, 14f ) );
			Text( font, $"{MathF.Ceiling( bar.Health.Current )}", new Vector2( at.X, rect.Position.Y - 3f ), size, Colors.White );
		}

		foreach ( var n in numbers )
		{
			if ( !OnScreen( camera, n.Anchor, out var at, out var scale ) ) continue;
			var t = MathX.Clamp( (float)n.SinceHit / NumberLife, 0f, 1f );
			// Full for the first 60% of its second, then fading; drifting up as it goes.
			var alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
			var size = (int)MathF.Round( MathX.Clamp( 20f * scale, 13f, 22f ) );
			var pop = n.SinceHit < 0.08f ? 1.25f : 1f;   // a small punch on each hit
			Text( font, $"{MathF.Round( n.Total ):0}", new Vector2( at.X, at.Y - 22f * scale - t * 18f ), (int)(size * pop), DamageColour with { A = alpha } );
		}
	}

	/// <summary> The screen point for a world point in front of the camera and in range, and a size for its distance. </summary>
	bool OnScreen( Camera3D camera, Vector3 world, out Vector2 at, out float scale )
	{
		at = default;
		scale = 1f;
		var distance = camera.GlobalPosition.DistanceTo( world );
		if ( distance > MaxDistance || camera.IsPositionBehind( world ) ) return false;
		at = camera.UnprojectPosition( world );
		if ( !GetViewportRect().HasPoint( at ) ) return false;
		scale = MathX.Clamp( 9f / MathF.Max( 0.1f, distance ), 0.55f, 1f );
		return true;
	}

	void Text( Font font, string text, Vector2 bottomCentre, int size, Color colour )
	{
		var width = font.GetStringSize( text, HorizontalAlignment.Left, -1, size ).X;
		var at = new Vector2( bottomCentre.X - width * 0.5f, bottomCentre.Y );
		DrawStringOutline( font, at, text, HorizontalAlignment.Left, -1, size, Math.Max( 3, size / 4 ), new Color( 0f, 0f, 0f, colour.A * 0.85f ) );
		DrawString( font, at, text, HorizontalAlignment.Left, -1, size, colour );
	}
}
