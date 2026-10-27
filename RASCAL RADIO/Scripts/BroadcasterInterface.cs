using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;

public partial class BroadcasterInterface : PopupFrame
{
    // Phrases and decoy words are random. See Core/Models/PhraseBank.cs to add more.
    private readonly PhraseBank _phrases = new(new Random());
    private readonly DeadAirMonitor _deadAir = new();

    [Export] public double SegmentTime = 20.0;

    private string[] _sentence = Array.Empty<string>();
    private string[] _decoys = Array.Empty<string>();
    private bool _tutorial;
    private bool _micLocked;

    private int _idx;
    private double _timeLeft;
    private bool _running;
    private RichTextLabel _target;
    private Label _timer, _status, _meterLabel;
    private HFlowContainer _bank;
    private Button _tryAgain;
    private HBoxContainer _meter;
    private readonly Dictionary<string, Button> _wordButtons = new();
    private Label _deadAirLabel, _listenerLabel;
    private ProgressBar _listenerBar;

    public override void _Ready()
    {
        base._Ready();
        _target = GetNode<RichTextLabel>("%TargetLabel");
        _timer = GetNode<Label>("%TimerLabel");
        _status = GetNode<Label>("%StatusLabel");
        _meterLabel = GetNode<Label>("%MeterLabel");
        _bank = GetNode<HFlowContainer>("%WordBank");
        _tryAgain = GetNode<Button>("%TryAgainBtn");
        _meter = GetNode<HBoxContainer>("%Meter");

        GetNode<Button>("%RetryBtn").Pressed += () => StartSegment(false);   // same phrase again
        _tryAgain.Pressed += () => StartSegment(false);
        GetNode<Button>("%AcceptBtn").Pressed += () => OnDecision(true);
        GetNode<Button>("%RejectBtn").Pressed += () => OnDecision(false);

        var gs = GameSession.Instance;
        _tutorial = gs != null && gs.IsTutorial;

        if (_tutorial)
        {
            AddChild(new TutorialOverlay { RoleName = "Broadcaster" });
            gs.StepChanged += OnStep;
            gs.CallerChanged += OnCaller;
            gs.QuotaChanged += OnQuota;
            SetLabel("%StrikeCount", "none");
            OnQuota();
            OnCaller();
            _micLocked = !MicShouldRun(gs.CurrentStep);
        }
        else BuildDeadAirUi();   // dead air only counts in a real shift: the tutorial has no penalties

        StartSegment();
    }

