using Godot;

/// <summary>
/// The skill tree hooks for the guns, the trident and the spear and shield (SkillFx has the numbers). Stats a skill
/// changes outright (magazine, pellets, reach, charge time) are set from the weapon's own base values whenever the
/// skills change (`SyncSkills`).
/// </summary>
public partial class PlayerWeapon
{
	int skillVersion = -1, baseMagazine = -1, basePellets;
	float baseSpearReach, baseChargeTime;
	/// <summary> Overclock: rifle shots left at the faster rate (always shocking), and whether this shot is one. </summary>
	int overclockLeft;
	bool overclockShot;
	int chokeCount;
	bool riposteReady;
	TimeSince sinceBash = 99f;
	float spartanTick;

	public int OverclockLeft => overclockLeft;
	public bool RiposteReady => riposteReady;

	int Skill( SkillId id ) => Skills.Rank( controller, id );

	/// <summary> The shotgun's shell time with Shell Shuffle (10% quicker a point). </summary>
	float ShellScale => MathF.Max( 0.4f, 1f - SkillFx.ShellShufflePer * Skill( SkillId.ShellShuffle ) );

	void SyncSkills()
	{
		if ( controller is null || skillVersion == PlayerSkills.Version ) return;
		skillVersion = PlayerSkills.Version;
		if ( baseMagazine < 0 ) { baseMagazine = MagazineSize; basePellets = Pellets; baseSpearReach = SpearReach; baseChargeTime = ChargeTime; }
		MagazineSize = ModelName == "rifle" ? (int)MathF.Round( baseMagazine * (1f + SkillFx.DeepMagPer * Skill( SkillId.DeepMag )) )
			: Vacuum ? baseMagazine + SkillFx.BellyPer * Skill( SkillId.BigBelly )
			: Harpoon ? baseMagazine + Skill( SkillId.DeepQuiver )
			: Lure ? baseMagazine + Skill( SkillId.DeepPockets ) : baseMagazine;
		if ( Ammo > MagazineSize ) Ammo = MagazineSize;
		Pellets = ModelName == "shotgun" ? basePellets + Skill( SkillId.WideLoad ) : basePellets;
		SpearReach = Spear ? baseSpearReach * (1f + SkillFx.LongSpearPer * Skill( SkillId.LongSpear )) : baseSpearReach;
		ChargeTime = Projectile ? baseChargeTime * (1f - SkillFx.QuickDrawPer * Skill( SkillId.QuickDraw )) : baseChargeTime;
	}

	/// <summary> Overclock: a finished rifle reload winds the gun up for its next shots. </summary>
	void StartOverclock()
	{
		var n = ModelName == "rifle" ? Skill( SkillId.Overclock ) : 0;
		if ( n <= 0 ) return;
		overclockLeft = SkillFx.OverclockShots( n );
		Sfx.Play2D( "beam_charge", 0.35f, 1.8f, Sfx.GunBus );
	}

	/// <summary> A hitscan shot is going out (before its first pellet). </summary>
	void BeginShotSkills()
	{
		overclockShot = ModelName == "rifle" && overclockLeft > 0;
		if ( overclockShot ) { overclockLeft--; SkillFx.Overclocked++; }
	}

	/// <summary> After a hitscan shot: Full Choke's slug on every fourth shotgun blast. </summary>
	void EndShotSkills( Vector3 start, Vector3 forward, Vector3 trailStart )
	{
		overclockShot = false;
		var choke = ModelName == "shotgun" ? Skill( SkillId.FullChoke ) : 0;
		if ( choke <= 0 ) return;
		if ( ++chokeCount % SkillFx.ChokeEvery != 0 ) return;
		SkillFx.Slug( controller, start, forward, trailStart, CurrentDamage * SkillFx.ChokeScale( choke ), Range );
		juice?.Shake( 0.3f );
	}

	/// <summary> Testing aid: a reload finishes now (Overclock winds up). </summary>
	public void TestFinishReload() { IsReloading = true; FinishReload(); }

	/// <summary> Testing aid: the next shotgun blast is the slug one. </summary>
	public void TestPrimeChoke() => chokeCount = SkillFx.ChokeEvery - 1;

	/// <summary> More damage from a hit: Headhunter (rifle, weak spots); Point Blank and Limb Ripper (shotgun). </summary>
	float HitScale( TraceResult tr, Vector3 start )
	{
		if ( Shootables.Find( tr.Node ) is not CreaturePart part || part.Creature is not Creature c ) return 1f;
		var scale = 1f;
		if ( ModelName == "rifle" && c.WeakSpot( part.Index ) ) scale += SkillFx.HeadhunterPer * Skill( SkillId.Headhunter );
		if ( ModelName == "shotgun" )
		{
			if ( tr.HitPosition.DistanceTo( start ) < SkillFx.PointBlankRange ) scale += SkillFx.PointBlankPer * Skill( SkillId.PointBlank );
			if ( !c.WeakSpot( part.Index ) ) scale += SkillFx.LimbPer * Skill( SkillId.LimbRipper );
		}
		return scale;
	}

