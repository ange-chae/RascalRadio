using Godot;

public partial class RoleCard : PanelContainer
{
	public enum CardState { Available, Mine, Taken }

	[Signal]
	public delegate void SelectPressedEventHandler();

	private Label _nameLabel;
	private Label _blurbLabel;
	private Label _takenByLabel;
	private Button _selectButton;
	private Panel _selectionOutline;

	public override void _Ready()
	{
		_nameLabel = GetNodeOrNull<Label>("CardArea/RoleNameLabel");
		_blurbLabel = GetNodeOrNull<Label>("CardArea/RoleBlurbLabel");
		_takenByLabel = GetNodeOrNull<Label>("CardArea/TakenByLabel");
		_selectButton = GetNodeOrNull<Button>("SelectButton");

		// Grab the outline and hide it by default
		_selectionOutline = GetNodeOrNull<Panel>("CardArea/SelectionOutline");
		if (_selectionOutline != null) _selectionOutline.Hide();

		if (_selectButton != null)
		{
			_selectButton.Pressed += () => EmitSignal(SignalName.SelectPressed);
		}
	}

	public void Setup(string roleName, string blurb, Texture2D icon = null)
	{
		if (_nameLabel != null) _nameLabel.Text = roleName;
		if (_blurbLabel != null) _blurbLabel.Text = blurb;

		if (icon != null)
		{
			var styleBox = new StyleBoxTexture { Texture = icon };
			AddThemeStyleboxOverride("panel", styleBox);
		}
	}

	public void SetState(CardState state, string takenBy = "")
	{
		if (_takenByLabel != null)
		{
			switch (state)
			{
				case CardState.Available:
					_takenByLabel.Text = "Available";
					if (_selectionOutline != null) _selectionOutline.Hide();
					break;

				case CardState.Mine:
					_takenByLabel.Text = "Your role";
					if (_selectionOutline != null)
					{
						_selectionOutline.Show();
						_selectionOutline.Modulate = new Color(1, 1, 1); // Keeps your original Inspector color (e.g., green/gold)
					}
					break;

				case CardState.Taken:
					_takenByLabel.Text = string.IsNullOrEmpty(takenBy) ? "Taken" : $"Taken by {takenBy}";
					if (_selectionOutline != null)
					{
						_selectionOutline.Show();
						_selectionOutline.Modulate = new Color("#F44336"); // Turns the outline red
					}
					break;
			}
		}

		if (_selectButton != null)
		{
			_selectButton.Disabled = (state == CardState.Taken);
		}
	}
}
