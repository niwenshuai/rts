using AIRTS.Lockstep.Server;

namespace AIRTS.Server;

public static class Program
{
    private const int Port = 7777;
    private const int NetworkFrameRate = 15;
    private const int InputDelayFrames = 2;

    public static async Task<int> Main()
    {
        using var server = new LockstepServer(NetworkFrameRate, InputDelayFrames);
        using var shutdown = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        server.Log += Console.WriteLine;
        server.ClientConnected += playerId => Console.WriteLine("Client joined: " + playerId);
        server.ClientDisconnected += playerId => Console.WriteLine("Client left: " + playerId);
        server.FrameAdvanced += (frame, commandCount) =>
        {
            if (frame % NetworkFrameRate == 0)
            {
                Console.WriteLine("Frame " + frame + ", commands: " + commandCount);
            }
        };

        server.Start(Port);
        Console.WriteLine("AIRTS server is running. Press Ctrl+C to stop.");

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
        }
        catch (OperationCanceledException)
        {
        }

        server.Stop();
        return 0;
    }
}
