import Foundation
import Security

/// Where your LifeHub is and this person's API key (kept in the iPhone's Keychain).
final class AppState: ObservableObject {
    static let shared = AppState()

    @Published var server: String { didSet { UserDefaults.standard.set(server, forKey: "server") } }
    @Published var personName: String { didSet { UserDefaults.standard.set(personName, forKey: "personName") } }
    @Published var notifications: Bool { didSet { UserDefaults.standard.set(notifications, forKey: "notifications") } }
    /// A page to open next (from a notification, a Shortcut or Siri).
    @Published var pendingPath: String?
    @Published var showSettings = false
    /// Bumped after saving settings, so the web view signs in again.
    @Published var signInToken = UUID()

    var lastNotice: String {
        get { UserDefaults.standard.string(forKey: "lastNotice") ?? "" }
        set { UserDefaults.standard.set(newValue, forKey: "lastNotice") }
    }

    var apiKey: String {
        get { Keychain.read("apiKey") ?? "" }
        set { Keychain.write("apiKey", newValue); objectWillChange.send() }
    }

    var configured: Bool { server.hasPrefix("http") && !apiKey.isEmpty }
    var serverURL: URL? { URL(string: server) }

    private init() {
        server = UserDefaults.standard.string(forKey: "server") ?? ""
        personName = UserDefaults.standard.string(forKey: "personName") ?? ""
        notifications = UserDefaults.standard.object(forKey: "notifications") as? Bool ?? true
    }
}

enum Keychain {
    private static let service = "io.github.shotgn16.lifehub"

    static func read(_ key: String) -> String? {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                    kSecAttrAccount as String: key, kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
        var out: AnyObject?
        guard SecItemCopyMatching(query as CFDictionary, &out) == errSecSuccess, let data = out as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func write(_ key: String, _ value: String) {
        let base: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: key]
        SecItemDelete(base as CFDictionary)
        guard !value.isEmpty else { return }
        var add = base
        add[kSecValueData as String] = Data(value.utf8)
        add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        SecItemAdd(add as CFDictionary, nil)
    }
}
