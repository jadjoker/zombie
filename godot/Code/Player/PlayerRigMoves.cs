using Godot;

/// <summary>
/// Third person poses for the robots' mouse 4 moves that had none: Coral-Bot's burrow (face down in the sand, digging,
/// then a spinning eruption with the arms up), Hook-Bot's grapple (the free hand reaching along the line), Plunger-Bot's
/// sling and Goo-Bot's splat jump (superman along the flight, then a starfish belly flop) and Pinball-Bot's roll (tucked into a ball that rolls along the ground). The frame moves in
/// `MoveFrame`; the spine, head, arms and legs blend toward each move's pose in s&box character space.
/// </summary>
public partial class PlayerRig
{
	float burrowBlend, rollBlend, slingBlend, zipBlend, rollAngle, rollHeading;
	float eruptKick, flopKick;
	Basis flopYaw = Basis.Identity;
	Vector3 lastFeet;

	/// <summary> For tests: where one of the rig's bones is in the world, and whether a move has put the gun away. </summary>
	public Vector3 BoneAt( string name ) => ready && bones.TryGetValue( name, out var b ) ? skeleton.GlobalTransform * skeleton.GetBoneGlobalPose( b ).Origin : controller.GlobalPosition;
	public bool GunPutAway => MoveHidesGun;

	/// <summary> 0 to 1 over a move's beat: up fast, then easing back down by `length` seconds after `at`. </summary>
	static float Kick( double at, float length, float rise = 0.06f )
	{
		var t = (float)(Clock.Now - at);
		if ( t < 0f || t > length ) return 0f;
		var k = 1f - t / length;
		return MathF.Min( 1f, t / rise ) * (1f - (1f - k) * (1f - k));
	}

	static float Ease( float t ) { t = MathX.Clamp( t, 0f, 1f ); return t * t * (3f - 2f * t); }

	/// <summary> Hides the gun while a move needs both hands (or there is nothing to hold it in). </summary>
	bool MoveHidesGun => burrowBlend > 0.3f || rollBlend > 0.3f || eruptKick > 0.3f || flopKick > 0.3f;

	void MoveBlends( float dt )
	{
		if ( jet is null ) return;
		var alive = survivor?.IsAlive ?? true;
		burrowBlend = MathX.Approach( burrowBlend, jet.Burrowed && alive ? 1f : 0f, dt * (jet.Burrowed ? 6f : 5f) );
		rollBlend = MathX.Approach( rollBlend, jet.Rolling && alive ? 1f : 0f, dt * (jet.Rolling ? 8f : 5f) );
		slingBlend = MathX.Approach( slingBlend, (jet.Slinging || jet.Splatting) && !controller.IsOnGround && alive ? 1f : 0f, dt * 7f );
		zipBlend = MathX.Approach( zipBlend, jet.Zipping ? 1f : 0f, dt * 12f );
		eruptKick = Kick( jet.EruptedAt, 0.8f );
		flopKick = Kick( jet.FloppedAt, 0.75f, 0.03f );
	}

