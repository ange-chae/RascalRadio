using Godot;
using RascalRadio.Core.Models;

public partial class TechnicianInterface : PopupFrame
{
	private static readonly (string Title, string Body)[] Pages =
	{
		("STATION BASICS",
		 "1. Read the topic and the quota at the top.\n2. Operator reads the caller story out loud.\n3. Operator says ACCEPT or REJECT.\n4. Broadcaster presses the matching button.\n5. Everyone checks that quota and strikes updated."),
		("STATUS WORDS", "TODO: list your status words here."),
		("STRIKES AND QUOTA", "TODO: explain quota goals and the strike limit."),
		("RACCOON WATCH", "TODO: explain the raccoon event."),
		("MODULE PAGES",
		 ""),   // built live by MathPage(): it needs the current level and this shift's rule
	};

	private const int ModulePageIndex = 4;
	private int _page;

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

    public override void _Ready()
	{
		base._Ready();
		string[] tabs = { "%TabBasics", "%TabStatus", "%TabStrikes", "%TabRaccoon", "%TabModules" };
		for (int i = 0; i < tabs.Length; i++)
		{
			int idx = i;
			GetNode<Button>(tabs[i]).Pressed += () => ShowPage(idx);
		}
		ShowPage(0);

        var gs = GameSession.Instance;
        if (gs == null) return;

        if (gs.IsTutorial) AddChild(new TutorialOverlay { RoleName = "Technician" });

        gs.StepChanged += OnStep;
        gs.QuotaChanged += OnQuota;
        gs.KeypadChanged += OnKeypadChanged;
        gs.AirtimeChanged += OnAirtime;
        SetText("%StrikeCount", "none");
        OnQuota();
        if (gs.IsTutorial) OnStep(gs.CurrentStep);
    }
    private void OnAirtime() => SetHealth(100, GameSession.Instance.AirtimeQuality);   // Signal is fixed at 100 until a level changes it
    public override void _ExitTree()
	{
		var gs = GameSession.Instance;
		if (gs == null) return;
        gs.AirtimeChanged -= OnAirtime;
        gs.StepChanged -= OnStep;
		gs.QuotaChanged -= OnQuota;
		gs.KeypadChanged -= OnKeypadChanged;
	}

	private void OnKeypadChanged()
	{
		if (_page == ModulePageIndex) ShowPage(ModulePageIndex);   // refresh level and suffixes
	}

	private void OnStep(TutorialStep step)
	{
		if (step == TutorialStep.KeypadModule) ShowPage(ModulePageIndex);   // open Module Pages automatically
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
		GetNode<Label>("%PageTitle").Text = Pages[i].Title;
		GetNode<Label>("%PageTag").Text = $"Page {i + 1} of {Pages.Length}";
		GetNode<RichTextLabel>("%PageText").Text = i == ModulePageIndex ? MathPage() : Pages[i].Body;
	}

	public void SetHealth(int signal, int quality)
	{
		GetNode<ProgressBar>("%SignalBar").Value = signal;
		GetNode<Label>("%SignalLabel").Text = $"Signal {signal}%";
		GetNode<ProgressBar>("%QualityBar").Value = quality;
		GetNode<Label>("%QualityLabel").Text = $"Quality {quality}%";
	}
}
