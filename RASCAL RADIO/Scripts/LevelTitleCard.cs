using Godot;

public partial class LevelTitleCard : CanvasLayer
{
    public string Title { get; set; } = "LEVEL 1";
    public float Delay { get; set; } = 0f;           // quiet pause before the card fades in
    public float Hold { get; set; } = 2.5f;          // how long it stays after fading in
    public bool ReportWhenDone { get; set; } = false; // true when this card leads into a level

    private ColorRect _dim;
    private Label _label;

    public override void _Ready()
    {
        Layer = 50;

        _dim = new ColorRect { Color = new Color(0, 0, 0, 0.85f), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_dim);
        _dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _label = new Label
        {
            Text = Title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 96);
        AddChild(_label);
        _label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _dim.Modulate = new Color(1, 1, 1, 0);
        _label.Modulate = new Color(1, 1, 1, 0);

        var t = CreateTween().SetParallel();
        t.TweenProperty(_dim, "modulate:a", 1.0f, 0.8).SetDelay(Delay);
        t.TweenProperty(_label, "modulate:a", 1.0f, 0.8).SetDelay(Delay);

        // once visible, block the mouse
        GetTree().CreateTimer(Delay).Timeout += () =>
        {
            if (IsInstanceValid(_dim)) _dim.MouseFilter = Control.MouseFilterEnum.Stop;
        };

        if (ReportWhenDone && GameSession.Instance != null)
        {
            GameSession.Instance.LevelStarted += OnLevelStarted;
            // after the hold, tell the host we're ready; the host starts the level when everyone is
            GetTree().CreateTimer(Delay + 0.8 + Hold).Timeout += () => GameSession.Instance?.ReportEndingDone();
        }
    }

    public override void _ExitTree()
    {
        if (GameSession.Instance != null) GameSession.Instance.LevelStarted -= OnLevelStarted;
    }

    private void OnLevelStarted()
    {
        // let the new screen load underneath, then fade the card away
        var t = CreateTween().SetParallel();
        t.TweenProperty(_dim, "modulate:a", 0.0f, 0.6).SetDelay(0.6);
        t.TweenProperty(_label, "modulate:a", 0.0f, 0.6).SetDelay(0.6);
        t.Chain().TweenCallback(Callable.From(QueueFree));
    }
}