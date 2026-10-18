using Godot;

public partial class RoleCard : PanelContainer
{
	public enum CardState { Available, Mine, Taken }

	[Signal]
	public delegate void SelectPressedEventHandler();

	private TextureRect _icon;
	private Label _nameLabel;
	private Label _blurbLabel;
	private Label _takenByLabel;
	private Button _selectButton;

	public override void _Ready()
	{
		_icon = GetNode<TextureRect>("Margin/VBox/IconFrame/RoleIcon");
		_nameLabel = GetNode<Label>("Margin/VBox/RoleNameLabel");
		_blurbLabel = GetNode<Label>("Margin/VBox/RoleBlurbLabel");
		_takenByLabel = GetNode<Label>("Margin/VBox/TakenByLabel");
		_selectButton = GetNode<Button>("Margin/VBox/SelectButton");

		_selectButton.Pressed += () => EmitSignal(SignalName.SelectPressed);
	}

	// Sets the text and art. Call this after the card has been added to the scene.
	public void Setup(string roleName, string blurb, Texture2D icon = null)
	{
		_nameLabel.Text = roleName;
		_blurbLabel.Text = blurb;
		_icon.Texture = icon;
	}

	// Switches the card between Available / Mine / Taken.
	public void SetState(CardState state, string takenBy = "")
	{
		switch (state)
		{
			case CardState.Available:
				ThemeTypeVariation = "";
				_takenByLabel.Text = "Available";
				_selectButton.Text = "Select";
				_selectButton.Disabled = false;
				break;
			case CardState.Mine:
				ThemeTypeVariation = "RoleCardSelected";
				_takenByLabel.Text = "Your role";
				_selectButton.Text = "Selected";
				_selectButton.Disabled = true;
				break;
			case CardState.Taken:
				ThemeTypeVariation = "RoleCardTaken";
				_takenByLabel.Text = string.IsNullOrEmpty(takenBy) ? "Taken" : $"Taken by {takenBy}";
				_selectButton.Text = "Taken";
				_selectButton.Disabled = true;
				break;
		}
	}
}
