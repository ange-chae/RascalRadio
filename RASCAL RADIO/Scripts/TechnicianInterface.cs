using Godot;
using RascalRadio.Core.Models;
using static StudioPianoData;
using System.Linq;
using RascalRadio.Modules;

public partial class TechnicianInterface : PopupFrame
{
    // ===== MANUAL PAGES =====
    // One entry per tab button (TabA..TabE), in order.
    private static readonly string[] PageTitles =
    {
    "Module A: Math Keypad",
    "Module B: Piano",
    "Module C: Soundboard",
    "Module D: Simon Says",
    "Module E:",
};

    private const int KeypadPageIndex = 0;
    private const int PianoPageIndex = 1;
    private int _page;
    private RichTextLabel _taskLabel;

    private Button _manualBtn;

    // ===== YOUR INSTRUCTIONS GO HERE =====
    // Old text, kept so you can copy from it. Delete it when you don't need it.
    /*
    private static string MathPage()
    {
        var gs = GameSession.Instance;
        string even = gs?.RuleEvenSuffix ?? "05";
        string odd = gs?.RuleOddSuffix ?? "99";

        return
            "MATH & KEYPAD: LINE OVERRIDE CODE\n\n" +
            "1. Ask the Operator for the incoming line number (1-9). It is only on their keypad.\n" +
            $"2. If the line number is EVEN, append {even}. If it is ODD, append {odd}.\n" +
            "3. Tell the Operator the code. They type it and press ROUTE.\n\n" +
            "CLEARING STATIC FAULTS\n" +
            "Press CLEAR, enter 0000, then press ROUTE.\n\n" +
            (gs != null && gs.IsTutorial ? "In practice mode a wrong code costs nothing." : "");
    }
    */

    private const int ModulePageIndex = 4;

    // Only the Technician sees the level and the suffixes. The Operator only sees the line number.
    private static string MathPage()
    {
        var gs = GameSession.Instance;

        // These can be randomized in GameSession per shift to keep it fresh
        string even = gs?.RuleEvenSuffix ?? "05";
        string odd = gs?.RuleOddSuffix ?? "99";

        return
            "Ask the Operator for the incoming line number. It is only on their keypad.\n\n" +
            $"If the line number is even, append {even}.\n" +
            $"Otherwise, append {odd}.\n\n" +
            "Tell the Operator the code. They type it and press ROUTE.\n" +
            "If there is a static fault, the Operator presses CLEAR, enters 0000, then presses ROUTE.\n\n" +
            (gs != null && gs.IsTutorial ? "In practice mode a wrong code costs nothing." : "") + FlipHelp();
    }

    private static string PianoPage()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("PIANO\n\n");
        sb.Append("The Operator sees the NAME of a jingle. You have the notes. Ask which jingle, find it below, and read the notes top to bottom, one at a time.\n");
        sb.Append("Read the first note aloud. When the lamp changes color, read the next note.\n\n");

        //sb.Append("OCTAVE\n");
        //sb.Append("If the octave is normal, the Operator leaves it alone.\n");
        //sb.Append("Otherwise, if the octave is -, the Operator presses the [lb]-[rb] button first.\n");
        //sb.Append("Otherwise, the Operator presses the [lb]+[rb] button first.\n\n");

        sb.Append("JINGLES\n");
        foreach (var jingle in StudioPianoData.GetJingles())
        {
            sb.Append($"\n{jingle.Key.ToUpper()}\n");
            foreach (var cue in jingle.Value)
            {
                string hex = StudioPianoData.SwatchColors.TryGetValue(cue.ColorName, out var c) ? "#" + c.ToHtml(false) : "#888888";
                sb.Append($"  [color={hex}]{cue.ColorName}[/color], {cue.Letter}, {StudioPianoData.OctaveLabel(cue.Octave)}\n");
            }
        }

