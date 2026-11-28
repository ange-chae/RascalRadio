using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;
using System.Collections.Generic;
using System.Linq;

// Subtitle-style tutorial. Built entirely in code: no .tscn needed.
//
// SPOTLIGHT: while a step is active the screen is dimmed and the UI element the player needs
// is framed by a pulsing outline. The highlight follows the tutorial step by itself and slides
// between targets. Which element each role highlights on each step is decided in Targets() below.
// The dim never blocks the mouse (unless BlockOutsideClicks is set), so the highlighted element
// stays fully clickable.
// Each role screen does: AddChild(new TutorialOverlay { RoleName = "Operator" });
//
// VOICELINES (optional): drop audio files at
//   res://Audio/Tutorial/{StepName}_{lineIndex}.ogg      e.g. MeetStudio_0.ogg, MeetStudio_1.ogg
// The LAST line of each step is the role-specific job, so it uses a role suffix:
//   res://Audio/Tutorial/{StepName}_job_{RoleName}.ogg   e.g. MeetStudio_job_Operator.ogg
// If a file exists it plays and the subtitle advances when it ends.
// If not, the subtitle advances after a reading-time delay. Nothing else to change.
public partial class TutorialOverlay : CanvasLayer
{
    [Signal] public delegate void TutorialEndedEventHandler();
    public string RoleName { get; set; } = "";
    // true: lines advance on their own (after the voiceline / reading time). SPACE always skips ahead.
    // false: lines only advance when the player presses SPACE.
    public bool AutoAdvance { get; set; } = true;
    public bool ShowDoneWhen { get; set; } = true;   // unused now; kept so existing role screens still compile

    // ---- spotlight settings ----
    public bool SpotlightEnabled { get; set; } = true;
    public float DimAmount { get; set; } = 0.68f;          // 0 = no dim, 1 = black
    public float DimHoldSeconds { get; set; } = 3f;        // how long the dim stays when a new task starts, then it fades away
    public float PulseSeconds { get; set; } = 3f;          // how long the outline pulses for a new task, then it stays steady
    public float HighlightPadding { get; set; } = 8f;      // space between the element and its outline
    public bool BlockOutsideClicks { get; set; } = false;  // true: clicks outside the spotlight are swallowed

    private const int FontSize = 16;
    private const float SubtitleWidth = 620f;
    private const float PauseAfterLine = 1.2f;   // breathing room after a line ends (auto-advance only)
    private const string VoiceFolder = "res://Audio/Tutorial/";
    private const int SubtitleLayer = 22;     // above the Operator's module popup (layer 20)
    private const int SpotlightLayer = 21;    // dim + outline, just under the subtitles
    private const int MaxHoles = 3;
    private const float HoleCorner = 12f;
    private const float CloseDelay = 1.5f;   // the dialogue closes this long after it finishes

    private static readonly Color Cream = new("FBEFD9");
    private static readonly Color Ink = new("2B1B2E");
    private static readonly Color Yellow = new("FFD25A");

    private readonly DialogueSequencer _dialogue = new();
    private PanelContainer _panel;
    private Label _speaker, _speech, _feedback, _hint;
    private Button _gotIt;
    private Control _row;
    private AudioStreamPlayer _voice;
    private Tween _autoTween, _fadeTween, _lineTween;
    private Control _content;
    private Font _font;
    private TutorialStep _step;
    private int _lineIndex;

    // ---- spotlight state ----
    private CanvasLayer _spotLayer;
    private ColorRect _dim;
    private ShaderMaterial _dimMat;
    private readonly Panel[] _frames = new Panel[MaxHoles];
    private readonly StyleBoxFlat[] _frameStyles = new StyleBoxFlat[MaxHoles];
    private readonly Rect2[] _cur = new Rect2[MaxHoles];     // what is drawn (eased)
    private readonly Rect2[] _goal = new Rect2[MaxHoles];    // where it should be
    private readonly bool[] _active = new bool[MaxHoles];
    private readonly Dictionary<string, Control> _targetCache = new();
    private readonly Dictionary<string, double> _retryAt = new();
    private float _spotAlpha, _dimAlpha, _fadeGoal = 1f;
    private float _burstLeft, _pulseLeft, _pulseAmt;   // "new task" attention burst timers
    private readonly HashSet<string> _mentioned = new();   // highlights the dialogue has mentioned so far in this step
    private List<(string Key, string[] Words, System.Func<Control[]> Get)> _rules = new();
    private static readonly string[] CallerKeys = { "caller", "topic", "decide", "callerpanel" };
    private int _lastCallerNum;
    private readonly HashSet<string> _reacted = new(); // highlights the player already reacted to: they disappear
    private readonly string[] _slotKey = new string[MaxHoles];

