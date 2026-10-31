using System;
using System.Collections.Generic;
using RascalRadio.Core.Systems;

namespace RascalRadio.Core.Models;

public class LevelDefinition
{
    public string Id = "";
    public string Title = "";
    public Genre QuotaGenre = Genre.Drama;
    public int QuotaTarget = 3;
    public double SegmentTime = 12.0;

    // The callers in the order they ring in. This is the main thing level coders write.
    public Func<CallerBank, List<Caller>> BuildCallers;

    public bool HasKeypad = false;     // does this level use the Line Override keypad?
    public bool AllowStatic = true;    // static faults on the keypad
}