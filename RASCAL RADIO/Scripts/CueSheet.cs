using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// PIANO COLOR CODE. Every piano key gets a color. The Broadcaster SEES the color on the cue sheet,
/// the Technician's manual says which key it means, the Operator presses the key.
/// The code is reshuffled for every jingle, so nobody can memorize it.
/// </summary>
public static class PianoColorCode
{
    public static readonly string[] Names = { "RED", "ORANGE", "YELLOW", "GREEN", "BLUE", "PURPLE", "PINK" };
    private static readonly string[] Hexes = { "#F44336", "#FF9800", "#FDD835", "#43A047", "#42A5F5", "#AB47BC", "#F48FB1" };

    public static string NameOf(int c) => Names[c % Names.Length];
    public static string HexOf(int c) => Hexes[c % Hexes.Length];
    public static Color ColorOf(int c) => new Color(Hexes[c % Hexes.Length]);

    /// <summary>The piano key letters, in the same order the Operator's keys use.</summary>
    public static List<string> Keys() => new List<string>(StudioPianoData.Keys);

    /// <summary>Random color for every key (index = key position, value = color index). Host only.</summary>
    public static int[] NewMap(Random rng)
    {
        int n = Keys().Count;
        var map = new int[n];
        for (int i = 0; i < n; i++) map[i] = i;
        for (int i = n - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (map[i], map[j]) = (map[j], map[i]);
        }
        return map;
    }
}