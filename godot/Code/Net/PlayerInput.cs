using Godot;

/// <summary>
/// Where a player's buttons come from. The local player reads the keyboard, mouse and pad (Godot's Input); a remote
/// player in an online game (<see cref="Net"/>) reads the presses its owner sent. Player and weapon code asks
/// `controller.In` instead of Godot's Input, so the same code drives both.
/// </summary>
public abstract class PlayerInput
{
	public abstract bool Pressed( string action );
	public abstract bool JustPressed( string action );
	public abstract bool JustReleased( string action );
	/// <summary> Like Input.GetVector (the movement stick or keys). </summary>
	public abstract Vector2 GetVector( string negX, string posX, string negY, string posY );
}

public sealed class LocalInput : PlayerInput
{
	public static readonly LocalInput Instance = new();
	/// <summary> Online the game doesn't pause behind a menu, so the robot stands still while one is open. </summary>
	static bool Held => Net.Online && (PauseMenu.IsOpen || DevMenu.IsOpen || ClassPicker.IsOpen);
	public override bool Pressed( string action ) => !Held && Input.IsActionPressed( action );
	public override bool JustPressed( string action ) => !Held && Input.IsActionJustPressed( action );
	public override bool JustReleased( string action ) => Input.IsActionJustReleased( action );
	public override Vector2 GetVector( string negX, string posX, string negY, string posY ) => Held ? Vector2.Zero : Input.GetVector( negX, posX, negY, posY );
}

/// <summary>
/// A remote player's buttons, rebuilt from the presses and releases its owner sent (reliable, in order). Each edge
/// shows for one process frame and one physics frame, like Godot's own just-pressed, and a press and release that
/// arrive together still give one frame of each.
/// </summary>
public sealed class RemoteInput : PlayerInput
{
	/// <summary> The buttons sent over the wire, by index. </summary>
	public static readonly string[] Actions = { "attack1", "attack2", "reload", "jump", "run", "duck", "melee", "surge", "use", "blast", "command" };

	readonly bool[] down = new bool[Actions.Length];
	readonly ulong[] pressFrame = new ulong[Actions.Length], releaseFrame = new ulong[Actions.Length];
	readonly ulong[] pressPhysics = new ulong[Actions.Length], releasePhysics = new ulong[Actions.Length];
	readonly Queue<(int Index, bool Down)> pending = new();
	public Vector2 Move;

	static int IndexOf( string action )
	{
		for ( var i = 0; i < Actions.Length; i++ ) if ( Actions[i] == action ) return i;
		return -1;
	}

	public void Queue( int index, bool isDown )
	{
		if ( index >= 0 && index < Actions.Length ) pending.Enqueue( (index, isDown) );
	}

	/// <summary> Once a frame, before the player's code runs: applies the edges that came in, one per button a frame. </summary>
	public void Apply()
	{
		var frame = Engine.GetProcessFrames();
		var physics = Engine.GetPhysicsFrames() + 1;   // the next physics step sees it too
		var touched = 0;
		var count = pending.Count;
		for ( var n = 0; n < count; n++ )
		{
			var (i, isDown) = pending.Dequeue();
			if ( (touched & (1 << i)) != 0 ) { pending.Enqueue( (i, isDown) ); continue; }   // one edge per button a frame
			if ( down[i] == isDown ) continue;
			touched |= 1 << i;
			down[i] = isDown;
			if ( isDown ) { pressFrame[i] = frame; pressPhysics[i] = physics; }
			else { releaseFrame[i] = frame; releasePhysics[i] = physics; }
		}
	}

	/// <summary> Everything let go (the owner opened a menu or left). </summary>
	public void ReleaseAll()
	{
		pending.Clear();
		for ( var i = 0; i < down.Length; i++ ) down[i] = false;
		Move = Vector2.Zero;
	}

	public override bool Pressed( string action ) => IndexOf( action ) is var i && i >= 0 && down[i];

	public override bool JustPressed( string action )
	{
		var i = IndexOf( action );
		if ( i < 0 ) return false;
		return Engine.IsInPhysicsFrame() ? pressPhysics[i] == Engine.GetPhysicsFrames() : pressFrame[i] == Engine.GetProcessFrames();
	}

	public override bool JustReleased( string action )
	{
		var i = IndexOf( action );
		if ( i < 0 ) return false;
		return Engine.IsInPhysicsFrame() ? releasePhysics[i] == Engine.GetPhysicsFrames() : releaseFrame[i] == Engine.GetProcessFrames();
	}

	public override Vector2 GetVector( string negX, string posX, string negY, string posY ) => negX == "left" ? Move : Vector2.Zero;
}
