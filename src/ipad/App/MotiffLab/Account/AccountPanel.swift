import SwiftUI

/// The account and its plan, in the Settings sheet's Account section, as on the web app's Settings page, with the same
/// words (settings.*, account.*). Testers switch between Pro and no Pro here.
struct AccountPanel: View {
    @ObservedObject var account: Account
    let text: UIText
    @State private var editing = false
    /// «Удалить аккаунт и данные онлайн» asks once more (an alert, as «Очистить всё»), then says what it came to.
    @State private var confirmingDelete = false
    @State private var deleteProblem: String?
    @State private var deletedOnline = false
    /// Saving online and sync with the player's other devices (version 0.2): on once signed in, until turned off here.
    @AppStorage(CloudSync.onKey) private var syncOn = true

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            if !account.isAvailable {
                Text(text["settings.accountWhy"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            } else if let player = account.player {
                signedIn(player)
            } else {
                signedOut
            }
        }
        .disabled(account.busy)
        .overlay { if account.busy { ProgressView() } }
        .alert(text["account.deleteTitle"], isPresented: $confirmingDelete) {
            Button(text["account.deleteConfirm"], role: .destructive) { deleteOnline() }
            Button(text["common.cancel"], role: .cancel) {}
        } message: {
            Text(text["account.deleteMessage"])
        }
        // Signed in again: what the last delete came to is no longer the news.
        .onChange(of: account.player) { _, now in if now != nil { deletedOnline = false } }
    }

    /// Signed in: who, the profile, what can be done with the account, the plan and saving online.
    @ViewBuilder private func signedIn(_ player: String) -> some View {
        Text(text["account.title"]).dsType(.label).foregroundStyle(Color.ds("ink"))
        HStack(spacing: CGFloat.ds("s-3")) {
            PlayerAvatar(name: account.shownName ?? player, image: account.profile?.image)
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                Text(account.shownName ?? player).dsType(.label).foregroundStyle(Color.ds("ink"))
                Text(text["settings.signedIn"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            Spacer()
        }
        .accessibilityElement(children: .combine)
        // The profile (Andrei, 2026-11-06): the name and photo the player chose, kept with the account on any plan.
        if editing {
            ProfileEditor(account: account, text: text) { editing = false }
        } else {
            Button(text["profile.edit"]) { editing = true }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
        }
        accountActions
        plan
        saveOnline
    }

    /// What the player does with the account itself, one button each, under the profile: signing out, and deleting what
    /// the account keeps online (App Store 5.1.1(v)), which asks once more. Where the app may not write online (Main in a
    /// version build) the delete is offered disabled, with why (E-settings-4).
    @ViewBuilder private var accountActions: some View {
        // Wrapped, so the long «Удалить аккаунт и данные онлайн» goes under «Выйти» in a narrow sheet.
        FlowLayout(spacing: CGFloat.ds("s-2")) {
            Button(text["settings.signOut"]) { account.signOut() }
                .buttonStyle(DSButtonStyle(kind: .danger, compact: true))
            Button(text["account.delete"]) {
                deleteProblem = nil
                confirmingDelete = true
            }
            .buttonStyle(DSButtonStyle(kind: .danger, compact: true))
            .disabled(!account.writes)
        }
        if !account.writes {
            Text(text["account.deleteReadOnly"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        }
        if let deleteProblem {
            Text(deleteProblem).dsType(.caption).foregroundStyle(Color.ds("danger"))
        }
    }

    /// The delete, once confirmed: signed out when it is done, and the signed-out section says the data online went.
    private func deleteOnline() {
        Task {
            let deleted = await account.deleteOnlineData()
            deleteProblem = deleted.problemKey.map { text[$0] }
            deletedOnline = deleted == .done
        }
    }

    /// Saving online and sync, with what it does now.
    private var saveOnline: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Toggle(text["settings.saveOnline"], isOn: $syncOn)
                .dsType(.label)
                .foregroundStyle(Color.ds("ink"))
                .tint(Color.ds("accent"))
            Text(syncOn ? text.format("settings.onlineOn", String(CloudSync.freeProjectsOnline)) : text["settings.onlineOff"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
        }
    }

    /// Signed out: why to sign in, and signing in or creating an account.
    @ViewBuilder private var signedOut: some View {
        Text(text["settings.accountTitle"]).dsType(.label).foregroundStyle(Color.ds("ink"))
        Text(text["settings.accountWhy"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        HStack(spacing: CGFloat.ds("s-2")) {
            Button(text["settings.signIn"]) { Task { await account.signIn() } }
                .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            Button(text["settings.createAccount"]) { Task { await account.signIn(create: true) } }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
        }
        Text(text["settings.signInHow"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        if deletedOnline {
            Text(text["account.deleted"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        }
        if let problem = account.problem {
            Text(problem).dsType(.caption).foregroundStyle(Color.ds("danger"))
        }
    }

    /// The plan (0018 section 12): what runs on the device is free, what needs the cloud is Pro.
    @ViewBuilder private var plan: some View {
        HStack {
            Text(text["settings.plan"]).dsType(.label).foregroundStyle(Color.ds("ink-2"))
            Spacer()
            Text(planName).dsType(.label).foregroundStyle(account.plan?.isPro == true ? Color.ds("plum") : Color.ds("ink"))
            if account.plan == nil && account.planFailed && !account.planAsking {
                Button(text["settings.planRetry"]) { Task { await account.refreshPlan() } }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
        }
        if account.plan?.isTester == true {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                Text(text["settings.testPlan"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                SegmentedChoice(
                    options: [(Plan?.none, text["settings.testPlanAccount"]), (Plan?.some(.pro), text["settings.testPlanPro"]), (Plan?.some(.free), text["settings.testPlanFree"])],
                    chosen: account.choice,
                    label: text["settings.testPlan"]
                ) { choice in Task { await account.choose(choice) } }
                Text(text["settings.testPlanHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
        }
    }

    private var planName: String {
        // "Did not answer" only after an ask failed; while asking (a cold start takes up to half a minute), «Проверяю…».
        guard let plan = account.plan else { return text[account.planFailed && !account.planAsking ? "settings.planUnknown" : "settings.planChecking"] }
        return plan.isPro ? text["settings.planPro"] : text["settings.planFree"]
    }
}

/// The player's picture (0018 section 9, "Account"): the photo they chose for their profile, else the first letter of
/// their name on cobalt, as the web app's avatar shows it.
struct PlayerAvatar: View {
    let name: String
    let image: UIImage?
    var size: CGFloat = 44

    var body: some View {
        Group {
            if let image {
                Image(uiImage: image).resizable().scaledToFill()
            } else {
                Text(Self.initial(name))
                    .font(DesignTokens.font(weight: 700, size: size * 0.4))
                    .foregroundStyle(Color.ds("on-accent"))
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Color.ds("accent"))
            }
        }
        .frame(width: size, height: size)
        .clipShape(Circle())
        .accessibilityHidden(true)
    }

    /// The first letter or digit of the name, upper case; "?" when it has none (the web's AccountButton.Initial).
    static func initial(_ name: String) -> String {
        name.first(where: { $0.isLetter || $0.isNumber }).map { String($0).uppercased() } ?? "?"
    }
}
