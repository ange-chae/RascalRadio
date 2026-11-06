using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;
using RascalRadio.Modules;
using System.Linq;
using static StudioPianoData;

public partial class TechnicianInterface : PopupFrame
{
    // ===== BLACKOUT SYSTEM =====
    private ColorRect    _blackoutOverlay;
    private ProgressBar  _rebootMeter;
    private Label        _rebootLabel;
    private Timer        _blackoutRngTimer;
    private Timer        _flickerTimer;

    private bool  _isBlackout    = false;
    private bool  _isHolding     = false;
    private float _rebootProgress = 0f;
    private int   _flickerCount   = 0;

    private const float RebootDuration  = 6.0f; // minimum hold time to restore power
    private const float DecayRate       = 0.3f; // progress lost per second when not holding
    private const float FlickerInterval = 0.08f;
    private const int   FlickerCycles   = 8;    // number of on/off flickers before full blackout

    // ===== MANUAL PAGES =====
    // One entry per tab button (TabA..TabE), in order.
    private static readonly string[] PageTitles =
    {
    "Module A: Math Keypad",
    "Module B: Piano",
    "Module C: Soundboard",
    "Module D: Simon Says",
    "Prompter Color Code",
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

        bool digitSum = gs?.CurrentLevel?.KeypadRule == LineRuleMode.DigitSum;
        string rule = digitSum
            ? "Add up all the digits of the line number. If the total is even,"
            : "If the line number is even,";

        return
            "Ask the Operator for the incoming line number. It is only on their keypad.\n\n" +
            $"{rule} append {even}.\n" +
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
        sb.Append("The Operator reads out each sound cue on their soundboard screen. Check the number of strikes on your computer, then tell them which pad to press.\n");

        string[] cues = SoundboardData.Pads;
        string[] headings = { "If there are no strikes:", "If there is one strike:", "If there are two or more strikes:" };

        for (int row = 0; row < headings.Length; row++)
        {
            sb.Append($"\n[b]{headings[row]}[/b]\n");
            for (int i = 0; i < cues.Length; i++)
            {
                string cue = SoundboardUI.NameOf(cues[i]);
                string press = SoundboardUI.NameOf(SoundboardData.PadToPress(cues[i], row));

                string lead = i == 0 ? "If" : "Otherwise, if";
                sb.Append($"{lead} the cue is [b]{cue}[/b], press [b]{press}[/b].\n");
            }
        }

        sb.Append("\nCheck the strikes again before every cue, because they can change.");
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
        SetupBlackoutSystem();

        var timeLabel = FindChild("TimeLabel", true, false) as Label;
        if (timeLabel != null)
        {
            var clock = new Timer { WaitTime = 0.5, Autostart = true };
            clock.Timeout += () =>
            {
                if (IsInstanceValid(timeLabel) && GameSession.Instance != null)
                    timeLabel.Text = GameSession.Instance.ClockText();
            };
            AddChild(clock);
        }

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

        // The Prompter Color Code page is specific to Level 3.
        var prompterTab = GetNodeOrNull<Button>("%TabE");
        if (prompterTab != null) prompterTab.Visible = gs?.LevelId == "emergency_broadcast";

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
        gs.PrompterColorMapChanged += OnPrompterColorMapChanged;
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

        if (GetNodeOrNull<RetroBar>("%QualityBar") is { } q)
        {
            q.DangerBelow = qFloor;
            q.WarnBelow = qFloor + (100 - qFloor) / 2;   // floor 85 → yellow below 92
        }
        if (GetNodeOrNull<RetroBar>("%SignalBar") is { } s)
        {
            s.DangerBelow = sFloor;
            s.WarnBelow = sFloor + (100 - sFloor) / 2;   // floor 75 → yellow below 87
        }
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
        if (_taskLabel == null || gs == null) return;

        if (!gs.IsTutorial) { _taskLabel.Text = gs.TaskBbcode(); return; }

        var s = gs.CurrentStep;
        var items = new (string text, bool done)[]
        {
        ("Meet the studio", s > TutorialStep.MeetStudio),
        ("Open the manual and read the Math Keypad page", s > TutorialStep.KeypadModule),
        ("Help the Operator fix the keypad", s > TutorialStep.KeypadModule),
        };
        _taskLabel.Text = string.Join("\n", items.Select(t => t.done
            ? $"[color=#7A7A7A][s]- {t.text}[/s][/color]"
            : $"- {t.text}"));
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
        gs.PrompterColorMapChanged -= OnPrompterColorMapChanged;
        if (gs.Piano != null) gs.Piano.FaultChanged -= OnPianoFault;
        gs.StepChanged -= OnStep;
        gs.QuotaChanged -= OnQuota;
        gs.KeypadChanged -= OnKeypadChanged;
        // Blackout timers are children, freed automatically
    }

