using Godot;
using System.Text;

// Title card between levels. Same public API as before (Title, Delay, Hold, ReportWhenDone).
// Transition: the screen cuts to radio static, settles to black, then the title fades in
// with a yellow underline that wipes across. Fades out again when the next level has loaded.
public partial class LevelTitleCard : CanvasLayer
{
    public string Title { get; set; } = "LEVEL 1";
    public float Delay { get; set; } = 0f;           // quiet pause before the card fades in
    public float Hold { get; set; } = 2.5f;          // how long it stays after fading in
    public bool ReportWhenDone { get; set; } = false; // true when this card leads into a level
    public bool StartCovered { get; set; } = false;   // true = screen is already black when the card appears (hides a scene swap)
    public bool FadeOutWhenDone { get; set; } = false; // true = fades itself away after the hold (cards that don't lead into a level)

    private static readonly Color Cream = new("FBEFD9");
    private static readonly Color Ink = new("2B1B2E");
    private static readonly Color Yellow = new("FFD25A");
    private const int BarCount = 16;

    private ColorRect _dim, _line;
    private Control _lineHolder;
    private Label _label, _tag;
    private Control _static;
    private readonly RandomNumberGenerator _rng = new();

    // Glitch text + continuous static
    private const string GlitchChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#%&@$?<>/=+*";
    private const double TickStep = 0.05;     // static and text refresh every 50 ms
    private const double DecodeStart = 0.45;  // when the title begins to lock in (same moment it fades in)
    private const double CharStep = 0.06;     // one more letter locks in every step
    private double _t, _nextTick, _burstUntil;
    private int _locked;                      // how many letters are already the real letter
    private bool _soft;                       // static eases off once the whole title has locked in

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

        int titleSize = FitTitleSize(font, Title);
        _label = MakeLabel(ScrambledTitle(), titleSize, Cream, font);
        // fixed size + clipped, so random characters never make the title (or the underline) jump around
        Font measure = font ?? ThemeDB.FallbackFont;
        _label.ClipText = true;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.CustomMinimumSize = new Vector2(
            measure.GetStringSize(Title, HorizontalAlignment.Left, -1, titleSize).X + 32,
            measure.GetHeight(titleSize) + 16);
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

        _dim.Modulate = new Color(1, 1, 1, StartCovered ? 1f : 0f);
        _label.Modulate = new Color(1, 1, 1, 0);
        _tag.Modulate = new Color(1, 1, 1, 0);

        // ---- timeline ----
        var t = CreateTween().SetParallel();
        if (!StartCovered) t.TweenProperty(_dim, "modulate:a", 1.0f, 0.5).SetDelay(Delay);                 // cut to black
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
        else if (FadeOutWhenDone)
        {
            GetTree().CreateTimer(Delay + 0.8 + Hold).Timeout += () => { if (IsInstanceValid(this)) FadeOut(0.0); };
        }
    }

    public override void _ExitTree()
    {
        if (GameSession.Instance != null) GameSession.Instance.LevelStarted -= OnLevelStarted;
    }

    // Static now runs for as long as the card is up. _Process keeps shuffling the bars.
    private void PlayStatic() => _static.Visible = true;

    public override void _Process(double delta)
    {
        _t += delta;
        if (_t < Delay) return;
        double since = _t - Delay;
        if (since < _nextTick) return;
        _nextTick = since + TickStep;

        // 1) static bars, every tick
        if (_static.Visible) ShuffleBars();

        // 2) title letters: lock in one by one, then keep glitching now and then
        if (since >= DecodeStart)
            _locked = Mathf.Min(Title.Length, (int)((since - DecodeStart) / CharStep) + 1);
        _soft = _locked >= Title.Length;

        if (_soft && _rng.Randf() < 0.04f) _burstUntil = since + 0.15;   // now and then a short burst
        _glitchChance = since < _burstUntil ? 0.3f : 0.025f;
        _label.Text = ScrambledTitle();
    }

    private float _glitchChance = 0.025f;

    // The title with the letters that aren't locked in yet (and a few flickering ones) swapped for random characters.
    private string ScrambledTitle()
    {
        var sb = new StringBuilder(Title.Length);
        for (int i = 0; i < Title.Length; i++)
        {
            char ch = Title[i];
            if (!char.IsLetterOrDigit(ch)) { sb.Append(ch); continue; }   // spaces, ':' and '!' stay put

            bool locked = i < _locked;
            bool flicker = locked && _rng.Randf() < _glitchChance;
            sb.Append(locked && !flicker ? ch : GlitchChars[_rng.RandiRange(0, GlitchChars.Length - 1)]);
        }
        return sb.ToString();
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
            bar.Color = new Color(Cream, _soft ? _rng.RandfRange(0.08f, 0.3f) : _rng.RandfRange(0.25f, 0.8f));
        }
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
        t.TweenProperty(_static, "modulate:a", 0.0f, 0.6).SetDelay(wait);
        t.Chain().TweenCallback(Callable.From(QueueFree));
    }

    // Biggest title size that still fits on screen. Long names shrink instead of running off the edge.
    private const int MaxTitleSize = 60;       // size for short titles (was 84)
    private const float TitleMaxWidth = 0.78f;  // share of the window width the title may use

    private int FitTitleSize(Font font, string text)
    {
        float maxWidth = GetViewport().GetVisibleRect().Size.X * TitleMaxWidth;
        Font f = font ?? ThemeDB.FallbackFont;

        int size = MaxTitleSize;
        while (size > 24 && f.GetStringSize(text, HorizontalAlignment.Left, -1, size).X > maxWidth)
            size -= 2;
        return size;
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