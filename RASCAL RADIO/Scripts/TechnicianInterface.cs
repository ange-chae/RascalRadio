using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;
using RascalRadio.Modules;
using System.Collections.Generic;
using System.Linq;
using static StudioPianoData;

public partial class TechnicianInterface : PopupFrame
{
    // ===== BLACKOUT SYSTEM =====
    private ColorRect _blackoutOverlay;
    private ProgressBar _rebootMeter;
    private Label _rebootLabel;
    private Timer _blackoutRngTimer;
    private Timer _flickerTimer;
    private bool _manualLocked;
    private bool _isBlackout = false;
    private bool _isHolding = false;
    private float _rebootProgress = 0f;
    private int _flickerCount = 0;

    private const float RebootDuration = 6.0f; // minimum hold time to restore power
    private const float DecayRate = 0.3f; // progress lost per second when not holding
    private const float FlickerInterval = 0.08f;
    private const int FlickerCycles = 8;    // number of on/off flickers before full blackout

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
    private Timer _scrambleTimer, _restoreTimer;
    private bool _tabsScrambled;
    private readonly Dictionary<Button, string> _tabLabels = new();
    private static readonly string[] TabPaths = { "%TabA", "%TabB", "%TabC", "%TabD", "%TabE" };
    private readonly int[] _tabToPage = { 0, 1, 2, 3, 4 };   // which page each tab button opens
    private int _tab;                                          // which tab button is selected
    private PanelContainer _toast;
    private Label _toastLabel;
    private const double ScrambleSeconds = 18;
    private Button _manualBtn;
    private InGameMenu _gameMenu;
    private bool _menuOpen;

    private const int ModulePageIndex = 4;

    // ===== UNIFORM MANUAL LAYOUT =====
    // Every module page uses the same sections, in the same order:
    //   WHO SEES WHAT  /  WHAT TO DO  /  RULES  /  IF SOMETHING GOES WRONG
    // Rules are always written as "If ..., ..." and "Otherwise, ...". Keep sentences short.
    private static string Section(string heading, string body) =>
        $"[b]{heading}[/b]\n{body.TrimEnd()}\n\n";

    private static string FlipLine() =>
        "If the module is upside down, the raccoon turned it. The Operator presses FLIP BACK on the module panel. It is at the opposite corner while the panel is upside down.";

    // Module-specific faults first, then the flip line every module shares.
    private static string Faults(params string[] ifLines) =>
        Section("IF SOMETHING GOES WRONG", string.Join("\n", ifLines.Append(FlipLine())));

    //private static string KeyHint() =>
    //    "[color=#7A7A7A]Q opens or closes the manual. W and S switch modules.[/color]";

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

        var sb = new System.Text.StringBuilder();
        sb.Append(Section("WHO SEES WHAT", "The Operator sees only the line number, on their keypad.\nYou see the rules."));
        sb.Append(Section("WHAT TO DO", "Ask the Operator for the line number.\nWork out the code with the rules below.\nTell the Operator the code. They type it and press ROUTE."));
        sb.Append(Section("RULES", $"{rule} append {even}.\nOtherwise, append {odd}."));
        sb.Append(Faults("If there is a static fault, the Operator presses CLEAR, enters 0000, then presses ROUTE."));
        if (gs != null && gs.IsTutorial) sb.Append("In practice mode, a wrong code costs nothing.\n\n");