    private void OnKeypadChanged()
    {
        if (_page == KeypadPageIndex) ShowPage(KeypadPageIndex);
    }

    private void OnPrompterColorMapChanged()
    {
        if (_page == 4) ShowPage(4);
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
                      : i == 4 ? PrompterColorPage()
                      : "";
            text.BbcodeEnabled = true;
        }

        // ===== PICTURE APPEARS HERE =====
        // (PageVisual isn't a unique-name node, so use FindChild)
        // var pic = FindChild("PageVisual", true, false) as TextureRect;
        // if (pic != null) pic.Texture = GD.Load<Texture2D>("res://assets/images/your_picture.png");
    }

    private static string PrompterColorPage()
    {
        var gs = GameSession.Instance;
        if (gs?.LevelId != "emergency_broadcast")
            return "This page is used during signal interference in Level 3.\n\n" +
                   "During an interruption, the Broadcaster sees random letters in colors. " +
                   "Tell them which WASD key matches each color.";

        int[] colorToKey = gs.PrompterColorKeyMap;
        if (colorToKey.Length != 4)
            return "Level 3 prompter color code is synchronizing...";

        string[] rows = Enumerable.Range(0, 4).Select(color =>
            $"[color={GameSession.PrompterColorHex(color)}][b]{GameSession.PrompterColorName(color)}[/b][/color]  →  " +
            $"[b]{GameSession.PrompterKeyName(colorToKey[color])}[/b]").ToArray();

        return "[b]LEVEL 3: PROMPTER SIGNAL INTERFERENCE[/b]\n\n" +
               "When random colored letters appear on the Broadcaster's monitor, the color tells them which WASD key to press. " +
               "The letter itself is not the key to press. Read this mapping aloud:\n\n" +
               string.Join("\n", rows);
    }

    public void SetHealth(int signal, int quality)
    {
        var sb = GetNodeOrNull<ProgressBar>("%SignalBar"); if (sb != null) sb.Value = signal;
        SetText("%SignalLabel", $"Signal {signal}%");
        var qb = GetNodeOrNull<ProgressBar>("%QualityBar"); if (qb != null) qb.Value = quality;
        SetText("%QualityLabel", $"Quality {quality}%");
    }

    // ===== BLACKOUT SYSTEM IMPLEMENTATION =====

    private void SetupBlackoutSystem()
    {
        // --- Promote the lit HUD nodes above the future overlay ---
        // Task panel (Hudbar, top-right) and TimePanel stay visible during blackout.
        foreach (var nodePath in new[] { "Task", "TimePanel" })
        {
            var n = GetNodeOrNull<Control>(nodePath);
            if (n != null) { n.ZAsRelative = false; n.ZIndex = 55; }
        }

        // --- Full-screen blackout overlay ---
        _blackoutOverlay = new ColorRect
        {
            Color   = new Color(0.02f, 0.02f, 0.03f, 0.97f),
            ZIndex  = 50,
            Visible = false,
        };
        _blackoutOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_blackoutOverlay);

        // --- Reboot progress bar (grows from bottom of screen) ---
        _rebootMeter = new ProgressBar
        {
            MinValue       = 0,
            MaxValue       = 100,
            Value          = 0,
            ShowPercentage = false,
            FillMode       = (int)ProgressBar.FillModeEnum.BottomToTop,
            ZIndex         = 60,
            Visible        = false,
        };
        // Style: emergency green glow
        var fillStyle = new StyleBoxFlat { BgColor = new Color(0.1f, 0.85f, 0.4f, 0.75f) };
        var bgStyle   = new StyleBoxFlat { BgColor = new Color(0.0f, 0.0f, 0.0f, 0.0f) };
        _rebootMeter.AddThemeStyleboxOverride("fill",       fillStyle);
        _rebootMeter.AddThemeStyleboxOverride("background", bgStyle);
        _rebootMeter.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _rebootMeter.CustomMinimumSize = new Vector2(0, 80);
        // Offset up by 80 px so it sits flush at the bottom edge
        _rebootMeter.OffsetTop    = -80;
        _rebootMeter.OffsetBottom = 0;
        AddChild(_rebootMeter);

        // --- "HOLD TO REBOOT" label sitting just above the meter ---
        _rebootLabel = new Label
        {
            Text              = "▲  HOLD TO REBOOT  ▲",
            HorizontalAlignment = HorizontalAlignment.Center,
            ZIndex            = 61,
            Visible           = false,
        };
        _rebootLabel.AddThemeColorOverride("font_color",        new Color(0.1f, 0.9f, 0.4f, 1f));
        _rebootLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 1f));
        _rebootLabel.AddThemeFontSizeOverride("font_size", 20);
        _rebootLabel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _rebootLabel.OffsetTop    = -112;
        _rebootLabel.OffsetBottom = -84;
        AddChild(_rebootLabel);

        // --- Flicker timer ---
        _flickerTimer = new Timer { WaitTime = FlickerInterval, OneShot = false };
        _flickerTimer.Timeout += OnFlicker;
        AddChild(_flickerTimer);

        // --- Random interval timer to schedule blackouts (Level 3 only) ---
        _blackoutRngTimer = new Timer { WaitTime = GetNextBlackoutDelay(), OneShot = true };
        _blackoutRngTimer.Timeout += StartFlicker;
        AddChild(_blackoutRngTimer);

        // Only start the timer on the emergency broadcast (Level 3).
        // On the tutorial, Level 1, and Level 2 the timer stays stopped.
        if (GameSession.Instance?.LevelId == "emergency_broadcast")
            _blackoutRngTimer.Start();

    }

    private static double GetNextBlackoutDelay() =>
        GD.RandRange(40.0, 90.0);

    // Phase 1: Flicker before full blackout
    private void StartFlicker()
    {
        _flickerCount = 0;
        GameSession.Instance?.ReportBlackoutSequence(true);
        _flickerTimer.Start();
    }

    private void OnFlicker()
    {
        _flickerCount++;
        // Toggle the whole tree's modulate to simulate a fluorescent lamp dying
        Modulate = _flickerCount % 2 == 0
            ? Colors.White
            : new Color(0.15f, 0.15f, 0.15f, 1f);

        if (_flickerCount >= FlickerCycles * 2)
        {
            _flickerTimer.Stop();
            Modulate = Colors.White;
            TriggerBlackout();
        }
    }

    // Phase 2: Full blackout
    private void TriggerBlackout()
    {
        _isBlackout     = true;
        _rebootProgress = 0f;
        _rebootMeter.Value = 0;

        _blackoutOverlay.Visible = true;
        _rebootMeter.Visible     = true;
        _rebootLabel.Visible     = true;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (!_isBlackout) return;

        // Detect hold: left mouse held AND cursor is NOT over the main blackout overlay area
        // (allowing the lit bar nodes to receive the hold naturally from anywhere on screen).
        _isHolding = Input.IsMouseButtonPressed(MouseButton.Left)
                  || Input.IsActionPressed("ui_accept");   // Spacebar / gamepad A as fallback

        if (_isHolding)
        {
            _rebootProgress += (float)delta / RebootDuration;
            _rebootProgress  = Mathf.Min(_rebootProgress, 1.0f);

            // Animate the label to pulse
            float pulse = 0.6f + 0.4f * Mathf.Sin((float)Time.GetTicksMsec() / 200f);
            _rebootLabel.Modulate = new Color(1f, 1f, 1f, pulse);
        }
        else
        {
            _rebootProgress -= (float)delta * DecayRate;
            _rebootProgress  = Mathf.Max(_rebootProgress, 0f);
            _rebootLabel.Modulate = Colors.White;
        }

        _rebootMeter.Value = _rebootProgress * 100.0;

        if (_rebootProgress >= 1.0f)
            EndBlackout();
    }

    // Phase 3: Restore power
    private void EndBlackout()
    {
        _isBlackout     = false;
        _rebootProgress = 0f;
        GameSession.Instance?.ReportBlackoutSequence(false);

        // Brief restore flash
        Modulate = new Color(1.4f, 1.4f, 1.4f, 1f);
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate", Colors.White, 0.3)
             .SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);

        _blackoutOverlay.Visible = false;
        _rebootMeter.Visible     = false;
        _rebootLabel.Visible     = false;
        _rebootMeter.Value       = 0;

        // Schedule the next random blackout (Level 3 only — guard in case level changed)
        if (GameSession.Instance?.LevelId == "emergency_broadcast")
        {
            _blackoutRngTimer.WaitTime = GetNextBlackoutDelay();
            _blackoutRngTimer.Start();
        }
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
