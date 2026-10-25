// Skua Mac game host using Ruffle (Flash emulator, WebAssembly) in a native WKWebView. Runs natively on
// Apple Silicon: no Electron, PepperFlash or Rosetta. Selected by FlashUtil.Unix.cs instead of Skua.FlashHost.
//
// Launched with:
//   --port=<n>       WebSocket port the .NET client listens on (127.0.0.1): the call bridge and the game-server relay
//   --swf=<path>     absolute path to skua.swf
//   --dir=<path>     folder holding host.html and ruffle/ (defaults to this executable's folder)
//   --title=<text>   optional window title
//   --docked         frameless window positioned by the client under its toolbar (no Dock icon)
//   --parent=<pid>   exit when this process is gone
//
// Pages load from skua://game/, a fixed origin, so the game's saved settings (Ruffle keeps Flash shared objects in
// localStorage) survive restarts. skua://game/proxy/<host>/<path> fetches https://<host>/<path> for the game, which
// game.aq.com would otherwise refuse (no CORS headers).

import AppKit
import WebKit

struct Options {
    var port = 0
    var swf = ""
    var dir = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath().deletingLastPathComponent().path
    var title = "Skua Mac - Game"
    var docked = false
    var parent: pid_t = 0

    init() {
        for a in CommandLine.arguments.dropFirst() {
            func value(_ name: String) -> String? { a.hasPrefix("--\(name)=") ? String(a.dropFirst(name.count + 3)) : nil }
            if let v = value("port") { port = Int(v) ?? 0 }
            if let v = value("swf") { swf = v }
            if let v = value("dir") { dir = v }
            if let v = value("title") { title = v }
            if let v = value("parent") { parent = pid_t(v) ?? 0 }
            if a == "--docked" { docked = true }
        }
    }
}

/// Hosts the game may be fetched from through skua://game/proxy/.
func isGameHost(_ host: String) -> Bool {
    let h = host.lowercased()
    return ["aq.com", "artix.com", "aqworlds.com"].contains { h == $0 || h.hasSuffix("." + $0) }
}

final class SchemeHandler: NSObject, WKURLSchemeHandler {
    let opts: Options
    private var active = Set<ObjectIdentifier>()
    private var upstream = [ObjectIdentifier: URLSessionDataTask]()
    private let session: URLSession

    init(_ opts: Options) {
        self.opts = opts
        let cfg = URLSessionConfiguration.default
        cfg.httpCookieStorage = HTTPCookieStorage.shared
        // Game files are versioned by query string; keep them on disk like a browser would.
        let caches = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
        cfg.urlCache = URLCache(memoryCapacity: 64 << 20, diskCapacity: 1 << 30,
                                directory: caches.appendingPathComponent("SkuaRuffleHost"))
        session = URLSession(configuration: cfg)
    }

    private static let types = ["html": "text/html", "js": "text/javascript", "wasm": "application/wasm",
                                "swf": "application/x-shockwave-flash", "map": "application/json"]

    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        let key = ObjectIdentifier(task)
        active.insert(key)
        guard let url = task.request.url else { return fail(task, 400) }
        let path = url.path
        let dir = URL(fileURLWithPath: opts.dir)

