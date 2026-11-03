import AppIntents
import Foundation

// Siri and Shortcuts actions. They talk to LifeHub directly with your API key — no need to open the app.

struct AddNoteIntent: AppIntent {
    static var title: LocalizedStringResource = "Add a note"
    static var description = IntentDescription("Saves a note in LifeHub; it's filed into a category for you.")
    @Parameter(title: "Note") var text: String

    func perform() async throws -> some IntentResult & ProvidesDialog {
        try await API.current.post("/api/v1/notes", ["text": text])
        return .result(dialog: IntentDialog(stringLiteral: "Saved to your notes."))
    }
}

struct AddTaskIntent: AppIntent {
    static var title: LocalizedStringResource = "Add a task"
    @Parameter(title: "Task") var title: String

    func perform() async throws -> some IntentResult & ProvidesDialog {
        try await API.current.post("/api/v1/tasks", ["title": title])
        return .result(dialog: IntentDialog(stringLiteral: "Added \(title) to your tasks."))
    }
}

struct AddShoppingIntent: AppIntent {
    static var title: LocalizedStringResource = "Add to the shopping list"
    @Parameter(title: "Item") var item: String

    func perform() async throws -> some IntentResult & ProvidesDialog {
        try await API.current.post("/api/v1/shopping", ["name": item])
        return .result(dialog: IntentDialog(stringLiteral: "Added \(item) to the shopping list."))
    }
}

enum LifeHubPage: String, AppEnum {
    case today = "", tasks, scan, shopping, meals, notes, renewals, kitchen
    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Page"
    static var caseDisplayRepresentations: [LifeHubPage: DisplayRepresentation] = [
        .today: "Today", .tasks: "Tasks", .scan: "Scan", .shopping: "Shopping", .meals: "Meals", .notes: "Notes", .renewals: "Renewals", .kitchen: "Kitchen"
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
        AppShortcut(intent: OpenPageIntent(), phrases: ["Open \(.applicationName)", "Open \(\.$page) in \(.applicationName)"],
                    shortTitle: "Open page", systemImageName: "square.grid.2x2")
    }
}
