using System;
using System.Collections.Generic;

public static class SoundboardData
{
    public static readonly string[] Pads = { "RED", "BLUE", "GREEN", "YELLOW" };

    public static bool IsPad(string padId) => Array.IndexOf(Pads, padId) >= 0;

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

    public static Dictionary<string, string[]> GetSequences() => new()
    {
        ["Short Flash"] = new[] { "RED", "BLUE", "GREEN" },
        ["Medium Flash"] = new[] { "YELLOW", "RED", "GREEN", "BLUE" },
        ["Long Flash"] = new[] { "GREEN", "GREEN", "YELLOW", "RED", "BLUE" },
    };
}