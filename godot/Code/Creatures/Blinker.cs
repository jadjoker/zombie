using Godot;

/// <summary>
/// Blinks a creature's eyes now and then (every few seconds, sometimes twice quickly): each eye's meshes squash
/// shut toward the eye's middle along its parent's up and open again, so it works on any eye built from blobs or
/// loaded from a model. For creatures with no lids of their own (the crab, spider, squid and MC-51 have theirs).
/// </summary>
public partial class Blinker : Node
{
	sealed class Piece
	{
		public Node3D Node;
		public Transform3D Rest;
		public Vector3 Pivot;
	}

	readonly List<Piece> pieces = new();
	/// <summary> Seconds between blinks. </summary>
	public float MinGap = 2f, MaxGap = 5.5f;
	/// <summary> How long one blink takes, shut and open. </summary>
	public float Length = 0.17f;
	/// <summary> How far the eyes close: 1 is shut to nothing. </summary>
	public float Depth = 0.9f;
	float next = 1f;
	float t = 1f;
	bool open = true;

	/// <summary> A blinker on a creature (or any node that holds eyes). Add its eyes with Eye. </summary>
	public static Blinker Add( Node owner )
	{
		var blinker = new Blinker { Name = "Blinker" };
		owner.AddChild( blinker );
		blinker.next = Rng.Float( 0.4f, 3f );
		return blinker;
	}

	/// <summary> One eye: its pieces squash together toward the first piece's middle. Null pieces are skipped. </summary>
	public Blinker Eye( params Node3D[] group )
	{
		var first = group.FirstOrDefault( n => n is not null );
		if ( first is null ) return this;
		var pivot = Centre( first );
		foreach ( var node in group )
		{
			if ( node is null ) continue;
			pieces.Add( new Piece { Node = node, Rest = node.Transform, Pivot = node.GetParent() == first.GetParent() ? pivot : Centre( node ) } );
		}
		return this;
	}

	/// <summary> The middle of a piece in its parent's space. </summary>
	static Vector3 Centre( Node3D node ) => node is VisualInstance3D visual ? node.Transform * visual.GetAabb().GetCenter() : node.Position;

	/// <summary> Blink now (and then carry on as usual). </summary>
	public void Blink() => t = 0f;

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		next -= dt;
		if ( next <= 0f )
		{
			t = 0f;
			next = Rng.Float() < 0.2f ? 0.3f : Rng.Float( MinGap, MaxGap );
		}
		if ( t >= 1f && open ) return;
		t = MathF.Min( 1f, t + dt / Length );
		var closing = 1f - MathF.Abs( t * 2f - 1f );
		open = t >= 1f;
		var squash = Basis.FromScale( new Vector3( 1f, MathF.Max( 0.02f, 1f - Depth * closing ), 1f ) );
		foreach ( var piece in pieces )
		{
			if ( !IsInstanceValid( piece.Node ) ) continue;
			var p = piece.Pivot;
			piece.Node.Transform = new Transform3D( Basis.Identity, p ) * new Transform3D( squash, Vector3.Zero ) * new Transform3D( Basis.Identity, -p ) * piece.Rest;
		}
	}
}