	/// <summary> Once per enemy a shotgun blast hits: Kickback's shove and Dragon's Breath's fire. </summary>
	void PelletSkills( TraceResult tr, Vector3 direction )
	{
		if ( ModelName != "shotgun" || SkillFx.EnemyOf( tr.Node ) is not { } c ) return;
		var kick = Skill( SkillId.Kickback );
		if ( kick > 0 ) SkillFx.Shove( c, direction.Flat().SafeNormal() * SkillFx.KickbackPer * kick );
		var fire = Skill( SkillId.DragonsBreath );
		if ( fire > 0 ) Affliction.Apply( c, Affliction.Kind.Burn, SkillFx.BurnPer * fire, SkillFx.BurnTime, controller );
	}

	/// <summary> Poseidon's Fork: a full charge throws two more tridents, fanned out either side. </summary>
	void Fork( Vector3 from, Vector3 aim, Vector3 eyes, Basis view )
	{
		var n = Skill( SkillId.PoseidonsFork );
		if ( n <= 0 ) return;
		SkillFx.Forks++;
		foreach ( var side in new[] { -1f, 1f } )
		{
			var dir = aim.Rotated( view.Y.Normalized(), MathX.Rad( side * SkillFx.ForkDegrees ) ).Normalized();
			TridentBolt.Launch( GetTree().CurrentScene, from, from + dir * Range, dir, eyes, ShotDamage * SkillFx.ForkShare( n ), controller, this, true );
		}
	}

	/// <summary> Shield Bash (raising the shield shoves and hits) and Spartan (the raised shield hurts what touches it). </summary>
	void ShieldSkills( bool wasBlocking, float dt )
	{
		var bash = Skill( SkillId.ShieldBash );
		if ( Blocking && !wasBlocking && bash > 0 && sinceBash > SkillFx.BashCooldown )
		{
			sinceBash = 0f;
			SkillFx.Bashes++;
			ShieldPush( SkillFx.BashReach, SkillFx.BashPer * bash, 1.6f );
			Sfx.Play2D( "glass_hit", 0.7f, 0.65f );
			juice?.Shake( 0.3f );
			Controls.Rumble( 0.35f );
		}
		var spartan = Skill( SkillId.Spartan );
		if ( Blocking && spartan > 0 && (spartanTick -= dt) <= 0f )
		{
			spartanTick = 0.5f;
			ShieldPush( 1.9f, SkillFx.SpartanDps( spartan ) * 0.5f, 0.25f );
		}
	}

	/// <summary> Hits and shoves every enemy just in front of the shield. </summary>
	public int ShieldPush( float reach, float damage, float shove )
	{
		var at = controller.GlobalPosition + Vector3.Up * 1f;
		var forward = controller.EyeAngles.Forward.Flat().SafeNormal();
		var hit = 0;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var part = SkillFx.NearestPart( c, at );
			var to = part - at;
			if ( to.Length() > reach || forward.Dot( to.Flat().SafeNormal() ) < 0.2f ) continue;
			hit++;
			PlayerPerks.Hurt( c, damage, part, at, controller );
			SkillFx.Shove( c, forward * shove );
			LightningArc.Spark( GetTree().CurrentScene, part );
		}
		return hit;
	}

	/// <summary> Riposte: the thrust after a blocked hit lands harder (used up by it). </summary>
	float TakeRiposte()
	{
		var n = Skill( SkillId.Riposte );
		if ( !riposteReady || n <= 0 ) return 1f;
		riposteReady = false;
		return 1f + SkillFx.RipostePer * n;
	}

	/// <summary> The raised shield took a hit (PlayerPerks.Absorb): Riposte gets ready, and Mirror Shield may flash it back. </summary>
	public void NoteBlocked( DamageInfo damage )
	{
		riposteReady = true;
		var n = Skill( SkillId.MirrorShield );
		if ( n <= 0 || !(ForceMirror ?? Rng.Float() < SkillFx.MirrorPer * n) ) return;
		if ( damage.Attacker is not Creature { IsEnemy: true, IsDead: false } c ) return;
		SkillFx.Reflects++;
		var from = controller.GlobalPosition + Vector3.Up * 1.1f + controller.EyeAngles.Forward.Flat() * 0.5f;
		var to = SkillFx.NearestPart( c, from );
		SkillFx.Zap( from, to );
		PlayerPerks.Hurt( c, damage.Damage * 2f, to, from, controller );
		Sfx.Play2D( "glass_hit", 0.6f, 1.6f );
	}

	/// <summary> Tests: makes Mirror Shield always (true) or never (false) go off; null leaves it to chance. </summary>
	public static bool? ForceMirror;
}
