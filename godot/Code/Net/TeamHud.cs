using Godot;

/// <summary>
/// The co-op HUD, drawn in one Control (no nodes made per frame): every other player's name and health bar over their
/// robot (seen through walls, so a teammate is easy to find), ping markers, the Tab (pad: View) scoreboard of everyone in
/// the game (robot, health, points, kills, ping), and while dead online the teammate being watched (<see cref="Spectate"/>).
/// </summary>
public partial class TeamHud : Control
{
	PlayerController me;

	public static TeamHud Create( PlayerController player )
	{
		var hud = new TeamHud { Name = "TeamHud", me = player, MouseFilter = MouseFilterEnum.Ignore };
		hud.SetAnchorsPreset( LayoutPreset.FullRect );
		return hud;
	}

	public override void _Process( double delta )
	{
		if ( me is null || !IsInstanceValid( me ) ) return;
		if ( !PauseMenu.IsOpen && !DevMenu.IsOpen && !ClassPicker.IsOpen && Input.IsActionJustPressed( "ping" ) ) Pings.Place( me );
		Pings.Tick();
		QueueRedraw();
	}

	public override void _Draw()
	{
		if ( me is null || !IsInstanceValid( me ) ) return;
		var camera = GetViewport().GetCamera3D();
		if ( camera is null ) return;
		DrawTags( camera );
		Pings.Draw( this, camera );
		if ( Spectate.Watching is { } watched && IsInstanceValid( watched ) )
			Centered( $"WATCHING  {Net.PlayerName( watched.PeerId ).ToUpperInvariant()}   ·   click to switch", new Vector2( Size.X * 0.5f, Size.Y - 70f ), 20, UiStyle.Gold );
		if ( Input.IsActionPressed( "scoreboard" ) && !PauseMenu.IsOpen && !ClassPicker.IsOpen ) DrawBoard();
	}

	// ---- Names and health over teammates ----

	void DrawTags( Camera3D camera )
	{
		foreach ( var s in PlayerSurvivor.All )
		{
			if ( s.Controller is not { IsRemote: true } c || !IsInstanceValid( c ) ) continue;
			var head = c.GlobalPosition + Vector3.Up * 2.15f;
			if ( camera.IsPositionBehind( head ) ) continue;
			var at = camera.UnprojectPosition( head );
			var distance = camera.GlobalPosition.DistanceTo( head );
			var scale = MathX.Clamp( 12f / MathF.Max( 1f, distance ), 0.6f, 1f );
			var font = UiStyle.LabelBold;
			var size = (int)(18 * scale);
			var name = Net.PlayerName( c.PeerId );
			var width = font.GetStringSize( name, HorizontalAlignment.Left, -1, size ).X;
			var tint = c.Robot.Tint;
			DrawString( font, at + new Vector2( -width * 0.5f + 1f, 1f ), name, HorizontalAlignment.Left, -1, size, new Color( 0f, 0f, 0f, 0.7f ) );
			DrawString( font, at + new Vector2( -width * 0.5f, 0f ), name, HorizontalAlignment.Left, -1, size, s.IsAlive ? tint.Lightened( 0.3f ) : new Color( 0.6f, 0.6f, 0.6f ) );
			// The health bar under the name.
			var barW = 80f * scale;
			var barH = 7f * scale;
			var bar = new Rect2( at + new Vector2( -barW * 0.5f, 5f * scale ), new Vector2( barW, barH ) );
			DrawRect( bar.Grow( 1.5f ), new Color( 0f, 0f, 0f, 0.65f ) );
			var f = s.Health is { } h && h.MaxHealth > 0f ? MathX.Clamp( h.Current / h.MaxHealth, 0f, 1f ) : 0f;
			var colour = f > 0.5f ? new Color( 0.3f, 0.95f, 0.45f ) : f > 0.25f ? new Color( 1f, 0.8f, 0.25f ) : new Color( 1f, 0.3f, 0.25f );
			if ( s.IsAlive ) DrawRect( new Rect2( bar.Position, new Vector2( barW * f, barH ) ), colour );
			else Centered( "DOWN", at + new Vector2( 0f, 22f * scale ), (int)(14 * scale), new Color( 1f, 0.35f, 0.3f ) );
		}
	}

	// ---- The scoreboard ----

