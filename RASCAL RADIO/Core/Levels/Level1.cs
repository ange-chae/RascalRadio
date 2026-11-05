using System.Collections.Generic;
using System.Linq;
using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems;

public static class Level1
{
	private static readonly Genre[] Accepted = { Genre.Drama, Genre.Comedy };

	public static readonly LevelDefinition Definition = new()
	{
		// ---------- Identity ----------
		Id = "morning_broadcast",     // internal name. Must match the key in LevelRegistry.
		Title = "LEVEL 1",            // text on the big title card

		// ---------- Topic and goal ----------
		QuotaGenre = Genre.Drama,     // fallback topic, used only if AcceptedGenres is empty
		AcceptedGenres = Accepted,    // the topics one of which is picked each run (see above)
		QuotaTarget = 4,              // callers the players must accept to win. Higher = longer level.

		// ---------- Modules (the Operator's Studio Desk) ----------
		HasKeypad = true,             // Line Override keypad on or off
		HasPiano = true,              // Piano module on or off
        KeypadMinLine = 10,
        KeypadMaxLine = 99,

        // ---------- Prompter (the Broadcaster's keys) ----------
        SegmentTime = 15.0,           // seconds to finish one line of keys. Lower = harder.
		PrompterLineRest = 12,         // seconds of quiet after each line. Lower = harder.
		PrompterParagraphRest = 6,    // longer breather after a whole paragraph. Lower = harder.

		SabotageInterval = 40,        // average seconds between raccoon sabotages (red piano keys, keypad static).
									  // Lower = harder. 0 = no timed sabotage. (A raccoon that gets accepted still sabotages.)

		KeypadTarget = 3,            // lines the Operator must route. Higher = longer level.
		PianoTarget = 2,             // jingles the Operator must play. Higher = longer level.
		QualityFloor = 45,           // Quality must stay above this %. Higher = harder.
		SignalFloor = 30,            // Signal must stay above this %. 30 = both modules faulty at once.
		FaultDrain = 1,              // Quality lost per second per active fault. Higher = harder.
		CalmRecovery = 3,            // Quality regained per second with no faults. Lower = harder.
		HealthGrace = 10,            // Signal must stay above this %. 30 means both modules can't be faulty at once.
		FlipSabotage = true,         // the raccoon can turn a module upside down

		// ---------- Callers ----------
		CallerGap = 16,                // average seconds of quiet between callers (randomized by about +/-25%). Lower = harder.
		BuildCallers = (bank, topic) =>
		{
			var list = new List<Caller>();
			for (int i = 0; i < 3; i++) list.Add(bank.Legit(topic));      // callers who FIT the topic: accept these. Keep at least QuotaTarget + 1.
			for (int i = 0; i < 2; i++) list.Add(bank.OffTopic(topic));   // callers with a DIFFERENT topic: reject these (each wrong call is a strike)
			list.Add(bank.Raccoon(topic));                                // raccoons: reject these. Accepting one is a strike AND triggers sabotage. Add more for a harder level.
			return list.OrderBy(_ => System.Random.Shared.Next()).ToList();   // shuffle so the order is different every run
		},

        ClockStartMinute = 8 * 60,
        ClockEndMinute = 9 * 60,
        ShiftSeconds = 240,
        // seconds below a floor before the task fails. Lower = harder.
    };
}
