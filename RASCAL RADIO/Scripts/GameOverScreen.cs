using System.Collections.Generic;
using Godot;

// Game over: red flash and screen shake, the ON AIR sign dies, GAME OVER slams in,
// then one red X per strike drops in. Built entirely in code, same Layer as before.
public partial class GameOverScreen : CanvasLayer
{
    public string LobbyScenePath { get; set; } = "res://scenes/lobby.tscn";   // kept so nothing else breaks

    private static readonly Color Cream = new("FBEFD9");
    private static readonly Color Red = new("E5484D");
    private static readonly Color Soft = new("F2A0A0");
    private static readonly Color Faded = new("B98080");

    private ColorRect _flash, _cover;
    private PanelContainer _sign;
    private Label _signLbl, _title, _reason, _sub;
    private Button _btn;
    private Tween _shake;
    private bool _leaving;
    private readonly List<Control> _boxes = new();

    public override void _Ready()
    {
        Layer = 100;

        // ---------- dark red backdrop (blocks clicks on the game underneath) ----------
        var bg = new ColorRect { Color = new Color(0.14f, 0.01f, 0.03f, 0f) };
        AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);

        // ---------- ON AIR sign (goes dark later) ----------
        _sign = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            Modulate = new Color(1, 1, 1, 0),
        };
        var ss = new StyleBoxFlat { BgColor = new Color("2A0A0C"), BorderColor = Red };
        ss.SetBorderWidthAll(3);
        ss.SetCornerRadiusAll(6);
        ss.SetContentMarginAll(8);
        ss.ContentMarginLeft = 18;
        ss.ContentMarginRight = 18;
        _sign.AddThemeStyleboxOverride("panel", ss);
        _signLbl = Lbl("ON AIR", 20, Red);
        _sign.AddChild(_signLbl);
        box.AddChild(_sign);

        // ---------- title ----------
        _title = Lbl("GAME OVER", 88, Cream);
        _title.AddThemeColorOverride("font_outline_color", new Color("4A0D12"));
        _title.AddThemeConstantOverride("outline_size", 18);
        _title.Modulate = new Color(1, 1, 1, 0);
        _title.Resized += () => _title.PivotOffset = _title.Size / 2;
        box.AddChild(_title);

        // ---------- one red X per strike ----------
        int max = GameSession.MaxStrikes;
        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        row.AddThemeConstantOverride("separation", 14);
        box.AddChild(row);

        for (int i = 0; i < max; i++)
        {
            var b = new PanelContainer
            {
                CustomMinimumSize = new Vector2(70, 70),
                PivotOffset = new Vector2(35, 35),
                Scale = Vector2.Zero,
            };
            var bs = new StyleBoxFlat { BgColor = new Color("2A0A0C"), BorderColor = Red };
            bs.SetBorderWidthAll(4);
            bs.SetCornerRadiusAll(10);
            b.AddThemeStyleboxOverride("panel", bs);
            var x = Lbl("X", 48, Red);
            x.VerticalAlignment = VerticalAlignment.Center;
            b.AddChild(x);
            row.AddChild(b);
            _boxes.Add(b);
        }

        // ---------- text ----------
        string reason = max == 1 ? "NO MISTAKES ALLOWED. THE STATION IS DOWN." : $"{max} STRIKES. THE STATION IS DOWN.";
        _reason = Lbl(reason, 24, Soft);
        _reason.Modulate = new Color(1, 1, 1, 0);
        box.AddChild(_reason);

        _sub = Lbl("The broadcast has gone silent...", 16, Faded);
        _sub.Modulate = new Color(1, 1, 1, 0);
        box.AddChild(_sub);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _btn = new Button
        {
            Text = "BACK TO LOBBY",
            CustomMinimumSize = new Vector2(240, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            Disabled = true,
            Modulate = new Color(1, 1, 1, 0),
        };
        StyleBackButton(_btn);
        _btn.Pressed += Leave;
        box.AddChild(_btn);

        // ---------- red flash and black cover, both on top ----------
        _flash = new ColorRect { Color = new Color(0.9f, 0.1f, 0.1f, 0.75f), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_flash);
        _flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _cover = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_cover);
        _cover.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // ---------- timeline ----------
        var t0 = CreateTween().SetParallel();
        t0.TweenProperty(_flash, "color:a", 0.0f, 0.7);   // red flash fades out
        t0.TweenProperty(bg, "color:a", 0.93f, 0.5);      // the game goes dark red
        Shake(14f);

        var seq = CreateTween();
        seq.TweenInterval(0.3);
        seq.TweenCallback(Callable.From(FlickerSign));
        seq.TweenInterval(0.5);
        seq.TweenCallback(Callable.From(SlamTitle));
        seq.TweenInterval(0.7);
        for (int i = 0; i < _boxes.Count; i++)
        {
            int idx = i;
            seq.TweenCallback(Callable.From(() => SlamBox(idx)));
            seq.TweenInterval(0.3);
        }
        seq.TweenCallback(Callable.From(ShowText));
    }

    // ===== animation =====
    private void FlickerSign()
    {
        var tw = CreateTween();
        tw.TweenProperty(_sign, "modulate:a", 1.0f, 0.1);
        for (int i = 0; i < 4; i++)
        {
            tw.TweenProperty(_sign, "modulate:a", 0.2f, 0.05);
            tw.TweenProperty(_sign, "modulate:a", 1.0f, 0.06);
        }
        tw.TweenCallback(Callable.From(() =>
        {
            _signLbl.Text = "OFF AIR";
            _signLbl.AddThemeColorOverride("font_color", new Color("6E2A2E"));
        }));
        tw.TweenProperty(_sign, "modulate:a", 0.8f, 0.2);
    }

    private void SlamTitle()
    {
        _title.Scale = new Vector2(1.7f, 1.7f);
        var tw = CreateTween().SetParallel();
        tw.TweenProperty(_title, "scale", Vector2.One, 0.2)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.TweenProperty(_title, "modulate:a", 1.0f, 0.1);
        tw.Chain().TweenCallback(Callable.From(() => { Flash(0.45f); Shake(18f); }));
    }

    private void SlamBox(int i)
    {
        var b = _boxes[i];
        b.Scale = new Vector2(2f, 2f);
        b.Modulate = new Color(1, 1, 1, 0);

        var tw = CreateTween().SetParallel();
        tw.TweenProperty(b, "scale", Vector2.One, 0.18)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.TweenProperty(b, "modulate:a", 1.0f, 0.08);
        tw.Chain().TweenCallback(Callable.From(() => { Flash(0.3f); Shake(9f); }));
    }

    private void ShowText()
    {
        var tw = CreateTween().SetParallel();
        tw.TweenProperty(_reason, "modulate:a", 1.0f, 0.4);
        tw.TweenProperty(_sub, "modulate:a", 1.0f, 0.4).SetDelay(0.2);
        tw.TweenProperty(_btn, "modulate:a", 1.0f, 0.4).SetDelay(0.5);
        tw.Chain().TweenCallback(Callable.From(() => { _btn.Disabled = false; _btn.GrabFocus(); }));
    }

    private void Flash(float alpha)
    {
        _flash.Color = new Color(0.9f, 0.1f, 0.1f, alpha);
        CreateTween().TweenProperty(_flash, "color:a", 0.0f, 0.35);
    }

    // shakes the whole screen by moving the layer
    private void Shake(float amount)
    {
        _shake?.Kill();
        _shake = CreateTween();
        for (int i = 0; i < 6; i++)
        {
            var off = new Vector2((float)GD.RandRange(-amount, amount), (float)GD.RandRange(-amount, amount));
            _shake.TweenProperty(this, "offset", off, 0.03);
        }
        _shake.TweenProperty(this, "offset", Vector2.Zero, 0.05);
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

    private static Label Lbl(string text, int size, Color color)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    // Dark maroon button with a red border that matches the game-over screen. Fills red on hover.
    public static void StyleBackButton(Button b)
    {
        StyleBoxFlat Box(Color bg, Color border)
        {
            var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            s.SetBorderWidthAll(3);
            s.SetCornerRadiusAll(10);
            s.ContentMarginLeft = 28; s.ContentMarginRight = 28;
            s.ContentMarginTop = 10; s.ContentMarginBottom = 10;
            return s;
        }

        b.AddThemeStyleboxOverride("normal", Box(new Color("2A0A0C"), Red));
        b.AddThemeStyleboxOverride("hover", Box(Red, Cream));
        b.AddThemeStyleboxOverride("pressed", Box(new Color("B3261E"), Cream));
        b.AddThemeStyleboxOverride("disabled", Box(new Color("2A0A0C"), new Color("6E2A2E")));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());   // removes the white focus outline
        b.AddThemeColorOverride("font_color", Cream);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        b.AddThemeColorOverride("font_disabled_color", new Color("6E2A2E"));
        b.AddThemeFontSizeOverride("font_size", 22);
        b.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
    }
}