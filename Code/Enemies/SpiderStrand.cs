/// <summary>
/// A glowing strand: shot from a sea spider's lure to a point. It shoots out, hangs for a moment and thins away.
/// Used for its harpoons at enemies out of reach and for the line that tops up a diver's jetpack.
/// </summary>
public sealed class SpiderStrand : Component
{
	Vector3 from, to;
	Color color;
	float life = 0.5f;
	float radius = 1.1f;
	TimeSince born;
	GameObject tipBall;

	public static SpiderStrand Create( Vector3 from, Vector3 to, Color color, float life = 0.5f, float radius = 1.1f )
	{
		var go = new GameObject( true, "Strand" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = from;
		var strand = go.AddComponent<SpiderStrand>();
		strand.from = from;
		strand.to = to;
		strand.color = color;
		strand.life = life;
		strand.radius = radius;

		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = ShapeMeshes.Spindle( 0.78f, 0f );
		renderer.Tint = color * 3f;
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		var tip = new GameObject( go, true, "Strand Tip" );
		var tr = tip.AddComponent<ModelRenderer>();
		tr.Model = Shapes.Sphere;
		tr.Tint = color * 4f;
		tr.RenderType = ModelRenderer.ShadowRenderType.Off;
		strand.tipBall = tip;
		return strand;
	}

	protected override void OnStart() => born = 0f;

	protected override void OnUpdate()
	{
		var t = born / life;
		if ( t >= 1f ) { GameObject.Destroy(); return; }

		// Shoots out over the first fifth, holds, then thins away.
		var reach = MathX.Clamp( t / 0.2f, 0f, 1f );
		var end = Vector3.Lerp( from, to, 1f - (1f - reach) * (1f - reach) );
		var thin = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
		var d = end - from;
		var length = d.Length;
		if ( length < 0.01f ) return;

		GameObject.WorldPosition = from;
		GameObject.WorldRotation = Rotation.LookAt( d / length, Vector3.Up );
		GameObject.WorldScale = new Vector3( length, radius * thin, radius * thin );

		if ( tipBall.IsValid() )
		{
			tipBall.WorldPosition = end;
			tipBall.WorldScale = Vector3.One * Shapes.SphereScale( radius * 2.8f * thin );
		}
	}
}
