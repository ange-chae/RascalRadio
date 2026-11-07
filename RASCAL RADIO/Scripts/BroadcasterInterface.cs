using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using static StudioPianoModule;

public partial class BroadcasterInterface : Control
{
    [Signal] public delegate void ClosedEventHandler();

    // =====================================================================
    // SETTINGS
    // =====================================================================
    [Export] public double SegmentTime = 12.0;                          // seconds to finish one floating line (real shift only)
    [Export] public string LobbyScenePath = "res://scenes/lobby.tscn";  // CHANGE to your real lobby scene path

    private const int AcceptGoal = 2;   // tutorial: callers to accept
    private const int MicGoal = 3;      // mic task goal (not shown in the task list yet)

    private static readonly bool DebugLog = false;   // set to true to print [Broadcaster] debug lines

    // key index: 0 = W / Up, 1 = A / Left, 2 = S / Down, 3 = D / Right
    private static readonly string[] Arrows = { "W", "A", "S", "D" };
    private static readonly char[] QwertyLetters = "QWERTYUIOPASDFGHJKLZXCVBNM".ToCharArray();
    private const double NormalColorPromptChance = 0.08;
    private const double PostBlackoutColorPromptChance = 0.75;
    private const ulong PostBlackoutBoostMs = 20000;

    // =====================================================================
    // NODES
    // =====================================================================
    // required (logged as an error if missing)
    private Control _wordArea;
    private RichTextLabel _target, _taskLabel;
    private Control _caller;

    // optional (skipped if they aren't in the scene)
    private Label _status;   // falls back to a hidden dummy label so the code never crashes
    private Label _nameLabel, _callerNumLabel, _deadAirLabel, _listenerLabel;
    private Label _topicLabel;
    private ProgressBar _listenerBar;
    private Control _airStatus, _pauseMenu;

    private static Font _wordFont;

    // =====================================================================
    // STATE
    // =====================================================================
    private bool _tutorial, _menuOpen;
    private bool _running;                 // a prompter line is live and accepting keys
    private bool _micLocked;               // tutorial: mic only runs on some steps
    private bool _callersAllowed = true;   // tutorial: only true during the caller steps
    private bool _calculatorDone;          // set to true when the calculator task is finished
    private int _micDone;
    private double _lineRest = 0.3, _paragraphRest = 0.3;

    // paragraph state
    private string[][] _paragraph = Array.Empty<string[]>();
    private string[] _words = Array.Empty<string>();
    private int[] _keys = Array.Empty<int>();
    private char[] _displayLetters = Array.Empty<char>();
    private bool _codedPromptActive;
    private bool _blackoutSequenceActive;
    private ulong _postBlackoutBoostUntilMsec;
    private int _segIdx;
    private int _idx;                      // words done in the current line
    private double _timeLeft;

    // ON AIR meter (not in the scene yet, see the bottom of the file)
    private int _typedTotal, _totalWords;
    private int _cueSig;
    private Tween _fade, _callerTween;
    private void PlaceStagePanel() { if (_stagePanel != null) PlaceDeskPanel(_stagePanel, false); }
    private readonly PhraseBank _phrases = new(new Random());
    private readonly DeadAirMonitor _deadAir = new();
    private readonly Random _rng = new();
    private Timer _interruptTimer;
    private bool _interruptionActive;
    private readonly List<Action> _interruptions = new();
    private readonly List<Panel> _blobs = new();
    private readonly Dictionary<Panel, float> _blobHp = new();
    private bool _spillActive;
    private int _spillId;
    private const double FeedbackSeconds = 4.0;
    private bool _feedbackActive;
    private int _feedbackClicks;
    private Control _feedbackOverlay;
    private Button _feedbackBtn;
    private PanelContainer _stagePanel;
    private PianoLamp _stageLamp;
    private Label _stageName, _stageHint;
    private ProgressBar _cueBar;
    private Label _feedbackTitle;
    private readonly List<Panel> _feedbackPips = new();
    private Tween _feedbackTween;
    private bool _prompterFlipped;
    private Button _flipBackBtn;
    private Tween _flipTween;
    private int _flipId;
    private Vector2 _shakeOrigin;
    private int _stolenIdx = -1;
    private ulong _stolenRevealMs;

    // =====================================================================
    // SETUP
    // =====================================================================
    public override void _Ready()
    {
        var gs = GameSession.Instance;
        _tutorial = gs != null && gs.IsTutorial;
        if (!_tutorial && gs?.CurrentLevel != null)
        {
            SegmentTime = gs.CurrentLevel.SegmentTime;
            _lineRest = gs.CurrentLevel.PrompterLineRest;
            _paragraphRest = gs.CurrentLevel.PrompterParagraphRest;
        }
        Log($"_Ready. tutorial={_tutorial}, session={gs != null}");

        // FIRST, so nothing below can stop the narrator from appearing
        if (_tutorial) AddChild(new TutorialOverlay { RoleName = "Broadcaster", ShowDoneWhen = false });

        FindNodes();
        WireButtons();

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

        // starting state
        if (_pauseMenu != null) _pauseMenu.Visible = false;
        if (_deadAirLabel != null) _deadAirLabel.Visible = false;
        if (_airStatus != null) _airStatus.Visible = !_tutorial;   // dead air only counts in a real shift
        if (_caller != null) _caller.Visible = false;

        if (gs != null)
        {
            gs.CallerChanged += OnCaller;
            gs.QuotaChanged += OnQuota;
            gs.TasksChanged += RefreshTasks;
            gs.TopicChanged += OnTopic;
            gs.PrompterColorMapChanged += OnPrompterColorMapChanged;
            gs.BlackoutSequenceChanged += OnBlackoutSequenceChanged;
            _blackoutSequenceActive = gs.BlackoutSequenceActive;
            if (_tutorial) gs.StepChanged += OnStep;
        }

        if (_tutorial)
        {
            _micLocked = !MicShouldRun(gs.CurrentStep);
            _callersAllowed = IsCallerStep(gs.CurrentStep);
        }

        OnTopic();
        OnCaller();
        OnQuota();
        if (_tutorial) BeginParagraph();
        else { _status.Text = "Get ready..."; FadeOutThen(BeginParagraph, 2.0); }
        GameSession.Instance?.ApplyBackground(GetNodeOrNull<TextureRect>("%Background"), "broadcaster");
        SetupInterruptions();
        SetupCueDesk();

        var bg = GetNodeOrNull<TextureRect>("%Background");
        GD.Print($"[BG] node={bg != null} tex={bg?.Texture?.ResourcePath} size={bg?.Size} visible={bg?.IsVisibleInTree()}");
    }

