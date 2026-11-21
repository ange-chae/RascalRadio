using Godot;

public partial class InGameMenu : CanvasLayer
{
    private ColorRect _dim;
    private Button _restart;
    private bool _open;

    public override void _Ready()
    {
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;

        _dim = new ColorRect
        {
            Color = new Color(0.06f, 0.03f, 0.09f, 0.68f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(_dim);
        _dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(360, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Card());
        center.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        var title = new Label { Text = "ON AIR MENU", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", UiStyle.Ink);
        column.AddChild(title);

        var resume = MakeButton("RESUME");
        resume.Pressed += () => SetOpen(false);
        column.AddChild(resume);

        _restart = MakeButton("RESTART");
        _restart.Pressed += () => GameSession.Instance?.RestartCurrentLevel();
        column.AddChild(_restart);

        var quit = MakeButton("QUIT TO MENU");
        quit.Pressed += QuitToMenu;
        column.AddChild(quit);

        SetOpen(false);
    }

    public void SetOpen(bool open)
    {
        _open = open;
        Visible = open;
        if (_restart != null)
        {
            bool host = !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
            _restart.Disabled = !host;
            _restart.Text = host ? "RESTART" : "RESTART (HOST ONLY)";
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel") && !e.IsEcho())
        {
            SetOpen(!_open);
            GetViewport().SetInputAsHandled();
        }
    }

    private void QuitToMenu()
    {
        var network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        network?.DisconnectFromRoom();
        GetTree().ChangeSceneToFile("res://scenes/lobby.tscn");
    }

    private static Button MakeButton(string text)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(300, 50),
            FocusMode = Control.FocusModeEnum.None,
        };
        UiStyle.StyleButton(button, UiStyle.Yellow);
        button.AddThemeFontSizeOverride("font_size", 20);
        return button;
    }
}
