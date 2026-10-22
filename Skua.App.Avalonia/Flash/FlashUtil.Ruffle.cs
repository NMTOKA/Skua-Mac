#if !IS_WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Flash;

// The Ruffle game window (Skua.RuffleHost): skua.swf runs in Ruffle, a Flash emulator, inside a native WKWebView.
// It needs neither Electron, PepperFlash nor Rosetta, so it keeps working on macOS 28, where Electron 8 won't open.
// The call bridge is the same as Skua.FlashHost's; this adds the relay Ruffle needs to reach the game servers.
public partial class FlashUtil
{
    /// <summary>
    /// Whether the game runs in Ruffle instead of Electron + PepperFlash. SKUA_GAME_ENGINE=ruffle|flash, or the same
    /// word in ~/Library/Application Support/VibeSkua/game-engine, chooses; otherwise Ruffle from macOS 28 on.
    /// </summary>
    public static bool UseRuffle { get; } = ResolveUseRuffle();

    private static bool ResolveUseRuffle()
    {
        string? choice = Environment.GetEnvironmentVariable("SKUA_GAME_ENGINE");
        if (string.IsNullOrWhiteSpace(choice))
        {
            try
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSkua", "game-engine");
                if (File.Exists(file))
                    choice = File.ReadAllText(file);
            }
            catch { /* ignored */ }
        }

        return choice?.Trim().ToLowerInvariant() switch
        {
            "ruffle" => true,
            "flash" => false,
            _ => OperatingSystem.IsMacOSVersionAtLeast(28),
        };
    }

    private static string FindRuffleHostDirectory()
    {
        string? env = Environment.GetEnvironmentVariable("SKUA_RUFFLEHOST_DIR");
        if (!string.IsNullOrEmpty(env))
            return env;

        string bundled = Path.Combine(AppContext.BaseDirectory, "RuffleHost");
        if (Directory.Exists(bundled))
            return bundled;

        // Development: walk up from bin/<config>/net10.0 to the repository's Skua.RuffleHost/build (see its build.sh).
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Skua.RuffleHost", "build");
            if (File.Exists(Path.Combine(candidate, "SkuaRuffleHost")))
                return candidate;
            dir = dir.Parent;
        }

        return bundled;
    }

    private Process? StartRuffleHost(int port, string swfPath)
    {
        string hostDir = FindRuffleHostDirectory();
        string exe = Path.Combine(hostDir, "SkuaRuffleHost");
        if (!File.Exists(exe))
        {
            Console.Error.WriteLine($"Ruffle game host not found at {exe}. Run Skua.RuffleHost/build.sh.");
            return null;
        }

        MacSetupCheck.ClearQuarantine(hostDir);

        ProcessStartInfo psi = new(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add($"--port={port}");
        psi.ArgumentList.Add($"--swf={swfPath}");
        psi.ArgumentList.Add($"--dir={hostDir}");
        psi.ArgumentList.Add($"--parent={Environment.ProcessId}");
        if (IsDocked)
            psi.ArgumentList.Add("--docked");
        return LaunchHost(psi);
    }

    /// <summary>The origin of the Ruffle host's page (see RuffleHost.swift).</summary>
    private const string RufflePageOrigin = "skua://game";

    // Ruffle can't open TCP sockets in a web view; it opens ws://127.0.0.1:<port>/sock/<host>/<port> instead (host.html
    // lists them) and this relays the bytes to the game server.
    private static bool IsRelayRequest(HttpListenerContext ctx)
    {
        return ctx.Request.IsWebSocketRequest && ctx.Request.Url?.AbsolutePath.StartsWith("/sock/", StringComparison.Ordinal) == true;
    }

    /// <summary>Game servers the relay connects to: Artix's (sockN.aq.com, euro.aqw.artix.com, ...).</summary>
    private static bool IsGameServer(string host)
    {
        host = host.ToLowerInvariant();
        foreach (string domain in new[] { "aq.com", "artix.com", "aqworlds.com" })
        {
            if (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static async Task RelayToGameServer(HttpListenerContext ctx, CancellationToken token)
    {
        string[] parts = ctx.Request.Url!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Only the game page may use the relay, and only to reach game servers.
        if (parts.Length != 3 || !IsGameServer(parts[1]) || !int.TryParse(parts[2], out int port) || port is < 1 or > 65535
            || ctx.Request.Headers["Origin"] != RufflePageOrigin)
        {
            ctx.Response.StatusCode = 403;
            ctx.Response.Close();
            return;
        }

        string host = parts[1];
        using TcpClient tcp = new() { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(host, port, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[RuffleHost] could not connect to {host}:{port}: {ex.Message}");
            ctx.Response.StatusCode = 502;
            ctx.Response.Close();
            return;
        }

        WebSocket ws;
        try
        {
            ws = (await ctx.AcceptWebSocketAsync(null).ConfigureAwait(false)).WebSocket;
        }
        catch
        {
            return;
        }

        Trace.WriteLine($"[RuffleHost] connected to {host}:{port}");
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        NetworkStream stream = tcp.GetStream();

        Task toServer = Task.Run(async () =>
        {
            byte[] buffer = new byte[16 * 1024];
            while (true)
            {
                WebSocketReceiveResult r = await ws.ReceiveAsync(buffer, cts.Token).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close)
                    return;
                await stream.WriteAsync(buffer.AsMemory(0, r.Count), cts.Token).ConfigureAwait(false);
            }
        });
        Task toGame = Task.Run(async () =>
        {
            byte[] buffer = new byte[64 * 1024];
            while (true)
            {
                int n = await stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
                if (n == 0)
                    return;
                await ws.SendAsync(buffer.AsMemory(0, n), WebSocketMessageType.Binary, true, cts.Token).ConfigureAwait(false);
            }
        });

        try
        {
            await Task.WhenAny(toServer, toGame).ConfigureAwait(false);
        }
        catch { /* ignored */ }
        cts.Cancel();
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
        }
        catch { /* ignored */ }
        ws.Dispose();
        Trace.WriteLine($"[RuffleHost] disconnected from {host}:{port}");
    }
}
#endif
