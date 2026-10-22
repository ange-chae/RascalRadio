
using Godot;
using System;
using System.Threading.Tasks;

public partial class SystemPanel : Control
{
	private const int MaxReserveSeconds = 360;
	private const int RestartAllCostSeconds = 30;

	private int _reserveSeconds = MaxReserveSeconds;
	private double _drainAccumulator;

	private bool _powerInterrupted;
	private bool _fuseTripped;

	private bool _transmitterFault;
	private bool _receiverFault;
	private bool _consoleFault;

	private bool _isRestartingAll;
	private bool _isRestartingTransmitter;
	private bool _isRestartingReceiver;
	private bool _isRestartingConsole;

	private PanelContainer _panelFrame;
	private Button _toggleButton;
	private Control _panelBody;
	private Control _recoveryOverlay;

	private Label _gridStatus;
	private Label _reserveLabel;
	private Label _faultLabel;

	private Label _transmitterLabel;
	private Label _receiverLabel;
	private Label _consoleLabel;
	private Label _historyLabel;

	private Label _recoveryTransmitterLabel;
	private Label _recoveryReceiverLabel;
	private Label _recoveryConsoleLabel;

	private Button _restartAllButton;
	private Button _resetFuseButton;

	private ProgressBar _reserveBar;

	private static readonly Color Cyan =
		new(0f, 0.94f, 0.93f);

	private static readonly Color Green =
		new(0.15f, 1f, 0.52f);

	private static readonly Color Yellow =
		new(1f, 0.75f, 0.15f);

	private static readonly Color Red =
		new(1f, 0.30f, 0.35f);

	private static readonly Color Muted =
		new(0.57f, 0.66f, 0.71f);

	public override void _Ready()
	{
		// Let the underlying room equipment receive clicks
		// wherever this UI does not have an interactive control.
		SetFullRect(this);
		MouseFilter = MouseFilterEnum.Ignore;

		BuildMainPanel();
		BuildRecoveryPanel();

		UpdateInterface();

		LogEvent(
			"Technician interface initialized.",
			Green
		);

		LogEvent(
			"Main grid available. Generator reserve at 100%.",
			Cyan
		);
	}

	public override void _Process(double delta)
	{
		// Reserve only drains during a station-wide power interruption.
		if (!_powerInterrupted || _reserveSeconds <= 0)
			return;

		_drainAccumulator += delta;

		while (_drainAccumulator >= 1.0 &&
			   _reserveSeconds > 0)
		{
			_drainAccumulator -= 1.0;
			_reserveSeconds--;

			if (_reserveSeconds == 0)
			{
				LogEvent(
					"Generator reserve depleted. Restore main power.",
					Red
				);
			}

			UpdateInterface();
		}
	}

	// ============================================================
	// MAIN BOTTOM SYSTEM PANEL
	// ============================================================

	private void BuildMainPanel()
	{
		_panelFrame = new PanelContainer();
		_panelFrame.Name = "SystemPanelFrame";
		AddChild(_panelFrame);

		_panelFrame.AnchorLeft = 0;
		_panelFrame.AnchorRight = 1;
		_panelFrame.AnchorTop = 1;
		_panelFrame.AnchorBottom = 1;

		_panelFrame.OffsetLeft = 20;
		_panelFrame.OffsetRight = -20;
		_panelFrame.OffsetTop = -60;
		_panelFrame.OffsetBottom = -12;

		ApplyPanelStyle(
			_panelFrame,
			new Color(0.035f, 0.065f, 0.085f, 0.98f)
		);

		MarginContainer margin = new();
		SetMargins(margin, 10);
		_panelFrame.AddChild(margin);

		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 8);
		margin.AddChild(layout);

		_toggleButton = new Button();
		_toggleButton.Text = "▲ OPEN SYSTEM PANEL ▲";
		_toggleButton.CustomMinimumSize = new Vector2(0, 36);
		_toggleButton.SizeFlagsHorizontal =
			SizeFlags.ExpandFill;

		layout.AddChild(_toggleButton);
		_toggleButton.Pressed += ToggleMainPanel;

		_panelBody = new HBoxContainer();
		_panelBody.Name = "SystemPanelContents";
		_panelBody.Visible = false;
		_panelBody.CustomMinimumSize = new Vector2(0, 190);
		_panelBody.SizeFlagsHorizontal =
			SizeFlags.ExpandFill;

