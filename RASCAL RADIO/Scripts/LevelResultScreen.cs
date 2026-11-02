using Godot;

public partial class LevelResultScreen : CanvasLayer
{
    public int CompletionPts, AccuracyPts, Completed, Total;

    public override void _Ready()
    {
        Layer = 100;
        var gs = GameSession.Instance;
        int score = CompletionPts + AccuracyPts;
        var (rank, title) = GameSession.Rating(score);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.85f) };   // blocks clicks underneath
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        center.AddChild(box);

        var rankColor = rank switch { "S" => new Color("FFD25A"), "A" => new Color("F4E3A0"), "B" => new Color("CFE8CF"), _ => new Color("E5484D") };
        var rankLbl = Line(rank, 120, rankColor);
        box.AddChild(rankLbl);
        box.AddChild(Line(title.ToUpper(), 34, Colors.White));
        box.AddChild(Line($"SCORE  {score} / 50", 24, Colors.White));
        box.AddChild(Line($"Task completion  {CompletionPts} / 25   ({Completed} of {Total} tasks)", 18, Colors.White));
        box.AddChild(Line($"Accuracy  {AccuracyPts} / 25", 18, Colors.White));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        foreach (var t in gs.Tasks)
        {
            bool ok = t.IsHealth ? !t.Failed : t.Done;
            string mistakes = t.Mistakes > 0 ? $"   ({t.Mistakes} mistake{(t.Mistakes > 1 ? "s" : "")})" : "";
            box.AddChild(Line($"{(ok ? "DONE  " : "MISSED")}   {gs.TaskText(t)}{mistakes}", 15,
                ok ? new Color("A8E0A8") : new Color("F2A0A0")));
        }

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) });
        var btn = new Button { Text = "BACK TO LOBBY", CustomMinimumSize = new Vector2(240, 56), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        UiStyle.StyleButton(btn, UiStyle.Yellow);
        btn.Pressed += () => GameSession.Instance.ReturnToParty();
        box.AddChild(btn);
    }

    private static Label Line(string text, int size, Color color)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}