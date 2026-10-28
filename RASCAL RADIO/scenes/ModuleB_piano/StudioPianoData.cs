using System.Collections.Generic;
using Godot;

/// <summary>
/// Static data for the Studio Piano module (Module B, manual page B1).
/// A cue is a color name + letter + octave (-1 = "-", 0 = "NORMAL", 1 = "+").
/// </summary>
public static class StudioPianoData
{
	public static readonly string[] Keys = { "C", "D", "E", "F", "G", "A", "B" };

	public const string RecalibrationName = "System Recalibration";

	// Every cue shows the color NAME and the letter, so color is never the only clue.
	public static readonly Dictionary<string, Color> SwatchColors = new()
	{
		["RED"] = Color.FromHtml("e53935"),
		["BLUE"] = Color.FromHtml("1e88e5"),
		["GREEN"] = Color.FromHtml("43a047"),
		["YELLOW"] = Color.FromHtml("fdd835"),
		["PURPLE"] = Color.FromHtml("8e24aa"),
	};

	public readonly record struct Cue(string ColorName, string Letter, int Octave = 0);

	public static string OctaveLabel(int octave) => octave switch
	{
		-1 => "-",
		1 => "+",
		_ => "NORMAL",
	};

	/// <summary>Jingles from the Technician's transposition table in the mockup.</summary>
	public static Dictionary<string, Cue[]> GetJingles() => new()
	{
		["Morning Jingle"] = new[] { new Cue("BLUE", "C"), new Cue("GREEN", "E"), new Cue("YELLOW", "G") },
		["Night Jingle"] = new[] { new Cue("PURPLE", "A", -1), new Cue("BLUE", "F", -1), new Cue("RED", "D", -1) },
		["Breaking News Alert"] = new[] { new Cue("RED", "G", 1), new Cue("YELLOW", "G", 1), new Cue("GREEN", "B", 1) },
	};

	/// <summary>Optional fault-recovery sequence: B A G F E D C, descending.</summary>
	public static Cue[] GetRecalibration()
	{
		string[] order = { "B", "A", "G", "F", "E", "D", "C" };
		var cues = new Cue[order.Length];
		for (int i = 0; i < order.Length; i++)
		{
			cues[i] = new Cue("RED", order[i]);
		}
		return cues;
	}
}
