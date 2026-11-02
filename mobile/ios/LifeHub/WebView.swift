import SwiftUI
import WebKit

/// LifeHub in a web view, signed in with your API key. Shows "Phone app settings" in LifeHub's menu.
struct LifeHubWebView: UIViewRepresentable {
    @EnvironmentObject private var state: AppState

    func makeCoordinator() -> Coordinator { Coordinator(state: state) }

    func makeUIView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.allowsInlineMediaPlayback = true
        config.mediaTypesRequiringUserActionForPlayback = []
        config.websiteDataStore = .default()
        config.applicationNameForUserAgent = "LifeHubApp/1.0 iOS"
        let script = WKUserScript(source: Coordinator.watchScript, injectionTime: .atDocumentEnd, forMainFrameOnly: true)
        config.userContentController.addUserScript(script)
        config.userContentController.addScriptMessageHandler(context.coordinator, contentWorld: .page, name: "lifehub")

        let web = WKWebView(frame: .zero, configuration: config)
        web.navigationDelegate = context.coordinator
        web.uiDelegate = context.coordinator
        web.allowsBackForwardNavigationGestures = true
        web.isOpaque = false
        web.backgroundColor = .systemBackground
        let refresh = UIRefreshControl()
        refresh.addTarget(context.coordinator, action: #selector(Coordinator.reload(_:)), for: .valueChanged)
        web.scrollView.refreshControl = refresh
        context.coordinator.web = web
        context.coordinator.signIn(path: state.pendingPath ?? "")
        DispatchQueue.main.async { state.pendingPath = nil }
        return web
    }

    func updateUIView(_ web: WKWebView, context: Context) {
        let c = context.coordinator
        if c.token != state.signInToken {
            c.token = state.signInToken
            c.signIn(path: "")
        } else if let path = state.pendingPath {
            DispatchQueue.main.async { state.pendingPath = nil }
            c.open(path: path)
        }
    }

    final class Coordinator: NSObject, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandlerWithReply {
        let state: AppState
        weak var web: WKWebView?
        var token: UUID
        var showingOffline = false
        private let store = OfflineStore.shared

        /// Runs in LifeHub's pages: marks the app, and if the live connection stays down for 12 s, switches to offline.
        static let watchScript = """
        (function () {
          document.documentElement.classList.add('in-app');
          if (window.__lifehubWatch) return; window.__lifehubWatch = 1;
          var since = 0;
          setInterval(function () {
            var m = document.getElementById('components-reconnect-modal');
            var shown = m && getComputedStyle(m).display !== 'none' && getComputedStyle(m).visibility !== 'hidden';
            if (!shown) { since = 0; return; }
            since = since || Date.now();
            if (Date.now() - since > 12000) { since = 0; try { window.webkit.messageHandlers.lifehub.postMessage({ name: 'goOffline', arg: '' }); } catch (e) {} }
          }, 2000);
        })();
        """

        /// LifeHub can't be reached: show the offline screen with the copy saved on the phone (or, if LifeHub
        /// turns out to be reachable after all, just reload it).
        func showOffline() {
            guard !showingOffline else { return }
            showingOffline = true
            Task { @MainActor in
                let ok = await store.sync()
                if ok && store.pending.isEmpty { showingOffline = false; signIn(path: "") }
                else if let url = Bundle.main.url(forResource: "offline", withExtension: "html") {
                    web?.loadFileURL(url, allowingReadAccessTo: url.deletingLastPathComponent())
                }
            }
        }

        func refreshOffline() {
            if showingOffline { web?.evaluateJavaScript("window.lifehubRefresh && window.lifehubRefresh()") }
        }

        /// Requests from the offline screen (and LifeHub's own pages).
        func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage,
                                   replyHandler: @escaping (Any?, String?) -> Void) {
            let body = message.body as? [String: Any] ?? [:]
            let name = body["name"] as? String ?? ""
            let arg = body["arg"] as? String ?? ""
            Task { @MainActor in
                switch name {
                case "state":
                    replyHandler(store.stateJSON(), nil)
                case "queue":
                    let obj = (try? JSONSerialization.jsonObject(with: Data(arg.utf8))) as? [String: Any] ?? [:]
                    store.enqueue(type: obj["type"] as? String ?? "", data: obj["data"] as? [String: Any] ?? [:])
                    replyHandler(store.stateJSON(), nil)
                    await store.sync()
                    refreshOffline()
                case "sync":
                    await store.sync()
                    replyHandler(store.stateJSON(), nil)
                case "goOnline":
                    replyHandler("", nil)
                    showingOffline = false
                    signIn(path: "")
                case "goOffline":
                    replyHandler("", nil)
                    showOffline()
                case "settings":
                    replyHandler("", nil)
                    state.showSettings = true
                default:
                    replyHandler(nil, "unknown request")
                }
            }
        }

        init(state: AppState) {
            self.state = state
            self.token = state.signInToken
        }

        /// Signs in with the API key (POST /auth/app) and goes to the page.
        func signIn(path: String) {
            showingOffline = false
            guard let web, let url = URL(string: state.server + "/auth/app") else { return }
            var r = URLRequest(url: url)
            r.httpMethod = "POST"
            r.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
            let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-._~"))
            let key = state.apiKey.addingPercentEncoding(withAllowedCharacters: allowed) ?? ""
            let back = ("/" + path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))).addingPercentEncoding(withAllowedCharacters: allowed) ?? "%2F"
            r.httpBody = "key=\(key)&returnUrl=\(back)".data(using: .utf8)
            web.load(r)
        }

        func open(path: String) {
            showingOffline = false
            guard let url = URL(string: state.server + "/" + path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))) else { return }
            web?.load(URLRequest(url: url))
        }

        @objc func reload(_ sender: UIRefreshControl) {
            web?.reload()
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { sender.endRefreshing() }
        }

        func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction,
                     decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
            guard let url = action.request.url else { return decisionHandler(.allow) }
            if url.isFileURL { return decisionHandler(.allow) }
            if url.scheme == "lifehub-app" {
                if url.host == "settings" { state.showSettings = true }
                return decisionHandler(.cancel)
            }
            // Other sites (YouVersion, StudyLight, store offers) open in their app or Safari.
            if let host = url.host, host != state.serverURL?.host, action.navigationType == .linkActivated || action.targetFrame == nil {
                UIApplication.shared.open(url)
                return decisionHandler(.cancel)
            }
            decisionHandler(.allow)
        }

        // Links with target=_blank.
        func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                     for action: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
            if let url = action.request.url {
                if url.host == state.serverURL?.host { webView.load(action.request) } else { UIApplication.shared.open(url) }
            }
            return nil
        }

        // The barcode scanner asks for the camera.
        func webView(_ webView: WKWebView, requestMediaCapturePermissionFor origin: WKSecurityOrigin,
                     initiatedByFrame frame: WKFrameInfo, type: WKMediaCaptureType,
                     decisionHandler: @escaping (WKPermissionDecision) -> Void) {
            decisionHandler(origin.host == state.serverURL?.host ? .grant : .deny)
        }

        func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
            if (error as NSError).code == NSURLErrorCancelled { return }
            showOffline()
        }

        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            // While online, keep the copy on the phone fresh (and send anything still waiting).
            if webView.url?.isFileURL == false { Task { await store.sync(force: false) } }
        }
    }
}