    public override void _ExitTree()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;
        gs.StepChanged -= OnStep;
        gs.CallerChanged -= OnCaller;
        gs.QuotaChanged -= OnQuota;
        gs.PrompterColorMapChanged -= OnPrompterColorMapChanged;
        gs.BlackoutSequenceChanged -= OnBlackoutSequenceChanged;
        gs.TasksChanged -= RefreshTasks;
        gs.TopicChanged -= OnTopic;
        gs.PianoColorMapChanged -= RefreshStageLight;
        if (gs.Piano != null)
        {
            gs.Piano.Configured -= RefreshStageLight;
            gs.Piano.CueChanged -= OnPianoCue;
            gs.Piano.StatusChanged -= OnPianoStatus;
            gs.Piano.FaultChanged -= OnPianoFault;
            gs.Piano.FaultProgressChanged -= OnPianoFaultProgress;
        }
    }

    private void FindNodes()
    {
        // ---- required ----
        _wordArea = Need<Control>("WordArea");
        _target = Need<RichTextLabel>("TargetLabel");
        _taskLabel = Need<RichTextLabel>("TaskLabel");
        _caller = GetNodeOrNull<Control>("%Caller") ?? FindChild("CallerPanel", true, false) as Control;
        if (_caller == null)
            GD.PushError("BroadcasterInterface: caller card not found (looked for %Caller and CallerPanel).");

        // ---- optional ----
        _status = GetNodeOrNull<Label>("%StatusLabel") ?? new Label();
        _nameLabel = GetNodeOrNull<Label>("%NameLabel");
        _callerNumLabel = GetNodeOrNull<Label>("%CallerNumLabel");
        _airStatus = GetNodeOrNull<Control>("%AirStatus");
        _deadAirLabel = GetNodeOrNull<Label>("%DeadAirLabel");
        _listenerLabel = GetNodeOrNull<Label>("%ListenerLabel");
        _listenerBar = GetNodeOrNull<ProgressBar>("%ListenerBar");
        _pauseMenu = GetNodeOrNull<Control>("%PauseMenu");
        _topicLabel = GetNodeOrNull<Label>("%Topic") ?? FindChild("Topic", true, false) as Label;

        if (_taskLabel != null)
        {
            _taskLabel.BbcodeEnabled = true;
            _taskLabel.FitContent = true;
            _taskLabel.ScrollActive = false;
            GameSession.Instance?.HookTaskToggle(_taskLabel);
        }

        if (_target != null)
        {
            _target.BbcodeEnabled = true;
            _target.ScrollActive = false;
            _target.MouseFilter = MouseFilterEnum.Ignore;
        }
    }


    private void WireButtons()
    {
        BindButton("%AcceptBtn", () => OnDecision(true));
        BindButton("%RejectBtn", () => OnDecision(false));
        BindButton("%MenuBtn", () => SetMenu(true));
        BindButton("%ResumeBtn", () => SetMenu(false));
        BindButton("%LobbyBtn", () => GameSession.Instance.ReturnToParty());
    }

    private void BindButton(string path, Action onPressed)
    {
        var b = GetNodeOrNull<Button>(path);
        if (b != null) b.Pressed += onPressed;
    }

    private T Need<T>(string name) where T : Node
    {
        var n = GetNodeOrNull<T>("%" + name);
        if (n == null)
            GD.PushError($"BroadcasterInterface: missing or wrong type: %{name} (expected {typeof(T).Name}). Add it or set Access as Unique Name.");
        return n;
    }

    private static void Log(string message)
    {
        if (DebugLog) GD.Print($"[Broadcaster] {message}");
    }

    // =====================================================================
    // TUTORIAL: steps + tasks
    // =====================================================================
    private static bool MicShouldRun(TutorialStep step) =>
        step == TutorialStep.GoLive || step == TutorialStep.KeypadModule || step == TutorialStep.Done;

    private static bool IsCallerStep(TutorialStep step) =>
        step == TutorialStep.PracticeCaller || step == TutorialStep.SuspiciousCaller;

    private void OnStep(TutorialStep step)
    {
        Log($"OnStep {step}");
        RefreshTasks();

        _callersAllowed = IsCallerStep(step);
        if (_callersAllowed) OnCaller();   // caller steps: show the card for the current caller
        else ShowCaller(false);            // otherwise the card goes away

        bool locked = !MicShouldRun(step);
        if (locked == _micLocked) return;
        _micLocked = locked;
        BeginParagraph();   // locked: clears the prompter. unlocked: starts a fresh paragraph.
    }

    private void OnQuota() => RefreshTasks();
    private void OnTopic()
    {
        var gs = GameSession.Instance;
        if (_topicLabel != null && gs != null)
            _topicLabel.Text = $"TOPIC: {gs.Topic.ToString().ToUpper()}";
        RefreshTasks();
    }
    private void RefreshTasks()
    {
        if (_taskLabel == null) return;

        var gs = GameSession.Instance;
        if (!_tutorial && gs != null) { _taskLabel.Text = gs.TaskBbcode("broadcaster"); return; }
        var items = new List<(string text, bool done)>();

        if (_tutorial && gs != null)
        {
            var s = gs.CurrentStep;
            items.Add(("Meet the studio", s > TutorialStep.MeetStudio));
            items.Add(($"Accept {AcceptGoal} callers ({Mathf.Min(gs.QuotaMet, AcceptGoal)}/{AcceptGoal})", s > TutorialStep.PracticeCaller));
            items.Add(("Talk to the mic", s > TutorialStep.GoLive));
            items.Add(("Keep talking while the keypad is fixed", s > TutorialStep.KeypadModule));
            items.Add(("Reject the suspicious caller", s > TutorialStep.SuspiciousCaller));
        }
        else if (gs != null)
        {
            // EDIT PER LEVEL
            items.Add(($"Accept {gs.QuotaTarget} {gs.Topic} callers ({gs.QuotaMet}/{gs.QuotaTarget})", gs.QuotaMet >= gs.QuotaTarget));
        }

        _taskLabel.Text = string.Join("\n", items.Select(t => t.done
            ? $"[color=#7A7A7A][s]- {t.text}[/s][/color]"   // crossed out + grayed
            : $"- {t.text}"));
    }

    private string LockedMessage() =>
        GameSession.Instance.CurrentStep < TutorialStep.GoLive
            ? "🎙 The mic unlocks in the Go Live step."
            : "📞 Mic paused: a caller is on the line. Decide first, then keep talking.";

    // =====================================================================
    // CALLER CARD
    // =====================================================================
    private void OnCaller()
    {
        var gs = GameSession.Instance;
        bool has = gs != null && !string.IsNullOrEmpty(gs.CallerName);

        // tutorial: no caller card outside the caller steps
        if (_tutorial && !_callersAllowed) has = false;

        Log($"OnCaller has={has} allowed={_callersAllowed} name='{gs?.CallerName}'");

        if (has)
        {
            // The Broadcaster NEVER sees the story. Only name and number.
            if (_nameLabel != null) _nameLabel.Text = gs.CallerName;
            if (_callerNumLabel != null) _callerNumLabel.Text = $"Caller {gs.CallerNumber} of {gs.CallerTotal}";
        }

        if (has && _caller != null && _caller.Visible) _caller.Visible = false;   // new caller: card clears and pops in again
        ShowCaller(has);
    }

    private void ShowCaller(bool show)
    {
        if (_caller == null) return;

        _callerTween?.Kill();
        if (show)
        {
            if (!_caller.Visible)
            {
                _caller.Modulate = new Color(1, 1, 1, 0);
                _caller.Scale = new Vector2(0.8f, 0.8f);
            }
            _caller.Visible = true;
            _caller.PivotOffset = _caller.Size / 2;
            _callerTween = CreateTween().SetParallel();
            _callerTween.TweenProperty(_caller, "modulate:a", 1.0f, 0.25);
            _callerTween.TweenProperty(_caller, "scale", Vector2.One, 0.3)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        else if (_caller.Visible)
        {
            _callerTween = CreateTween();
            _callerTween.TweenProperty(_caller, "modulate:a", 0.0f, 0.2);
            _callerTween.TweenCallback(Callable.From(() => _caller.Visible = false));
        }
    }

    private void OnDecision(bool accept)
    {
        GameSession.Instance.Decide(accept);
        if (!_tutorial) ShowCaller(false);   // real shift: clear the card right away
    }

    // ===== STAGE LIGHT (piano colors) + CUE CARD =====
    private void SetupCueDesk()
    {
        GD.Print($"[CUE] SetupCueDesk reached. tutorial={_tutorial} level={GameSession.Instance?.CurrentLevel?.Id} hasPiano={GameSession.Instance?.CurrentLevel?.HasPiano} piano={GameSession.Instance?.Piano != null}");
        var gs = GameSession.Instance;
        var d = gs?.CurrentLevel;
        if (gs == null || _tutorial || d == null) return;

        if (d.HasPiano && gs.Piano != null)
        {
            _stagePanel = MakeDeskPanel(false, out var box);   // false = left side

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 10);
            box.AddChild(row);

            _stageLamp = new PianoLamp { SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(64, 64) };
            row.AddChild(_stageLamp);

            var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            col.AddThemeConstantOverride("separation", 0);
            row.AddChild(col);

            col.AddChild(DeskLabel("CUE SHEET", 13, new Color("FBEFD9")));
            _stageName = DeskLabel("", 22, Colors.White);
            col.AddChild(_stageName);
            _stageHint = DeskLabel("", 12, new Color("F2D9B8"));
            col.AddChild(_stageHint);
        }
    }

    private PanelContainer MakeDeskPanel(bool rightSide, out VBoxContainer box)
    {
        var p = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 5 };
        var s = new StyleBoxFlat { BgColor = new Color("#7A3036"), BorderColor = new Color("#301E2E") };
        s.SetBorderWidthAll(3); s.BorderWidthBottom = 5; s.SetCornerRadiusAll(10); s.SetContentMarginAll(8);
        p.AddThemeStyleboxOverride("panel", s);
        AddChild(p);
        p.AnchorLeft = p.AnchorRight = p.AnchorTop = p.AnchorBottom = 0f;
        p.GrowHorizontal = GrowDirection.End;
        p.GrowVertical = GrowDirection.End;
        p.Resized += () => PlaceDeskPanel(p, rightSide);
        box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 6);
        p.AddChild(box);
        return p;
    }
    private void PlaceDeskPanel(Control p, bool rightSide)
    {
        var vp = GetViewportRect().Size;
        p.GlobalPosition = new Vector2(rightSide ? vp.X - p.Size.X - 12f : 12f, 12f);
    }
    private static Label DeskLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private void OnPianoCue(int i, int n, StudioPianoData.Cue c) => RefreshStageLight();
    private void OnPianoStatus(StudioPianoModule.PianoStatus s) => RefreshStageLight();
    private void OnPianoFault(bool f) => RefreshStageLight();
    private void OnPianoFaultProgress(int done, int total) => RefreshStageLight();

    private void RefreshStageLight()
    {
        var gs = GameSession.Instance;
        if (_stagePanel == null || gs?.Piano == null) return;
        var p = gs.Piano;

        bool live = p.Active && p.Cues.Count > 0 && p.Status != StudioPianoModule.PianoStatus.Complete;
        _stagePanel.Visible = live;
        if (live) CallDeferred(nameof(PlaceStagePanel));
        if (!live) { _stageLamp.TurnOff(); return; }

        // during a fault the light walks through the recalibration notes, one color at a time
        string letter;
        if (p.InFault)
        {
            var seq = StudioPianoData.GetRecalibration();
            letter = seq[Mathf.Clamp(p.FaultIndex, 0, seq.Length - 1)].Letter;
        }
        else letter = p.Cues[Mathf.Clamp(p.CueIndex, 0, p.Cues.Count - 1)].Letter;

        int c = gs.PianoColorOf(letter);
        if (c < 0) { _stageLamp.TurnOff(); _stageName.Text = "..."; return; }

        _stageLamp.TurnOn(PianoColorCode.ColorOf(c), p.InFault);   // blinking = recalibrating
        _stageName.Text = PianoColorCode.NameOf(c);
        _stageName.AddThemeColorOverride("font_color", PianoColorCode.ColorOf(c));
        _stageHint.Text = p.InFault ? "RECALIBRATING" : $"Note {p.CueIndex + 1} of {p.Cues.Count}";
    }

    // =====================================================================
    // MENU
    // =====================================================================
    private void SetMenu(bool open)
    {
        if (_pauseMenu == null) return;   // no pause menu in the scene yet
        _menuOpen = open;
        _pauseMenu.Visible = open;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_pauseMenu != null && e.IsActionPressed("ui_cancel"))
            SetMenu(!_menuOpen);   // Esc opens/closes the pause menu
    }

    // =====================================================================
    // PROMPTER
    // =====================================================================
    private void BeginParagraph()
    {
        if (_target == null) return;   // TargetLabel missing: already reported in FindNodes
        ClearStolenKey();

        Log($"BeginParagraph. micLocked={_micLocked}");
        _fade?.Kill();
        _running = false;
        _paragraph = _phrases.NextParagraph(shortForm: _tutorial);
        _segIdx = 0;
        _typedTotal = 0;
        _totalWords = _paragraph.Sum(s => s.Length);

        if (_deadAir.Listeners <= 0f) _deadAir.Reset();
        _deadAir.Register();

        if (_micLocked)
        {
            _target.Modulate = new Color(1, 1, 1, 0);
            _status.Text = LockedMessage();
            return;
        }
        ShowSegment();
    }

    private void ShowSegment()
    {
        ClearStolenKey();
        _words = _paragraph[_segIdx];
        _keys = _words.Select(_ => _rng.Next(4)).ToArray();
        _codedPromptActive = ShouldUseColorPrompt();
        _displayLetters = _codedPromptActive
            ? _keys.Select(_ => QwertyLetters[_rng.Next(QwertyLetters.Length)]).ToArray()
            : Array.Empty<char>();
        _idx = 0;
        _timeLeft = SegmentTime;
        _running = true;
        _deadAir.Register();   // fresh silence window when a line appears
        _status.Text = _codedPromptActive
            ? "⚡ SIGNAL INTERFERENCE — use the Technician's color code."
            : _tutorial ? "Press the key shown next to each word, in order." : "";

        RenderTarget();
        EnsureTargetVisible();
        Log($"ShowSegment text='{_target.Text}' visible={_target.IsVisibleInTree()} size={_target.Size} pos={_target.GlobalPosition}");

        _fade?.Kill();
        _target.Modulate = new Color(1, 1, 1, 0);
        _fade = CreateTween();
        _fade.TweenProperty(_target, "modulate:a", 1.0f, 0.4);
    }

    // Safety net while the scene layout is being redesigned: keeps the key label visible and big enough.
    // Once you're happy with TargetLabel in the scene, this can be deleted.
    private void EnsureTargetVisible()
    {
        _target.Visible = true;
        _target.ZIndex = 10;   // draw above neighbours
    }

    private void RenderTarget()
    {
        var parts = _keys.Select((k, i) =>
        {
            if (i == _stolenIdx && i >= _idx) return "[color=#E5484D][b]?[/b][/color]";
            if (_codedPromptActive && _displayLetters.Length == _keys.Length)
            {
                int[] colorToKey = GameSession.Instance?.PrompterColorKeyMap ?? Array.Empty<int>();
                int color = Array.IndexOf(colorToKey, k);
                if (color >= 0)
                {
                    string letter = _displayLetters[i].ToString();
                    if (i < _idx) letter = $"[s]{letter}[/s]";
                    else if (i == _idx) letter = $"[b][u]{letter}[/u][/b]";
                    return $"[color={GameSession.PrompterColorHex(color)}]{letter}[/color]";
                }
            }

            string a = Arrows[k];
            if (i < _idx) return $"[color=#7CFC9A]{a}[/color]";          // done
            if (i == _idx) return $"[color=#FFD54F][b]{a}[/b][/color]";  // press this one
            return $"[color=#B0BEC5]{a}[/color]";                        // coming up
        });
        _target.Text = "[center][font_size=32]" + string.Join("  ", parts) + "[/font_size][/center]";
    }

    private bool ShouldUseColorPrompt()
    {
        var gs = GameSession.Instance;
        if (_tutorial || gs == null || gs.LevelId != "emergency_broadcast") return false;
        if (_blackoutSequenceActive || gs.BlackoutSequenceActive) return false;
        if (gs.PrompterColorKeyMap.Length != 4) return false;

        double chance = Time.GetTicksMsec() < _postBlackoutBoostUntilMsec
            ? PostBlackoutColorPromptChance
            : NormalColorPromptChance;
        return _rng.NextDouble() < chance;
    }

    private void OnPrompterColorMapChanged()
    {
        if (_codedPromptActive && GameSession.Instance?.PrompterColorKeyMap.Length != 4)
        {
            _codedPromptActive = false;
            RenderTarget();
        }
    }

    private void OnBlackoutSequenceChanged(bool active)
    {
        bool wasActive = _blackoutSequenceActive;
        _blackoutSequenceActive = active;

        if (active)
        {
            if (_codedPromptActive)
            {
                _codedPromptActive = false;
                _displayLetters = Array.Empty<char>();
                RenderTarget();
                _status.Text = "";
            }
        }
        else if (wasActive)
        {
            _postBlackoutBoostUntilMsec = Time.GetTicksMsec() + PostBlackoutBoostMs;
        }
    }

    private void SpawnWord(string word, bool correct)
    {
        if (_wordArea == null) return;

        // load once, reuse after that
        _wordFont ??= GD.Load<Font>("res://assets/fonts/Fredoka/static/Fredoka-SemiBold.ttf");

        var l = new Label { Text = word, MouseFilter = MouseFilterEnum.Ignore };
        if (_wordFont != null) l.AddThemeFontOverride("font", _wordFont);
        l.AddThemeFontSizeOverride("font_size", 28);
        l.AddThemeColorOverride("font_color", correct ? Colors.White : new Color("#F44336"));

        // border around the letters
        l.AddThemeColorOverride("font_outline_color", new Color("#2B1B12"));
        l.AddThemeConstantOverride("outline_size", 8);

        // soft drop shadow underneath
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.55f));
        l.AddThemeConstantOverride("shadow_offset_x", 2);
        l.AddThemeConstantOverride("shadow_offset_y", 3);
        l.AddThemeConstantOverride("shadow_outline_size", 8);   // makes the shadow follow the outline shape

        _wordArea.AddChild(l);

        var size = l.GetMinimumSize();
        var area = _wordArea.Size;
        var pos = new Vector2(
            (float)_rng.NextDouble() * Mathf.Max(0, area.X - size.X),
            (float)_rng.NextDouble() * Mathf.Max(0, area.Y - size.Y));
        l.Position = pos;
        l.Modulate = new Color(1, 1, 1, 0);

        CreateTween().TweenProperty(l, "position:y", pos.Y - 40, 1.4);   // drifts upward
        var f = CreateTween();
        f.TweenProperty(l, "modulate:a", 1.0f, 0.2);   // fade in
        f.TweenInterval(0.7);
        f.TweenProperty(l, "modulate:a", 0.0f, 0.5);   // fade away
        f.TweenCallback(Callable.From(l.QueueFree));
    }

    // fades the current line out, waits a moment, then runs `then`
    private void FadeOutThen(Action then, double wait)
    {
        _running = false;
        _fade?.Kill();
        _fade = CreateTween();
        _fade.TweenProperty(_target, "modulate:a", 0.0f, 0.4);
        _fade.TweenInterval(wait);
        _fade.TweenCallback(Callable.From(then));
    }

    private void NextSegment()
    {
        if (_micLocked) return;   // a caller took over the line while we waited
        _segIdx++;
        if (_segIdx >= _paragraph.Length) BeginParagraph();   // keep talking: no gap = no dead air
        else ShowSegment();
    }

    private double RestAfterSegment()
    {
        if (_tutorial) return 0.3;
        bool endOfParagraph = _segIdx + 1 >= _paragraph.Length;
        return endOfParagraph ? _paragraphRest : _lineRest;
    }

    private void SegmentDone()
    {
        if (!_tutorial) { _micDone++; RefreshTasks(); }
        _status.Text = (!_tutorial && _segIdx + 1 >= _paragraph.Length) ? "ON AIR! Take a breath..." : "ON AIR!";
        if (_deadAirLabel != null) _deadAirLabel.Visible = false;   // no stale warning during the rest
        FadeOutThen(NextSegment, RestAfterSegment());   // do this BEFORE Report: Report may reset the prompter

        var gs = GameSession.Instance;
        if (_tutorial && gs.CurrentStep == TutorialStep.GoLive)
            gs.Report(TutorialAction.PhraseMatched);
    }

    // =====================================================================
    // KEYBOARD INPUT
    // =====================================================================
    public override void _Input(InputEvent e)
    {
        if (_spillActive && e is InputEventMouseMotion mm) ScrubSpill(mm.GlobalPosition);
        if (_feedbackActive && e is InputEventKey) { GetViewport().SetInputAsHandled(); return; }
        if (!_running || _menuOpen) return;
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;

        int key = k.PhysicalKeycode switch
        {
            Key.W or Key.Up => 0,
            Key.A or Key.Left => 1,
            Key.S or Key.Down => 2,
            Key.D or Key.Right => 3,
            _ => -1,
        };
        if (key < 0) return;

        GetViewport().SetInputAsHandled();
        _deadAir.Register();   // any prompter key counts as talking, even a wrong one

        if (key == _keys[_idx])
        {
            SpawnWord(_words[_idx], true);   // white: right key
            _idx++; _typedTotal++;
            if (_stolenIdx >= 0 && _idx > _stolenIdx) ClearStolenKey();
            RenderTarget();
            if (_idx == _keys.Length) SegmentDone();
        }
        else
        {
            SpawnWord(_words[_idx], false);  // red: wrong key, same word, no advance
            if (_tutorial)
            {
                _status.Text = "Wrong key. Press the one shown in yellow. No penalty.";
            }
            else
            {
                _timeLeft = Mathf.Max(0, _timeLeft - 2);
                GameSession.Instance?.ReportAirtime(-5);
            }
        }
    }

    // =====================================================================
    // TIMER + DEAD AIR
    // =====================================================================
    public override void _Process(double delta)
    {
        if (_stagePanel != null && GameSession.Instance is { } gsx && gsx.Piano is { } pp)
        {
            int sig = HashCode.Combine(pp.Active, pp.Cues?.Count ?? 0, pp.CueIndex, pp.Status, pp.InFault, pp.FaultIndex, gsx.HasPianoColorMap);
            if (sig != _cueSig) { _cueSig = sig; RefreshStageLight(); }
        }

        if (_feedbackActive)
        {
            Position = _shakeOrigin + new Vector2((float)GD.RandRange(-6.0, 6.0), (float)GD.RandRange(-6.0, 6.0));   // screen shake
            if (_running && !_tutorial) { _deadAir.Register(); _timeLeft += delta; }   // the lock isn't punished as dead air
        }

        if (!_running || _tutorial) return;   // tutorial: no countdown, no dead air
        if (_stolenIdx >= 0 && Time.GetTicksMsec() >= _stolenRevealMs) ClearStolenKey();   // raccoon drops the key

        _deadAir.Tick((float)delta);
        UpdateDeadAirUi();
        if (_deadAir.Listeners <= 0f)
        {
            _status.Text = "📻 The listeners tuned out! Starting over...";
            GameSession.Instance?.ReportAirtime(-15);
            FadeOutThen(BeginParagraph, 2.0);   // BeginParagraph resets the monitor
            return;
        }

        _timeLeft -= delta;
        if (_timeLeft > 0) return;
        _status.Text = "Too slow! Next line...";
        GameSession.Instance?.ReportAirtime(-5);
        FadeOutThen(NextSegment, RestAfterSegment());
    }

    private void UpdateDeadAirUi()
    {
        if (_listenerBar != null) _listenerBar.Value = _deadAir.Listeners;
        if (_listenerLabel != null) _listenerLabel.Text = $"Listeners: {Mathf.CeilToInt(_deadAir.Listeners)}%";
        if (_deadAirLabel == null) return;

        _deadAirLabel.Visible = _deadAir.IsDeadAir;
        if (_deadAir.IsDeadAir)   // flash: the text itself is the warning, not just the color
            _deadAirLabel.Modulate = new Color(1, 1, 1, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin((float)Time.GetTicksMsec() / 1000f * 10f)));
    }

    private void SetupInterruptions()
    {
        var d = GameSession.Instance?.CurrentLevel;
        if (_tutorial || d == null) return;
        if (d.CoffeeSpill) _interruptions.Add(StartCoffeeSpill);
        if (d.StolenKey) _interruptions.Add(StartStolenKey);
        if (d.MicFeedback) _interruptions.Add(StartMicFeedback);
        if (_interruptions.Count == 0) return;
        if (d.InvertedPrompter) _interruptions.Add(StartInvertedPrompter);

        _interruptTimer = new Timer { OneShot = true, WaitTime = NextInterruptionGap() };
        _interruptTimer.Timeout += TriggerInterruption;
        AddChild(_interruptTimer);
        _interruptTimer.Start();
    }

    private double NextInterruptionGap()
    {
        double gap = GameSession.Instance?.CurrentLevel?.InterruptionGap ?? 30;
        return gap * (0.7 + _rng.NextDouble() * 0.6);
    }

    private void RetryInterruptionSoon() { _interruptTimer.WaitTime = 3; _interruptTimer.Start(); }

    private void TriggerInterruption()
    {
        var gs = GameSession.Instance;
        if (gs == null || gs.Ended) return;
        // wait for a calm moment: a live line, nothing else going on
        if (_interruptionActive || !_running || _micLocked) { RetryInterruptionSoon(); return; }
        _interruptions[_rng.Next(_interruptions.Count)]();
    }

    private void EndInterruption()
    {
        _interruptionActive = false;
        if (_interruptTimer != null && IsInstanceValid(_interruptTimer))
        {
            _interruptTimer.WaitTime = NextInterruptionGap();
            _interruptTimer.Start();
        }
    }

    private void StartCoffeeSpill()
    {
        if (_target == null) { RetryInterruptionSoon(); return; }
        _interruptionActive = true;
        _spillActive = true;
        int id = ++_spillId;

        var area = _target.GetGlobalRect();
        const int n = 7;
        for (int i = 0; i < n; i++)
        {
            float size = 80 + (float)_rng.NextDouble() * 50;
            var blob = new Panel
            {
                MouseFilter = MouseFilterEnum.Ignore,
                ZIndex = 20,
                CustomMinimumSize = new Vector2(size, size),
                Size = new Vector2(size, size),
                PivotOffset = new Vector2(size / 2, size / 2),
            };
            var style = new StyleBoxFlat { BgColor = new Color(0.30f, 0.17f, 0.08f, 0.95f) };
            style.SetCornerRadiusAll((int)(size / 2));
            blob.AddThemeStyleboxOverride("panel", style);
            AddChild(blob);

            float x = area.Position.X + area.Size.X * (i + 0.5f) / n - size / 2 + (float)(_rng.NextDouble() - 0.5) * 30;
            float y = area.GetCenter().Y - size / 2 + (float)(_rng.NextDouble() - 0.5) * 30;
            blob.GlobalPosition = new Vector2(x, y);

            blob.Scale = new Vector2(0.3f, 0.3f);
            CreateTween().TweenProperty(blob, "scale", Vector2.One, 0.2).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            _blobs.Add(blob);
            _blobHp[blob] = 1f;
        }
        _status.Text = "Coffee spill! Wipe the prompter with your mouse.";

        // failsafe: it dries up on its own after 20 seconds
        GetTree().CreateTimer(20).Timeout += () => { if (_spillActive && id == _spillId) EndCoffeeSpill(); };
    }

    private void ScrubSpill(Vector2 p)
    {
        for (int i = _blobs.Count - 1; i >= 0; i--)
        {
            var b = _blobs[i];
            if (!b.GetGlobalRect().Grow(8).HasPoint(p)) continue;
            float hp = _blobHp[b] - 0.07f;
            if (hp <= 0.05f) { b.QueueFree(); _blobs.RemoveAt(i); _blobHp.Remove(b); }
            else { _blobHp[b] = hp; b.Modulate = new Color(1, 1, 1, hp); }
        }
        if (_blobs.Count == 0) EndCoffeeSpill();
    }

    private void EndCoffeeSpill()
    {
        if (!_spillActive) return;
        _spillActive = false;
        foreach (var b in _blobs) if (IsInstanceValid(b)) b.QueueFree();
        _blobs.Clear(); _blobHp.Clear();
        if (_status.Text.StartsWith("Coffee")) _status.Text = "";
        EndInterruption();
    }

    private void StartStolenKey()
    {
        // needs a live line with at least one key still to come, and no color-code prompt
        if (!_running || _codedPromptActive || _keys.Length - 1 <= _idx) { RetryInterruptionSoon(); return; }
        _interruptionActive = true;
        _stolenIdx = _rng.Next(_idx + 1, _keys.Length);
        _stolenRevealMs = Time.GetTicksMsec() + 8000;   // after 8s the raccoon drops it (so it can't soft-lock)
        GameSession.Instance?.ReportStolenKey(_stolenIdx + 1, Arrows[_keys[_stolenIdx]]);
        _status.Text = $"The raccoon stole key #{_stolenIdx + 1}! Ask the Technician what it is.";
        RenderTarget();
    }

    private void ClearStolenKey()
    {
        if (_stolenIdx < 0) return;
        _stolenIdx = -1;
        GameSession.Instance?.ReportStolenKey(-1, "");
        if (_status.Text.StartsWith("The raccoon stole")) _status.Text = "";
        RenderTarget();
        EndInterruption();
    }

    private void StartMicFeedback()
    {
        _interruptionActive = true;
        _feedbackActive = true;
        _feedbackClicks = 0;
        _shakeOrigin = Position;
        _feedbackPips.Clear();

        _feedbackOverlay = new Control { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 80 };
        AddChild(_feedbackOverlay);
        _feedbackOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var overlay = _feedbackOverlay;

        // pulsing red wash (tween belongs to the overlay, so it dies with it)
        var tint = new ColorRect { Color = new Color(0.9f, 0.1f, 0.1f, 0.22f), MouseFilter = MouseFilterEnum.Ignore };
        overlay.AddChild(tint);
        tint.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var pulse = tint.CreateTween().SetLoops();
        pulse.TweenProperty(tint, "modulate:a", 0.35f, 0.25);
        pulse.TweenProperty(tint, "modulate:a", 1.0f, 0.25);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        overlay.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // card (same look as the Operator module popup)
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var cardStyle = new StyleBoxFlat { BgColor = new Color("#7A3036"), BorderColor = new Color("#301E2E") };
        cardStyle.SetBorderWidthAll(4);
        cardStyle.BorderWidthBottom = 8;
        cardStyle.SetCornerRadiusAll(14);
        cardStyle.ShadowColor = new Color(0, 0, 0, 0.5f);
        cardStyle.ShadowSize = 12;
        card.AddThemeStyleboxOverride("panel", cardStyle);
        center.AddChild(card);

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        card.AddChild(margin);

        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(box);

        var title = new Label { Text = "MIC FEEDBACK!", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontSizeOverride("font_size", 38);
        title.AddThemeColorOverride("font_color", new Color("FBEFD9"));
        title.AddThemeColorOverride("font_outline_color", new Color("4A0D12"));
        title.AddThemeConstantOverride("outline_size", 12);
        box.AddChild(title);
        _feedbackTitle = title;

        var sub = new Label { Text = "Hit MUTE 3 times before time runs out!", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        sub.AddThemeFontSizeOverride("font_size", 18);
        sub.AddThemeColorOverride("font_color", new Color("F2D9B8"));
        box.AddChild(sub);

        // 3 lamps: one lights up per click
        var pips = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        pips.AddThemeConstantOverride("separation", 14);
        for (int i = 0; i < 3; i++)
        {
            var pip = new Panel { CustomMinimumSize = new Vector2(30, 30), MouseFilter = MouseFilterEnum.Ignore };
            pip.AddThemeStyleboxOverride("panel", PipStyle(false));
            pips.AddChild(pip);
            _feedbackPips.Add(pip);
        }
        box.AddChild(pips);

        // timer bar: drains and turns red
        var bar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(360, 22), MouseFilter = MouseFilterEnum.Ignore };
        var fill = new StyleBoxFlat { BgColor = new Color("#FFD54F") };
        fill.SetCornerRadiusAll(8);
        var back = new StyleBoxFlat { BgColor = new Color("#301E2E") };
        back.SetCornerRadiusAll(8);
        bar.AddThemeStyleboxOverride("fill", fill);
        bar.AddThemeStyleboxOverride("background", back);
        box.AddChild(bar);

        _feedbackBtn = new Button { Text = "MUTE", CustomMinimumSize = new Vector2(260, 68), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        UiStyle.StyleButton(_feedbackBtn, UiStyle.Yellow);
        _feedbackBtn.AddThemeFontSizeOverride("font_size", 28);
        _feedbackBtn.Pressed += OnMutePressed;
        box.AddChild(_feedbackBtn);

        // pop-in + title wobble (needs the layout size, so run next frame)
        Callable.From(() =>
        {
            if (!IsInstanceValid(card) || !IsInstanceValid(title)) return;
            card.PivotOffset = card.Size / 2;
            card.Scale = new Vector2(0.6f, 0.6f);
            card.CreateTween().TweenProperty(card, "scale", Vector2.One, 0.25)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

            title.PivotOffset = title.Size / 2;
            var wob = title.CreateTween().SetLoops();
            wob.TweenProperty(title, "rotation", 0.04f, 0.06);
            wob.TweenProperty(title, "rotation", -0.04f, 0.06);
        }).CallDeferred();

        _status.Text = "";
        _feedbackTween = CreateTween().SetParallel();
        _feedbackTween.TweenProperty(bar, "value", 0.0, FeedbackSeconds);
        _feedbackTween.TweenProperty(fill, "bg_color", new Color("#E5484D"), FeedbackSeconds);
        _feedbackTween.Chain().TweenCallback(Callable.From(() => EndMicFeedback(false)));
    }

    private void OnMutePressed()
    {
        if (!_feedbackActive) return;
        if (_feedbackClicks < _feedbackPips.Count)
            _feedbackPips[_feedbackClicks].AddThemeStyleboxOverride("panel", PipStyle(true));
        _feedbackClicks++;

        _feedbackBtn.PivotOffset = _feedbackBtn.Size / 2;   // little button punch
        _feedbackBtn.Scale = new Vector2(0.92f, 0.92f);
        _feedbackBtn.CreateTween().TweenProperty(_feedbackBtn, "scale", Vector2.One, 0.12)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        if (_feedbackClicks >= 3) EndMicFeedback(true);
    }

    private static StyleBoxFlat PipStyle(bool lit)
    {
        var s = new StyleBoxFlat { BgColor = lit ? new Color("#7CFC9A") : new Color("#301E2E"), BorderColor = new Color("FBEFD9") };
        s.SetBorderWidthAll(3);
        s.SetCornerRadiusAll(15);
        return s;
    }

    private void EndMicFeedback(bool success)
    {
        if (!_feedbackActive) return;
        _feedbackActive = false;
        _feedbackTween?.Kill();
        Position = _shakeOrigin;

        var overlay = _feedbackOverlay;
        _feedbackOverlay = null;
        if (overlay != null && IsInstanceValid(overlay))
        {
            if (success && _feedbackTitle != null && IsInstanceValid(_feedbackTitle))
            {
                _feedbackTitle.Text = "MUTED!";
                _feedbackTitle.AddThemeColorOverride("font_color", new Color("#7CFC9A"));
                if (_feedbackBtn != null && IsInstanceValid(_feedbackBtn)) _feedbackBtn.Disabled = true;

                var t = overlay.CreateTween();   // brief "MUTED!" then fade out
                t.TweenInterval(0.25);
                t.TweenProperty(overlay, "modulate:a", 0.0f, 0.2);
                t.TweenCallback(Callable.From(overlay.QueueFree));
            }
            else overlay.QueueFree();
        }

        if (!success)
        {
            GameSession.Instance?.ReportAirtime(-10);
            _status.Text = "Feedback ruined the broadcast! Quality dropped.";
        }
        EndInterruption();
    }
    private void StartInvertedPrompter()
    {
        if (_target == null) { RetryInterruptionSoon(); return; }
        _interruptionActive = true;
        _prompterFlipped = true;
        int id = ++_flipId;

        SpinPrompter(Mathf.Pi);

        _flipBackBtn = new Button { Text = "FLIP BACK", ZIndex = 30, CustomMinimumSize = new Vector2(200, 52) };
        GameOverScreen.StyleBackButton(_flipBackBtn);
        _flipBackBtn.Pressed += EndInvertedPrompter;
        AddChild(_flipBackBtn);
        var r = _target.GetGlobalRect();
        _flipBackBtn.GlobalPosition = new Vector2(r.GetCenter().X - 100, r.End.Y + 12);   // adjust if it overlaps something

        _status.Text = "The raccoon flipped the prompter! Read it upside down or click FLIP BACK.";

        // failsafe so it can't soft-lock
        GetTree().CreateTimer(12).Timeout += () => { if (_prompterFlipped && id == _flipId) EndInvertedPrompter(); };
    }

    private void EndInvertedPrompter()
    {
        if (!_prompterFlipped) return;
        _prompterFlipped = false;
        if (_target != null) SpinPrompter(0f);
        if (_flipBackBtn != null && IsInstanceValid(_flipBackBtn)) _flipBackBtn.QueueFree();
        _flipBackBtn = null;
        if (_status.Text.StartsWith("The raccoon flipped")) _status.Text = "";
        EndInterruption();
    }

    private void SpinPrompter(float radians)
    {
        _flipTween?.Kill();
        _target.PivotOffset = _target.Size / 2;
        _flipTween = CreateTween();
        _flipTween.TweenProperty(_target, "rotation", radians, 0.35)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    // =====================================================================
    // ON AIR METER (not hooked up: needs a `_meter` container in the scene)
    // To use it: add `private Control _meter;`, find it in FindNodes(), then call
    // UpdateMeter() after `_typedTotal = 0;` in BeginParagraph() and after
    // `_typedTotal++` in _Input().
    // =====================================================================
    //private void UpdateMeter()
    //{
    //    int segs = _meter.GetChildCount();
    //    int lit = _totalWords == 0 ? 0 : _typedTotal * segs / _totalWords;
    //    for (int i = 0; i < segs; i++)
    //    {
    //        var seg = (Control)_meter.GetChild(i);
    //        seg.ThemeTypeVariation = i < lit ? "SegOn" : "SegOff";
    //        seg.Modulate = i < lit ? Colors.White : new Color(1, 1, 1, 0.35f);
    //    }
    //}
}
