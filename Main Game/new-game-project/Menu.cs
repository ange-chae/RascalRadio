using Godot;
using System;

public partial class Menu : Control {
	[Export] public Button PlayButton { get; set; }
	[Export] public Button QuitButton { get; set; }

	public override void _Ready() {
		if (PlayButton == null || QuitButton == null)
		{
			GD.PrintErr("Backend Error: Buttons are not assigned in the Godot Inspector!");
			return;
		}

		PlayButton.Pressed += OnPlayButtonPressed;
		QuitButton.Pressed += OnQuitButtonPressed;
	}

	private void OnPlayButtonPressed()
	{
		GD.Print("Backend Event: Play requested. Swapping game state...");
	}

	private void OnQuitButtonPressed()
	{
		GD.Print("Backend Event: Exit requested. Safely terminating engine lifecycle...");
		GetTree().Quit();
	}
	
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (PlayButton != null) PlayButton.Pressed -= OnPlayButtonPressed;
			if (QuitButton != null) QuitButton.Pressed -= OnQuitButtonPressed;
		}
		base.Dispose(disposing);
	}
}
