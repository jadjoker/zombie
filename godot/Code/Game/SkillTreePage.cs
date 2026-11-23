using Godot;

/// <summary>
/// The pause menu's first page: the robot's three skill trees side by side (primary, secondary, core), the tokens to
/// spend, and what the skill under the cursor (or the pad's focus) does now and at its next point. Click or press A
/// on a skill to put a token in it. The arrow on the right goes to the menu (settings, restart, quit).
/// </summary>
public partial class SkillTreePage : Control
{
	public Action Resume, ShowMenu;
	public Button ResumeButton { get; private set; }
	public Button MenuArrow { get; private set; }

	HBoxContainer trees;
	Label tokens, earned, robotName, detailName, detailRank, detailNow, detailNext, detailNeed;
	readonly List<SkillNode> nodes = new();
	readonly List<(Label Label, string Robot, int Tree, int Row)> locks = new();
	readonly List<(Label Label, string Robot, int Tree)> treePoints = new();
	string builtFor;
	SkillNode shown;
	PlayerSkills skills;

	public override void _Ready()
	{
		Name = "SkillTreePage";
		SetAnchorsAndOffsetsPreset( LayoutPreset.FullRect );   // in the tree already: the offsets must follow too
		MouseFilter = MouseFilterEnum.Ignore;

		var margin = new MarginContainer();
		margin.SetAnchorsPreset( LayoutPreset.FullRect );
		foreach ( var side in new[] { "left", "right" } ) margin.AddThemeConstantOverride( $"margin_{side}", 110 );
		margin.AddThemeConstantOverride( "margin_top", 28 );
		margin.AddThemeConstantOverride( "margin_bottom", 24 );
		margin.MouseFilter = MouseFilterEnum.Ignore;
		AddChild( margin );
		var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		column.AddThemeConstantOverride( "separation", 12 );
		margin.AddChild( column );

		// Header: the title and robot on the left, the tokens on the right.
		var header = new HBoxContainer();
		header.AddThemeConstantOverride( "separation", 18 );
		column.AddChild( header );
		var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		titles.AddThemeConstantOverride( "separation", 0 );
		header.AddChild( titles );
		var title = UiStyle.Big( titles, 46, UiStyle.Gold, HorizontalAlignment.Left );
		title.Text = "SKILL TREES";
		robotName = UiStyle.Tag( titles, 18, UiStyle.Teal, HorizontalAlignment.Left );
		var purse = new VBoxContainer();
		purse.AddThemeConstantOverride( "separation", 0 );
		header.AddChild( purse );
		tokens = UiStyle.Big( purse, 40, UiStyle.Gold, HorizontalAlignment.Right );
		earned = UiStyle.Tag( purse, 15, UiStyle.Readout, HorizontalAlignment.Right );

		trees = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
		trees.AddThemeConstantOverride( "separation", 22 );
		column.AddChild( trees );

		// What the skill in focus does.
		var detail = new PanelContainer { CustomMinimumSize = new Vector2( 0, 118 ) };
		detail.AddThemeStyleboxOverride( "panel", UiStyle.Frame( 1.2f, UiStyle.PanelDeep, 14 ) );
		column.AddChild( detail );
		var lines = new VBoxContainer();
		lines.AddThemeConstantOverride( "separation", 2 );
		detail.AddChild( lines );
		var top = new HBoxContainer();
		top.AddThemeConstantOverride( "separation", 16 );
		lines.AddChild( top );
		detailName = UiStyle.Big( top, 28, UiStyle.Gold, HorizontalAlignment.Left );
		detailRank = UiStyle.Tag( top, 16, UiStyle.Teal, HorizontalAlignment.Left );
		detailRank.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		detailNow = UiStyle.Text( lines, 19, UiStyle.Readout, HorizontalAlignment.Left );
		detailNext = UiStyle.Text( lines, 19, UiStyle.Teal, HorizontalAlignment.Left );
		detailNeed = UiStyle.Text( lines, 17, UiStyle.Gold, HorizontalAlignment.Left );
		foreach ( var l in new[] { detailNow, detailNext, detailNeed } ) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;

		var footer = new HBoxContainer();
		footer.AddThemeConstantOverride( "separation", 16 );
		column.AddChild( footer );
		ResumeButton = new Button { Text = "Resume", Name = "Resume", CustomMinimumSize = new Vector2( 220, 46 ) };
		ResumeButton.AddThemeFontSizeOverride( "font_size", 22 );
		ResumeButton.Pressed += () => Resume?.Invoke();
		footer.AddChild( ResumeButton );
		var hint = UiStyle.Tag( footer, 15, UiStyle.Readout with { A = 0.75f }, HorizontalAlignment.Left );
		hint.Name = "Hint";
		hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		hint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		footerHint = hint;

		// The arrow to the rest of the pause menu, on the right edge.
		MenuArrow = new Button { Name = "Menu", Text = "›\nMENU", CustomMinimumSize = new Vector2( 112, 160 ) };
		MenuArrow.AddThemeFontSizeOverride( "font_size", 30 );
		MenuArrow.SetAnchorsPreset( LayoutPreset.CenterRight );
		MenuArrow.OffsetLeft = -128; MenuArrow.OffsetRight = -16; MenuArrow.OffsetTop = -80; MenuArrow.OffsetBottom = 80;
		MenuArrow.Pressed += () => ShowMenu?.Invoke();
		AddChild( MenuArrow );
	}

