using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Plays piano-like notes for the Studio Piano. No audio files needed: each note is
/// generated in code the first time it is played, then cached.
/// Octave -1 / 0 / +1 halves / keeps / doubles the frequency (the manual's "-", NORMAL, "+").
/// </summary>
public partial class StudioPianoSound : Node
{
	private const int MixRate = 44100;
	private const double NoteSeconds = 1.4;
	private const int VoiceCount = 6; // how many notes can ring at once

	// Octave 4 frequencies in Hz.
	private static readonly Dictionary<string, double> BaseFrequencies = new()
	{
		["C"] = 261.63,
		["D"] = 293.66,
		["E"] = 329.63,
		["F"] = 349.23,
		["G"] = 392.00,
		["A"] = 440.00,
		["B"] = 493.88,
	};

	private readonly Dictionary<string, AudioStreamWav> _cache = new();
	private readonly List<AudioStreamPlayer> _voices = new();
	private int _nextVoice;

	public bool Muted { get; set; }

	public void PlayNote(string letter, int octave)
	{
		if (Muted || !BaseFrequencies.TryGetValue(letter, out double baseFrequency))
		{
			return;
		}

		EnsureVoices();

		string cacheKey = $"{letter}{octave}";
		if (!_cache.TryGetValue(cacheKey, out var stream))
		{
			stream = BuildNote(baseFrequency * Math.Pow(2.0, octave));
			_cache[cacheKey] = stream;
		}

		var voice = _voices[_nextVoice];
		_nextVoice = (_nextVoice + 1) % _voices.Count;
		voice.Stream = stream;
		voice.Play();
	}

	private void EnsureVoices()
	{
		if (_voices.Count > 0)
		{
			return;
		}
		for (int i = 0; i < VoiceCount; i++)
		{
			var player = new AudioStreamPlayer { VolumeDb = -6f };
			AddChild(player);
			_voices.Add(player);
		}
	}

	private static AudioStreamWav BuildNote(double frequency)
	{
		int sampleCount = (int)(MixRate * NoteSeconds);
		var data = new byte[sampleCount * 2]; // 16-bit mono

		for (int i = 0; i < sampleCount; i++)
		{
			double t = i / (double)MixRate;

			// Piano-ish tone: a fundamental plus two harmonics that fade faster.
			double wave =
				Math.Sin(2.0 * Math.PI * frequency * t)
				+ 0.5 * Math.Sin(2.0 * Math.PI * frequency * 2.0 * t) * Math.Exp(-3.0 * t)
				+ 0.25 * Math.Sin(2.0 * Math.PI * frequency * 3.0 * t) * Math.Exp(-5.0 * t);

			double attack = Math.Min(1.0, t / 0.004); // tiny fade-in so there is no click
			double decay = Math.Exp(-3.2 * t); // note dies away like a struck string
			double release = Math.Min(1.0, (NoteSeconds - t) / 0.03); // fade-out at the very end

			double sample = wave * attack * decay * release * 0.35;
			short value = (short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue);
			data[i * 2] = (byte)(value & 0xFF);
			data[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
		}

		return new AudioStreamWav
		{
			Format = AudioStreamWav.FormatEnum.Format16Bits,
			MixRate = MixRate,
			Stereo = false,
			Data = data,
		};
	}
}