	/// <summary> The character frame for the moves: sunk in the sand, rolling, flying on the bungee, flat on its belly, spinning out of the ground. </summary>
	void MoveFrame( float dt, Basis yawRot, Vector3 feet, float hips, ref Basis rot, ref Vector3 pos )
	{
		MoveBlends( dt );
		// The way it travels, from where the feet went (a teleport or a shove counts as much as walking).
		var heading = ((feet - lastFeet) / MathF.Max( dt, 0.001f )).Flat();
		if ( heading.Length() > 30f ) heading = Vector3.Zero;
		lastFeet = feet;
		if ( burrowBlend > 0.001f )
		{
			// Face down along the way it digs, sunk until only the back shows, wriggling.
			var e = Ease( burrowBlend );
			var wriggle = MathF.Sin( (float)Clock.Now * 9f ) * 7f;
			var dig = yawRot * new Angles( 72f, 0f, wriggle ).ToBasis();
			rot = new Basis( rot.GetRotationQuaternion().SafeSlerp( dig.GetRotationQuaternion(), e ) );
			var pelvis = feet + Vector3.Up * MathX.Lerp( hips, -0.12f, e );
			pos = pelvis - rot * (Vector3.Up * hips);
		}
		if ( eruptKick > 0.001f )
		{
			// Out of the sand in a spin, leaning back a touch.
			var t = (float)(Clock.Now - jet.EruptedAt);
			var spin = 360f * Ease( t / 0.55f );
			var q = (yawRot * new Angles( -10f * eruptKick, spin, 0f ).ToBasis()).GetRotationQuaternion();
			rot = new Basis( rot.GetRotationQuaternion().SafeSlerp( q, eruptKick ) );
			pos = feet + Vector3.Up * hips - rot * (Vector3.Up * hips);
		}
		if ( rollBlend > 0.001f )
		{
			// A ball about half a metre across the middle, rolling along the way it travels.
			const float radius = 0.5f, centre = 0.95f;
			if ( heading.Length() > 0.5f ) rollHeading = MathF.Atan2( -heading.X, -heading.Z ) * 180f / MathF.PI;
			else if ( rollBlend < 0.05f ) rollHeading = bodyYaw;
			rollAngle = (rollAngle + MathX.Deg( heading.Length() * dt / radius )) % 360f;
			var ball = new Angles( 0f, rollHeading, 0f ).ToBasis() * new Angles( 40f + rollAngle, 0f, 0f ).ToBasis();
			var e = Ease( rollBlend );
			rot = new Basis( rot.GetRotationQuaternion().SafeSlerp( ball.GetRotationQuaternion(), e ) );
			var middle = feet + Vector3.Up * MathX.Lerp( centre, radius, e );
			pos = middle - rot * (Vector3.Up * centre);
		}
		else rollAngle = 0f;
		if ( slingBlend > 0.001f && controller.Velocity.Length() > 0.5f )
		{
			// Superman along the flight: head first, belly down.
			var dir = controller.Velocity.Normalized();
			var belly = Vector3.Down - dir * Vector3.Down.Dot( dir );
			if ( belly.Length() < 0.2f ) belly = -yawRot.Z;
			var superman = Basis.LookingAt( belly.Normalized(), dir );
			rot = new Basis( rot.GetRotationQuaternion().SafeSlerp( superman.GetRotationQuaternion(), Ease( slingBlend ) ) );
			pos = feet + Vector3.Up * hips - rot * (Vector3.Up * hips);
		}
		if ( flopKick > 0.001f )
		{
			// The belly flop: flat on the sand for a beat, a squash of a bounce, then back up.
			if ( flopKick >= 0.99f || flopYaw == Basis.Identity ) flopYaw = yawRot;
			var t = (float)(Clock.Now - jet.FloppedAt);
			var flat = t < 0.35f ? 1f : 1f - Ease( (t - 0.35f) / 0.4f );
			var bounce = MathF.Sin( MathX.Clamp( t / 0.25f, 0f, 1f ) * MathF.PI ) * 0.12f;
			var down = Basis.LookingAt( Vector3.Down, -flopYaw.Z );
			rot = new Basis( rot.GetRotationQuaternion().SafeSlerp( down.GetRotationQuaternion(), Ease( flat ) ) );
			var pelvis = feet + Vector3.Up * MathX.Lerp( hips, 0.16f + bounce, Ease( flat ) );
			pos = pelvis - rot * (Vector3.Up * hips);
		}
		else flopYaw = Basis.Identity;
	}

	/// <summary> The spine for the moves: hunched for the roll, back for the eruption. </summary>
	Basis MoveSpine( Basis spineRot )
	{
		var pitch = 45f * Ease( rollBlend ) - 12f * eruptKick;
		return MathF.Abs( pitch ) < 0.01f ? spineRot : SAng( pitch, 0f, 0f ) * spineRot;
	}

	/// <summary> The head tucks down into the roll. </summary>
	Basis MoveHead( Basis head ) => rollBlend > 0.001f ? SAng( 40f * Ease( rollBlend ), 0f, 0f ) * head : head;

