using Godot;
using System;

public partial class Menu : Control
{
    [Export] public Button PlayButton { get; set; }
    [Export] public Button CreditsButton { get; set; }
    [Export] public Button QuitButton { get; set; }
    [Export] public ColorRect Fade { get; set; }
    [Export] public AudioStreamPlayer StartSfx { get; set; }

    // Assign the "TextureRect" node (the background image) so it can drift slowly
    [Export] public TextureRect BackgroundRect { get; set; }

    private const string LobbyScenePath = "res://scenes/lobby.tscn";
    private const string CreditsScenePath = "res://scenes/credits.tscn";

    private bool _isTransitioning = false;

    public override void _Ready()
    {
        if (Fade != null)
        {
            Fade.Color = new Color(0, 0, 0, 0);
            Fade.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        if (PlayButton == null || QuitButton == null)
        {
            GD.PrintErr("Assign PlayButton and QuitButton in the Inspector!");
            return;
        }

        PlayButton.Pressed += () => TransitionTo(LobbyScenePath);
        QuitButton.Pressed += OnQuitButtonPressed;

        if (CreditsButton != null)
            CreditsButton.Pressed += () => TransitionTo(CreditsScenePath);
        else
            GD.PrintErr("CreditsButton is not assigned in the Inspector.");
    }

    // Shared by Play and Credits: same sound, same fade to black, then change scene.
    // Buttons are not disabled; _isTransitioning just ignores extra clicks.
    private async void TransitionTo(string scenePath)
    {
        if (_isTransitioning)
            return;

        _isTransitioning = true;

        StartSfx?.Play();

        if (Fade != null)
        {
            Tween tween = CreateTween();
            tween.TweenProperty(Fade, "color:a", 1.0f, 1.5f);
            await ToSignal(tween, Tween.SignalName.Finished);
        }

        Error result = GetTree().ChangeSceneToFile(scenePath);

        if (result != Error.Ok)
        {
            GD.PrintErr($"Failed to load scene {scenePath}: {result}");
            _isTransitioning = false;
        }
    }

    // Slow, subtle camera-like drift: the image is scaled up a little so its edges
    // never show, then it floats a few pixels around and breathes in and out.

    private void OnQuitButtonPressed()
    {
        if (_isTransitioning)
            return;

        GD.Print("Backend Event: Exit requested. Safely terminating engine lifecycle...");
        GetTree().Quit();
    }
}