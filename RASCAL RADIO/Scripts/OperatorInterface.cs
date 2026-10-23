using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;

public partial class OperatorInterface : Control
{
	[Export] public bool TestKeys { get; set; } = true;

	public RadioOperatorLogic Logic { get; private set; }

	private static readonly Color FaultColor = new Color(1f, 0.45f, 0.35f);
	private static readonly Color LightOn = new Color(0.3f, 1f, 0.4f);
	private static readonly Color LightOff = new Color(0.22f, 0.22f, 0.22f);

	private static readonly string[] PanelTitles = { "A  Power", "B  Channel", "C  Module", "D  Generator" };

	private const float TestGeneratorSeconds = 90f;
	private double _attackLeft;
	private bool _testOutage;

	private Label _powerLabel, _channelLabel, _linkLabel, _errorLabel;
	private Label _callStatusLabel, _feedbackLabel;
	private Label _voltageLabel, _powerStatusLabel;
	private Label _targetLabel, _dialLabel, _tuneLabel;
	private Label _moduleLabel;
	private Label _mainPowerLabel, _backupLabel, _generatorLabel;
	private ProgressBar _signalBar, _voltageBar, _generatorBar;
	private TabContainer _panels;
	private Button _leverButton, _dipTestButton, _acceptButton, _rejectButton;
	private Button _dialDownTen, _dialDownOne, _dialUpOne, _dialUpTen;
	private Control _dipLight;

	private readonly Button[] _powerSwitchButtons = new Button[RadioRules.PowerSwitchCount];
	private readonly Button[] _dipButtons = new Button[RadioRules.DipSwitchCount];
	private readonly Label[] _componentLabels = new Label[RadioRules.ComponentCount];
	private readonly Button[] _componentButtons = new Button[RadioRules.ComponentCount];
	private readonly Dictionary<RadioChannel, Button> _channelButtons = new();
	private readonly Dictionary<WarningLight, WarningTriangle> _lamps = new();

	private T Bind<T>(string name) where T : Node => GetNode<T>("%" + name);

