/// <summary>
/// The pieces a body can be cut into. Each one is a run of the citizen skeleton,
/// from its root bone down to (not including) the next segment's root.
/// </summary>
public enum Segment
{
	Pelvis,
	Belly,
	Chest,
	Head,
	UpperArmL,
	ForearmL,
	HandL,
	UpperArmR,
	ForearmR,
	HandR,
	ThighL,
	ShinL,
	FootL,
	ThighR,
	ShinR,
	FootR,
}

/// <summary> Static description of one segment. </summary>
public sealed record SegmentDef(
	Segment Segment,
	Segment? Parent,
	string RootBone,
	string[] RagdollBodies,
	float HealthFraction,
	bool Lethal,
	float Radius,
	string CapsuleFrom,
	string CapsuleTo,
	float GibHealth );

/// <summary>
/// Maps a zombie's skeleton (hidden citizen + visible mesh) onto body segments.
/// Plain object so gibs can keep using it after their zombie is gone.
/// Not static: editor hotload keeps old static values, which would hide tuning changes.
/// </summary>
public sealed class BodySegments
{
	public static readonly Segment[] All = Enum.GetValues<Segment>();

	public IReadOnlyDictionary<Segment, SegmentDef> Defs { get; }
	public Model CitizenModel { get; }
	public Model MeshModel { get; }

	/// <summary> Segment of every bone of the visible mesh, by bone index. </summary>
	public Segment[] MeshBoneSegment { get; }

	readonly Dictionary<Segment, List<Segment>> children = new();

	public BodySegments( Model citizen, Model mesh )
	{
		CitizenModel = citizen;
		MeshModel = mesh ?? citizen;

		var defs = new SegmentDef[]
		{
			//  segment             parent               root bone      ragdoll bodies        hp     lethal radius capsule from     capsule to      gib hp
			new( Segment.Pelvis,    null,                "pelvis",      new[] { "pelvis" },      9f,    true,  7f,   "leg_upper_L",   "leg_upper_R",  60f ),
			new( Segment.Belly,     Segment.Pelvis,      "spine_0",     new[] { "spine_0" },     2.0f,  true,  8f,   "spine_0",       "spine_2",      60f ),
			new( Segment.Chest,     Segment.Belly,       "spine_2",     new[] { "spine_2" },     1.8f,  true,  9.5f, "spine_2",       "neck_0",       60f ),
			new( Segment.Head,      Segment.Chest,       "head",        new[] { "head" },        0.35f, true,  5.5f, "head",          "@head_top",    30f ),
			new( Segment.UpperArmL, Segment.Chest,       "arm_upper_L", new[] { "arm_upper_L" }, 0.45f, false, 3.5f, "arm_upper_L",   "arm_lower_L",  25f ),
			new( Segment.ForearmL,  Segment.UpperArmL,   "arm_lower_L", new[] { "arm_lower_L" }, 0.3f,  false, 3f,   "arm_lower_L",   "hand_L",       25f ),
			new( Segment.HandL,     Segment.ForearmL,    "hand_L",      new[] { "hand_L" },      0.2f,  false, 2.5f, "hand_L",        "@hand_tip_L",  20f ),
			new( Segment.UpperArmR, Segment.Chest,       "arm_upper_R", new[] { "arm_upper_R" }, 0.45f, false, 3.5f, "arm_upper_R",   "arm_lower_R",  25f ),
			new( Segment.ForearmR,  Segment.UpperArmR,   "arm_lower_R", new[] { "arm_lower_R" }, 0.3f,  false, 3f,   "arm_lower_R",   "hand_R",       25f ),
			new( Segment.HandR,     Segment.ForearmR,    "hand_R",      new[] { "hand_R" },      0.2f,  false, 2.5f, "hand_R",        "@hand_tip_R",  20f ),
			new( Segment.ThighL,    Segment.Pelvis,      "leg_upper_L", new[] { "leg_upper_L" }, 0.55f, false, 5f,   "leg_upper_L",   "leg_lower_L",  30f ),
			new( Segment.ShinL,     Segment.ThighL,      "leg_lower_L", new[] { "leg_lower_L" }, 0.4f,  false, 4f,   "leg_lower_L",   "ankle_L",      25f ),
			new( Segment.FootL,     Segment.ShinL,       "ankle_L",     new[] { "ankle_L" },     0.25f, false, 3f,   "ankle_L",       "ball_L",       20f ),
			new( Segment.ThighR,    Segment.Pelvis,      "leg_upper_R", new[] { "leg_upper_R" }, 0.55f, false, 5f,   "leg_upper_R",   "leg_lower_R",  30f ),
			new( Segment.ShinR,     Segment.ThighR,      "leg_lower_R", new[] { "leg_lower_R" }, 0.4f,  false, 4f,   "leg_lower_R",   "ankle_R",      25f ),
			new( Segment.FootR,     Segment.ShinR,       "ankle_R",     new[] { "ankle_R" },     0.25f, false, 3f,   "ankle_R",       "ball_R",       20f ),
		};
		Defs = defs.ToDictionary( d => d.Segment );

		foreach ( var d in defs )
		{
			children[d.Segment] = new();
		}
		foreach ( var d in defs )
		{
			if ( d.Parent is { } p ) children[p].Add( d.Segment );
		}

		// Visible mesh bones: shared bones use the citizen hierarchy, mesh-only bones (jaw, pinkies) follow their mesh parent.
		var meshBones = MeshModel.Bones.AllBones.ToList();
		MeshBoneSegment = new Segment[meshBones.Count];
		foreach ( var b in meshBones )
		{
			if ( CitizenModel.Bones.HasBone( b.Name ) ) MeshBoneSegment[b.Index] = SegmentForCitizenBone( b.Name );
			else MeshBoneSegment[b.Index] = b.Parent is null ? Segment.Pelvis : MeshBoneSegment[b.Parent.Index];
		}
	}

