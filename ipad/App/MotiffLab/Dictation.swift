import AVFoundation
import Speech
import SwiftUI

/// Dictation into a chat's field (v0.6.4, Andrei 2026-11-15): the
/// microphone beside the field hears the player in the interface's language and writes what it hears into the field as it
/// hears it, after what was typed. Speech is recognised on the iPad itself only: where this iPad cannot recognise the
/// language on the device, a press on the microphone says so and nothing is heard (the sound never goes to Apple's servers
/// or ours). A press on the microphone, or a pause in the speech, stops it. The rule is pure; `Dictation` runs it.
struct DictationFlow: Equatable {
    /// Why the microphone cannot dictate now.
    enum Problem: Equatable {
        /// This iPad cannot recognise the language on the device itself.
        case notOnDevice
        /// The player did not allow the microphone or speech recognition.
        case denied
        /// The iPad's own settings (Screen Time, a profile) do not allow it.
        case restricted
        /// The last dictation broke off (no input, the recogniser stopped); a press tries again.
        case failed
    }

    enum State: Equatable {
        case ready
        /// Waiting for the iPad's permission prompts or for the microphone to start.
        case starting
        case listening
        case unavailable(Problem)
    }

    enum Event: Equatable {
        case press
        /// The microphone and the recogniser started.
        case started
        case refused(Problem)
        /// The words heard so far in this dictation, whole.
        case heard(String)
        /// No new words for `quiet` seconds.
        case silence(TimeInterval)
        /// The recogniser ended by itself (its final result, or an error).
        case ended(failed: Bool)
        /// Stop now: the player types or sends, the assistant is busy, the chat went.
        case halt
    }

    /// What the controller does after an event.
    enum Command: Equatable {
        case start, stop
    }

    /// Seconds of quiet that stop a dictation once something was heard, and before anything was.
    static let pauseAfterWords: TimeInterval = 2.5
    static let pauseBeforeWords: TimeInterval = 8

    private(set) var state: State = .ready
    /// The player pressed the microphone since it became unavailable: its line says why (known before, it waits for a press).
    private(set) var told = false
    /// The field's text when the dictation began: what is heard goes after it.
    private(set) var before = ""
    private(set) var heard = ""

    init(problem: Problem? = nil) {
        if let problem { state = .unavailable(problem) }
    }

    var listening: Bool { state == .listening }

    /// The microphone takes a press but while it starts: unavailable, a press asks again (a permission given in Settings
    /// since) or says why.
    var pressable: Bool { state != .starting }

    /// What stands in the field: the text typed before, a space, then the words heard.
    var text: String { Self.join(before, heard) }

    static func join(_ before: String, _ heard: String) -> String {
        if heard.isEmpty { return before }
        if before.isEmpty || before.last?.isWhitespace == true { return before + heard }
        return before + " " + heard
    }

    /// The language dictation hears: the interface's (Russian or English).
    static func locale(language: String) -> Locale {
        Locale(identifier: language == "ru" ? "ru-RU" : "en-US")
    }

    /// The dictation after `event`, with what the field holds now (`draft`), and what the controller does next.
    @discardableResult
    mutating func apply(_ event: Event, draft: String = "") -> Command? {
        switch event {
        case .press:
            if state == .listening {
                state = .ready
                return .stop
            }
            guard pressable else { return nil }
            state = .starting
            told = false
            before = draft
            heard = ""
            return .start
        case .started:
            if state == .starting { state = .listening }
        case let .refused(problem):
            guard state == .starting else { return nil }
            state = .unavailable(problem)
            told = true
            return .stop
        case let .heard(words):
            if state == .listening { heard = words }
        case let .silence(quiet):
            guard state == .listening, quiet >= (heard.isEmpty ? Self.pauseBeforeWords : Self.pauseAfterWords) else { return nil }
            state = .ready
            return .stop
        case let .ended(failed):
            guard state == .listening || state == .starting else { return nil }
            state = failed && heard.isEmpty ? .unavailable(.failed) : .ready
            told = state != .ready
            return .stop
        case .halt:
            let was = state
            if was == .listening || was == .starting { state = .ready }
            return was == .listening || was == .starting ? .stop : nil
        }
        return nil
    }
}

