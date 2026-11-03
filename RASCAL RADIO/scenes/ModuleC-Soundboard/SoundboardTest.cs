using Godot;

public partial class SoundboardTest : Control
{
	private SoundboardModule _module;
	private SoundboardUI _ui;
	private Button _sequenceButton;

	private string _currentSequence = "Caller Prank";
	private bool _showSequence = true;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		_module = new SoundboardModule();
		AddChild(_module);

		_ui = new SoundboardUI();
		_ui.Bind(_module);
		_ui.PadPressed += _module.RequestPad;
		_ui.ResetSequencePressed += _module.ResetSequence;
		_module.Completed += () => GD.Print("[Soundboard] COMPLETE");
		_module.TimedOut += () => GD.Print("[Soundboard] TIMED OUT");

		var center = new CenterContainer();
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 16);
		center.AddChild(column);
		column.AddChild(_ui);

		var tests = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.Center };
		column.AddChild(tests);

		foreach (string sequenceName in SoundboardData.GetSequences().Keys)
		{
			var button = new Button { Text = sequenceName };
			button.Pressed += () => LoadSequence(sequenceName);
			tests.AddChild(button);
		}

		_sequenceButton = new Button();
		_sequenceButton.Pressed += () =>
		{
			_showSequence = !_showSequence;
			LoadSequence(_currentSequence);
		};
		tests.AddChild(_sequenceButton);

		var toggle = new Button { Text = "Toggle Active" };
		toggle.Pressed += () =>
		{
			_module.SetActive(!_module.Active);
			GD.Print($"[Soundboard] active = {_module.Active}");
		};
		tests.AddChild(toggle);

		var soundButton = new Button { Text = "Sound: ON" };
		soundButton.Pressed += () =>
		{
			_ui.Sound.Muted = !_ui.Sound.Muted;
			soundButton.Text = _ui.Sound.Muted ? "Sound: OFF" : "Sound: ON";
		};
		tests.AddChild(soundButton);

        var strikeButton = new Button { Text = "Strikes: 0" };
        strikeButton.Pressed += () =>
        {
            int next = (_module.Strikes + 1) % 3;
            _module.SetStrikes(next);
            strikeButton.Text = $"Strikes: {next}";
        };
        tests.AddChild(strikeButton);

        LoadSequence(_currentSequence);
	}

	private void LoadSequence(string sequenceName)
	{
		_currentSequence = sequenceName;
		_module.Configure(SoundboardData.GetSequences()[sequenceName], 30f, _showSequence);
		_module.SetActive(true);
		_module.Start();

		_sequenceButton.Text = _showSequence ? "Sequence: SHOWN" : "Sequence: HIDDEN";
	}
}
