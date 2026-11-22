using Godot;

// Title card between levels. Same public API as before (Title, Delay, Hold, ReportWhenDone, ...).
// Transition: the screen cuts to radio static, settles to black, then the title SLAMS in with a flash and
// screen shake. With GlitchStatic on (Level 3 only) the whole title then stays covered in live static: thin
// noise bars crawl across it and red/cyan ghost copies of the letters jitter, with glitch bursts now and then.
// Fades out again when the next level has loaded.
public partial class LevelTitleCard : CanvasLayer
{
    public string Title { get; set; } = "LEVEL 1";
    public float Delay { get; set; } = 0f;           // quiet pause before the card fades in
    public float Hold { get; set; } = 2.5f;          // how long it stays after fading in
    public bool ReportWhenDone { get; set; } = false; // true when this card leads into a level
    public bool StartCovered { get; set; } = false;   // true = screen is already black when the card appears (hides a scene swap)
    public bool FadeOutWhenDone { get; set; } = false; // true = fades itself away after the hold (cards that don't lead into a level)
    public Color Accent { get; set; } = new("FFD25A"); // underline + small tag color
    public bool GlitchStatic { get; set; } = false;   // true = constant dramatic static + glitch over the title (Level 3 only)

    private static readonly Color Cream = new("FBEFD9");
    private static readonly Color Ink = new("2B1B2E");
    private static readonly Color GhostRed = new("E5484D");
    private static readonly Color GhostCyan = new("5AE0FF");
    private const int BarCount = 16;      // the quick static cut at the start
    private const int NoiseCount = 26;    // the constant static over the title

    private ColorRect _dim, _line, _flash;
    private Control _lineHolder;
    private Label _label, _tag, _ghostR, _ghostC;
    private Control _static, _noise;
    private Timer _glitchTimer;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        Layer = 50;
        _rng.Randomize();
        AddToGroup("level_title_card");   // GameSession checks this so two cards never stack

        // Font: assets/fonts/ShortStack-Regular (falls back to the default font if missing)
        Font font = null;
        foreach (var path in new[] { "res://assets/fonts/ShortStack-Regular.ttf", "res://assets/fonts/ShortStack-Regular.otf" })
            if (ResourceLoader.Exists(path)) { font = GD.Load<Font>(path); break; }
        if (font == null) GD.PushWarning("LevelTitleCard: ShortStack-Regular not found in res://assets/fonts/");

        // solid black, so the tutorial screen doesn't show through
        _dim = new ColorRect { Color = new Color(Ink.Darkened(0.5f), 1f), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_dim);
        _dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // the quick static burst at the start of the cut
        _static = MakeBarLayer(BarCount);
        _static.Visible = false;

