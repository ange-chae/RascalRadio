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
        ("Maricel", "Hi. Funny, right? Calling a radio station during my break. Three years, no calls, no texts. Now Ma's in the hospital. We fought about my studies. I miss her rice and egg every night. Ma, I'm sorry. I'll come home this weekend."),
    };

	private static readonly (string Name, string Story)[] Comedy =
	{
		("Gus", "So I tried to teach my goldfish to fetch. Day three, and I'm the one who got wet."),
        ("Priya", "My grandma texted me 'LOL', thinking it meant 'lots of love'. She sent it to my boss after the layoff memo. HR was confused."),
        ("Benny", "I wore two different shoes to my job interview. The CEO called it bold. I got the job! I still don't know if he noticed."),
        ("Tasha", "My cat learned to open the fridge. Last night I caught her making a sandwich. A whole sandwich."),
		("Trisha", "Okay, so I got a VA job! P25k monthly! But plot twist! My boss is Carlo, my bestie's ex! He even asked me to write sweet messages for his girlfriend. Well... I'm the girlfriend! Mika, I swear I didn't know!"),    
	};

	private static readonly (string Name, string Story)[] Music =
	{
           ("Jazzy Jo", "I wrote a song on my ukulele during a blackout. My neighbors say it's the best thing they've heard all year. Finally, some fans!"),
			("Marco", "Can you play something by that band stuck in my head? It goes da-da-DUM! I only know the drums. Wait, is that enough?"),
			("Hana", "I started a choir in my apartment hallway. Eleven neighbors now! Even the landlord wants to join the tenor section. Isn't that lovely?"),
			("Rex", "My band has played the same three chords for ten years and we sold out a stadium. Don't ask me how. Honestly, neither do I."),
			("Cleo", "I learned piano at 60. Tonight I play my first recital, and my hands are shaking. What if I forget everything? Any advice?")
    };

	private static readonly (string Name, string Story, string Clue)[] Raccoons =
	{
		("Rocky", "Hello! Big fan! I'm calling from... inside your trash can. Please don't look. Crunch crunch. Is this thing on?", "Calling from a trash can"),
		("Bandit", "Yes hello, I'd like to dedicate a song to someone very special. Their sandwich. I mean, my friend. Also I have tiny hands.", "Mentions tiny hands and a stolen sandwich"),
		("Trixie", "I can only call at night, after the neighbors put out their bins. Also why does the phone smell like leftovers? Chirrrr.", "Only calls at night and chirrs"),
		("Dumpster Dan", "Great show! I washed my hands in the fountain before calling. Twice. Everything I own is in the shape of a ring.", "Washes everything and loves shiny rings"),
		("Masked Max", "I'm calling to report a totally normal human problem. Where do humans keep the good snacks? Asking for me. A human.", "Asks where humans keep the snacks"),
        ("Mr. Jackpot", "Congratulations! You've won P500,000! I know you never joined a raffle, but that's the magic! Just send P1,500 first. Hurry, I'm excited!", "Claims a prize was won without entering and asks for an upfront fee"),
		("Agent Cash", "Hello, valued customer! Your bank account is in danger! Please give me your verification code immediately. Don't worry, I'm definitely not a stranger!", "Pretends to be from a bank and requests a verification code"),
		("Cousin Carlo", "Tita! It's me, Carlo! New number! I'm in trouble and need P10,000. Please don't tell Mama! Wait, you do remember me, right?", "Pretends to be a relative and urgently asks for money"),
		("Richie Rich", "Want to become rich overnight? My investment guarantees double your money in three days! Zero risks! I haven't lost a single peso. Well, not counting yesterday.", "Promises guaranteed profits with no risk"),
		("Officer Bravo", "This is Officer Bravo! You're under investigation! Send payment immediately or we'll arrest you! Yes, over the phone! Very official!", "Pretends to be a police officer and demands payment"),
		("Seller Sally", "Hi, dear! Brand-new phone, only P3,000! Such a steal! Pay first and I'll ship it tomorrow. Pinky promise! No refunds, though!", "Demands full payment before delivering an item"),
		("Sweetheart Sam", "Hello, my love! I miss you! My flight got delayed because of some paperwork. Can you send P15,000? I'll marry you when I arrive! Promise!", "Pretends to be a romantic partner and asks for money"),
		("Tech Tony", "Hello! Your computer has a terrible virus! I can fix it remotely. Just give me access to your device. What could possibly go wrong?", "Claims to detect a virus and requests remote access"),
		("Charity Cherry", "Hello, kind soul! We're saving hundreds of children! Please donate P500 to my personal account. Our official account is... taking a vacation!", "Requests donations through an unverified personal account"),
		("Delivery Dave", "Good day! Your mystery package is here! Just pay P850 for customs. You didn't order anything? Surprise! Everyone loves surprises!", "Claims an unexpected package requires an advance payment")
    };

    private static readonly (string Name, string Story)[] Romance =
  {
    ("Kristine", "Hi hi hi! I saw my ex at Jollibee yesterday! Five years together, then he moved to Dubai. He STILL has my keychain! Wait, do you think that's a sign? Should I message him? Oh my gosh, I'm overthinking again!"),
    ("Andrea", "Hello? Is this a radio station? Anyway, my boyfriend forgot our anniversary AGAIN! But he remembered his basketball schedule. I swear, sometimes I think I'm dating the entire basketball team."),
    ("Miguel", "Uh, hi. I wrote a love letter for someone. Practiced it twenty times, too. But when I finally saw her, I just said, 'Nice weather.' NICE WEATHER. Please tell me there's still hope."),
    ("Sofia", "Okay, so I've been dating this guy for six months. He introduced me to his parents yesterday! Well... technically, he introduced me as his friend. Should I be happy or concerned?"),
    ("Paolo", "Good evening. Can you play a love song? It's for my best friend. She doesn't know I like her. Actually, she's standing right beside me. WAIT, DON'T SAY MY NAME!")
};

    private static readonly (string Name, string Story)[] Horror =
    {
    ("Roberto", "Boss, I don't believe in ghosts. Twenty years as a guard! But at this school, someone reads a roll call every night at 2 a.m. Nobody's there. Last night, I heard, 'Santos. Roberto.' I might skip tonight's round."),
    ("Nina", "Hello? Is this the radio? Sorry, I'm whispering. My baby monitor keeps playing lullabies. I don't have a baby. I live alone. Actually... something just hummed along."),
    ("Felix", "I bought an old mirror yesterday. Cheap! Very cheap! But now my reflection keeps smiling when I'm not. Anyway, do you think I can still return it?"),
    ("Carla", "Good evening. My grandmother says there's someone living in our attic. I laughed at her. Last night, something knocked three times. I haven't laughed since."),
    ("Oscar", "Listen, I drive a taxi at night. Yesterday, a passenger asked to go to the cemetery. When I checked the back seat, he was gone. And he never paid! That's the scary part!")
};

    private static readonly (string Name, string Story)[] Heartwarming =
    {
    ("Lourdes", "Hello, Radio. I'm a nurse. I used to bring pandesal to an elderly patient, Tatay Berting, who had no visitors. Yesterday, his son came home from Canada. They cried together. Tatay said I became his family. Oh, that's embarrassing!"),
    ("Ben", "Hello! My daughter bought me a birthday cake using her allowance. It was crooked, the frosting was everywhere, and it was beautiful. I think I'll keep the box forever."),
    ("Maya", "Hi! Someone paid for my groceries yesterday when I forgot my wallet. I didn't even get to thank her! I hope she's listening. Thank you, stranger!"),
    ("Ramon", "Good evening. My students surprised me with handmade cards on my last day teaching. I pretended not to cry. Didn't work. I'm a terrible actor."),
    ("Ella", "Hello, Radio! My little brother says he wants to be like me when he grows up. I used to think he found me annoying. Apparently, he's been watching everything I do.")
};

    private static readonly (string Name, string Story)[] Thriller =
    {
    ("Jerome", "First name only. Jerome, please. I'm a tricycle driver. A passenger left a bag full of cash and IDs. Now a black SUV keeps following me! It's outside my house, Sir! I did nothing wrong. Should I call the police?"),
    ("Diana", "Hello? Can you keep me on the line? Someone keeps leaving photographs of me outside my apartment. Today's picture was taken yesterday. I don't know who's watching me."),
    ("Arthur", "I got a call from my own number yesterday. My OWN number! Someone told me not to take the highway tomorrow. I don't even know how that's possible."),
    ("Grace", "My office says I was fired. My manager says he never sent the message. Someone changed my access codes, too. I think somebody's inside our system."),
    ("Leo", "Sir, I found a phone in my car. It keeps receiving messages asking where I am. I tried turning it off. It rang again. I'm starting to regret picking it up.")
};

    private static readonly (string Name, string Story)[] Mystery =
    {
    ("Ernesto", "Good evening. Ernesto, from Nueva Ecija. Yesterday, I found an old box while plowing. Inside were photographs and letters. One mentioned my grandfather, Isidro. He never told us about this woman. If anyone recognizes her, please call."),
    ("Bea", "Hello! I found flowers outside my door again. Every Friday! No name, no card. Honestly, I'm flattered, but I'm also starting to suspect my neighbors."),
    ("Victor", "I found an old photograph of my father from 1952. There's a man beside him who looks exactly like me. I showed it to him. He immediately locked it away."),
    ("Liza", "Our town clock stopped at exactly 3:17 twenty years ago. Yesterday, it started ticking again! Everyone's excited. I'm just wondering who fixed it."),
    ("Noel", "I inherited an old house from an uncle I barely knew. Found a diary inside. It describes things that haven't happened yet. Tomorrow's entry has my name.")
};

    private static readonly (string Name, string Story)[] Tragedy =
    {
    ("Joshua", "Hi... I'm Joshua. My brother died two months ago. We fought that morning. I called him worthless. He texted me afterward, but I never read it. I'm scared of what it says. I just don't want to read it alone."),
    ("Marlon", "My wife and I saved for ten years to open a bakery. She passed away before opening day. I still make her favorite bread every morning. I guess some habits never leave."),
    ("Anne", "My best friend moved abroad. We promised to call every Sunday. I missed her last call because I was busy. Now I keep checking my phone, hoping she'll call again."),
    ("Daniel", "My father never attended my basketball games. I thought he didn't care. After he passed, I found every ticket he had secretly kept. Every single one."),
    ("Cathy", "My daughter left for college yesterday. Finally, some peace and quiet! ...Sorry. I didn't mean that. I miss her already. The house is too quiet.")
};

    private static readonly (string Name, string Story)[] Revenge =
    {
    ("Rhea", "Good evening. I worked at a company for ten years, then got fired for embezzling P900,000. The signature wasn't mine. I collected the evidence they ignored. Tomorrow, the board meets. I don't need sympathy. I need the truth."),
    ("Victor", "My business partner took everything! The money, the credit, even my office chair! Anyway, I kept the contracts. Let's see who's laughing tomorrow."),
    ("Monica", "My sister sold our grandmother's house without telling me. She thought I'd never find the documents. Oh, I found them. And I've been waiting for this conversation."),
    ("Rafael", "My old boss ruined my career just to save his own reputation. Three years later, he asked me for a recommendation. I laughed. Then I remembered I was on a professional call."),
    ("Irene", "My classmates blamed me for their failed project. I did all the work, too! Now they're asking for my notes. Absolutely not. Let them struggle for once.")
};

    private static readonly (string Name, string Story)[] Supernatural =
    {
    ("Alfredo", "Oh, son! My jeepney, Rosing, refuses to start every Monday! Ahahaha! But seriously, I drove in Baguio for thirty years. A woman in white always rides my jeepney at dawn. Yesterday, I learned she died on that road in 1998. Maybe she needs a ride home."),
    ("Dora", "Hello, dear! My grandmother leaves food by the window every night for my grandfather. He's been gone fifteen years! Yesterday, the plate was empty. I told her the ghost must be hungry."),
    ("Emilio", "I bought an old radio at a garage sale. Cheap! Beautiful! But every midnight, it plays my father's favorite song. It's not even plugged in. Still, I refuse to throw away a good bargain."),
    ("Carmen", "My little boy keeps talking to someone named Lola Rosa. We don't know anyone by that name. Yesterday, he pointed to my grandmother's old photograph. Oh, dear Lord."),
    ("Tony", "There's a stray dog outside my house every evening. Looks exactly like my old dog, Bruno. He passed away twenty years ago. Anyway, I still leave food outside. Just in case.")
};
    private readonly Random _rng;
	private readonly HashSet<string> _used = new();

	public CallerBank(Random rng) { _rng = rng; }

    private static (string Name, string Story)[] Pool(Genre g) => g switch
    {
        Genre.Drama => Drama,
        Genre.Comedy => Comedy,
        Genre.Music => Music,
        Genre.Romance => Romance,
        Genre.Horror => Horror,
        Genre.Heartwarming => Heartwarming,
        Genre.Thriller => Thriller,
        Genre.Mystery => Mystery,
        Genre.Tragedy => Tragedy,
        Genre.Revenge => Revenge,
        Genre.Supernatural => Supernatural,
        _ => Drama,
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
