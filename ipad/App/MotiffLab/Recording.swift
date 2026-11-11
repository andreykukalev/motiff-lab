import AVFoundation
import SwiftUI
import UniformTypeIdentifiers

/// A recording's sound read on the iPad (decision records 0014 and 0022): mono 16-bit at 22,050 Hz, as the web app takes it
/// out (Scripts/import/recording.ts), so only the sound, never the file, goes to the scan server, in the parts music-core
/// plans (`recording.parts`).
struct RecordingSound: Sendable {
    static let sampleRate = 22_050.0

    let samples: [Int16]

    var milliseconds: Int64 { Int64((Double(samples.count) * 1000 / Self.sampleRate).rounded()) }

    /// The sound of an audio or video file, its first `most` milliseconds; nil when it has none the iPad can read.
    static func read(_ url: URL, most: Int64) -> RecordingSound? {
        guard let file = try? AVAudioFile(forReading: url),
              let target = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: sampleRate, channels: 1, interleaved: true),
              let converter = AVAudioConverter(from: file.processingFormat, to: target)
        else { return nil }
        converter.downmix = true
        let chunk: AVAudioFrameCount = 16_384
        guard let input = AVAudioPCMBuffer(pcmFormat: file.processingFormat, frameCapacity: chunk) else { return nil }
        let wanted = Int(Double(most) / 1000 * sampleRate)
        var samples: [Int16] = []
        samples.reserveCapacity(min(wanted, Int(Double(file.length) * sampleRate / max(file.processingFormat.sampleRate, 1)) + Int(chunk)))
        var ended = false
        while samples.count < wanted {
            guard let output = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: chunk) else { return nil }
            var failure: NSError?
            let status = converter.convert(to: output, error: &failure) { _, state in
                if ended {
                    state.pointee = .endOfStream
                    return nil
                }
                do {
                    try file.read(into: input, frameCount: chunk)
                } catch {
                    ended = true
                }
                if ended || input.frameLength == 0 {
                    ended = true
                    state.pointee = .endOfStream
                    return nil
                }
                state.pointee = .haveData
                return input
            }
            if status == .error || failure != nil { return samples.isEmpty ? nil : RecordingSound(samples: Array(samples.prefix(wanted))) }
            if let channel = output.int16ChannelData?[0], output.frameLength > 0 {
                samples.append(contentsOf: UnsafeBufferPointer(start: channel, count: Int(output.frameLength)))
            }
            if status == .endOfStream || output.frameLength == 0 { break }
        }
        return samples.isEmpty ? nil : RecordingSound(samples: Array(samples.prefix(wanted)))
    }

    /// The sound from one moment to another as a WAV file, as a part is sent.
    func wav(from start: Int64, to end: Int64) -> Data {
        let first = max(0, min(samples.count, Int(Double(start) / 1000 * Self.sampleRate)))
        let last = max(first, min(samples.count, Int(Double(end) / 1000 * Self.sampleRate)))
        return Self.wav(samples[first..<last])
    }

    /// A 16-bit mono PCM WAV file of the samples.
    static func wav(_ samples: ArraySlice<Int16>) -> Data {
        let bytes = samples.count * 2
        var data = Data(capacity: 44 + bytes)
        func put(_ text: String) { data.append(contentsOf: Array(text.utf8)) }
        func put32(_ value: UInt32) { withUnsafeBytes(of: value.littleEndian) { data.append(contentsOf: $0) } }
        func put16(_ value: UInt16) { withUnsafeBytes(of: value.littleEndian) { data.append(contentsOf: $0) } }
        let rate = UInt32(sampleRate)
        put("RIFF")
        put32(UInt32(36 + bytes))
        put("WAVE")
        put("fmt ")
        put32(16)
        put16(1)
        put16(1)
        put32(rate)
        put32(rate * 2)
        put16(2)
        put16(16)
        put("data")
        put32(UInt32(bytes))
        for sample in samples {
            withUnsafeBytes(of: sample.littleEndian) { data.append(contentsOf: $0) }
        }
        return data
    }
}

/// Motiff Lab's scan server hearing a recording (`POST /transcribe`, 0014): the sound as a WAV file, the player's Scan
/// token and a tester's plan choice; the MIDI it heard and its engine, or why there is none, as the web app's ScanService.
enum Transcriber {
    enum Heard: Equatable {
        case notes(midi: Data, engine: String)
        /// The part had no notes to hear: a quiet stretch of a longer recording.
        case nothing
        case failed(String)
    }

