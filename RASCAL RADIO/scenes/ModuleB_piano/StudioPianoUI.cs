using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Operator view for the Studio Piano: label plate, a lamp, octave buttons and seven keys.
/// It only DISPLAYS state and RAISES requests.
/// </summary>
public partial class StudioPianoUI : PanelContainer
{
    public event Action<string> KeyPressed;
    public event Action<int> OctaveStepPressed;

    /// <summary>Small progress dots under the lamp. Set false to hide them.</summary>
    public bool ShowStepDots { get; set; } = true;

    /// <summary>The faint side bars on the faceplate. Set false for a plain faceplate (screws stay).</summary>
    public bool ShowDecor { get; set; } = true;

    /// <summary>Optional font for the label plate, for example "res://fonts/MyFont.ttf". Empty = the default font.</summary>
    public string LabelFontPath { get; set; } = "";

    // ===== COLORS (change these to retune the look) =====
    private static readonly Color NeutralLamp = new("E3B05A");   // the real colors are on the Broadcaster's screen now
    private static readonly Color FrameBg = new("D9BE8F");      // warm tan outer frame
    private static readonly Color FrameEdge = new("5A4128");    // brown outlines
    private static readonly Color Ring = new("262C2A");         // dark ring between frame and faceplate
    private static readonly Color Face = new("3E6B69");         // dusty teal faceplate
    private static readonly Color Plate = new("E3C28F");        // label plate and octave buttons
    private static readonly Color PlateInk = new("3A2A1E");     // dark brown text on tan
    private static readonly Color TextOnFace = new("E3C28F");   // tan text on the teal
    private static readonly Color KeyCream = new("F3E2BE");     // warm cream keys (the lightest thing)
    private static readonly Color TrayBg = new("1F2625");       // slot the keys sit in
    private static readonly Color DotOn = new("E3B05A");
    private static readonly Color DotOff = new("1E2B2A");
    private static readonly Color Good = new("43A047");
    private static readonly Color Alarm = new("E04A32");

    private StudioPianoModule _module;
    private bool _built;

    private PianoLamp _lamp;
    private Label _nameLabel;
    private Label _noteLabel;          // only used when the module shows notes (test scene)
    private HBoxContainer _dots;
    private HBoxContainer _octaveRow;
    private Label _octaveLabel;
    private HBoxContainer _keysRow;
    private Tween _faultTween;
    private StudioPianoSound _sound;
    private readonly Dictionary<string, Button> _keyButtons = new();

    /// <summary>Use Sound.Muted to switch the piano notes on or off.</summary>
    public StudioPianoSound Sound => _sound;

    public override void _Ready() => EnsureBuilt();

    public override void _ExitTree()
    {
        StopFaultFlash();
        if (_module == null) return;
        _module.Configured -= RefreshAll;
        _module.StatusChanged -= OnStatusChanged;
        _module.CueChanged -= OnCueChanged;
        _module.OctaveChanged -= OnOctaveChanged;
        _module.KeyResult -= OnKeyResult;
        _module.FaultChanged -= OnFaultChanged;
        _module.FaultProgressChanged -= OnFaultProgressChanged;
        _module = null;
    }

    /// <summary>Call once. Reconfiguring the module later is fine.</summary>
    public void Bind(StudioPianoModule module)
    {
        EnsureBuilt();
        _module = module;
        module.Configured += RefreshAll;
        module.StatusChanged += OnStatusChanged;
        module.CueChanged += OnCueChanged;
        module.OctaveChanged += OnOctaveChanged;
        module.KeyResult += OnKeyResult;
        module.FaultChanged += OnFaultChanged;
        module.FaultProgressChanged += OnFaultProgressChanged;
        RefreshAll();
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _sound = new StudioPianoSound();
        AddChild(_sound);

        // 1) warm tan outer frame
        var frame = new StyleBoxFlat { BgColor = FrameBg, BorderColor = FrameEdge };
        frame.SetBorderWidthAll(3);
        frame.SetCornerRadiusAll(18);
        frame.SetContentMarginAll(10);
        AddThemeStyleboxOverride("panel", frame);

        // 2) dark ring
        var ring = new PanelContainer();
        var ringStyle = new StyleBoxFlat { BgColor = Ring, BorderColor = FrameEdge };
        ringStyle.SetBorderWidthAll(2);
        ringStyle.SetCornerRadiusAll(13);
        ringStyle.SetContentMarginAll(8);
        ring.AddThemeStyleboxOverride("panel", ringStyle);
        AddChild(ring);

        // 3) teal faceplate
        var face = new PanelContainer();
        var faceStyle = new StyleBoxFlat { BgColor = Face };
        faceStyle.SetCornerRadiusAll(8);
        faceStyle.SetContentMarginAll(0);
        face.AddThemeStyleboxOverride("panel", faceStyle);
        ring.AddChild(face);

        face.AddChild(new PianoFaceDecor { ShowBars = ShowDecor });   // behind the controls (first child)

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 26);
        margin.AddThemeConstantOverride("margin_right", 26);
        margin.AddThemeConstantOverride("margin_top", 22);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        face.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        margin.AddChild(root);

