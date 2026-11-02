import Foundation

/// LifeHub's REST API, authenticated with this person's API key.
struct API {
    struct Failure: LocalizedError {
        let message: String
        var errorDescription: String? { message }
    }

    struct Me: Decodable {
        let name: String
        let household: String?
        let version: String?
    }

    struct Notice: Decodable {
        let id: String
        let title: String
        let message: String
        let link: String?
        let createdUtc: String
        let read: Bool?
    }

    var server: String
    var key: String

    static var current: API { API(server: AppState.shared.server, key: AppState.shared.apiKey) }

    private func request(_ path: String, method: String = "GET", body: [String: Any]? = nil) async throws -> Data {
        guard let url = URL(string: server.trimmingCharacters(in: CharacterSet(charactersIn: "/")) + path) else {
            throw Failure(message: "That LifeHub address doesn't look right.")
        }
        var r = URLRequest(url: url, timeoutInterval: 30)
        r.httpMethod = method
        r.setValue(key, forHTTPHeaderField: "X-Api-Key")
        r.setValue("application/json", forHTTPHeaderField: "Accept")
        if let body {
            r.setValue("application/json", forHTTPHeaderField: "Content-Type")
            r.httpBody = try JSONSerialization.data(withJSONObject: body)
        }
        let (data, response) = try await URLSession.shared.data(for: r)
        let code = (response as? HTTPURLResponse)?.statusCode ?? 0
        if code == 401 { throw Failure(message: "The API key wasn't accepted. Copy it again from Settings → Security & API.") }
        guard (200..<300).contains(code) else { throw Failure(message: "LifeHub answered \(code).") }
        return data
    }

    func me() async throws -> Me { try JSONDecoder().decode(Me.self, from: try await request("/api/v1/me")) }

    func notices(since: String) async throws -> [Notice] {
        let q = since.isEmpty ? "" : "?since=" + (since.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? since)
        return try JSONDecoder().decode([Notice].self, from: try await request("/api/v1/notices" + q))
    }

    @discardableResult
    func post(_ path: String, _ body: [String: Any]) async throws -> Data {
        try await request(path, method: "POST", body: body)
    }
}
