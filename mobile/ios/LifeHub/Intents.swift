import AppIntents
import Foundation

// Siri and Shortcuts actions. They talk to LifeHub directly with your API key — no need to open the app.

struct AddNoteIntent: AppIntent {
    static var title: LocalizedStringResource = "Add a note"
    static var description = IntentDescription("Saves a note in LifeHub; it's filed into a category for you.")
    @Parameter(title: "Note") var text: String

    func perform() async throws -> some IntentResult & ProvidesDialog {
        try await API.current.post("/api/v1/notes", ["text": text])
        return .result(dialog: "Saved to your notes.")
    }
}

struct AddTaskIntent: AppIntent {
    static var title: LocalizedStringResource = "Add a task"
    @Parameter(title: "Task") var title: String

    func perform() async throws -> some IntentResult & ProvidesDialog {
        try await API.current.post("/api/v1/tasks", ["title": title])
        return .result(dialog: "Added \(title) to your tasks.")
    }
}

struct AddShoppingIntent: AppIntent {
    static var title: LocalizedStringResource = "Add to the shopping list"
    @Parameter(title: "Item") var item: String

    func perform() async throws -> some IntentResult & ProvidesDialog {
        try await API.current.post("/api/v1/shopping", ["name": item])
        return .result(dialog: "Added \(item) to the shopping list.")
    }
}

struct LogReadingIntent: AppIntent {
    static var title: LocalizedStringResource = "Log a Bible reading"
    static var description = IntentDescription("Logs what you read. Leave it empty to tick off today's verse.")
    @Parameter(title: "Passage", description: "e.g. Psalm 23, or a bible.com link") var reference: String?

    func perform() async throws -> some IntentResult & ProvidesDialog {
        var body: [String: Any] = [:]
        if let reference, !reference.isEmpty { body["reference"] = reference }
        try await API.current.post("/api/v1/bible/read", body)
        return .result(dialog: reference?.isEmpty == false ? "Logged \(reference!)." : "Today's verse is ticked off.")
    }
}

/// For the Shortcuts automations "When Bible is opened / closed" — times your reading.
struct BibleOpenedIntent: AppIntent {
    static var title: LocalizedStringResource = "Bible app opened"
    func perform() async throws -> some IntentResult {
        try await API.current.post("/api/v1/bible/app", ["event": "opened", "device": "iPhone"])
        return .result()
    }
}

struct BibleClosedIntent: AppIntent {
    static var title: LocalizedStringResource = "Bible app closed"
    func perform() async throws -> some IntentResult {
        try await API.current.post("/api/v1/bible/app", ["event": "closed", "device": "iPhone"])
        return .result()
    }
}

enum LifeHubPage: String, AppEnum {
    case today = "", tasks, scan, shopping, meals, notes, bible
    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Page"
    static var caseDisplayRepresentations: [LifeHubPage: DisplayRepresentation] = [
        .today: "Today", .tasks: "Tasks", .scan: "Scan", .shopping: "Shopping", .meals: "Meals", .notes: "Notes", .bible: "Bible"
    ]
}

struct OpenPageIntent: AppIntent {
    static var title: LocalizedStringResource = "Open LifeHub"
    static var openAppWhenRun = true
    @Parameter(title: "Page", default: .today) var page: LifeHubPage

    @MainActor
    func perform() async throws -> some IntentResult {
        AppState.shared.pendingPath = page.rawValue
        return .result()
    }
}

struct LifeHubShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: AddNoteIntent(), phrases: ["Add a note in \(.applicationName)", "Note in \(.applicationName)"],
                    shortTitle: "Add note", systemImageName: "note.text")
        AppShortcut(intent: AddShoppingIntent(), phrases: ["Add to my \(.applicationName) shopping list"],
                    shortTitle: "Add shopping", systemImageName: "cart")
        AppShortcut(intent: AddTaskIntent(), phrases: ["Add a task in \(.applicationName)"],
                    shortTitle: "Add task", systemImageName: "checkmark.circle")
        AppShortcut(intent: LogReadingIntent(), phrases: ["Log my Bible reading in \(.applicationName)"],
                    shortTitle: "Log reading", systemImageName: "book")
        AppShortcut(intent: OpenPageIntent(), phrases: ["Open \(.applicationName)", "Open \(\.$page) in \(.applicationName)"],
                    shortTitle: "Open page", systemImageName: "square.grid.2x2")
    }
}