        // Label plate with the jingle name
        var plate = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(380, 0),
        };
        var plateStyle = new StyleBoxFlat { BgColor = Plate, BorderColor = FrameEdge };
        plateStyle.SetBorderWidthAll(2);
        plateStyle.SetCornerRadiusAll(4);
        plateStyle.ContentMarginLeft = 14; plateStyle.ContentMarginRight = 14;
        plateStyle.ContentMarginTop = 4; plateStyle.ContentMarginBottom = 4;
        plate.AddThemeStyleboxOverride("panel", plateStyle);
        root.AddChild(plate);

        _nameLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        if (!string.IsNullOrEmpty(LabelFontPath) && ResourceLoader.Exists(LabelFontPath))
            _nameLabel.AddThemeFontOverride("font", GD.Load<Font>(LabelFontPath));
        _nameLabel.AddThemeFontSizeOverride("font_size", 24);
        _nameLabel.AddThemeColorOverride("font_color", PlateInk);
        plate.AddChild(_nameLabel);

        // The lamp
        _lamp = new PianoLamp { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        root.AddChild(_lamp);

        // Progress dots
        _dots = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _dots.AddThemeConstantOverride("separation", 8);
        root.AddChild(_dots);

        // Letter hint, only when the module is set to show notes (test scene)
        _noteLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _noteLabel.AddThemeFontSizeOverride("font_size", 18);
        _noteLabel.AddThemeColorOverride("font_color", TextOnFace);
        root.AddChild(_noteLabel);

        // Octave:  [-]  OCTAVE: NORMAL  [+]
        _octaveRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _octaveRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(_octaveRow);

        var minus = MakeSmallButton("-");
        minus.Pressed += () => OctaveStepPressed?.Invoke(-1);
        _octaveRow.AddChild(minus);

        _octaveLabel = new Label
        {
            CustomMinimumSize = new Vector2(170, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _octaveLabel.AddThemeFontSizeOverride("font_size", 16);
        _octaveLabel.AddThemeColorOverride("font_color", TextOnFace);
        _octaveRow.AddChild(_octaveLabel);

        var plus = MakeSmallButton("+");
        plus.Pressed += () => OctaveStepPressed?.Invoke(1);
        _octaveRow.AddChild(plus);

        // Seven cream keys in a dark tray
        var tray = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        var trayStyle = new StyleBoxFlat { BgColor = TrayBg, BorderColor = FrameEdge };
        trayStyle.SetBorderWidthAll(2);
        trayStyle.SetCornerRadiusAll(8);
        trayStyle.SetContentMarginAll(6);
        tray.AddThemeStyleboxOverride("panel", trayStyle);
        root.AddChild(tray);

        _keysRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _keysRow.AddThemeConstantOverride("separation", 4);
        tray.AddChild(_keysRow);
        foreach (var letter in StudioPianoData.Keys)
        {
            var key = new Button { Text = letter, CustomMinimumSize = new Vector2(70, 150) };
            key.AddThemeFontSizeOverride("font_size", 28);
            key.AddThemeStyleboxOverride("normal", MakeKeyStyle(KeyCream, 7));
            key.AddThemeStyleboxOverride("hover", MakeKeyStyle(KeyCream.Lightened(0.35f), 7));
            key.AddThemeStyleboxOverride("pressed", MakeKeyStyle(KeyCream.Darkened(0.12f), 2));
            key.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            foreach (var colorName in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                key.AddThemeColorOverride(colorName, PlateInk);
            key.Pressed += () => KeyPressed?.Invoke(letter);
            _keysRow.AddChild(key);
            _keyButtons[letter] = key;
        }
    }

    private static StyleBoxFlat MakeKeyStyle(Color background, int lip)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = PlateInk };
        style.SetBorderWidthAll(2);
        style.BorderWidthBottom = lip;               // thick bottom edge = the "lip" of a key
        style.CornerRadiusTopLeft = 3;
        style.CornerRadiusTopRight = 3;
        style.CornerRadiusBottomLeft = 10;
        style.CornerRadiusBottomRight = 10;
        style.ContentMarginTop = 85;                 // pushes the letter toward the bottom of the key
        return style;
    }

    private static Button MakeSmallButton(string text)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(52, 40) };
        b.AddThemeFontSizeOverride("font_size", 22);
        b.AddThemeStyleboxOverride("normal", Pill(Plate, 5));
        b.AddThemeStyleboxOverride("hover", Pill(Plate.Lightened(0.2f), 5));
        b.AddThemeStyleboxOverride("pressed", Pill(Plate.Darkened(0.12f), 1));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            b.AddThemeColorOverride(c, PlateInk);
        return b;
    }

    private static StyleBoxFlat Pill(Color bg, int lip)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = FrameEdge };
        s.SetBorderWidthAll(2);
        s.BorderWidthBottom = lip;
        s.SetCornerRadiusAll(8);
        return s;
    }

    private void SetDots(int total, int done)
    {
        foreach (Node n in _dots.GetChildren()) { _dots.RemoveChild(n); n.QueueFree(); }
        _dots.Visible = ShowStepDots && total > 0;
        for (int i = 0; i < total; i++)
        {
            var dot = new Panel { CustomMinimumSize = new Vector2(12, 12) };
            var s = new StyleBoxFlat { BgColor = i < done ? DotOn : DotOff };
            s.SetCornerRadiusAll(6);
            dot.AddThemeStyleboxOverride("panel", s);
            _dots.AddChild(dot);
        }
    }

    private void RefreshAll()
    {
        _octaveRow.Visible = _module.UseOctave;
        OnOctaveChanged(_module.Octave);

        if (_module.InFault)
        {
            ShowFault();
            StartFaultFlash();
        }
        else
        {
            StopFaultFlash();
            if (_module.Cues.Count == 0)
            {
                _nameLabel.Text = "STANDING BY";
                _noteLabel.Visible = false;
                _lamp.TurnOff();
                SetDots(0, 0);
            }
            else
            {
                int index = Mathf.Clamp(_module.CueIndex, 0, _module.Cues.Count - 1);
                OnCueChanged(index, _module.Cues.Count, _module.Cues[index]);
            }
        }
        OnStatusChanged(_module.Status);
    }

    private void OnStatusChanged(StudioPianoModule.PianoStatus status)
    {
        if (status != StudioPianoModule.PianoStatus.Complete) return;
        _nameLabel.Text = "JINGLE PLAYED";
        _noteLabel.Visible = false;
        _lamp.TurnOn(Good);
        SetDots(_module.Cues.Count, _module.Cues.Count);
    }

    private void OnCueChanged(int index, int total, StudioPianoData.Cue cue)
    {
        _nameLabel.Text = _module.SequenceName.ToUpper();
        _lamp.TurnOn(NeutralLamp);
        SetDots(total, index);

        _noteLabel.Visible = true;
        _noteLabel.Text = _module.ShowNotes
            ? (_module.UseOctave ? $"{cue.Letter}    OCTAVE {StudioPianoData.OctaveLabel(cue.Octave)}" : cue.Letter)
            : "Ask the Broadcaster for the color";
    }

    private void OnOctaveChanged(int octave)
    {
        _octaveLabel.Text = $"OCTAVE: {StudioPianoData.OctaveLabel(octave)}";
    }

    private void OnKeyResult(string letter, bool correct)
    {
        if (!_keyButtons.TryGetValue(letter, out var button)) return;
        button.Modulate = correct ? new Color(0.5f, 1.0f, 0.5f) : new Color(1.0f, 0.75f, 0.3f);
        CreateTween().TweenProperty(button, "modulate", Colors.White, 0.4);
        if (!correct) _lamp.Nudge();

        // Every accepted press plays its note (right or wrong, like a real piano).
        // Recalibration has no octave in the manual, so it always plays at NORMAL.
        int octave = (_module.UseOctave && !_module.InFault) ? _module.Octave : 0;
        _sound.PlayNote(letter, octave);
    }

    private void OnFaultChanged(bool inFault)
    {
        if (inFault)
        {
            ShowFault();
            StartFaultFlash();
        }
        else
        {
            StopFaultFlash();   // the module re-emits the interrupted cue, which restores the display
        }
    }

    private void OnFaultProgressChanged(int done, int total) => SetDots(total, done);

    private void ShowFault()
    {
        _nameLabel.Text = "RECALIBRATE";
        _noteLabel.Visible = false;
        _lamp.TurnOn(Alarm, true);   // blinking red
        SetDots(7, _module.FaultIndex);
    }

    // Keys flash red while the module is in a fault.
    private void StartFaultFlash()
    {
        StopFaultFlash();
        if (!IsInsideTree()) return;
        _faultTween = CreateTween().SetLoops();
        _faultTween.TweenProperty(_keysRow, "modulate", new Color(1.0f, 0.35f, 0.35f), 0.4);
        _faultTween.TweenProperty(_keysRow, "modulate", Colors.White, 0.4);
    }

    private void StopFaultFlash()
    {
        _faultTween?.Kill();
        _faultTween = null;
        if (_keysRow != null) _keysRow.Modulate = Colors.White;
    }
}