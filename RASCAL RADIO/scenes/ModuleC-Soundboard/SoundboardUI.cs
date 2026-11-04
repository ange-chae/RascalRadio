using System;
using System.Collections.Generic;
using Godot;

public partial class SoundboardUI : PanelContainer
{
    public event Action<string> PadPressed;
    public event Action ResetSequencePressed;

    // ------------------------------------------------------------------
    // SFX NAMES - edit the values on the right to rename the pads and cues.
    // The keys (RED, BLUE...) are the internal pad ids used by SoundboardData,
    // the module and the sequences, so don't change those.
    // The Technician's manual must use these same names.
    // ------------------------------------------------------------------
    private static readonly Dictionary<string, string> PadNames = new()
    {
        { "RED",    "BUZZER" },
        { "BLUE",   "PHONE" },
        { "GREEN",  "APPLAUSE" },
        { "YELLOW", "DRUM ROLL" },
    };

    public static string NameOf(string padId) =>
        PadNames.TryGetValue(padId, out var name) ? name : padId;

    // Palette (matches the game's buttons and manual)
    private static readonly Color Ink = new Color(0.1254902f, 0.09019608f, 0.25490198f);   // navy border
    private static readonly Color Body = Color.FromHtml("4a2a2c");                          // device casing
    private static readonly Color LcdBg = Color.FromHtml("14211f");
    private static readonly Color LcdText = Color.FromHtml("a5d6a7");
    private static readonly Color Amber = Color.FromHtml("ffd54f");
    private static readonly Color Mustard = Color.FromHtml("ffd166");

    private SoundboardModule _module;
    private bool _built;
    private SoundboardSound _sound;

    private PanelContainer _lcd;
    private PanelContainer _led;
    private StyleBoxFlat _ledStyle;
    private Label _statusLabel;
    private Label _timerLabel;
    private HBoxContainer _cueRow;
    private readonly Dictionary<string, Button> _padButtons = new();

    public SoundboardSound Sound => _sound;

    public override void _Ready()
    {
        EnsureBuilt();
    }

    public override void _ExitTree()
    {
        Unbind();
    }

    public void Bind(SoundboardModule module)
    {
        Unbind();
        EnsureBuilt();
        _module = module;
        module.Configured += RefreshAll;
        module.StatusChanged += OnStatusChanged;
        module.ProgressChanged += OnProgressChanged;
        module.PadResult += OnPadResult;
        module.TimeChanged += OnTimeChanged;
        RefreshAll();
    }

    private void Unbind()
    {
        if (_module == null) return;
        _module.Configured -= RefreshAll;
        _module.StatusChanged -= OnStatusChanged;
        _module.ProgressChanged -= OnProgressChanged;
        _module.PadResult -= OnPadResult;
        _module.TimeChanged -= OnTimeChanged;
        _module = null;
    }

    // ---------------------------------------------------------------- build

    private static Color PadColor(string padId) => padId switch
    {
        "RED" => Color.FromHtml("e57373"),
        "BLUE" => Color.FromHtml("64b5f6"),
        "GREEN" => Color.FromHtml("81c784"),
        "YELLOW" => Color.FromHtml("fff176"),
        _ => Color.FromHtml("90a4ae"),
    };

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _sound = new SoundboardSound();
        AddChild(_sound);

