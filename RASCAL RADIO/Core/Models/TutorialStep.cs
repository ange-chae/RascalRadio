namespace RascalRadio.Core.Models;

public enum TutorialStep { MeetStudio, PracticeCaller, GoLive, KeypadModule, SuspiciousCaller, Done }

public enum TutorialAction { Acknowledged, CallerDecided, PhraseMatched, KeypadSolved, RaccoonRejected }

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
        "The Keypad module is active.",
        "Get the technician to figure it out while the rest of you keep the broadcast running."
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

        (TutorialStep.PracticeCaller, "Operator") => "Operator, read each caller's story aloud to the Broadcaster." ,
        (TutorialStep.PracticeCaller, "Broadcaster") => "Broadcaster, listen to the Operator, then press ACCEPT or REJECT whether it matches the assigned topic",
        (TutorialStep.PracticeCaller, _) => "Technician, keep track of the quota and assist the others when needed.",

        (TutorialStep.GoLive, "Broadcaster") => "Broadcaster, follow the keys on the Prompter using WASD in your keyboard. Press them in the correct order to keep the words moving.",

        (TutorialStep.KeypadModule, "Operator") => "Operator, read the INCOMING LINE number to the Technician. Once they give you the override code, enter it and press ROUTE.",
        (TutorialStep.KeypadModule, "Technician") => "Technician, ask the Operator for the incoming line number. Open the Manual, go to Module Pages, and use the level information to calculate the override code. Give the code to the Operator.",
        (TutorialStep.KeypadModule, "Broadcaster") => "Broadcaster, keep the broadcast going. Continue following the Prompter while the others handle the module.",
        (TutorialStep.KeypadModule, _) => "Coordinate with your team. The module needs to be solved without interrupting the broadcast.",

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
        TutorialStep.SuspiciousCaller => "The suspicious caller is rejected.",
        _ => "Nothing left to do."
    };
}