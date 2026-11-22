using Godot;

// Pause menu. The pause itself lives in GameSession so it is the same for everyone:
// whoever opens it pauses every player, and the menu shows who did.
public partial class InGameMenu : CanvasLayer
{
    private ColorRect _dim;
    private Button _restart;
    private Label _pausedBy;
    private bool _localOpen;   // only used when there is no GameSession (running a scene by itself)

    public override void _Ready()
    {
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;   // keeps working while the game is paused

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

        // "Player 1 paused the game"
        _pausedBy = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _pausedBy.AddThemeFontSizeOverride("font_size", 18);
        _pausedBy.AddThemeColorOverride("font_color", UiStyle.Ink);
        column.AddChild(_pausedBy);

        var resume = MakeButton("RESUME");
        resume.Pressed += () => SetOpen(false);
        column.AddChild(resume);

        _restart = MakeButton("RESTART");
        _restart.Pressed += () => GameSession.Instance?.RestartCurrentLevel();
        column.AddChild(_restart);

        // Takes everyone back to the players list. Roles and the chosen level are kept there.
        var back = MakeButton("BACK TO LOBBY");
        back.Pressed += BackToLobby;
        column.AddChild(back);

        if (GameSession.Instance != null) GameSession.Instance.PauseChanged += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (GameSession.Instance != null) GameSession.Instance.PauseChanged -= Refresh;
    }

    // Open or close the menu. With a GameSession this pauses or resumes for everyone.
    public void SetOpen(bool open)
    {
        var gs = GameSession.Instance;
        if (gs == null) { _localOpen = open; Refresh(); return; }
        gs.RequestPause(open);
    }

    private void Refresh()
    {
        var gs = GameSession.Instance;
        bool open = gs != null ? gs.IsPaused : _localOpen;
        Visible = open;

        if (_pausedBy != null)
            _pausedBy.Text = gs != null && open ? $"{gs.PausedBy} paused the game" : "";

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
            var gs = GameSession.Instance;
            SetOpen(!(gs != null ? gs.IsPaused : _localOpen));
            GetViewport().SetInputAsHandled();
        }
    }

    private void BackToLobby()
    {
        var gs = GameSession.Instance;
        if (gs != null) gs.ReturnToParty();
        else GetTree().ChangeSceneToFile(GameSession.PartyScenePath);
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