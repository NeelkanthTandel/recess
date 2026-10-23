using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Recess.App.Platform;

/// <summary>
/// Keeps one Recess per Windows user session. A second launch (from the Start menu or a
/// recess:// link) hands its request to the running copy over a named pipe and exits.
/// The pipe only accepts the same user on this computer, and messages are small and plain.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>The request a second launch sends when it has no link: show Settings.</summary>
    public const string Reopen = "reopen";

    private const int MaxMessage = 4096;

    /// <summary>A fixed name the installer checks (Inno Setup's AppMutex) to ask you to quit Recess first.</summary>
    public const string InstallerMutexName = @"Local\RecessAppRunning";

    private readonly Mutex mutex;
    private readonly Mutex installerMutex = new(initiallyOwned: false, InstallerMutexName);
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();

    private SingleInstance(Mutex mutex, string pipeName)
    {
        this.mutex = mutex;
        this.pipeName = pipeName;
    }

    /// <summary>Claims this session for Recess, or returns null if another copy already has it.</summary>
    public static SingleInstance? TryClaim()
    {
        var (mutexName, pipeName) = Names();
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var created);
        if (!created)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, pipeName);
    }

    private static (string Mutex, string Pipe) Names()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        var session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        return ($@"Local\Recess-{sid}", $"Recess-{sid}-{session}");
    }

    /// <summary>Sends a request to the running copy. False if it couldn't be reached.</summary>
    public static bool Send(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Names().Pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            var bytes = Encoding.UTF8.GetBytes(message);
            if (bytes.Length > MaxMessage)
            {
                return false;
            }
            client.Write(bytes);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Starts listening; <paramref name="received"/> is called on a background thread.</summary>
    public void Listen(Action<string> received)
    {
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = CreateServer();
                    await server.WaitForConnectionAsync(stop.Token);
                    var buffer = new byte[MaxMessage + 1];
                    var total = 0;
                    int read;
                    while (total <= MaxMessage && (read = await server.ReadAsync(buffer.AsMemory(total), stop.Token)) > 0)
                    {
                        total += read;
                    }
                    if (total is > 0 and <= MaxMessage)
                    {
                        received(Encoding.UTF8.GetString(buffer, 0, total));
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // A client that went away mid-message; wait for the next one.
                }
            }
        });
    }

    private NamedPipeServerStream CreateServer()
    {
        // Only this user, and never over the network.
        using var identity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl,
            AccessControlType.Deny));
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0,
            security);
    }

    public void Dispose()
    {
        stop.Cancel();
        mutex.ReleaseMutex();
        mutex.Dispose();
        installerMutex.Dispose();
        stop.Dispose();
    }
}