        // Casing of the soundboard
        var casing = new StyleBoxFlat { BgColor = Body, BorderColor = Ink };
        casing.SetBorderWidthAll(4);
        casing.BorderWidthBottom = 10;
        casing.SetCornerRadiusAll(16);
        AddThemeStyleboxOverride("panel", casing);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 18);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 16);
        margin.AddChild(root);

        root.AddChild(BuildLcd());
        root.AddChild(BuildPadGrid());
        root.AddChild(BuildResetButton());
    }

    // The screen at the top: status LED, timer and the cue sequence
    private Control BuildLcd()
    {
        _lcd = new PanelContainer();
        var lcdStyle = new StyleBoxFlat { BgColor = LcdBg, BorderColor = Ink };
        lcdStyle.SetBorderWidthAll(3);
        lcdStyle.SetCornerRadiusAll(8);
        lcdStyle.SetContentMarginAll(12);
        _lcd.AddThemeStyleboxOverride("panel", lcdStyle);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        _lcd.AddChild(box);

        // top row: LED + status on the left, timer on the right
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        box.AddChild(top);

        _led = new PanelContainer { CustomMinimumSize = new Vector2(16, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _ledStyle = new StyleBoxFlat { BgColor = Colors.White };
        _ledStyle.SetCornerRadiusAll(8);
        _led.AddThemeStyleboxOverride("panel", _ledStyle);
        top.AddChild(_led);

        _statusLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _statusLabel.AddThemeFontSizeOverride("font_size", 16);
        _statusLabel.AddThemeColorOverride("font_color", LcdText);
        top.AddChild(_statusLabel);

        _timerLabel = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _timerLabel.AddThemeFontSizeOverride("font_size", 18);
        _timerLabel.AddThemeColorOverride("font_color", Amber);
        top.AddChild(_timerLabel);

        _cueRow = new HBoxContainer();
        _cueRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(_cueRow);

        return _lcd;
    }

    // The pads: big, chunky, 2x2 like a real sampler
    private Control BuildPadGrid()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);

        foreach (string padId in SoundboardData.Pads)
        {
            var button = new Button
            {
                Text = NameOf(padId),
                CustomMinimumSize = new Vector2(170, 100),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.None
            };
            button.AddThemeFontSizeOverride("font_size", 20);

            Color baseColor = PadColor(padId);
            button.AddThemeStyleboxOverride("normal", MakePadStyle(baseColor, 10));
            button.AddThemeStyleboxOverride("hover", MakePadStyle(baseColor.Lightened(0.2f), 10));
            button.AddThemeStyleboxOverride("pressed", MakePadStyle(baseColor.Darkened(0.15f), 3));
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            foreach (var colorName in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                button.AddThemeColorOverride(colorName, Ink);

            string captured = padId;
            button.Pressed += () => PadPressed?.Invoke(captured);
            grid.AddChild(button);
            _padButtons[padId] = button;
        }
        return grid;
    }

    private Control BuildResetButton()
    {
        var reset = new Button
        {
            Text = "RESET",
            CustomMinimumSize = new Vector2(170, 52),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None
        };
        reset.AddThemeFontSizeOverride("font_size", 20);
        reset.AddThemeStyleboxOverride("normal", MakePadStyle(Mustard, 8));
        reset.AddThemeStyleboxOverride("hover", MakePadStyle(Mustard.Lightened(0.35f), 8));
        reset.AddThemeStyleboxOverride("pressed", MakePadStyle(Mustard, 3));
        reset.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        foreach (var colorName in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            reset.AddThemeColorOverride(colorName, Ink);
        reset.Pressed += () => ResetSequencePressed?.Invoke();
        return reset;
    }

    private static StyleBoxFlat MakePadStyle(Color color, int bottomBorder)
    {
        var style = new StyleBoxFlat { BgColor = color, BorderColor = Ink };
        style.SetBorderWidthAll(3);
        style.BorderWidthBottom = bottomBorder;
        style.SetCornerRadiusAll(12);
        return style;
    }

    // One cell on the LCD: just the cue name, no extra text
    private static PanelContainer MakeCue(string text, bool done, bool isNext)
    {
        var style = new StyleBoxFlat
        {
            BgColor = done ? Color.FromHtml("2e7d32") : Color.FromHtml("0d1716"),
            BorderColor = isNext ? Amber : Color.FromHtml("37474f"),
        };
        style.SetBorderWidthAll(isNext ? 3 : 1);
        style.SetCornerRadiusAll(6);
        style.SetContentMarginAll(8);

        var cell = new PanelContainer
        {
            CustomMinimumSize = new Vector2(96, 54),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        cell.AddThemeStyleboxOverride("panel", style);

        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeFontSizeOverride("font_size", 15);
        label.AddThemeColorOverride("font_color", done ? Colors.White : (isNext ? Amber : LcdText));
        cell.AddChild(label);
        return cell;
    }

    // -------------------------------------------------------------- refresh

    private void RefreshAll()
    {
        if (_module == null) return;
        OnStatusChanged(_module.Status);
        OnProgressChanged(_module.Progress, _module.Sequence.Count);
        OnTimeChanged(_module.TimeLeft);
    }

    private void RebuildSequence()
    {
        if (_module == null || !IsInstanceValid(_cueRow)) return;

        foreach (Node child in _cueRow.GetChildren())
        {
            _cueRow.RemoveChild(child);
            child.QueueFree();
        }

        int total = _module.Sequence.Count;
        for (int i = 0; i < total; i++)
        {
            bool done = i < _module.Progress;
            bool isNext = i == _module.Progress
                && _module.Status != SoundboardModule.SoundboardStatus.Complete;

            // Cue shows the real sfx name. Hidden cues show "?" until they are done.
            // Show finished cues and the one the Operator has to do now. Future cues stay hidden.
            string text = (_module.ShowSequence || done || isNext) ? NameOf(_module.Sequence[i]) : "?";
            _cueRow.AddChild(MakeCue(text, done, isNext));
        }
    }

    private void OnStatusChanged(SoundboardModule.SoundboardStatus status)
    {
        switch (status)
        {
            case SoundboardModule.SoundboardStatus.Ready:
                SetStatus("READY", Color.FromHtml("cfd8dc"));
                break;
            case SoundboardModule.SoundboardStatus.Working:
                SetStatus("LIVE", Color.FromHtml("64b5f6"));
                break;
            case SoundboardModule.SoundboardStatus.Check:
                SetStatus("TIME'S UP", Color.FromHtml("ffb74d"));
                break;
            case SoundboardModule.SoundboardStatus.Complete:
                SetStatus("DONE", Color.FromHtml("81c784"));
                break;
            case SoundboardModule.SoundboardStatus.Retry:
                SetStatus("WRONG PAD", Color.FromHtml("e57373"));
                break;
        }
        RebuildSequence();
    }

    private void SetStatus(string text, Color color)
    {
        if (!IsInstanceValid(_statusLabel)) return;
        _statusLabel.Text = text;
        _ledStyle.BgColor = color;
        _led.QueueRedraw();
    }

    private void OnProgressChanged(int done, int total)
    {
        RebuildSequence();
    }

    private void OnPadResult(string padId, bool correct, string expectedNext)
    {
        if (_padButtons.TryGetValue(padId, out var button) && IsInstanceValid(button))
        {
            button.Modulate = correct ? new Color(0.7f, 1.3f, 0.7f) : new Color(1.6f, 0.9f, 0.7f);
            CreateTween().TweenProperty(button, "modulate", Colors.White, 0.4);
        }

        _sound.Play(SoundboardData.SoundFor(padId));

        // Wrong pad: flash the screen red
        if (!correct && IsInstanceValid(_lcd))
        {
            _lcd.Modulate = new Color(1.6f, 0.7f, 0.7f);
            CreateTween().TweenProperty(_lcd, "modulate", Colors.White, 0.5);
        }
    }

    private void OnTimeChanged(float secondsLeft)
    {
        if (_module == null || !IsInstanceValid(_timerLabel)) return;
        _timerLabel.Text = _module.TimeLimit > 0f ? $"{Mathf.CeilToInt(secondsLeft)}s" : "";
    }
}