using System;
using System.Collections.Generic;
using Godot;

public partial class SoundboardSound : Node
{
	private const int MixRate = 44100;
	private const int VoiceCount = 6;
	private const string OverrideFolder = "res://assets/sfx/soundboard/";
	private static readonly string[] OverrideExtensions = { "ogg", "wav", "mp3" };

	private readonly Dictionary<string, AudioStream> _cache = new();
	private readonly List<AudioStreamPlayer> _voices = new();
	private int _nextVoice;

	public bool Muted { get; set; }

	public void Play(string padId)
	{
		if (Muted)
		{
			return;
		}

		EnsureVoices();

		if (!_cache.TryGetValue(padId, out var stream))
		{
			stream = LoadOverride(padId) ?? BuildEffect(padId);
			if (stream == null)
			{
				return;
			}
			_cache[padId] = stream;
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
			var player = new AudioStreamPlayer { VolumeDb = -4f };
			AddChild(player);
			_voices.Add(player);
		}
	}

	private static AudioStream LoadOverride(string padId)
	{
		string fileName = padId.ToLower().Replace(' ', '_');
		foreach (string extension in OverrideExtensions)
		{
			string path = $"{OverrideFolder}{fileName}.{extension}";
			if (ResourceLoader.Exists(path))
			{
				return GD.Load<AudioStream>(path);
			}
		}
		return null;
	}

	private static AudioStream BuildEffect(string padId) => padId switch
	{
		"BUZZER" => ToWav(MakeBuzzer()),
		"PHONE" => ToWav(MakePhone()),
		"DRUM ROLL" => ToWav(MakeDrumRoll()),
		"APPLAUSE" => ToWav(MakeApplause()),
		"LAUGHTER" => ToWav(MakeLaughter()),
		_ => null,
	};

	private static float[] MakeBuzzer()
	{
		int n = (int)(MixRate * 0.7);
		var s = new float[n];
		for (int i = 0; i < n; i++)
		{
			double t = i / (double)MixRate;
			double a = Math.Sign(Math.Sin(2.0 * Math.PI * 110.0 * t));
			double b = Math.Sign(Math.Sin(2.0 * Math.PI * 165.5 * t));
			s[i] = (float)(0.6 * a + 0.4 * b);
		}
		return s;
	}

	private static float[] MakePhone()
	{
		int n = (int)(MixRate * 1.0);
		var s = new float[n];
		for (int i = 0; i < n; i++)
		{
			double t = i / (double)MixRate;
			bool ringing = t < 0.4 || (t >= 0.55 && t < 0.95);
			if (!ringing)
			{
				continue;
			}
			double trill = 0.5 * (1.0 + Math.Sin(2.0 * Math.PI * 20.0 * t - Math.PI / 2.0));
			double tone = Math.Sin(2.0 * Math.PI * 440.0 * t) + Math.Sin(2.0 * Math.PI * 480.0 * t);
			s[i] = (float)(tone * trill);
		}
		return s;
	}

	private static float[] MakeDrumRoll()
	{
		var hits = new List<double>();
		double hitTime = 0.0;
		double interval = 0.08;
		while (hitTime < 1.1)
		{
			hits.Add(hitTime);
			hitTime += interval;
			interval = Math.Max(0.035, interval * 0.96);
		}
		hits.Add(1.15);

		var rng = new Random(7);
		int n = (int)(MixRate * 1.45);
		var s = new float[n];
		int hitIndex = -1;
		for (int i = 0; i < n; i++)
		{
			double t = i / (double)MixRate;
			while (hitIndex + 1 < hits.Count && hits[hitIndex + 1] <= t)
			{
				hitIndex++;
			}
			if (hitIndex < 0)
			{
				continue;
			}

			double since = t - hits[hitIndex];
			bool accent = hitIndex == hits.Count - 1;
			double amp = accent ? 1.3 : 0.4 + 0.6 * (hits[hitIndex] / 1.15);
			double noiseDecay = accent ? 0.06 : 0.025;
			double noise = (rng.NextDouble() * 2.0 - 1.0) * Math.Exp(-since / noiseDecay);
			double thump = Math.Sin(2.0 * Math.PI * 160.0 * since) * Math.Exp(-since / 0.05);
			s[i] = (float)(amp * (0.7 * noise + 0.5 * thump));
		}
		return s;
	}

