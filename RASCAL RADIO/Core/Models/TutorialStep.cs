namespace RascalRadio.Core.Models;

public enum TutorialStep { MeetStudio, PracticeCaller, GoLive, KeypadModule, PianoModule, SuspiciousCaller, Done }

public enum TutorialAction { Acknowledged, CallerDecided, PhraseMatched, KeypadSolved, PianoPlayed, RaccoonRejected }

// All the on-screen tutorial wording lives here so you can edit text without touching logic.
// Every step answers: What is happening? Who acts? What confirms success?
public static class TutorialText
{
    public static string[] HappeningLines(TutorialStep s) => s switch
    {
        TutorialStep.MeetStudio => new[]
        {
        "Welcome to Rascal Radio. I'm the station manager.",
        "Let's get this orientation over with."
    },
        TutorialStep.PracticeCaller => new[]
        {
        "We've got two callers waiting.",
    },
        TutorialStep.GoLive => new[]
        {
        "The prompter should be running.",

    },
        TutorialStep.KeypadModule => new[]
        {
        "We've got a problem with one of the incoming lines.",
    },
        TutorialStep.PianoModule => new[]
        {
        "Line's fixed. Good.",
        "Next up is the Studio Piano. We open every segment with a jingle.",
    },
        TutorialStep.SuspiciousCaller => new[]
        {
        "Another caller. Ugh, You know the drill.",
        "Check the caller's story before you make a deci— *KRRRK*",
        "*bzzzt... zzzt... fzzzzt*",
        "...Hello? Can you hear me?",
        "...Hello?"
    },
        _ => new[]
        {
        "Orientation's over. You've got the basics down, more or less.",
        "I'll leave the rest to you. Try to keep the sta— *KRRRK*",
        "*bzzzt... zzzt... click.*",
        "...."
    }
    };

    public static string Happening(TutorialStep s) => string.Join(" ", HappeningLines(s));

    public static string Job(TutorialStep s, string role) => (s, role) switch
    {
        (TutorialStep.MeetStudio, "Broadcaster") => "Broadcaster, take a look at your Task list and Prompter. Familiarize yourself with them, then press GOT IT.",
        (TutorialStep.MeetStudio, "Operator") => "Operator, check the Task list and the Studio Desk. You'll be handling the callers, so make sure you know where everything is. Press GOT IT when you're done.",
        (TutorialStep.MeetStudio, "Technician") => "Technician, look through the Manual. You'll be responsible for keeping the equipment running. Press GOT IT when you're ready.",

        (TutorialStep.PracticeCaller, "Operator") => "Operator, read each caller's story aloud to the Broadcaster.",
        (TutorialStep.PracticeCaller, "Broadcaster") => "Broadcaster, listen to the Operator, then press ACCEPT or REJECT whether it matches the assigned topic",
        (TutorialStep.PracticeCaller, _) => "Technician, wait for them to finish.",

        (TutorialStep.GoLive, "Broadcaster") => "Broadcaster, follow the keys on the Prompter using WASD in your keyboard. Press them in the correct order to keep the words moving.",

        (TutorialStep.KeypadModule, "Operator") => "Operator, read the INCOMING LINE number to the Technician. Once they give you the override code, enter it and press ROUTE.",
        (TutorialStep.KeypadModule, "Technician") => "Technician, ask the Operator for the incoming line number. Open the Manual, give the code to the Operator.",
        (TutorialStep.KeypadModule, "Broadcaster") => "Broadcaster, keep the broadcast going. Continue following the Prompter while the others handle the module.",

        (TutorialStep.PianoModule, "Broadcaster") => "Broadcaster, watch the CUE SHEET in the top-left corner. Read each color aloud to the Technician, one at a time, while you keep following the Prompter.",
        (TutorialStep.PianoModule, "Operator") => "Operator, open the Piano module. You only see the jingle's name, so listen to the Technician and press the key they tell you, one note at a time.",
        (TutorialStep.PianoModule, "Technician") => "Technician, open the Piano page in the Manual. When the Broadcaster calls out a color, find its key in the table and tell the Operator.",

        (TutorialStep.SuspiciousCaller, "Operator") => "Operator, read the caller's story aloud and check for anything unusual. If you find something suspicious, tell the Broadcaster what to do.",
        (TutorialStep.SuspiciousCaller, "Broadcaster") => "Broadcaster, listen to the Operator's findings. If the caller doesn't seem legitimate, press REJECT.",
        (TutorialStep.SuspiciousCaller, _) => "Technician, listen to the caller's story as well. See if you can identify anything that doesn't add up.",

        _ => "Coordinate with your team and follow your assigned responsibilities."
    };

    public static string DoneWhen(TutorialStep s) => s switch
    {
        TutorialStep.MeetStudio => "Everyone presses GOT IT.",
        TutorialStep.PracticeCaller => "Both practice callers are accepted (the quota shows 2/3).",
        TutorialStep.GoLive => "The Broadcaster finishes a Prompter line.",
        TutorialStep.KeypadModule => "The keypad shows SOLVED.",
        TutorialStep.PianoModule => "The Operator plays the whole Morning Jingle.",
        TutorialStep.SuspiciousCaller => "The suspicious caller is rejected.",
        _ => "Nothing left to do."
    };
}