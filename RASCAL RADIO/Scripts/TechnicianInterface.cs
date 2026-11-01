using Godot;
using RascalRadio.Core.Models;

public partial class TechnicianInterface : PopupFrame
{
    // ===== MANUAL PAGES =====
    // One entry per tab button (TabA..TabE), in order.
    private static readonly string[] PageTitles =
    {
    "Module A: Math Keypad",
    "Module B: Piano",
    "Module C: Soundboard",
    "Module D:",
    "Module E:",
};

    private const int KeypadPageIndex = 0;
    private const int PianoPageIndex = 1;
    private int _page;

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
            "MATH & KEYPAD: LINE OVERRIDE CODE\n\n" +
            "1. Ask the Operator for the incoming line number (1-9). It is only on their keypad.\n" +
            $"2. If the line number is EVEN, append {even}. If it is ODD, append {odd}.\n" +
            "3. Tell the Operator the code. They type it and press ROUTE.\n\n" +
            "CLEARING STATIC FAULTS\n" +
            "Press CLEAR, enter 0000, then press ROUTE.\n\n" +
            (gs != null && gs.IsTutorial ? "In practice mode a wrong code costs nothing." : ""); ;
    }

    // The jingle table is built from StudioPianoData, so the manual can never disagree with the game.
    private static string PianoPage()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("PIANO\n\n");
        sb.Append("The Operator sees the NAME of a jingle. You have the notes. Read them out, one at a time.\n\n");

        sb.Append("WHAT TO DO\n");
        sb.Append("1. Ask the Operator: which jingle?\n");
        sb.Append("2. Find it in the list below.\n");
        sb.Append("3. Read the first note aloud. Example: \"Blue, C, normal.\"\n");
        sb.Append("4. Wait for the green light, then read the next note.\n\n");

        sb.Append("OCTAVE\n");
        sb.Append("NORMAL = leave it alone.\n");
        sb.Append("-  = Operator presses the [-] button first.\n");
        sb.Append("+  = Operator presses the [+] button first.\n\n");

        sb.Append("JINGLES\n");
        foreach (var jingle in StudioPianoData.GetJingles())
        {
            sb.Append($"\n{jingle.Key.ToUpper()}\n");
            int n = 1;
            foreach (var cue in jingle.Value)
                sb.Append($"  {n++}. {cue.ColorName}, {cue.Letter}, {StudioPianoData.OctaveLabel(cue.Octave)}\n");
        }

        sb.Append("\nKEYS FLASHING RED?\n");
        sb.Append("Operator plays B A G F E D C. Then the jingle continues.");
        return sb.ToString();
    }
    public override void _Ready()
    {
        var gs = GameSession.Instance;

        // FIRST, so nothing below can stop the tutorial from appearing
        if (gs != null && gs.IsTutorial) AddChild(new TutorialOverlay { RoleName = "Technician" });

        base._Ready();

        var manual = GetNodeOrNull<Control>("%Popup");
        if (manual != null) manual.Visible = false;   // the manual starts closed

        var manualBtn = GetNodeOrNull<Button>("%ManualBtn");
        if (manualBtn != null) manualBtn.Pressed += ToggleManual;

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
        if (gs.Piano != null) gs.Piano.FaultChanged += OnPianoFault;
        OnHealth();
        OnStrikes();
        OnQuota();
        if (gs.IsTutorial) OnStep(gs.CurrentStep);
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

    private void OnHealth()
    {
        var gs = GameSession.Instance;
        SetHealth(gs.Signal, gs.AirtimeQuality);
    }

    private void OnPianoFault(bool inFault) => OnHealth();

    private void OnStrikes()
    {
        var gs = GameSession.Instance;
        SetText("%StrikeCount", gs.Strikes == 0 ? "none" : $"{gs.Strikes}/{GameSession.MaxStrikes}");
    }
    public override void _ExitTree()
	{
		var gs = GameSession.Instance;
		if (gs == null) return;
        gs.AirtimeChanged -= OnHealth;
        gs.KeypadChanged -= OnHealth;
        gs.StrikesChanged -= OnStrikes;
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
                      : "";
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
        var btn = GetNodeOrNull<Control>("%ManualBtn");
        if (btn != null && btn.GetGlobalRect().HasPoint(mb.GlobalPosition)) return;

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