	public IReadOnlyList<Segment> Children( Segment s ) => children[s];

	/// <summary> Walks up the citizen skeleton to the first segment root. </summary>
	public Segment SegmentForCitizenBone( string boneName )
	{
		var bone = CitizenModel.Bones.GetBone( boneName );
		for ( var b = bone; b is not null; b = b.Parent )
		{
			foreach ( var d in Defs.Values )
				if ( d.RootBone == b.Name ) return d.Segment;
		}
		return Segment.Pelvis;
	}

	/// <summary> A segment and everything below it, limited to the given set. </summary>
	public HashSet<Segment> Subtree( Segment root, IReadOnlySet<Segment> within )
	{
		var result = new HashSet<Segment>();
		void Walk( Segment s )
		{
			if ( !within.Contains( s ) ) return;
			result.Add( s );
			foreach ( var c in children[s] ) Walk( c );
		}
		Walk( root );
		return result;
	}

	/// <summary> The topmost segment of a connected set (the one whose parent isn't in the set). </summary>
	public Segment RootOf( IReadOnlySet<Segment> set )
	{
		foreach ( var s in set )
		{
			var parent = Defs[s].Parent;
			if ( parent is null || !set.Contains( parent.Value ) ) return s;
		}
		return set.First();
	}

	public bool IsTorso( Segment s ) => s is Segment.Pelvis or Segment.Belly or Segment.Chest;

	/// <summary>
	/// Collision capsule for a segment from the citizen's current pose, in world space.
	/// "@" end points are made up: the top of the head, the fingertips.
	/// </summary>
	public (Vector3 From, Vector3 To) Capsule( Segment s, SceneModel citizen )
	{
		var d = Defs[s];
		var from = citizen.GetBoneWorldTransform( d.CapsuleFrom ).Position;
		Vector3 to;
		switch ( d.CapsuleTo )
		{
			case "@head_top":
				var neck = citizen.GetBoneWorldTransform( "neck_0" ).Position;
				to = from + (from - neck).Normal * 7f;
				break;
			case "@hand_tip_L":
			case "@hand_tip_R":
				var side = d.CapsuleTo.EndsWith( "L" ) ? "L" : "R";
				var wrist = citizen.GetBoneWorldTransform( $"arm_lower_{side}" ).Position;
				to = from + (from - wrist).Normal * 6f;
				break;
			default:
				to = citizen.GetBoneWorldTransform( d.CapsuleTo ).Position;
				break;
		}
		return (from, to);
	}
}
