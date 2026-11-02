import SwiftUI

struct ContentView: View {
    @EnvironmentObject private var state: AppState

    var body: some View {
        Group {
            if state.configured {
                LifeHubWebView()
                    .ignoresSafeArea(.container, edges: .bottom)
            } else {
                NavigationStack { SetupView(firstRun: true) }
            }
        }
        .sheet(isPresented: $state.showSettings) {
            NavigationStack { SetupView(firstRun: false) }
        }
    }
}
