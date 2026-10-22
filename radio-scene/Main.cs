using Godot;
using System;
using System.Collections.Generic;

public partial class Main : Control
{
	private const float HeaderHeight = 58.0f;
	private const float FloorHeight = 132.0f;
	private const float SystemPanelHeight = 292.0f;
	private const float GeneratorMaxSeconds = 360.0f;
	private const float RestartAllReserveCostSeconds = 30.0f;

	private readonly Color Cyan = new Color("#00DCD2");
	private readonly Color Green = new Color("#54D982");
	private readonly Color Amber = new Color("#FFC247");
	private readonly Color Red = new Color("#FF626B");
	private readonly Color PanelColor = new Color("#111A23");
	private readonly Color LineColor = new Color("#34434E");
	private readonly Color Muted = new Color("#91A2B2");

	private TextureRect background;
	private Control roomLayer;
	private Control interfaceLayer;
	private TextureRect breakerTexture;
	private TextureRect generatorTexture;
	private TextureRect techManualTexture;

	private ColorRect headerRect;

	private Label brandLabel;
	private Label subbrandLabel;
	private Label roomLabel;
	private Label generatorLabel;
	private Label generatorPercentLabel;

	private Button breakerButton;
	private Button manualButton;
	private Button panelToggleButton;
	private Panel systemPanel;
	private Panel equipmentPanel;
	private Panel eventPanel;
	private Label panelMeta;
	private Label eventLogLabel;
	private Button[] equipmentButtons = Array.Empty<Button>();
	private Button restartSelectedButton;
	private Button restartAllButton;

	private Control manualOverlay;
	private Panel manualCard;
	private Button manualCloseButton;
	private ScrollContainer manualScroll;

	private Control breakerOverlay;
	private Panel breakerCard;
	private Button breakerCloseButton;
	private Button panelATabButton;
	private Button fuseTabButton;
	private Control sequenceLayer;
	private Control fuseLayer;
	private Label voltageStatusLabel;
	private Label breakerMessageLabel;
	private Label fuseStatusLabel;
	private Label fuseDescriptionLabel;
	private Button[] breakerSwitchButtons = Array.Empty<Button>();
	private Button masterPowerButton;
	private Button resetFuseButton;

	private readonly string[] equipmentNames =
	{
		"MAIN TRANSMITTER",
		"AUDIO PROCESSOR",
		"ANTENNA MONITOR"
	};

	private readonly string[] equipmentStatuses = { "FAULT", "ONLINE", "WARNING" };
	private readonly bool[] needsAttention = { true, false, true };
	private readonly bool[] equipmentRestarting = { false, false, false };
	private readonly List<string> eventLog = new List<string>();

	private readonly string[] breakerSwitchNames =
	{
		"MAIN ISOLATION",
		"FILAMENT HEATER",
		"PLATE VOLTAGE",
		"ANTENNA LOAD"
	};

	private readonly bool[] breakerSwitchesOn = { false, false, false, false };
	private int selectedEquipment;
	private int breakerSequenceVersion;
	private int stabilizationSecondsLeft;
	private float generatorReserveSeconds = GeneratorMaxSeconds;
	private bool panelOpen;
	private bool restartingAll;
	private bool voltageStable;
	private bool masterFuseTripped;
	private bool breakerControlsLocked;
	// Reserved for future power-state integration; no outage trigger is exposed here.
	public bool MainPowerInterrupted { get; set; }

	public override void _Ready()
	{
		SetFullRect(this);

		background = GetNodeOrNull<TextureRect>("Background");
		roomLayer = GetNodeOrNull<Control>("Room");
		interfaceLayer = GetNodeOrNull<Control>("Interface");

		if (background == null || roomLayer == null || interfaceLayer == null)
		{
			GD.PrintErr("Main scene is missing Background, Room, or Interface.");
			return;
		}

		breakerTexture = roomLayer.GetNodeOrNull<TextureRect>("BreakerTexture");
		generatorTexture = roomLayer.GetNodeOrNull<TextureRect>("GeneratorTexture");
		techManualTexture = roomLayer.GetNodeOrNull<TextureRect>("TechManualTexture");

		SetFullRect(background);
		SetFullRect(roomLayer);
		SetFullRect(interfaceLayer);
		background.StretchMode = TextureRect.StretchModeEnum.Scale;
		background.MouseFilter = MouseFilterEnum.Ignore;
		background.ZIndex = -20;
		roomLayer.MouseFilter = MouseFilterEnum.Ignore;
		interfaceLayer.MouseFilter = MouseFilterEnum.Pass;
		interfaceLayer.ZIndex = 10;

		ConfigureEquipmentTexture(breakerTexture);
		ConfigureEquipmentTexture(generatorTexture);
		ConfigureEquipmentTexture(techManualTexture);

		BuildRoomChrome();
		BuildSystemPanel();
		BuildManualPanel();
		BuildBreakerPanel();

		GetViewport().SizeChanged += UpdateLayout;
		UpdateLayout();
		UpdateGeneratorReadout();

		LogEvent("Monitoring terminal initialized.");
		LogEvent("Equipment status scan completed.");
		LogEvent("Main transmitter fault detected.");
		LogEvent("Antenna monitor signal check required.");
		RefreshSystemPanel();
		RefreshBreakerPanel();
	}

	private void ConfigureEquipmentTexture(TextureRect rect)
	{
		if (rect == null)
		{
			return;
		}

		rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		rect.StretchMode = TextureRect.StretchModeEnum.Scale;
		rect.MouseFilter = MouseFilterEnum.Ignore;
		rect.ZIndex = 2;
	}