    // ---- dialogue hide / recall ----
    private Button _recall;
    private bool _collapsed, _recalled, _rowWanted;
    private Tween _closeTween, _peekTween;
    private string[] _stepLines = System.Array.Empty<string>();
    private double _clock;
    private bool _hasStep, _gotItFocus;

    private const string DimShader = @"
shader_type canvas_item;
uniform vec2 screen_size = vec2(1280.0, 720.0);
uniform vec4 hole0 = vec4(0.0);
uniform vec4 hole1 = vec4(0.0);
uniform vec4 hole2 = vec4(0.0);
uniform float radius = 12.0;
uniform float dim = 0.72;
uniform vec4 dim_color : source_color = vec4(0.169, 0.106, 0.18, 1.0);

float box(vec2 p, vec4 h) {
    if (h.z < 1.0 || h.w < 1.0) return 100000.0;
    vec2 hs = h.zw * 0.5;
    float r = min(radius, min(hs.x, hs.y));
    vec2 q = abs(p - (h.xy + hs)) - hs + vec2(r);
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
    vec2 p = UV * screen_size;
    float d = min(box(p, hole0), min(box(p, hole1), box(p, hole2)));
    float a = smoothstep(-1.5, 1.5, d) * dim;
    COLOR = vec4(dim_color.rgb, a);
}";

    public override void _Ready()
    {
        var gs = GameSession.Instance;
        if (gs == null || !gs.IsTutorial) { QueueFree(); return; }

        Layer = SubtitleLayer;
        if (SpotlightEnabled) BuildSpotlight();

        // Font: assets/fonts/ShortStack-Regular (falls back to the default font if missing)
        foreach (var path in new[] { "res://assets/fonts/ShortStack-Regular.ttf", "res://assets/fonts/ShortStack-Regular.otf" })
            if (ResourceLoader.Exists(path)) { _font = GD.Load<Font>(path); break; }
        if (_font == null) GD.PushWarning("TutorialOverlay: ShortStack-Regular not found in res://assets/fonts/");

        // Full-screen holder that ignores the mouse, so buttons underneath stay clickable.
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_bottom", 24);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(column);

        // Dark translucent strip, like movie subtitles
        _panel = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var style = new StyleBoxFlat { BgColor = new Color(Ink, 0.72f) };
        style.SetCornerRadiusAll(8);
        style.ContentMarginLeft = style.ContentMarginRight = 16;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 8;
        _panel.AddThemeStyleboxOverride("panel", style);
        column.AddChild(_panel);

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        _panel.AddChild(box);
        _content = box;   // faded in/out between lines

        // Speaker name (small, above the line)
        _speaker = new Label { Text = "BOSS", HorizontalAlignment = HorizontalAlignment.Center };
        if (_font != null) _speaker.AddThemeFontOverride("font", _font);
        _speaker.AddThemeFontSizeOverride("font_size", FontSize - 4);
        _speaker.AddThemeColorOverride("font_color", Yellow);
        box.AddChild(_speaker);

        // The subtitle line itself
        _speech = MakeLabel(FontSize, Cream);
        box.AddChild(_speech);

        // Success/failure feedback, smaller
        _feedback = MakeLabel(FontSize - 2, Yellow);
        _feedback.Visible = false;
        box.AddChild(_feedback);

        // Slim row: hint on the left, GOT IT on the right (only when needed)
        _row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddChild(_row);

        _hint = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (_font != null) _hint.AddThemeFontOverride("font", _font);
        _hint.AddThemeFontSizeOverride("font_size", FontSize - 4);
        _hint.AddThemeColorOverride("font_color", new Color(Cream, 0.55f));
        _row.AddChild(_hint);

        _gotIt = new Button { Text = "GOT IT" };
        if (_font != null) _gotIt.AddThemeFontOverride("font", _font);
        _gotIt.AddThemeFontSizeOverride("font_size", FontSize - 3);
        StyleButton(_gotIt);
        _gotIt.Pressed += () =>
        {
            GameSession.Instance.Report(TutorialAction.Acknowledged);
            _gotIt.Disabled = true;
            _gotIt.Text = "GOT IT ✔";
            foreach (var r in _rules) _reacted.Add(r.Key);
            _reacted.Add("gotit");
            ScheduleCollapse(CloseDelay);
        };
        _row.AddChild(_gotIt);

        // Button that brings the dialogue back. Sits bottom-centre under the dialogue strip, same look as the strip.
        _recall = new Button { Text = "DIALOGUE", Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        if (_font != null) _recall.AddThemeFontOverride("font", _font);
        _recall.AddThemeFontSizeOverride("font_size", FontSize - 4);
        StyleRecall(_recall);
        column.AddChild(_recall);
        _recall.Pressed += OnRecallPressed;

        _voice = new AudioStreamPlayer { Bus = AudioSettingsManager.SfxBusName };
        AddChild(_voice);

        _dialogue.LineShown += ShowLine;
        gs.StepChanged += OnStep;
        gs.Feedback += OnFeedback;
        gs.QuotaChanged += OnQuotaChanged;
        gs.CallerChanged += OnCallerChanged;
        _lastCallerNum = gs.CallerNumber;
        OnStep(gs.CurrentStep);
    }

    public override void _ExitTree()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;
        gs.StepChanged -= OnStep;
        gs.Feedback -= OnFeedback;
        gs.QuotaChanged -= OnQuotaChanged;
        gs.CallerChanged -= OnCallerChanged;
    }

