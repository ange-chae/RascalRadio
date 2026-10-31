using Godot;
using RascalRadio.Core.Models;
using System.Linq;

public partial class OperatorInterface : Control
{
    [Export] public string LobbyScenePath = "res://scenes/lobby.tscn";   // same path as the Broadcaster
    private KeypadModule _keypad;
    private Control _pauseMenu;
    private RichTextLabel _taskLabel;
    private bool _menuOpen;
    private readonly System.Collections.Generic.List<ModuleSlot> _modules = new();
    private Control _openModule;      // keep, now just "currently shown module"
    private PanelContainer _panel;
    private Control _popup;
    private VBoxContainer _holder;
    private readonly System.Collections.Generic.List<Button> _slotBtns = new();

    public override void _Ready()
    {
        var gs = GameSession.Instance;

        // FIRST, so nothing below can stop the narrator from appearing
        if (gs != null && gs.IsTutorial)
            AddChild(new TutorialOverlay { RoleName = "Operator" });

        if (gs == null) return;

        // module popup
        BuildUi();

        // task + menu (same as Broadcaster)
        _taskLabel = GetNodeOrNull<RichTextLabel>("%TaskLabel");
        if (_taskLabel != null)
        {
            _taskLabel.BbcodeEnabled = true;
            _taskLabel.FitContent = true;
            _taskLabel.ScrollActive = false;
        }
        _pauseMenu = GetNodeOrNull<Control>("%PauseMenu");
        var menuBtn = GetNodeOrNull<Button>("%MenuBtn");
        var resumeBtn = GetNodeOrNull<Button>("%ResumeBtn");
        var lobbyBtn = GetNodeOrNull<Button>("%LobbyBtn");
        if (menuBtn != null) menuBtn.Pressed += () => SetMenu(true);
        if (resumeBtn != null) resumeBtn.Pressed += () => SetMenu(false);
        if (lobbyBtn != null) lobbyBtn.Pressed += () => GetTree().ChangeSceneToFile(LobbyScenePath);
        if (_pauseMenu != null) _pauseMenu.Visible = false;

        gs.StepChanged += OnStep;
        gs.CallerChanged += OnCaller;
        gs.QuotaChanged += OnQuota;
        gs.KeypadChanged += OnKeypadChanged;

        SetText("%StrikeCount", "none");
        SetupSlots();
        OnQuota();
        OnCaller();
        if (gs.IsTutorial) OnStep(gs.CurrentStep);
        RefreshTasks();
    }

