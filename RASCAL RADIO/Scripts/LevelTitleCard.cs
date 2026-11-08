using Godot;

// Title card between levels. Same public API as before (Title, Delay, Hold, ReportWhenDone).
// Transition: the screen cuts to radio static, settles to black, then the title fades in
// with a yellow underline that wipes across. Fades out again when the next level has loaded.
public partial class LevelTitleCard : CanvasLayer
{
    public string Title { get; set; } = "LEVEL 1";
    public float Delay { get; set; } = 0f;           // quiet pause before the card fades in
    public float Hold { get; set; } = 2.5f;          // how long it stays after fading in
    public bool ReportWhenDone { get; set; } = false; // true when this card leads into a level

    private static readonly Color Cream = new("FBEFD9");
    private static readonly Color Ink = new("2B1B2E");
    private static readonly Color Yellow = new("FFD25A");
    private const int BarCount = 16;

    private ColorRect _dim, _line;
    private Control _lineHolder;
    private Label _label, _tag;
    private Control _static;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        Layer = 50;
        _rng.Randomize();

        // Font: assets/fonts/ShortStack-Regular (falls back to the default font if missing)
        Font font = null;
        foreach (var path in new[] { "res://assets/fonts/ShortStack-Regular.ttf", "res://assets/fonts/ShortStack-Regular.otf" })
            if (ResourceLoader.Exists(path)) { font = GD.Load<Font>(path); break; }
        if (font == null) GD.PushWarning("LevelTitleCard: ShortStack-Regular not found in res://assets/fonts/");

        // solid black, so the tutorial screen doesn't show through
        _dim = new ColorRect { Color = new Color(Ink.Darkened(0.5f), 1f), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_dim);
        _dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // the static layer: a bunch of thin bars that jump around for a moment
        _static = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        AddChild(_static);
        _static.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        for (int i = 0; i < BarCount; i++)
            _static.AddChild(new ColorRect { Color = Cream, MouseFilter = Control.MouseFilterEnum.Ignore });

        // title + underline + small tag, stacked in the middle
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 10);
        center.AddChild(col);

        _label = MakeLabel(Title, 84, Cream, font);
        col.AddChild(_label);

        // holder fills the column (= the title's width), so the line lines up with the title
        _lineHolder = new Control
        {
            CustomMinimumSize = new Vector2(0, 6),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        col.AddChild(_lineHolder);
        _line = new ColorRect { Color = Yellow, Size = new Vector2(0, 6), MouseFilter = Control.MouseFilterEnum.Ignore };
        _lineHolder.AddChild(_line);

        _tag = MakeLabel("RASCAL RADIO", 20, Yellow, font);
        col.AddChild(_tag);

        _dim.Modulate = new Color(1, 1, 1, 0);
        _label.Modulate = new Color(1, 1, 1, 0);
        _tag.Modulate = new Color(1, 1, 1, 0);

        // ---- timeline ----
        var t = CreateTween().SetParallel();
        t.TweenProperty(_dim, "modulate:a", 1.0f, 0.5).SetDelay(Delay);                 // cut to black
        t.TweenProperty(_label, "modulate:a", 1.0f, 0.8).SetDelay(Delay + 0.45);
        t.TweenProperty(_tag, "modulate:a", 1.0f, 0.8).SetDelay(Delay + 0.65);
        // the underline wipes out from the left once the title is up
        t.TweenCallback(Callable.From(() =>
        {
            if (!IsInstanceValid(_line)) return;
            float w = _lineHolder.Size.X;
            _line.Size = new Vector2(w, 6);
            _line.PivotOffset = new Vector2(w / 2f, 3f);   // grows outwards from the middle
            _line.Scale = new Vector2(0f, 1f);
            CreateTween().TweenProperty(_line, "scale:x", 1.0f, 0.6)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        })).SetDelay(Delay + 0.9);

        // radio static flickers for a moment at the start of the cut
        GetTree().CreateTimer(Delay).Timeout += () =>
        {
            if (!IsInstanceValid(this)) return;
            _dim.MouseFilter = Control.MouseFilterEnum.Stop;   // block the mouse once the cut begins
            PlayStatic();
        };

        if (ReportWhenDone && GameSession.Instance != null)
        {
            GameSession.Instance.LevelStarted += OnLevelStarted;
            // after the hold, tell the host we're ready; the host starts the level when everyone is
            GetTree().CreateTimer(Delay + 0.8 + Hold).Timeout += () => GameSession.Instance?.ReportEndingDone();
        }
    }

    public override void _ExitTree()
    {
        if (GameSession.Instance != null) GameSession.Instance.LevelStarted -= OnLevelStarted;
    }

    private void PlayStatic()
    {
        _static.Visible = true;
        var tw = CreateTween();
        for (int i = 0; i < 8; i++)
        {
            tw.TweenCallback(Callable.From(ShuffleBars));
            tw.TweenInterval(0.05);
        }
        tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(_static)) _static.Visible = false; }));
    }

    private void ShuffleBars()
    {
        if (!IsInstanceValid(_static)) return;
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var child in _static.GetChildren())
        {
            if (child is not ColorRect bar) continue;
            float h = _rng.RandfRange(3f, 40f);
            bar.Position = new Vector2(0, _rng.RandfRange(0f, size.Y - h));
            bar.Size = new Vector2(size.X, h);
            bar.Color = new Color(Cream, _rng.RandfRange(0.15f, 0.7f));
        }
    }

    private void OnLevelStarted()
    {
        // let the new screen load underneath, then fade the card away
        var t = CreateTween().SetParallel();
        t.TweenProperty(_dim, "modulate:a", 0.0f, 0.6).SetDelay(0.6);
        t.TweenProperty(_label, "modulate:a", 0.0f, 0.6).SetDelay(0.6);
        t.TweenProperty(_tag, "modulate:a", 0.0f, 0.6).SetDelay(0.6);
        t.TweenProperty(_line, "modulate:a", 0.0f, 0.6).SetDelay(0.6);
        t.Chain().TweenCallback(Callable.From(QueueFree));
    }

    private static Label MakeLabel(string text, int size, Color color, Font font)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", Ink);
        l.AddThemeConstantOverride("outline_size", 6);
        return l;
    }
}