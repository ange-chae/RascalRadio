using Godot;
using System.Collections.Generic;

// Credits scene for Rascal Radio. Everything is built in code, so the scene
// only needs a plain Control root with this script attached.
public partial class Credits : Control
{
    // ---- fonts (optional: assign in the Inspector, otherwise it searches res://assets/fonts) ----
    [Export] public Font LabelFont { get; set; }   // Fredoka Medium
    [Export] public Font NameFont { get; set; }    // Fredoka SemiBold
    [Export] public Font BoldFont { get; set; }    // Fredoka Bold (title only)

    // ---- images ----
    [Export] public Texture2D TeamLogo { get; set; }
    [Export] public Texture2D Background { get; set; }   // optional, shown very dimmed

    // ---- layout ----
    [Export] public int MarginTop { get; set; } = 36;
    [Export] public int MarginBottom { get; set; } = 28;
    [Export] public int LogoSize { get; set; } = 110;

    // ---- old-TV overlay ----
    [Export] public bool StaticOverlay { get; set; } = true;
    [Export(PropertyHint.Range, "0,0.3,0.005")] public float StaticStrength { get; set; } = 0.07f;
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float ScanlineStrength { get; set; } = 0.12f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float VignetteStrength { get; set; } = 0.40f;

    [Export(PropertyHint.File, "*.tscn")]
    public string MenuScenePath { get; set; } = "res://scenes/menu.tscn";

    // ---------- palette ----------
    private static readonly Color BgDark = new Color("14101A");
    private static readonly Color Yellow = new Color("FFD35A");
    private static readonly Color Cream = new Color("F6EBD3");
    private static readonly Color Muted = new Color("8C7F93");
    private static readonly Color Outline = new Color("2A1530");
    private static readonly Color OnAirRed = new Color("F2452E");

    // ---------- credits data (edit these!) ----------
    // col 0 = left column, col 1 = right column
    private static readonly (int col, string role, string[] names)[] Entries =
    {
        (0, "Game Director",       new[] { "Christian G. Baitec" }),
        (0, "Lead Programmer",     new[] { "Mary Angeline Denise V. Bajo" }),
        (0, "Producer",            new[] { "Christian G. Baitec", "Mary Angeline Denise V. Bajo", "James S. Diaz", "Monique Anne C. Frondozo" }),
        (0, "Art & Illustration",  new[] { "Mary Angeline Denise V. Bajo", "Monique Anne C. Frondozo" }),
        (1, "Sound & Music",       new[] { "Christian G. Baitec", "Monique Anne C. Frondozo", "James S. Diaz" }),

        (1, "GAME SYSTEM DESIGNER",      new[] { "Christian G. Baitec", "Mary Angeline Denise V. Bajo", "James S. Diaz" }),
        (1, "Voice of the Boss",   new[] { "Joshiel Leonil Dela Cruz" }),
        (1, "Special Thanks",      new[] { "Rommel L. Dorin" }),
    };

    private const string FontFolder = "res://assets/fonts";
    private readonly List<Control> _entryNodes = new();

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        LabelFont ??= FindFredoka("medium") ?? FindFredoka("regular");
        NameFont ??= FindFredoka("semibold", "semi-bold", "semi_bold") ?? FindFredoka("medium") ?? FindFredoka("regular");
        LabelFont ??= NameFont;
        NameFont ??= LabelFont;
        BoldFont ??= FindFredokaBold() ?? NameFont;
        if (NameFont == null)
            GD.Print("Credits: Fredoka not found in " + FontFolder + ". Assign Label Font / Name Font in the Inspector.");