    private void BuildUi()
    {
        // ---- popup (built in code, drawn on top) ----
        _popup = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _popup.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_popup);
        _popup.GuiInput += OnPopupInput;

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        _popup.AddChild(dim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        _popup.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 380), MouseFilter = MouseFilterEnum.Stop };
        var style = new StyleBoxFlat { BgColor = new Color("#EEEED0"), BorderColor = new Color("#2B1B5A") };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(8);
        _panel.AddThemeStyleboxOverride("panel", style);
        center.AddChild(_panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 16);
        _panel.AddChild(margin);

        _holder = new VBoxContainer();
        margin.AddChild(_holder);

        // ---- slot buttons already in the scene ----
        for (int i = 1; i <= 4; i++)
        {
            var btn = GetNodeOrNull<Button>($"%Slot{i}Btn");
            if (btn == null) { GD.PushError($"OperatorInterface: %Slot{i}Btn not found."); continue; }

            btn.Text = "Empty Slot";
            btn.Disabled = true;
            UiStyle.StyleButton(btn, UiStyle.Cream);
            _slotBtns.Add(btn);
        }
        GD.Print($"[Operator] found {_slotBtns.Count} slot buttons");
    }

    public override void _ExitTree()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;
        gs.StepChanged -= OnStep;
        gs.CallerChanged -= OnCaller;
        gs.QuotaChanged -= OnQuota;
        gs.KeypadChanged -= OnKeypadChanged;
    }

    // ===== menu =====
    private void SetMenu(bool open)
    {
        if (_pauseMenu == null) return;
        _menuOpen = open;
        _pauseMenu.Visible = open;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel")) return;
        if (_popup != null && _popup.Visible) { CloseModule(); return; }   // Esc closes a module first
        SetMenu(!_menuOpen);
    }

    // ===== tasks =====
    private void RefreshTasks()
    {
        if (_taskLabel == null) return;
        var gs = GameSession.Instance;
        var items = new System.Collections.Generic.List<(string text, bool done)>();

        if (gs.IsTutorial)
        {
            var s = gs.CurrentStep;
            items.Add(("Meet the studio", s > TutorialStep.MeetStudio));
            items.Add(($"Handle all {gs.CallerTotal} callers: accept 2, reject the suspicious one", s > TutorialStep.SuspiciousCaller));
            items.Add(("Fix the keypad", s > TutorialStep.KeypadModule));
        }
        else
        {
            // EDIT PER LEVEL
            items.Add(($"Accept {gs.QuotaTarget} callers ({gs.QuotaMet}/{gs.QuotaTarget})", gs.QuotaMet >= gs.QuotaTarget));
            if (_modules.Count > 0)
                items.Add(("Fix the keypad", _modules[0].Solved));
        }

        _taskLabel.Text = string.Join("\n", items.Select(t => t.done
            ? $"[color=#7A7A7A][s]- {t.text}[/s][/color]"
            : $"- {t.text}"));
    }

    private void OnPopupInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (!_panel.GetGlobalRect().HasPoint(mb.GlobalPosition))
                CloseModule();
        }
    }

    public void SetCaller(string name, int number, int total, string quote)
	{
		GetNode<Label>("%NameLabel").Text = name;
		GetNode<Label>("%CallerNumLabel").Text = $"Caller {number} of {total}";
		GetNode<Label>("%QuoteLabel").Text = quote;
	}

	public void SetVerdict(bool accept)
	{
		GetNode<Label>("%VerdictLabel").Text = accept ? "ACCEPT" : "REJECT";
	}

	private void OnCaller()
	{
		var gs = GameSession.Instance;
		if (gs.CallerName == "") return;
		// The OPERATOR sees the story and reads it out loud.
		SetCaller(gs.CallerName, gs.CallerNumber, gs.CallerTotal, gs.CallerStory);
	}

    private void OnQuota()
    {
        SetText("%QuotaCount", $"{GameSession.Instance.QuotaMet}/{GameSession.Instance.QuotaTarget}");
        RefreshTasks();
    }

    private void SetupSlots()
    {
        var gs = GameSession.Instance;
        _modules.Clear();

        // Register modules. Add more here later (wires, dials, etc.)
        if (gs.IsTutorial || gs.HasKeypadProblem)
        {
            _modules.Add(new ModuleSlot
            {
                Title = "Keypad",
                Build = () =>
                {
                    _keypad = new KeypadModule { Line = gs.KeypadLine, StaticFault = gs.KeypadStatic };
                    return _keypad;
                }
            });
        }

        for (int i = 0; i < 4; i++)
        {
            var btn = GetNodeOrNull<Button>($"%Slot{i + 1}Btn");
            if (btn == null) continue;

            foreach (var c in btn.GetSignalConnectionList(Button.SignalName.Pressed))
                btn.Disconnect(Button.SignalName.Pressed, (Callable)c["callable"]);

            if (i < _modules.Count)
            {
                var module = _modules[i];
                btn.Text = module.Title;
                btn.Disabled = false;
                btn.Pressed += () => OpenModule(module);
            }
            else
            {
                btn.Text = "Empty Slot";
                btn.Disabled = true;
            }
        }
    }

    private void OpenModule(ModuleSlot module)
    {
        if (module.Instance == null)
        {
            module.Instance = module.Build();
            _holder.AddChild(module.Instance);
            if (module.Instance is KeypadModule kp)
                kp.Solved += () => OnModuleSolved(module);
        }

        foreach (Node c in _holder.GetChildren())
            if (c is Control ctrl) ctrl.Visible = (ctrl == module.Instance);

        _openModule = module.Instance;
        _popup.Visible = true;
    }

    private void CloseModule()
    {
        _popup.Visible = false;
        _openModule = null;
    }

    private void OnModuleSolved(ModuleSlot module)
    {
        module.Solved = true;
        UpdateSlotButtons();
        CloseModule();
    }

    private void UpdateSlotButtons()
    {
        for (int i = 0; i < _modules.Count; i++)
        {
            var btn = GetNodeOrNull<Button>($"%Slot{i + 1}Btn");
            if (btn == null) continue;
            var m = _modules[i];
            btn.Text = m.Solved ? $"{m.Title} ✔" : $"{m.Title} !";
            btn.Modulate = m.Solved ? new Color(0.7f, 1f, 0.7f) : Colors.White;
        }
    }

    private void OnStep(TutorialStep step)
    {
        // Tutorial: keypad button only becomes usable at the keypad step
        var btn = GetNodeOrNull<Button>("%Slot1Btn");
        if (btn != null) btn.Disabled = step < TutorialStep.KeypadModule;

        if (step > TutorialStep.KeypadModule) _keypad?.SetSolved();
    }

    private void OnKeypadChanged()
    {
        var gs = GameSession.Instance;
        if (!gs.IsTutorial && gs.HasKeypadProblem && _modules.Count == 0) SetupSlots();

        _keypad?.Refresh(gs.KeypadLine, gs.KeypadStatic);

        // A fresh problem means the module needs attention again
        if (_modules.Count > 0) { _modules[0].Solved = false; UpdateSlotButtons(); }
    }

    private void SetText(string path, string text)
	{
		var l = GetNodeOrNull<Label>(path);
		if (l != null) l.Text = text;
	}
}
