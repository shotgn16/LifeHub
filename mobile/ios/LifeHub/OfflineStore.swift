import Foundation

/// A copy of your LifeHub data kept on the iPhone for when LifeHub can't be reached, plus the changes you made
/// offline ("outbox"). `sync()` sends the outbox (each change has its own id, so a retry is never applied twice)
/// and refreshes the saved copy.
final class OfflineStore {
    static let shared = OfflineStore()

    private let queue = DispatchQueue(label: "io.github.shotgn16.lifehub.offline")
    private let dir: URL
    private var snapshotURL: URL { dir.appendingPathComponent("offline-snapshot.json") }
    private var outboxURL: URL { dir.appendingPathComponent("offline-outbox.json") }
    private(set) var online = false
    private(set) var lastError = ""
    private var lastRefresh = Date.distantPast

    private init() {
        dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    private func readJSON(_ url: URL) -> Any? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONSerialization.jsonObject(with: data)
    }

    private func write(_ value: Any, to url: URL) {
        if let data = try? JSONSerialization.data(withJSONObject: value) { try? data.write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication]) }
    }

    var snapshot: [String: Any]? { queue.sync { readJSON(snapshotURL) as? [String: Any] } }
    var pending: [[String: Any]] { queue.sync { readJSON(outboxURL) as? [[String: Any]] ?? [] } }

    func enqueue(type: String, data: [String: Any]) {
        queue.sync {
            var list = readJSON(outboxURL) as? [[String: Any]] ?? []
            list.append(["id": UUID().uuidString.lowercased(), "type": type,
                         "at": ISO8601DateFormatter().string(from: Date()), "data": data])
            write(list, to: outboxURL)
        }
    }

    /// The offline screen's view of things, as JSON text.
    func stateJSON() -> String {
        let snap = snapshot
        let state: [String: Any] = ["snapshot": snap ?? NSNull(), "pending": pending, "online": online,
                                    "lastError": lastError, "lastSyncUtc": snap?["generatedUtc"] ?? ""]
        guard let data = try? JSONSerialization.data(withJSONObject: state) else { return "{}" }
        return String(data: data, encoding: .utf8) ?? "{}"
    }

    /// Sends waiting changes and refreshes the saved copy. Returns true if LifeHub was reachable.
    @discardableResult
    func sync(force: Bool = true) async -> Bool {
        let app = AppState.shared
        guard app.configured, let base = URL(string: app.server) else { return false }
        let out = pending
        if !force && out.isEmpty && Date().timeIntervalSince(lastRefresh) < 300 { return true }
        do {
            if !out.isEmpty {
                var r = URLRequest(url: base.appendingPathComponent("api/v1/sync"), timeoutInterval: 20)
                r.httpMethod = "POST"
                r.setValue(app.apiKey, forHTTPHeaderField: "X-Api-Key")
                r.setValue("application/json", forHTTPHeaderField: "Content-Type")
                r.httpBody = try JSONSerialization.data(withJSONObject: ["changes": out])
                let (data, resp) = try await URLSession.shared.data(for: r)
                try check(resp)
                let json = try JSONSerialization.jsonObject(with: data) as? [String: Any] ?? [:]
                let results = json["results"] as? [[String: Any]] ?? []
                var finished = Set<String>()
                var error = ""
                for res in results {
                    if (res["status"] as? String) == "error" { error = res["message"] as? String ?? "" }
                    else if let id = res["id"] as? String { finished.insert(id) }
                }
                queue.sync {
                    // Re-read: more may have been added while this was being sent.
                    let now = (readJSON(outboxURL) as? [[String: Any]] ?? []).filter { !finished.contains($0["id"] as? String ?? "") }
                    write(now, to: outboxURL)
                    if let snap = json["snapshot"] { write(snap, to: snapshotURL) }
                }
                lastError = error.isEmpty ? "" : "LifeHub couldn't apply a change: \(error) (it will try again)"
            } else {
                var r = URLRequest(url: base.appendingPathComponent("api/v1/offline"), timeoutInterval: 15)
                r.setValue(app.apiKey, forHTTPHeaderField: "X-Api-Key")
                let (data, resp) = try await URLSession.shared.data(for: r)
                try check(resp)
                let snap = try JSONSerialization.jsonObject(with: data)
                queue.sync { write(snap, to: snapshotURL) }
                lastError = ""
            }
            lastRefresh = Date()
            online = true
            return true
        } catch let e as API.Failure {
            online = true
            lastError = e.message
            return false
        } catch {
            online = false
            return false
        }
    }

    private func check(_ response: URLResponse) throws {
        let code = (response as? HTTPURLResponse)?.statusCode ?? 0
        if code == 401 { throw API.Failure(message: "The API key wasn't accepted. Check Phone app settings.") }
        guard (200..<300).contains(code) else { throw API.Failure(message: "LifeHub answered \(code).") }
    }
}
