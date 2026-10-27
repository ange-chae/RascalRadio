using Godot;
using RascalRadio.Core.Models;

// Built entirely in code: no .tscn needed.
// Each role screen does: AddChild(new TutorialOverlay { RoleName = "Operator" });
// A small box at the bottom-center. It never blocks clicks outside the box itself.
public partial class TutorialOverlay : CanvasLayer
{
    public string RoleName { get; set; } = "";

    private const int FontSize = 14;
    private Label _happening, _job, _done, _feedback;
    private Button _gotIt, _toggle;
    private VBoxContainer _body;

    public override void _Ready()
    {
        var gs = GameSession.Instance;
        if (gs == null || !gs.IsTutorial) { QueueFree(); return; }

        Layer = 20;

        // Full-screen holder that ignores the mouse, so buttons underneath stay clickable.
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_bottom", 6);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(column);

        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(460, 0),
        };
        column.AddChild(panel);

        var inner = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            inner.AddThemeConstantOverride(side, 6);
        panel.AddChild(inner);

        var box = new VBoxContainer();
        inner.AddChild(box);

        var header = new HBoxContainer();
        box.AddChild(header);
        var badge = new Label { Text = "🎓 PRACTICE · no penalties", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        badge.AddThemeFontSizeOverride("font_size", FontSize);
        header.AddChild(badge);
        _toggle = new Button { Text = "Hide ▼" };
        _toggle.AddThemeFontSizeOverride("font_size", FontSize);
        _toggle.Pressed += () => SetExpanded(!_body.Visible);
        header.AddChild(_toggle);

        _body = new VBoxContainer();
        box.AddChild(_body);
        _happening = MakeLine(_body);
        _job = MakeLine(_body);
        _done = MakeLine(_body);
        _feedback = MakeLine(_body);

        _gotIt = new Button { Text = "GOT IT" };
        _gotIt.AddThemeFontSizeOverride("font_size", FontSize);
        _gotIt.Pressed += () =>
        {
            GameSession.Instance.Report(TutorialAction.Acknowledged);
            _gotIt.Disabled = true;
            _gotIt.Text = "Waiting for the others...";
        };
        _body.AddChild(_gotIt);

        gs.StepChanged += OnStep;
        gs.Feedback += OnFeedback;
        OnStep(gs.CurrentStep);
    }

    public override void _ExitTree()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;
        gs.StepChanged -= OnStep;
        gs.Feedback -= OnFeedback;
    }

    private static Label MakeLine(VBoxContainer parent)
    {
        var l = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(440, 0),
        };
        l.AddThemeFontSizeOverride("font_size", FontSize);
        parent.AddChild(l);
        return l;
    }

    private void SetExpanded(bool open)
    {
        _body.Visible = open;
        _toggle.Text = open ? "Hide ▼" : "Show ▲";
    }

    private void OnStep(TutorialStep step)
    {
        _happening.Text = "❓ " + TutorialText.Happening(step);
        _job.Text = "👉 " + TutorialText.Job(step, RoleName);
        _done.Text = "✔ Done when: " + TutorialText.DoneWhen(step);
        _feedback.Text = "";
        _gotIt.Visible = step == TutorialStep.MeetStudio;
        SetExpanded(true);   // new step: always show the new instructions
    }

    private void OnFeedback(string text, bool success) => _feedback.Text = text;
}