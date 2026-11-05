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

/// From a recording, in Other versions (0022; the web app's RecordingVersion): its sound read on the iPad and heard by
/// Motiff Lab's scan server, up to 7 minutes, in parts of about two minutes; the notes heard become another version. It
/// needs an account and Pro, offered here, and a switch to let the sound go.
struct RecordingVersionSection: View {
    @ObservedObject var score: ScoreController
    @EnvironmentObject private var account: Account
    let text: UIText
    @State private var send = false
    @State private var choosing = false
    @State private var progress: String?
    @State private var problem: String?

    /// The recordings the iPad can read the sound of: audio, and the sound of a video.
    static let recordings: [UTType] = [.audio, .movie]

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["version.recordingHeading"]).overline()
            if account.options.map({ URL(string: $0.scan)?.host == nil }) ?? true {
                caption(text["version.recordingNoServer"])
            } else if account.player == nil {
                caption(text["transcribe.signInPrompt"])
            } else if !account.allows(.transcribe) {
                HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                    Text("Pro")
                        .font(DesignTokens.font(weight: 600, size: 12, relativeTo: .caption))
                        .foregroundStyle(Color.ds("plum"))
                        .padding(.horizontal, CGFloat.ds("s-2"))
                        .background(Color.ds("plum-tint"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
                    caption(text["transcribe.proPrompt"])
                }
            } else {
                caption(text["version.recordingLineDevice"])
                Toggle(text["transcribe.sendHosted"], isOn: $send)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink"))
                    .tint(Color.ds("accent"))
                    .disabled(progress != nil)
                Button(progress ?? text["version.addRecording"]) { choosing = true }
                    .buttonStyle(DSButtonStyle(kind: .secondary))
                    .disabled(!send || progress != nil)
                    .accessibilityHint(text["version.chooseRecording"])
            }
            if let problem {
                Text(problem)
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("danger"))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.top, CGFloat.ds("s-2"))
        .fileImporter(isPresented: $choosing, allowedContentTypes: Self.recordings) { result in
            guard case .success(let url) = result else { return }
            Task { await hear(url) }
        }
    }

    private func caption(_ words: String) -> some View {
        Text(words)
            .dsType(.caption)
            .foregroundStyle(Color.ds("ink-2"))
            .fixedSize(horizontal: false, vertical: true)
    }

    /// Reads the recording's sound, plans its parts, sends each to be heard and gives the notes to music-core.
    private func hear(_ url: URL) async {
        problem = nil
        progress = text["transcribe.decoding"]
        defer { progress = nil }
        let lent = url.startAccessingSecurityScopedResource()
        let most = 420_000 as Int64
        let sound = await Task.detached { RecordingSound.read(url, most: most) }.value
        if lent { url.stopAccessingSecurityScopedResource() }
        guard let sound, sound.milliseconds > 0 else {
            problem = text["transcribe.empty"]
            return
        }
        let parts = await RecordingParts.plan(sound.milliseconds)
        guard !parts.isEmpty else {
            problem = text["transcribe.failed"]
            return
        }
        var heard: [(index: Int, midi: Data)] = []
        var engine = "unknown engine"
        for part in parts {
            progress = parts.count > 1 ? text.format("transcribe.part", String(part.index + 1), String(parts.count)) : text["transcribe.busy"]
            let wav = await Task.detached { sound.wav(from: part.sendFrom, to: part.sendTo) }.value
            switch await Transcriber.hear(wav, account: account, text: text) {
            case .notes(let midi, let by):
                heard.append((part.index, midi))
                engine = by
            case .nothing where parts.count > 1:
                continue
            case .nothing:
                problem = text["transcribe.failed"] + " " + text["transcribe.notHeardTip"]
                return
            case .failed(let why):
                problem = why
                return
            }
        }
        guard !heard.isEmpty else {
            problem = text["transcribe.failed"] + " " + text["transcribe.notHeardTip"]
            return
        }
        let name = url.deletingPathExtension().lastPathComponent
        problem = await score.addRecordingVersion(name: name, engine: engine, milliseconds: sound.milliseconds, parts: heard)
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