	record struct Row( string Name, RobotClass Robot, float Health, float MaxHealth, bool Alive, int Points, int Kills, int Ping, bool Me );

	List<Row> Rows()
	{
		var rows = new List<Row>();
		foreach ( var s in PlayerSurvivor.All )
		{
			if ( s.Controller is not { } c || !IsInstanceValid( c ) ) continue;
			var h = s.Health;
			if ( c.IsRemote )
			{
				var net = c.GetNodeOrNull<NetPlayer>( "NetPlayer" );
				rows.Add( new Row( Net.PlayerName( c.PeerId ), c.Robot, h?.Current ?? 0f, h?.MaxHealth ?? 1f, s.IsAlive, net?.Points ?? 0, net?.Kills ?? 0, net?.Ping ?? 0, false ) );
			}
			else rows.Add( new Row( GameSettings.PlayerNameOrDefault, c.Robot, h?.Current ?? 0f, h?.MaxHealth ?? 1f, s.IsAlive, s.Points, s.RunKills, Net.MyPing, true ) );
		}
		rows.Sort( ( a, b ) => b.Points.CompareTo( a.Points ) );
		return rows;
	}

	void DrawBoard()
	{
		var rows = Rows();
		const float rowH = 42f;
		var w = MathF.Min( 860f, Size.X - 40f );
		var h = 96f + rows.Count * rowH;
		var box = new Rect2( new Vector2( (Size.X - w) * 0.5f, Size.Y * 0.22f ), new Vector2( w, h ) );
		DrawRect( box, new Color( 0.02f, 0.05f, 0.09f, 0.88f ) );
		DrawRect( box, UiStyle.Teal with { A = 0.8f }, false, 2f );
		var wave = WaveDirector.Current is { } d ? $"WAVE {d.Wave}" : "";
		var title = Net.Online ? $"PLAYERS  {rows.Count}/{Net.MaxPlayers}" : "PLAYERS";
		DrawString( UiStyle.Display, box.Position + new Vector2( 24f, 40f ), title, HorizontalAlignment.Left, -1, 28, UiStyle.Gold );
		if ( wave != "" ) DrawString( UiStyle.Display, box.Position + new Vector2( w - 24f - UiStyle.Display.GetStringSize( wave, HorizontalAlignment.Left, -1, 28 ).X, 40f ), wave, HorizontalAlignment.Left, -1, 28, UiStyle.Gold );
		// Columns: name, robot, health, points, kills, ping.
		float[] x = { 24f, w * 0.30f, w * 0.48f, w * 0.68f, w * 0.80f, w * 0.90f };
		string[] heads = { "NAME", "ROBOT", "HEALTH", "POINTS", "KILLS", "PING" };
		var top = box.Position.Y + 68f;
		for ( var i = 0; i < heads.Length; i++ ) DrawString( UiStyle.Caption, new Vector2( box.Position.X + x[i], top ), heads[i], HorizontalAlignment.Left, -1, 13, UiStyle.Readout );
		var y = top + 10f;
		foreach ( var r in rows )
		{
			var line = new Rect2( box.Position.X + 12f, y, w - 24f, rowH - 6f );
			if ( r.Me ) DrawRect( line, UiStyle.Teal with { A = 0.12f } );
			var baseY = y + rowH * 0.62f;
			var text = r.Alive ? Colors.White : new Color( 0.6f, 0.6f, 0.6f );
			DrawString( UiStyle.LabelBold, new Vector2( box.Position.X + x[0], baseY ), r.Name + (r.Alive ? "" : "  (down)"), HorizontalAlignment.Left, x[1] - x[0] - 8f, 20, text );
			DrawString( UiStyle.LabelBold, new Vector2( box.Position.X + x[1], baseY ), r.Robot.Name, HorizontalAlignment.Left, x[2] - x[1] - 8f, 18, r.Robot.Tint.Lightened( 0.2f ) );
			var barW = x[3] - x[2] - 24f;
			var bar = new Rect2( box.Position.X + x[2], y + rowH * 0.32f, barW, 10f );
			DrawRect( bar, new Color( 0f, 0f, 0f, 0.6f ) );
			var f = r.MaxHealth > 0f ? MathX.Clamp( r.Health / r.MaxHealth, 0f, 1f ) : 0f;
			if ( r.Alive ) DrawRect( new Rect2( bar.Position, new Vector2( barW * f, 10f ) ), f > 0.5f ? new Color( 0.3f, 0.95f, 0.45f ) : f > 0.25f ? new Color( 1f, 0.8f, 0.25f ) : new Color( 1f, 0.3f, 0.25f ) );
			DrawString( UiStyle.Mono, new Vector2( box.Position.X + x[3], baseY ), $"{r.Points}", HorizontalAlignment.Left, -1, 20, UiStyle.Gold );
			DrawString( UiStyle.Mono, new Vector2( box.Position.X + x[4], baseY ), $"{r.Kills}", HorizontalAlignment.Left, -1, 20, Colors.White );
			DrawString( UiStyle.Mono, new Vector2( box.Position.X + x[5], baseY ), Net.Online ? (r.Ping > 0 ? $"{r.Ping}" : "host") : "-", HorizontalAlignment.Left, -1, 18, UiStyle.Readout );
			y += rowH;
		}
	}