        BuildBackground();
        BuildLayout();
        if (StaticOverlay) BuildStaticOverlay();
        PlayIntro();
    }

    // ------------------------------------------------------------------ build

    private void BuildBackground()
    {
        var bg = new ColorRect { Color = BgDark, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        if (Background != null)
        {
            var pic = new TextureRect
            {
                Texture = Background,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Modulate = new Color(1, 1, 1, 0.12f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            pic.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(pic);
        }
    }

    private void BuildLayout()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_top", MarginTop);
        margin.AddThemeConstantOverride("margin_bottom", MarginBottom);
        margin.AddThemeConstantOverride("margin_left", 40);
        margin.AddThemeConstantOverride("margin_right", 40);
        AddChild(margin);

        var center = new CenterContainer();
        margin.AddChild(center);

        var main = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        main.AddThemeConstantOverride("separation", 14);
        center.AddChild(main);

        main.AddChild(BuildTitle());
        main.AddChild(BuildColumns());
        main.AddChild(BuildLogo());
        main.AddChild(BuildFooter());
    }

    // Title block: small show tag, big "CREDITS", and a thin radio tuner dial
    private Control BuildTitle()
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        box.AddThemeConstantOverride("separation", 2);

        Label tag = MakeLabel("RASCAL RADIO", 14, Yellow, LabelFont);
        tag.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(tag);

        Label title = MakeLabel("CREDITS", 40, Cream, BoldFont);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        // tuner dial: small ticks, longer every 5th, one red needle in the middle
        //var dial = new HBoxContainer
        //{
        //    SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        //    CustomMinimumSize = new Vector2(0, 24)
        //};
        //dial.AddThemeConstantOverride("separation", 7);

        //const int count = 31;
        //for (int i = 0; i < count; i++)
        //{
        //    bool needle = i == count / 2;
        //    bool major = i % 5 == 0;
        //    dial.AddChild(new ColorRect
        //    {
        //        Color = needle ? OnAirRed : (major ? Cream : Muted),
        //        CustomMinimumSize = new Vector2(2, needle ? 24 : (major ? 16 : 9)),
        //        SizeFlagsVertical = SizeFlags.ShrinkEnd,
        //        MouseFilter = MouseFilterEnum.Ignore
        //    });
        //}
        //box.AddChild(dial);
        return box;
    }

    private Control BuildColumns()
    {
        var cols = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        cols.AddThemeConstantOverride("separation", 180);

        var left = new VBoxContainer();
        var right = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 14);
        right.AddThemeConstantOverride("separation", 14);
        cols.AddChild(left);
        cols.AddChild(right);

        foreach (var (col, role, names) in Entries)
        {
            Control entry = MakeEntry(role, names);
            (col == 0 ? left : right).AddChild(entry);
            _entryNodes.Add(entry);
        }
        return cols;
    }

    private Control MakeEntry(string role, string[] names)
    {
        var box = new VBoxContainer { Modulate = new Color(1, 1, 1, 0) };
        box.AddThemeConstantOverride("separation", 0);

        Label roleLabel = MakeLabel(role.ToUpper(), 13, Yellow, LabelFont);
        roleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        box.AddChild(roleLabel);

        foreach (string n in names)
        {
            Label nameLabel = MakeLabel(n.ToUpper(), 22, Cream, NameFont);
            nameLabel.HorizontalAlignment = HorizontalAlignment.Left;
            box.AddChild(nameLabel);
        }

        return box;
    }

    // Team logo: no frame, just the image with a soft warm glow behind it.
    // (A plain Control, not a container, so the glow can spill past its size.)
    private Control BuildLogo()
    {
        var slot = new Control
        {
            CustomMinimumSize = new Vector2(LogoSize * 1.6f, LogoSize * 0.8f),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore
        };

        // soft radial glow
        //var grad = new Gradient();
        //grad.SetColor(0, new Color(Yellow, 0.26f));
        //grad.SetColor(1, new Color(Yellow, 0f));
        //var glowTex = new GradientTexture2D
        //{
        //    Gradient = grad,
        //    Fill = GradientTexture2D.FillEnum.Radial,
        //    FillFrom = new Vector2(0.5f, 0.5f),
        //    FillTo = new Vector2(1f, 0.5f),
        //    Width = 256,
        //    Height = 256
        //};
        //float glowSize = LogoSize * 2.6f;
        //var glow = new TextureRect
        //{
        //    Texture = glowTex,
        //    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        //    StretchMode = TextureRect.StretchModeEnum.Scale,
        //    MouseFilter = MouseFilterEnum.Ignore,
        //    Position = new Vector2(-(glowSize - LogoSize) / 2f, -(glowSize - LogoSize) / 2f),
        //    Size = new Vector2(glowSize, glowSize)
        //};
        //slot.AddChild(glow);

        if (TeamLogo != null)
        {
            var logo = new TextureRect
            {
                Texture = TeamLogo,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore
            };
            logo.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            slot.AddChild(logo);
        }
        else
        {
            var hint = MakeLabel("TEAM LOGO", 13, Muted, LabelFont);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            slot.AddChild(hint);
        }
        return slot;
    }

    // Small footer under the logo: thank-you line, then how to leave
    private Control BuildFooter()
    {
        var footer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        footer.AddThemeConstantOverride("separation", 4);

        Label thanks = MakeLabel("THANK YOU FOR PLAYING OUR GAME", 14, Cream, LabelFont);
        thanks.HorizontalAlignment = HorizontalAlignment.Center;
        thanks.Modulate = new Color(1, 1, 1, 0);
        footer.AddChild(thanks);
        _entryNodes.Add(thanks);   // fades in with the credits

        Label hint = MakeLabel("PRESS ESC TO GO BACK", 12, Muted, LabelFont);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Modulate = new Color(1, 1, 1, 0);
        footer.AddChild(hint);
        _entryNodes.Add(hint);     // fades in last

        return footer;
    }

    // ------------------------------------------------- old TV static overlay

    private const string StaticShader = @"
