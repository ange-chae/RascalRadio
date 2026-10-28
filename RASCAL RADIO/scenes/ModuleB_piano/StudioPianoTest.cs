using Godot;

/// <summary>
/// Attach to a Control root node in a scene and press F6.
/// Tests the module alone: no host, no LAN, no other modules.
/// </summary>
public partial class StudioPianoTest : Control
{
	private StudioPianoModule _module;
	private StudioPianoUI _ui;
	private Button _notesButton;

	private string _currentSequence = "Morning Jingle";
	private bool _showNotes = false; // default matches the manual: Operator sees the jingle NAME only

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		_module = new StudioPianoModule();
		AddChild(_module);

		_ui = new StudioPianoUI();
		_ui.Bind(_module);
		// Local stand-in for the network: UI requests go straight to the module.
		_ui.KeyPressed += _module.RequestKey;
		_ui.OctaveStepPressed += _module.RequestOctaveStep;
		_module.Completed += () => GD.Print("[Piano] COMPLETE");
		_module.TimedOut += () => GD.Print("[Piano] TIMED OUT");
		_module.FaultChanged += inFault => GD.Print($"[Piano] fault = {inFault}");

		var center = new CenterContainer();
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 16);
		center.AddChild(column);
		column.AddChild(_ui);

		// Test controls
		var tests = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		column.AddChild(tests);

		foreach (var sequenceName in new[]
		{
			"Morning Jingle", "Night Jingle", "Breaking News Alert", StudioPianoData.RecalibrationName,
		})
		{
			var button = new Button { Text = sequenceName };
			button.Pressed += () => LoadSequence(sequenceName);
			tests.AddChild(button);
		}

		var faultButton = new Button { Text = "Trigger Fault (keys flash red)" };
		faultButton.Pressed += () => _module.TriggerFault();
		tests.AddChild(faultButton);

		_notesButton = new Button();
		_notesButton.Pressed += () =>
		{
			_showNotes = !_showNotes;
			LoadSequence(_currentSequence);
		};
		tests.AddChild(_notesButton);

		var toggle = new Button { Text = "Toggle Active" };
		toggle.Pressed += () =>
		{
			_module.SetActive(!_module.Active);
			GD.Print($"[Piano] active = {_module.Active}");
		};
		tests.AddChild(toggle);

		LoadSequence(_currentSequence);
	}

	private void LoadSequence(string sequenceName)
	{
		_currentSequence = sequenceName;
		bool isRecalibration = sequenceName == StudioPianoData.RecalibrationName;

		var cues = isRecalibration
			? StudioPianoData.GetRecalibration()
			: StudioPianoData.GetJingles()[sequenceName];

		// Jingles enforce octave (Morning = NORMAL, Night = -, Breaking News = +).
		// Recalibration is notes only, no octave.
		_module.Configure(sequenceName, cues, useOctave: !isRecalibration, timeLimit: 30f, showNotes: _showNotes);
		_module.SetActive(true);
		_module.Start();

		_notesButton.Text = _showNotes ? "Notes: SHOWN" : "Notes: HIDDEN";
	}
}
