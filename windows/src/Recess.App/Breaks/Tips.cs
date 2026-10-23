namespace Recess.App.Breaks;

/// <summary>Suggestions the Default theme shows when a break has no message of its own.</summary>
internal static class Tips
{
    private static readonly string[] All =
    [
        "Stand up and stretch your arms above your head.",
        "Look at something at least 20 feet away for 20 seconds.",
        "Roll your shoulders backwards ten times, slowly.",
        "Take five slow, deep breaths.",
        "Get a glass of water.",
        "Gently tilt your head toward each shoulder.",
        "Close your eyes and relax your face and jaw.",
        "Walk around the room, or step outside for some fresh air.",
        "Shake out your hands and wrists.",
        "Stretch your fingers wide, then make a gentle fist. Repeat.",
        "Sit back, unclench your shoulders, and let your arms hang loose.",
        "Blink slowly a few times to rewet your eyes.",
        "Stand on your toes, then lower slowly. Repeat ten times.",
        "Twist gently at the waist to each side.",
        "Notice your posture: feet flat, back supported, screen at eye level.",
        "Look out of a window and let your eyes focus far away.",
        "Massage your temples and the back of your neck.",
        "Put your hands behind your head and open up your chest.",
        "Tidy one small thing on your desk.",
        "Smile. You're doing great.",
    ];

    public static string Random() => All[System.Random.Shared.Next(All.Length)];
}