    // SPACE / ENTER skips to the next line (never clicks, so it can't fight the game UI).
    public override void _Input(InputEvent e)
    {
        if (_dim != null) DetectReaction(e);
        if (BlockOutsideClicks && _dim != null && _dimAlpha > 0.5f && e is InputEventMouseButton
            && !OverSpotlight(GetViewport().GetMousePosition()))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_panel == null || !_panel.Visible) return;
        if (e.IsActionPressed("ui_accept") && !_dialogue.IsLastLine)
        {
            Advance();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Advance()
    {
        _autoTween?.Kill();
        _voice.Stop();
        _dialogue.Advance();
    }

    private void OnStep(TutorialStep step)
    {
        _step = step;
        _hasStep = true;
        _closeTween?.Kill(); _peekTween?.Kill();
        _recalled = false;
        SetCollapsed(false);
        _reacted.RemoveWhere(k => k != "task" && k != "prompter");   // those two stay dismissed for the whole tutorial
        _gotItFocus = false;
        _burstLeft = 0f; _pulseLeft = 0f;   // the burst starts when the dialogue mentions something
        _mentioned.Clear();
        _rules = Rules(step);
        _lineIndex = 0;

        _feedback.Text = "";
        _feedback.Visible = false;
        if (step == TutorialStep.MeetStudio) { _gotIt.Disabled = false; _gotIt.Text = "GOT IT"; }
        FadeTo(1f, 0.15f);

        var lines = TutorialText.HappeningLines(step);
        if (step == TutorialStep.PracticeCaller)
            lines = lines.Append($"Today's topic is {GameSession.Instance.Topic}. Only accept callers whose story fits it.").ToArray();
        if (step != TutorialStep.Done)
            lines = lines.Append(TutorialText.Job(step, RoleName)).ToArray();
        _stepLines = lines;
        _dialogue.Start(lines);
    }

    private void ShowLine(string line)
    {
        bool last = _dialogue.IsLastLine;
        bool ending = last && _step == TutorialStep.Done;
        bool isJob = last && _step != TutorialStep.Done;

        // Subtitles fade out, swap text, fade in (no typewriter, so they match the voice).
        bool first = _lineIndex == 0;
        string hintText = ending || last ? "" : AutoAdvance ? "SPACE to skip" : "SPACE to continue";
        bool showGotIt = last && _step == TutorialStep.MeetStudio;
        _gotItFocus = showGotIt;   // spotlight also frames the GOT IT button
        if (showGotIt) { _burstLeft = DimHoldSeconds; _pulseLeft = PulseSeconds; }

        // A highlight only appears once the dialogue talks about it.
        string lower = line.ToLowerInvariant();
        foreach (var r in _rules)
        {
            if (_mentioned.Contains(r.Key) || !r.Words.Any(w => lower.Contains(w))) continue;
            _mentioned.Add(r.Key);
            if (!_reacted.Contains(r.Key)) { _burstLeft = DimHoldSeconds; _pulseLeft = PulseSeconds; }
        }

        _lineTween?.Kill();
        _lineTween = CreateTween();
        if (!first) _lineTween.TweenProperty(_content, "modulate:a", 0f, 0.15);
        else _content.Modulate = new Color(1, 1, 1, 0);
        _lineTween.TweenCallback(Callable.From(() =>
        {
            _speech.Text = line;
            _hint.Text = hintText;
            _gotIt.Visible = showGotIt;
            _rowWanted = hintText != "" || showGotIt;
            _row.Visible = !_collapsed && _rowWanted;
        }));
        _lineTween.TweenProperty(_content, "modulate:a", 1f, 0.25);

        // Voiceline if one exists, otherwise a reading-time delay.
        float duration = PlayVoice(isJob ? $"{_step}_job_{RoleName}" : $"{_step}_{_lineIndex}");
        if (duration <= 0f) duration = ReadTime(line);
        _lineIndex++;

        _autoTween?.Kill();
        if (!last && AutoAdvance)
        {
            // Auto-advance to the next subtitle when the line is over.
            _autoTween = CreateTween();
            _autoTween.TweenInterval(duration + PauseAfterLine);
            _autoTween.TweenCallback(Callable.From(() => { if (IsInstanceValid(this)) _dialogue.Advance(); }));
        }
        else if (ending)
        {
            GetTree().CreateTimer(duration + 2.0).Timeout += () =>
            {
                if (!IsInstanceValid(this)) return;
                FadeTo(0f, 0.4f);
                EmitSignal(SignalName.TutorialEnded);
                var title = LevelRegistry.Get(GameSession.Instance.PendingLevelId)?.Title ?? "LEVEL 1";
                GetTree().Root.AddChild(new LevelTitleCard { Title = title, Delay = 1.5f, ReportWhenDone = true });
            };
        }
        else if (last && !showGotIt)
        {
            // the instruction has been given: close the dialogue shortly after it finishes
            ScheduleCollapse(duration + CloseDelay);
        }
    }

    // Returns the voiceline length in seconds, or 0 if there is no audio file.
    private float PlayVoice(string name)
    {
        _voice.Stop();
        string path = $"{VoiceFolder}{name}.ogg";
        if (!ResourceLoader.Exists(path)) return 0f;
        var stream = GD.Load<AudioStream>(path);
        if (stream == null) return 0f;
        _voice.Stream = stream;
        _voice.Play();
        return (float)stream.GetLength();
    }

    private static float ReadTime(string line) => Mathf.Max(3.5f, line.Length * 0.09f + 1.5f);

    private void FadePanel(float alpha, double time)   // subtitles only, the spotlight is not touched
    {
        _fadeTween?.Kill();
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(_panel, "modulate:a", alpha, time);
    }

    private void ScheduleCollapse(double seconds)
    {
        _closeTween?.Kill();
        _closeTween = CreateTween();
        _closeTween.TweenInterval(seconds);
        _closeTween.TweenCallback(Callable.From(() => { if (IsInstanceValid(this)) { _recalled = false; SetCollapsed(true); } }));
    }

    // collapsed = dialogue hidden, only the DIALOGUE button is left. peek = show just the feedback line for a moment.
    private void SetCollapsed(bool collapsed, bool peek = false)
    {
        _collapsed = collapsed;
        _speaker.Visible = _speech.Visible = !collapsed;
        _row.Visible = !collapsed && _rowWanted;
        _recall.Visible = collapsed || _recalled;
        _recall.Text = collapsed ? "DIALOGUE" : "HIDE";
        FadePanel(collapsed && !peek ? 0f : 1f, collapsed ? 0.4 : 0.2);
    }

    private void OnRecallPressed()
    {
        if (_collapsed)
        {
            _recalled = true;
            _peekTween?.Kill();
            _speech.Text = string.Join("\n", _stepLines);   // the whole dialogue of this step, same wording
            _hint.Text = "";
            _content.Modulate = Colors.White;
            SetCollapsed(false);
        }
        else { _recalled = false; SetCollapsed(true); }
    }

    private void FadeTo(float alpha, double time)
    {
        _fadeGoal = alpha;   // the spotlight fades together with the subtitles
        _fadeTween?.Kill();
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(_panel, "modulate:a", alpha, time);
    }

    // =====================================================================
    // SPOTLIGHT
    // =====================================================================
    private void BuildSpotlight()
    {
        _spotLayer = new CanvasLayer { Layer = SpotlightLayer };
        AddChild(_spotLayer);

        _dimMat = new ShaderMaterial { Shader = new Shader { Code = DimShader } };
        _dim = new ColorRect { Color = Colors.White, Material = _dimMat, MouseFilter = Control.MouseFilterEnum.Ignore };
        _spotLayer.AddChild(_dim);
        _dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _dimMat.SetShaderParameter("radius", HoleCorner);
        _dimMat.SetShaderParameter("dim", 0f);

        for (int i = 0; i < MaxHoles; i++)
        {
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0),
                BorderColor = new Color(1f, 1f, 1f, 0.6f),
                ShadowColor = new Color(1f, 1f, 1f, 0.3f),
                ShadowSize = 8,
            };
            style.SetBorderWidthAll(4);
            style.SetCornerRadiusAll((int)HoleCorner);
            var frame = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            frame.AddThemeStyleboxOverride("panel", style);
            _spotLayer.AddChild(frame);
            _frames[i] = frame;
            _frameStyles[i] = style;
        }
    }

    public override void _Process(double delta)
    {
        if (_dim == null || !IsInstanceValid(_dim)) return;
        float dt = (float)delta;
        _clock += delta;

        // 1. where should the holes be right now?
        var groups = _hasStep ? Targets(_step) : new List<(string Key, Control[] Cs)>();
        for (int i = 0; i < MaxHoles; i++)
        {
            if (i < groups.Count)
            {
                _slotKey[i] = groups[i].Key;
                Rect2 r = Bounds(groups[i].Cs).Grow(HighlightPadding);
                _goal[i] = r;
                if (!_active[i]) { _cur[i] = new Rect2(r.GetCenter(), Vector2.Zero); _active[i] = true; }   // grows out of its centre
            }
            else
            {
                _active[i] = false;
                _goal[i] = new Rect2(_cur[i].GetCenter(), Vector2.Zero);                                   // shrinks into its centre
            }
        }

        // 2. ease towards them (this is what makes the highlight slide between steps)
        float k = 1f - Mathf.Exp(-14f * dt);
        for (int i = 0; i < MaxHoles; i++)
        {
            _cur[i] = new Rect2(_cur[i].Position.Lerp(_goal[i].Position, k), _cur[i].Size.Lerp(_goal[i].Size, k));
            if (!_active[i] && _cur[i].Size.X < 2f) _cur[i] = new Rect2(_cur[i].Position, Vector2.Zero);
        }

        // 3. attention burst: a NEW task (new step, or the target changed, e.g. the module opened)
        //    dims the screen and pulses the outline for a few seconds. After that only a calm outline stays.
        if (groups.Count > 0) { _burstLeft = Mathf.Max(0f, _burstLeft - dt); _pulseLeft = Mathf.Max(0f, _pulseLeft - dt); }

        float outlineGoal = groups.Count > 0 ? _fadeGoal : 0f;
        float dimGoal = groups.Count > 0 && _burstLeft > 0f ? _fadeGoal : 0f;
        _spotAlpha = Mathf.MoveToward(_spotAlpha, outlineGoal, dt / 0.35f);
        _dimAlpha = Mathf.MoveToward(_dimAlpha, dimGoal, dt / (dimGoal > _dimAlpha ? 0.35f : 0.8f));   // fades out slowly
        _pulseAmt = Mathf.MoveToward(_pulseAmt, _pulseLeft > 0f ? 1f : 0f, dt / 0.6f);
        float a = _spotAlpha * _spotAlpha * (3f - 2f * _spotAlpha);          // smoothstep
        float d = _dimAlpha * _dimAlpha * (3f - 2f * _dimAlpha);

        // 4. draw
        _dimMat.SetShaderParameter("screen_size", _dim.Size);
        _dimMat.SetShaderParameter("dim", DimAmount * d);
        float wave = 0.5f + 0.5f * Mathf.Sin((float)_clock * 5f);
        float pulse = Mathf.Lerp(0.3f, wave, _pulseAmt);                      // steady once the burst is over
        for (int i = 0; i < MaxHoles; i++)
        {
            var r = _cur[i];
            _dimMat.SetShaderParameter($"hole{i}", new Vector4(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y));

            bool show = a > 0.01f && r.Size.X >= 2f;
            _frames[i].Visible = show;
            if (!show) continue;
            _frames[i].Position = r.Position;
            _frames[i].Size = r.Size;
            _frames[i].Modulate = new Color(1, 1, 1, a * (0.55f + 0.2f * pulse));
            _frameStyles[i].ShadowSize = 3 + (int)(5f * pulse);
        }
    }

    // What can be pointed at. Each rule: a key, the words that make the dialogue "mention" it, and the control(s) to frame.
    // A highlight shows up when a line of the step mentions it, and goes away once the player reacted to it
    // (clicked it, pressed WASD for the prompter, pressed GOT IT) or the step ends. Keys "task" and "prompter"
    // stay dismissed for the whole tutorial.
    private List<(string Key, string[] Words, System.Func<Control[]> Get)> Rules(TutorialStep s)
    {
        var r = new List<(string Key, string[] Words, System.Func<Control[]> Get)>();
        if (s == TutorialStep.Done) return r;

        void R(string key, string[] words, System.Func<Control[]> get) => r.Add((key, words, get));
        Control Prompter() => Find("TargetLabel") ?? Find("WordArea");   // where the WASD words show up

        R("task", new[] { "task list", "task box" }, () => new[] { Find("Task") });

        if (s == TutorialStep.MeetStudio)
        {
            switch (RoleName)
            {
                case "Broadcaster": R("look-prompter", new[] { "prompter" }, () => new[] { Prompter() }); break;
                case "Operator": R("desk", new[] { "studio desk" }, () => new[] { Find("Slot1Btn"), Find("Slot2Btn") }); break;
                case "Technician": R("manual", new[] { "manual" }, () => new[] { Find("ManualBtn") }); break;
            }
            return r;
        }

        bool callerStep = s == TutorialStep.PracticeCaller || s == TutorialStep.SuspiciousCaller;
        switch (RoleName)
        {
            case "Broadcaster":
                if (callerStep)
                {
                    R("caller", new[] { "caller" }, () => new[] { Find("Caller") ?? Find("CallerPanel") });
                    R("topic", new[] { "topic" }, () => new[] { Find("Topic") });
                    R("decide", new[] { "accept", "reject" }, () => new[] { Find("AcceptBtn"), Find("RejectBtn") });
                }
                else if (s == TutorialStep.GoLive || s == TutorialStep.KeypadModule) R("prompter", new[] { "prompter" }, () => new[] { Prompter() });
                else if (s == TutorialStep.PianoModule) R("cue", new[] { "cue sheet" }, () => new[] { Find("CueSheet") });
                break;

            case "Operator":
                if (callerStep) R("callerpanel", new[] { "caller" }, () => new[] { Find("CallerPanel") });
                else if (s == TutorialStep.KeypadModule) R("module", new[] { "incoming line", "keypad", "route" }, () => new[] { OpenOr("ModulePanel", "Slot1Btn") });
                else if (s == TutorialStep.PianoModule) R("module", new[] { "piano" }, () => new[] { OpenOr("ModulePanel", "Slot2Btn") });
                break;

            case "Technician":
                if (s == TutorialStep.KeypadModule || s == TutorialStep.PianoModule)
                    R("manual", new[] { "manual" }, () => new[] { OpenOr("Popup", "ManualBtn") });
                break;
        }
        return r;
    }

    private List<(string Key, Control[] Cs)> Targets(TutorialStep s)
    {
        var groups = new List<(string Key, Control[] Cs)>();
        if (s == TutorialStep.Done) return groups;

        foreach (var rule in _rules)
        {
            if (!_mentioned.Contains(rule.Key) || _reacted.Contains(rule.Key) || groups.Count >= MaxHoles) continue;
            var visible = rule.Get().Where(c => c != null && IsInstanceValid(c) && c.IsVisibleInTree()).ToArray();
            if (visible.Length > 0) groups.Add((rule.Key, visible));
        }
        if (_gotItFocus && !_reacted.Contains("gotit") && groups.Count < MaxHoles && _panel != null) groups.Add(("gotit", new Control[] { _panel }));
        return groups;
    }

    // The caller was accepted: the caller highlights are resolved.
    private void OnQuotaChanged()
    {
        foreach (var k in CallerKeys) _reacted.Add(k);
    }

    // The next caller comes in: those highlights are a new task again.
    private void OnCallerChanged()
    {
        int n = GameSession.Instance?.CallerNumber ?? 0;
        if (n > 0 && n != _lastCallerNum)
        {
            foreach (var k in CallerKeys) _reacted.Remove(k);
            _burstLeft = DimHoldSeconds; _pulseLeft = PulseSeconds;
        }
        _lastCallerNum = n;
    }

    private void DetectReaction(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            var p = GetViewport().GetMousePosition();
            for (int i = 0; i < MaxHoles; i++)
                if (_active[i] && _slotKey[i] != null && _slotKey[i] != "prompter" && _cur[i].HasPoint(p))
                    _reacted.Add(_slotKey[i]);
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } k
                 && k.PhysicalKeycode is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Left or Key.Down or Key.Right)
        {
            for (int i = 0; i < MaxHoles; i++)
                if (_active[i] && _slotKey[i] == "prompter") _reacted.Add("prompter");   // the player is using the prompter
        }
    }

    private Control OpenOr(string openKey, string fallbackKey)
    {
        var open = Find(openKey);
        return open != null && open.IsVisibleInTree() ? open : Find(fallbackKey);
    }

    // Looks a node up on the role screen (this overlay's parent): unique name (%Key) first, then by plain name.
    // Results are cached, and misses are only retried a few times per second.
    private Control Find(string key)
    {
        if (_targetCache.TryGetValue(key, out var cached))
        {
            if (IsInstanceValid(cached) && cached.IsInsideTree()) return cached;
            _targetCache.Remove(key);
        }
        if (_retryAt.TryGetValue(key, out var t) && _clock < t) return null;
        _retryAt[key] = _clock + 0.25;

        var screen = GetParent();
        if (screen == null) return null;

        Control found = null;
        switch (key)
        {
            case "Task":   // the whole Task card, not just its text label
                var label = Find("TaskLabel");
                if (label != null)
                {
                    Node n = label.GetParent();
                    while (n != null && n.Name != "Task") n = n.GetParent();
                    found = n as Control ?? label;
                }
                break;

            case "CueSheet":   // Broadcaster's cue sheet desk panel (name it "CueSheet" in code, or it is found by its ZIndex)
                found = screen.FindChild("CueSheet", true, false) as Control
                    ?? screen.GetChildren().OfType<PanelContainer>().FirstOrDefault(p => p.ZIndex == 5 && p.Visible);
                break;

            default:
                found = screen.GetNodeOrNull<Control>("%" + key) ?? screen.FindChild(key, true, false) as Control;
                break;
        }

        if (found != null) _targetCache[key] = found;
        return found;
    }

    // Bounding box of the controls on screen (respects scale and rotation, like the cue sheet's scale).
    private static Rect2 Bounds(Control[] controls)
    {
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        foreach (var c in controls)
        {
            var t = c.GetGlobalTransformWithCanvas();
            foreach (var corner in new[] { Vector2.Zero, new Vector2(c.Size.X, 0), c.Size, new Vector2(0, c.Size.Y) })
            {
                var p = t * corner;
                min = new Vector2(Mathf.Min(min.X, p.X), Mathf.Min(min.Y, p.Y));
                max = new Vector2(Mathf.Max(max.X, p.X), Mathf.Max(max.Y, p.Y));
            }
        }
        return new Rect2(min, max - min);
    }

    // For BlockOutsideClicks: is this point inside a spotlight hole (or the subtitle box)?
    private bool OverSpotlight(Vector2 p)
    {
        for (int i = 0; i < MaxHoles; i++)
            if (_cur[i].Size.X >= 2f && _cur[i].HasPoint(p)) return true;
        return _panel != null && _panel.Visible && Bounds(new Control[] { _panel }).HasPoint(p);
    }

    private void OnFeedback(string text, bool success)
    {
        _feedback.Text = text;
        _feedback.Visible = !string.IsNullOrEmpty(text);
        if (_collapsed && !string.IsNullOrEmpty(text))   // the dialogue is closed but the player still needs to see feedback
        {
            SetCollapsed(true, peek: true);
            _peekTween?.Kill();
            _peekTween = CreateTween();
            _peekTween.TweenInterval(4.0);
            _peekTween.TweenCallback(Callable.From(() => { if (IsInstanceValid(this) && _collapsed) SetCollapsed(true); }));
        }
    }

    private Label MakeLabel(int size, Color color)
    {
        var l = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(SubtitleWidth, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (_font != null) l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", Ink);
        l.AddThemeConstantOverride("outline_size", 3);
        return l;
    }

    // The DIALOGUE button looks like the dialogue strip: same dark translucent box, cream text.
    private static void StyleRecall(Button b)
    {
        StyleBoxFlat Box(float alpha)
        {
            var st = new StyleBoxFlat { BgColor = new Color(Ink, alpha) };
            st.SetCornerRadiusAll(8);
            st.ContentMarginLeft = st.ContentMarginRight = 16;
            st.ContentMarginTop = 4;
            st.ContentMarginBottom = 5;
            return st;
        }
        b.AddThemeStyleboxOverride("normal", Box(0.72f));
        b.AddThemeStyleboxOverride("hover", Box(0.85f));
        b.AddThemeStyleboxOverride("pressed", Box(0.95f));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", Cream);
        b.AddThemeColorOverride("font_hover_color", Yellow);
        b.AddThemeColorOverride("font_pressed_color", Yellow);
    }

    private static StyleBoxFlat Btn(Color bg, int lip)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = Ink };
        s.SetCornerRadiusAll(8);
        s.BorderWidthLeft = s.BorderWidthRight = s.BorderWidthTop = 3;
        s.BorderWidthBottom = lip;
        s.ContentMarginLeft = s.ContentMarginRight = 10;
        s.ContentMarginTop = 1 + (6 - lip);   // keeps the button the same height when pressed
        s.ContentMarginBottom = lip;
        return s;
    }

    private static void StyleButton(Button b)
    {
        b.AddThemeStyleboxOverride("normal", Btn(Yellow, 6));
        b.AddThemeStyleboxOverride("hover", Btn(Yellow.Lightened(0.15f), 6));
        b.AddThemeStyleboxOverride("pressed", Btn(Yellow.Darkened(0.1f), 3));
        b.AddThemeStyleboxOverride("disabled", Btn(new Color("D9CBB0"), 3));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_disabled_color" })
            b.AddThemeColorOverride(c, Ink);
    }
}