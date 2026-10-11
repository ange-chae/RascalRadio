using Godot;
using System;

public partial class Menu : Control
{
    [Export] public Button PlayButton { get; set; }
    [Export] public Button QuitButton { get; set; }
    [Export] public ColorRect Fade { get; set; }

    private bool _isTransitioning = false;

    public override void _Ready()
    {
        if (PlayButton == null || QuitButton == null || Fade == null)
        {
            GD.PrintErr(
                "Backend Error: Assign PlayButton, QuitButton, and Fade in the Inspector!"
            );
            return;
        }

        Fade.Color = new Color(0, 0, 0, 0);
        Fade.MouseFilter = Control.MouseFilterEnum.Ignore;

        PlayButton.Pressed += OnPlayButtonPressed;
        QuitButton.Pressed += OnQuitButtonPressed;
    }

    private async void OnPlayButtonPressed()
    {
        if (_isTransitioning)
            return;

        _isTransitioning = true;
        PlayButton.Disabled = true;

        Tween tween = CreateTween();
        tween.TweenProperty(Fade, "color:a", 1.0f, 0.7f);

        await ToSignal(tween, Tween.SignalName.Finished);

        Error result = GetTree().ChangeSceneToFile(
            "res://scenes/lobby.tscn"
        );

        if (result != Error.Ok)
        {
            GD.PrintErr($"Failed to load lobby scene: {result}");
            _isTransitioning = false;
            PlayButton.Disabled = false;
        }
    }

    private void OnQuitButtonPressed()
    {
        GD.Print("Backend Event: Exit requested. Safely terminating engine lifecycle...");
        GetTree().Quit();
    }
}