	private void BuildRoomChrome()
	{
		headerRect = MakeColorRect(interfaceLayer, "TopBar", new Color("#071017"), 1);

		brandLabel = MakeLabel(interfaceLayer, "RADIO TECHNICIAN", Cyan, 16, 3);
		subbrandLabel = MakeLabel(interfaceLayer, "TECHNICIAN STATION // TRAINING BUILD", Muted, 9, 3);
		roomLabel = MakeLabel(interfaceLayer, "MAINTENANCE ROOM — CONTROL INTERFACE", new Color("#42515B"), 11, 2);
		generatorLabel = MakeLabel(roomLayer, "BACKUP POWER\nGENERATOR", new Color("#F3D5CF"), 10, 3);
		generatorPercentLabel = MakeLabel(roomLayer, "100%", Green, 14, 3);

		generatorLabel.HorizontalAlignment = HorizontalAlignment.Center;
		generatorLabel.VerticalAlignment = VerticalAlignment.Center;
		generatorPercentLabel.HorizontalAlignment = HorizontalAlignment.Center;
		generatorPercentLabel.AddThemeFontSizeOverride("font_size", 14);

		breakerButton = MakeButton(interfaceLayer, "CIRCUIT BREAKER", Colors.White, 9, 4);
		ApplyButtonStyle(breakerButton, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), new Color("#17462F"));
		breakerButton.Pressed += OpenBreakerPanel;

