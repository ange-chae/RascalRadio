using System.Collections.Generic;
using System.Linq;
using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems;

public static class Level2
{
	private static readonly Genre[] Accepted = { Genre.Drama, Genre.Comedy };

	public static readonly LevelDefinition Definition = new()
	{
		Id = "afternoon_broadcast",
		Title = "LEVEL 2",

		QuotaGenre = Genre.Drama,
		AcceptedGenres = Accepted,
		QuotaTarget = 4,

		HasKeypad = false,
		HasPiano = true,
		HasSoundboard = true,

		SegmentTime = 11.0,
		PrompterLineRest = 10,
		PrompterParagraphRest = 5,

		SabotageInterval = 35,

		PianoTarget = 3,
		SoundboardTarget = 2,
		QualityFloor = 50,
		SignalFloor = 30,
		FaultDrain = 1,
		CalmRecovery = 2,
		HealthGrace = 10,
		FlipSabotage = true,

		CallerGap = 14,
		BuildCallers = (bank, topic) =>
		{
			var list = new List<Caller>();
			for (int i = 0; i < 5; i++) list.Add(bank.Legit(topic));
			for (int i = 0; i < 2; i++) list.Add(bank.OffTopic(topic));
			list.Add(bank.Raccoon(topic));
			return list.OrderBy(_ => System.Random.Shared.Next()).ToList();
		},
	};
}
