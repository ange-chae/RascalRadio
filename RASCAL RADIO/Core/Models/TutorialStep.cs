namespace RascalRadio.Core.Models;

public enum TutorialStep { MeetStudio, PracticeCaller, GoLive, KeypadModule, SuspiciousCaller, Done }

public enum TutorialAction { Acknowledged, CallerDecided, PhraseMatched, KeypadSolved, RaccoonRejected }

// All the on-screen tutorial wording lives here so you can edit text without touching logic.
// Every step answers: What is happening? Who acts? What confirms success?
public static class TutorialText
{
    public static string Happening(TutorialStep s) => s switch
    {
        TutorialStep.MeetStudio => "Welcome to Rascal Radio, rookies. I'm your Boss, and I'll talk you through your first shift. Today is practice, so nothing you do will cost us.",
        TutorialStep.PracticeCaller => "Line one is lit up. We've got a practice caller waiting, so let's see how you handle them.",
        TutorialStep.GoLive => "Good. Now we go live. A short phrase is up on the prompter, so keep that mic busy.",
        TutorialStep.KeypadModule => "Trouble on the desk. A Line Override module just popped up, and it needs more than one pair of hands.",
        TutorialStep.SuspiciousCaller => "Another call is coming in, and something sounds off to me. The mic is paused, so take your time.",
        _ => "That's a wrap. Nice first shift, team. I'll be in my office if you need me.",
    };

    public static string Job(TutorialStep s, string role) => (s, role) switch
    {
        (TutorialStep.MeetStudio, "Broadcaster") => "Broadcaster, look over the Topic card, the Caller line and the Live Mic. Hit GOT IT when you're ready.",
        (TutorialStep.MeetStudio, "Operator") => "Operator, check the Topics list and the Studio Desk, then hit GOT IT.",
        (TutorialStep.MeetStudio, "Technician") => "Technician, get to know the Manual and Station Health, then hit GOT IT.",

        (TutorialStep.PracticeCaller, "Operator") => "Operator, read the caller's story out loud. Does it fit the topic? Tell the Broadcaster to ACCEPT or REJECT.",
        (TutorialStep.PracticeCaller, "Broadcaster") => "Broadcaster, listen to the Operator, then press ACCEPT or REJECT.",
        (TutorialStep.PracticeCaller, _) => "Technician, keep an eye on the quota and help the others decide.",

        (TutorialStep.GoLive, "Broadcaster") => "Broadcaster, tap the words in order and follow the arrow. Keep talking, because a new phrase shows up after each one.",
        (TutorialStep.GoLive, _) => "Cheer the Broadcaster on. Nothing bad happens if they miss.",

        (TutorialStep.KeypadModule, "Operator") => "Operator, read the INCOMING LINE number to the Technician. You don't have the rules, so type the code they give you and press ROUTE.",
        (TutorialStep.KeypadModule, "Technician") => "Technician, ask the Operator for the line number. Open the Manual, then Module Pages, and work out the override code with the level shown there. Tell that code to the Operator.",
        (TutorialStep.KeypadModule, "Broadcaster") => "Broadcaster, keep speaking! Match phrase after phrase while the others work the module.",
        (TutorialStep.KeypadModule, _) => "Work together, and keep the Broadcaster talking on air.",

        (TutorialStep.SuspiciousCaller, "Operator") => "Operator, read the story out loud. Can you spot the odd clue? Tell the Broadcaster what to press.",
        (TutorialStep.SuspiciousCaller, "Broadcaster") => "Broadcaster, listen to the Operator. If the caller sounds fake, press REJECT.",
        (TutorialStep.SuspiciousCaller, _) => "Technician, listen for the odd clue and speak up.",

        (TutorialStep.Done, _) => "Relax. All your steps are finished.",
        _ => "Talk with your team.",
    };

    public static string DoneWhen(TutorialStep s) => s switch
    {
        TutorialStep.MeetStudio => "Everyone presses GOT IT.",
        TutorialStep.PracticeCaller => "The quota shows 1/3.",
        TutorialStep.GoLive => "The ON AIR meter is full.",
        TutorialStep.KeypadModule => "The keypad shows SOLVED.",
        TutorialStep.SuspiciousCaller => "The suspicious caller is rejected.",
        _ => "Nothing left to do.",
    };
}