	public void Centered( string text, Vector2 at, int size, Color colour )
	{
		var font = UiStyle.LabelBold;
		var width = font.GetStringSize( text, HorizontalAlignment.Left, -1, size ).X;
		DrawString( font, at + new Vector2( -width * 0.5f + 1f, 1f ), text, HorizontalAlignment.Left, -1, size, new Color( 0f, 0f, 0f, 0.7f ) );
		DrawString( font, at + new Vector2( -width * 0.5f, 0f ), text, HorizontalAlignment.Left, -1, size, colour );
	}
}

/// <summary>
/// Ping markers (middle mouse; the pad's left stick click): points at whatever is under the crosshair, an enemy (it
/// follows the enemy, red) or a spot (yellow), for everyone in the game, with who pinged it and how far it is. Off
/// screen it sits at the edge pointing the way. One per player at a time.
/// </summary>
public static class Pings
{
	public const float Life = 7f, Range = 250f;

	sealed class Mark { public long Peer; public Vector3 At; public int Creature; public double Until; }
	static readonly Dictionary<long, Mark> marks = new();
	static double lastPlaced;
	/// <summary> Tests: pings seen (placed here or sent from another player). </summary>
	public static int Seen;

	/// <summary> The local player pinged: under the crosshair, shown here and sent to everyone. </summary>
	public static void Place( PlayerController me )
	{
		if ( Clock.Real - lastPlaced < 0.35 || me.Camera is null ) return;
		lastPlaced = Clock.Real;
		var from = me.Camera.GlobalPosition;
		var forward = -me.Camera.GlobalBasis.Z;
		var tr = Trace.Ray( me, from, from + forward * Range, Layers.World | Layers.Creature, new Godot.Collections.Array<Rid> { me.GetRid() } );
		var at = tr.Hit ? tr.HitPosition : from + forward * 30f;
		var creature = tr.Hit && Shootables.Find( tr.Node ) is CreaturePart part && part.Creature is Creature { IsEnemy: true } c ? c : null;
		var id = creature?.NetId ?? 0;
		Show( Net.Online ? Net.Me : 0, at, id, creature );
		if ( Net.Online ) Net.Instance?.SendPing( at, id );
	}

	/// <summary> A ping arrives (or is placed here). </summary>
	public static void Show( long peer, Vector3 at, int creatureId, Creature local = null )
	{
		Seen++;
		marks[peer] = new Mark { Peer = peer, At = at, Creature = creatureId, Until = Clock.Real + Life };
		if ( local is not null && creatureId == 0 ) marks[peer].At = local.GlobalPosition + Vector3.Up * 1f;
		Sfx.Play2D( "pinball_ding", 0.55f, creatureId != 0 ? 1.5f : 1.2f );
	}

	public static void Tick()
	{
		List<long> gone = null;
		foreach ( var (peer, m) in marks )
		{
			if ( Clock.Real > m.Until ) { (gone ??= new()).Add( peer ); continue; }
			if ( m.Creature != 0 )
			{
				if ( NetCreatures.Find( m.Creature ) is { IsDead: false } c ) m.At = c.GlobalPosition + Vector3.Up * 1.2f;
				else { (gone ??= new()).Add( peer ); continue; }
			}
		}
		if ( gone is not null ) foreach ( var p in gone ) marks.Remove( p );
	}