        if path == "/" || path == "/host.html" { return serveFile(task, dir.appendingPathComponent("host.html")) }
        if path == "/skua.swf" { return serveFile(task, URL(fileURLWithPath: opts.swf)) }
        if path.hasPrefix("/ruffle/") {
            return serveFile(task, dir.appendingPathComponent("ruffle").appendingPathComponent(url.lastPathComponent))
        }
        if path.hasPrefix("/proxy/") { return proxy(task, url) }
        fail(task, 404)
    }

    func webView(_ webView: WKWebView, stop task: WKURLSchemeTask) {
        let key = ObjectIdentifier(task)
        active.remove(key)
        upstream.removeValue(forKey: key)?.cancel()
    }

    private func respond(_ task: WKURLSchemeTask, _ status: Int, _ headers: [String: String], _ body: Data) {
        guard active.remove(ObjectIdentifier(task)) != nil, let url = task.request.url else { return }
        task.didReceive(HTTPURLResponse(url: url, statusCode: status, httpVersion: "HTTP/1.1", headerFields: headers)!)
        task.didReceive(body)
        task.didFinish()
    }

    private func fail(_ task: WKURLSchemeTask, _ status: Int) {
        respond(task, status, ["Content-Type": "text/plain"], Data())
    }

    private func serveFile(_ task: WKURLSchemeTask, _ file: URL) {
        guard let data = try? Data(contentsOf: file) else { return fail(task, 404) }
        let type = Self.types[file.pathExtension.lowercased()] ?? "application/octet-stream"
        respond(task, 200, ["Content-Type": type, "Cache-Control": "no-store"], data)
    }

    private func proxy(_ task: WKURLSchemeTask, _ url: URL) {
        // /proxy/<host>/<rest> -> https://<host>/<rest>
        let parts = url.path.dropFirst("/proxy/".count).split(separator: "/", maxSplits: 1, omittingEmptySubsequences: false)
        guard let host = parts.first.map(String.init), isGameHost(host),
              var target = URLComponents(string: "https://\(host)/") else { return fail(task, 403) }
        target.percentEncodedPath = "/" + (parts.count > 1 ? String(parts[1]) : "")
        target.percentEncodedQuery = URLComponents(url: url, resolvingAgainstBaseURL: false)?.percentEncodedQuery
        guard let targetURL = target.url else { return fail(task, 400) }

        var req = URLRequest(url: targetURL)
        req.httpMethod = task.request.httpMethod ?? "GET"
        for (k, v) in task.request.allHTTPHeaderFields ?? [:] {
            if ["host", "origin", "referer", "cookie", "content-length", "accept-encoding", "x-skua-body"].contains(k.lowercased()) { continue }
            req.setValue(v, forHTTPHeaderField: k)
        }
        // What the Flash plugin sent, so the game's web API sees the same requests as from the Electron host.
        req.setValue("https://game.aq.com/game/", forHTTPHeaderField: "Referer")
        req.setValue("ShockwaveFlash/32.0.0.344", forHTTPHeaderField: "X-Requested-With")
        // host.html sends POST bodies in X-Skua-Body: WebKit drops the body of a request to a custom scheme.
        if let encoded = task.request.value(forHTTPHeaderField: "X-Skua-Body") {
            req.httpBody = Data(base64Encoded: encoded)
        } else {
            req.httpBody = task.request.httpBody ?? task.request.httpBodyStream.map(readAll)
        }
        let method = req.httpMethod ?? "GET"
        let sentBytes = req.httpBody?.count ?? 0

        let key = ObjectIdentifier(task)
        let dataTask = session.dataTask(with: req) { [weak self] data, response, error in
            DispatchQueue.main.async {
                guard let self else { return }
                self.upstream.removeValue(forKey: key)
                guard let http = response as? HTTPURLResponse else {
                    if let error { log("proxy \(targetURL.absoluteString): \(error.localizedDescription)") }
                    return self.fail(task, 502)
                }
                // Sizes only: request bodies can hold the login password.
                if method != "GET" || http.statusCode >= 400 {
                    log("proxy \(method) \(host)\(target.percentEncodedPath) sent \(sentBytes) bytes -> \(http.statusCode), \(data?.count ?? 0) bytes")
                }
                var headers = ["Cache-Control": "no-store"]
                if let type = http.value(forHTTPHeaderField: "Content-Type") { headers["Content-Type"] = type }
                self.respond(task, http.statusCode, headers, data ?? Data())
            }
        }
        upstream[key] = dataTask
        dataTask.resume()
    }
}

func readAll(_ stream: InputStream) -> Data {
    var data = Data()
    stream.open()
    defer { stream.close() }
    var buffer = [UInt8](repeating: 0, count: 64 * 1024)
    while true {
        let n = stream.read(&buffer, maxLength: buffer.count)
        if n <= 0 { break }
        data.append(buffer, count: n)
    }
    return data
}

func log(_ text: String) {
    FileHandle.standardOutput.write((text + "\n").data(using: .utf8)!)
}

/// Borderless windows can't take keyboard focus by default; the game needs it.
final class GameWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