    @MainActor
    static func hear(_ wav: Data, account: Account, text: UIText, session: URLSession = .shared) async -> Heard {
        guard let options = account.options, let base = URL(string: options.scan.hasSuffix("/") ? options.scan : options.scan + "/"),
              base.host != nil, let url = URL(string: "transcribe", relativeTo: base)
        else { return .failed(text["version.recordingNoServer"]) }
        guard let token = await account.token(options.scanScope) else { return .failed(text["transcribe.signIn"]) }
        let boundary = "motiff-\(UUID().uuidString)"
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 600
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        account.addPlanChoice(to: &request)
        var body = Data()
        body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"file\"; filename=\"recording.wav\"\r\nContent-Type: application/octet-stream\r\n\r\n".utf8))
        body.append(wav)
        body.append(Data("\r\n--\(boundary)--\r\n".utf8))
        do {
            let (data, response) = try await session.upload(for: request, from: body)
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            switch status {
            case 200..<300:
                let engine = (response as? HTTPURLResponse)?.value(forHTTPHeaderField: "X-Engine") ?? "unknown engine"
                return .notes(midi: data, engine: engine)
            case 403:
                return .failed(text["transcribe.needsPro"])
            case 404, 405:
                return .failed(text["transcribe.oldServer"])
            case 422:
                return .nothing
            default:
                let message = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["message"] as? String
                return .failed(message.flatMap { $0.isEmpty ? nil : $0 } ?? text["transcribe.failed"])
            }
        } catch let error as URLError where error.code == .timedOut {
            return .failed(text["transcribe.tooLong"])
        } catch {
            return .failed(text["transcribe.failed"])
        }
    }
}

/// A recording heard by Motiff Lab's scan server, part by part: what music-core needs to make notes of it, as a new piece
/// (`open` with "transcription") or another version (`performance.add` with "parts").
struct HeardRecording: Sendable {
    let name: String
    let engine: String
    let milliseconds: Int64
    let parts: [(index: Int, midi: Data)]

    /// The longest stretch heard, from the start of the recording: 7 minutes.
    static let most: Int64 = 420_000

    /// The recording was longer than the 7 minutes heard.
    var cut: Bool { milliseconds >= Self.most }

    /// {"engine", "milliseconds", "parts": [{index, midi}]}, as the facade takes it.
    var request: [String: Any] {
        [
            "engine": engine,
            "milliseconds": milliseconds,
            "parts": parts.map { ["index": $0.index, "midi": $0.midi.base64EncodedString()] as [String: Any] },
        ]
    }
}

/// Hearing a recording (0014): its sound read on the iPad, the parts music-core plans sent one after another to the scan
/// server, and the notes heard in each kept for music-core to join.
@MainActor
enum RecordingHearing {
    /// The recordings the iPad can read the sound of: audio, and the sound of a video.
    static let recordings: [UTType] = [.audio, .movie]

    /// The notes heard in the recording, or why there are none. `progress` tells what is happening.
    static func hear(_ url: URL, account: Account, text: UIText, progress: (String) -> Void) async -> Result<HeardRecording, RecordingProblem> {
        progress(text["transcribe.decoding"])
        let lent = url.startAccessingSecurityScopedResource()
        let most = HeardRecording.most
        let sound = await Task.detached { RecordingSound.read(url, most: most) }.value
        if lent { url.stopAccessingSecurityScopedResource() }
        guard let sound, sound.milliseconds > 0 else { return .failure(RecordingProblem(text["transcribe.empty"])) }
        let parts = await RecordingParts.plan(sound.milliseconds)
        guard !parts.isEmpty else { return .failure(RecordingProblem(text["transcribe.failed"])) }
        var heard: [(index: Int, midi: Data)] = []
        var engine = "unknown engine"
        for part in parts {
            progress(parts.count > 1 ? text.format("transcribe.part", String(part.index + 1), String(parts.count)) : text["transcribe.busy"])
            let wav = await Task.detached { sound.wav(from: part.sendFrom, to: part.sendTo) }.value
            switch await Transcriber.hear(wav, account: account, text: text) {
            case .notes(let midi, let by):
                heard.append((part.index, midi))
                engine = by
            case .nothing where parts.count > 1:
                continue
            case .nothing:
                return .failure(RecordingProblem(text["transcribe.failed"] + " " + text["transcribe.notHeardTip"]))
            case .failed(let why):
                return .failure(RecordingProblem(why))
            }
        }
        guard !heard.isEmpty else { return .failure(RecordingProblem(text["transcribe.failed"] + " " + text["transcribe.notHeardTip"])) }
        let name = url.deletingPathExtension().lastPathComponent
        return .success(HeardRecording(name: name, engine: engine, milliseconds: sound.milliseconds, parts: heard))
    }
}

