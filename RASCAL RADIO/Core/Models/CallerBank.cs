using System;
using System.Collections.Generic;
using System.Linq;

namespace RascalRadio.Core.Models;

/// <summary>
/// Random callers. Pure C# (no Godot types). Callers are shuffled so the same one does not
/// show up twice until the whole pool for that kind has been used.
/// </summary>
public sealed class CallerBank
{
	private static readonly (string Name, string Story)[] Drama =
	{
		("Mia", "My sister and I have not talked in years. Yesterday she finally called me, and we cried for an hour."),
		("Daniel", "I found out my best friend has been secretly paying my rent for a year. I don't know whether to thank him or be furious."),
		("Rosa", "My mother left me a letter before she passed. I have carried it unopened for ten years. Tonight I read it."),
		("Victor", "I forgave my brother on the very day he moved overseas. Now I can't stop wondering if it was too late."),
		("Lena", "I told my fiancé the truth about my past on the night before the wedding. He just sat there. Then he took my hand."),
	};

	private static readonly (string Name, string Story)[] Comedy =
	{
		("Gus", "So I tried to teach my goldfish to fetch. Day three, and I'm the one who got wet."),
		("Priya", "My grandma texted me 'LOL' thinking it meant 'lots of love'. She sent it to my boss after the layoff memo."),
		("Benny", "I wore two different shoes to my own job interview and the CEO said it was 'bold'. I got the job."),
		("Tasha", "My cat learned to open the fridge. Last night I caught her making a sandwich. A whole sandwich."),
		("Moe", "I entered a hot dog eating contest by accident. I thought it was a book club."),
	};

	private static readonly (string Name, string Story)[] Music =
	{
		("Jazzy Jo", "I wrote a song on my ukulele during a blackout. My neighbors say it is the best thing they have heard all year."),
		("Marco", "Can you play something by the band that's been stuck in my head? It goes da-da-DUM, and I only know the drums."),
		("Hana", "I started a choir in my apartment hallway. Eleven neighbors now. The landlord asked to join the tenor section."),
		("Rex", "My band has played the same three chords for ten years and we sold out a stadium. Don't ask me how."),
		("Cleo", "I learned piano at 60. Tonight I play my first recital, and my hands are shaking. Any advice?"),
	};

	private static readonly (string Name, string Story, string Clue)[] Raccoons =
	{
		("Rocky", "Hello! Big fan! I'm calling from... inside your trash can. Please don't look. Crunch crunch. Is this thing on?", "Calling from a trash can"),
		("Bandit", "Yes hello, I'd like to dedicate a song to someone very special. Their sandwich. I mean, my friend. Also I have tiny hands.", "Mentions tiny hands and a stolen sandwich"),
		("Trixie", "I can only call at night, after the neighbors put out their bins. Also why does the phone smell like leftovers? Chirrrr.", "Only calls at night and chirrs"),
		("Dumpster Dan", "Great show! I washed my hands in the fountain before calling. Twice. Everything I own is in the shape of a ring.", "Washes everything and loves shiny rings"),
		("Masked Max", "I'm calling to report a totally normal human problem. Where do humans keep the good snacks? Asking for me. A human.", "Asks where humans keep the snacks"),
	};

	private readonly Random _rng;
	private readonly HashSet<string> _used = new();

	public CallerBank(Random rng) { _rng = rng; }

	private static (string Name, string Story)[] Pool(Genre g) => g switch
	{
		Genre.Drama => Drama,
		Genre.Comedy => Comedy,
		_ => Music,
	};

	/// <summary>A real caller whose story fits the genre.</summary>
	public Caller Legit(Genre genre)
	{
		var pick = PickUnused(Pool(genre), c => c.Name);
		return new Caller(genre) { Name = pick.Name, Story = pick.Story };
	}

	/// <summary>A caller about a different genre than the quota (should be rejected).</summary>
	public Caller OffTopic(Genre quota)
	{
		var others = Enum.GetValues<Genre>().Where(g => g != quota).ToArray();
		return Legit(others[_rng.Next(others.Length)]);
	}

	/// <summary>A raccoon pretending to be a caller. Topic matches, but the clue gives it away.</summary>
	public Caller Raccoon(Genre genre)
	{
		var pick = PickUnused(Raccoons, c => c.Name);
		return new Caller(genre) { Name = pick.Name, Story = pick.Story, Clue = pick.Clue, IsRaccoon = true };
	}

	/// <summary>Weighted random caller for a real round.</summary>
	public Caller Next(Genre quota, double raccoonChance = 0.2, double offTopicChance = 0.25)
	{
		double roll = _rng.NextDouble();
		if (roll < raccoonChance) return Raccoon(quota);
		if (roll < raccoonChance + offTopicChance) return OffTopic(quota);
		return Legit(quota);
	}

	private T PickUnused<T>(T[] pool, Func<T, string> key)
	{
		var fresh = pool.Where(p => !_used.Contains(key(p))).ToList();
		if (fresh.Count == 0)
		{
			foreach (var p in pool) _used.Remove(key(p));
			fresh = pool.ToList();
		}
		var pick = fresh[_rng.Next(fresh.Count)];
		_used.Add(key(pick));
		return pick;
	}
}
