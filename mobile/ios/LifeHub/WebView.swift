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
        let script = WKUserScript(source: "document.documentElement.classList.add('in-app')",
                                  injectionTime: .atDocumentEnd, forMainFrameOnly: true)
        config.userContentController.addUserScript(script)

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

    final class Coordinator: NSObject, WKNavigationDelegate, WKUIDelegate {
        let state: AppState
        weak var web: WKWebView?
        var token: UUID

        init(state: AppState) {
            self.state = state
            self.token = state.signInToken
        }

        /// Signs in with the API key (POST /auth/app) and goes to the page.
        func signIn(path: String) {
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
            let html = """
            <html><head><meta name="viewport" content="width=device-width,initial-scale=1">
            <style>body{font:17px -apple-system;padding:40px 24px;color:#333}@media(prefers-color-scheme:dark){body{background:#151917;color:#ddd}}
            a{color:#0f766e;font-weight:600}</style></head><body>
            <h2>Can't reach LifeHub</h2><p>\(state.server)</p><p>\(error.localizedDescription)</p>
            <p>Are you on your home network, or is your LifeHub address reachable from outside?</p>
            <p><a href="\(state.server)/">Try again</a> &nbsp; <a href="lifehub-app://settings">Settings</a></p></body></html>
            """
            webView.loadHTMLString(html, baseURL: nil)
        }
    }
}
