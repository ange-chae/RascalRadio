using System.Collections.Generic;
using System.Linq;
using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems;

public static class Level3
{
	private static readonly Genre[] Accepted = { Genre.Drama, Genre.Comedy, Genre.Music, Genre.Romance, Genre.Horror, Genre.Thriller, Genre.Mystery, Genre.Tragedy, Genre.Revenge, Genre.Supernatural };

	public static readonly LevelDefinition Definition = new()
	{
		// ---------- Identity ----------
		Id = "emergency_broadcast",     // Must match LevelRegistry and PlayerList cassette tape 4
		Title = "LEVEL 3: EMERGENCY BROADCAST",
        BackgroundId = "night",

        // ---------- Topic and goal ----------
        QuotaGenre = Genre.Drama,
		AcceptedGenres = Accepted,
		QuotaTarget = 5,              // Requires 5 accepted callers to win

		// ---------- Strike Rule ----------
		MaxStrikes = 1,               // 1-strike failure mode for Level 3

		// ---------- Modules (Studio Desk) ----------
		HasMemory = true,
		HasKeypad = true,             // Line Override keypad active (Slot 1: top-left)
		HasPiano = true,              // Piano module active (Slot 2: top-right)
		HasSoundboard = true,         // Soundboard module active (Slot 3: bottom-left)
		AllowStatic = true,           // Raccoon can cause static on keypad
		FlipSabotage = true,          // Raccoon can flip modules upside down
        CoffeeSpill = true,
        StolenKey = true,
        MicFeedback = true,

        KeypadMinLine = 1000,
		KeypadMaxLine = 9999,
		// ---------- Prompter (Broadcaster timing) ----------
		SegmentTime = 9.5,            // Down from 15.0s in Level 1 (faster prompter)
		PrompterLineRest = 4.0,       // Down from 12.0s in Level 1
		PrompterParagraphRest = 4.0,  // Down from 6.0s in Level 1

		// ---------- Sabotage & Module Disruption Timers ----------
		SabotageInterval = 22,        // Attacks every ~22s (down from 40s in Level 1)
		FaultDrain = 4,               // Quality drops 2 points/sec per fault (up from 1 in Level 1)
		HealthGrace = 6.0,            // Grace period reduced to 6s (down from 10s in Level 1)
		QualityFloor = 85,            // Quality threshold raised to 50% (up from 45% in Level 1)
		SignalFloor = 75,             // Signal threshold
		CalmRecovery = 1,

		KeypadTarget = 4,             // 4 lines to route
		PianoTarget = 3,              // 3 jingles to complete
		SoundboardTarget = 2,         // 2 sound cues to complete

		// ---------- Callers ----------
		CallerGap = 9,                // Callers ring every ~9s (down from 16s in Level 1)
		BuildCallers = (bank, topic) =>
		{
			var list = new List<Caller>();
			for (int i = 0; i < 5; i++) list.Add(bank.Legit(topic));      // 5 callers matching quota topic
			for (int i = 0; i < 2; i++) list.Add(bank.OffTopic(topic));   // 2 off-topic distractions (1-strike trap!)
			list.Add(bank.Raccoon(topic));                                // 2 disguised raccoons with sneaky clues
			list.Add(bank.Raccoon(topic));
			return list.OrderBy(_ => System.Random.Shared.Next()).ToList();
		},

		ClockStartMinute = 0,
		ClockEndMinute = 6 * 60,
		ShiftSeconds = 360,

        MaxSimultaneousModules = 3,
        SimultaneousChance = 0.6
    };
}
