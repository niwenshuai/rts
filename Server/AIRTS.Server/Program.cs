using AIRTS.Lockstep.Server;

namespace AIRTS.Server;

public static class Program
{
    private const int Port = 7777;
    private const int NetworkFrameRate = 15;
    private const int InputDelayFrames = 2;
    private const int RequiredPlayers = 2;

    public static async Task<int> Main()
    {
        using var server = new LockstepServer(NetworkFrameRate, InputDelayFrames, RequiredPlayers);
        using var shutdown = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        server.Log += Console.WriteLine;
        server.ClientConnected += playerId => Console.WriteLine("Client joined: " + playerId);
        server.ClientDisconnected += playerId => Console.WriteLine("Client left: " + playerId);

        server.Start(Port);
        Console.WriteLine("AIRTS server is running. Commands: status, start, exit. Press Ctrl+C to stop.");
        _ = RunCommandLoopAsync(server, shutdown);

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

    private static async Task RunCommandLoopAsync(LockstepServer server, CancellationTokenSource shutdown)
    {
        while (!shutdown.IsCancellationRequested)
        {
            string line = await Task.Run(Console.ReadLine);
            if (line == null)
            {
                shutdown.Cancel();
                return;
            }

            string command = line.Trim().ToLowerInvariant();
            if (command.Length == 0)
            {
                continue;
            }

            if (command == "exit" || command == "quit" || command == "q" || command == "stop")
            {
                shutdown.Cancel();
                return;
            }

            if (command == "start" || command == "force" || command == "forcestart" || command == "force-start")
            {
                if (!server.ForceStart())
                {
                    Console.WriteLine("Game is already running.");
                }

                continue;
            }

            if (command == "status")
            {
                PrintStatus(server);
                continue;
            }

            if (command == "help" || command == "?")
            {
                Console.WriteLine("Commands: status, start, exit.");
                continue;
            }

            Console.WriteLine("Unknown command. Type help for commands.");
        }
    }

    private static void PrintStatus(LockstepServer server)
    {
        Console.WriteLine(
            "Status: players " + server.ConnectedPlayerCount + "/" + server.RequiredPlayers +
            ", ready " + server.ReadyPlayerCount + "/" + server.ConnectedPlayerCount +
            ", game " + (server.IsGameStarted ? "running" : "waiting") +
            ", frame " + server.CurrentFrame + ".");
    }
}