	public override void _Ready()
	{
		_powerLabel = Bind<Label>("PowerLabel");
		_channelLabel = Bind<Label>("ChannelLabel");
		_linkLabel = Bind<Label>("LinkLabel");
		_errorLabel = Bind<Label>("ErrorLabel");
		_callStatusLabel = Bind<Label>("CallStatusLabel");
		_feedbackLabel = Bind<Label>("FeedbackLabel");
		_voltageLabel = Bind<Label>("VoltageLabel");
		_powerStatusLabel = Bind<Label>("PowerStatusLabel");
		_targetLabel = Bind<Label>("TargetLabel");
		_dialLabel = Bind<Label>("DialLabel");
		_tuneLabel = Bind<Label>("TuneLabel");
		_moduleLabel = Bind<Label>("ModuleLabel");
		_mainPowerLabel = Bind<Label>("MainPowerLabel");
		_backupLabel = Bind<Label>("BackupLabel");
		_generatorLabel = Bind<Label>("GeneratorLabel");
		_signalBar = Bind<ProgressBar>("SignalBar");
		_voltageBar = Bind<ProgressBar>("VoltageBar");
		_generatorBar = Bind<ProgressBar>("GeneratorBar");
		_panels = Bind<TabContainer>("Panels");
		_leverButton = Bind<Button>("MasterLever");
		_dipTestButton = Bind<Button>("DipTestButton");
		_acceptButton = Bind<Button>("AcceptButton");
		_rejectButton = Bind<Button>("RejectButton");
		_dialDownTen = Bind<Button>("DialDownTen");
		_dialDownOne = Bind<Button>("DialDownOne");
		_dialUpOne = Bind<Button>("DialUpOne");
		_dialUpTen = Bind<Button>("DialUpTen");
		_dipLight = Bind<Control>("DipLight");

		for (int i = 0; i < _powerSwitchButtons.Length; i++)
		{
			_powerSwitchButtons[i] = Bind<Button>($"PowerSwitch{i + 1}");
			_powerSwitchButtons[i].Text = $"Switch {i + 1}: {RadioRules.PowerSwitchLabels[i]}";
		}

		for (int i = 0; i < _dipButtons.Length; i++)
			_dipButtons[i] = Bind<Button>($"DipSwitch{i + 1}");

		for (int i = 0; i < RadioRules.ComponentCount; i++)
		{
			_componentLabels[i] = Bind<Label>($"Component{i + 1}Label");
			_componentButtons[i] = Bind<Button>($"Component{i + 1}Button");
		}

		foreach (RadioChannel channel in Enum.GetValues<RadioChannel>())
			_channelButtons[channel] = Bind<Button>($"{channel}Button");

		foreach (WarningLight light in Enum.GetValues<WarningLight>())
			if (light != WarningLight.None)
				_lamps[light] = Bind<WarningTriangle>($"Lamp{light}");

		for (int i = 0; i < PanelTitles.Length && i < _panels.GetTabCount(); i++)
			_panels.SetTabTitle(i, PanelTitles[i]);

		_signalBar.MinValue = 0;
		_signalBar.MaxValue = 100;
		_voltageBar.MinValue = 0;
		_voltageBar.MaxValue = 100;
		_generatorBar.MinValue = 0;
		_generatorBar.MaxValue = 100;

		Logic = new RadioOperatorLogic();
		Logic.Changed += Refresh;
		Logic.Notice += message => _feedbackLabel.Text = message;

		Logic.CallDecided += accepted => GD.Print($"TODO (host): call {(accepted ? "accepted" : "rejected")}.");
		Logic.CallLost += reason => GD.Print($"TODO (host): caller lost, reason {reason}.");
		Logic.PowerChanged += on => GD.Print($"TODO (host): radio power {(on ? "on" : "off")}.");
		Logic.ChannelChanged += channel => GD.Print($"TODO (host): channel changed to {channel}.");
		Logic.ModuleRestored += () => GD.Print("TODO (host): module restored.");
		Logic.ComponentReset += (index, correct) => GD.Print($"TODO (host): {RadioRules.ComponentNames[index]} reset ({(correct ? "was the failed part" : "was not the failed part")}).");
		Logic.GeneratorDepleted += () => GD.Print("TODO (host): generator is empty.");

		for (int i = 0; i < _powerSwitchButtons.Length; i++)
		{
			int captured = i;
			_powerSwitchButtons[i].Toggled += on => Logic.SetPowerSwitch(captured, on);
		}

		_leverButton.Toggled += on => Logic.SetLever(on);

		foreach (var pair in _channelButtons)
		{
			RadioChannel captured = pair.Key;
			pair.Value.Pressed += () => Logic.SelectChannel(captured);
		}

		_dialDownTen.Pressed += () => Logic.AdjustDial(-10);
		_dialDownOne.Pressed += () => Logic.AdjustDial(-1);
		_dialUpOne.Pressed += () => Logic.AdjustDial(1);
		_dialUpTen.Pressed += () => Logic.AdjustDial(10);

		for (int i = 0; i < _dipButtons.Length; i++)
		{
			int captured = i;
			_dipButtons[i].Toggled += on => Logic.SetDipSwitch(captured, on);
		}

		_dipTestButton.Pressed += () => Logic.PressDipTest();

		for (int i = 0; i < _componentButtons.Length; i++)
		{
			int captured = i;
			_componentButtons[i].Pressed += () => Logic.ResetComponent(captured);
		}

		_acceptButton.Pressed += () => Logic.AcceptCall();
		_rejectButton.Pressed += () => Logic.RejectCall();

		if (TestKeys)
			GD.Print("Test keys: Q quick-starts the radio, C rings a call, 1 antenna attack, 2 interference, 3 corrupted module, 4 power outage.");

		_feedbackLabel.Text = "Radio is off. Start it up from Panel A.";
		Refresh();
	}

