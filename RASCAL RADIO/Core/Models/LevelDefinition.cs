using System;
using System.Collections.Generic;
using RascalRadio.Core.Systems;

namespace RascalRadio.Core.Models;

public class LevelDefinition
{
    public double PrompterLineRest = 1.5;        // seconds of quiet after each prompter line
    public double PrompterParagraphRest = 5.0;   // longer breather after a whole paragraph
    public double CallerGap = 6;   // seconds of quiet between callers
    public string Id = "";
    public string Title = "";
    public Genre QuotaGenre = Genre.Drama;

    // Several genres per level. If left null, the level just uses QuotaGenre.
    public Genre[] AcceptedGenres = null;
    public IReadOnlyList<Genre> Genres => AcceptedGenres ?? new[] { QuotaGenre };
    public int QuotaTarget = 3;
    public int MaxStrikes = 3;
    public double SegmentTime = 12.0;
    public double SabotageInterval = 0;
    // The callers in the order they ring in. This is the main thing level coders write.
    public Func<CallerBank, Genre, List<Caller>> BuildCallers;

    public bool HasKeypad = false;     // does this level use the Line Override keypad?
    public bool AllowStatic = true;    // static faults on the keypad
    public bool HasPiano = false;      // does this level use Module B: Piano?
    public bool HasSoundboard = false; // does this level use Module C: Soundboard?
    public int KeypadTarget = 3;        // lines to route to finish the keypad task
    public int PianoTarget = 2;         // jingles to play to finish the piano task
    public int SoundboardTarget = 2;    // cues to play to finish the soundboard task
    public int QualityFloor = 55;       // Quality must stay ABOVE this
    public int SignalFloor = 30;        // Signal must stay ABOVE this (30 = both modules faulty at once)
    public bool FlipSabotage = false;   // raccoon can turn a module upside down

    public int FaultDrain = 1;        // Quality lost per second for each active fault
    public int CalmRecovery = 2;      // Quality regained per second when there are no faults
    public double HealthGrace = 10;   // seconds Quality/Signal must stay below the floor in a row before the task fails
}