using System;
using System.Collections.Generic;
using Godot;

public partial class SoundboardUI : PanelContainer
{
	public event Action<string> PadPressed;
	public event Action ResetSequencePressed;

	private SoundboardModule _module;
	private bool _built;
	private SoundboardSound _sound;

	private Label _statusLabel;
	private Label _instructionLabel;
	private HBoxContainer _sequenceRow;
	private Label _progressLabel;
	private Label _messageLabel;
	private Label _timerLabel;
	private readonly Dictionary<string, Button> _padButtons = new();

	public SoundboardSound Sound => _sound;

	public override void _Ready()
	{
		EnsureBuilt();
	}

    private static Color PadColor(string padId) => padId switch
    {
        "RED" => Color.FromHtml("e57373"),
        "BLUE" => Color.FromHtml("64b5f6"),
        "GREEN" => Color.FromHtml("81c784"),
        "YELLOW" => Color.FromHtml("fff176"),
        _ => Color.FromHtml("90a4ae"),
    };
    
	public void Bind(SoundboardModule module)
	{
		EnsureBuilt();
		_module = module;
		module.Configured += RefreshAll;
		module.StatusChanged += OnStatusChanged;
		module.ProgressChanged += OnProgressChanged;
		module.PadResult += OnPadResult;
		module.TimeChanged += OnTimeChanged;
		RefreshAll();
	}

	private void EnsureBuilt()
	{
		if (_built)
		{
			return;
		}
		_built = true;

		_sound = new SoundboardSound();
		AddChild(_sound);

		var margin = new MarginContainer();
		foreach (var side in new[] { "left", "right", "top", "bottom" })
		{
			margin.AddThemeConstantOverride("margin_" + side, 16);
		}
		AddChild(margin);

		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 12);
		margin.AddChild(root);

		root.AddChild(MakeLabel("SOUNDBOARD", 28));

		_statusLabel = MakeLabel("", 24);
		root.AddChild(_statusLabel);

		_instructionLabel = MakeLabel("", 16);
		root.AddChild(_instructionLabel);

		_sequenceRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_sequenceRow.AddThemeConstantOverride("separation", 8);
		root.AddChild(_sequenceRow);

		_progressLabel = MakeLabel("", 18);
		root.AddChild(_progressLabel);

		_messageLabel = MakeLabel("", 20);
		_messageLabel.AddThemeColorOverride("font_color", Color.FromHtml("ffb74d"));
		root.AddChild(_messageLabel);

		_timerLabel = MakeLabel("", 16);
		root.AddChild(_timerLabel);

		var grid = new GridContainer { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 10);
		grid.AddThemeConstantOverride("v_separation", 10);
		root.AddChild(grid);
		foreach (string padId in SoundboardData.Pads)
		{
			var button = new Button { Text = padId, CustomMinimumSize = new Vector2(120, 60) };
			button.AddThemeFontSizeOverride("font_size", 15);
            Color baseColor = PadColor(padId);
            button.AddThemeStyleboxOverride("normal", MakePadStyle(baseColor));
            button.AddThemeStyleboxOverride("hover", MakePadStyle(baseColor.Lightened(0.2f)));
            button.AddThemeStyleboxOverride("pressed", MakePadStyle(baseColor.Darkened(0.2f)));
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
			foreach (var colorName in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
			{
				button.AddThemeColorOverride(colorName, Color.FromHtml("102027"));
			}
			button.Pressed += () => PadPressed?.Invoke(padId);
			grid.AddChild(button);
			_padButtons[padId] = button;
		}

		var reset = new Button { Text = "RESET SEQUENCE", CustomMinimumSize = new Vector2(260, 56) };
		reset.AddThemeFontSizeOverride("font_size", 20);
		reset.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		reset.Pressed += () => ResetSequencePressed?.Invoke();
		root.AddChild(reset);
	}

	private static Label MakeLabel(string text, int fontSize)
	{
		var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
		label.AddThemeFontSizeOverride("font_size", fontSize);
		return label;
	}

    private static StyleBoxFlat MakePadStyle(Color color)
    {
        var style = new StyleBoxFlat { BgColor = color, BorderColor = Color.FromHtml("37474f") };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(14);
        return style;
    }

