using System;
using System.Collections.Generic;

public static class SoundboardData
{
    public static readonly string[] Pads = { "RED", "BLUE", "GREEN", "YELLOW" };
    public static readonly string[] Sounds = { "BUZZER", "PHONE", "APPLAUSE", "DRUM ROLL" };

    public static bool IsPad(string padId) => Array.IndexOf(Pads, padId) >= 0;
    public static bool IsSound(string soundId) => Array.IndexOf(Sounds, soundId) >= 0;

    public static string[] DefaultSoundMap() => new[] { "BUZZER", "PHONE", "APPLAUSE", "DRUM ROLL" };

    public static string[] DefaultPressMap() => new[]
    {
        "BLUE", "RED", "YELLOW", "GREEN",
        "YELLOW", "GREEN", "BLUE", "RED",
        "GREEN", "RED", "YELLOW", "BLUE",
    };

    public static string[] RandomSoundMap(Random rng)
    {
        var sounds = new List<string>(Sounds);
        Shuffle(sounds, rng);
        return sounds.ToArray();
    }

    public static string[] RandomSequence(Random rng)
    {
        int length = rng.Next(3, 6);
        var sequence = new string[length];
        for (int i = 0; i < sequence.Length; i++)
            sequence[i] = Pads[rng.Next(Pads.Length)];
        return sequence;
    }

    public static string[] RandomPressMap(Random rng)
    {
        var map = new string[Pads.Length * 3];
        for (int row = 0; row < 3; row++)
        {
            var buttons = new List<string>(Pads);
            Shuffle(buttons, rng);
            for (int cue = 0; cue < Pads.Length; cue++)
                map[row * Pads.Length + cue] = buttons[cue];
        }
        return map;
    }

    private static void Shuffle<T>(IList<T> items, Random rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    // Which existing sound each coloured pad plays
    public static string SoundFor(string padId) => padId switch
    {
        "RED" => "BUZZER",
        "BLUE" => "PHONE",
        "GREEN" => "APPLAUSE",
        "YELLOW" => "DRUM ROLL",
        _ => padId,
    };

    // Rows: no strikes, one strike, two strikes. Columns: RED, BLUE, GREEN, YELLOW flash.
    private static readonly string[][] PressTable =
    {
        new[] { "BLUE",   "RED",   "YELLOW", "GREEN" },
        new[] { "YELLOW", "GREEN", "BLUE",   "RED"   },
        new[] { "GREEN",  "RED",   "YELLOW", "BLUE"  },
    };

    public static string PadToPress(string flash, int strikes)
    {
        int row = Math.Clamp(strikes, 0, PressTable.Length - 1);
        int col = Array.IndexOf(Pads, flash);
        return PressTable[row][col];
    }

    public static string PadToPress(string flash, int strikes, string[] randomizedMap)
    {
        if (randomizedMap == null || randomizedMap.Length != Pads.Length * PressTable.Length)
            return PadToPress(flash, strikes);

        int row = Math.Clamp(strikes, 0, PressTable.Length - 1);
        int col = Array.IndexOf(Pads, flash);
        return col < 0 ? Pads[0] : randomizedMap[row * Pads.Length + col];
    }

    public static Dictionary<string, string[]> GetSequences() => new()
    {
        ["Short Flash"] = new[] { "RED", "BLUE", "GREEN" },
        ["Medium Flash"] = new[] { "YELLOW", "RED", "GREEN", "BLUE" },
        ["Long Flash"] = new[] { "GREEN", "GREEN", "YELLOW", "RED", "BLUE" },
    };
}