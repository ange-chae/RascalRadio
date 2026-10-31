using System.Collections.Generic;
using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems;

public static class LevelRegistry
{
    private static readonly Dictionary<string, LevelDefinition> Levels = new()
    {
        ["morning_broadcast"] = Level1.Definition,
        ["afternoon_broadcast"] = Level2.Definition,
        ["emergency_broadcast"] = Level3.Definition,
    };

    public static LevelDefinition Get(string id) =>
        Levels.TryGetValue(id, out var d) ? d : null;   // null for "tutorial"
}