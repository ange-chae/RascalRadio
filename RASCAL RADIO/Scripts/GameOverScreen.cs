using Godot;

public partial class GameOverScreen : CanvasLayer
{
    public string LobbyScenePath { get; set; } = "res://scenes/lobby.tscn";   // same path as your other screens

    public override void _Ready()
    {
        Layer = 100;

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.8f) };   // blocks clicks underneath
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        center.AddChild(box);

        var title = new Label { Text = "OFF THE AIR", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 56);
        box.AddChild(title);

        int max = GameSession.MaxStrikes;
        string reason = max == 1 ? "No mistakes allowed. The station is down." : $"{max} strikes. The station is down.";
        var sub = new Label { Text = reason, HorizontalAlignment = HorizontalAlignment.Center };
        sub.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(sub);

        var btn = new Button { Text = "BACK TO LOBBY", CustomMinimumSize = new Vector2(240, 56) };
        UiStyle.StyleButton(btn, UiStyle.Yellow);
        btn.Pressed += () => GameSession.Instance.ReturnToParty();
        box.AddChild(btn);
        btn.GrabFocus();
    }
}