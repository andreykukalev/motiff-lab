import SwiftUI

/// Settings until the redesign's settings replace them (0018): the account and its plan, as on the web app's Settings
/// page, with the same words (settings.*, account.*). Testers switch between Pro and no Pro here.
struct SettingsView: View {
    @ObservedObject var account: Account
    let text: UIText
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                if account.isAvailable {
                    accountSection
                    if account.player != nil { planSection }
                }
            }
            .disabled(account.busy)
            .overlay { if account.busy { ProgressView() } }
            .navigationTitle(text["settings.title"])
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button(text["common.done"]) { dismiss() }
                }
            }
        }
    }

    @ViewBuilder private var accountSection: some View {
        if let player = account.player {
            Section(text["account.title"]) {
                LabeledContent(text["settings.signedIn"], value: player)
                Button(text["settings.signOut"], role: .destructive) { account.signOut() }
            }
        } else {
            Section {
                Button(text["settings.signIn"]) { Task { await account.signIn() } }
                Button(text["settings.createAccount"]) { Task { await account.signIn(create: true) } }
            } header: {
                Text(text["settings.accountTitle"])
            } footer: {
                VStack(alignment: .leading, spacing: 6) {
                    Text(text["settings.accountWhy"])
                    Text(text["settings.signInHow"])
                    if let problem = account.problem { Text(problem).foregroundStyle(.red) }
                }
            }
        }
    }

    /// The plan (0018 section 12, preparation item 14): what runs on the device is free, what needs the cloud is Pro.
    @ViewBuilder private var planSection: some View {
        Section {
            LabeledContent(text["settings.plan"], value: planName)
            if account.plan?.isTester == true {
                Picker(text["settings.testPlan"], selection: Binding(get: { account.choice }, set: { choice in Task { await account.choose(choice) } })) {
                    Text(text["settings.testPlanAccount"]).tag(Plan?.none)
                    Text(text["settings.testPlanPro"]).tag(Plan?.some(.pro))
                    Text(text["settings.testPlanFree"]).tag(Plan?.some(.free))
                }
            }
        } footer: {
            if account.plan?.isTester == true { Text(text["settings.testPlanHint"]) }
        }
    }

    private var planName: String {
        guard let plan = account.plan else { return text["settings.planUnknown"] }
        return plan.isPro ? text["settings.planPro"] : text["settings.planFree"]
    }
}
