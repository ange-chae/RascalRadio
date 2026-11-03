using System.Collections.Generic;
using Godot;
using RascalRadio.Core.Systems;

// Operator-side Line Override keypad. Built in code and dropped into Studio Desk slot 1.
// Designed to sit on the maroon popup panel (see OperatorInterface changes).
public partial class KeypadModule : VBoxContainer
{
    [Signal] public delegate void SolvedEventHandler();

    // Palette taken from the Operator background
    private static readonly Color ScreenBg = new("#25383A");     // dark teal wall
    private static readonly Color ScreenText = new("#E6E0B0");   // warm window-light cream
    private static readonly Color ScreenBorder = new("#D8D2A0");
    private static readonly Color StaticRed = new("#E4572E");
    private static readonly Color SolvedGreen = new("#8FD694");
    private static readonly Color Orange = new("#E8955A");       // floor orange

    private const float KeySize = 68f;

    private int _lineValue;
    public int Line
    {
        get => _lineValue;
        set
        {
            _lineValue = value;
            UpdateLineText();
        }
    }

    private bool _staticFaultValue;
    public bool StaticFault
    {
        get => _staticFaultValue;
        set
        {
            _staticFaultValue = value;
            ApplyStaticLook();
            UpdateStatusText();
        }
    }

    private string _currentEntry = "";
    private readonly List<Button> _keys = new();
    private Label _line, _entry, _status;
    private PanelContainer _display, _badge;
    private Control _noise;
    private bool _solved;
    private double _tick;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 10);

        // ---- display ----
        _display = new PanelContainer();
        AddChild(_display);

        var displayBox = new VBoxContainer();
        displayBox.AddThemeConstantOverride("separation", 2);
        _display.AddChild(displayBox);

        var header = new HBoxContainer();
        displayBox.AddChild(header);

        _line = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _line.AddThemeFontSizeOverride("font_size", 14);
        _line.AddThemeColorOverride("font_color", ScreenText);
        header.AddChild(_line);

        // Only visible while the static fault is active
        _badge = new PanelContainer { Visible = false };
        var badgeStyle = new StyleBoxFlat { BgColor = StaticRed };
        badgeStyle.SetCornerRadiusAll(4);
        badgeStyle.SetContentMarginAll(3);
        badgeStyle.ContentMarginLeft = 6;
        badgeStyle.ContentMarginRight = 6;
        _badge.AddThemeStyleboxOverride("panel", badgeStyle);
        var badgeLabel = new Label { Text = "⚡ STATIC" };
        badgeLabel.AddThemeFontSizeOverride("font_size", 12);
        badgeLabel.AddThemeColorOverride("font_color", Colors.White);
        _badge.AddChild(badgeLabel);
        header.AddChild(_badge);

        _entry = new Label { Text = "_", HorizontalAlignment = HorizontalAlignment.Center };
        _entry.AddThemeFontSizeOverride("font_size", 32);
        _entry.AddThemeColorOverride("font_color", ScreenText);
        displayBox.AddChild(_entry);

        // Noise overlay drawn on top of the display (ignores the mouse)
        _noise = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _noise.Draw += DrawNoise;
        _display.AddChild(_noise);

        // ---- keys: square ----
        var gridHolder = new CenterContainer();
        AddChild(gridHolder);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        gridHolder.AddChild(grid);

        foreach (string key in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLEAR", "0", "ROUTE" })
        {
            string k = key;
            var b = new Button
            {
                Text = k,
                CustomMinimumSize = new Vector2(KeySize, KeySize),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            b.AddThemeFontSizeOverride("font_size", k.Length > 1 ? 13 : 26);
            Color fill = k == "ROUTE" ? UiStyle.Yellow : k == "CLEAR" ? Orange : UiStyle.Cream;
            UiStyle.StyleButton(b, fill);
            b.Pressed += () => OnKey(k);
            grid.AddChild(b);
            _keys.Add(b);
        }

        // ---- one short status line ----
        _status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 36),
        };
        _status.AddThemeFontSizeOverride("font_size", 12);
        _status.AddThemeColorOverride("font_color", ScreenText);
        AddChild(_status);

        // Apply initial values now that UI nodes exist
        UpdateLineText();
        ApplyStaticLook();
        UpdateStatusText();

        if (GameSession.Instance != null) GameSession.Instance.Feedback += OnFeedback;
    }

    public override void _ExitTree()
    {
        if (GameSession.Instance != null) GameSession.Instance.Feedback -= OnFeedback;
    }

    public void Refresh(int line, bool staticFault)
    {
        Line = line;
        StaticFault = staticFault;
        _currentEntry = "";
        if (_entry != null) _entry.Text = "_";
    }

    // ===== display / static =====
    private StyleBoxFlat ScreenStyle(Color border, int width)
    {
        var s = new StyleBoxFlat { BgColor = ScreenBg, BorderColor = border };
        s.SetBorderWidthAll(width);
        s.SetCornerRadiusAll(8);
        s.SetContentMarginAll(10);
        return s;
    }

    private void UpdateLineText()
    {
        if (_line == null) return;
        _line.Text = _lineValue > 0 ? $"LINE {_lineValue}" : "NO LINE";
    }

    private void ApplyStaticLook()
    {
        if (_display == null) return;
        bool on = StaticFault && !_solved;
        _display.AddThemeStyleboxOverride("panel", on ? ScreenStyle(StaticRed, 4) : ScreenStyle(ScreenBorder, 3));
        _badge.Visible = on;
        _noise.Visible = on;
        SetProcess(on);
        if (!on)
        {
            _display.SelfModulate = Colors.White;
            UpdateLineText();
            _entry.Text = _currentEntry == "" ? "_" : _currentEntry;
        }
    }

    public override void _Process(double delta)
    {
        if (!StaticFault || _solved) return;

        _tick += delta;
        if (_tick < 0.07) return;
        _tick = 0;

        // Redraw noise, flicker the screen, blink the badge
        _noise.QueueRedraw();
        _display.SelfModulate = new Color(1, 1, 1, 0.75f + GD.Randf() * 0.25f);
        _badge.Modulate = (Time.GetTicksMsec() / 250) % 2 == 0 ? Colors.White : new Color(1, 1, 1, 0.45f);

        // Scramble some characters, but keep the real text readable most of the time
        _line.Text = Glitch(_lineValue > 0 ? $"LINE {_lineValue}" : "NO LINE", 0.25f);
        _entry.Text = Glitch(_currentEntry == "" ? "_" : _currentEntry, 0.15f);
    }

    private static string Glitch(string text, float chance)
    {
        const string junk = "#%&?*/\\=+";
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] != ' ' && GD.Randf() < chance)
                chars[i] = junk[(int)(GD.Randi() % (uint)junk.Length)];
        return new string(chars);
    }

    private void DrawNoise()
    {
        if (!StaticFault || _solved) return;
        var size = _noise.Size;

        // horizontal scan bands
        for (int i = 0; i < 14; i++)
        {
            float y = GD.Randf() * size.Y;
            float h = 1f + GD.Randf() * 3f;
            float x = GD.Randf() * size.X * 0.5f;
            float w = size.X * (0.3f + GD.Randf() * 0.7f);
            _noise.DrawRect(new Rect2(x, y, w, h), new Color(1, 1, 1, 0.12f + GD.Randf() * 0.25f));
        }

        // a couple of red tear lines
        for (int i = 0; i < 2; i++)
        {
            float y = GD.Randf() * size.Y;
            _noise.DrawRect(new Rect2(0, y, size.X, 2f), new Color(StaticRed, 0.55f));
        }

        // speckle
        for (int i = 0; i < 50; i++)
        {
            var p = new Vector2(GD.Randf() * size.X, GD.Randf() * size.Y);
            float s = 1f + GD.Randf() * 2f;
            _noise.DrawRect(new Rect2(p, new Vector2(s, s)), new Color(1, 1, 1, 0.2f + GD.Randf() * 0.4f));
        }
    }

    // ===== status =====
    private void UpdateStatusText()
    {
        if (_status == null || _solved) return;
        _status.Text = StaticFault
            ? "⚡ Static on the line! Ask the Technician how to clear it."
            : "Read the line to the Technician, then enter the code.";
    }

    // ===== input =====
    private void OnKey(string key)
    {
        if (_solved) return;

        if (key == "CLEAR")
        {
            _currentEntry = "";
        }
        else if (key == "ROUTE")
        {
            if (_currentEntry.Length > 0) GameSession.Instance.SubmitKeypad(_currentEntry);
            else _status.Text = "Type a code first.";
            _currentEntry = "";
        }
        else if (_currentEntry.Length < 6)
        {
            _currentEntry += key;
        }

        _entry.Text = _currentEntry == "" ? "_" : _currentEntry;
    }

    private void OnFeedback(string text, bool success)
    {
        if (_solved || !text.StartsWith(GameSession.KeypadTag)) return;
        _status.Text = text.Substring(GameSession.KeypadTag.Length);
    }

    public void SetSolved()
    {
        if (_solved) return;
        _solved = true;
        foreach (var b in _keys) b.Disabled = true;

        ApplyStaticLook();
        _display.AddThemeStyleboxOverride("panel", ScreenStyle(SolvedGreen, 4));
        _line.Text = _lineValue > 0 ? $"LINE {_lineValue}" : "NO LINE";
        _entry.Text = "✔";
        _status.Text = "✔ SOLVED";
        EmitSignal(SignalName.Solved);
    }
}