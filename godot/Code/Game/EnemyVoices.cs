using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Enemy calls (user's ask: enemies that feel like animals). Each kind with a <see cref="Creature.Voice"/> cries out
/// when it first spots a player (within <see cref="SpotRange"/>), and calls now and then while it hunts, so a hiss from
/// behind gives one away before it is seen. Spaced out across all enemies so a wave doesn't turn into a choir. Every
/// machine plays its own (puppets too): only sound.
/// </summary>
public static class EnemyVoices
{
	public const float SpotRange = 20f, CallRange = 26f;
	const float SpotGap = 0.3f, CallGap = 1.1f;
	static readonly Dictionary<Creature, double> nextCall = new();
	static readonly HashSet<Creature> spotted = new();
	static double nextSpot, nextAnyCall, nextScan;
	public static int Spots, Calls;

	public static void Step()
	{
		var now = Clock.Now;
		if ( now < nextScan ) return;
		nextScan = now + 0.2;
		spotted.RemoveWhere( c => !GodotObject.IsInstanceValid( c ) || c.IsDead );
		if ( nextCall.Count > 64 ) foreach ( var c in new List<Creature>( nextCall.Keys ) ) if ( !GodotObject.IsInstanceValid( c ) || c.IsDead ) nextCall.Remove( c );
		foreach ( var c in Creature.Everyone )
		{
			if ( !GodotObject.IsInstanceValid( c ) || c.IsDead || !c.IsEnemy || c.Voice is not { } voice ) continue;
			var p = PlayerSurvivor.FindNearestAlive( c.GlobalPosition );
			if ( p is null ) continue;
			var d = p.RealPosition.DistanceTo( c.GlobalPosition );
			if ( !spotted.Contains( c ) )
			{
				if ( d > SpotRange || now < nextSpot ) continue;
				spotted.Add( c );
				nextSpot = now + SpotGap;
				nextCall[c] = now + Rng.Float( 5f, 9f );
				Sfx.Play( voice, c.GlobalPosition, 0.9f * c.VoiceVolume, Rng.Float( 0.92f, 1.08f ) * c.VoicePitch );
				Spots++;
				continue;
			}
			if ( d > CallRange || now < nextAnyCall || !nextCall.TryGetValue( c, out var at ) || now < at ) continue;
			nextAnyCall = now + CallGap;
			nextCall[c] = now + Rng.Float( 6f, 13f );
			Sfx.Play( voice, c.GlobalPosition, 0.55f * c.VoiceVolume, Rng.Float( 0.85f, 1.15f ) * c.VoicePitch );
			Calls++;
		}
	}
}
