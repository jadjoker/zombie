using Godot;

/// <summary>
/// The skill tree hooks for the surge and the blink: Undertow drags enemies along a surge, Kraken's Blessing leaves a
/// whirlpool where it ends, and the blink's Toxic Trail, Slipstream and Phase Shift (untouchable, a decoy).
/// </summary>
public partial class Jetpack
{
	bool surgeWasBlink, wasSurging;
	TimeSince sinceToxic;
	public static int Refunds, Dragged;

	int Skill( SkillId id ) => Skills.Rank( controller, id );

	void SurgeSkills( float dt )
	{
		var surging = Surging;
		if ( surging && !surgeWasBlink ) Undertow( dt );
		if ( surging && surgeWasBlink && sinceToxic > 0.05f && Skill( SkillId.ToxicTrail ) is var toxic && toxic > 0 )
		{
			sinceToxic = 0f;
			SkillFx.DropToxic( controller, Feet + Vector3.Up * 0.3f, SkillFx.ToxicDps( toxic ) * (blinkGunk ? 2f : 1f), blinkColour );
		}
		if ( wasSurging && !surging )
		{
			if ( surgeWasBlink )
			{
				if ( Skill( SkillId.Slipstream ) > 0 && PlayerPerks.Of( controller ) is { } perks ) perks.SlipUntil = Clock.Now + SkillFx.SlipTime;
			}
			else if ( Skill( SkillId.KrakensBlessing ) is var kraken && kraken > 0 )
				SurgeWhirlpool.Spawn( controller, controller.GlobalPosition, SkillFx.WhirlTime( kraken ) );
		}
		wasSurging = surging;
	}

	/// <summary> Undertow: enemies near the surging player are pulled in behind them and carried along. </summary>
	void Undertow( float dt )
	{
		var n = Skill( SkillId.Undertow );
		if ( n <= 0 ) return;
		var reach = SkillFx.UndertowReach( n );
		var behind = controller.GlobalPosition - surgeDirection * 1.2f;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || !c.Pullable || rammed.Contains( c ) ) continue;
			var to = behind - c.GlobalPosition;
			if ( (controller.GlobalPosition - c.GlobalPosition).Length() > reach + 1f ) continue;
			Dragged++;
			c.Pull( to * MathF.Min( 1f, 9f * dt ) );
		}
	}

	/// <summary> Phase Shift: a blink makes the player untouchable for a moment and leaves a decoy where it began. </summary>
	void BlinkSkills()
	{
		var n = Skill( SkillId.PhaseShift );
		if ( n <= 0 || controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) is not { } survivor ) return;
		survivor.UntouchableUntil = Clock.Now + SkillFx.PhaseTime( n );
		if ( survivor.Decoy is { } old && IsInstanceValid( old ) ) old.QueueFree();
		survivor.Decoy = Decoy.Spawn( controller, controller.GlobalPosition, SkillFx.DecoyTime( n ) );
	}
}
