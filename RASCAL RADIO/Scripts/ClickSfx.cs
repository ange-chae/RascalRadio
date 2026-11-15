using Godot;

public partial class ClickSfx : Node
{
	private const string ClickPath = "res://assets/sfx/click_sfx.mp3";
	private const int VoiceCount = 4;
	private const float VolumeDb = -6f;

	private readonly System.Collections.Generic.List<AudioStreamPlayer> _voices = new();
	private AudioStream _click;
	private int _nextVoice;

	public bool Muted { get; set; }

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		if (ResourceLoader.Exists(ClickPath))
		{
			_click = GD.Load<AudioStream>(ClickPath);
		}
		else
		{
			GD.PushWarning($"[ClickSfx] Missing click sound at {ClickPath}");
		}

		for (int i = 0; i < VoiceCount; i++)
		{
			var player = new AudioStreamPlayer { VolumeDb = VolumeDb };
			AddChild(player);
			_voices.Add(player);
		}

		GetTree().NodeAdded += OnNodeAdded;
	}

	public override void _Input(InputEvent @event)
	{
		HandleClick(@event, GetViewport());
	}

	private void OnNodeAdded(Node node)
	{
		if (node is Window window && window != GetTree().Root)
		{
			window.WindowInput += @event => HandleClick(@event, window);
		}
	}

	private void HandleClick(InputEvent @event, Viewport viewport)
	{
		if (Muted || _click == null)
		{
			return;
		}

		if (@event is InputEventMouseButton mouse
			&& mouse.Pressed
			&& mouse.ButtonIndex == MouseButton.Left)
		{
			var hovered = viewport.GuiGetHoveredControl();
			if (hovered is LineEdit || hovered is TextEdit)
			{
				return;
			}

			var voice = _voices[_nextVoice];
			_nextVoice = (_nextVoice + 1) % _voices.Count;
			voice.Stream = _click;
			voice.Play();
		}
	}
}