/// The microphone's dictation for one field (DictationFlow): the iPad's recogniser on the device only
/// (`requiresOnDeviceRecognition`), the microphone through the audio engine, the permissions asked on the first press.
/// What it hears goes to `write`, whole, each time it hears more. No sound and no text leave the iPad.
@MainActor
final class Dictation: ObservableObject {
    @Published private(set) var flow: DictationFlow
    /// The field's new text, as words are heard.
    var write: (String) -> Void = { _ in }
    private let recognizer: SFSpeechRecognizer?
    private let engine = AVAudioEngine()
    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var task: SFSpeechRecognitionTask?
    /// The audio session was turned to recording for a dictation, to be given back to playback after it.
    private var recording = false
    private var quiet: Task<Void, Never>?
    /// Each dictation's number: a late result of one stopped is not heard.
    private var round = 0
    /// Each press that starts one: a start still asking for permissions when another press came gives way to it.
    private var attempt = 0

    init(language: String) {
        let recognizer = SFSpeechRecognizer(locale: DictationFlow.locale(language: language))
        self.recognizer = recognizer
        flow = DictationFlow(problem: Self.problem(recognizer))
    }

    /// What stops dictation before anything is pressed: no recogniser on the device, or permissions refused before.
    private static func problem(_ recognizer: SFSpeechRecognizer?) -> DictationFlow.Problem? {
        guard let recognizer, recognizer.supportsOnDeviceRecognition else { return .notOnDevice }
        switch SFSpeechRecognizer.authorizationStatus() {
        case .denied: return .denied
        case .restricted: return .restricted
        default: break
        }
        return AVAudioApplication.shared.recordPermission == .denied ? .denied : nil
    }

    func press(draft: String) {
        run(flow.apply(.press, draft: draft))
    }

    /// A dictation going stops (DictationFlow.Event.halt).
    func halt() {
        run(flow.apply(.halt))
    }

    private func run(_ command: DictationFlow.Command?) {
        switch command {
        case .start:
            attempt += 1
            let this = attempt
            Task { await start(this) }
        case .stop: stop()
        case nil: break
        }
    }

    private func start(_ this: Int) async {
        // No prompts on an iPad that cannot recognise the language on the device: the line says so.
        guard let recognizer, recognizer.supportsOnDeviceRecognition else {
            run(flow.apply(.refused(.notOnDevice)))
            return
        }
        let problem = await Self.permit()
        guard this == attempt, flow.state == .starting else { return }
        if let problem {
            run(flow.apply(.refused(problem)))
            return
        }
        round += 1
        let request = SFSpeechAudioBufferRecognitionRequest()
        // On the device only: never Apple's servers (its one condition).
        request.requiresOnDeviceRecognition = true
        request.shouldReportPartialResults = true
        request.taskHint = .dictation
        request.addsPunctuation = true
        do {
            let session = AVAudioSession.sharedInstance()
            recording = true
            try session.setCategory(.playAndRecord, mode: .default, options: [.defaultToSpeaker, .mixWithOthers])
            try session.setActive(true)
            let input = engine.inputNode
            guard input.outputFormat(forBus: 0).sampleRate > 0 else { throw DictationError.noMicrophone }
            Self.tap(input, into: request)
            engine.prepare()
            try engine.start()
        } catch {
            self.request = request
            run(flow.apply(.ended(failed: true)))
            return
        }
        self.request = request
        task = Self.listen(request, recognizer, round: round, to: self)
        run(flow.apply(.started))
        listenForQuiet()
    }

    /// Asks the iPad for speech recognition and the microphone, the first time; nil once both are allowed.
    private nonisolated static func permit() async -> DictationFlow.Problem? {
        let speech = await withCheckedContinuation { (done: CheckedContinuation<SFSpeechRecognizerAuthorizationStatus, Never>) in
            SFSpeechRecognizer.requestAuthorization { done.resume(returning: $0) }
        }
        switch speech {
        case .authorized: break
        case .restricted: return .restricted
        default: return .denied
        }
        return await AVAudioApplication.requestRecordPermission() ? nil : .denied
    }