final class Host: NSObject, NSApplicationDelegate, NSWindowDelegate, WKUIDelegate, WKScriptMessageHandlerWithReply,
    WKScriptMessageHandler {
    let opts = Options()
    var window: GameWindow!
    var web: WKWebView!
    var schemes: SchemeHandler!
    var hiddenByClient = false
    var activity: NSObjectProtocol?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Bots run with the game window in the background or hidden: no App Nap.
        activity = ProcessInfo.processInfo.beginActivity(options: [.userInitiated, .idleSystemSleepDisabled],
                                                         reason: "Game running")
        NSApp.setActivationPolicy(opts.docked ? .accessory : .regular)

        let cfg = WKWebViewConfiguration()
        schemes = SchemeHandler(opts)
        cfg.setURLSchemeHandler(schemes, forURLScheme: "skua")
        cfg.mediaTypesRequiringUserActionForPlayback = []
        cfg.userContentController.addScriptMessageHandler(self, contentWorld: .page, name: "skua")
        cfg.userContentController.add(self, name: "log")
        // WebKit slows down pages it thinks nobody is looking at. Keep the game running at full speed when the
        // window is covered or hidden (WebKit SPI; skipped if a WebKit version doesn't have it).
        setPrivate(cfg.preferences, "pageVisibilityBasedProcessSuppressionEnabled", false)
        setPrivate(cfg.preferences, "hiddenPageDOMTimerThrottlingEnabled", false)
        setPrivate(cfg.preferences, "hiddenPageDOMTimerThrottlingAutoIncreases", false)
        if ProcessInfo.processInfo.environment["SKUA_RUFFLE_INSPECT"] == "1" {
            setPrivate(cfg.preferences, "developerExtrasEnabled", true)
        }

        let rect = NSRect(x: 0, y: 0, width: 958, height: 550)
        window = GameWindow(contentRect: rect,
                            styleMask: opts.docked ? [.borderless] : [.titled, .closable, .miniaturizable, .resizable],
                            backing: .buffered, defer: false)
        window.title = opts.title
        window.backgroundColor = .black
        window.delegate = self
        window.isReleasedWhenClosed = false
        if !opts.docked { window.center() }

        web = WKWebView(frame: rect, configuration: cfg)
        web.autoresizingMask = [.width, .height]
        web.uiDelegate = self
        setPrivate(web, "windowOcclusionDetectionEnabled", false)
        if #available(macOS 13.3, *), ProcessInfo.processInfo.environment["SKUA_RUFFLE_INSPECT"] == "1" {
            web.isInspectable = true
        }
        window.contentView = web
        web.load(URLRequest(url: URL(string: "skua://game/host.html?port=\(opts.port)")!))
        window.makeKeyAndOrderFront(nil)
        if !opts.docked { NSApp.activate(ignoringOtherApps: true) }

        if opts.parent > 0 {
            Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [parent = opts.parent] _ in
                if kill(parent, 0) != 0 && errno == ESRCH { exit(0) }
            }
        }
    }

    func setPrivate(_ object: NSObject, _ key: String, _ value: Bool) {
        let setter = "_set" + key.prefix(1).uppercased() + key.dropFirst() + ":"
        if object.responds(to: NSSelectorFromString(setter)) { object.setValue(value, forKey: key) }
    }

    func windowWillClose(_ notification: Notification) { exit(0) }

    // Links the game opens (navigateToURL) go to the default browser.
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                 for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if let url = navigationAction.request.url, ["http", "https"].contains(url.scheme?.lowercased() ?? "") {
            NSWorkspace.shared.open(url)
        }
        return nil
    }

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        if let text = message.body as? String { log("[page] \(text)") }
    }

    // Control calls from .NET (see FlashUtil.Unix.cs), routed through host.html.
    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage,
                               replyHandler: @escaping (Any?, String?) -> Void) {
        guard let body = message.body as? [String: Any], let fn = body["fn"] as? String else {
            return replyHandler(nil, "bad message")
        }
        let args = body["args"] as? [Any] ?? []
        func num(_ i: Int) -> CGFloat { CGFloat((args.count > i ? args[i] as? NSNumber : nil)?.doubleValue ?? 0) }

        switch fn {
        case "__show":
            hiddenByClient = false
            window.orderFrontRegardless()
        case "__hide":
            // The page keeps running while hidden (host.html asks Ruffle for background ticks).
            hiddenByClient = true
            window.orderOut(nil)
        case "__dock":
            // x, y, width, height in screen points, top-left origin (of the primary screen).
            let top = NSScreen.screens.first?.frame.maxY ?? 0
            window.setFrame(NSRect(x: num(0), y: top - num(1) - num(3), width: num(2), height: num(3)), display: true)
        case "__raise":
            if !hiddenByClient { window.orderFrontRegardless() }
        case "__focus":
            hiddenByClient = false
            NSApp.activate(ignoringOtherApps: true)
            window.makeKeyAndOrderFront(nil)
        case "__setTitle":
            window.title = args.first.map { "\($0)" } ?? ""
        case "__screenshot":
            web.takeSnapshot(with: nil) { image, error in
                guard let image, let tiff = image.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff),
                      let png = rep.representation(using: .png, properties: [:]) else {
                    return replyHandler(nil, error?.localizedDescription ?? "snapshot failed")
                }
                replyHandler(png.base64EncodedString(), nil)
            }
            return
        case "__exit":
            replyHandler("true", nil)
            DispatchQueue.main.async { exit(0) }
            return
        default:
            return replyHandler(nil, "unknown host call \(fn)")
        }
        replyHandler("true", nil)
    }
}

let app = NSApplication.shared
let host = Host()
app.delegate = host
if Options().port == 0 {
    log("rufflehost: missing --port")
    exit(2)
}
if !FileManager.default.fileExists(atPath: Options().swf) {
    log("rufflehost: skua.swf not found at \(Options().swf)")
    exit(4)
}
app.run()