    public override void _ExitTree()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;
        gs.StepChanged -= OnStep;
        gs.CallerChanged -= OnCaller;
        gs.QuotaChanged -= OnQuota;
    }

    // ----- tutorial hooks -----
    // The mic runs from Go Live until the next caller arrives, and keeps running
    // while the Operator and Technician work on the module (all at the same time).
    private static bool MicShouldRun(TutorialStep step) =>
        step == TutorialStep.GoLive || step == TutorialStep.KeypadModule || step == TutorialStep.Done;

    private void OnStep(TutorialStep step)
    {
        bool locked = !MicShouldRun(step);
        if (locked == _micLocked) return;
        _micLocked = locked;
        StartSegment();   // locked: shows the paused message. unlocked: starts a fresh phrase.
    }

    private void OnCaller()
    {
        var gs = GameSession.Instance;
        if (gs.CallerName == "")
        {
            SetLabel("%NameLabel", "Waiting for a caller...");
            SetLabel("%CallerNumLabel", "No caller yet");
            return;
        }
        // The Broadcaster NEVER sees the story. Only name and number.
        SetLabel("%NameLabel", gs.CallerName);
        SetLabel("%CallerNumLabel", $"Caller {gs.CallerNumber} of {gs.CallerTotal}");
    }

    private void OnQuota() =>
        SetLabel("%QuotaCount", $"{GameSession.Instance.QuotaMet}/{GameSession.Instance.QuotaTarget}");

    private void SetLabel(string path, string text)
    {
        var l = GetNodeOrNull<Label>(path);
        if (l != null) l.Text = text;
    }

    private void OnDecision(bool accept)
    {
        if (_tutorial) GameSession.Instance.Decide(accept);
        else GD.Print(accept ? "ACCEPT pressed" : "REJECT pressed");
    }

    // ----- word match -----
    // newPhrase = true: pick a new random phrase + decoys. false: Try Again repeats the same one.
    private void StartSegment(bool newPhrase = true)
    {
        if (newPhrase || _sentence.Length == 0)
        {
            _sentence = _phrases.Next(longForm: !_tutorial);
            _decoys = _phrases.Decoys(_sentence, 2);
        }
        if (_deadAir.Listeners <= 0f) _deadAir.Reset();
        _deadAir.Register();
        _idx = 0; _timeLeft = SegmentTime; _running = !_micLocked;
        _tryAgain.Disabled = true;
        _status.Text = _micLocked ? LockedMessage() : "Click the words in order.";
        _timer.Text = _tutorial ? "no timer" : "";
        _wordButtons.Clear();
        foreach (Node c in _bank.GetChildren())
            if (c != _tryAgain) c.QueueFree();

        foreach (var w in _sentence.Concat(_decoys).OrderBy(_ => GD.Randi()).ToList())
        {
            var word = w;
            var b = new Button { Text = word, ThemeTypeVariation = "ButtonWord", Disabled = _micLocked };
            b.Pressed += () => OnWord(b, word);
            _bank.AddChild(b);
            _wordButtons[word] = b;
        }
        _bank.MoveChild(_tryAgain, -1);
        Refresh();
    }

    private void OnWord(Button b, string w)
    {
        if (!_running) return;
        _deadAir.Register();   // any word counts as talking, even a wrong one
        if (w == _sentence[_idx])
        {
            _idx++; b.Disabled = true; Refresh();
            if (_idx == _sentence.Length)
            {
                _running = false;
                _status.Text = "✔ ON AIR! Segment complete.";
                if (_tutorial && GameSession.Instance.CurrentStep == TutorialStep.GoLive)
                    GameSession.Instance.Report(TutorialAction.PhraseMatched);
                QueueNextPhrase();   // keep talking: a new random phrase follows (no gap = no dead air)
            }
        }
        else if (_tutorial) _status.Text = "↻ Not that one. Tap the word with ▶. No penalty.";
        else _timeLeft = Mathf.Max(0, _timeLeft - 2);

        GameSession.Instance?.ReportAirtime(-5);
    }

    private string LockedMessage() =>
        GameSession.Instance.CurrentStep < TutorialStep.GoLive
            ? "🎙 The mic unlocks in the Go Live step."
            : "📞 Mic paused: a caller is on the line. Decide first, then keep talking.";

    private async void QueueNextPhrase()
    {
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
        if (_micLocked) return;   // a caller took over the line while we waited
        StartSegment();
    }

    public override void _Process(double delta)
    {
        if (!_running || _tutorial) return;   // tutorial: no countdown, no dead air

        _deadAir.Tick((float)delta);
        UpdateDeadAirUi();
        if (_deadAir.Listeners <= 0f)
        {
            _running = false;
            _status.Text = "📻 The listeners tuned out! Press Try again.";
            _tryAgain.Disabled = false;
            foreach (Node c in _bank.GetChildren())
                if (c is Button wb && wb != _tryAgain) wb.Disabled = true;

            GameSession.Instance?.ReportAirtime(-15);
            return;
        }

        _timeLeft -= delta;
        _timer.Text = $"{Mathf.Max(0, Mathf.CeilToInt(_timeLeft))} s";
        if (_timeLeft > 0) return;
        _running = false;
        _status.Text = "Time is up. Press Try again.";
        _tryAgain.Disabled = false;
        foreach (Node c in _bank.GetChildren())
            if (c is Button bb && bb != _tryAgain) bb.Disabled = true;
    }

    // ----- dead air -----
    private void BuildDeadAirUi()
    {
        var parent = _status.GetParent();
        int at = _status.GetIndex() + 1;

        _deadAirLabel = new Label { Text = "⚠ DEAD AIR ⚠ Say something!", HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _deadAirLabel.AddThemeColorOverride("font_color", new Color("#F44336"));
        _listenerLabel = new Label { Text = "Listeners: 100%", HorizontalAlignment = HorizontalAlignment.Center };
        _listenerBar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 16) };

        foreach (Control c in new Control[] { _deadAirLabel, _listenerLabel, _listenerBar })
        {
            parent.AddChild(c);
            parent.MoveChild(c, at++);
        }
    }

    private void UpdateDeadAirUi()
    {
        if (_listenerBar == null) return;
        _listenerBar.Value = _deadAir.Listeners;
        _listenerLabel.Text = $"Listeners: {Mathf.CeilToInt(_deadAir.Listeners)}%";
        _deadAirLabel.Visible = _deadAir.IsDeadAir;
        if (_deadAir.IsDeadAir)   // flash: the text itself is the warning, not just the color
            _deadAirLabel.Modulate = new Color(1, 1, 1, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin((float)Time.GetTicksMsec() / 1000f * 10f)));
    }

    private void Refresh()
    {
        var parts = _sentence.Select((w, i) => i < _idx ? $"[color=#0b5a2a]{w}[/color]" : w);
        _target.Text = "[center]" + string.Join(" ", parts) + "[/center]";
        _meterLabel.Text = $"ON AIR meter: {_idx} of {_sentence.Length} words";

        int segs = _meter.GetChildCount();
        int lit = _idx * segs / _sentence.Length;
        for (int i = 0; i < segs; i++)
        {
            var seg = (Control)_meter.GetChild(i);
            seg.ThemeTypeVariation = i < lit ? "SegOn" : "SegOff";
            seg.Modulate = i < lit ? Colors.White : new Color(1, 1, 1, 0.35f);   // works without a theme
        }

        // ▶ marks the next word (text marker, not color alone)
        foreach (var kv in _wordButtons)
        {
            bool isNext = _tutorial && !_micLocked && _idx < _sentence.Length && kv.Key == _sentence[_idx];
            kv.Value.Text = isNext ? "▶ " + kv.Key : kv.Key;
        }
    }
}