	public override void _Process(double delta)
	{
		if (Logic == null) return;

		Logic.Tick(delta);

		if (TestKeys) RunTestHost(delta);
	}

	private void Refresh()
	{
		RefreshStatus();
		RefreshPowerPanel();
		RefreshChannelPanel();
		RefreshModulePanel();
		RefreshGeneratorPanel();
		RefreshCall();
	}

	private void RefreshStatus()
	{
		_powerLabel.Text = Logic.RadioOn ? "Power: On" : "Power: Off";
		_channelLabel.Text = Logic.RadioOn ? $"Channel: {Logic.Channel}" : "Channel: --";
		_linkLabel.Text = $"Link: {Logic.Link}";
		_signalBar.Value = Logic.Signal;

		var codes = Logic.WarningTexts.ToList();
		_errorLabel.Text = codes.Count == 0 ? "Error codes: --" : $"Error codes: {string.Join(", ", codes)}";

		var lit = Logic.ActiveWarnings.Select(w => w.Light).ToHashSet();
		foreach (var pair in _lamps)
			pair.Value.Lit = lit.Contains(pair.Key);
	}

	private void RefreshPowerPanel()
	{
		for (int i = 0; i < _powerSwitchButtons.Length; i++)
			_powerSwitchButtons[i].SetPressedNoSignal(Logic.GetPowerSwitch(i));

		_leverButton.SetPressedNoSignal(Logic.LeverOn);
		_leverButton.Text = Logic.LeverOn ? "Master Power: ON" : "Master Power: OFF";

		_voltageBar.Value = Logic.Voltage;
		_voltageLabel.Text = Logic.InSafeZone
			? $"Voltage: {Logic.Voltage:0}  (SAFE ZONE)"
			: $"Voltage: {Logic.Voltage:0}  (safe zone starts at {RadioRules.SafeVoltageMin:0})";

		if (Logic.LockLeft > 0f)
			_powerStatusLabel.Text = $"System locked: {Logic.LockLeft:0.0}s";
		else if (Logic.Fault == PowerFault.FuseTripped)
			_powerStatusLabel.Text = "Master fuse tripped.";
		else if (Logic.Fault == PowerFault.StartupFailed)
			_powerStatusLabel.Text = "Startup failed.";
		else
			_powerStatusLabel.Text = Logic.RadioOn ? "Radio is running." : "Radio is off.";
	}

	private void RefreshChannelPanel()
	{
		foreach (var pair in _channelButtons)
			pair.Value.SetPressedNoSignal(pair.Key == Logic.Channel);

		_targetLabel.Text = Logic.RadioOn ? $"Target: {RadioChannels.Code(Logic.Channel)}" : "Target: --";
		_dialLabel.Text = $"Dial: {Logic.Dial}";

		if (!Logic.RadioOn)
			_tuneLabel.Text = "Signal indicator: --";
		else
			_tuneLabel.Text = Logic.SignalStable ? "Signal indicator: STABLE" : "Signal indicator: not stable";
	}

	private void RefreshModulePanel()
	{
		for (int i = 0; i < _dipButtons.Length; i++)
			_dipButtons[i].SetPressedNoSignal(Logic.GetDipSwitch(i));

		if (!Logic.RadioOn)
			_moduleLabel.Text = "Module: --";
		else if (Logic.DipLockLeft > 0f)
			_moduleLabel.Text = $"Module locked out: {Logic.DipLockLeft:0.0}s";
		else if (Logic.ModuleCorrupted)
			_moduleLabel.Text = $"MODULE ERROR. Target code: {Logic.TargetCode}";
		else
			_moduleLabel.Text = "Module OK";

		_dipLight.Modulate = Logic.DipLightOn ? LightOn : LightOff;
	}