	private static float[] MakeApplause()
	{
		const double duration = 1.8;
		var rng = new Random(11);
		int n = (int)(MixRate * duration);
		var s = new float[n];

		double lowPass = 0.0;
		for (int i = 0; i < n; i++)
		{
			double t = i / (double)MixRate;
			double noise = rng.NextDouble() * 2.0 - 1.0;
			lowPass += 0.15 * (noise - lowPass);
			double hiss = noise - lowPass;
			s[i] = (float)(hiss * 0.25 * Swell(t, duration));
		}

		for (int clap = 0; clap < 140; clap++)
		{
			double start = rng.NextDouble() * 1.6;
			double amp = (0.3 + rng.NextDouble() * 0.7) * Swell(start, duration);
			int startIndex = (int)(start * MixRate);
			int length = (int)(0.03 * MixRate);
			for (int j = 0; j < length && startIndex + j < n; j++)
			{
				double d = j / (double)MixRate;
				double burst = (rng.NextDouble() * 2.0 - 1.0) * Math.Exp(-d / 0.007);
				s[startIndex + j] += (float)(burst * amp);
			}
		}
		return s;
	}

	private static double Swell(double t, double duration)
		=> Math.Min(1.0, t / 0.3) * Math.Min(1.0, (duration - t) / 0.5);

	private static float[] MakeLaughter()
	{
		var rng = new Random(5);
		int n = (int)(MixRate * 1.3);
		var s = new float[n];
		double[] starts = { 0.05, 0.30, 0.55, 0.80 };
		double[] basePitch = { 300.0, 280.0, 260.0, 235.0 };
		const double burstLength = 0.17;
		double phase = 0.0;

		for (int i = 0; i < n; i++)
		{
			double t = i / (double)MixRate;

			int burst = -1;
			for (int k = 0; k < starts.Length; k++)
			{
				if (t >= starts[k] && t < starts[k] + burstLength)
				{
					burst = k;
				}
			}
			if (burst < 0)
			{
				continue;
			}

			double local = t - starts[burst];
			double f0 = basePitch[burst] * (1.0 - 0.25 * local / burstLength);
			phase += 2.0 * Math.PI * f0 / MixRate;
			double env = Math.Sin(Math.PI * local / burstLength) * (1.0 - 0.12 * burst);

			double voiced = 0.0;
			for (int h = 1; h <= 14; h++)
			{
				double freq = f0 * h;
				double weight = Math.Exp(-Math.Pow((freq - 800.0) / 450.0, 2.0))
					+ 0.6 * Math.Exp(-Math.Pow((freq - 1300.0) / 500.0, 2.0));
				voiced += weight * Math.Sin(h * phase);
			}
			double breath = (rng.NextDouble() * 2.0 - 1.0) * 0.15;
			s[i] = (float)((voiced * 0.25 + breath) * env);
		}
		return s;
	}

	private static AudioStreamWav ToWav(float[] samples)
	{
		int fadeIn = (int)(MixRate * 0.004);
		int fadeOut = (int)(MixRate * 0.03);

		float peak = 0f;
		foreach (float v in samples)
		{
			peak = Math.Max(peak, Math.Abs(v));
		}
		float scale = peak > 0f ? 0.6f / peak : 1f;

		var data = new byte[samples.Length * 2];
		for (int i = 0; i < samples.Length; i++)
		{
			float gain = scale;
			if (i < fadeIn)
			{
				gain *= i / (float)fadeIn;
			}
			int fromEnd = samples.Length - 1 - i;
			if (fromEnd < fadeOut)
			{
				gain *= fromEnd / (float)fadeOut;
			}

			short value = (short)(Math.Clamp(samples[i] * gain, -1f, 1f) * short.MaxValue);
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