	public static void Draw( TeamHud hud, Camera3D camera )
	{
		var size = hud.Size;
		foreach ( var m in marks.Values )
		{
			var enemy = m.Creature != 0;
			var colour = enemy ? new Color( 1f, 0.3f, 0.25f ) : new Color( 1f, 0.85f, 0.2f );
			var behind = camera.IsPositionBehind( m.At );
			var p = camera.UnprojectPosition( m.At );
			if ( behind ) p = size - p;   // behind: mirrored, then pushed to the edge
			var margin = 40f;
			var onScreen = !behind && p.X > margin && p.X < size.X - margin && p.Y > margin && p.Y < size.Y - margin;
			if ( !onScreen )
			{
				var centre = size * 0.5f;
				var dir = (p - centre).Normalized();
				if ( dir == Vector2.Zero ) dir = Vector2.Down;
				var t = MathF.Min( (size.X * 0.5f - margin) / MathF.Max( 0.001f, MathF.Abs( dir.X ) ), (size.Y * 0.5f - margin) / MathF.Max( 0.001f, MathF.Abs( dir.Y ) ) );
				p = centre + dir * t;
			}
			var pulse = 1f + 0.15f * MathF.Sin( (float)Clock.Real * 8f );
			var r = 11f * pulse;
			var diamond = new[] { p + new Vector2( 0, -r ), p + new Vector2( r, 0 ), p + new Vector2( 0, r ), p + new Vector2( -r, 0 ) };
			hud.DrawColoredPolygon( diamond, colour with { A = 0.85f } );
			hud.DrawPolyline( new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] }, Colors.Black with { A = 0.7f }, 2f );
			var distance = camera.GlobalPosition.DistanceTo( m.At );
			var who = m.Peer == 0 || m.Peer == Net.Me ? "You" : Net.PlayerName( m.Peer );
			hud.Centered( $"{who}{(enemy ? "  ENEMY" : "")}  {distance:0} m", p + new Vector2( 0f, -18f ), 15, colour.Lightened( 0.3f ) );
		}
	}

	public static void Clear() => marks.Clear();
}

/// <summary>
/// Dead in an online game: after a moment the camera follows a living teammate from over their shoulder, along their
/// aim; a click moves to the next one. Back to normal on respawn. (PlayerRig's death camera hands over to this.)
/// </summary>
public static class Spectate
{
	public const float After = 2.5f;
	public static PlayerController Watching { get; private set; }
	static int index;
	static Vector3 camAt;

	/// <summary> The camera for the local player, dead `deadFor` seconds; null keeps the death camera on the ragdoll. </summary>
	public static Transform3D? Camera( PlayerController me, float deadFor, float dt )
	{
		if ( !Net.Online || deadFor < After ) { Watching = null; return null; }
		var alive = new List<PlayerController>();
		foreach ( var s in PlayerSurvivor.All )
			if ( s.Controller is { IsRemote: true } c && GodotObject.IsInstanceValid( c ) && s.IsAlive ) alive.Add( c );
		if ( alive.Count == 0 ) { Watching = null; return null; }
		var switched = false;
		if ( Input.IsActionJustPressed( "attack1" ) ) { index++; switched = true; }
		if ( Input.IsActionJustPressed( "attack2" ) ) { index--; switched = true; }
		index = ((index % alive.Count) + alive.Count) % alive.Count;
		var first = Watching is null;
		Watching = alive[index];
		var eye = Watching.EyeTransform;
		var forward = -eye.Basis.Z;
		var want = eye.Origin - forward * 3.4f + Vector3.Up * 0.7f + eye.Basis.X * 0.6f;
		var tr = Trace.Sphere( me, 0.25f, eye.Origin, want, Layers.World, new Godot.Collections.Array<Rid> { Watching.GetRid(), me.GetRid() } );
		if ( tr.Hit && !tr.StartedSolid ) want = eye.Origin + (want - eye.Origin).SafeNormal() * MathF.Max( 0.4f, tr.Distance - 0.1f );
		camAt = first || switched ? want : camAt.Lerp( want, 1f - MathF.Exp( -10f * dt ) );
		return new Transform3D( Shapes.LookAt( (eye.Origin + forward * 20f - camAt).SafeNormal() ), camAt );
	}

	public static void Stop() => Watching = null;
}