	/// <summary> An arm's wrist target and elbow pole for the moves (character space, inches). </summary>
	void MoveArm( string s, float side, Vector3 shoulder, float reach, ref Vector3 wristRef, ref Vector3 poleRef )
	{
		var now = (float)Clock.Now;
		var wrist = wristRef; var pole = poleRef;
		void To( Vector3 dir, float amount, Vector3 bend, float w )
		{
			if ( w <= 0.001f ) return;
			wrist = wrist.Lerp( shoulder + dir.Normalized() * reach * amount, w );
			pole = pole.Normalized().Lerp( bend.Normalized(), w );
		}
		// Burrowed: a breaststroke through the sand over the head (the body is face down, so up is ahead).
		if ( burrowBlend > 0.001f )
		{
			var p = now * 7f + (s == "L" ? 0f : 0.4f);
			To( new Vector3( 0.25f + 0.35f * MathF.Cos( p ), side * (0.25f + 0.5f * MathF.Max( 0f, MathF.Sin( p ) )), 0.8f ), 0.85f, new Vector3( 0.4f, side, 0f ), Ease( burrowBlend ) );
		}
		// The eruption: both arms thrown up in a V.
		To( new Vector3( 0.1f, side * 0.45f, 1f ), 0.95f, new Vector3( -0.5f, side, 0f ), eruptKick );
		// The grapple: the free hand reaching along the line.
		if ( s == "L" && zipBlend > 0.001f && jet is not null ) To( CharDir( jet.SurgeDirection ), 0.95f, new Vector3( 0f, side, -1f ), zipBlend );
		// The sling: one fist punched out ahead like a flying hero (the other keeps the gun).
		if ( s == "L" ) To( new Vector3( 0.3f, side * 0.2f, 1f ), 0.95f, new Vector3( -0.3f, side, 0f ), Ease( slingBlend ) );
		// The belly flop: spread like a starfish.
		To( new Vector3( 0.1f, side, 0.7f ), 0.95f, new Vector3( 0f, 0f, 1f ), flopKick );
		// The roll: hugging the knees.
		To( new Vector3( 0.7f, side * 0.15f, -0.75f ), 0.75f, new Vector3( 0f, side, 0.2f ), Ease( rollBlend ) );
		wristRef = wrist; poleRef = pole;
	}

	/// <summary> A leg's ankle target and knee pole for the moves (character space, inches). </summary>
	void MoveLeg( string s, float side, Vector3 hip, float length, ref Vector3 ankleRef, ref Vector3 poleRef )
	{
		var now = (float)Clock.Now;
		var ankle = ankleRef; var pole = poleRef;
		void To( Vector3 target, Vector3 bend, float w )
		{
			if ( w <= 0.001f ) return;
			ankle = ankle.Lerp( target, w );
			pole = pole.Lerp( bend, w );
		}
		// Burrowed: kicking behind like a swimmer.
		if ( burrowBlend > 0.001f )
			To( hip + new Vector3( -2f + 4f * MathF.Sin( now * 10f + side * 1.5f ), side * 2f, -length * 0.93f ), new Vector3( 30f, 0f, 0f ), Ease( burrowBlend ) );
		// The eruption: legs straight and together.
		To( hip + new Vector3( 0f, -side * 1f, -length * 0.97f ), new Vector3( 30f, 0f, 0f ), eruptKick );
		// The grapple: trailing straight behind along the line.
		To( hip + new Vector3( -3f, side * 1.2f, -length * 0.97f ), new Vector3( 30f, 0f, 0f ), zipBlend );
		// The sling: straight back, a little flutter.
		To( hip + new Vector3( -2f + MathF.Sin( now * 14f + side ) * 1.5f, side * 1.5f, -length * 0.96f ), new Vector3( 30f, 0f, 0f ), Ease( slingBlend ) );
		// The belly flop: spread wide.
		To( hip + new Vector3( 0f, side * 0.55f, -1f ).Normalized() * length * 0.97f, new Vector3( 0f, 0f, 30f ), flopKick );
		// The roll: knees tucked up to the chest.
		To( hip + new Vector3( 13f, side * 3f, -7f ), new Vector3( 30f, 0f, 20f ), Ease( rollBlend ) );
		ankleRef = ankle; poleRef = pole;
	}

	readonly Dictionary<(Node3D, string), Node3D> gunParts = new();

	/// <summary> The third person Pinball-Bot guns move like the ones in view: the flippers flick up, the spring squashes. </summary>
	void PinballParts( PlayerWeapon weapon, Node3D root )
	{
		Node3D Find( string name )
		{
			if ( !gunParts.TryGetValue( (root, name), out var n ) || (n is not null && !IsInstanceValid( n )) ) gunParts[(root, name)] = n = root.FindChild( name, true, false ) as Node3D;
			return n;
		}
		if ( weapon.Flippers )
		{
			if ( Find( "Left Flipper" ) is { } l ) l.Rotation = new Vector3( 0f, 0f, MathX.Rad( 55f * weapon.FlipSwing( true ) ) );
			if ( Find( "Right Flipper" ) is { } r ) r.Rotation = new Vector3( 0f, 0f, -MathX.Rad( 55f * weapon.FlipSwing( false ) ) );
		}
		if ( weapon.Pinball && Find( "Spring" ) is { } spring ) spring.Scale = new Vector3( 1f, 1f, 1f - 0.6f * weapon.SpringPull );
	}
}