        return sb.ToString();
    }

    private static string PianoPage()
    {
        var gs = GameSession.Instance;
        bool tutorial = gs != null && gs.IsTutorial;

        var sb = new System.Text.StringBuilder();
        sb.Append(Section("WHO SEES WHAT", "The Operator sees only the jingle name.\nThe Broadcaster sees the cue sheet, a colored light."));
        sb.Append(Section("WHAT TO DO", "Ask the Broadcaster for the color.\nFind it in the rules below and tell the Operator which key to press.\nThen ask for the next color."));

        var rules = new System.Text.StringBuilder();
        if (gs == null || !gs.HasPianoColorMap)
        {
            rules.Append("Waiting for a jingle. The code changes every time one starts.");
        }
        else
        {
            bool first = true;
            for (int c = 0; c < PianoColorCode.Keys().Count; c++)
            {
                string key = gs.PianoKeyFor(c);
                if (key == "") continue;
                string lead = first ? "If" : "Otherwise, if";
                first = false;
                rules.Append($"{lead} the color is [color={PianoColorCode.HexOf(c)}][b]{PianoColorCode.NameOf(c)}[/b][/color], press [b]{key}[/b].\n");
            }
            rules.Append("The code is reshuffled for every jingle. Check this page, don't trust your memory.");
        }
        sb.Append(Section("RULES", rules.ToString()));

        string[] faults = tutorial
            ? new string[0]
            : new[] { "If the piano keys are flashing red, ask the Broadcaster to read each color the cue sheet blinks, one at a time. Give the Operator the matching key for each. Then the jingle continues." };
        sb.Append(Faults(faults));

        return sb.ToString();
    }

    private static string SoundboardPage()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Section("WHO SEES WHAT", "The Operator sees the cue on their screen.\nThe pad labels and cue order change every round."));
        sb.Append(Section("WHAT TO DO", "Check the number of strikes.\nFind the cue in the rules below.\nTell the Operator which labeled pad to press."));

        var module = GameSession.Instance?.Soundboard;
        if (module == null || module.Sequence.Count == 0)
        {
            sb.Append(Section("RULES", "Waiting for the next Soundboard round."));
        }
        else
        {
            var rules = new System.Text.StringBuilder();
            string[] cues = SoundboardData.Pads;
            string[] headings = { "If there are no strikes:", "If there is one strike:", "If there are two or more strikes:" };

            for (int row = 0; row < headings.Length; row++)
            {
                if (row > 0) rules.Append('\n');
                rules.Append($"[b]{headings[row]}[/b]\n");
                for (int i = 0; i < cues.Length; i++)
                {
                    string cue = module.SoundForPad(cues[i]);
                    string press = module.SoundForPad(module.PadToPress(cues[i], row));

                    string lead = i == 0 ? "If" : "Otherwise, if";
                    rules.Append($"{lead} the cue is [b]{cue}[/b], press [b]{press}[/b].\n");
                }
            }
            rules.Append("\nCheck the strikes again before every cue, because they can change.");
            sb.Append(Section("RULES", rules.ToString()));
        }

        sb.Append(Faults());

        return sb.ToString();
    }


    public override void _Ready()
    {
        var gs = GameSession.Instance;

        // FIRST, so nothing below can stop the tutorial from appearing
        if (gs != null && gs.IsTutorial) AddChild(new TutorialOverlay { RoleName = "Technician" });

        base._Ready();
        _gameMenu = new InGameMenu();
        AddChild(_gameMenu);
        StyleBars();
        SetupBlackoutSystem();

        GameSession.Instance?.ApplyBackground(GetNodeOrNull<TextureRect>("%Background"), "technician");
        GameSession.Instance?.ApplyUiTint(this);
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
        if (manual != null)
        {
            manual.Visible = true;   // the manual starts open
            manual.ZIndex = 100;      // always draw above Task, TimePanel, etc.
            manual.MoveToFront();     // and last in the tree, just in case
        }

        _manualBtn = GetNodeOrNull<Button>("%ManualBtn")
            ?? FindChild("ManualBtn", true, false) as Button
            ?? FindChildren("*", "Button", true, false).OfType<Button>()
                .FirstOrDefault(b => b.Text.ToLower().Contains("manual"));

        if (_manualBtn != null)
        {
            _manualBtn.Pressed += ToggleManual;
            if (!_manualBtn.Text.Contains("(Q)")) _manualBtn.Text = _manualBtn.Text.TrimEnd() + " (Q)";   // key hint; delete this line if you don't want it
        }
        else GD.PushError("TechnicianInterface: manual button not found. Name it ManualBtn.");

        // NEW: task box setup (step 8b)
        _taskLabel = GetNodeOrNull<RichTextLabel>("%TaskLabel");
        if (_taskLabel != null) { _taskLabel.BbcodeEnabled = true; _taskLabel.FitContent = true; _taskLabel.ScrollActive = false; GameSession.Instance?.HookTaskToggle(_taskLabel); }

        // Only list the modules this level actually uses.
        ApplyLevelTabs(gs);

        string[] tabs = { "%TabA", "%TabB", "%TabC", "%TabD", "%TabE" };
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i;
            var b = GetNodeOrNull<Button>(tabs[i]);
            if (b != null) b.Pressed += () => SelectTab(idx);
            else GD.PushError($"TechnicianInterface: {tabs[i]} not found.");
        }
        SelectTab(VisibleTabIdx().FirstOrDefault());

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
        gs.PianoColorMapChanged += OnPianoColorMapChanged;
        if (gs.Soundboard != null) gs.Soundboard.Configured += OnSoundboardConfigured;
        if (gs.Piano != null) gs.Piano.FaultChanged += OnPianoFault;
        OnHealth();
        OnStrikes();
        OnQuota();
        RefreshTasks();
        SetupTechInterruptions();
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

    private void SetManualLocked(bool locked)
    {
        _manualLocked = locked;
        if (_manualBtn != null) _manualBtn.Disabled = locked;
        if (locked)
        {
            var popup = GetNodeOrNull<Control>("%Popup");
            if (popup != null) popup.Visible = false;   // slams shut
        }
    }

    private void ToggleManual()
    {
        if (_manualLocked) return;
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
    private void OnPianoColorMapChanged()
    {
        if (_page == PianoPageIndex) ShowPage(PianoPageIndex);   // table updates live
    }

    private void RefreshTasks()
    {
        var gs = GameSession.Instance;
        if (_taskLabel == null || gs == null) return;

        if (!gs.IsTutorial) { _taskLabel.Text = gs.TaskBbcode("technician"); return; }

        var s = gs.CurrentStep;
        var items = new (string text, bool done)[]
        {
        ("Meet the studio", s > TutorialStep.MeetStudio),
        ("Open the manual and read the Math Keypad page", s > TutorialStep.KeypadModule),
        ("Help the Operator fix the keypad", s > TutorialStep.KeypadModule),
        ("Read the Piano page of the manual", s > TutorialStep.PianoModule),
        ("Tell the Operator which key to press for each color", s > TutorialStep.PianoModule),
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
        if (gs != null) gs.StolenKeyChanged -= RefreshToast;
        if (gs == null) return;
        gs.AirtimeChanged -= OnHealth;
        gs.KeypadChanged -= OnHealth;
        gs.StrikesChanged -= OnStrikes;
        gs.TasksChanged -= RefreshTasks;   // NEW
        gs.TopicChanged -= RefreshTasks;   // NEW
        gs.FlipChanged -= OnHealth;        // NEW
        gs.PrompterColorMapChanged -= OnPrompterColorMapChanged;
        if (gs.Soundboard != null) gs.Soundboard.Configured -= OnSoundboardConfigured;
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

    private void OnSoundboardConfigured()
    {
        if (_page == 2) ShowPage(2);
    }

    private void OnStep(TutorialStep step)
    {
        if (step == TutorialStep.KeypadModule) SelectTab(KeypadPageIndex);   // open the keypad page automatically
        if (step == TutorialStep.PianoModule) SelectTab(PianoPageIndex);     // and the piano page at the piano step
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
                      : i == 2 ? SoundboardPage()
                      : i == 3 ? MemoryManual.PageText() + "\n\n"
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

        var sb = new System.Text.StringBuilder();
        sb.Append(Section("WHO SEES WHAT", "During signal interference, the Broadcaster sees random letters in colors.\nThe letter is not the key to press. The color is."));
        sb.Append(Section("WHAT TO DO", "Ask the Broadcaster for the color of the letter.\nTell them which WASD key matches it."));

        if (gs?.LevelId != "emergency_broadcast")
        {
            sb.Append(Section("RULES", "This page is only used in Level 3."));
        }
        else
        {
            int[] colorToKey = gs.PrompterColorKeyMap;
            if (colorToKey.Length != 4)
            {
                sb.Append(Section("RULES", "The Level 3 color code is synchronizing..."));
            }
            else
            {
                var rules = new System.Text.StringBuilder();
                for (int color = 0; color < 4; color++)
                {
                    string lead = color == 0 ? "If" : "Otherwise, if";
                    rules.Append($"{lead} the color is [color={GameSession.PrompterColorHex(color)}][b]{GameSession.PrompterColorName(color)}[/b][/color], press [b]{GameSession.PrompterKeyName(colorToKey[color])}[/b].\n");
                }
                sb.Append(Section("RULES", rules.ToString()));
            }
        }


        return sb.ToString();
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
            Color = new Color(0.02f, 0.02f, 0.03f, 0.97f),
            ZIndex = 50,
            Visible = false,
        };
        _blackoutOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_blackoutOverlay);

        // --- Reboot progress bar (grows from bottom of screen) ---
        _rebootMeter = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            ShowPercentage = false,
            FillMode = (int)ProgressBar.FillModeEnum.BottomToTop,
            ZIndex = 60,
            Visible = false,
        };
        // Style: emergency green glow
        var fillStyle = new StyleBoxFlat { BgColor = new Color(0.1f, 0.85f, 0.4f, 0.75f) };
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0.0f, 0.0f, 0.0f, 0.0f) };
        _rebootMeter.AddThemeStyleboxOverride("fill", fillStyle);
        _rebootMeter.AddThemeStyleboxOverride("background", bgStyle);
        _rebootMeter.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _rebootMeter.CustomMinimumSize = new Vector2(0, 80);
        // Offset up by 80 px so it sits flush at the bottom edge
        _rebootMeter.OffsetTop = -80;
        _rebootMeter.OffsetBottom = 0;
        AddChild(_rebootMeter);

        // --- "HOLD TO REBOOT" label sitting just above the meter ---
        _rebootLabel = new Label
        {
            Text = "▲  HOLD TO REBOOT  ▲",
            HorizontalAlignment = HorizontalAlignment.Center,
            ZIndex = 61,
            Visible = false,
        };
        _rebootLabel.AddThemeColorOverride("font_color", new Color(0.1f, 0.9f, 0.4f, 1f));
        _rebootLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 1f));
        _rebootLabel.AddThemeFontSizeOverride("font_size", 20);
        _rebootLabel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _rebootLabel.OffsetTop = -112;
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
        SetManualLocked(true);
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
        _isBlackout = true;
        _rebootProgress = 0f;
        _rebootMeter.Value = 0;

        _blackoutOverlay.Visible = true;
        _rebootMeter.Visible = true;
        _rebootLabel.Visible = true;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        PlaceToast();

        if (!_isBlackout) return;

        // Detect hold: left mouse held AND cursor is NOT over the main blackout overlay area
        // (allowing the lit bar nodes to receive the hold naturally from anywhere on screen).
        _isHolding = Input.IsMouseButtonPressed(MouseButton.Left)
                  || Input.IsActionPressed("ui_accept");   // Spacebar / gamepad A as fallback

        if (_isHolding)
        {
            _rebootProgress += (float)delta / RebootDuration;
            _rebootProgress = Mathf.Min(_rebootProgress, 1.0f);

            // Animate the label to pulse
            float pulse = 0.6f + 0.4f * Mathf.Sin((float)Time.GetTicksMsec() / 200f);
            _rebootLabel.Modulate = new Color(1f, 1f, 1f, pulse);
        }
        else
        {
            _rebootProgress -= (float)delta * DecayRate;
            _rebootProgress = Mathf.Max(_rebootProgress, 0f);
            _rebootLabel.Modulate = Colors.White;
        }

        _rebootMeter.Value = _rebootProgress * 100.0;

        if (_rebootProgress >= 1.0f)
            EndBlackout();
    }

    // Phase 3: Restore power
    private void EndBlackout()
    {
        _isBlackout = false;
        _rebootProgress = 0f;
        GameSession.Instance?.ReportBlackoutSequence(false);

        // Brief restore flash
        Modulate = new Color(1.4f, 1.4f, 1.4f, 1f);
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate", Colors.White, 0.3)
             .SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);

        _blackoutOverlay.Visible = false;
        _rebootMeter.Visible = false;
        _rebootLabel.Visible = false;
        _rebootMeter.Value = 0;
        SetManualLocked(false);

        // Schedule the next random blackout (Level 3 only — guard in case level changed)
        if (GameSession.Instance?.LevelId == "emergency_broadcast")
        {
            _blackoutRngTimer.WaitTime = GetNextBlackoutDelay();
            _blackoutRngTimer.Start();
        }
    }

    private void SetupTechInterruptions()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;

        // notice box (top-left): shows the stolen key and the scramble warning
        _toast = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 200, ZAsRelative = false };
        var s = new StyleBoxFlat { BgColor = new Color("2A0A0C"), BorderColor = new Color("E5484D") };
        s.SetBorderWidthAll(3); s.SetCornerRadiusAll(8); s.SetContentMarginAll(10);
        _toast.AddThemeStyleboxOverride("panel", s);
        _toastLabel = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _toastLabel.AddThemeColorOverride("font_color", new Color("FBEFD9"));
        _toastLabel.AddThemeFontSizeOverride("font_size", 16);
        _toast.AddChild(_toastLabel);
        AddChild(_toast);
        _toast.SetAnchorsPreset(LayoutPreset.TopLeft);
        _toast.Position = new Vector2(16, 16);

        gs.StolenKeyChanged += RefreshToast;
        RefreshToast();

        var d = gs.CurrentLevel;
        if (gs.IsTutorial || d == null || !d.ScrambledTabs) return;

        _restoreTimer = new Timer { OneShot = true, WaitTime = ScrambleSeconds };
        _restoreTimer.Timeout += EndScramble;
        AddChild(_restoreTimer);

        _scrambleTimer = new Timer { OneShot = true, WaitTime = NextScrambleGap() };
        _scrambleTimer.Timeout += StartScramble;
        AddChild(_scrambleTimer);
        _scrambleTimer.Start();
    }

    private void PlaceToast()
    {
        if (_toast == null || !_toast.Visible) return;
        var size = _toast.GetCombinedMinimumSize();
        var pos = new Vector2(16, 16);
        var popup = GetNodeOrNull<Control>("%Popup");
        if (popup != null && popup.Visible)
        {
            var r = popup.GetGlobalRect();
            pos = new Vector2(r.GetCenter().X - size.X / 2, Mathf.Max(8, r.Position.Y - size.Y - 8));
        }
        _toast.GlobalPosition = new Vector2(Mathf.Max(8, pos.X), pos.Y);
    }

    private double NextScrambleGap()
    {
        double gap = GameSession.Instance?.CurrentLevel?.InterruptionGap ?? 30;
        return gap * (0.7 + GD.Randf() * 0.6);
    }

    private void RetryScrambleSoon() { _scrambleTimer.WaitTime = 4; _scrambleTimer.Start(); }

    private List<Button> VisibleTabs() =>
        new[] { "%TabA", "%TabB", "%TabC", "%TabD", "%TabE" }
            .Select(p => GetNodeOrNull<Button>(p))
            .Where(b => b != null && b.Visible).ToList();

    // A = Math Keypad, B = Piano, C = Soundboard, D = Simon Says, E = Prompter colors (Level 3).
    // The tutorial teaches the keypad and the piano only. Real levels follow the level's Has... flags.
    private void ApplyLevelTabs(GameSession gs)
    {
        var d = gs?.CurrentLevel;
        bool tutorial = gs != null && gs.IsTutorial;

        bool[] show =
        {
            tutorial || (d?.HasKeypad ?? true),
            tutorial || (d?.HasPiano ?? true),
            !tutorial && (d?.HasSoundboard ?? true),
            !tutorial && (d?.HasMemory ?? true),
            gs?.LevelId == "emergency_broadcast",
        };
        if (!show.Any(x => x)) show[0] = true;   // never leave the manual with no tabs

        for (int i = 0; i < TabPaths.Length; i++)
            if (GetNodeOrNull<Button>(TabPaths[i]) is { } b) b.Visible = show[i];
    }

    // W = previous module, S = next module (wraps around).
    private void StepTab(int dir)
    {
        var idx = VisibleTabIdx();
        if (idx.Count == 0) return;
        int pos = idx.IndexOf(_tab);
        if (pos < 0) pos = 0;
        SelectTab(idx[(pos + dir + idx.Count) % idx.Count]);
    }

    private void SelectTab(int tab)
    {
        _tab = tab;
        // keep the highlighted tab in sync, and make sure ONLY the selected tab looks pressed
        for (int i = 0; i < TabPaths.Length; i++)
            if (GetNodeOrNull<Button>(TabPaths[i]) is { ToggleMode: true } b)
                b.SetPressedNoSignal(i == tab);
        ShowPage(_tabToPage[tab]);
    }

    private List<int> VisibleTabIdx() =>
        Enumerable.Range(0, TabPaths.Length)
            .Where(i => GetNodeOrNull<Button>(TabPaths[i]) is { Visible: true }).ToList();
    private void StartScramble()
    {
        var gs = GameSession.Instance;
        if (gs == null || gs.Ended) return;
        var tabs = VisibleTabs();
        if (_tabsScrambled || _isBlackout || tabs.Count < 3) { RetryScrambleSoon(); return; }

        var labels = tabs.Select(t => t.Text).ToList();
        List<string> shuffled;
        int tries = 0;
        do { shuffled = labels.OrderBy(_ => GD.Randi()).ToList(); }
        while (++tries < 20 && shuffled.Where((l, i) => l == labels[i]).Any());   // make sure every label moved

        _tabLabels.Clear();
        for (int i = 0; i < tabs.Count; i++)
        {
            _tabLabels[tabs[i]] = labels[i];
            tabs[i].Text = shuffled[i];
            tabs[i].Modulate = new Color(1f, 0.75f, 0.75f);
        }

        var idxs = VisibleTabIdx();
        List<int> pages;
        tries = 0;
        do { pages = idxs.OrderBy(_ => GD.Randi()).ToList(); }
        while (++tries < 20 && pages.Where((p, k) => p == idxs[k]).Any());   // every tab gets a different page
        for (int k = 0; k < idxs.Count; k++) _tabToPage[idxs[k]] = pages[k];
        ShowPage(_tabToPage[_tab]);   // the open page changes right now

        _tabsScrambled = true;
        RefreshToast();
        _restoreTimer.Start();
    }

    private void EndScramble()
    {
        for (int i = 0; i < _tabToPage.Length; i++) _tabToPage[i] = i;
        ShowPage(_tabToPage[_tab]);
        foreach (var kv in _tabLabels)
            if (IsInstanceValid(kv.Key)) { kv.Key.Text = kv.Value; kv.Key.Modulate = Colors.White; }
        _tabLabels.Clear();
        _tabsScrambled = false;
        RefreshToast();
        _scrambleTimer.WaitTime = NextScrambleGap();
        _scrambleTimer.Start();
    }

    private void RefreshToast()
    {
        if (_toast == null) return;
        var gs = GameSession.Instance;
        var lines = new List<string>();
        if (_tabsScrambled) lines.Add("The raccoon scrambled the tabs!\nCheck each page title.");
        if (gs != null && gs.StolenPos > 0) lines.Add($"STOLEN KEY: position {gs.StolenPos} is {gs.StolenKey}");

        _toast.Visible = lines.Count > 0;
        _toastLabel.Text = string.Join("\n\n", lines);
        _toast.CallDeferred(Control.MethodName.ResetSize);
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
        // Manual keys: Q opens/closes it, W/S move between modules while it is open.
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Q:
                    ToggleManual();
                    GetViewport().SetInputAsHandled();
                    return;

                case Key.W:
                case Key.S:
                    var popup = GetNodeOrNull<Control>("%Popup");
                    if (popup != null && popup.Visible && !_manualLocked)
                    {
                        StepTab(key.Keycode == Key.W ? -1 : 1);
                        GetViewport().SetInputAsHandled();
                    }
                    return;
            }
        }

        // Esc is only the pause menu now.
        if (!e.IsActionPressed("ui_cancel")) return;
        _menuOpen = !_menuOpen;
        _gameMenu?.SetOpen(_menuOpen);
        GetViewport().SetInputAsHandled();
    }


}