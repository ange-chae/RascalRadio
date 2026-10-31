using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;
using System.Linq;

// Built entirely in code: no .tscn needed.
// Each role screen does: AddChild(new TutorialOverlay { RoleName = "Operator" });
// A small box at the bottom-center. It never blocks clicks outside the box itself.
public partial class TutorialOverlay : CanvasLayer
{
    [Signal] public delegate void TutorialEndedEventHandler();
    public string RoleName { get; set; } = "";
    public bool ShowDoneWhen { get; set; } = true;

    private const int FontSize = 18;
    private Label _speech, _done, _feedback, _hint;
    private Tween _typeTween;

    private static readonly Color Cream = new("FBEFD9");
    private static readonly Color Ink = new("2B1B2E");
    private static readonly Color Yellow = new("FFD25A");
    private static readonly Color Orange = new("E8A06A");
    private static readonly Color TextCol = new("26194A");
    private Button _gotIt;
    private Control _body;
    private TextureRect _portrait;
    private PanelContainer _tab;

    private const int PortraitSize = 96;
    private static readonly Color Muted = new("7A5C6E");

    private readonly DialogueSequencer _dialogue = new();
    private PanelContainer _bubble;
    private TutorialStep _step;

    public override void _Ready()
    {
        var gs = GameSession.Instance;
        if (gs == null || !gs.IsTutorial) { QueueFree(); return; }

        Layer = 20;

        // Full-screen holder that ignores the mouse, so buttons underneath stay clickable.
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_bottom", 6);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(column);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddThemeConstantOverride("separation", 0);
        column.AddChild(root);

        // ---------- Top row: name tab + hide button ----------
        var tabRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(tabRow);

        // Empty space so the tab lines up with the bubble, not with the picture.
        tabRow.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(PortraitSize + 18, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var tab = _tab = new PanelContainer();
        var tabStyle = Box(Ink, Ink, 12, 0, 0, 6);
        tabStyle.CornerRadiusBottomLeft = 0;
        tabStyle.CornerRadiusBottomRight = 0;
        tabStyle.ContentMarginLeft = 12;
        tabStyle.ContentMarginRight = 14;
        tab.AddThemeStyleboxOverride("panel", tabStyle);
        tabRow.AddChild(tab);
        tab.MouseDefaultCursorShape = Control.CursorShape.PointingHand;

        var tabInner = new HBoxContainer();
        tabInner.AddThemeConstantOverride("separation", 8);
        tab.AddChild(tabInner);

        // Blinking "on air" light
        var dot = new Panel
        {
            CustomMinimumSize = new Vector2(12, 12),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var dotStyle = new StyleBoxFlat { BgColor = new Color("E5484D") };
        dotStyle.SetCornerRadiusAll(6);
        dot.AddThemeStyleboxOverride("panel", dotStyle);
        tabInner.AddChild(dot);
        var blink = CreateTween().SetLoops();
        blink.TweenProperty(dot, "modulate:a", 0.25f, 0.7);
        blink.TweenProperty(dot, "modulate:a", 1.0f, 0.7);

        var nameLbl = new Label { Text = "BOSS", VerticalAlignment = VerticalAlignment.Center };
        nameLbl.AddThemeFontSizeOverride("font_size", FontSize);
        nameLbl.AddThemeColorOverride("font_color", Yellow);
        tabInner.AddChild(nameLbl);

        var practice = new Label { Text = "practice shift, no penalties", VerticalAlignment = VerticalAlignment.Center };
        practice.AddThemeFontSizeOverride("font_size", FontSize - 4);
        practice.AddThemeColorOverride("font_color", Cream);
        tabInner.AddChild(practice);

        // Little bouncing signal bars: the "buzz"
        var meter = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 18),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        meter.AddThemeConstantOverride("separation", 3);
        tabInner.AddChild(meter);
        for (int i = 0; i < 4; i++)
        {
            var bar = new ColorRect
            {
                Color = Orange,
                CustomMinimumSize = new Vector2(4, 5),
                SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
            };
            meter.AddChild(bar);
            var t = CreateTween().SetLoops();
            t.TweenProperty(bar, "custom_minimum_size:y", 16f, 0.22 + 0.07 * i);
            t.TweenProperty(bar, "custom_minimum_size:y", 5f, 0.22 + 0.07 * i);
        }

        tabRow.AddChild(new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        // ---------- Body row: picture | tail | bubble ----------
        var body = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(body);
        _body = body;

        // Picture frame (blank for now)
        var frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(PortraitSize, PortraitSize),
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
        frame.AddThemeStyleboxOverride("panel", Box(new Color("3A2A3E"), Ink, 18, 3, 3, 4));
        body.AddChild(frame);

        _portrait = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
        };
        frame.AddChild(_portrait);
        // ======================= ADD THE BOSS PICTURE HERE =======================
        // Uncomment and point it at your image:
        // _portrait.Texture = GD.Load<Texture2D>("res://Art/boss.png");
        // =========================================================================

        // Bubble tail pointing at the picture
        var tail = new Control
        {
            CustomMinimumSize = new Vector2(18, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var tailPts = new[] { new Vector2(18, 14), new Vector2(4, 26), new Vector2(18, 38) };
        tail.AddChild(new Polygon2D { Polygon = tailPts, Color = Cream });
        tail.AddChild(new Line2D { Points = tailPts, Width = 3, DefaultColor = Ink });
        tail.AddChild(new ColorRect   // hides the bubble border where the tail joins it
        {
            Color = Cream,
            Position = new Vector2(16, 16),
            Size = new Vector2(6, 20),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        body.AddChild(tail);

        // The speech bubble itself
        var bubble = _bubble = new PanelContainer { CustomMinimumSize = new Vector2(640, 0) };
        var bubbleStyle = Box(Cream, Ink, 22, 3, 3, 14);
        bubbleStyle.CornerRadiusTopLeft = 0;   // the name tab attaches here
        bubbleStyle.ShadowColor = new Color(0, 0, 0, 0.28f);
        bubbleStyle.ShadowSize = 6;
        bubbleStyle.ShadowOffset = new Vector2(0, 3);
        bubble.AddThemeStyleboxOverride("panel", bubbleStyle);
        body.AddChild(bubble);

        var bubbleBox = new VBoxContainer();
        bubbleBox.AddThemeConstantOverride("separation", 10);
        bubble.AddChild(bubbleBox);

        _speech = MakeLine(bubbleBox);
        _done = MakeLine(bubbleBox);
        _done.AddThemeFontSizeOverride("font_size", FontSize - 3);
        _done.AddThemeColorOverride("font_color", Muted);
        _feedback = MakeLine(bubbleBox);

        _hint = MakeLine(bubbleBox);
        _hint.Text = "Click anywhere to hide the tutoial";
        _hint.AddThemeFontSizeOverride("font_size", FontSize - 4);
        _hint.AddThemeColorOverride("font_color", Muted);

        _gotIt = new Button { Text = "GOT IT", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        _gotIt.AddThemeFontSizeOverride("font_size", FontSize);
        StyleButton(_gotIt);
        _gotIt.Pressed += () =>
        {
            GameSession.Instance.Report(TutorialAction.Acknowledged);
            _gotIt.Disabled = true;
            _gotIt.Text = "Waiting for the others...";
        };
        bubbleBox.AddChild(_gotIt);

        _dialogue.LineShown += ShowLine;
        gs.StepChanged += OnStep;
        gs.Feedback += OnFeedback;
        OnStep(gs.CurrentStep);

        // IMPORTANT: these three lines must stay at the very end of _Ready.
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

    private static StyleBoxFlat Box(Color bg, Color border, int radius, int side = 4, int bottom = 4, int pad = 8)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        s.SetCornerRadiusAll(radius);
        s.BorderWidthLeft = s.BorderWidthRight = s.BorderWidthTop = side;
        s.BorderWidthBottom = bottom;   // thick bottom = your chunky "button lip"
        s.SetContentMarginAll(pad);
        return s;
    }

    private static StyleBoxFlat Btn(Color bg, int lip, bool compact)
    {
        int vpad = compact ? 3 : 6;
        int hpad = compact ? 10 : 18;
        var s = Box(bg, Ink, 12, 3, lip, 0);
        s.ContentMarginLeft = 3 + hpad;
        s.ContentMarginRight = 3 + hpad;
        s.ContentMarginTop = 3 + vpad + (8 - lip);   // keeps the button the same height when pressed
        s.ContentMarginBottom = lip + vpad;
        return s;
    }

    private static void StyleButton(Button b, bool compact = false)
    {
        b.AddThemeStyleboxOverride("normal", Btn(Yellow, 8, compact));
        b.AddThemeStyleboxOverride("hover", Btn(Yellow.Lightened(0.15f), 8, compact));
        b.AddThemeStyleboxOverride("pressed", Btn(Yellow.Darkened(0.1f), 3, compact));
        b.AddThemeStyleboxOverride("disabled", Btn(new Color("D9CBB0"), 3, compact));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_disabled_color" })
            b.AddThemeColorOverride(c, Ink);
    }

    private static Label MakeLine(VBoxContainer parent)
    {
        var l = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(00, 0),
        };
        l.AddThemeColorOverride("font_color", TextCol);
        parent.AddChild(l);
        return l;
    }
  public override void _Input(InputEvent e)
{
    if (_body == null) return;

    if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
    {
        var p = mb.Position;
        if (_tab.GetGlobalRect().HasPoint(p)) { SetExpanded(!_body.Visible); return; }   // BOSS tab hides/shows
        if (_gotIt.Visible && _gotIt.GetGlobalRect().HasPoint(p)) return;                 // GOT IT still works
        if (_body.Visible && _bubble.GetGlobalRect().HasPoint(p)) Advance();              // click the bubble to continue
        else if (_body.Visible) SetExpanded(false);                                       // NEW: click elsewhere hides it
    }
    else if (e.IsActionPressed("ui_accept") && _body.Visible && !_dialogue.IsLastLine)
    {
        Advance();
        GetViewport().SetInputAsHandled();
    }
}

    private void Advance()
    {
        // first click finishes the typing, second click goes to the next sentence
        if (_speech.VisibleRatio < 1f)
        {
            _typeTween?.Kill();
            _speech.VisibleRatio = 1f;
            return;
        }
        _dialogue.Advance();
    }

    private void SetExpanded(bool open)
    {
        _body.Visible = open;
    }

    private void OnStep(TutorialStep step)
    {
        _step = step;
        _done.Text = "Done when: " + TutorialText.DoneWhen(step);
        _feedback.Text = "";
        if (step == TutorialStep.MeetStudio) { _gotIt.Disabled = false; _gotIt.Text = "GOT IT"; }
        SetExpanded(true);

        var lines = TutorialText.HappeningLines(step);
        if (step != TutorialStep.Done)
            lines = lines.Append(TutorialText.Job(step, RoleName)).ToArray();
        _dialogue.Start(lines);
    }

    private void ShowLine(string line)
    {
        bool last = _dialogue.IsLastLine;
        bool ending = last && _step == TutorialStep.Done;

        _speech.Text = line;
        _hint.Text = ending ? "" : last
            ? "Click anywhere to hide this"
            : "Click here or press SPACE to continue ▼   (click elsewhere to hide)";
        _done.Visible = last && ShowDoneWhen && _step != TutorialStep.Done;
        _gotIt.Visible = last && _step == TutorialStep.MeetStudio;

        _typeTween?.Kill();
        _speech.VisibleRatio = 0;
        _typeTween = CreateTween();
        _typeTween.TweenProperty(_speech, "visible_ratio", 1.0, line.Length * 0.02);

        if (last && RoleName == "Broadcaster" && _step == TutorialStep.GoLive)   // optional, from before
            GetTree().CreateTimer(4.0).Timeout += () => SetExpanded(false);
        if (ending)
        {
            GetTree().CreateTimer(line.Length * 0.02 + 2.5).Timeout += () =>
            {
                if (!IsInstanceValid(this)) return;
                SetExpanded(false);
                EmitSignal(SignalName.TutorialEnded);
                var title = LevelRegistry.Get(GameSession.Instance.PendingLevelId)?.Title ?? "LEVEL 1";
                GetTree().Root.AddChild(new LevelTitleCard { Title = title, Delay = 1.5f, ReportWhenDone = true });
            };
        }
    }

    private void OnFeedback(string text, bool success) => _feedback.Text = text;
}