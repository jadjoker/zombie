using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// During the dive, marks the nearest dive Bloobers so they're easy to find on the way down: a ring on the ones
/// in view, an arrow at the screen edge for the ones out of view. Removed when the dive ends.
/// </summary>
public partial class DiveMarkers : CanvasLayer
{
	public readonly List<Bloober> Targets = new();
	public int Shown = 6;
	public float Range = 2400f * U.In;
	Drawer drawer;

	public override void _Ready()
	{
		Name = "DiveMarkers";
		Layer = 5;
		drawer = new Drawer { Owner = this, MouseFilter = Control.MouseFilterEnum.Ignore };
		drawer.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		AddChild( drawer );
	}

	public override void _Process( double delta ) => drawer.QueueRedraw();

	partial class Drawer : Control
	{
		public new DiveMarkers Owner;

		public override void _Draw()
		{
			var cam = GetViewport().GetCamera3D();
			if ( cam is null ) return;
			Owner.Targets.RemoveAll( b => !IsInstanceValid( b ) || b.IsDead );
			var eye = cam.GlobalPosition;
			var size = GetViewportRect().Size;
			var center = size * 0.5f;
			var pulse = 0.75f + 0.25f * Mathf.Sin( (float)Clock.Real * 6f );
			var color = new Color( 1f, 0.35f, 0.25f, 0.9f );
			foreach ( var b in Owner.Targets.Where( b => b.GlobalPosition.DistanceTo( eye ) < Owner.Range ).OrderBy( b => b.GlobalPosition.DistanceSquaredTo( eye ) ).Take( Owner.Shown ) )
			{
				var at = b.GlobalPosition;
				var near = 1f - Mathf.Clamp( at.DistanceTo( eye ) / Owner.Range, 0f, 1f );
				var onScreen = !cam.IsPositionBehind( at ) && new Rect2( Vector2.Zero, size ).Grow( -20f ).HasPoint( cam.UnprojectPosition( at ) );
				if ( onScreen )
				{
					var p = cam.UnprojectPosition( at );
					var r = (14f + 18f * near) * pulse;
					DrawArc( p, r, 0f, Mathf.Tau, 24, color, 2.5f, true );
					DrawArc( p, r * 0.35f, 0f, Mathf.Tau, 12, color, 2f, true );
					continue;
				}
				// Off screen: an arrow on the edge pointing toward it.
				var local = cam.GlobalTransform.AffineInverse() * at;
				var dir = new Vector2( local.X, -local.Y );
				if ( dir.LengthSquared() < 1e-6f ) dir = Vector2.Down;
				dir = dir.Normalized();
				var edge = center + dir * (Mathf.Min( size.X, size.Y ) * 0.42f);
				var tip = edge + dir * 16f;
				var side = new Vector2( -dir.Y, dir.X ) * 10f;
				DrawColoredPolygon( new[] { tip, edge - dir * 6f + side, edge - dir * 6f - side }, color with { A = 0.5f + 0.4f * near } );
			}
		}
	}
}