    /// The microphone's sound into the request, off the main thread as the engine gives it.
    private nonisolated static func tap(_ input: AVAudioInputNode, into request: SFSpeechAudioBufferRecognitionRequest) {
        input.removeTap(onBus: 0)
        input.installTap(onBus: 0, bufferSize: 1024, format: input.outputFormat(forBus: 0)) { buffer, _ in
            request.append(buffer)
        }
    }

    /// The recogniser's results, brought to the main thread with the round they belong to.
    private nonisolated static func listen(
        _ request: SFSpeechAudioBufferRecognitionRequest, _ recognizer: SFSpeechRecognizer, round: Int, to owner: Dictation
    ) -> SFSpeechRecognitionTask {
        recognizer.recognitionTask(with: request) { [weak owner] result, error in
            guard let owner else { return }
            let words = result?.bestTranscription.formattedString
            let final = result?.isFinal ?? false
            let failed = error != nil
            Task { @MainActor in owner.recognized(words, final: final, failed: failed, round: round) }
        }
    }

    private func recognized(_ words: String?, final: Bool, failed: Bool, round: Int) {
        guard round == self.round else { return }
        if let words, flow.listening {
            flow.apply(.heard(words))
            write(flow.text)
            listenForQuiet()
        }
        if final || failed { run(flow.apply(.ended(failed: failed))) }
    }

    /// A pause in the speech stops the dictation: the clock starts again with every word heard.
    private func listenForQuiet() {
        quiet?.cancel()
        let wait = flow.heard.isEmpty ? DictationFlow.pauseBeforeWords : DictationFlow.pauseAfterWords
        quiet = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(wait * 1_000_000_000))
            guard let self, !Task.isCancelled else { return }
            run(flow.apply(.silence(wait)))
        }
    }

    /// Lets go of the microphone and the recogniser, and gives the sound back to playback as the app has it.
    private func stop() {
        round += 1
        quiet?.cancel()
        quiet = nil
        if engine.isRunning { engine.stop() }
        if request != nil { engine.inputNode.removeTap(onBus: 0) }
        request?.endAudio()
        task?.cancel()
        request = nil
        task = nil
        if recording {
            recording = false
            try? AVAudioSession.sharedInstance().setCategory(.playback, mode: .default)
        }
    }

    private enum DictationError: Error {
        case noMicrophone
    }
}

/// The microphone inside a chat's field (Dictation), with the words for what it is doing; shown
/// on. Its line under the field says why it cannot dictate, or that it listens.
struct DictationButton: View {
    @ObservedObject var dictation: Dictation
    let text: UIText
    let draft: String

    var body: some View {
        Button {
            // Speaking, not typing: the keyboard goes, and a press on the field to type stops the dictation (AskInputRow).
            Typing.shared.hide()
            dictation.press(draft: draft)
        } label: {
            Icon(name: "mic")
        }
        .buttonStyle(DSIconItemStyle(on: dictation.flow.listening))
        .disabled(!dictation.flow.pressable)
        .accessibilityLabel(text[dictation.flow.listening ? "dictation.stop" : "dictation.start"])
        .accessibilityHint(DictationButton.line(dictation.flow, text) ?? "")
    }

    /// What the line under the field says: listening, or, once pressed, why the microphone does not dictate; nil otherwise.
    static func line(_ flow: DictationFlow, _ text: UIText) -> String? {
        if case .unavailable = flow.state, !flow.told { return nil }
        switch flow.state {
        case .ready, .starting: return nil
        case .listening: return text["dictation.listening"]
        case .unavailable(.notOnDevice): return text["dictation.notOnDevice"]
        case .unavailable(.denied): return text["dictation.denied"]
        case .unavailable(.restricted): return text["dictation.restricted"]
        case .unavailable(.failed): return text["dictation.failed"]
        }
    }
}
