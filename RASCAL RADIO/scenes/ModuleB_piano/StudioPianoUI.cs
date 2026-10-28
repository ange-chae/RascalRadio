using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Operator view for the Studio Piano. It only DISPLAYS state and RAISES requests.
/// Local test: connect KeyPressed to module.RequestKey.
/// LAN: connect KeyPressed to an RPC that asks the host to call RequestKey.
/// </summary>
public partial class StudioPianoUI : PanelContainer
{
	public event Action<string> KeyPressed;
	public event Action<int> OctaveStepPressed;

	private StudioPianoModule _module;
	private bool _built;

	private ColorRect _cueSwatch;
	private Label _cueLabel;
	private Label _progressLabel;
	private Label _statusLabel;
	private Label _timerLabel;
	private HBoxContainer _octaveRow;
	private Label _octaveLabel;
	private HBoxContainer _keysRow;
	private Tween _faultTween;
	private readonly Dictionary<string, Button> _keyButtons = new();

	public override void _Ready()
	{
		EnsureBuilt();
	}

	/// <summary>Call once. Reconfiguring the module later is fine.</summary>
	public void Bind(StudioPianoModule module)
	{
		EnsureBuilt();
		_module = module;
		module.Configured += RefreshAll;
		module.StatusChanged += OnStatusChanged;
		module.CueChanged += OnCueChanged;
		module.OctaveChanged += OnOctaveChanged;
		module.KeyResult += OnKeyResult;
		module.TimeChanged += OnTimeChanged;
		module.FaultChanged += OnFaultChanged;
		module.FaultProgressChanged += OnFaultProgressChanged;
		RefreshAll();
	}

	private void EnsureBuilt()
	{
		if (_built)
		{
			return;
		}
		_built = true;

		var margin = new MarginContainer();
		foreach (var side in new[] { "left", "right", "top", "bottom" })
		{
			margin.AddThemeConstantOverride("margin_" + side, 16);
		}
		AddChild(margin);

		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 12);
		margin.AddChild(root);

		var title = new Label { Text = "STUDIO PIANO", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 28);
		root.AddChild(title);

		_statusLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_statusLabel.AddThemeFontSizeOverride("font_size", 24);
		root.AddChild(_statusLabel);

		// Cue row: swatch + "BLUE LIGHT — E"  (or "PLAY: MORNING JINGLE" when notes are hidden)
		var cueRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		cueRow.AddThemeConstantOverride("separation", 12);
		root.AddChild(cueRow);
		_cueSwatch = new ColorRect { CustomMinimumSize = new Vector2(48, 48) };
		cueRow.AddChild(_cueSwatch);
		_cueLabel = new Label();
		_cueLabel.AddThemeFontSizeOverride("font_size", 36);
		cueRow.AddChild(_cueLabel);

		_progressLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		root.AddChild(_progressLabel);

		_timerLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		root.AddChild(_timerLabel);

		// Octave controls: [-] OCTAVE: NORMAL [+]
		_octaveRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_octaveRow.AddThemeConstantOverride("separation", 12);
		root.AddChild(_octaveRow);

		var minus = new Button { Text = "-", CustomMinimumSize = new Vector2(64, 48) };
		minus.Pressed += () => OctaveStepPressed?.Invoke(-1);
		_octaveRow.AddChild(minus);

		_octaveLabel = new Label
		{
			CustomMinimumSize = new Vector2(200, 0),
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		_octaveRow.AddChild(_octaveLabel);

		var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(64, 48) };
		plus.Pressed += () => OctaveStepPressed?.Invoke(1);
		_octaveRow.AddChild(plus);

		// Seven tall white piano keys in one row, letter near the bottom of each key.
		_keysRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_keysRow.AddThemeConstantOverride("separation", 3);
		root.AddChild(_keysRow);
		foreach (var letter in StudioPianoData.Keys)
		{
			var button = new Button { Text = letter, CustomMinimumSize = new Vector2(80, 240) };
			button.AddThemeFontSizeOverride("font_size", 32);
			button.AddThemeStyleboxOverride("normal", MakeKeyStyle(Colors.White));
			button.AddThemeStyleboxOverride("hover", MakeKeyStyle(Color.FromHtml("f5f5f5")));
			button.AddThemeStyleboxOverride("pressed", MakeKeyStyle(Color.FromHtml("cfcfcf")));
			button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
			foreach (var colorName in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
			{
				button.AddThemeColorOverride(colorName, Color.FromHtml("212121"));
			}
			button.Pressed += () => KeyPressed?.Invoke(letter);
			_keysRow.AddChild(button);
			_keyButtons[letter] = button;
		}
	}

	private static StyleBoxFlat MakeKeyStyle(Color background)
	{
		var style = new StyleBoxFlat
		{
			BgColor = background,
			BorderColor = Color.FromHtml("424242"),
			CornerRadiusTopLeft = 0,
			CornerRadiusTopRight = 0,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10,
			ContentMarginTop = 140, // pushes the letter toward the bottom of the key
		};
		style.SetBorderWidthAll(2);
		return style;
	}