struct RecordingProblem: Error, Sendable {
    let message: String
    init(_ message: String) { self.message = message }
}

/// Choosing a recording to be heard: what it needs first (Motiff Lab's scan server, an account, Pro, offered here),
/// the switch that lets the sound go, and the button that picks the recording. Used to add a piece from a recording
/// (the library's Add piece) and another version from one (Other versions, 0022).
struct RecordingChooser: View {
    @EnvironmentObject private var account: Account
    let text: UIText
    /// What the scan server does with the recording, said before the switch.
    let line: String
    let button: String
    let hint: String
    let progress: String?
    /// Why the last recording was not heard, under the button.
    var problem: String?
    let picked: (URL) -> Void
    @State private var send = false
    @State private var choosing = false

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            if account.options.map({ URL(string: $0.scan)?.host == nil }) ?? true {
                caption(text["version.recordingNoServer"])
            } else if account.player == nil {
                caption(text["transcribe.signInPrompt"])
            } else if !account.allows(.transcribe) {
                HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                    ProTag()
                    caption(text["transcribe.proPrompt"])
                }
            } else {
                caption(line)
                Toggle(text["transcribe.sendHosted"], isOn: $send)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink"))
                    .tint(Color.ds("accent"))
                    .disabled(progress != nil)
                Button(progress ?? button) { choosing = true }
                    .buttonStyle(DSButtonStyle(kind: .secondary))
                    .disabled(!send || progress != nil)
                    .accessibilityHint(hint)
            }
            if let problem {
                Text(problem)
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("danger"))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .fileImporter(isPresented: $choosing, allowedContentTypes: RecordingHearing.recordings) { result in
            guard case .success(let url) = result else { return }
            picked(url)
        }
    }

    private func caption(_ words: String) -> some View {
        Text(words)
            .dsType(.caption)
            .foregroundStyle(Color.ds("ink-2"))
            .fixedSize(horizontal: false, vertical: true)
    }
}

/// Pro, in plum (0018): what Motiff Lab's server or AI does.
struct ProTag: View {
    var body: some View {
        Text("Pro")
            .font(DesignTokens.font(weight: 600, size: 12, relativeTo: .caption))
            .foregroundStyle(Color.ds("plum"))
            .padding(.horizontal, CGFloat.ds("s-2"))
            .background(Color.ds("plum-tint"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
    }
}

/// From a recording, in Other versions (0022; the web app's RecordingVersion): its sound read on the iPad and heard by
/// Motiff Lab's scan server, up to 7 minutes, in parts of about two minutes; the notes heard become another version. It
/// needs an account and Pro, offered here, and a switch to let the sound go.
struct RecordingVersionSection: View {
    @ObservedObject var score: ScoreController
    @EnvironmentObject private var account: Account
    let text: UIText
    @State private var progress: String?
    @State private var problem: String?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["version.recordingHeading"]).overline()
            RecordingChooser(
                text: text,
                line: text["version.recordingLineDevice"],
                button: text["version.addRecording"],
                hint: text["version.chooseRecording"],
                progress: progress,
                problem: problem
            ) { url in
                Task { await hear(url) }
            }
        }
        .padding(.top, CGFloat.ds("s-2"))
    }

    /// Hears the recording and gives the notes to music-core as another version.
    private func hear(_ url: URL) async {
        problem = nil
        defer { progress = nil }
        switch await RecordingHearing.hear(url, account: account, text: text, progress: { progress = $0 }) {
        case .success(let heard):
            problem = await score.addRecordingVersion(heard)
        case .failure(let failure):
            problem = failure.message
        }
    }
}

/// The parts music-core plans for a recording (`recording.parts`): what of the sound each sends, in milliseconds.
enum RecordingParts {
    struct Part: Sendable {
        var index: Int
        var sendFrom: Int64
        var sendTo: Int64
    }

    static func plan(_ milliseconds: Int64) async -> [Part] {
        let request = "{\"milliseconds\": \(milliseconds)}"
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "recording.parts", request) }
        return (facadeObject(answer)?["parts"] as? [[String: Any]] ?? []).compactMap { part in
            guard let index = (part["index"] as? NSNumber)?.intValue,
                  let from = (part["sendFrom"] as? NSNumber)?.int64Value,
                  let to = (part["sendTo"] as? NSNumber)?.int64Value else { return nil }
            return Part(index: index, sendFrom: from, sendTo: to)
        }
    }
}
