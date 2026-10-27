using System.Collections.Generic;
using Godot;
using RascalRadio.Core.Systems;

// Operator-side Line Override keypad. Built in code and dropped into Studio Desk slot 1.
// The Operator only sees the incoming LINE number. The rule and the studio level are in the
// Technician's manual, so the Operator has to ask the Technician for the code.
public partial class KeypadModule : VBoxContainer
{
	public int Line { get; set; }
	public bool StaticFault { get; set; }

	private readonly KeypadLogic _logic = new();
	private readonly List<Button> _keys = new();
	private Label _line, _entry, _status;
	private bool _solved;

	public override void _Ready()
	{
		AddThemeConstantOverride("separation", 6);
		AddChild(new Label { Text = "🔢 LINE OVERRIDE", HorizontalAlignment = HorizontalAlignment.Center });
		_line = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		AddChild(_line);
		_entry = new Label { Text = "Entry: _", HorizontalAlignment = HorizontalAlignment.Center };
		AddChild(_entry);

		var grid = new GridContainer { Columns = 3 };
		AddChild(grid);
		foreach (string key in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLEAR", "0", "ROUTE" })
		{
			string k = key;
			var b = new Button { Text = k, CustomMinimumSize = new Vector2(64, 36) };
			b.Pressed += () => OnKey(k);
			grid.AddChild(b);
			_keys.Add(b);
		}

		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		AddChild(_status);

		Refresh(Line, StaticFault);

		if (GameSession.Instance != null) GameSession.Instance.Feedback += OnFeedback;
	}

	public override void _ExitTree()
	{
		if (GameSession.Instance != null) GameSession.Instance.Feedback -= OnFeedback;
	}

	/// <summary>A new problem arrived (or the static fault changed).</summary>
	public void Refresh(int line, bool staticFault)
	{
		Line = line; StaticFault = staticFault;
		if (_line == null) return;   // _Ready has not run yet; it will call Refresh itself

		_line.Text = line > 0 ? $"INCOMING LINE: {line}" : "No incoming line";
		_status.Text = staticFault
			? "⚡ STATIC FAULT. Ask the Technician how to clear it."
			: "You don't have the rules. Read the line number to the Technician and ask for the override code.";
		_logic.Clear();
		_entry.Text = "Entry: _";
	}

	private void OnKey(string key)
	{
		if (_solved) return;

		if (key == "CLEAR") _logic.Clear();
		else if (key == "ROUTE")
		{
			if (_logic.HasEntry) GameSession.Instance.SubmitKeypad(_logic.Entry);
			else _status.Text = "↻ Type a code first.";
			_logic.Clear();
		}
		else _logic.Press(int.Parse(key));

		_entry.Text = "Entry: " + (_logic.Entry == "" ? "_" : _logic.Entry);
	}

	private void OnFeedback(string text, bool success)
	{
		if (_solved || !text.StartsWith(GameSession.KeypadTag)) return;
		_status.Text = text.Substring(GameSession.KeypadTag.Length);
	}

	public void SetSolved()
	{
		_solved = true;
		foreach (var b in _keys) b.Disabled = true;
		_status.Text = "✔ SOLVED";
	}
}
