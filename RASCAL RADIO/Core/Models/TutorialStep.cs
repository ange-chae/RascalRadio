namespace RascalRadio.Core.Models;

public enum TutorialStep { MeetStudio, PracticeCaller, GoLive, KeypadModule, SuspiciousCaller, Done }

public enum TutorialAction { Acknowledged, CallerDecided, PhraseMatched, KeypadSolved, RaccoonRejected }

// All the on-screen tutorial wording lives here so you can edit text without touching logic.
// Every step answers: What is happening? Who acts? What confirms success?
public static class TutorialText
{
    public static string Happening(TutorialStep s) => s switch
    {
        TutorialStep.MeetStudio => "Welcome to your first day at Rascal Radio! Today has no penalties.",
        TutorialStep.PracticeCaller => "📞 A practice caller is on the line.",
        TutorialStep.GoLive => "🎙 You are going live. A short phrase is on the prompter.",
        TutorialStep.KeypadModule => "🔢 A Line Override module appeared on the Studio Desk.",
        TutorialStep.SuspiciousCaller => "📞 Another caller is on the line, and something sounds odd... (the mic pauses)",
        _ => "🎉 Tutorial complete! Great first day.",
    };

    public static string Job(TutorialStep s, string role) => (s, role) switch
    {
        (TutorialStep.MeetStudio, "Broadcaster") => "Look at the Topic card, the Caller line and the Live Mic. Then press GOT IT.",
        (TutorialStep.MeetStudio, "Operator") => "Look at the Topics list and the Studio Desk. Then press GOT IT.",
        (TutorialStep.MeetStudio, "Technician") => "Look at the Manual and Station Health. Then press GOT IT.",

        (TutorialStep.PracticeCaller, "Operator") => "Read the caller's story out loud. Does it fit the topic? Tell the Broadcaster ACCEPT or REJECT.",
        (TutorialStep.PracticeCaller, "Broadcaster") => "Listen to the Operator, then press ACCEPT or REJECT.",
        (TutorialStep.PracticeCaller, _) => "Watch the quota and help the others decide.",

        (TutorialStep.GoLive, "Broadcaster") => "Tap the words in order. Follow the ▶ arrow. Keep talking: a new phrase appears after each one.",
        (TutorialStep.GoLive, _) => "Cheer the Broadcaster on. Nothing bad happens if they miss.",

        (TutorialStep.KeypadModule, "Operator") => "Read the INCOMING LINE number to the Technician. You do not have the rules. Type the code they give you and press ROUTE.",
        (TutorialStep.KeypadModule, "Technician") => "Ask the Operator for the line number. Open Manual > Module Pages, work out the override code with the level shown there, and tell it to the Operator.",
        (TutorialStep.KeypadModule, "Broadcaster") => "Keep speaking! Match phrase after phrase while the others work on the module.",
        (TutorialStep.KeypadModule, _) => "Work together. The Broadcaster keeps talking on air.",

        (TutorialStep.SuspiciousCaller, "Operator") => "Read the story out loud. Can you spot the odd clue? Tell the Broadcaster what to press.",
        (TutorialStep.SuspiciousCaller, "Broadcaster") => "Listen to the Operator. If the caller sounds fake, press REJECT.",
        (TutorialStep.SuspiciousCaller, _) => "Listen for the odd clue and speak up.",

        (TutorialStep.Done, _) => "You can relax. All steps are finished.",
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