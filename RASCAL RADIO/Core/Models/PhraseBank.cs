using System;
using System.Collections.Generic;
using System.Linq;

namespace RascalRadio.Core.Models;

/// <summary>
/// Random prompter paragraphs for the Broadcaster. Pure C# (no Godot types).
/// A paragraph is a few sentences; each sentence is one floating "segment" on screen.
/// Paragraphs are shuffled so none repeats until the pool is used up.
/// </summary>
public sealed class PhraseBank
{
    // ADD YOUR OWN PARAGRAPHS HERE. One string = one sentence = one floating segment.
    private static readonly string[][] ShortParagraphs =
    {
        new[] { "Welcome to the show.", "Stay tuned for more." },
        new[] { "Thanks for listening friends.", "Lines are open now." },
        new[] { "Here comes your song.", "You are live tonight." },
    };

    private static readonly string[][] LongParagraphs =
    {
        new[] { "Welcome back listeners, your stories are on air.", "Tonight we share laughter and heartbreak together.", "Our lines are open, so call us now." },
        new[] { "This next caller has quite a tale.", "Stay with us, the best is still coming.", "Thank you for making this station home." },
        new[] { "Good evening to everyone out there tonight.", "The raccoons have taken over the garbage cans again.", "Keep this dial locked for more voices after the break." },
        new[] { "You are listening to Rascal Radio.", "Call us with your wildest stories.", "More voices are coming right after the break." },
    };

    private readonly Random _rng;
    private readonly List<string[]> _bag = new();
    private bool _bagIsLong;
    private string[] _last;

    public PhraseBank(Random rng) { _rng = rng; }

    /// <summary>
    /// Next paragraph as segments, each segment already split into words.
    /// shortForm = true for the tutorial.
    /// </summary>
    public string[][] NextParagraph(bool shortForm)
    {
        bool isLong = !shortForm;
        if (_bag.Count == 0 || _bagIsLong != isLong)
        {
            _bag.Clear();
            _bag.AddRange(isLong ? LongParagraphs : ShortParagraphs);
            _bagIsLong = isLong;
        }

        // Avoid showing the same paragraph twice in a row when the bag was just refilled.
        int i;
        int tries = 0;
        do { i = _rng.Next(_bag.Count); tries++; }
        while (_bag.Count > 1 && ReferenceEquals(_bag[i], _last) && tries < 10);

        _last = _bag[i];
        _bag.RemoveAt(i);
        return _last
            .Select(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
    }
}