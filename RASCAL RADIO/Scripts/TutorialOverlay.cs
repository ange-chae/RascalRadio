using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;
using System.Linq;

// Subtitle-style tutorial. Built entirely in code: no .tscn needed.
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
    // true: lines also advance on their own (after the voiceline / reading time). SPACE always skips.
    // false: lines only advance when the player presses SPACE.
    public bool AutoAdvance { get; set; } = false;
    public bool ShowDoneWhen { get; set; } = true;   // unused now; kept so existing role screens still compile

    private const int FontSize = 16;
    private const float SubtitleWidth = 620f;
    private const float PauseAfterLine = 1.2f;   // breathing room after a line ends (auto-advance only)
    private const string VoiceFolder = "res://Audio/Tutorial/";

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

    public override void _Ready()
    {
        var gs = GameSession.Instance;
        if (gs == null || !gs.IsTutorial) { QueueFree(); return; }

        Layer = 20;

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
        };
        _row.AddChild(_gotIt);

        _voice = new AudioStreamPlayer();
        AddChild(_voice);

        _dialogue.LineShown += ShowLine;
        gs.StepChanged += OnStep;
        gs.Feedback += OnFeedback;
        OnStep(gs.CurrentStep);
    }

    public override void _ExitTree()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;
        gs.StepChanged -= OnStep;
        gs.Feedback -= OnFeedback;
    }

    // SPACE / ENTER skips to the next line (never clicks, so it can't fight the game UI).
    public override void _Input(InputEvent e)
    {
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

        _lineTween?.Kill();
        _lineTween = CreateTween();
        if (!first) _lineTween.TweenProperty(_content, "modulate:a", 0f, 0.15);
        else _content.Modulate = new Color(1, 1, 1, 0);
        _lineTween.TweenCallback(Callable.From(() =>
        {
            _speech.Text = line;
            _hint.Text = hintText;
            _gotIt.Visible = showGotIt;
            _row.Visible = hintText != "" || showGotIt;
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
        else if (RoleName == "Broadcaster" && _step == TutorialStep.GoLive)
        {
            // Get out of the Broadcaster's way while they work the prompter.
            _autoTween = CreateTween();
            _autoTween.TweenInterval(duration + 3.0);
            _autoTween.TweenCallback(Callable.From(() => FadeTo(0f, 0.6f)));
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

    private void FadeTo(float alpha, double time)
    {
        _fadeTween?.Kill();
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(_panel, "modulate:a", alpha, time);
    }

    private void OnFeedback(string text, bool success)
    {
        _feedback.Text = text;
        _feedback.Visible = !string.IsNullOrEmpty(text);
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