	private void RefreshAll()
	{
		_octaveRow.Visible = _module.UseOctave;
		OnStatusChanged(_module.Status);
		OnOctaveChanged(_module.Octave);
		OnTimeChanged(_module.TimeLeft);

		if (_module.InFault)
		{
			ShowFaultDisplay();
			StartFaultFlash();
			return;
		}

		StopFaultFlash();
		if (_module.Cues.Count == 0)
		{
			_cueSwatch.Visible = false;
			_cueLabel.Text = "NO CUE";
			_progressLabel.Text = "";
		}
		else
		{
			int index = Mathf.Clamp(_module.CueIndex, 0, _module.Cues.Count - 1);
			OnCueChanged(index, _module.Cues.Count, _module.Cues[index]);
		}
	}

	private void OnStatusChanged(StudioPianoModule.PianoStatus status)
	{
		// Text always carries the meaning; color is only extra.
		switch (status)
		{
			case StudioPianoModule.PianoStatus.Ready:
				SetStatusText("READY", Colors.White);
				break;
			case StudioPianoModule.PianoStatus.Working:
				SetStatusText("WORKING", Color.FromHtml("64b5f6"));
				break;
			case StudioPianoModule.PianoStatus.Check:
				if (_module.InFault)
				{
					SetStatusText("CHECK - KEYS FLASHING RED", Color.FromHtml("ef5350"));
				}
				else
				{
					SetStatusText("CHECK - time ran out", Color.FromHtml("ffb74d"));
				}
				break;
			case StudioPianoModule.PianoStatus.Complete:
				SetStatusText("COMPLETE", Color.FromHtml("81c784"));
				break;
			case StudioPianoModule.PianoStatus.Retry:
				SetStatusText("RETRY - reread the cue", Color.FromHtml("ffb74d"));
				break;
		}
	}

	private void SetStatusText(string text, Color color)
	{
		_statusLabel.Text = text;
		_statusLabel.AddThemeColorOverride("font_color", color);
	}

	private void OnCueChanged(int index, int total, StudioPianoData.Cue cue)
	{
		if (_module.ShowNotes)
		{
			_cueSwatch.Visible = true;
			_cueSwatch.Color = StudioPianoData.SwatchColors.TryGetValue(cue.ColorName, out var swatch)
				? swatch
				: Colors.Gray;

			string text = $"{cue.ColorName} LIGHT — {cue.Letter}";
			if (_module.UseOctave)
			{
				text += $"  (OCTAVE {StudioPianoData.OctaveLabel(cue.Octave)})";
			}
			_cueLabel.Text = text;
		}
		else
		{
			// Notes hidden: the Technician reads them from the manual table.
			_cueSwatch.Visible = false;
			_cueLabel.Text = $"PLAY: {_module.SequenceName.ToUpper()}";
		}
		_progressLabel.Text = $"Step {index + 1} of {total}";
	}

	private void OnOctaveChanged(int octave)
	{
		_octaveLabel.Text = $"OCTAVE: {StudioPianoData.OctaveLabel(octave)}";
	}

	private void OnTimeChanged(float secondsLeft)
	{
		if (_module.TimeLimit <= 0f)
		{
			_timerLabel.Text = "";
		}
		else if (_module.InFault)
		{
			_timerLabel.Text = "TIMER PAUSED";
		}
		else
		{
			_timerLabel.Text = $"TIME LEFT: {Mathf.CeilToInt(secondsLeft)}";
		}
	}

	private void OnKeyResult(string letter, bool correct)
	{
		if (!_keyButtons.TryGetValue(letter, out var button))
		{
			return;
		}
		button.Modulate = correct ? new Color(0.5f, 1.0f, 0.5f) : new Color(1.0f, 0.75f, 0.3f);
		CreateTween().TweenProperty(button, "modulate", Colors.White, 0.4);
	}

	private void OnFaultChanged(bool inFault)
	{
		if (inFault)
		{
			ShowFaultDisplay();
			StartFaultFlash();
		}
		else
		{
			StopFaultFlash(); // the module re-emits the interrupted cue, which restores the display
		}
		OnTimeChanged(_module.TimeLeft);
	}

	private void OnFaultProgressChanged(int done, int total)
	{
		_progressLabel.Text = $"Recalibrating: {done} of {total}";
	}

	private void ShowFaultDisplay()
	{
		_cueSwatch.Visible = false;
		_cueLabel.Text = "RECALIBRATE";
		_progressLabel.Text = $"Recalibrating: {_module.FaultIndex} of 7";
	}

	// Keys flash red while the module is in a fault. Text (CHECK - KEYS FLASHING RED) says the same thing.
	private void StartFaultFlash()
	{
		StopFaultFlash();
		if (!IsInsideTree())
		{
			return;
		}
		_faultTween = CreateTween().SetLoops();
		_faultTween.TweenProperty(_keysRow, "modulate", new Color(1.0f, 0.35f, 0.35f), 0.4);
		_faultTween.TweenProperty(_keysRow, "modulate", Colors.White, 0.4);
	}

	private void StopFaultFlash()
	{
		_faultTween?.Kill();
		_faultTween = null;
		if (_keysRow != null)
		{
			_keysRow.Modulate = Colors.White;
		}
	}
}
