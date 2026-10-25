using Godot;

public partial class PopupFrame : Control
{
	[Signal] public delegate void ClosedEventHandler();

	public override void _Ready()
	{
		GetNode<Button>("%CloseBtn").Pressed += Close;
		Open();
	}

	private async void Open()
	{
		var popup = GetNode<Control>("%Popup");
		Modulate = new Color(1, 1, 1, 0);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		popup.PivotOffset = popup.Size / 2;
		popup.Scale = new Vector2(0.92f, 0.92f);
		var t = CreateTween().SetParallel();
		t.TweenProperty(this, "modulate:a", 1.0f, 0.15);
		t.TweenProperty(popup, "scale", Vector2.One, 0.15)
			.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
	}

	public void Close()
	{
		var t = CreateTween();
		t.TweenProperty(this, "modulate:a", 0.0f, 0.1);
		t.TweenCallback(Callable.From(() => { EmitSignal(SignalName.Closed); QueueFree(); }));
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e.IsActionPressed("ui_cancel")) Close();
	}
}