        // title + underline + small tag, stacked in the middle
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 10);
        center.AddChild(col);

        _label = MakeLabel(Title, 104, Cream, font);
        col.AddChild(_label);
        _label.Resized += () => _label.PivotOffset = _label.Size / 2f;

        // Level 3 only: two ghost copies behind the title (red + cyan) that jitter sideways, like a bad signal
        if (GlitchStatic)
        {
            _ghostR = MakeGhost(Title, 104, new Color(GhostRed, 0.8f), font);
            _ghostC = MakeGhost(Title, 104, new Color(GhostCyan, 0.8f), font);
            _label.AddChild(_ghostR);
            _label.AddChild(_ghostC);
            _ghostR.ShowBehindParent = true;
            _ghostC.ShowBehindParent = true;
            _ghostR.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _ghostC.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }

        // holder fills the column (= the title's width), so the line lines up with the title
        _lineHolder = new Control
        {
            CustomMinimumSize = new Vector2(0, 6),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        col.AddChild(_lineHolder);
        _line = new ColorRect { Color = Accent, Size = new Vector2(0, 6), MouseFilter = Control.MouseFilterEnum.Ignore };
        _lineHolder.AddChild(_line);

        _tag = MakeLabel("RASCAL RADIO", 20, Accent, font);
        col.AddChild(_tag);

        // Level 3 only: constant static ON TOP of the title (thin, see-through bars so the text stays readable)
        if (GlitchStatic)
        {
            _noise = MakeBarLayer(NoiseCount);
            _noise.Visible = false;
        }

        // white-hot flash for the slam
        _flash = new ColorRect { Color = new Color(Cream, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_flash);
        _flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        if (GlitchStatic)
        {
            _glitchTimer = new Timer { WaitTime = 0.05, OneShot = false, Autostart = false };
            _glitchTimer.Timeout += GlitchTick;
            AddChild(_glitchTimer);
        }

        _dim.Modulate = new Color(1, 1, 1, StartCovered ? 1f : 0f);
        _label.Modulate = new Color(1, 1, 1, 0);
        _tag.Modulate = new Color(1, 1, 1, 0);

        // ---- timeline ----
        var t = CreateTween().SetParallel();
        if (!StartCovered) t.TweenProperty(_dim, "modulate:a", 1.0f, 0.5).SetDelay(Delay);                 // cut to black
        t.TweenCallback(Callable.From(Slam)).SetDelay(Delay + 0.45);                                       // title slams in
        t.TweenProperty(_tag, "modulate:a", 1.0f, 0.5).SetDelay(Delay + 0.85);
        // the underline wipes out from the middle once the title is up
        t.TweenCallback(Callable.From(() =>
        {
            if (!IsInstanceValid(_line)) return;
            float w = _lineHolder.Size.X;
            _line.Size = new Vector2(w, 6);
            _line.PivotOffset = new Vector2(w / 2f, 3f);   // grows outwards from the middle
            _line.Scale = new Vector2(0f, 1f);
            CreateTween().TweenProperty(_line, "scale:x", 1.0f, 0.5)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        })).SetDelay(Delay + 0.8);

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
        else if (FadeOutWhenDone)
        {
            GetTree().CreateTimer(Delay + 0.8 + Hold).Timeout += () => { if (IsInstanceValid(this)) FadeOut(0.0); };
        }
    }

    public override void _ExitTree()
    {
        if (GameSession.Instance != null) GameSession.Instance.LevelStarted -= OnLevelStarted;
    }

    // ===== the slam =====
    private void Slam()
    {
        if (!IsInstanceValid(_label)) return;

        _label.PivotOffset = _label.Size / 2f;
        _label.Scale = new Vector2(2.2f, 2.2f);

        var tw = CreateTween().SetParallel();
        tw.TweenProperty(_label, "modulate:a", 1.0f, 0.08);
        tw.TweenProperty(_label, "scale", Vector2.One, 0.22)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.Chain().TweenCallback(Callable.From(Impact));
    }

    private void Impact()
    {
        if (!IsInstanceValid(this)) return;

        // flash + screen shake
        _flash.Color = new Color(Cream, 0.65f);
        CreateTween().TweenProperty(_flash, "color:a", 0.0f, 0.4);
        Shake(GlitchStatic ? 26f : 18f);

        // Level 3: the static never leaves again
        if (GlitchStatic)
        {
            _noise.Visible = true;
            _glitchTimer.Start();
        }
    }

    private void Shake(float amount)
    {
        var tw = CreateTween();
        for (int i = 0; i < 7; i++)
        {
            var off = new Vector2(_rng.RandfRange(-amount, amount), _rng.RandfRange(-amount, amount));
            tw.TweenProperty(this, "offset", off, 0.03);
            amount *= 0.8f;
        }
        tw.TweenProperty(this, "offset", Vector2.Zero, 0.04);
    }

    // ===== constant static / glitch (runs every 0.05 s while the title is up) =====
    private void GlitchTick()
    {
        if (!IsInstanceValid(_noise) || !IsInstanceValid(_label)) return;

        bool burst = _rng.Randf() < 0.26f;

        // thin see-through bars crawl over the title; a burst makes them thicker and brighter
        ScatterBars(_noise,
            burst ? 2f : 1f, burst ? 26f : 5f,
            burst ? 0.2f : 0.04f, burst ? 0.6f : 0.22f);

        // ghost letters jitter sideways
        float j = burst ? 16f : 3.5f;
        SetGhostOffset(_ghostR, _rng.RandfRange(-j, 0f), _rng.RandfRange(-j * 0.3f, j * 0.3f));
        SetGhostOffset(_ghostC, _rng.RandfRange(0f, j), _rng.RandfRange(-j * 0.3f, j * 0.3f));

        // on a burst the title itself stretches and the whole screen twitches for a moment
        if (burst)
        {
            _label.Scale = new Vector2(1f + _rng.RandfRange(0f, 0.07f), 1f + _rng.RandfRange(-0.03f, 0.03f));
            Offset = new Vector2(_rng.RandfRange(-7f, 7f), _rng.RandfRange(-3f, 3f));
        }
        else
        {
            _label.Scale = Vector2.One;
            Offset = Vector2.Zero;
        }
    }

    private static void SetGhostOffset(Label ghost, float x, float y)
    {
        ghost.OffsetLeft = x;
        ghost.OffsetRight = x;
        ghost.OffsetTop = y;
        ghost.OffsetBottom = y;
    }

    // ===== static layers =====
    private Control MakeBarLayer(int count)
    {
        var layer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(layer);
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        for (int i = 0; i < count; i++)
            layer.AddChild(new ColorRect { Color = Cream, MouseFilter = Control.MouseFilterEnum.Ignore });
        return layer;
    }

    private void ScatterBars(Control layer, float minH, float maxH, float minA, float maxA)
    {
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var child in layer.GetChildren())
        {
            if (child is not ColorRect bar) continue;
            float h = _rng.RandfRange(minH, maxH);
            bar.Position = new Vector2(0, _rng.RandfRange(0f, Mathf.Max(1f, size.Y - h)));
            bar.Size = new Vector2(size.X, h);

            // mostly cream, with some red and cyan bars mixed in
            float pick = _rng.Randf();
            Color baseCol = pick < 0.7f ? Cream : pick < 0.85f ? GhostRed : GhostCyan;
            bar.Color = new Color(baseCol, _rng.RandfRange(minA, maxA));
        }
    }

    // the quick static cut at the start
    private void PlayStatic()
    {
        _static.Visible = true;
        var tw = CreateTween();
        for (int i = 0; i < 8; i++)
        {
            tw.TweenCallback(Callable.From(() =>
            {
                if (IsInstanceValid(_static)) ScatterBars(_static, 3f, 40f, 0.15f, 0.7f);
            }));
            tw.TweenInterval(0.05);
        }
        tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(_static)) _static.Visible = false; }));
    }

    // the new level loads underneath, then the card fades away
    private void OnLevelStarted() => FadeOut(0.6);

    private void FadeOut(double wait)
    {
        if (!IsInstanceValid(_dim)) return;
        var t = CreateTween().SetParallel();
        t.TweenProperty(_dim, "modulate:a", 0.0f, 0.6).SetDelay(wait);
        t.TweenProperty(_label, "modulate:a", 0.0f, 0.6).SetDelay(wait);
        t.TweenProperty(_tag, "modulate:a", 0.0f, 0.6).SetDelay(wait);
        t.TweenProperty(_line, "modulate:a", 0.0f, 0.6).SetDelay(wait);
        if (_noise != null) t.TweenProperty(_noise, "modulate:a", 0.0f, 0.6).SetDelay(wait);
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

    private static Label MakeGhost(string text, int size, Color color, Font font)
    {
        var l = MakeLabel(text, size, color, font);
        l.VerticalAlignment = VerticalAlignment.Center;
        l.AddThemeConstantOverride("outline_size", 0);   // no dark outline, so the color shows cleanly
        return l;
    }
}