		manualButton = MakeButton(interfaceLayer, "TECHNICIAN MANUAL", new Color("#5A451B"), 9, 4);
		ApplyButtonStyle(manualButton, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), new Color("#FFD366"));
		manualButton.AddThemeColorOverride("font_hover_color", new Color("#3D3116"));
		manualButton.Pressed += OpenManual;

		panelToggleButton = MakeButton(interfaceLayer, "▲ OPEN SYSTEM PANEL ▲", Cyan, 10, 10);
		ApplyButtonStyle(panelToggleButton, new Color("#111A23"), LineColor, new Color("#182833"));
		panelToggleButton.Pressed += ToggleSystemPanel;
}

	private void BuildSystemPanel()
	{
		systemPanel = new Panel();
		systemPanel.Name = "DiagnosticTerminal";
		systemPanel.Visible = false;
		systemPanel.ZIndex = 20;
		systemPanel.MouseFilter = MouseFilterEnum.Stop;
		ApplyPanelStyle(systemPanel, PanelColor, new Color("#2C3946"));
		interfaceLayer.AddChild(systemPanel);

		Label title = MakePanelLabel("DIAGNOSTIC TERMINAL", Cyan, 15, 22);
		title.Position = new Vector2(18, 12);
		title.Size = new Vector2(320, 24);
		systemPanel.AddChild(title);

		panelMeta = MakePanelLabel("SYS_VER 4.02 // TECH ACCESS", Muted, 9, 22);
		panelMeta.HorizontalAlignment = HorizontalAlignment.Right;
		systemPanel.AddChild(panelMeta);

		equipmentPanel = new Panel();
		equipmentPanel.Name = "ConnectedEquipment";
		ApplyPanelStyle(equipmentPanel, new Color("#0A0F15"), new Color("#2C3945"));
		systemPanel.AddChild(equipmentPanel);

		eventPanel = new Panel();
		eventPanel.Name = "LiveEventLog";
		ApplyPanelStyle(eventPanel, new Color("#0A0F15"), new Color("#2C3945"));
		systemPanel.AddChild(eventPanel);

		Label equipmentHeading = MakePanelLabel("CONNECTED EQUIPMENT", new Color("#AABAC7"), 9, 23);
		equipmentHeading.Position = new Vector2(10, 7);
		equipmentHeading.Size = new Vector2(220, 18);
		equipmentPanel.AddChild(equipmentHeading);

		Label eventHeading = MakePanelLabel("LIVE EVENT LOG", new Color("#AABAC7"), 9, 23);
		eventHeading.Position = new Vector2(10, 7);
		eventHeading.Size = new Vector2(220, 18);
		eventPanel.AddChild(eventHeading);

		equipmentButtons = new Button[equipmentNames.Length];
		for (int i = 0; i < equipmentNames.Length; i++)
		{
			int equipmentIndex = i;
			Button row = MakeButton(equipmentPanel, "", Muted, 9, 24);
			row.Alignment = HorizontalAlignment.Left;
			ApplyButtonStyle(row, new Color("#121922"), new Color("#24313C"), new Color("#10262A"));
			row.Pressed += () => SelectEquipment(equipmentIndex);
			equipmentButtons[i] = row;
		}

		restartSelectedButton = MakeButton(equipmentPanel, "RESTART SELECTED", new Color("#D7FFFF"), 9, 24);
		ApplyButtonStyle(restartSelectedButton, new Color("#064D4E"), Cyan, new Color("#087474"));
		restartSelectedButton.Pressed += RestartSelectedEquipment;

		restartAllButton = MakeButton(equipmentPanel, "RESTART ALL", Amber, 9, 24);
		ApplyButtonStyle(restartAllButton, new Color("#2A2115"), new Color("#84502A"), new Color("#392A18"));
		restartAllButton.Pressed += RestartAllEquipment;

		eventLogLabel = MakePanelLabel("", new Color("#93A5B3"), 9, 24);
		eventLogLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		eventPanel.AddChild(eventLogLabel);
}

	private void BuildManualPanel()
	{
		manualOverlay = MakeOverlay("ManualOverlay", 40, new Color(0, 0, 0, 0.72f));
		manualOverlay.Visible = false;
		manualCard = MakePanel("ManualCard", new Color("#101720"), new Color("#344450"), 41);
		manualOverlay.AddChild(manualCard);

		Label heading = MakePanelLabel("RESTRICTED DOCUMENT", Amber, 16, 42);
		heading.Position = new Vector2(20, 14);
		heading.Size = new Vector2(400, 24);
		manualCard.AddChild(heading);

		Label subtitle = MakePanelLabel("Rascal Radio Equipment Manual // Technicians Only", Muted, 10, 42);
		subtitle.Position = new Vector2(20, 39);
		subtitle.Size = new Vector2(480, 18);
		manualCard.AddChild(subtitle);

		manualCloseButton = MakeButton(manualCard, "✕", Colors.White, 12, 42);
		ApplyButtonStyle(manualCloseButton, new Color("#101720"), new Color("#42515C"), new Color("#263642"));
		manualCloseButton.Pressed += CloseManual;

		manualScroll = new ScrollContainer();
		manualScroll.Name = "ManualScroll";
		manualCard.AddChild(manualScroll);

		VBoxContainer content = new VBoxContainer();
		content.Name = "ManualContent";
		content.AddThemeConstantOverride("separation", 9);
		content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		manualScroll.AddChild(content);

		Label documentTitle = MakePanelLabel("RASCAL RADIO EQUIPMENT MANUAL", new Color("#26251F"), 17, 43);
		documentTitle.HorizontalAlignment = HorizontalAlignment.Center;
		documentTitle.CustomMinimumSize = new Vector2(0, 34);
		content.AddChild(ManualPaperBlock(documentTitle, 44));

		Label restricted = MakePanelLabel("RESTRICTED ACCESS — TECHNICIANS ONLY", new Color("#26251F"), 11, 43);
		restricted.HorizontalAlignment = HorizontalAlignment.Center;
		content.AddChild(ManualPaperBlock(restricted, 38));

		Label pageTitle = MakePanelLabel("Manual Page A1: Standard Startup", new Color("#26251F"), 15, 43);
		content.AddChild(ManualPaperBlock(pageTitle, 42));

		Label panelTitle = MakePanelLabel("Panel A — Main Power Sequence", new Color("#26251F"), 13, 43);
		content.AddChild(ManualPaperBlock(panelTitle, 38));

		Label description = MakePanelLabel(
			"Use this procedure to safely restore power to the primary radio equipment.",
			new Color("#26251F"), 11, 43
		);
		description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(ManualPaperBlock(description, 54));

		GridContainer controlTable = new GridContainer();
		controlTable.Name = "ControlTable";
		controlTable.Columns = 2;
		controlTable.AddThemeConstantOverride("h_separation", 0);
		controlTable.AddThemeConstantOverride("v_separation", 0);
		AddManualTableCell(controlTable, "CONTROL", true);
		AddManualTableCell(controlTable, "LABEL", true);
		AddManualTableCell(controlTable, "Switch 1", false);
		AddManualTableCell(controlTable, "Main Isolation", false);
		AddManualTableCell(controlTable, "Switch 2", false);
		AddManualTableCell(controlTable, "Filament Heater", false);
		AddManualTableCell(controlTable, "Switch 3", false);
		AddManualTableCell(controlTable, "Plate Voltage", false);
		AddManualTableCell(controlTable, "Switch 4", false);
		AddManualTableCell(controlTable, "Antenna Load", false);
		AddManualTableCell(controlTable, "Large red lever", false);
		AddManualTableCell(controlTable, "Master Power", false);
		content.AddChild(ManualPaperBlock(controlTable, 222));

		Label procedure = MakePanelLabel(
			"STARTUP PROCEDURE\n" +
			"1. Confirm all four switches are OFF.\n" +
			"2. Turn ON Switch 1: Main Isolation.\n" +
			"3. Turn ON Switch 2: Filament Heater.\n" +
			"4. Wait until the voltage meter enters the safe zone (approximately 3 seconds).\n" +
			"5. Turn ON Switch 3: Plate Voltage.\n" +
			"6. Turn ON Switch 4: Antenna Load.\n" +
			"7. Pull the Master Power lever to complete startup.",
			new Color("#26251F"), 11, 43
		);
		procedure.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(ManualPaperBlock(procedure, 172));

		Label faults = MakePanelLabel(
			"FAULTS\n" +
			"• Wrong order trips the master fuse.\n" +
			"• Activating the next switch before the meter stabilizes temporarily locks the system.\n" +
			"• Pulling the lever too early causes startup failure.",
			new Color("#26251F"), 11, 43
		);
		faults.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(ManualPaperBlock(faults, 106));

		Label reset = MakePanelLabel(
			"RESET\nIf the fuse trips, all switches automatically reset. Start again from Step 1.",
			new Color("#26251F"), 11, 43
		);
		reset.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(ManualPaperBlock(reset, 72));
	}

	private Control ManualPaperBlock(Control child, float height)
	{
		Panel paper = MakePanel("ManualPaperBlock", new Color("#EFE5C9"), new Color("#EFE5C9"), 43);
		paper.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		paper.CustomMinimumSize = new Vector2(0, height);
		paper.AddChild(child);
		child.Position = new Vector2(12, 9);
		child.Size = new Vector2(560, height - 18);
		return paper;
	}

	private void AddManualTableCell(GridContainer table, string text, bool header)
	{
		Panel cell = MakePanel("ManualTableCell", new Color("#EFE5C9"), new Color("#77766D"), 44);
		Label label = MakePanelLabel(text, new Color("#26251F"), 10, 45);
		if (header)
		{
			label.AddThemeFontSizeOverride("font_size", 11);
		}
		label.Position = new Vector2(7, 5);
		label.Size = new Vector2(220, 26);
		cell.CustomMinimumSize = new Vector2(240, 34);
		cell.AddChild(label);
		table.AddChild(cell);
	}

	private void BuildBreakerPanel()
	{
		breakerOverlay = MakeOverlay("BreakerOverlay", 50, new Color(0, 0, 0, 0.68f));
		breakerOverlay.Visible = false;
		breakerCard = MakePanel("BreakerCard", new Color("#101720"), new Color("#344450"), 51);
		breakerOverlay.AddChild(breakerCard);

		Label title = MakePanelLabel("CIRCUIT BREAKER", Cyan, 16, 52);
		title.Position = new Vector2(20, 14);
		title.Size = new Vector2(360, 26);
		breakerCard.AddChild(title);

		breakerCloseButton = MakeButton(breakerCard, "✕", Colors.White, 12, 52);
		ApplyButtonStyle(breakerCloseButton, new Color("#101720"), new Color("#42515C"), new Color("#263642"));
		breakerCloseButton.Pressed += CloseBreakerPanel;

		panelATabButton = MakeButton(breakerCard, "PANEL A — MAIN POWER", Cyan, 9, 52);
		ApplyButtonStyle(panelATabButton, new Color("#10262A"), Cyan, new Color("#18383A"));
		panelATabButton.Pressed += () => SetBreakerTab(false);

		fuseTabButton = MakeButton(breakerCard, "FUSE MANAGEMENT", Muted, 9, 52);
		ApplyButtonStyle(fuseTabButton, new Color("#151E28"), LineColor, new Color("#263642"));
		fuseTabButton.Pressed += () => SetBreakerTab(true);

		sequenceLayer = new Control();
		sequenceLayer.Name = "PanelAContent";
		breakerCard.AddChild(sequenceLayer);

		voltageStatusLabel = MakePanelLabel("VOLTAGE: STANDBY", Amber, 11, 53);
		sequenceLayer.AddChild(voltageStatusLabel);

		breakerSwitchButtons = new Button[breakerSwitchNames.Length];
		for (int i = 0; i < breakerSwitchNames.Length; i++)
		{
			int switchIndex = i;
			Button switchButton = MakeButton(sequenceLayer, "", Green, 10, 53);
			ApplyButtonStyle(switchButton, new Color("#1D6C49"), new Color("#2E9665"), new Color("#27885B"));
			switchButton.Pressed += () => ActivateBreakerSwitch(switchIndex);
			breakerSwitchButtons[i] = switchButton;
		}

		masterPowerButton = MakeButton(sequenceLayer, "PULL MASTER POWER", new Color("#FFF4EE"), 10, 53);
		ApplyButtonStyle(masterPowerButton, new Color("#74302E"), new Color("#9B5B53"), new Color("#8C3A36"));
		masterPowerButton.Pressed += PullMasterPower;

		breakerMessageLabel = MakePanelLabel("Confirm all four switches are OFF before startup.", Muted, 10, 53);
		breakerMessageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		sequenceLayer.AddChild(breakerMessageLabel);

		fuseLayer = new Control();
		fuseLayer.Name = "FuseManagementContent";
		fuseLayer.Visible = false;
		breakerCard.AddChild(fuseLayer);

		Panel fuseCard = MakePanel("MasterFuseCard", new Color("#151E28"), new Color("#354450"), 53);
		fuseLayer.AddChild(fuseCard);

		Label fuseTitle = MakePanelLabel("MASTER FUSE", new Color("#E3EDF5"), 13, 54);
		fuseTitle.Name = "MasterFuse";
		fuseTitle.Position = new Vector2(16, 16);
		fuseCard.AddChild(fuseTitle);

		fuseStatusLabel = MakePanelLabel("INTACT", Green, 13, 54);
		fuseStatusLabel.Name = "FuseStatus";
		fuseCard.AddChild(fuseStatusLabel);

		fuseDescriptionLabel = MakePanelLabel(
			"A fuse trip automatically resets all four switches. Reset the fuse here, then restart from Switch 1.",
			Muted, 10, 54
		);
		fuseDescriptionLabel.Name = "FuseDescription";
		fuseDescriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		fuseCard.AddChild(fuseDescriptionLabel);

		resetFuseButton = MakeButton(fuseLayer, "RESET MASTER FUSE", Colors.White, 10, 53);
		ApplyButtonStyle(resetFuseButton, new Color("#2A2115"), new Color("#84502A"), new Color("#392A18"));
		resetFuseButton.Pressed += ResetMasterFuse;
}

	private void ToggleSystemPanel()
	{
		panelOpen = !panelOpen;
		systemPanel.Visible = panelOpen;
		panelToggleButton.Text = panelOpen
			? "▼ CLOSE SYSTEM PANEL ▼"
			: "▲ OPEN SYSTEM PANEL ▲";

		if (panelOpen)
		{
			LogEvent("Diagnostic terminal opened.");
			RefreshSystemPanel();
		}

		UpdateLayout();
	}

	private void OpenManual()
	{
		manualOverlay.Visible = true;
	}

	private void CloseManual()
	{
		manualOverlay.Visible = false;
	}

	private void OpenBreakerPanel()
	{
		breakerOverlay.Visible = true;
		SetBreakerTab(masterFuseTripped);
		RefreshBreakerPanel();
	}

	private void CloseBreakerPanel()
	{
		breakerOverlay.Visible = false;
	}

	private void SetBreakerTab(bool showFuses)
	{
		sequenceLayer.Visible = !showFuses;
		fuseLayer.Visible = showFuses;
		panelATabButton.AddThemeColorOverride("font_color", showFuses ? Muted : Cyan);
		fuseTabButton.AddThemeColorOverride("font_color", showFuses ? Amber : Muted);
		RefreshBreakerPanel();
	}

	private void ActivateBreakerSwitch(int index)
	{
		if (masterFuseTripped || breakerControlsLocked || breakerSwitchesOn[index])
		{
			return;
		}

		int expectedIndex = Array.FindIndex(breakerSwitchesOn, isOn => !isOn);
		if (index != expectedIndex)
		{
			if (index == 2 && breakerSwitchesOn[1] && !voltageStable)
			{
				LockBreakerControls();
				return;
			}

			TripMasterFuse($"Wrong order: {breakerSwitchNames[index]} was activated.");
			return;
		}

		if (index == 2 && !voltageStable)
		{
			LockBreakerControls();
			return;
		}

		breakerSwitchesOn[index] = true;
		breakerSequenceVersion++;
		SetBreakerMessage($"{breakerSwitchNames[index]} is ON.", Green);
		LogEvent($"Breaker switch {index + 1} ON: {breakerSwitchNames[index]}.");
		RefreshBreakerPanel();

		if (index == 1)
		{
			StabilizeVoltage();
		}
	}

	private async void StabilizeVoltage()
	{
		int version = breakerSequenceVersion;
		voltageStable = false;

		for (int remaining = 3; remaining > 0; remaining--)
		{
			if (version != breakerSequenceVersion || !breakerSwitchesOn[1])
			{
				return;
			}

			stabilizationSecondsLeft = remaining;
			RefreshBreakerPanel();
			await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
		}

		if (version != breakerSequenceVersion || !breakerSwitchesOn[1])
		{
			return;
		}

		stabilizationSecondsLeft = 0;
		voltageStable = true;
		SetBreakerMessage("Voltage is stable. Continue with Switch 3.", Cyan);
		LogEvent("Voltage meter entered the safe zone.");
		RefreshBreakerPanel();
	}

	private async void LockBreakerControls()
	{
		if (breakerControlsLocked || masterFuseTripped)
		{
			return;
		}

		breakerControlsLocked = true;
		int version = breakerSequenceVersion;
		SetBreakerMessage("TEMPORARY LOCK — wait for the voltage meter to stabilize.", Amber);
		LogEvent("Breaker sequence temporarily locked: voltage was not stable.");
		RefreshBreakerPanel();

		await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);

		if (version != breakerSequenceVersion || masterFuseTripped)
		{
			return;
		}

		breakerControlsLocked = false;
		SetBreakerMessage("Lock released. Continue when the voltage meter is stable.", Muted);
		RefreshBreakerPanel();
	}

	private void TripMasterFuse(string reason)
	{
		masterFuseTripped = true;
		breakerControlsLocked = false;
		voltageStable = false;
		stabilizationSecondsLeft = 0;
		breakerSequenceVersion++;
		Array.Fill(breakerSwitchesOn, false);
		SetBreakerMessage("MASTER FUSE TRIPPED — all switches reset.", Red);
		fuseDescriptionLabel.Text = "The master fuse tripped. All four switches have automatically reset. Reset the fuse, then restart from Switch 1.";
		LogEvent(reason);
		LogEvent("Master fuse tripped. All breaker switches reset.");
		SetBreakerTab(true);
		RefreshBreakerPanel();
	}

	private void ResetMasterFuse()
	{
		if (!masterFuseTripped)
		{
			SetBreakerMessage("Master fuse is already intact.", Green);
			return;
		}

		masterFuseTripped = false;
		breakerControlsLocked = false;
		voltageStable = false;
		stabilizationSecondsLeft = 0;
		breakerSequenceVersion++;
		Array.Fill(breakerSwitchesOn, false);
		fuseDescriptionLabel.Text = "A fuse trip automatically resets all four switches. Reset the fuse here, then restart from Switch 1.";
		SetBreakerMessage("Fuse reset. Begin again with Switch 1: Main Isolation.", Green);
		LogEvent("Master fuse reset. Breaker sequence ready.");
		SetBreakerTab(false);
	}

	private void PullMasterPower()
	{
		if (masterFuseTripped || breakerControlsLocked)
		{
			return;
		}

		bool allSwitchesOn = true;
		foreach (bool isOn in breakerSwitchesOn)
		{
			allSwitchesOn &= isOn;
		}

		if (!allSwitchesOn || !voltageStable)
		{
			SetBreakerMessage("STARTUP FAILURE — complete the sequence and wait for stable voltage before pulling the lever.", Red);
			LogEvent("Startup failed: Master Power lever was pulled too early.");
			return;
		}

		MainPowerInterrupted = false;
		SetBreakerMessage("STARTUP COMPLETE — main power sequence restored.", Green);
		LogEvent("Main power startup sequence completed.");
		UpdateGeneratorReadout();
	}

	private void RefreshBreakerPanel()
	{
		if (breakerSwitchButtons == null || breakerSwitchButtons.Length == 0)
		{
			return;
		}

		for (int i = 0; i < breakerSwitchButtons.Length; i++)
		{
			bool isOn = breakerSwitchesOn[i];
			Button button = breakerSwitchButtons[i];
			button.Text = $"SWITCH {i + 1}  {(isOn ? "ON" : "OFF")}\n{breakerSwitchNames[i]}";
			button.Disabled = masterFuseTripped || breakerControlsLocked || isOn;
			button.AddThemeColorOverride("font_color", isOn ? new Color("#D5FBE1") : new Color("#D7E5DC"));
			button.AddThemeStyleboxOverride(
				"normal",
				MakeStylebox(isOn ? new Color("#17462F") : new Color("#1D6C49"), isOn ? Green : new Color("#2E9665"), 1)
			);
		}

		if (voltageStatusLabel != null)
		{
			if (voltageStable)
			{
				voltageStatusLabel.Text = "VOLTAGE: SAFE ZONE";
				voltageStatusLabel.AddThemeColorOverride("font_color", Green);
			}
			else if (breakerSwitchesOn[1])
			{
				voltageStatusLabel.Text = $"VOLTAGE: STABILIZING // {stabilizationSecondsLeft}s";
				voltageStatusLabel.AddThemeColorOverride("font_color", Amber);
			}
			else
			{
				voltageStatusLabel.Text = "VOLTAGE: STANDBY";
				voltageStatusLabel.AddThemeColorOverride("font_color", Muted);
			}
		}

		if (masterPowerButton != null)
		{
			masterPowerButton.Disabled = masterFuseTripped || breakerControlsLocked;
		}
		if (breakerMessageLabel != null)
		{
			breakerMessageLabel.AddThemeColorOverride("font_color", breakerMessageColor);
		}
		if (fuseStatusLabel != null)
		{
			fuseStatusLabel.Text = masterFuseTripped ? "TRIPPED" : "INTACT";
			fuseStatusLabel.AddThemeColorOverride("font_color", masterFuseTripped ? Red : Green);
		}
		if (resetFuseButton != null)
		{
			resetFuseButton.Disabled = !masterFuseTripped;
		}
	}

	private Color breakerMessageColor = new Color("#91A2B2");

	private void SetBreakerMessage(string message, Color color)
	{
		breakerMessageColor = color;
		if (breakerMessageLabel != null)
		{
			breakerMessageLabel.Text = message;
			breakerMessageLabel.AddThemeColorOverride("font_color", color);
		}
	}

	private void SelectEquipment(int index)
	{
		selectedEquipment = index;
		RefreshSystemPanel();
	}

	private async void RestartSelectedEquipment()
	{
		if (selectedEquipment < 0 || selectedEquipment >= equipmentNames.Length)
		{
			return;
		}

		if (!needsAttention[selectedEquipment] || equipmentRestarting[selectedEquipment] || restartingAll)
		{
			return;
		}

		int index = selectedEquipment;
		equipmentRestarting[index] = true;
		equipmentStatuses[index] = "RESTARTING";
		LogEvent($"Restart started: {equipmentNames[index]}.");
		RefreshSystemPanel();
		await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);

		needsAttention[index] = false;
		equipmentRestarting[index] = false;
		equipmentStatuses[index] = "ONLINE";
		LogEvent($"Recovery successful: {equipmentNames[index]} online.");
		RefreshSystemPanel();
	}

	private async void RestartAllEquipment()
	{
		if (restartingAll)
		{
			return;
		}

		List<int> pending = new List<int>();
		for (int i = 0; i < needsAttention.Length; i++)
		{
			if (needsAttention[i])
			{
				pending.Add(i);
			}
		}

		if (pending.Count == 0)
		{
			LogEvent("No equipment requires a restart.");
			RefreshSystemPanel();
			return;
		}

		if (MainPowerInterrupted)
		{
			if (generatorReserveSeconds < RestartAllReserveCostSeconds)
			{
				LogEvent("Insufficient generator reserve for Restart All.");
				return;
			}

			generatorReserveSeconds = Mathf.Max(0.0f, generatorReserveSeconds - RestartAllReserveCostSeconds);
			UpdateGeneratorReadout();
			LogEvent("Generator reserve used for Restart All during an interruption.");
		}

		restartingAll = true;
		foreach (int index in pending)
		{
			equipmentRestarting[index] = true;
			equipmentStatuses[index] = "RESTARTING";
		}

		LogEvent("Restarting all equipment requiring attention.");
		RefreshSystemPanel();
		await ToSignal(GetTree().CreateTimer(1.1), SceneTreeTimer.SignalName.Timeout);

		foreach (int index in pending)
		{
			needsAttention[index] = false;
			equipmentRestarting[index] = false;
			equipmentStatuses[index] = "ONLINE";
		}

		restartingAll = false;
		LogEvent("All selected equipment is operational.");
		RefreshSystemPanel();
	}

	private void RefreshSystemPanel()
	{
		if (equipmentButtons == null || equipmentButtons.Length == 0)
		{
			return;
		}

		for (int i = 0; i < equipmentButtons.Length; i++)
		{
			Button row = equipmentButtons[i];
			row.Text = $"{equipmentNames[i]}     [{equipmentStatuses[i]}]";
			Color statusColor = equipmentRestarting[i]
				? Amber
				: equipmentStatuses[i] == "ONLINE"
					? Green
					: equipmentStatuses[i] == "FAULT"
						? Red
						: Amber;
			row.AddThemeColorOverride("font_color", statusColor);
			row.AddThemeColorOverride("font_hover_color", Colors.White);
			row.AddThemeColorOverride("font_pressed_color", Colors.White);
			row.Disabled = restartingAll || equipmentRestarting[i];
			row.AddThemeStyleboxOverride(
				"normal",
				MakeStylebox(i == selectedEquipment ? new Color("#10262A") : new Color("#121922"), i == selectedEquipment ? Cyan : new Color("#24313C"), 1)
			);
		}

		if (restartSelectedButton != null)
		{
			restartSelectedButton.Disabled = restartingAll || !needsAttention[selectedEquipment] || equipmentRestarting[selectedEquipment];
		}

		if (restartAllButton != null)
		{
			bool anyAttention = false;
			foreach (bool attention in needsAttention)
			{
				anyAttention |= attention;
			}
			restartAllButton.Disabled = restartingAll || !anyAttention;
		}

		if (eventLogLabel != null)
		{
			eventLogLabel.Text = string.Join("\n", eventLog);
		}
	}

	private void UpdateGeneratorReadout()
	{
		if (generatorPercentLabel == null)
		{
			return;
		}

		int percentage = Mathf.RoundToInt(generatorReserveSeconds / GeneratorMaxSeconds * 100.0f);
		generatorPercentLabel.Text = $"{percentage}%";
		generatorPercentLabel.AddThemeColorOverride(
			"font_color",
			percentage <= 20 ? Red : percentage <= 50 ? Amber : Green
		);
	}

	private void LogEvent(string message)
	{
		eventLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
		while (eventLog.Count > 7)
		{
			eventLog.RemoveAt(eventLog.Count - 1);
		}

		if (eventLogLabel != null)
		{
			eventLogLabel.Text = string.Join("\n", eventLog);
		}
	}

	private void UpdateLayout()
	{
		if (background == null || interfaceLayer == null)
		{
			return;
		}

		Vector2 screen = GetViewportRect().Size;
		float floorY = Mathf.Max(HeaderHeight + 180.0f, screen.Y - FloorHeight);
		SetFullRect(background);
		SetFullRect(roomLayer);
		SetFullRect(interfaceLayer);
		SetRect(headerRect, Vector2.Zero, new Vector2(screen.X, HeaderHeight));

		brandLabel.Position = new Vector2(22, 8);
		brandLabel.Size = new Vector2(300, 22);
		subbrandLabel.Position = new Vector2(22, 31);
		subbrandLabel.Size = new Vector2(310, 18);
		roomLabel.Position = new Vector2(0, HeaderHeight + 15);
		roomLabel.Size = new Vector2(screen.X, 26);
		roomLabel.HorizontalAlignment = HorizontalAlignment.Center;

		if (breakerTexture != null)
		{
			breakerTexture.Size = new Vector2(100, 180);
			breakerTexture.Position = new Vector2(Mathf.Max(12, screen.X - 132), Mathf.Max(HeaderHeight + 90, floorY - 245));
			breakerButton.Position = breakerTexture.Position;
			breakerButton.Size = breakerTexture.Size;
		}

		if (generatorTexture != null)
		{
			generatorTexture.Size = new Vector2(120, 85);
			generatorTexture.Position = new Vector2(22, floorY - 98);
			generatorLabel.Position = generatorTexture.Position + new Vector2(0, 17);
			generatorLabel.Size = new Vector2(generatorTexture.Size.X, 48);
			generatorPercentLabel.Position = generatorTexture.Position + new Vector2(0, -28);
			generatorPercentLabel.Size = new Vector2(generatorTexture.Size.X, 22);
		}

		if (techManualTexture != null)
		{
			techManualTexture.Size = new Vector2(166, 34);
			techManualTexture.Position = new Vector2(Mathf.Max(150, screen.X - 220), floorY - 70);
			manualButton.Position = techManualTexture.Position;
			manualButton.Size = techManualTexture.Size;
		}

		float buttonWidth = Mathf.Min(280, screen.X - 24);
		float panelWidth = Mathf.Min(700, screen.X - 28);
		float panelTop = screen.Y - SystemPanelHeight;
		float buttonY = panelOpen ? panelTop - 42 : screen.Y - 44;
		panelToggleButton.Position = new Vector2((screen.X - buttonWidth) / 2.0f, buttonY);
		panelToggleButton.Size = new Vector2(buttonWidth, 36);
		systemPanel.Position = new Vector2((screen.X - panelWidth) / 2.0f, panelTop);
		systemPanel.Size = new Vector2(panelWidth, SystemPanelHeight);
		LayoutSystemPanel(panelWidth);

		LayoutManualPanel(screen);
		LayoutBreakerPanel(screen);
	}

	private void LayoutSystemPanel(float panelWidth)
	{
		if (equipmentPanel == null || eventPanel == null)
		{
			return;
		}

		panelMeta.Position = new Vector2(panelWidth - 190, 14);
		panelMeta.Size = new Vector2(170, 18);

		float innerWidth = panelWidth - 36;
		float columnGap = 12;
		float leftWidth = Mathf.Max(230, innerWidth * 0.47f);
		float rightWidth = Mathf.Max(180, innerWidth - leftWidth - columnGap);
		float columnTop = 45;
		float columnHeight = 176;

		equipmentPanel.Position = new Vector2(18, columnTop);
		equipmentPanel.Size = new Vector2(leftWidth, columnHeight);
		eventPanel.Position = new Vector2(18 + leftWidth + columnGap, columnTop);
		eventPanel.Size = new Vector2(rightWidth, columnHeight);

		for (int i = 0; i < equipmentButtons.Length; i++)
		{
			equipmentButtons[i].Position = new Vector2(9, 29 + i * 34);
			equipmentButtons[i].Size = new Vector2(leftWidth - 18, 30);
		}

		restartSelectedButton.Position = new Vector2(9, 137);
		restartSelectedButton.Size = new Vector2((leftWidth - 24) / 2.0f, 28);
		restartAllButton.Position = new Vector2(15 + (leftWidth - 24) / 2.0f, 137);
		restartAllButton.Size = new Vector2((leftWidth - 24) / 2.0f, 28);
		eventLogLabel.Position = new Vector2(10, 29);
		eventLogLabel.Size = new Vector2(rightWidth - 20, 136);
	}

	private void LayoutManualPanel(Vector2 screen)
	{
		if (manualCard == null)
		{
			return;
		}

		float width = Mathf.Min(650, screen.X - 40);
		float height = Mathf.Min(610, screen.Y - 48);
		manualCard.Size = new Vector2(width, height);
		manualCard.Position = new Vector2((screen.X - width) / 2.0f, (screen.Y - height) / 2.0f);
		manualCloseButton.Position = new Vector2(width - 54, 12);
		manualCloseButton.Size = new Vector2(38, 36);
		manualScroll.Position = new Vector2(16, 64);
		manualScroll.Size = new Vector2(width - 32, height - 80);
	}

	private void LayoutBreakerPanel(Vector2 screen)
	{
		if (breakerCard == null)
		{
			return;
		}

		float width = Mathf.Min(640, screen.X - 40);
		float height = Mathf.Min(500, screen.Y - 48);
		breakerCard.Size = new Vector2(width, height);
		breakerCard.Position = new Vector2((screen.X - width) / 2.0f, (screen.Y - height) / 2.0f);
		breakerCloseButton.Position = new Vector2(width - 54, 10);
		breakerCloseButton.Size = new Vector2(38, 36);
		panelATabButton.Position = new Vector2(18, 58);
		panelATabButton.Size = new Vector2((width - 48) / 2.0f, 34);
		fuseTabButton.Position = new Vector2(30 + (width - 48) / 2.0f, 58);
		fuseTabButton.Size = new Vector2((width - 48) / 2.0f, 34);

		float contentWidth = width - 36;
		float contentHeight = height - 116;
		sequenceLayer.Position = new Vector2(18, 104);
		sequenceLayer.Size = new Vector2(contentWidth, contentHeight);
		fuseLayer.Position = new Vector2(18, 104);
		fuseLayer.Size = new Vector2(contentWidth, contentHeight);

		voltageStatusLabel.Position = new Vector2(0, 0);
		voltageStatusLabel.Size = new Vector2(contentWidth, 24);
		float gap = 12;
		float switchWidth = (contentWidth - gap) / 2.0f;
		float switchHeight = Mathf.Min(70, Mathf.Max(56, contentHeight * 0.22f));
		for (int i = 0; i < breakerSwitchButtons.Length; i++)
		{
			int column = i % 2;
			int row = i / 2;
			breakerSwitchButtons[i].Position = new Vector2(column * (switchWidth + gap), 30 + row * (switchHeight + 10));
			breakerSwitchButtons[i].Size = new Vector2(switchWidth, switchHeight);
		}

		float masterY = 30 + 2 * (switchHeight + 10) + 2;
		masterPowerButton.Position = new Vector2(0, masterY);
		masterPowerButton.Size = new Vector2(contentWidth, 42);
		breakerMessageLabel.Position = new Vector2(0, masterY + 50);
		breakerMessageLabel.Size = new Vector2(contentWidth, Mathf.Max(38, contentHeight - masterY - 54));

		Control fuseCardControl = fuseLayer.GetNode<Control>("MasterFuseCard");
		fuseCardControl.Position = new Vector2(0, 8);
		fuseCardControl.Size = new Vector2(contentWidth, Mathf.Min(190, contentHeight - 68));
		fuseCardControl.GetNode<Control>("MasterFuse").Position = new Vector2(16, 14);
		fuseCardControl.GetNode<Control>("MasterFuse").Size = new Vector2(220, 22);
		fuseCardControl.GetNode<Control>("FuseStatus").Position = new Vector2(contentWidth - 142, 14);
		fuseCardControl.GetNode<Control>("FuseStatus").Size = new Vector2(120, 22);
		fuseCardControl.GetNode<Control>("FuseDescription").Position = new Vector2(16, 54);
		fuseCardControl.GetNode<Control>("FuseDescription").Size = new Vector2(contentWidth - 32, 62);
		resetFuseButton.Position = new Vector2(0, Mathf.Min(contentHeight - 48, 222));
		resetFuseButton.Size = new Vector2(contentWidth, 40);
	}

	private Control MakeOverlay(string nodeName, int zIndex, Color shadeColor)
	{
		Control overlay = new Control();
		overlay.Name = nodeName;
		overlay.ZIndex = zIndex;
		overlay.MouseFilter = MouseFilterEnum.Stop;
		SetFullRect(overlay);
		interfaceLayer.AddChild(overlay);

		ColorRect shade = new ColorRect();
		shade.Name = "Shade";
		shade.Color = shadeColor;
		shade.MouseFilter = MouseFilterEnum.Stop;
		SetFullRect(shade);
		overlay.AddChild(shade);
		return overlay;
	}

	private Panel MakePanel(string nodeName, Color backgroundColor, Color borderColor, int zIndex)
	{
		Panel panel = new Panel();
		panel.Name = nodeName;
		panel.ZIndex = zIndex;
		ApplyPanelStyle(panel, backgroundColor, borderColor);
		return panel;
	}

	private ColorRect MakeColorRect(Control parent, string nodeName, Color color, int zIndex)
	{
		ColorRect rect = new ColorRect();
		rect.Name = nodeName;
		rect.Color = color;
		rect.MouseFilter = MouseFilterEnum.Ignore;
		rect.ZIndex = zIndex;
		parent.AddChild(rect);
		return rect;
	}

	private Label MakeLabel(Control parent, string text, Color color, int fontSize, int zIndex)
	{
		Label label = MakePanelLabel(text, color, fontSize, zIndex);
		parent.AddChild(label);
		return label;
	}

	private Label MakePanelLabel(string text, Color color, int fontSize, int zIndex)
	{
		Label label = new Label();
		label.Text = text;
		label.MouseFilter = MouseFilterEnum.Ignore;
		label.ZIndex = zIndex;
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private Button MakeButton(Control parent, string text, Color fontColor, int fontSize, int zIndex)
	{
		Button button = new Button();
		button.Text = text;
		button.ZIndex = zIndex;
		button.AddThemeFontSizeOverride("font_size", fontSize);
		button.AddThemeColorOverride("font_color", fontColor);
		button.AddThemeColorOverride("font_hover_color", Colors.White);
		button.AddThemeColorOverride("font_pressed_color", Colors.White);
		parent.AddChild(button);
		return button;
	}

	private void ApplyButtonStyle(Button button, Color normal, Color border, Color hover)
	{
		button.AddThemeStyleboxOverride("normal", MakeStylebox(normal, border, 1));
		button.AddThemeStyleboxOverride("hover", MakeStylebox(hover, border, 1));
		button.AddThemeStyleboxOverride("pressed", MakeStylebox(hover, Cyan, 1));
	}

	private void ApplyPanelStyle(Panel panel, Color backgroundColor, Color borderColor)
	{
		panel.AddThemeStyleboxOverride("panel", MakeStylebox(backgroundColor, borderColor, 1));
	}

	private StyleBoxFlat MakeStylebox(Color backgroundColor, Color borderColor, int borderWidth)
	{
		StyleBoxFlat style = new StyleBoxFlat();
		style.BgColor = backgroundColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(borderWidth);
		style.SetCornerRadiusAll(3);
		return style;
	}

	private void SetFullRect(Control control)
	{
		control.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		control.OffsetLeft = 0;
		control.OffsetTop = 0;
		control.OffsetRight = 0;
		control.OffsetBottom = 0;
	}

	private void SetRect(Control control, Vector2 position, Vector2 size)
	{
		if (control == null)
		{
			return;
		}

		control.Position = position;
		control.Size = size;
	}
}
