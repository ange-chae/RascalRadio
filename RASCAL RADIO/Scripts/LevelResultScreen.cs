using System.Collections.Generic;
using Godot;

// Shift report: a sheet of paper with fill bars and a rating stamp that slams down.
// Built entirely in code. Same Layer and same public fields as before, so GameSession needs no changes.
public partial class LevelResultScreen : CanvasLayer
{
    public int CompletionPts, AccuracyPts, Completed, Total;

    private static readonly Color PaperCol = new("F6EBD3");
    private static readonly Color Ink = new("2B1B2E");
    private static readonly Color Muted = new("8A6F7C");
    private static readonly Color Good = new("3FA66B");
    private static readonly Color Mid = new("E8A53A");
    private static readonly Color Bad = new("D64545");

    private const int BarMax = 25;

    private PanelContainer _paper;
    private Control _stamp;
    private ColorRect _dim, _cover;
    private Button _btn;
    private Label _scoreLbl, _compVal, _accVal;
    private ProgressBar _compBar, _accBar;
    private readonly List<Control> _rows = new();
    private string _rank = "";
    private bool _leaving;

    // Font: assets/fonts/ShortStack-Regular (falls back to the default font if missing)
    private static Font _font;
    private static bool _fontTried;
    private static Font ShortStack()
    {
        if (_fontTried) return _font;
        _fontTried = true;
        foreach (var path in new[] { "res://assets/fonts/ShortStack-Regular.ttf", "res://assets/fonts/ShortStack-Regular.otf" })
            if (ResourceLoader.Exists(path)) { _font = GD.Load<Font>(path); break; }
        if (_font == null) GD.PushWarning("LevelResultScreen: ShortStack-Regular not found in res://assets/fonts/");
        return _font;
    }

    public override void _Ready()
    {
        Layer = 100;
        var gs = GameSession.Instance;
        int score = CompletionPts + AccuracyPts;
        var (rank, title) = GameSession.Rating(score);
        _rank = rank;

        Color rankColor = rank switch
        {
            "S" => new Color("D08A00"),
            "A" => new Color("2E8B57"),
            "B" => new Color("3A6EA5"),
            _ => new Color("C23B3B"),
        };

        // ---------- backdrop (also blocks clicks on the game underneath) ----------
        _dim = new ColorRect { Color = new Color(0.06f, 0.03f, 0.09f, 0f) };
        AddChild(_dim);
        _dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // ---------- the paper ----------
        _paper = new PanelContainer
        {
            CustomMinimumSize = new Vector2(580, 0),
            Modulate = new Color(1, 1, 1, 0),
        };
        var ps = new StyleBoxFlat { BgColor = PaperCol, BorderColor = new Color("D9C7A0") };
        ps.SetBorderWidthAll(2);
        ps.SetCornerRadiusAll(4);
        ps.SetContentMarginAll(28);
        ps.ShadowColor = new Color(0, 0, 0, 0.5f);
        ps.ShadowSize = 16;
        ps.ShadowOffset = new Vector2(0, 8);
        _paper.AddThemeStyleboxOverride("panel", ps);
        _paper.Resized += () => _paper.PivotOffset = _paper.Size / 2;   // rotate/scale around the middle
        center.AddChild(_paper);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        _paper.AddChild(col);

        // header
        col.AddChild(Lbl("SHIFT REPORT", 13, Muted));
        string lvlTitle = gs?.CurrentLevel?.Title ?? "";
        if (lvlTitle != "") col.AddChild(Lbl(lvlTitle.ToUpper(), 30, Ink));
        col.AddChild(Rule());

        // body: bars on the left, stamp on the right
        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 24);
        col.AddChild(body);

        var left = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        left.AddThemeConstantOverride("separation", 16);
        body.AddChild(left);

        (_compBar, _compVal) = BarRow(left, "TASK COMPLETION", CompletionPts);
        (_accBar, _accVal) = BarRow(left, "ACCURACY", AccuracyPts);
        _scoreLbl = Lbl("SCORE   0 / 50", 22, Ink, HorizontalAlignment.Left);
        left.AddChild(_scoreLbl);

        var holder = new Control { CustomMinimumSize = new Vector2(180, 200) };
        body.AddChild(holder);
        _stamp = BuildStamp(rank, title, rankColor);
        holder.AddChild(_stamp);
        _stamp.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _stamp.PivotOffset = new Vector2(90, 100);   // matches the holder size
        _stamp.Modulate = new Color(1, 1, 1, 0);

