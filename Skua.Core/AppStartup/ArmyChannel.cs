using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.Core.AppStartup;

/// <summary>
/// Army control off Windows, where every bot is its own copy of the app with no tab host to relay window messages.
/// Each copy listens on a Unix domain socket, ~/Library/Application Support/VibeSkua/army/&lt;pid&gt;.sock, and
/// <see cref="Broadcast"/> sends an action to every copy that is running (this one included). Actions use the
/// Windows WM_SKUA_* numbers, so both platforms share one vocabulary.
/// </summary>
public static class ArmyChannel
{
    public const uint LoginMessage = 0x0400 + 447;
    public const uint LogoutMessage = 0x0400 + 448;
    public const uint JumpMapMessage = 0x0400 + 449;
    public const uint SetOptionMessage = 0x0400 + 450;
    public const uint LoadScriptMessage = 0x0400 + 453;
    public const uint JumpPlayerMessage = 0x0400 + 457;

    /// <summary>SetOptionMessage ids (wParam); lParam is 1 for on, 0 for off.</summary>
    public const int OptionLagKiller = 1, OptionHeadless = 2, OptionHidePlayers = 3, OptionScript = 99;

    private const int TimeoutMs = 3000;

    private static Socket? _listener;
    private static CancellationTokenSource? _stop;
    private static string? _socketPath;

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSkua", "army");

    /// <summary>One action: a WM_SKUA_* number, its wParam/lParam, and optional text (script path, map...).</summary>
    public record Command(uint Msg, int W, int L, string? Data);

    /// <summary>
    /// Starts listening for actions from other copies. <paramref name="handler"/> runs on a background thread and
    /// returns the reply (used by actions that ask a question, such as "logged in?").
    /// </summary>
    public static void Listen(Func<Command, Task<int>> handler)
    {
        if (_listener is not null || OperatingSystem.IsWindows())
            return;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            _socketPath = Path.Combine(Directory, $"{Environment.ProcessId}.sock");
            File.Delete(_socketPath);
            Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            listener.Listen(8);
            _listener = listener;
            _stop = new CancellationTokenSource();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();

            _ = Task.Run(() => AcceptLoop(listener, handler, _stop.Token));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Army channel: could not listen: {ex.Message}");
        }
    }

    public static void Stop()
    {
        try
        {
            // Cancel the pending accept first: disposing a socket with a blocking accept() in progress hangs on macOS.
            _stop?.Cancel();
            _listener?.Dispose();
            _listener = null;
            if (_socketPath is not null)
                File.Delete(_socketPath);
        }
        catch { }
    }

    private static async Task AcceptLoop(Socket listener, Func<Command, Task<int>> handler, CancellationToken stop)
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(stop);
            }
            catch
            {
                return; // Stopped.
            }

            _ = Task.Run(async () =>
            {
                using Socket c = client;
                try
                {
                    using NetworkStream stream = new(c);
                    using StreamReader reader = new(stream, Encoding.UTF8);
                    using CancellationTokenSource cts = new(TimeoutMs);
                    string? line = await reader.ReadLineAsync(cts.Token);
                    Command? command = line is null ? null : JsonSerializer.Deserialize<Command>(line);
                    int reply = command is null ? -1 : await handler(command);
                    byte[] bytes = Encoding.UTF8.GetBytes(reply + "\n");
                    await stream.WriteAsync(bytes);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Army channel: bad request: {ex.Message}");
                }
            });
        }
    }

    /// <summary>Process ids of the running copies, this one included, cleaning up sockets left by copies that quit.</summary>
    public static List<int> Instances()
    {
        List<int> pids = new();
        if (!System.IO.Directory.Exists(Directory))
            return pids;

        foreach (string file in System.IO.Directory.GetFiles(Directory, "*.sock"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int pid))
                continue;
            if (IsAlive(pid))
                pids.Add(pid);
            else
                try { File.Delete(file); } catch { }
        }
        pids.Sort();
        return pids;
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sends one action to one copy and returns its reply, or null if it could not be reached.</summary>
    public static async Task<int?> SendAsync(int pid, Command command)
    {
        try
        {
            using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using CancellationTokenSource cts = new(TimeoutMs);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(Directory, $"{pid}.sock")), cts.Token);
            using NetworkStream stream = new(socket);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command) + "\n"), cts.Token);
            using StreamReader reader = new(stream, Encoding.UTF8);
            string? reply = await reader.ReadLineAsync(cts.Token);
            return int.TryParse(reply, out int value) ? value : null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Army channel: could not reach {pid}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sends an action to every running copy. <paramref name="delayBetween"/> spaces them out (logging in many
    /// accounts at once gets them rejected). Returns how many copies received it.
    /// </summary>
    public static async Task<int> BroadcastAsync(uint msg, int wParam = 0, int lParam = 0, string? data = null, TimeSpan delayBetween = default)
    {
        Command command = new(msg, wParam, lParam, data);
        int reached = 0;
        List<int> pids = Instances();
        for (int i = 0; i < pids.Count; i++)
        {
            if (i > 0 && delayBetween > TimeSpan.Zero)
                await Task.Delay(delayBetween);
            if (await SendAsync(pids[i], command) is not null)
                reached++;
        }
        return reached;
    }

    /// <summary>Asks for a script file, then loads it in every running copy. Null if no file was picked.</summary>
    public static Task<int>? PickAndLoadScriptOnAll()
    {
        string? path = Ioc.Default.GetRequiredService<IFileDialogService>()
            .OpenFile(ClientFileSources.SkuaScriptsDIR, "Skua Scripts (*.cs)|*.cs");
        return string.IsNullOrWhiteSpace(path) ? null : BroadcastAsync(LoadScriptMessage, data: path);
    }

    /// <summary>Fire-and-forget <see cref="BroadcastAsync"/>.</summary>
    public static void Broadcast(uint msg, int wParam = 0, int lParam = 0, string? data = null)
    {
        _ = BroadcastAsync(msg, wParam, lParam, data);
    }
}
