using System;
using System.Collections.Generic;
using System.Linq;

namespace RascalRadio.Core.Models;

/// <summary>
/// Random prompter phrases and decoy words for the Broadcaster. Pure C# (no Godot types).
/// Rules the UI relies on: words inside ONE phrase are unique, and decoys never match a word
/// in the current phrase. Phrases are shuffled so none repeats until the pool is used up.
/// </summary>
public sealed class PhraseBank
{
	private static readonly string[][] Short =
	{
		new[] { "Welcome", "to", "the", "show" },
		new[] { "Stay", "tuned", "for", "more" },
		new[] { "Thanks", "for", "listening", "friends" },
		new[] { "You", "are", "live", "tonight" },
		new[] { "Lines", "are", "open", "now" },
		new[] { "Here", "comes", "your", "song" },
		new[] { "Good", "evening", "to", "everyone" },
		new[] { "Call", "us", "with", "stories" },
		new[] { "Keep", "this", "dial", "locked" },
		new[] { "More", "voices", "after", "the", "break" },
	};

	private static readonly string[][] Long =
	{
		new[] { "Welcome", "back", "listeners", "your", "stories", "are", "on", "air" },
		new[] { "Tonight", "we", "share", "laughter", "and", "heartbreak", "together" },
		new[] { "Our", "lines", "are", "open", "so", "call", "us", "now" },
		new[] { "This", "next", "caller", "has", "quite", "a", "tale" },
		new[] { "Stay", "with", "us", "the", "best", "is", "coming" },
		new[] { "Thank", "you", "for", "making", "this", "station", "home" },
	};

	private static readonly string[] DecoyPool =
	{
		"radio", "raccoon", "tonight", "music", "static", "signal", "banana", "garbage",
		"thunder", "pickle", "volume", "antenna", "midnight", "coffee", "whisper", "ketchup",
		"cassette", "puddle", "trumpet", "mailbox",
	};

	private readonly Random _rng;
	private readonly List<string[]> _bag = new();
	private bool _bagIsLong;
	private string[] _last;

	public PhraseBank(Random rng) { _rng = rng; }

	/// <summary>Next phrase. longForm = the 6-8 word sentences used outside the tutorial.</summary>
	public string[] Next(bool longForm)
	{
		if (_bag.Count == 0 || _bagIsLong != longForm)
		{
			_bag.Clear();
			_bag.AddRange((longForm ? Long : Short).Where(HasUniqueWords));
			_bagIsLong = longForm;
		}

		// Avoid showing the same phrase twice in a row when the bag was just refilled.
		int i;
		int tries = 0;
		do { i = _rng.Next(_bag.Count); tries++; }
		while (_bag.Count > 1 && ReferenceEquals(_bag[i], _last) && tries < 10);

		_last = _bag[i];
		_bag.RemoveAt(i);
		return _last;
	}

	/// <summary>Random wrong words that are not part of the phrase.</summary>
	public string[] Decoys(string[] phrase, int count)
	{
		var banned = new HashSet<string>(phrase, StringComparer.OrdinalIgnoreCase);
		return DecoyPool.Where(w => !banned.Contains(w))
			.OrderBy(_ => _rng.Next())
			.Take(count)
			.ToArray();
	}

	private static bool HasUniqueWords(string[] phrase) =>
		phrase.Select(w => w.ToLowerInvariant()).Distinct().Count() == phrase.Length;
}