	/// <summary> Shown: catches up with the robot and its points. </summary>
	public void Open()
	{
		skills = PlayerSkills.Local;
		var robot = RobotClass.Current.Id;
		if ( builtFor != robot ) Build( robot );
		if ( skills is not null ) { skills.Changed -= Refresh; skills.Changed += Refresh; }
		Refresh();
		Controls.Changed -= ButtonsChanged;
		Controls.Changed += ButtonsChanged;
		Show( nodes.FirstOrDefault( n => n.Def.Row == 0 ) );
	}

	public override void _ExitTree()
	{
		if ( skills is not null ) skills.Changed -= Refresh;
		Controls.Changed -= ButtonsChanged;
	}

	void Build( string robot )
	{
		builtFor = robot;
		foreach ( var c in trees.GetChildren() ) c.QueueFree();
		nodes.Clear();
		locks.Clear();
		treePoints.Clear();
		for ( var t = 0; t < 3; t++ )
		{
			var panel = new PanelContainer { CustomMinimumSize = new Vector2( 500, 0 ), SizeFlagsHorizontal = SizeFlags.ExpandFill, Name = $"Tree {t}" };
			panel.AddThemeStyleboxOverride( "panel", UiStyle.Frame( 1.4f, UiStyle.Panel with { A = 0.92f }, 16 ) );
			trees.AddChild( panel );
			var col = new VBoxContainer();
			col.AddThemeConstantOverride( "separation", 6 );
			panel.AddChild( col );
			var name = UiStyle.Big( col, 28, UiStyle.Theme.LightPanel ? UiStyle.Theme.PanelNumber : UiStyle.Gold );
			name.Text = Skills.TreeName( robot, t ).ToUpperInvariant();
			var kit = UiStyle.Tag( col, 14, UiStyle.Teal );
			kit.Text = Skills.TreeKitName( robot, t );
			var points = UiStyle.Tag( col, 13, UiStyle.Readout );
			treePoints.Add( (points, robot, t) );
			for ( var row = 0; row <= Skills.Rows; row++ )
			{
				if ( row > 0 )
				{
					var gate = UiStyle.Tag( col, 13, UiStyle.Readout with { A = 0.7f } );
					gate.CustomMinimumSize = new Vector2( 0, 18 );
					locks.Add( (gate, robot, t, row) );
				}
				var line = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
				line.AddThemeConstantOverride( "separation", 46 );
				col.AddChild( line );
				foreach ( var def in Skills.Tree( robot, t ).Where( d => d.Row == row ) )
				{
					var cell = new VBoxContainer { CustomMinimumSize = new Vector2( 150, 0 ) };
					cell.AddThemeConstantOverride( "separation", 3 );
					line.AddChild( cell );
					var node = new SkillNode { Def = def, Name = def.Id.ToString(), CustomMinimumSize = new Vector2( 92, 92 ), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
					node.Pressed += () => Buy( node );
					node.FocusEntered += () => Show( node );
					node.MouseEntered += () => Show( node );
					cell.AddChild( node );
					var label = UiStyle.Text( cell, 16, UiStyle.Readout );
					label.Text = def.Name;
					node.Caption = label;
					nodes.Add( node );
				}
			}
		}
	}

	void Buy( SkillNode node )
	{
		if ( skills is null ) return;
		if ( skills.Buy( node.Def.Id ) )
		{
			Sfx.Play2D( "buy", 0.8f, 1f + 0.06f * skills.Held( node.Def.Id ) );
			node.Pop();
		}
		else { Sfx.Play2D( "deny", 0.7f ); node.Shake(); }
		Show( node );
	}

	Label footerHint;
	void ButtonsChanged( Scheme scheme ) { Refresh(); if ( shown is not null ) Show( shown ); }

	void Refresh()
	{
		if ( !IsInsideTree() ) return;
		if ( footerHint is not null ) footerHint.Text = $"Clear a wave: +1 token (up to {Skills.MaxTokens} a run)   ·   {Skills.UnlockPoints} points in a row open the next   ·   {(Controls.IsController ? $"{(Controls.IsPlayStation ? "Cross" : "A")} on" : "click")} a skill to buy a point   ·   {(Controls.IsController ? "LB RB" : "the arrow")}: menu";
		var robot = RobotClass.Current.Id;
		if ( builtFor != robot ) Build( robot );
		robotName.Text = $"{RobotClass.Current.Name}  ·  {RobotClass.Current.Weapons}";
		var have = skills?.Tokens ?? 0;
		tokens.Text = $"{have} TOKEN{(have == 1 ? "" : "S")}";
		UiStyle.StyleBig( tokens, 40, have > 0 ? UiStyle.Gold : UiStyle.Readout );
		earned.Text = $"{skills?.Earned ?? 0} / {Skills.MaxTokens} earned this run";
		foreach ( var n in nodes ) n.Skills = skills;
		foreach ( var (label, r, t, row) in locks )
		{
			var got = skills?.RowPoints( r, t, row - 1 ) ?? 0;
			var open = got >= Skills.UnlockPoints;
			label.Text = open ? (row == Skills.Rows ? "▼  CAPSTONE  ▼" : "▼") : $"{got} / {Skills.UnlockPoints} points to open";
			label.Modulate = open ? Colors.White : new Color( 1f, 1f, 1f, 0.8f );
		}
		foreach ( var (label, r, t) in treePoints ) label.Text = $"{skills?.TreePoints( r, t ) ?? 0} points";
		if ( shown is not null ) Show( shown );
	}

	void Show( SkillNode node )
	{
		if ( node is null ) return;
		shown = node;
		var d = node.Def;
		var rank = skills?.Held( d.Id ) ?? 0;
		detailName.Text = d.Name.ToUpperInvariant();
		UiStyle.StyleBig( detailName, 28, d.Colour.Lightened( 0.2f ) );
		detailRank.Text = $"{rank} / {d.Max}   ·   {(d.Capstone ? "capstone" : $"row {d.Row + 1}")}   ·   {Skills.TreeName( d.Robot, d.Tree )}";
		detailNow.Text = Controls.Prompts( rank > 0 ? $"Now: {d.Says( rank )}" : $"First point: {d.Says( 1 )}" );
		detailNext.Text = Controls.Prompts( rank > 0 && rank < d.Max ? $"Next point: {d.Says( rank + 1 )}" : rank >= d.Max ? "Full" : "" );
		detailNeed.Text = skills is null ? ""
			: !skills.Unlocked( d ) ? $"Locked: put {Skills.UnlockPoints} points in {(d.Row == Skills.Rows ? "the last row" : $"row {d.Row}")} of this tree"
			: rank >= d.Max ? ""
			: skills.Tokens <= 0 ? "No tokens: clear waves to earn them"
			: Controls.IsController ? $"{(Controls.IsPlayStation ? "Cross" : "A")} to put a token in it" : "Click to put a token in it";
	}

	public override void _Process( double delta )
	{
		if ( !IsVisibleInTree() ) return;
		foreach ( var n in nodes ) n.QueueRedraw();
	}
}

/// <summary>
/// One skill on the tree: its icon in a badge with a pip for each point it can take. Dim while locked, a gold rim when
/// full, a pulsing teal rim when it can take a point now.
/// </summary>
public partial class SkillNode : Button
{
	public SkillDef Def;
	public PlayerSkills Skills;
	public Label Caption;
	float pop, shake;

