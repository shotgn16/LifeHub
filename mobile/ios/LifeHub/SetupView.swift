import SwiftUI
import UserNotifications

/// First run and "Phone app settings": your LifeHub address, your API key, notifications.
struct SetupView: View {
    let firstRun: Bool
    @EnvironmentObject private var state: AppState
    @Environment(\.dismiss) private var dismiss
    @State private var server = ""
    @State private var key = ""
    @State private var status = ""
    @State private var busy = false

    var body: some View {
        Form {
            if firstRun {
                Section {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("LifeHub").font(.largeTitle.bold())
                        Text("Connect this iPhone to your own LifeHub.").foregroundStyle(.secondary)
                    }
                }
            }
            Section {
                TextField("https://lifehub.example.com", text: $server)
                    .keyboardType(.URL).textContentType(.URL)
                    .textInputAutocapitalization(.never).autocorrectionDisabled()
            } header: { Text("LifeHub address") } footer: {
                Text("The address you open LifeHub on from your phone: your own https address, or http://192.168.1.10:8099 at home (map the add-on's port in Home Assistant).")
            }
            Section {
                SecureField("lh_…", text: $key)
                    .textInputAutocapitalization(.never).autocorrectionDisabled()
                    .font(.system(.body, design: .monospaced))
            } header: { Text("Your API key") } footer: {
                Text("In LifeHub: Settings → Security & API → Your API key → Show → copy. It signs you in as you.")
            }
            Section {
                Button(busy ? "Checking…" : "Check & save") { Task { await save() } }
                    .disabled(busy || server.isEmpty || key.isEmpty)
                if !status.isEmpty { Text(status).font(.footnote) }
            }
            Section {
                Toggle("LifeHub notifications", isOn: $state.notifications)
                    .onChange(of: state.notifications) { _, on in if on { requestNotifications() } }
            } footer: {
                Text("Checked whenever you open the app and when iOS allows a background refresh (a few times a day). For instant reminders, also keep notifications on in the Home Assistant app.")
            }
            Section("Siri & Shortcuts") {
                Text("Add notes, tasks and shopping, log a Bible reading, or open a page — say “Add a note in LifeHub”, or use the LifeHub actions in the Shortcuts app.")
                Text("Time in the Bible app: Shortcuts → Automation → + → App → Bible → Is Opened → Run Immediately → add “Bible app opened (LifeHub)”. Repeat for Is Closed with “Bible app closed”.")
                    .font(.footnote).foregroundStyle(.secondary)
            }
        }
        .navigationTitle(firstRun ? "" : "Phone app settings")
        .toolbar {
            if !firstRun { ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } } }
        }
        .onAppear {
            server = state.server
            key = state.apiKey
            if !state.personName.isEmpty { status = "Signed in as \(state.personName)" }
        }
    }

    private func save() async {
        busy = true
        defer { busy = false }
        var url = server.trimmingCharacters(in: .whitespaces)
        while url.hasSuffix("/") { url.removeLast() }
        if !url.hasPrefix("http") { url = "http://" + url }
        let k = key.trimmingCharacters(in: .whitespaces)
        do {
            let me = try await API(server: url, key: k).me()
            state.server = url
            state.apiKey = k
            state.personName = me.name
            status = "Signed in as \(me.name)" + (me.household.map { " · \($0)" } ?? "")
            if state.notifications { requestNotifications() }
            state.signInToken = UUID()
            if !firstRun { dismiss() }
        } catch {
            status = "Couldn't reach LifeHub: \(error.localizedDescription)"
        }
    }

    private func requestNotifications() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge]) { _, _ in }
    }
}
