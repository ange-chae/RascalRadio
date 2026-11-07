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
    public int KeypadMinLine = 1;     // smallest line number the Operator can get
    public int KeypadMaxLine = 9;     // largest line number (99 = tens, 999 = hundreds, 9999 = thousands)
    public LineRuleMode KeypadRule = LineRuleMode.DigitSum;
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
    public bool HasMemory = false;      // does this level use Module D: Memory?
    public int MemoryTarget = 1;        // memory puzzles to solve to finish the memory task
    public int FaultDrain = 1;        // Quality lost per second for each active fault
    public int CalmRecovery = 2;      // Quality regained per second when there are no faults
    public double HealthGrace = 10;   // seconds Quality/Signal must stay below the floor in a row before the task fails
    public int ClockStartMinute = 8 * 60;    // minutes after midnight
    public int ClockEndMinute = 9 * 60;
    public double ShiftSeconds = 240;        // real seconds the whole shift takes
    public double TaskRevealSeconds = 10;   // full task list shows this long at shift start, then only the main task
    public string BackgroundId = "";   // picks the background art: "morning", "afternoon", "night"
    public int MaxSimultaneousModules = 1;   // most modules that can be live at once (1 = never at the same time)
    public double SimultaneousChance = 0;    // 0..1: chance a new module is allowed to start while another is live
    public double ModuleSpawnGap = 18;       // average seconds between spawn attempts
    public double FirstModuleDelay = 6;      // seconds before the first module appears
    public double ModuleIdleGap = 4;

    // Broadcaster interruptions (set per level)
    public bool CoffeeSpill = false;
    public bool StolenKey = false;
    public bool MicFeedback = false;
    public bool InvertedPrompter = false;
    // Technician interruption (on for every level; set false to disable)
    public bool ScrambledTabs = true;
    public double InterruptionGap = 30;   // average seconds between interruptions on each screen
}