	public override void _Ready()
	{
		Text = "";
		FocusMode = FocusModeEnum.All;
		foreach ( var style in new[] { "normal", "hover", "pressed", "focus", "disabled", "hover_pressed" } ) AddThemeStyleboxOverride( style, new StyleBoxEmpty() );
		PivotOffset = CustomMinimumSize * 0.5f;
	}

	public void Pop() => pop = 1f;
	public void Shake() => shake = 1f;

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		pop = MathF.Max( 0f, pop - dt * 4f );
		shake = MathF.Max( 0f, shake - dt * 4f );
		Scale = Vector2.One * (1f + 0.18f * pop * pop + (HasFocus() || IsHovered() ? 0.06f : 0f));
		RotationDegrees = shake > 0f ? MathF.Sin( shake * 40f ) * 6f * shake : 0f;
	}

	public override void _Draw()
	{
		if ( Def is null ) return;
		var rank = Skills?.Held( Def.Id ) ?? 0;
		var open = Skills?.Unlocked( Def ) ?? Def.Row == 0;
		var full = rank >= Def.Max;
		var buyable = Skills?.CanBuy( Def ) ?? false;
		var size = Size;
		var rect = new Rect2( Vector2.Zero, size );
		var pulse = 0.5f + 0.5f * MathF.Sin( (float)Time.GetTicksMsec() / 1000f * 5f );
		var rim = full ? UiStyle.Gold : buyable ? UiStyle.Teal.Lerp( Colors.White, pulse * 0.4f ) : open ? UiStyle.Readout with { A = 0.6f } : new Color( 0.4f, 0.4f, 0.45f, 0.6f );
		if ( HasFocus() || IsHovered() ) rim = rim.Lightened( 0.3f );
		var box = new StyleBoxFlat
		{
			BgColor = open ? new Color( 0.03f, 0.06f, 0.09f, 0.95f ) : new Color( 0.05f, 0.05f, 0.06f, 0.85f ),
			BorderColor = rim, CornerDetail = 6,
		};
		box.SetCornerRadiusAll( Def.Capstone ? 46 : 18 );
		box.SetBorderWidthAll( full || buyable || HasFocus() ? 4 : 2 );
		if ( buyable ) { box.ShadowColor = UiStyle.Teal with { A = 0.35f + 0.25f * pulse }; box.ShadowSize = 8; }
		else if ( full ) { box.ShadowColor = UiStyle.Gold with { A = 0.4f }; box.ShadowSize = 6; }
		DrawStyleBox( box, rect );
		if ( PerkPickup.Icon( Def.Icon ) is { } icon )
		{
			var inset = Def.Capstone ? 14f : 10f;
			var tint = open ? (rank > 0 ? Colors.White : new Color( 0.82f, 0.82f, 0.82f )) : new Color( 0.35f, 0.35f, 0.38f );
			DrawTextureRect( icon, new Rect2( new Vector2( inset, inset - 4f ), size - new Vector2( inset * 2f, inset * 2f ) ), false, tint );
		}
		// The pips: one for each point it can take, filled for the ones it has.
		var pip = 7f;
		var gap = 4f;
		var width = Def.Max * pip + (Def.Max - 1) * gap;
		var x = (size.X - width) * 0.5f;
		for ( var i = 0; i < Def.Max; i++ )
		{
			var at = new Vector2( x + i * (pip + gap) + pip * 0.5f, size.Y - 9f );
			DrawCircle( at, pip * 0.5f + 1.5f, new Color( 0f, 0f, 0f, 0.8f ) );
			DrawCircle( at, pip * 0.5f, i < rank ? (full ? UiStyle.Gold : UiStyle.Teal) : new Color( 0.25f, 0.28f, 0.32f ) );
		}
		if ( !open )
		{
			// A little padlock in the corner.
			var lockAt = new Vector2( size.X - 20f, 20f );
			DrawArc( lockAt + new Vector2( 0f, -2f ), 5f, MathF.PI, MathF.Tau, 10, new Color( 0.85f, 0.85f, 0.9f ), 2.5f );
			DrawRect( new Rect2( lockAt + new Vector2( -7f, -2f ), new Vector2( 14f, 11f ) ), new Color( 0.85f, 0.85f, 0.9f ) );
		}
		if ( Caption is not null ) Caption.Modulate = open ? Colors.White : new Color( 1f, 1f, 1f, 0.5f );
	}
}
