using Godot;
using RascalRadio.Core.Models;
using System.Linq;
using RascalRadio.Modules;

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
    private Control _callerPanel;
    private Control _phone;
    private Tween _buzzTween;
    private string _lastCallerKey = "";
    private ModuleSlot _openSlot;
    private Button _flipBtn;

    public override void _EnterTree() => GD.Print($"[Operator] _EnterTree peer={Multiplayer.GetUniqueId()}");
    public override void _Ready()
    {
        var gs = GameSession.Instance;

        // FIRST, so nothing below can stop the narrator from appearing
        if (gs != null && gs.IsTutorial)
            AddChild(new TutorialOverlay { RoleName = "Operator" });

        if (gs == null) return;

        // module popup
        BuildUi();

        _callerPanel = FindChild("CallerPanel", true, false) as Control;
        _phone = FindChild("Phone", true, false) as Control;
        if (_callerPanel != null) _callerPanel.Visible = false;

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
        if (lobbyBtn != null) lobbyBtn.Pressed += () => GameSession.Instance.ReturnToParty();
        if (_pauseMenu != null) _pauseMenu.Visible = false;

        var timeLabel = FindChild("TimeLabel", true, false) as Label;
        if (timeLabel != null)
        {
            var clock = new Timer { WaitTime = 0.5, Autostart = true };
            clock.Timeout += () =>
            {
                if (IsInstanceValid(timeLabel) && GameSession.Instance != null)
                    timeLabel.Text = GameSession.Instance.ClockText();
            };
            AddChild(clock);
        }

        Subscribe();
        //gs.StepChanged -= OnStep;
        //gs.CallerChanged -= OnCaller;
        //gs.QuotaChanged -= OnQuota;
        //gs.KeypadChanged -= OnKeypadChanged;
        //gs.TasksChanged -= OnTasksChanged;   
        //gs.FlipChanged -= ApplyFlip;         
        //gs.TopicChanged -= RefreshTasks;

        GD.Print($"[Operator] after subscribe: gs={gs.GetInstanceId()} step={gs.StepSubs} caller={gs.CallerSubs}");
        GetTree().CreateTimer(1.0).Timeout += () =>
            GD.Print($"[Operator] +1s: gs={GameSession.Instance.GetInstanceId()} step={GameSession.Instance.StepSubs} caller={GameSession.Instance.CallerSubs}");

        SetText("%StrikeCount", "none");
        SetupSlots();
        OnQuota();
        OnCaller();
        if (gs.IsTutorial) OnStep(gs.CurrentStep);
        RefreshTasks();
        Subscribe();
    }

    private void Subscribe()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;

        // remove first so calling this twice never double-subscribes
        gs.StepChanged -= OnStep; gs.StepChanged += OnStep;
        gs.CallerChanged -= OnCaller; gs.CallerChanged += OnCaller;
        gs.QuotaChanged -= OnQuota; gs.QuotaChanged += OnQuota;
        gs.KeypadChanged -= OnKeypadChanged; gs.KeypadChanged += OnKeypadChanged;
        gs.TasksChanged -= OnTasksChanged; gs.TasksChanged += OnTasksChanged;
        gs.FlipChanged -= ApplyFlip; gs.FlipChanged += ApplyFlip;
        gs.TopicChanged -= RefreshTasks; gs.TopicChanged += RefreshTasks;

        GD.Print($"[Operator] Subscribe(): step={gs.StepSubs} caller={gs.CallerSubs}");
    }

    private void BuildUi()
    {
        // ---- popup (built in code, drawn on top) ----
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);

        _popup = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        layer.AddChild(_popup);
        _popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _popup.GuiInput += OnPopupInput;

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = MouseFilterEnum.Ignore };
        _popup.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(280, 0), MouseFilter = MouseFilterEnum.Stop };
        var style = new StyleBoxFlat { BgColor = new Color("#7A3036"), BorderColor = new Color("#301E2E") };
        style.SetBorderWidthAll(4);
        style.BorderWidthBottom = 7;
        style.SetCornerRadiusAll(10);
        _panel.AddThemeStyleboxOverride("panel", style);
        _popup.AddChild(_panel);
        _panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.Minsize);
        _panel.GrowHorizontal = GrowDirection.Both;
        _panel.GrowVertical = GrowDirection.Both;

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 16);
        _panel.AddChild(margin);

        _holder = new VBoxContainer();
        margin.AddChild(_holder);

        _flipBtn = new Button { Text = "FLIP BACK", Visible = false, CustomMinimumSize = new Vector2(0, 40) };
        UiStyle.StyleButton(_flipBtn, UiStyle.Yellow);
        _flipBtn.Pressed += () => GameSession.Instance.RequestUnflip();
        _holder.AddChild(_flipBtn);

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
        GD.Print($"[Operator] _ExitTree peer={Multiplayer.GetUniqueId()}");
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
        if (!gs.IsTutorial) { _taskLabel.Text = gs.TaskBbcode(); return; }
        var items = new System.Collections.Generic.List<(string text, bool done)>();

        if (gs.IsTutorial)
        {
            var s = gs.CurrentStep;
            items.Add(("Meet the studio", s > TutorialStep.MeetStudio));
            items.Add(($"Handle all {gs.CallerTotal} callers: accept 2, reject the suspicious one", s > TutorialStep.SuspiciousCaller));
            items.Add(("Fix the keypad", s > TutorialStep.KeypadModule || (_modules.Count > 0 && _modules[0].Solved)));
        }
        else
        {
            // EDIT PER LEVEL
            items.Add(($"Accept {gs.QuotaTarget} callers ({gs.QuotaMet}/{gs.QuotaTarget})", gs.QuotaMet >= gs.QuotaTarget));
            var keypadSlot = FindSlot("Keypad");
            if (keypadSlot != null) items.Add(("Fix the keypad", keypadSlot.Solved));
            if (FindSlot("Piano") != null) items.Add(("Play the jingles with the Technician", false));
        }

        _taskLabel.Text = string.Join("\n", items.Select(t => t.done
            ? $"[color=#7A7A7A][s]- {t.text}[/s][/color]"
            : $"- {t.text}"));
    }

    private void OnPopupInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (!_panel.GetRect().HasPoint(mb.Position))
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
        GD.Print("[Operator] OnCaller '" + GameSession.Instance.CallerName + "'");
        var gs = GameSession.Instance;
        bool hasCaller = !string.IsNullOrEmpty(gs.CallerName);

        if (_callerPanel != null) _callerPanel.Visible = hasCaller;

        if (!hasCaller) { _lastCallerKey = ""; return; }

        SetCaller(gs.CallerName, gs.CallerNumber, gs.CallerTotal, gs.CallerStory);

        // buzz the phone once per new caller
        string key = $"{gs.CallerName}#{gs.CallerNumber}";
        if (key != _lastCallerKey)
        {
            _lastCallerKey = key;
            BuzzPhone();
        }
    }

    private void BuzzPhone()
    {
        if (_phone == null) return;
        _buzzTween?.Kill();
        _phone.PivotOffset = _phone.Size / 2;
        _phone.Rotation = 0;

        _buzzTween = CreateTween();
        for (int i = 0; i < 12; i++)
        {
            float angle = (i % 2 == 0 ? 1f : -1f) * Mathf.DegToRad(4f);
            _buzzTween.TweenProperty(_phone, "rotation", angle, 0.05);
        }
        _buzzTween.TweenProperty(_phone, "rotation", 0.0, 0.05);
    }

    private void OnQuota()
    {
        SetText("%QuotaCount", $"{GameSession.Instance.QuotaMet}/{GameSession.Instance.QuotaTarget}");
        RefreshTasks();
    }

    private ModuleSlot FindSlot(string title) => _modules.FirstOrDefault(m => m.Title == title);

    private void SetupSlots()
    {
        var gs = GameSession.Instance;
        _modules.Clear();

        // Register modules. Add more here later (wires, dials, etc.)
        if (gs.IsTutorial || gs.HasKeypadProblem || gs.CurrentLevel?.HasKeypad == true)
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

        if (!gs.IsTutorial && gs.CurrentLevel?.HasPiano == true)
        {
            _modules.Add(new ModuleSlot
            {
                Title = "Piano",
                Build = () =>
                {
                    var ui = new StudioPianoUI();
                    ui.Bind(gs.Piano);
                    ui.KeyPressed += gs.PianoKey;
                    ui.OctaveStepPressed += gs.PianoOctave;
                    return ui;
                }
            });
        }

        if (!gs.IsTutorial && gs.CurrentLevel?.HasSoundboard == true)
        {
            _modules.Add(new ModuleSlot
            {
                Title = "Soundboard",
                Build = () =>
                {
                    var ui = new SoundboardUI();
                    ui.Bind(gs.Soundboard);
                    ui.PadPressed += gs.SoundboardPad;
                    ui.ResetSequencePressed += gs.SoundboardReset;
                    return ui;
                }
            });
        }

        if (!gs.IsTutorial && gs.CurrentLevel?.HasMemory == true)
        {
            _modules.Add(new ModuleSlot
            {
                Title = "Memory",
                Build = () =>
                {
                    var memory = new MemoryModule { PenaltiesEnabled = true, UseOwnStrikes = false };
                    memory.Strike += _ => gs.MemoryStrike();
                    memory.Solved += gs.MemorySolved;
                    return memory;
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

      
        OnTasksChanged();
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
            if (c is Control ctrl && ctrl != _flipBtn) ctrl.Visible = (ctrl == module.Instance);

        _openSlot = module;
        _openModule = module.Instance;
        _popup.Visible = true;
        CallDeferred(nameof(ApplyFlip));

        _openModule = module.Instance;
        _popup.Visible = true;
    }

    private void CloseModule()
    {
        _popup.Visible = false;
        _openModule = null;
        _openSlot = null;
        ApplyFlip();
    }

    private void OnModuleSolved(ModuleSlot module)
    {
        module.Solved = true;
        UpdateSlotButtons();
        RefreshTasks();
        CloseModule();
    }

    private void UpdateSlotButtons()
    {
        var gs = GameSession.Instance;
        for (int i = 0; i < _modules.Count; i++)
        {
            var btn = GetNodeOrNull<Button>($"%Slot{i + 1}Btn");
            if (btn == null) continue;
            var m = _modules[i];
            bool flipped = !m.Solved && gs.FlippedModule == m.Title;
            btn.Text = m.Solved ? $"{m.Title} ✔" : flipped ? $"{m.Title} !!" : $"{m.Title} !";
            btn.Modulate = m.Solved ? new Color(0.7f, 1f, 0.7f) : flipped ? new Color(1f, 0.7f, 0.7f) : Colors.White;
            btn.Disabled = m.Solved;
        }
    }

    private void OnStep(TutorialStep step)
    {
        GD.Print("[Operator] OnStep " + step);
        // Tutorial: keypad button only becomes usable at the keypad step
        var btn = GetNodeOrNull<Button>("%Slot1Btn");
        if (btn != null) btn.Disabled = step < TutorialStep.KeypadModule;

        if (step > TutorialStep.KeypadModule) _keypad?.SetSolved();
        RefreshTasks();
    }

    private void OnKeypadChanged()
    {
        var gs = GameSession.Instance;
        if (!gs.IsTutorial && gs.HasKeypadProblem && FindSlot("Keypad") == null) SetupSlots();

        _keypad?.Refresh(gs.KeypadLine, gs.KeypadStatic);

        var keypadSlot = FindSlot("Keypad");
        if (keypadSlot != null && gs.IsTutorial) { keypadSlot.Solved = false; UpdateSlotButtons(); }
    }

    private void OnTasksChanged()
    {
        var gs = GameSession.Instance;
        if (!gs.IsTutorial)
        {
            foreach (var m in _modules)
                m.Solved = gs.IsDone(KindOf(m.Title));
            UpdateSlotButtons();
            if (_openSlot != null && _openSlot.Solved) CloseModule();   // module finished: it closes and its button turns green
        }
        RefreshTasks();
    }

    private void ApplyFlip()
    {
        if (_panel == null) return;
        var gs = GameSession.Instance;
        bool flipped = _openSlot != null && gs.FlippedModule != "" && gs.FlippedModule == _openSlot.Title;
        _panel.PivotOffset = _panel.Size / 2;
        _panel.Rotation = flipped ? Mathf.Pi : 0f;
        if (_flipBtn != null) _flipBtn.Visible = flipped;
        UpdateSlotButtons();
    }

    private static TaskKind KindOf(string title) => title switch
    {
        "Keypad" => TaskKind.Keypad,
        "Piano" => TaskKind.Piano,
        "Soundboard" => TaskKind.Soundboard,
        _ => TaskKind.Memory,
    };

    private void SetText(string path, string text)
	{
		var l = GetNodeOrNull<Label>(path);
		if (l != null) l.Text = text;
	}
}