        // checklist
        col.AddChild(Rule());
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 4);
        col.AddChild(list);

        foreach (var t in gs.Tasks)
        {
            bool ok = t.IsHealth ? !t.Failed : t.Done;
            var row = new HBoxContainer { Modulate = new Color(1, 1, 1, 0) };
            row.AddThemeConstantOverride("separation", 10);

            var mark = Lbl(ok ? "✔" : "X", 15, ok ? Good : Bad);
            mark.CustomMinimumSize = new Vector2(18, 0);
            row.AddChild(mark);

            var txt = Lbl(gs.TaskText(t), 15, ok ? Ink : Bad, HorizontalAlignment.Left);
            txt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(txt);

            if (t.Mistakes > 0)
                row.AddChild(Lbl($"{t.Mistakes} mistake{(t.Mistakes > 1 ? "s" : "")}", 12, Muted, HorizontalAlignment.Right));

            list.AddChild(row);
            _rows.Add(row);
        }

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        _btn = new Button
        {
            Text = "BACK TO LOBBY",
            CustomMinimumSize = new Vector2(240, 52),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        UiStyle.StyleButton(_btn, UiStyle.Yellow);
        if (ShortStack() != null) _btn.AddThemeFontOverride("font", ShortStack());
        _btn.Pressed += Leave;
        col.AddChild(_btn);

        // ---------- black cover on top: fades out (transition in) and back in (transition out) ----------
        _cover = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_cover);
        _cover.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // ---------- timeline ----------
        var t0 = CreateTween().SetParallel();
        t0.TweenProperty(_cover, "color:a", 0.0f, 0.5);   // fade in from black
        t0.TweenProperty(_dim, "color:a", 0.8f, 0.5);     // the game dims behind the paper

        var seq = CreateTween();
        seq.TweenInterval(0.3);
        seq.TweenCallback(Callable.From(PaperIn));
        seq.TweenInterval(1.0);
        seq.TweenCallback(Callable.From(() =>
        {
            FillBar(_compBar, _compVal, CompletionPts, 0.0);
            FillBar(_accBar, _accVal, AccuracyPts, 0.35);
            CountScore(score);
        }));
        seq.TweenInterval(1.7);
        seq.TweenCallback(Callable.From(Slam));
    }

    // ===== animation =====
    private void PaperIn()
    {
        _paper.Scale = new Vector2(0.8f, 0.8f);
        _paper.Rotation = Mathf.DegToRad(4f);

        var tw = CreateTween().SetParallel();
        tw.TweenProperty(_paper, "modulate:a", 1.0f, 0.25);
        tw.TweenProperty(_paper, "scale", Vector2.One, 0.45)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(_paper, "rotation", Mathf.DegToRad(-1.2f), 0.45)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        for (int i = 0; i < _rows.Count; i++)   // checklist rows appear one by one
            tw.TweenProperty(_rows[i], "modulate:a", 1.0f, 0.25).SetDelay(0.5 + i * 0.08);
    }

    private void FillBar(ProgressBar bar, Label val, int pts, double delay)
    {
        var tw = CreateTween().SetParallel();
        tw.TweenProperty(bar, "value", (double)pts, 0.9)
            .SetDelay(delay).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.TweenMethod(Callable.From<float>(v => val.Text = $"{Mathf.RoundToInt(v)} / {BarMax}"), 0f, (float)pts, 0.9)
            .SetDelay(delay).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    private void CountScore(int score)
    {
        CreateTween().TweenMethod(
            Callable.From<float>(v => _scoreLbl.Text = $"SCORE   {Mathf.RoundToInt(v)} / 50"),
            0f, (float)score, 1.2);
    }

    private void Slam()
    {
        _stamp.Scale = new Vector2(3f, 3f);
        _stamp.Rotation = Mathf.DegToRad(-35f);

        var tw = CreateTween().SetParallel();
        tw.TweenProperty(_stamp, "modulate:a", 0.93f, 0.08);
        tw.TweenProperty(_stamp, "scale", Vector2.One, 0.18)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.TweenProperty(_stamp, "rotation", Mathf.DegToRad(-12f), 0.18)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.Chain().TweenCallback(Callable.From(OnStampLanded));
    }

    private void OnStampLanded()
    {
        // the paper jolts when the stamp hits
        var tw = CreateTween();
        tw.TweenProperty(_paper, "rotation", Mathf.DegToRad(0.8f), 0.05);
        tw.TweenProperty(_paper, "rotation", Mathf.DegToRad(-1.6f), 0.07);
        tw.TweenProperty(_paper, "rotation", Mathf.DegToRad(-1.2f), 0.12);

        var sq = CreateTween();
        sq.TweenProperty(_paper, "scale", new Vector2(0.985f, 0.985f), 0.05);
        sq.TweenProperty(_paper, "scale", Vector2.One, 0.15);

        // confetti only if the shift was passed (more for a better rank)
        switch (_rank)
        {
            case "S": SpawnConfetti(20); break;
            case "A": SpawnConfetti(14); break;
            case "B": SpawnConfetti(8); break;
        }
    }

    private void SpawnConfetti(int perEmitter)
    {
        var vp = GetViewport().GetVisibleRect().Size;

        var img = Image.CreateEmpty(10, 5, false, Image.Format.Rgba8);
        img.Fill(Colors.White);
        var tex = ImageTexture.CreateFromImage(img);

        var colors = new[]
        {
            new Color("FFD25A"), new Color("E5484D"), new Color("2FD6A0"),
            new Color("5AB0FF"), new Color("FF8FD0"),
        };

        // two "cannons" in the bottom corners, shooting inwards and up
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0;
            foreach (var c in colors)
            {
                var p = new CpuParticles2D
                {
                    Texture = tex,
                    Color = c,
                    Amount = perEmitter,
                    OneShot = true,
                    Explosiveness = 0.95f,
                    Lifetime = 3.5f,
                    LifetimeRandomness = 0.3f,
                    Position = new Vector2(left ? 0f : vp.X, vp.Y * 0.9f),
                    Direction = new Vector2(left ? 0.55f : -0.55f, -1f),
                    Spread = 22f,
                    InitialVelocityMin = 600f,
                    InitialVelocityMax = 1000f,
                    Gravity = new Vector2(0, 1000),
                    DampingMin = 60f,
                    DampingMax = 140f,
                    AngularVelocityMin = -400f,
                    AngularVelocityMax = 400f,
                    ScaleAmountMin = 1f,
                    ScaleAmountMax = 1.8f,
                };
                AddChild(p);
                p.Emitting = true;
            }
        }

        // keep the black cover (used for the fade out) above the confetti
        MoveChild(_cover, GetChildCount() - 1);
    }

    private void Leave()
    {
        if (_leaving) return;
        _leaving = true;
        _btn.Disabled = true;
        _cover.MouseFilter = Control.MouseFilterEnum.Stop;

        var tw = CreateTween();
        tw.TweenProperty(_cover, "color:a", 1.0f, 0.4);   // fade to black, then go back
        tw.TweenCallback(Callable.From(() => GameSession.Instance.ReturnToParty()));
    }

    // ===== builders =====
    private (ProgressBar, Label) BarRow(VBoxContainer parent, string name, int pts)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        parent.AddChild(box);

        var head = new HBoxContainer();
        box.AddChild(head);

        var n = Lbl(name, 13, Muted, HorizontalAlignment.Left);
        n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(n);

        var val = Lbl($"0 / {BarMax}", 15, Ink, HorizontalAlignment.Right);
        head.AddChild(val);

        float ratio = pts / (float)BarMax;
        Color fillCol = ratio >= 0.8f ? Good : ratio >= 0.5f ? Mid : Bad;

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = BarMax,
            Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 22),
        };
        var back = new StyleBoxFlat { BgColor = new Color("E6D6B4"), BorderColor = Ink };
        back.SetBorderWidthAll(3);
        back.SetCornerRadiusAll(6);
        var fill = new StyleBoxFlat { BgColor = fillCol };
        fill.SetCornerRadiusAll(4);
        bar.AddThemeStyleboxOverride("background", back);
        bar.AddThemeStyleboxOverride("fill", fill);
        box.AddChild(bar);

        return (bar, val);
    }

    private static VBoxContainer BuildStamp(string rank, string title, Color c)
    {
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 6);

        var ring = new PanelContainer
        {
            CustomMinimumSize = new Vector2(150, 150),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        var rs = new StyleBoxFlat { BgColor = new Color(c, 0.08f), BorderColor = c };
        rs.SetBorderWidthAll(7);
        rs.SetCornerRadiusAll(75);
        ring.AddThemeStyleboxOverride("panel", rs);
        box.AddChild(ring);

        var letter = Lbl(rank, 100, c);
        letter.VerticalAlignment = VerticalAlignment.Center;
        ring.AddChild(letter);

        var name = Lbl(title.ToUpper(), 13, c);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.CustomMinimumSize = new Vector2(170, 0);
        box.AddChild(name);

        return box;
    }

    private static ColorRect Rule() => new()
    {
        Color = new Color(Ink, 0.25f),
        CustomMinimumSize = new Vector2(0, 2),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static Label Lbl(string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var l = new Label { Text = text, HorizontalAlignment = align };
        if (ShortStack() != null) l.AddThemeFontOverride("font", ShortStack());
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}