namespace Recess.App;

/// <summary>App-level commands shared by the tray menu, links and shortcuts.</summary>
internal sealed class AppCommands
{
    public required Action<string?, Guid?> OpenSettings { get; init; }

    public required Action Quit { get; init; }

    public required Action StopSound { get; init; }

    public required Func<bool> IsSoundPlaying { get; init; }
}
