#if !IS_WINDOWS
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Utils;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Skua.App.Avalonia.Flash;

/// <summary>
/// macOS/Linux <see cref="IFlashUtil"/>. The game runs in Skua.FlashHost (Electron 8 + PepperFlash) in its own
/// window; calls and ExternalInterface events travel as JSON over a WebSocket on 127.0.0.1.
/// Return values and event arguments follow the same rules as the Windows ActiveX implementation.
/// </summary>
public class FlashUtil : IFlashUtil
{
    public FlashUtil(IMessenger messenger, Lazy<IScriptManager> manager)
    {
        _messenger = messenger;
        _lazyManager = manager;
    }

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    private readonly IMessenger _messenger;
    private readonly Lazy<IScriptManager> _lazyManager;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JObject>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private HttpListener? _listener;
    private WebSocket? _socket;
    private Process? _host;
    private CancellationTokenSource? _cts;
    private int _nextId;

    public event FlashCallHandler? FlashCall;

    /// <summary>Raised when the game window process exits on its own (closed by the user or crashed).</summary>
    public event Action? HostExited;

    /// <summary>FlashCall name the host uses to forward key presses from the game window.</summary>
    public const string HostKeyEvent = "__key";

    private bool _cleaningUp;

    // The Windows build caches the SWF bytes in RAM; the host loads it from disk here.
    public static void PreloadSwf() { }

    public void InitializeFlash()
    {
        string swfPath = Path.Combine(AppContext.BaseDirectory, "skua.swf");
        if (!File.Exists(swfPath))
        {
            Console.Error.WriteLine($"Missing skua.swf at {swfPath}. Build the AS3 client first.");
            Environment.Exit(0);
            return;
        }

        CleanupOldHost();

        _cts = new CancellationTokenSource();
        int port = GetFreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        // Run the socket loops on the thread pool. Started from the UI thread, their awaits would resume
        // there, and a UI-thread Call() blocking on a reply would deadlock until its timeout.
        HttpListener listener = _listener;
        CancellationToken token = _cts.Token;
        Task.Run(() => AcceptLoop(listener, token));

        _host = StartHost(port, swfPath);
    }

    private static int GetFreePort()
    {
        TcpListener l = new(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string FindHostDirectory()
    {
        string? env = Environment.GetEnvironmentVariable("SKUA_FLASHHOST_DIR");
        if (!string.IsNullOrEmpty(env))
            return env;

        string bundled = Path.Combine(AppContext.BaseDirectory, "FlashHost");
        if (Directory.Exists(bundled))
            return bundled;

        // Development: walk up from bin/<config>/net10.0 to the repository's Skua.FlashHost.
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Skua.FlashHost");
            if (File.Exists(Path.Combine(candidate, "main.js")))
                return candidate;
            dir = dir.Parent;
        }

        return bundled;
    }

    private Process? StartHost(int port, string swfPath)
    {
        string hostDir = FindHostDirectory();
        string electron = Environment.GetEnvironmentVariable("SKUA_ELECTRON")
            ?? Path.Combine(hostDir, "node_modules", "electron", "dist", "Electron.app", "Contents", "MacOS", "Electron");

        if (!File.Exists(electron))
        {
            Console.Error.WriteLine($"Flash host not found at {electron}. Run 'npm_config_arch=x64 npm install' in Skua.FlashHost.");
            return null;
        }

        if (!MacSetupCheck.EnsureReady(hostDir))
            return null;

        ProcessStartInfo psi = new(electron)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(hostDir);
        psi.ArgumentList.Add($"--port={port}");
        psi.ArgumentList.Add($"--swf={swfPath}");
        if (IsDocked)
            psi.ArgumentList.Add("--docked");
        string? ppapi = Environment.GetEnvironmentVariable("SKUA_PPAPI");
        if (!string.IsNullOrEmpty(ppapi))
            psi.ArgumentList.Add($"--ppapi={ppapi}");

        Process p = new() { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) Trace.WriteLine($"[FlashHost] {e.Data}"); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Trace.WriteLine($"[FlashHost] {e.Data}"); };
        p.Exited += (_, _) =>
        {
            Trace.WriteLine($"[FlashHost] exited with code {SafeExitCode(p)}");
            if (_cleaningUp || !ReferenceEquals(p, _host))
                return;

            _socket = null;
            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();
            _pending.Clear();
            HostExited?.Invoke();
        };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }

    private async Task AcceptLoop(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (!ctx.Request.IsWebSocketRequest || _socket is { State: WebSocketState.Open })
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
                continue;
            }

            HttpListenerWebSocketContext wsCtx = await ctx.AcceptWebSocketAsync(null).ConfigureAwait(false);
            _socket = wsCtx.WebSocket;
            _ = Task.Run(() => ReceiveLoop(wsCtx.WebSocket, token));
        }
    }

    private async Task ReceiveLoop(WebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[64 * 1024];
        using MemoryStream message = new();
        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;

                string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                HandleMessage(text);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
        finally
        {
            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();
            _pending.Clear();
        }
    }

    private void HandleMessage(string text)
    {
        JObject msg;
        try
        {
            msg = JObject.Parse(text);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"FlashHost sent invalid JSON: {ex.Message}");
            return;
        }

        if (msg.TryGetValue("ev", out JToken? ev))
        {
            string function = ev.ToString();
            object[] args = { msg.Value<string>("a") ?? string.Empty };
            // The ActiveX FlashCall event fires on the UI thread; keep that for handlers.
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    FlashCall?.Invoke(function, args);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Flash CallHandler exception: {ex.Message}");
                }
            });
            return;
        }

        int id = msg.Value<int?>("id") ?? -1;
        if (_pending.TryRemove(id, out var pending))
            pending.TrySetResult(msg);
    }

    public string Call(string function, params object[] args)
    {
        return Call<string>(function, args);
    }

    public T Call<T>(string function, params object[] args)
    {
        try
        {
            object? o = Call(function, typeof(T), args);
            return o is T value ? value : GetDefaultValue<T>();
        }
        catch
        {
            return GetDefaultValue<T>();
        }
    }

    private static T GetDefaultValue<T>()
    {
        object? defaultValue = DefaultProvider.GetDefault<T>(typeof(T));
        return defaultValue is T typed ? typed : default!;
    }

    // SKUA_BRIDGE_STATS=1 prints calls per second by function to stderr every 10 s (performance diagnostics).
    private static readonly ConcurrentDictionary<string, int>? _callStats =
        Environment.GetEnvironmentVariable("SKUA_BRIDGE_STATS") == "1" ? StartStats() : null;

    private static ConcurrentDictionary<string, int> StartStats()
    {
        ConcurrentDictionary<string, int> stats = new();
        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(10_000);
                var snapshot = stats.ToArray();
                stats.Clear();
                int total = snapshot.Sum(kv => kv.Value);
                Console.Error.WriteLine($"[bridge] {total / 10.0:F1} calls/s: " +
                    string.Join(", ", snapshot.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key}={kv.Value / 10.0:F1}")));
            }
        }) { IsBackground = true, Name = "Bridge stats" }.Start();
        return stats;
    }

    public object Call(string function, Type type, params object[] args)
    {
        if (_callStats is not null)
            _callStats.AddOrUpdate(function is "getGameObject" or "isNull" && args.Length > 0 ? $"{function}({args[0]})" : function, 1, (_, n) => n + 1);

        if (_lazyManager.Value.ShouldExit && Thread.CurrentThread.Name == "Script Thread")
            _lazyManager.Value.ScriptCts?.Token.ThrowIfCancellationRequested();

        WebSocket? socket = _socket;
        if (socket is not { State: WebSocketState.Open })
            return default!;

        int id = Interlocked.Increment(ref _nextId);
        // The caller blocks on this; no async continuations, so complete inline (no thread-pool hop per call).
        TaskCompletionSource<JObject> tcs = new();
        _pending[id] = tcs;

        try
        {
            string request = JsonConvert.SerializeObject(new { id, fn = function, args });
            Send(socket, request);

            if (!tcs.Task.Wait(CallTimeout))
                throw new TimeoutException($"Flash call '{function}' timed out.");

            JObject reply = tcs.Task.Result;
            if (reply.Value<bool?>("ok") != true)
                throw new InvalidOperationException(reply.Value<string>("err") ?? "Flash call failed.");

            string? text = ToWindowsReturnText(reply["r"]);
            if (text is null)
                return default!;

            object? value = Convert.ChangeType(text, type);
            return value ?? default!;
        }
        catch (Exception e)
        {
            if (function != "isKicked")
                _messenger.Send<FlashErrorMessage>(new(e, function, args));
            return default!;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Mirrors <c>XElement.Parse(result).FirstNode</c> on Windows: strings and numbers come back as text;
    /// booleans, null and undefined are empty elements and yield the default value.
    /// </summary>
    private static string? ToWindowsReturnText(JToken? token)
    {
        return token?.Type switch
        {
            JTokenType.String => token.Value<string>(),
            JTokenType.Integer => token.Value<long>().ToString(CultureInfo.InvariantCulture),
            JTokenType.Float => token.Value<double>().ToString(CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private void Send(WebSocket socket, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        _sendLock.Wait();
        try
        {
            socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public object FromFlashXml(XElement el)
    {
        switch (el.Name.ToString())
        {
            case "number":
                return int.TryParse(el.Value, out int i) ? i : float.TryParse(el.Value, out float f) ? f : 0;

            case "true":
                return true;

            case "false":
                return false;

            case "null":
                return null!;

            case "array":
                return el.Elements().Select(e => FromFlashXml(e)).ToArray();

            case "object":
                dynamic d = new ExpandoObject();
                el.Elements().ForEach(e => d[e.Attribute("id")!.Value] = FromFlashXml(e.Elements().First()));
                return d;

            default:
                return el.Value;
        }
    }

    public IFlashObject<T> CreateFlashObject<T>(string path)
    {
        return new FlashObject<T>(Call<int>("lnkCreate", path), this);
    }

    /// <summary>Shows or hides the game window (headless mode). The game keeps running while hidden.</summary>
    public void SetHostVisible(bool visible)
    {
        Task.Run(() => Call(visible ? "__show" : "__hide"));
    }

    /// <summary>
    /// Whether the game window is frameless and kept under the client's toolbar.
    /// Set SKUA_UNDOCKED=1 to get a normal, freely movable game window instead.
    /// </summary>
    public static bool IsDocked { get; } = Environment.GetEnvironmentVariable("SKUA_UNDOCKED") != "1";

    private (double X, double Y, double Width, double Height)? _pendingDock;
    private int _dockSending;

    /// <summary>
    /// Moves the game window to the given screen rectangle (points, top-left origin). Rapid calls while the
    /// client window is dragged are coalesced so only the latest position is sent.
    /// </summary>
    public void DockHost(double x, double y, double width, double height)
    {
        _pendingDock = (x, y, width, height);
        if (Interlocked.Exchange(ref _dockSending, 1) == 1)
            return;

        Task.Run(() =>
        {
            try
            {
                while (_pendingDock is { } next)
                {
                    _pendingDock = null;
                    Call("__dock", next.X, next.Y, next.Width, next.Height);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _dockSending, 0);
                if (_pendingDock is { } late)
                    DockHost(late.X, late.Y, late.Width, late.Height);
            }
        });
    }

    /// <summary>Brings the game window above other apps' windows without taking focus.</summary>
    public void RaiseHost()
    {
        Task.Run(() => Call("__raise"));
    }

    /// <summary>Shows the game window and brings it to the front.</summary>
    public void FocusHost()
    {
        Task.Run(() => Call("__focus"));
    }

    public void SetHostTitle(string title)
    {
        Task.Run(() => Call("__setTitle", title));
    }

    /// <summary>PNG bytes of the game window, or empty if the host is not running.</summary>
    public Task<byte[]> CaptureHostAsync()
    {
        return Task.Run(() =>
        {
            string? base64 = Call("__screenshot");
            if (string.IsNullOrEmpty(base64))
                return Array.Empty<byte>();
            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return Array.Empty<byte>();
            }
        });
    }

    private void CleanupOldHost()
    {
        _cleaningUp = true;
        try
        {
            CleanupOldHostCore();
        }
        finally
        {
            _cleaningUp = false;
        }
    }

    private void CleanupOldHostCore()
    {
        _cts?.Cancel();
        _cts = null;

        try { _socket?.Abort(); } catch { /* ignored */ }
        _socket = null;

        try { _listener?.Stop(); } catch { /* ignored */ }
        _listener = null;

        try
        {
            if (_host is { HasExited: false })
                _host.Kill(entireProcessTree: true);
        }
        catch { /* ignored */ }
        _host?.Dispose();
        _host = null;
    }

    public void Dispose()
    {
        try
        {
            CleanupOldHost();
        }
        catch { /* ignored */ }
    }
}
#endif