shader_type canvas_item;

uniform float noise_strength = 0.07;
uniform float scanline_strength = 0.12;
uniform float scanline_count = 240.0;
uniform float vignette_strength = 0.4;
uniform vec2 grain_grid = vec2(320.0, 180.0);

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	// grain that changes ~24 times per second
	float frame = floor(TIME * 24.0);
	float n = hash(floor(UV * grain_grid) + frame * 17.0) - 0.5;
	float a_noise = abs(n) * 2.0 * noise_strength;
	vec3 noise_col = vec3(n > 0.0 ? 1.0 : 0.0);

	// faint horizontal scanlines
	float scan = 0.5 + 0.5 * sin(UV.y * scanline_count * 6.28318);
	float a_dark = scan * scanline_strength;

	// darker corners
	float v = smoothstep(0.35, 0.85, distance(UV, vec2(0.5)));
	a_dark = clamp(a_dark + v * vignette_strength, 0.0, 1.0);

	// slow, very soft brightness flicker
	a_noise *= 0.85 + 0.15 * sin(TIME * 7.0);

	// noise on top of the darkening layer
	float a = a_noise + a_dark * (1.0 - a_noise);
	vec3 rgb = (noise_col * a_noise) / max(a, 0.0001);
	COLOR = vec4(rgb, a);
}
";

    private void BuildStaticOverlay()
    {
        var shader = new Shader { Code = StaticShader };
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("noise_strength", StaticStrength);
        mat.SetShaderParameter("scanline_strength", ScanlineStrength);
        mat.SetShaderParameter("vignette_strength", VignetteStrength);

        var overlay = new ColorRect
        {
            Material = mat,
            MouseFilter = MouseFilterEnum.Ignore
        };
        overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(overlay);   // added last so it draws over everything
    }

    // ----------------------------------------------------------------- helpers

    private static Label MakeLabel(string text, int size, Color color, Font font)
    {
        var l = new Label { Text = text };
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    // Collects every font file under a folder, including sub-folders (e.g. Fredoka/static)
    private static void CollectFonts(string folder, List<string> found)
    {
        foreach (string raw in DirAccess.GetFilesAt(folder))
        {
            string file = raw.Replace(".import", "").Replace(".remap", "");
            string lower = file.ToLowerInvariant();

            if (lower.EndsWith(".ttf") || lower.EndsWith(".otf") || lower.EndsWith(".woff2") || lower.EndsWith(".woff"))
            {
                string path = folder + "/" + file;
                if (!found.Contains(path))
                    found.Add(path);
            }
        }

        foreach (string dir in DirAccess.GetDirectoriesAt(folder))
            CollectFonts(folder + "/" + dir, found);
    }

    // Plain Fredoka only (skips the Condensed / Expanded families that ship with it)
    private static bool IsPlainFredoka(string lowerPath)
    {
        string name = lowerPath.Substring(lowerPath.LastIndexOf('/') + 1);
        return name.Contains("fredoka") && !name.Contains("condensed") && !name.Contains("expanded");
    }

    // Finds a Fredoka file whose name contains one of the keywords (e.g. "medium")
    private static Font FindFredoka(params string[] keywords)
    {
        var files = new List<string>();
        CollectFonts(FontFolder, files);

        foreach (string path in files)
        {
            string lower = path.ToLowerInvariant();
            if (!IsPlainFredoka(lower)) continue;

            string name = lower.Substring(lower.LastIndexOf('/') + 1);
            foreach (string kw in keywords)
                if (name.Contains(kw))
                    return GD.Load<Font>(path);
        }
        return null;
    }

    // Finds Fredoka Bold (but not SemiBold)
    private static Font FindFredokaBold()
    {
        var files = new List<string>();
        CollectFonts(FontFolder, files);

        foreach (string path in files)
        {
            string lower = path.ToLowerInvariant();
            if (!IsPlainFredoka(lower)) continue;

            string name = lower.Substring(lower.LastIndexOf('/') + 1);
            if (name.Contains("bold") && !name.Contains("semi"))
                return GD.Load<Font>(path);
        }
        return null;
    }

    // --------------------------------------------------------------- behaviour

    private void PlayIntro()
    {
        for (int i = 0; i < _entryNodes.Count; i++)
        {
            Tween tw = CreateTween();
            tw.TweenInterval(0.3 + i * 0.18);
            tw.TweenProperty(_entryNodes[i], "modulate:a", 1.0f, 0.45f);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel") || @event.IsActionPressed("ui_accept"))
        {
            GetViewport().SetInputAsHandled();
            GoBack();
        }
    }

    private void GoBack()
    {
        Error err = GetTree().ChangeSceneToFile(MenuScenePath);
        if (err != Error.Ok)
            GD.PrintErr($"Failed to load menu scene: {err}");
    }
}