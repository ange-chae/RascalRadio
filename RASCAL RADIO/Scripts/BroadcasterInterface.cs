using System;
using System.Linq;
using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;
using System.Collections.Generic;

public partial class BroadcasterInterface : Control
{
    [Signal] public delegate void ClosedEventHandler();
    private readonly PhraseBank _phrases = new(new Random());
    private readonly DeadAirMonitor _deadAir = new();
    private readonly Random _rng = new();

    [Export] public double SegmentTime = 12.0;                       // seconds to finish one floating line (real shift only)
    [Export] public string LobbyScenePath = "res://scenes/lobby.tscn"; // CHANGE to your real lobby scene path
    private Control _wordArea;
    // key index: 0 = W / Up, 1 = A / Left, 2 = S / Down, 3 = D / Right
    private static readonly string[] Arrows = { "W", "A", "S", "D" };

    // paragraph state
    private string[][] _paragraph = Array.Empty<string[]>();
    private int _segIdx;
    private string[] _words = Array.Empty<string>();
    private int[] _keys = Array.Empty<int>();
    private int _idx;                 // words done in the current line
    private int _typedTotal, _totalWords;   // for the ON AIR meter
    private double _timeLeft;

    private bool _running, _tutorial, _micLocked, _menuOpen;
    private Tween _fade, _callerTween;

    // nodes
    private RichTextLabel _target;
    private Label _status, _deadAirLabel, _listenerLabel;
    private RichTextLabel _taskLabel;
    private bool _calculatorDone;   // set to true when the calculator task is finished (see note below)
    private int _micDone;
    private const int MicGoal = 3;
    private const int AcceptGoal = 2;
    private ProgressBar _listenerBar;
    private Control _caller, _airStatus, _pauseMenu;

    private T Need<T>(string name) where T : Node
    {
        var n = GetNodeOrNull<T>("%" + name);
        if (n == null)
            GD.PushError($"BroadcasterInterface: missing or wrong type: %{name} (expected {typeof(T).Name}). Add it or set Access as Unique Name.");
        return n;
    }
    public override void _Ready()
    {

        var gs = GameSession.Instance;
        _tutorial = gs != null && gs.IsTutorial;
        if (!_tutorial && gs?.CurrentLevel != null) SegmentTime = gs.CurrentLevel.SegmentTime;
        GD.Print($"[Broadcaster] _Ready running. tutorial={_tutorial}, session={(gs != null)}");
        // FIRST, so nothing below can stop the narrator from appearing
        if (_tutorial) AddChild(new TutorialOverlay { RoleName = "Broadcaster", ShowDoneWhen = false });

        // ---- required nodes (all of these exist in your scene) ----
        _wordArea = Need<Control>("WordArea");
        _target = Need<RichTextLabel>("TargetLabel");
        _status = Need<Label>("StatusLabel");
        _taskLabel = Need<RichTextLabel>("TaskLabel");
        _caller = Need<Control>("Caller");
        if (_wordArea == null || _target == null || _status == null || _taskLabel == null || _caller == null)
        {
            GD.PushError("BroadcasterInterface: fix the missing nodes listed above, then run again.");
            return;
        }

        _taskLabel.BbcodeEnabled = true;
        _taskLabel.FitContent = true;
        _taskLabel.ScrollActive = false;

        // ---- optional nodes: the script skips them if they aren't in the scene ----
        _airStatus = GetNodeOrNull<Control>("%AirStatus");
        _deadAirLabel = GetNodeOrNull<Label>("%DeadAirLabel");
        _listenerLabel = GetNodeOrNull<Label>("%ListenerLabel");
        _listenerBar = GetNodeOrNull<ProgressBar>("%ListenerBar");
        _pauseMenu = GetNodeOrNull<Control>("%PauseMenu");
        var accept = GetNodeOrNull<Button>("%AcceptBtn");
        var reject = GetNodeOrNull<Button>("%RejectBtn");
        var menuBtn = GetNodeOrNull<Button>("%MenuBtn");
        var resumeBtn = GetNodeOrNull<Button>("%ResumeBtn");
        var lobbyBtn = GetNodeOrNull<Button>("%LobbyBtn");

        _target.BbcodeEnabled = true;
        _target.ScrollActive = false;
        _target.MouseFilter = MouseFilterEnum.Ignore;

        if (accept != null) accept.Pressed += () => OnDecision(true);
        if (reject != null) reject.Pressed += () => OnDecision(false);
        if (menuBtn != null) menuBtn.Pressed += () => SetMenu(true);
        if (resumeBtn != null) resumeBtn.Pressed += () => SetMenu(false);
        if (lobbyBtn != null) lobbyBtn.Pressed += () => GetTree().ChangeSceneToFile(LobbyScenePath);

        if (_pauseMenu != null) _pauseMenu.Visible = false;
        if (_deadAirLabel != null) _deadAirLabel.Visible = false;
        if (_airStatus != null) _airStatus.Visible = !_tutorial;   // dead air only counts in a real shift
        _caller.Visible = false;

        if (gs != null)
        {
            gs.CallerChanged += OnCaller;
            gs.QuotaChanged += OnQuota;
            if (_tutorial) gs.StepChanged += OnStep;
        }

        if (_tutorial)
        {
            _micLocked = !MicShouldRun(gs.CurrentStep);
            //ShowTask(gs.CurrentStep);
        }

        OnCaller();
        OnQuota();
        BeginParagraph();
    }