		_panelBody.AddThemeConstantOverride("separation", 10);
		layout.AddChild(_panelBody);

		BuildPowerSection();
		BuildDiagnosticsSection();
		BuildHistorySection();
	}

	private void ToggleMainPanel()
	{
		bool open = !_panelBody.Visible;
		_panelBody.Visible = open;

		_panelFrame.OffsetTop = open ? -295 : -60;

		_toggleButton.Text = open
			? "▼ CLOSE SYSTEM PANEL ▼"
			: "▲ OPEN SYSTEM PANEL ▲";
	}

	private void BuildPowerSection()
	{
		VBoxContainer section = CreateSection(
			_panelBody,
            "POWER STATUS"
		);

		_gridStatus = AddReadout(
			section,
			"MAIN GRID",
            "AVAILABLE"
		);

		_reserveLabel = AddReadout(
			section,
			"GENERATOR RESERVE",
            "100%"
		);

		_reserveBar = new ProgressBar();
		_reserveBar.Name = "ReserveBar";
		_reserveBar.MinValue = 0;
		_reserveBar.MaxValue = MaxReserveSeconds;
		_reserveBar.Value = MaxReserveSeconds;
		_reserveBar.ShowPercentage = false;
		_reserveBar.CustomMinimumSize = new Vector2(0, 12);

		section.AddChild(_reserveBar);

		_reserveBar.AddThemeStyleboxOverride(
			"fill",
			CreateFlatStyle(Green)
		);

		Button outageButton = CreateButton(
            "SIMULATE OUTAGE"
		);

		section.AddChild(outageButton);
		outageButton.Pressed += StartPowerInterruption;

		Button restoreButton = CreateButton(
            "RESTORE MAIN POWER"
		);

		section.AddChild(restoreButton);
		restoreButton.Pressed += RestoreMainPower;

		_resetFuseButton = CreateButton("RESET MASTER FUSE");
		section.AddChild(_resetFuseButton);
		_resetFuseButton.Pressed += ResetMasterFuse;
	}

	private void BuildDiagnosticsSection()
	{
		VBoxContainer section = CreateSection(
			_panelBody,
            "SYSTEM DIAGNOSTICS"
		);

		_transmitterLabel = AddReadout(
			section,
			"TRANSMITTER",
            "OPERATIONAL"
		);

		_receiverLabel = AddReadout(
			section,
			"RECEIVER ARRAY",
            "OPERATIONAL"
		);

		_consoleLabel = AddReadout(
			section,
			"CONTROL CONSOLE",
            "OPERATIONAL"
		);

		_faultLabel = AddReadout(
			section,
			"CURRENT FAULT",
            "NONE"
		);

		Button simulateFaultButton = CreateButton(
            "SIMULATE TRANSMITTER FAULT"
		);

		section.AddChild(simulateFaultButton);

		simulateFaultButton.Pressed += () =>
		{
			_transmitterFault = true;

			LogEvent(
				"Transmitter fault detected. Targeted recovery recommended.",
				Red
			);

			UpdateInterface();
		};

		Button simulateFuseButton = CreateButton(
            "SIMULATE TRIPPED FUSE"
		);

		section.AddChild(simulateFuseButton);

		simulateFuseButton.Pressed += () =>
		{
			_fuseTripped = true;

			LogEvent(
				"Master fuse tripped. Reset the fuse before recovery.",
				Red
			);

			UpdateInterface();
		};

		Button openRecoveryButton = CreateButton(
            "OPEN RECOVERY PANEL"
		);

		section.AddChild(openRecoveryButton);
		openRecoveryButton.Pressed += OpenRecoveryPanel;
	}

	private void BuildHistorySection()
	{
		VBoxContainer section = CreateSection(
			_panelBody,
            "DIAGNOSTIC TERMINAL"
		);

		_historyLabel = new Label();
		_historyLabel.CustomMinimumSize = new Vector2(0, 110);
		_historyLabel.SizeFlagsHorizontal =
			SizeFlags.ExpandFill;

		_historyLabel.AutowrapMode =
			TextServer.AutowrapMode.WordSmart;

		_historyLabel.VerticalAlignment =
			VerticalAlignment.Top;

		_historyLabel.AddThemeColorOverride(
			"font_color",
			Muted
		);

		_historyLabel.AddThemeFontSizeOverride(
			"font_size",
			11
		);

		section.AddChild(_historyLabel);

		Button clearButton = CreateButton("CLEAR HISTORY");
		section.AddChild(clearButton);

		clearButton.Pressed += () =>
		{
			_historyLabel.Text = "";
			LogEvent("Event history cleared.", Cyan);
		};
	}

	// ============================================================
	// SEPARATE RECOVERY PANEL
	// ============================================================

	private void BuildRecoveryPanel()
	{
		_recoveryOverlay = new Control();
		_recoveryOverlay.Name = "RecoveryPanel";

		SetFullRect(_recoveryOverlay);

		_recoveryOverlay.Visible = false;
		_recoveryOverlay.MouseFilter =
			MouseFilterEnum.Stop;

		AddChild(_recoveryOverlay);

		ColorRect shade = new();
		shade.Color = new Color(0, 0, 0, 0.78f);
		shade.MouseFilter = MouseFilterEnum.Stop;

		SetFullRect(shade);
		_recoveryOverlay.AddChild(shade);

		PanelContainer window = new();
		window.Name = "RecoveryWindow";

		window.AnchorLeft = 0.5f;
		window.AnchorRight = 0.5f;
		window.AnchorTop = 0.5f;
		window.AnchorBottom = 0.5f;

		window.OffsetLeft = -290;
		window.OffsetRight = 290;
		window.OffsetTop = -215;
		window.OffsetBottom = 215;

		ApplyPanelStyle(
			window,
			new Color(0.035f, 0.065f, 0.085f, 1f)
		);

		_recoveryOverlay.AddChild(window);

		MarginContainer margin = new();
		SetMargins(margin, 16);
		window.AddChild(margin);

		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 10);
		margin.AddChild(layout);

		HBoxContainer header = new();
		layout.AddChild(header);

		Label title = MakeLabel(
			"RECOVERY PANEL",
			Cyan,
			17
		);

		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddChild(title);

		Button closeButton = CreateButton("CLOSE");
		header.AddChild(closeButton);
		closeButton.Pressed += CloseRecoveryPanel;

		Label description = MakeLabel(
			"Restart faulty equipment here. These controls are " +
			"separate from the main diagnostic panel.",
			Muted,
			12
		);

		description.AutowrapMode =
			TextServer.AutowrapMode.WordSmart;

		layout.AddChild(description);

		AddRecoveryRow(
			layout,
			"TRANSMITTER",
			"Signal generation and broadcast output",
			out _recoveryTransmitterLabel,
			RestartTransmitter
		);

		AddRecoveryRow(
			layout,
			"RECEIVER ARRAY",
			"Incoming signal monitoring",
			out _recoveryReceiverLabel,
			RestartReceiver
		);

		AddRecoveryRow(
			layout,
			"CONTROL CONSOLE",
			"Technician interface and diagnostics",
			out _recoveryConsoleLabel,
			RestartConsole
		);

		_restartAllButton = CreateButton(
            "RESTART ALL SYSTEMS"
		);

		_restartAllButton.CustomMinimumSize =
			new Vector2(0, 42);

		layout.AddChild(_restartAllButton);
		_restartAllButton.Pressed += RestartAllSystems;

		Label note = MakeLabel(
			"Individual restarts are free. Restart All consumes " +
			"reserve only during a power interruption. " +
			"A tripped fuse must be reset separately.",
			Yellow,
			11
		);

		note.AutowrapMode =
			TextServer.AutowrapMode.WordSmart;

		layout.AddChild(note);
	}

	private void AddRecoveryRow(
		VBoxContainer parent,
		string systemName,
		string description,
		out Label statusLabel,
		Action restartAction
	)
	{
		PanelContainer rowPanel = new();

		ApplyPanelStyle(
			rowPanel,
			new Color(0.055f, 0.09f, 0.115f, 1f)
		);

		parent.AddChild(rowPanel);

		MarginContainer margin = new();
		SetMargins(margin, 8);
		rowPanel.AddChild(margin);

		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 10);
		margin.AddChild(row);

		VBoxContainer details = new();
		details.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(details);

		Label name = MakeLabel(
			systemName,
			Colors.White,
			12
		);

		details.AddChild(name);

		Label detail = MakeLabel(
			description,
			Muted,
			10
		);

		detail.AutowrapMode =
			TextServer.AutowrapMode.WordSmart;

		details.AddChild(detail);

		statusLabel = MakeLabel(
			"OPERATIONAL",
			Green,
			10
		);

		row.AddChild(statusLabel);

		Button restartButton = CreateButton("RESTART");
		row.AddChild(restartButton);

		restartButton.Pressed += restartAction;
	}

	private void OpenRecoveryPanel()
	{
		UpdateInterface();
		_recoveryOverlay.Visible = true;
	}

	private void CloseRecoveryPanel()
	{
		_recoveryOverlay.Visible = false;
	}

	// ============================================================
	// POWER AND FUSE LOGIC
	// ============================================================

	private void StartPowerInterruption()
	{
		if (_powerInterrupted)
		{
			LogEvent(
				"Power interruption is already active.",
				Yellow
			);

			return;
		}

		_powerInterrupted = true;

		LogEvent(
			"Power interruption. Generator reserve is now draining.",
			Yellow
		);

		UpdateInterface();
	}

	private void RestoreMainPower()
	{
		if (!_powerInterrupted)
		{
			LogEvent(
				"Main power is already available.",
				Cyan
			);

			return;
		}

		_powerInterrupted = false;
		_drainAccumulator = 0;

		LogEvent(
			"Main power restored. Reserve drain paused.",
			Green
		);

		UpdateInterface();
	}

	private void ResetMasterFuse()
	{
		if (!_fuseTripped)
		{
			LogEvent(
				"Master fuse is already normal.",
				Cyan
			);

			return;
		}

		_fuseTripped = false;

		LogEvent(
			"Master fuse reset. Electrical protection restored.",
			Green
		);

		UpdateInterface();
	}

	// ============================================================
	// INDIVIDUAL EQUIPMENT RESTARTS
	// ============================================================

	private async void RestartTransmitter()
	{
		if (_isRestartingTransmitter)
			return;

		if (_fuseTripped)
		{
			LogEvent(
				"Recovery blocked. Reset the master fuse first.",
				Red
			);

			return;
		}

		if (!_transmitterFault)
		{
			LogEvent(
				"Transmitter is already operational.",
				Cyan
			);

			return;
		}

		_isRestartingTransmitter = true;

		SetStatus(
			_recoveryTransmitterLabel,
			"RESTARTING...",
			Yellow
		);

		LogEvent(
			"Transmitter restart started. No reserve cost.",
			Cyan
		);

		await ToSignal(
			GetTree().CreateTimer(0.8),
			SceneTreeTimer.SignalName.Timeout
		);

		_transmitterFault = false;
		_isRestartingTransmitter = false;

		UpdateInterface();

		LogEvent(
			"Transmitter operational. Signal check passed.",
			Green
		);
	}

	private async void RestartReceiver()
	{
		if (_isRestartingReceiver)
			return;

		if (_fuseTripped)
		{
			LogEvent(
				"Recovery blocked. Reset the master fuse first.",
				Red
			);

			return;
		}

		if (!_receiverFault)
		{
			LogEvent(
				"Receiver array is already operational.",
				Cyan
			);

			return;
		}

		_isRestartingReceiver = true;

		SetStatus(
			_recoveryReceiverLabel,
			"RESTARTING...",
			Yellow
		);

		LogEvent(
			"Receiver array restart started. No reserve cost.",
			Cyan
		);

		await ToSignal(
			GetTree().CreateTimer(0.8),
			SceneTreeTimer.SignalName.Timeout
		);

		_receiverFault = false;
		_isRestartingReceiver = false;

		UpdateInterface();

		LogEvent(
			"Receiver array operational.",
			Green
		);
	}

	private async void RestartConsole()
	{
		if (_isRestartingConsole)
			return;

		if (_fuseTripped)
		{
			LogEvent(
				"Recovery blocked. Reset the master fuse first.",
				Red
			);

			return;
		}

		if (!_consoleFault)
		{
			LogEvent(
				"Control console is already operational.",
				Cyan
			);

			return;
		}

		_isRestartingConsole = true;

		SetStatus(
			_recoveryConsoleLabel,
			"RESTARTING...",
			Yellow
		);

		LogEvent(
			"Control console restart started. No reserve cost.",
			Cyan
		);

		await ToSignal(
			GetTree().CreateTimer(0.8),
			SceneTreeTimer.SignalName.Timeout
		);

		_consoleFault = false;
		_isRestartingConsole = false;

		UpdateInterface();

		LogEvent(
			"Control console operational.",
			Green
		);
	}

	// ============================================================
	// RESTART ALL
	// ============================================================

	private async void RestartAllSystems()
	{
		if (_isRestartingAll)
			return;

		if (_fuseTripped)
		{
			LogEvent(
				"Restart All blocked. Reset the master fuse first.",
				Red
			);

			return;
		}

		if (!_powerInterrupted &&
			!_transmitterFault &&
			!_receiverFault &&
			!_consoleFault)
		{
			LogEvent(
				"All systems are already operational.",
				Cyan
			);

			return;
		}

		if (_powerInterrupted)
		{
			if (_reserveSeconds < RestartAllCostSeconds)
			{
				LogEvent(
					"Insufficient reserve for Restart All.",
					Red
				);

				return;
			}

			_reserveSeconds -= RestartAllCostSeconds;

			LogEvent(
				"Restart All used approximately " +
				Mathf.CeilToInt(
					(float)RestartAllCostSeconds /
					MaxReserveSeconds * 100f
				) +
				"% reserve. " +
				GetReservePercentage() +
				"% remains.",
				Yellow
			);
		}
		else
		{
			LogEvent(
				"Restart All started on main power. " +
				"No reserve cost.",
				Cyan
			);
		}

		_isRestartingAll = true;
		_restartAllButton.Disabled = true;

		LogEvent(
			"Station-wide recovery sequence started.",
			Yellow
		);

		UpdateInterface();

		await ToSignal(
			GetTree().CreateTimer(1.2),
			SceneTreeTimer.SignalName.Timeout
		);

		_transmitterFault = false;
		_receiverFault = false;
		_consoleFault = false;

		_isRestartingAll = false;
		_restartAllButton.Disabled = false;

		UpdateInterface();

		LogEvent(
			"Restart All complete. Operational checks passed.",
			Green
		);
	}

	// ============================================================
	// UPDATE UI
	// ============================================================

	private void UpdateInterface()
	{
		int percentage = GetReservePercentage();

		if (_gridStatus != null)
		{
			_gridStatus.Text = _powerInterrupted
				? "OFFLINE"
				: "AVAILABLE";

			_gridStatus.Modulate =
				_powerInterrupted ? Red : Green;
		}

		if (_reserveLabel != null)
		{
			_reserveLabel.Text = percentage + "%";

			_reserveLabel.Modulate =
				percentage <= 20 ? Red :
				percentage <= 50 ? Yellow :
				Green;
		}

		if (_reserveBar != null)
		{
			_reserveBar.Value = _reserveSeconds;

			Color fill =
				percentage <= 20 ? Red :
				percentage <= 50 ? Yellow :
				Green;

			_reserveBar.AddThemeStyleboxOverride(
				"fill",
				CreateFlatStyle(fill)
			);
		}

		if (_faultLabel != null)
		{
			bool equipmentFault =
				_transmitterFault ||
				_receiverFault ||
				_consoleFault;

			if (_fuseTripped)
			{
				_faultLabel.Text = "MASTER FUSE TRIPPED";
				_faultLabel.Modulate = Red;
			}
			else if (equipmentFault)
			{
				_faultLabel.Text = "EQUIPMENT FAULT";
				_faultLabel.Modulate = Red;
			}
			else
			{
				_faultLabel.Text = "NONE";
				_faultLabel.Modulate = Green;
			}
		}

		UpdateSystemStatus(
			_transmitterLabel,
			_transmitterFault
		);

		UpdateSystemStatus(
			_receiverLabel,
			_receiverFault
		);

		UpdateSystemStatus(
			_consoleLabel,
			_consoleFault
		);

		UpdateSystemStatus(
			_recoveryTransmitterLabel,
			_transmitterFault
		);

		UpdateSystemStatus(
			_recoveryReceiverLabel,
			_receiverFault
		);

		UpdateSystemStatus(
			_recoveryConsoleLabel,
			_consoleFault
		);

		if (_resetFuseButton != null)
		{
			_resetFuseButton.Disabled = !_fuseTripped;
		}

		if (_restartAllButton != null)
		{
			bool noWork =
				!_powerInterrupted &&
				!_transmitterFault &&
				!_receiverFault &&
				!_consoleFault;

			_restartAllButton.Disabled =
				noWork || _fuseTripped || _isRestartingAll;
		}
	}

	private int GetReservePercentage()
	{
		return Mathf.RoundToInt(
			(float)_reserveSeconds /
			MaxReserveSeconds * 100f
		);
	}

	private void UpdateSystemStatus(
		Label label,
		bool fault
	)
	{
		if (label == null)
			return;

		label.Text = fault
			? "FAULT"
			: "OPERATIONAL";

		label.Modulate = fault ? Red : Green;
	}

	private void SetStatus(
		Label label,
		string text,
		Color color
	)
	{
		if (label == null)
			return;

		label.Text = text;
		label.Modulate = color;
	}

	// ============================================================
	// EVENT HISTORY
	// ============================================================

	private void LogEvent(
		string message,
		Color color
	)
	{
		if (_historyLabel == null)
			return;

		string timestamp =
			DateTime.Now.ToString("HH:mm:ss");

		_historyLabel.Text =
			$"[{timestamp}] {message}\n" +
			_historyLabel.Text;

		_historyLabel.Modulate = color;
	}

	// ============================================================
	// UI CREATION HELPERS
	// ============================================================

	private VBoxContainer CreateSection(
		Control parent,
		string title
	)
	{
		// FIXED: Control accepts _panelBody as well as other
		// UI container types. This resolves the 3 errors.
		PanelContainer card = new();

		card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		card.SizeFlagsVertical = SizeFlags.ExpandFill;

		ApplyPanelStyle(
			card,
			new Color(0.045f, 0.075f, 0.095f, 1f)
		);

		parent.AddChild(card);

		MarginContainer margin = new();
		SetMargins(margin, 10);
		card.AddChild(margin);

		VBoxContainer content = new();

		content.AddThemeConstantOverride(
			"separation",
			8
		);

		margin.AddChild(content);

		Label heading = MakeLabel(
			title,
			Cyan,
			12
		);

		content.AddChild(heading);

		return content;
	}

	private Label AddReadout(
		VBoxContainer parent,
		string name,
		string initialValue
	)
	{
		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);

		Label nameLabel = MakeLabel(
			name,
			Muted,
			10
		);

		nameLabel.SizeFlagsHorizontal =
			SizeFlags.ExpandFill;

		row.AddChild(nameLabel);

		Label valueLabel = MakeLabel(
			initialValue,
			Green,
			10
		);

		row.AddChild(valueLabel);

		return valueLabel;
	}

	private Button CreateButton(string text)
	{
		Button button = new();

		button.Text = text;
		button.CustomMinimumSize = new Vector2(0, 28);

		button.SizeFlagsHorizontal =
			SizeFlags.ExpandFill;

		button.AddThemeFontSizeOverride(
			"font_size",
			10
		);

		button.AddThemeColorOverride(
			"font_color",
			Cyan
		);

		return button;
	}

	private Label MakeLabel(
		string text,
		Color color,
		int fontSize
	)
	{
		Label label = new();

		label.Text = text;

		label.AddThemeColorOverride(
			"font_color",
			color
		);

		label.AddThemeFontSizeOverride(
			"font_size",
			fontSize
		);

		return label;
	}

	private void ApplyPanelStyle(
		PanelContainer panel,
		Color background
	)
	{
		StyleBoxFlat style = new();

		style.BgColor = background;

		style.BorderColor =
			new Color(0.15f, 0.27f, 0.34f, 1f);

		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);

		panel.AddThemeStyleboxOverride(
			"panel",
			style
		);
	}

	private StyleBoxFlat CreateFlatStyle(Color color)
	{
		StyleBoxFlat style = new();

		style.BgColor = color;
		style.SetCornerRadiusAll(3);

		return style;
	}

	private void SetMargins(
		MarginContainer margin,
		int size
	)
	{
		margin.AddThemeConstantOverride(
			"margin_left",
			size
		);

		margin.AddThemeConstantOverride(
			"margin_right",
			size
		);

		margin.AddThemeConstantOverride(
			"margin_top",
			size
		);

		margin.AddThemeConstantOverride(
			"margin_bottom",
			size
		);
	}

	private void SetFullRect(Control control)
	{
		control.AnchorLeft = 0;
		control.AnchorTop = 0;
		control.AnchorRight = 1;
		control.AnchorBottom = 1;

		control.OffsetLeft = 0;
		control.OffsetTop = 0;
		control.OffsetRight = 0;
		control.OffsetBottom = 0;
	}
}
