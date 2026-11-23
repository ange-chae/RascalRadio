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
    new[] { "Welcome to the show, folks!", "Stay tuned, don't touch that dial!" },
    new[] { "Thanks for listening, friends!", "The lines are open, so give us a ring!" },
    new[] { "Here comes your favorite tune!", "You're listening to the hottest station in town!" },
    new[] { "Good evening, beautiful people!", "We've got a whole lot of music coming your way!" },
    new[] { "Hey there, night owls!", "You're right where you belong!" },
    new[] { "Keep those radios humming!", "We've got more coming up!" },
    new[] { "Well, butter my biscuits!", "Ain't this a fine night for radio!" },
    new[] { "You're tuned in and turned up!", "Let's keep this party rolling!" },
    new[] { "Don't go anywhere, folks!", "We'll be right back after these messages!" },
    new[] { "How's everybody doing tonight?", "We're sending good vibes through the airwaves!" },
    new[] { "Hey, hey, hey, listeners!", "Your favorite voices are back on the air!" },
    new[] { "It's a beautiful night out there!", "Let's make it a night to remember!" },
    new[] { "Dial it up and turn it loud!", "We've got the good stuff coming!" },
    new[] { "This is your friendly neighborhood DJ!", "Keeping you company, one tune at a time!" },
    new[] { "Alright, alright, alright!", "Let's get this show on the road!" },
};

    private static readonly string[][] LongParagraphs =
  {
    new[] { "Welcome back, listeners, your stories are on air.", "Tonight we're bringing you music, laughter, and a little bit of magic.", "So grab a seat, turn up that radio, and stay awhile!" },

    new[] { "Good evening to everyone out there tonight!", "The stars are shining and the airwaves are buzzing.", "You've got nowhere else to be, so let's make this evening count!" },

    new[] { "You're listening to Rascal Radio, broadcasting straight to your hearts.", "We've got the tunes, you've got the stories, and together we've got a show!", "So don't be shy, pick up that phone and give us a call!" },

    new[] { "Well, would you look at the time!", "We've got another hour of good music and great company ahead.", "Keep that dial right here, because we're just getting started!" },

    new[] { "Hey there, night owls, wherever you may be!", "Whether you're driving down the highway or dancing in your kitchen, we've got you covered.", "This one's for all of you out there listening tonight!" },

    new[] { "Our phone lines are hotter than a summer sidewalk!", "We've got callers waiting and stories worth sharing.", "So hold onto your seats, because you never know who's coming through next!" },

    new[] { "Now, I hope you've all been behaving yourselves tonight!", "Word around town is that the raccoons are causing quite a commotion again.", "But don't you worry, we've got our eyes on the situation!" },

    new[] { "You're tuned to the one and only Rascal Radio!", "Bringing you the latest tunes, the wildest callers, and the finest company this side of the dial.", "Don't touch that dial, we'll be right here!" },

    new[] { "It's almost midnight, but we're nowhere near finished!", "The coffee's hot, the microphone's on, and the stories keep rolling in.", "Let's keep these airwaves alive until the break of dawn!" },

    new[] { "Alright, folks, it's time to turn back the clock and let the good times roll!", "We've got a little something special lined up for you tonight.", "So sit back, relax, and let the music take you away!" },

    new[] { "A big hello to all our regular listeners out there!", "You know who you are, and you know we wouldn't be the same without you.", "Thanks for making Rascal Radio your home away from home!" },

    new[] { "We've got a little sunshine coming through these speakers tonight!", "And if your day hasn't been the greatest, well, we've got just the thing for you.", "Stay with us, friends. The best is yet to come!" },
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