        sb.Append("\nIf the keys are flashing red, the Operator plays B A G F E D C. Then the jingle continues.");
        sb.Append(FlipHelp());
        return sb.ToString();
    }

    private static string SoundboardPage()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("The Operator reads out each color that flashes. Check the number of strikes on your computer, then tell them which pad to press.\n");

        string[] flashes = SoundboardData.Pads;
        string[] headings = { "If there are no strikes:", "If there is one strike:", "Otherwise:" };

        for (int row = 0; row < headings.Length; row++)
        {
            sb.Append($"\n[b]{headings[row]}[/b]\n");
            for (int i = 0; i < flashes.Length; i++)
            {
                string flash = flashes[i].ToLower();
                string press = SoundboardData.PadToPress(flashes[i], row).ToLower();

                if (i == flashes.Length - 1) sb.Append($"Otherwise, press {press}.\n");
                else if (i == 0) sb.Append($"If the flash is {flash}, press {press}.\n");
                else sb.Append($"Otherwise, if the flash is {flash}, press {press}.\n");
            }
        }

        sb.Append("\nCheck the strikes again before every flash, because they can change.");
        sb.Append(FlipHelp());
        return sb.ToString();
    }


    public override void _Ready()
    {
        var gs = GameSession.Instance;

        // FIRST, so nothing below can stop the tutorial from appearing
        if (gs != null && gs.IsTutorial) AddChild(new TutorialOverlay { RoleName = "Technician" });

        base._Ready();
        StyleBars();

        var manual = GetNodeOrNull<Control>("%Popup");
        if (manual != null) manual.Visible = false;   // the manual starts closed

        _manualBtn = GetNodeOrNull<Button>("%ManualBtn")
            ?? FindChild("ManualBtn", true, false) as Button
            ?? FindChildren("*", "Button", true, false).OfType<Button>()
                .FirstOrDefault(b => b.Text.ToLower().Contains("manual"));

        if (_manualBtn != null) _manualBtn.Pressed += ToggleManual;
        else GD.PushError("TechnicianInterface: manual button not found. Name it ManualBtn.");

        // NEW: task box setup (step 8b)
        _taskLabel = GetNodeOrNull<RichTextLabel>("%TaskLabel");
        if (_taskLabel != null) { _taskLabel.BbcodeEnabled = true; _taskLabel.FitContent = true; _taskLabel.ScrollActive = false; }

        string[] tabs = { "%TabA", "%TabB", "%TabC", "%TabD", "%TabE" };
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i;
            var b = GetNodeOrNull<Button>(tabs[i]);
            if (b != null) b.Pressed += () => ShowPage(idx);
            else GD.PushError($"TechnicianInterface: {tabs[i]} not found.");
        }
        ShowPage(0);

        if (gs == null) return;

        gs.StepChanged += OnStep;
        gs.QuotaChanged += OnQuota;
        gs.KeypadChanged += OnKeypadChanged;
        gs.AirtimeChanged += OnHealth;
        gs.KeypadChanged += OnHealth;
        gs.StrikesChanged += OnStrikes;
        gs.TasksChanged += RefreshTasks;   // NEW
        gs.TopicChanged += RefreshTasks;   // NEW
        gs.FlipChanged += OnHealth;        // NEW
        if (gs.Piano != null) gs.Piano.FaultChanged += OnPianoFault;
        OnHealth();
        OnStrikes();
        OnQuota();
        RefreshTasks();                    // NEW
        if (gs.IsTutorial) OnStep(gs.CurrentStep);

        foreach (var path in new[] { "%SignalBar", "%QualityBar" })
        {
            var bar = GetNodeOrNull<Control>(path);
            GD.Print($"[Tech] {path}: size={bar?.Size} minSize={bar?.CustomMinimumSize} visible={bar?.IsVisibleInTree()} " +
                     $"pos={bar?.GlobalPosition} parent={bar?.GetParent()?.Name} modulate={bar?.Modulate}");
        }
    }

    private void StyleBars()
    {
        var d = GameSession.Instance?.CurrentLevel;
        int qFloor = d?.QualityFloor ?? 45;
        int sFloor = d?.SignalFloor ?? 30;

        if (GetNodeOrNull<RetroBar>("%QualityBar") is { } q) { q.DangerBelow = qFloor; q.WarnBelow = qFloor + 20; }
        if (GetNodeOrNull<RetroBar>("%SignalBar") is { } s) { s.DangerBelow = sFloor; s.WarnBelow = sFloor + 36; }
    }

    private void ToggleManual()
    {
        var popup = GetNodeOrNull<Control>("%Popup");
        if (popup == null) return;

        popup.Visible = !popup.Visible;
        if (popup.Visible)   // same pop-in as before
        {
            popup.PivotOffset = popup.Size / 2;
            popup.Scale = new Vector2(0.92f, 0.92f);
            CreateTween().TweenProperty(popup, "scale", Vector2.One, 0.15)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
    }

    private void RefreshTasks()
    {
        var gs = GameSession.Instance;
        if (_taskLabel != null && gs != null && !gs.IsTutorial) _taskLabel.Text = gs.TaskBbcode();
    }

    private void OnHealth()
    {
        var gs = GameSession.Instance;
        SetHealth(gs.Signal, gs.AirtimeQuality);
    }

    private void OnPianoFault(bool inFault) => OnHealth();

    private void OnStrikes()
    {
        var gs = GameSession.Instance;
        int max = GameSession.MaxStrikes;

        var parts = new string[max];
        for (int i = 0; i < max; i++) parts[i] = i < gs.Strikes ? "X" : "-";
        SetText("%StrikeCount", string.Join(" ", parts));   // none: "- - -", one strike: "X - -", three: "X X X"

        var lbl = GetNodeOrNull<Label>("%StrikeCount");
        if (lbl != null)
            lbl.AddThemeColorOverride("font_color",
                gs.Strikes == 0 ? new Color("2FD6A0")        // green
              : gs.Strikes == 1 ? new Color("FFD25A")        // yellow
                                : new Color("E5484D"));      // red
    }
    public override void _ExitTree()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;
        gs.AirtimeChanged -= OnHealth;
        gs.KeypadChanged -= OnHealth;
        gs.StrikesChanged -= OnStrikes;
        gs.TasksChanged -= RefreshTasks;   // NEW
        gs.TopicChanged -= RefreshTasks;   // NEW
        gs.FlipChanged -= OnHealth;        // NEW
        if (gs.Piano != null) gs.Piano.FaultChanged -= OnPianoFault;
        gs.StepChanged -= OnStep;
        gs.QuotaChanged -= OnQuota;
        gs.KeypadChanged -= OnKeypadChanged;
    }

    private void OnKeypadChanged()
    {
        if (_page == KeypadPageIndex) ShowPage(KeypadPageIndex);
    }

    private void OnStep(TutorialStep step)
    {
        if (step == TutorialStep.KeypadModule) ShowPage(KeypadPageIndex);   // open the keypad page automatically
    }

	private void OnQuota() =>
		SetText("%QuotaCount", $"{GameSession.Instance.QuotaMet}/{GameSession.Instance.QuotaTarget}");

	private void SetText(string path, string text)
	{
		var l = GetNodeOrNull<Label>(path);
		if (l != null) l.Text = text;
	}

    private static string FlipHelp() =>
    "\n\nMODULE UPSIDE DOWN?\n" +
    "The raccoon turned it. Operator presses the FLIP BACK button on the module panel.\n" +
    "It is at the opposite corner while the panel is upside down.";

    private void ShowPage(int i)
    {
        _page = i;

        var title = GetNodeOrNull<Label>("%PageTitle");
        if (title != null) title.Text = PageTitles[i];

        // ===== INSTRUCTIONS APPEAR HERE =====
        var text = GetNodeOrNull<RichTextLabel>("%PageText");
        if (text != null)
        {
            text.Text = i == KeypadPageIndex ? MathPage()
                      : i == PianoPageIndex ? PianoPage()
                      : i == 2 ? SoundboardPage()
                      : i == 2 ? SoundboardPage()
                      : i == 3 ? MemoryManual.PageText()
                      : "";
            text.BbcodeEnabled = true;
        }

        // ===== PICTURE APPEARS HERE =====
        // (PageVisual isn't a unique-name node, so use FindChild)
        // var pic = FindChild("PageVisual", true, false) as TextureRect;
        // if (pic != null) pic.Texture = GD.Load<Texture2D>("res://assets/images/your_picture.png");
    }

    public void SetHealth(int signal, int quality)
    {
        var sb = GetNodeOrNull<ProgressBar>("%SignalBar"); if (sb != null) sb.Value = signal;
        SetText("%SignalLabel", $"Signal {signal}%");
        var qb = GetNodeOrNull<ProgressBar>("%QualityBar"); if (qb != null) qb.Value = quality;
        SetText("%QualityLabel", $"Quality {quality}%");
    }
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;

        var popup = GetNodeOrNull<Control>("%Popup");
        if (popup == null || !popup.Visible) return;

        // The MANUAL button handles its own toggle.
        if (_manualBtn != null && _manualBtn.GetGlobalRect().HasPoint(mb.GlobalPosition)) return;

        // A click on the manual itself does nothing (unless Popup is a full-screen holder).
        var rect = popup.GetGlobalRect();
        var screen = GetViewportRect().Size;
        bool fullScreen = rect.Size.X >= screen.X * 0.95f && rect.Size.Y >= screen.Y * 0.95f;
        if (!fullScreen && rect.HasPoint(mb.GlobalPosition)) return;

        foreach (var child in popup.GetChildren())
            if (child is Control c && c.Visible && c is not ColorRect && c.GetGlobalRect().HasPoint(mb.GlobalPosition))
                return;

        popup.Visible = false;   // clicked outside: close the manual
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel")) return;
        var popup = GetNodeOrNull<Control>("%Popup");
        if (popup != null && popup.Visible) popup.Visible = false;
    }
}