    public override void _ExitTree()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;
        gs.StepChanged -= OnStep;
        gs.CallerChanged -= OnCaller;
        gs.QuotaChanged -= OnQuota;
    }

    // ===== tutorial: narrator + task =====
    private static bool MicShouldRun(TutorialStep step) =>
        step == TutorialStep.GoLive || step == TutorialStep.KeypadModule || step == TutorialStep.Done;

    private void OnStep(TutorialStep step)
    {
        RefreshTasks();
        if (step != TutorialStep.PracticeCaller && step != TutorialStep.SuspiciousCaller)
            ShowCaller(false);   // caller decision was correct, so the card goes away
        bool locked = !MicShouldRun(step);
        if (locked == _micLocked) return;
        _micLocked = locked;
        BeginParagraph();   // locked: clears the prompter. unlocked: starts a fresh paragraph.
    }
    public override void _UnhandledInput(InputEvent e)
    {
        if (_pauseMenu != null && e.IsActionPressed("ui_cancel"))
            SetMenu(!_menuOpen);   // Esc opens/closes the pause menu
    }
    private void RefreshTasks()
    {
        var gs = GameSession.Instance;
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
            items.Add(($"Accept {gs.QuotaTarget} callers ({gs.QuotaMet}/{gs.QuotaTarget})", gs.QuotaMet >= gs.QuotaTarget));
            items.Add(("Finish the calculator task", _calculatorDone));
        }

        _taskLabel.Text = string.Join("\n", items.Select(t => t.done
            ? $"[color=#7A7A7A][s]- {t.text}[/s][/color]"   // crossed out + grayed
            : $"- {t.text}"));
    }

    private string LockedMessage() =>
        GameSession.Instance.CurrentStep < TutorialStep.GoLive
            ? "🎙 The mic unlocks in the Go Live step."
            : "📞 Mic paused: a caller is on the line. Decide first, then keep talking.";

    // ===== caller popup =====
    private void OnCaller()
    {
        var gs = GameSession.Instance;
        bool has = gs != null && !string.IsNullOrEmpty(gs.CallerName);
        if (has)
        {
            // The Broadcaster NEVER sees the story. Only name and number.
            SetLabel("%NameLabel", gs.CallerName);
            SetLabel("%CallerNumLabel", $"Caller {gs.CallerNumber} of {gs.CallerTotal}");
        }

        if (has && _caller.Visible) _caller.Visible = false;   // new caller: card clears and pops in again
        ShowCaller(has);
    }

    private void ShowCaller(bool show)
    {
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

    private void OnQuota() => RefreshTasks();

    private void SetLabel(string path, string text)
    {
        var l = GetNodeOrNull<Label>(path);
        if (l != null) l.Text = text;
    }

    // ===== menu =====
    private void SetMenu(bool open)
    {
        if (_pauseMenu == null) return;   // no pause menu in the scene yet
        _menuOpen = open;
        _pauseMenu.Visible = open;
    }
    // ===== prompter =====
    private void BeginParagraph()
    {
        GD.Print($"[Broadcaster] BeginParagraph. micLocked={_micLocked}");
        _fade?.Kill();
        _running = false;
        _paragraph = _phrases.NextParagraph(shortForm: _tutorial);
        _segIdx = 0;
        _typedTotal = 0;
        _totalWords = _paragraph.Sum(s => s.Length);
        //UpdateMeter();

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
        _status.Text = _tutorial ? "Press the key shown next to each word, in order." : "";

        RenderTarget();

        _fade?.Kill();
        _target.Modulate = new Color(1, 1, 1, 0);
        _fade = CreateTween();
        _fade.TweenProperty(_target, "modulate:a", 1.0f, 0.4);
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
        _target.Text = "[center][font_size=44]" + string.Join("  ", parts) + "[/font_size][/center]";
    }

    private void SpawnWord(string word, bool correct)
    {
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

    // ===== keyboard input =====
    public override void _Input(InputEvent e)
    {

        if (e is InputEventKey { Pressed: true, Echo: false }) GD.Print($"[Broadcaster] key pressed. running={_running}, menuOpen={_menuOpen}");
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
            //UpdateMeter();
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

    private void SegmentDone()
    {
        if (!_tutorial) { _micDone++; RefreshTasks(); }
        _status.Text = "ON AIR!";
        FadeOutThen(NextSegment, 0.3);   // do this BEFORE Report: Report may reset the prompter
        var gs = GameSession.Instance;
        if (_tutorial && gs.CurrentStep == TutorialStep.GoLive)
            gs.Report(TutorialAction.PhraseMatched);
    }

    // ===== timer + dead air =====
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
        FadeOutThen(NextSegment, 0.3);
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
}