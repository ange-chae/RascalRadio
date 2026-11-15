using Godot;

public partial class GameMusic : Node
{
	private const string MusicFolder = "res://assets/sfx/BGM/";
	private const float PlayingDb = -14f;
	private const float FadeSeconds = 1.0f;

	private AudioStreamPlayer _player;
	private string _currentTrack = "";
	private Tween _fade;

	public override void _Ready()
	{
		_player = new AudioStreamPlayer { VolumeDb = PlayingDb };
		AddChild(_player);

		// Check once a second, in case the level isn't set yet on the first frame
		var timer = new Timer { WaitTime = 1.0, Autostart = true };
		timer.Timeout += CheckLevel;
		AddChild(timer);

		CheckLevel();
	}

	private void CheckLevel()
	{
		if (GameSession.Instance == null)
		{
			return;
		}

		string track = TrackForLevel(GameSession.Instance.LevelId);

		// Only switch when the track changes, so the song doesn't restart
		if (track == null || track == _currentTrack)
		{
			return;
		}
		SwitchTo(track);
	}

	// Each level has its own time of day, so we pick the track by level
	private static string TrackForLevel(string levelId)
	{
		switch (levelId)
		{
			case "tutorial":
			case "morning_broadcast":
				return "bgm_morning";
			case "afternoon_broadcast":
				return "bgm_afternoon";
			case "emergency_broadcast":
				return "bgm_night";
			default:
				return null;
		}
	}

	private void SwitchTo(string track)
	{
		AudioStream stream = LoadTrack(track);
		if (stream == null)
		{
			return;
		}
		_currentTrack = track;

		_fade?.Kill();
		_fade = CreateTween();

		// Fade the old song out, swap to the new one, then fade it in
		if (_player.Playing)
		{
			_fade.TweenProperty(_player, "volume_db", -60f, FadeSeconds);
		}
		_fade.TweenCallback(Callable.From(() =>
		{
			_player.Stream = stream;
			_player.VolumeDb = -60f;
			_player.Play();
		}));
		_fade.TweenProperty(_player, "volume_db", PlayingDb, FadeSeconds);
	}

	private static AudioStream LoadTrack(string name)
	{
		foreach (string extension in new[] { "ogg", "mp3" })
		{
			string path = $"{MusicFolder}{name}.{extension}";
			if (!ResourceLoader.Exists(path))
			{
				continue;
			}

			AudioStream stream = GD.Load<AudioStream>(path);

			// Make the song repeat
			if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
			else if (stream is AudioStreamMP3 mp3) mp3.Loop = true;
			return stream;
		}

		GD.PushWarning($"[GameMusic] Missing track: {name} (looked in {MusicFolder})");
		return null;
	}
}