    private static PanelContainer MakeChip(int number, string effect, string state, bool done, bool isNext)
	{
		var style = new StyleBoxFlat
		{
            BgColor = done ? Color.FromHtml("2e7d32") : (effect == "?" ? Color.FromHtml("37474f") : PadColor(effect)),
            BorderColor = isNext ? Color.FromHtml("fdd835") : Color.FromHtml("90a4ae"),
		};
		style.SetBorderWidthAll(isNext ? 4 : 1);
		style.SetCornerRadiusAll(8);
		style.SetContentMarginAll(8);

		var chip = new PanelContainer { CustomMinimumSize = new Vector2(120, 0) };
		chip.AddThemeStyleboxOverride("panel", style);

		var box = new VBoxContainer();
		chip.AddChild(box);
		box.AddChild(MakeLabel($"STEP {number}", 14));
		box.AddChild(MakeLabel(effect, 22));
		box.AddChild(MakeLabel(state == "" ? " " : state, 14));

        if (!done && effect != "?")
        {
            foreach (Node n in box.GetChildren())
            {
                if (n is Label l) l.AddThemeColorOverride("font_color", Color.FromHtml("102027"));
            }
        }
        return chip;
	}

	private void RefreshAll()
	{
		_instructionLabel.Text = _module.ShowSequence
			? "Read each flash to the Technician, then press the pad the manual gives."
            : "SOUND CUE HIDDEN - ask the Technician for the sequence.";
		_messageLabel.Text = "";
		OnStatusChanged(_module.Status);
		OnProgressChanged(_module.Progress, _module.Sequence.Count);
		OnTimeChanged(_module.TimeLeft);
	}

	private void RebuildSequence()
	{
		foreach (Node child in _sequenceRow.GetChildren())
		{
			_sequenceRow.RemoveChild(child);
			child.QueueFree();
		}

		int total = _module.Sequence.Count;
		for (int i = 0; i < total; i++)
		{
			bool done = i < _module.Progress;
			bool isNext = i == _module.Progress
				&& _module.Status != SoundboardModule.SoundboardStatus.Complete;

			string effect = (_module.ShowSequence || done) ? _module.Sequence[i] : "?";
			string state = done ? "DONE" : (isNext ? "NEXT" : "");

			_sequenceRow.AddChild(MakeChip(i + 1, effect, state, done, isNext));
			if (i < total - 1)
			{
				_sequenceRow.AddChild(MakeLabel(">", 24));
			}
		}
	}

	private void OnStatusChanged(SoundboardModule.SoundboardStatus status)
	{
		switch (status)
		{
			case SoundboardModule.SoundboardStatus.Ready:
				SetStatusText("READY", Colors.White);
				break;
			case SoundboardModule.SoundboardStatus.Working:
				SetStatusText("WORKING", Color.FromHtml("64b5f6"));
				break;
			case SoundboardModule.SoundboardStatus.Check:
				SetStatusText("CHECK - time ran out", Color.FromHtml("ffb74d"));
				break;
			case SoundboardModule.SoundboardStatus.Complete:
				SetStatusText("COMPLETE", Color.FromHtml("81c784"));
				break;
			case SoundboardModule.SoundboardStatus.Retry:
				SetStatusText("RETRY - wrong pad", Color.FromHtml("ffb74d"));
				break;
		}
		RebuildSequence();
	}

	private void SetStatusText(string text, Color color)
	{
		_statusLabel.Text = text;
		_statusLabel.AddThemeColorOverride("font_color", color);
	}

	private void OnProgressChanged(int done, int total)
	{
		_progressLabel.Text = total > 0 ? $"Progress: {done} of {total}" : "";
		_messageLabel.Text = "";
		RebuildSequence();
	}

	private void OnPadResult(string padId, bool correct, string expectedNext)
	{
		if (_padButtons.TryGetValue(padId, out var button))
		{
			button.Modulate = correct ? new Color(0.55f, 1.4f, 0.55f) : new Color(1.7f, 1.2f, 0.5f);
			CreateTween().TweenProperty(button, "modulate", Colors.White, 0.45);
		}

        _sound.Play(SoundboardData.SoundFor(padId));

        if (!correct)
		{
            _messageLabel.Text = "WRONG PAD - check the manual";
        }
	}

	private void OnTimeChanged(float secondsLeft)
	{
		_timerLabel.Text = _module.TimeLimit > 0f
			? $"TIME LEFT: {Mathf.CeilToInt(secondsLeft)}"
			: "";
	}
}
