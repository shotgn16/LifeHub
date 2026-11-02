import SwiftUI
import BackgroundTasks
import UserNotifications

@main
struct LifeHubApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @Environment(\.scenePhase) private var phase
    @StateObject private var state = AppState.shared

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(state)
        }
        .onChange(of: phase) { _, newPhase in
            if newPhase == .background { Notices.scheduleRefresh() }
            if newPhase == .active { Task { await Notices.check() } }
        }
        .backgroundTask(.appRefresh(Notices.taskId)) {
            await Notices.check()
            Notices.scheduleRefresh()
        }
    }
}

/// Tapping a notification opens its page in LifeHub.
final class AppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        UNUserNotificationCenter.current().delegate = self
        return true
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification) async -> UNNotificationPresentationOptions {
        [.banner, .sound, .list]
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse) async {
        let link = response.notification.request.content.userInfo["link"] as? String ?? ""
        await MainActor.run { AppState.shared.pendingPath = link }
    }
}

/// LifeHub notifications, fetched when iOS lets the app refresh in the background (usually a few times a day)
/// and whenever you open the app. For instant alerts, keep LifeHub's Home Assistant notifications on as well.
enum Notices {
    static let taskId = "io.github.shotgn16.lifehub.refresh"

    static func scheduleRefresh() {
        let request = BGAppRefreshTaskRequest(identifier: taskId)
        request.earliestBeginDate = Date(timeIntervalSinceNow: 15 * 60)
        try? BGTaskScheduler.shared.submit(request)
    }

    static func check() async {
        let state = AppState.shared
        guard state.configured, state.notifications else { return }
        do {
            let first = state.lastNotice.isEmpty
            let list = try await API.current.notices(since: state.lastNotice)
            var newest = state.lastNotice
            for n in list {
                if n.createdUtc > newest { newest = n.createdUtc }
                if !first && n.read != true { await show(n) }
            }
            if newest.isEmpty { newest = ISO8601DateFormatter().string(from: Date()) }
            state.lastNotice = newest
        } catch { }
    }

    private static func show(_ n: API.Notice) async {
        let content = UNMutableNotificationContent()
        content.title = n.title
        content.body = n.message
        content.sound = .default
        content.userInfo = ["link": n.link ?? ""]
        try? await UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: n.id, content: content, trigger: nil))
    }
}
