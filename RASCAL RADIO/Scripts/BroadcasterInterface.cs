using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;

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
    private int _segIdx;
    private int _idx;                      // words done in the current line
    private double _timeLeft;

    // ON AIR meter (not in the scene yet, see the bottom of the file)
    private int _typedTotal, _totalWords;

    private Tween _fade, _callerTween;

    private readonly PhraseBank _phrases = new(new Random());
    private readonly DeadAirMonitor _deadAir = new();
    private readonly Random _rng = new();

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
    }

    public override void _ExitTree()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;
        gs.StepChanged -= OnStep;
        gs.CallerChanged -= OnCaller;
        gs.QuotaChanged -= OnQuota;
        gs.TasksChanged -= RefreshTasks;
        gs.TopicChanged -= OnTopic;
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
        if (!_tutorial && gs != null) { _taskLabel.Text = gs.TaskBbcode(); return; }
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
        _words = _paragraph[_segIdx];
        _keys = _words.Select(_ => _rng.Next(4)).ToArray();
        _idx = 0;
        _timeLeft = SegmentTime;
        _running = true;
        _deadAir.Register();   // fresh silence window when a line appears
        _status.Text = _tutorial ? "Press the key shown next to each word, in order." : "";

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
            string a = Arrows[k];
            if (i < _idx) return $"[color=#7CFC9A]{a}[/color]";          // done
            if (i == _idx) return $"[color=#FFD54F][b]{a}[/b][/color]";  // press this one
            return $"[color=#B0BEC5]{a}[/color]";                        // coming up
        });
        _target.Text = "[center][font_size=32]" + string.Join("  ", parts) + "[/font_size][/center]";
    }

    private void SpawnWord(string word, bool correct)
    {
        if (_wordArea == null) return;

        var l = new Label { Text = word, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", 28);
        l.AddThemeColorOverride("font_color", correct ? Colors.White : new Color("#F44336"));
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
        if (!_running || _tutorial) return;   // tutorial: no countdown, no dead air

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