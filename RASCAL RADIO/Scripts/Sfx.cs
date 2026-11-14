using Godot;
using System.Collections.Generic;

public static class Sfx
{
	// Sounds we already loaded, so we don't reload the file every time
	private static readonly Dictionary<string, AudioStream> Loaded = new();

	// Plays a sound from res://assets/sfx/ by name, e.g. Sfx.Play(this, "blip_sfx", -6f)
	public static void Play(Node source, string name, float volumeDb = -6f)
	{
		AudioStream stream = Load(name);
		if (stream == null)
		{
			return;
		}

		// Add the player to the root so the sound keeps playing if the scene changes
		var player = new AudioStreamPlayer { Stream = stream, VolumeDb = volumeDb };
		source.GetTree().Root.AddChild(player);

		// Delete the player once the sound is done
		player.Finished += player.QueueFree;
		player.Play();
	}

	private static AudioStream Load(string name)
	{
		if (Loaded.TryGetValue(name, out AudioStream cached))
		{
			return cached;
		}

		// Try each file type until one exists
		foreach (string extension in new[] { "ogg", "wav", "mp3" })
		{
			string path = $"res://assets/sfx/{name}.{extension}";
			if (ResourceLoader.Exists(path))
			{
				AudioStream stream = GD.Load<AudioStream>(path);
				Loaded[name] = stream;
				return stream;
			}
		}

		GD.PushWarning($"[Sfx] Missing sound: {name}");
		return null;
	}
}
