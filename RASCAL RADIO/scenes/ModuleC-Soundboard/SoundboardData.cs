using System;
using System.Collections.Generic;

public static class SoundboardData
{
	public static readonly string[] Pads = { "APPLAUSE", "LAUGHTER", "BUZZER", "PHONE", "DRUM ROLL" };

	public static bool IsPad(string padId) => Array.IndexOf(Pads, padId) >= 0;

	public static Dictionary<string, string[]> GetSequences() => new()
	{
		["Caller Prank"] = new[] { "PHONE", "LAUGHTER", "APPLAUSE" },
		["Big Reveal"] = new[] { "DRUM ROLL", "APPLAUSE", "LAUGHTER" },
		["Wrong Answer"] = new[] { "BUZZER", "LAUGHTER", "BUZZER" },
	};
}