	private void RefreshGeneratorPanel()
	{
		_mainPowerLabel.Text = Logic.MainPowerOut ? "Main power: OUTAGE" : "Main power: OK";
		_backupLabel.Text = Logic.MainPowerOut ? "Backup generator: ACTIVE" : "Backup generator: standby";
		_generatorBar.Value = Logic.GeneratorCharge;
		_generatorLabel.Text = $"Charge: {Logic.GeneratorCharge:0}%";

		for (int i = 0; i < _componentLabels.Length; i++)
		{
			bool faulted = Logic.ComponentFaulted(i);
			_componentLabels[i].Text = $"{RadioRules.ComponentNames[i]}: {(faulted ? "FAULT" : "OK")}";

			if (faulted) _componentLabels[i].AddThemeColorOverride("font_color", FaultColor);
			else _componentLabels[i].RemoveThemeColorOverride("font_color");
		}
	}

	private void RefreshCall()
	{
		_callStatusLabel.Text = Logic.HasIncomingCall ? "Incoming call!" : "The line is quiet.";
		_acceptButton.Disabled = !Logic.HasIncomingCall;
		_rejectButton.Disabled = !Logic.HasIncomingCall;
	}

	public override void _Input(InputEvent @event)
	{
		if (Logic == null || !TestKeys) return;

		if (@event is InputEventKey { Pressed: true, Echo: false } key)
		{
			switch (key.Keycode)
			{
				case Key.Q: Logic.DemoQuickStart(); break;
				case Key.C: RingTestCall(); break;
				case Key.Key1: TriggerAttack(); break;
				case Key.Key2: TriggerInterference(); break;
				case Key.Key3: TriggerModule(); break;
				case Key.Key4: TriggerOutage(); break;
			}
		}
	}

	private void RingTestCall()
	{
		Logic.SetIncomingCall(!Logic.HasIncomingCall);

		if (Logic.HasIncomingCall)
			GD.Print("Test call ringing. Play the Broadcaster: say accept or reject out loud.");
	}

	private void RunTestHost(double delta)
	{
		if (_attackLeft > 0)
		{
			_attackLeft -= delta;
			if (_attackLeft <= 0)
			{
				Logic.SetAntennaAttack(0f);
				GD.Print("Test: the raccoon leaves the antenna.");
			}
		}

		if (Logic.CompromisedChannel.HasValue && Logic.Channel != Logic.CompromisedChannel && Logic.SignalStable)
		{
			Logic.SetCompromisedChannel(null);
			GD.Print("Test: interference cleared.");
		}

		if (_testOutage)
		{
			if (Logic.MainPowerOut)
				Logic.SetGeneratorCharge(Logic.GeneratorCharge - (float)(100.0 / TestGeneratorSeconds * delta));
			else
				_testOutage = false;
		}
	}

	private void TriggerAttack()
	{
		Logic.SetAntennaAttack(55f);
		_attackLeft = 12.0;
		GD.Print("Test: the raccoon attacks the antenna. Report the signal meter.");
	}

	private void TriggerInterference()
	{
		Logic.SetCompromisedChannel(Logic.Channel);
		GD.Print($"Test: channel {Logic.Channel} is compromised. Switch to a working channel and tune the dial (Zebra 173, Alpha 246, Delta 315).");
	}

	private void TriggerModule()
	{
		int target = (int)(GD.Randi() % 255) + 1;
		Logic.CorruptModule(target);
		GD.Print($"Test: module corrupted, target code {target}. Set the DIP switches (128, 64, 32, 16, 8, 4, 2, 1) to add up to it, then press TEST.");
	}

	private void TriggerOutage()
	{
		int failed = (int)(GD.Randi() % RadioRules.ComponentCount);
		Logic.SetGeneratorCharge(100f);
		Logic.StartOutage(failed, true);
		_testOutage = true;
		GD.Print($"Test: main power outage. The failed part is {RadioRules.ComponentNames[failed]}. Reset it, then restart the radio from Panel A.");
	}
}
